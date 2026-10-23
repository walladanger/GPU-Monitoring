using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace GpuMonitor;

public sealed record RuntimeMetricMap(string Name, string Key, string Title, string Unit, double Multiplier = 1, bool Counter = false);

/// <summary>Optional, bounded, read-only scrape. No inference requests or prompt capture.</summary>
public sealed class RuntimeMetricsCollector : ITelemetryProvider, IDisposable, ITelemetryHistoryPolicy
{
    public static Uri DefaultSglangEndpoint { get; } = new("http://127.0.0.1:30000/metrics");
    public static Uri DefaultVllmEndpoint { get; } = new("http://127.0.0.1:8000/metrics");
    public static RuntimeMetricsCollector CreateSglang(string id = "sglang-local", Uri? endpoint = null) =>
        new(id, endpoint ?? DefaultSglangEndpoint, StandardMaps("sglang"));
    public static RuntimeMetricsCollector CreateVllm(string id = "vllm-local", Uri? endpoint = null) =>
        new(id, endpoint ?? DefaultVllmEndpoint, StandardMaps("vllm"));
    public string SourceId { get; }
    public int HistoryCapacity => 900;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(30);
    public IReadOnlyList<MetricDefinition> Definitions { get; }
    private readonly Uri endpoint;
    private readonly HttpClient client;
    private readonly IReadOnlyList<RuntimeMetricMap> maps;
    private readonly IReadOnlyList<MetricDefinition> derivedMeans;
    private readonly Dictionary<string, CumulativeRate> rates = [];

    public RuntimeMetricsCollector(string id, Uri endpoint, IReadOnlyList<RuntimeMetricMap> maps, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Runtime source ID must be 1–64 characters.");
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0)
            throw new ArgumentException("Metrics endpoint must be HTTP(S), without embedded credentials or fragment.");
        if (maps.Count is < 1 or > 64 || maps.Select(m => m.Name).Distinct().Count() != maps.Count ||
            maps.Any(m => string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.Key) || string.IsNullOrWhiteSpace(m.Title) || string.IsNullOrWhiteSpace(m.Unit) || !double.IsFinite(m.Multiplier) || m.Multiplier <= 0) ||
            maps.GroupBy(m => m.Key).Any(g => g.Select(m => (m.Unit, m.Counter)).Distinct().Count() > 1) ||
            maps.Any(m => m.Counter && maps.Any(other => other.Key == m.Key + ".rate")))
            throw new ArgumentException("Invalid runtime metric mapping.");
        SourceId = "runtime:" + id; this.endpoint = endpoint; this.maps = maps.ToArray();
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        derivedMeans = new[] { "ttft", "request", "decode", "inter_token" }.Where(k => maps.Any(m => m.Key == "latency." + k + ".sum") && maps.Any(m => m.Key == "latency." + k + ".count") && !maps.Any(m => m.Key == "latency." + k + ".mean"))
            .Select(k => new MetricDefinition(SourceId, "latency." + k + ".mean", "Interval mean " + k + " latency", "s")).ToArray();
        Definitions = Array.AsReadOnly(maps.Select(m => new MetricDefinition(SourceId, m.Key, m.Title, m.Unit))
            .Concat(maps.Where(m => m.Counter).Select(m => new MetricDefinition(SourceId, m.Key + ".rate", m.Title + " rate", m.Unit + "/s")))
            .Concat(derivedMeans)
            .DistinctBy(d => d.Key).ToArray());
    }

    public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new IOException("Metrics endpoint returned HTTP " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new IOException("Metrics response exceeds limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (memory.Length + count > 4 * 1024 * 1024) throw new IOException("Metrics response exceeds limit.");
                memory.Write(buffer, 0, count);
            }
            return Normalize(Encoding.UTF8.GetString(memory.ToArray()), DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
        }
        catch { rates.Clear(); throw; } // Recovery begins with a gap, not an average over an outage.
    }

    internal TelemetryBatch Normalize(string text, DateTimeOffset now, long monotonicTick)
    {
        var points = PrometheusText.Parse(text).Where(p => maps.Any(m => m.Name == p.Name)).ToArray();
        var groups = points.GroupBy(p => PrometheusText.Canonical(p.Labels)).ToArray();
        if (groups.Length > 32) throw new FormatException("Runtime series cardinality exceeds 32 label sets.");
        if (points.Length == 0) throw new FormatException("Endpoint contains no configured runtime metrics.");
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>(); var active = new HashSet<string>();
        foreach (var group in groups)
        {
            var labels = group.First().Labels;
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)));
            string name = labels.TryGetValue("model_name", out var model) ? model : "Runtime";
            if (name.Length > 256) name = name[..256];
            var device = new DeviceIdentity(SourceId, id, DeviceKind.Runtime, name, devices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            devices.Add(device);
            var groupValues = new Dictionary<string, double?>();
            foreach (var definition in maps.DistinctBy(m => m.Key))
            {
                // Ordered aliases support current and earlier cache metric names without double counting.
                var map = maps.FirstOrDefault(m => m.Key == definition.Key && group.Any(p => p.Name == m.Name)) ?? definition;
                var point = group.FirstOrDefault(p => p.Name == map.Name);
                double? value = point?.Value * map.Multiplier;
                if (value < 0 || (map.Unit == "%" && value > 100) || (value.HasValue && !double.IsFinite(value.Value))) value = null;
                var timestamp = point?.TimestampMilliseconds is { } milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : now;
                samples.Add(new(device, map.Key, timestamp, value));
                if (map.Counter)
                {
                    bool sourceTime = point?.TimestampMilliseconds != null;
                    string rateKey = id + "/" + map.Key + (sourceTime ? "/source" : "/observed");
                    active.Add(rateKey);
                    if (!rates.TryGetValue(rateKey, out var rate)) rates[rateKey] = rate = new(sourceTime ? TimeSpan.TicksPerSecond : Stopwatch.Frequency);
                    var measuredRate = rate.Observe(value, sourceTime ? timestamp.UtcTicks : monotonicTick);
                    groupValues[map.Key + ".rate"] = measuredRate;
                    samples.Add(new(device, map.Key + ".rate", timestamp, measuredRate));
                }
            }
            foreach (var definition in derivedMeans)
            {
                string prefix = definition.Key[..^5];
                groupValues.TryGetValue(prefix + ".sum.rate", out var sum);
                groupValues.TryGetValue(prefix + ".count.rate", out var count);
                samples.Add(new(device, definition.Key, now, count > 0 ? sum / count : null));
            }
        }
        foreach (var key in rates.Keys.Except(active).ToArray()) rates.Remove(key);
        return WindowsSystemCollector.Batch(devices.AsReadOnly(), samples);
    }

    public static IReadOnlyList<RuntimeMetricMap> StandardMaps(string runtime)
    {
        if (runtime is not ("sglang" or "vllm")) throw new ArgumentException("Unknown runtime kind.");
        string prefix = runtime + ":";
        var result = new List<RuntimeMetricMap> {
            new(prefix + "prompt_tokens_total", "tokens.prompt", "Prompt tokens", "tokens", Counter: true),
            new(prefix + "generation_tokens_total", "tokens.output", "Output tokens", "tokens", Counter: true),
            new(prefix + (runtime == "sglang" ? "num_running_reqs" : "num_requests_running"), "requests.running", "Running requests", "count"),
            new(prefix + (runtime == "sglang" ? "num_queue_reqs" : "num_requests_waiting"), "requests.waiting", "Waiting requests", "count"),
            new(prefix + (runtime == "sglang" ? "token_usage" : "kv_cache_usage_perc"), "cache.usage", "KV cache usage", "%", 100),
            new(prefix + (runtime == "sglang" ? "cache_hit_rate" : "gpu_cache_usage_perc"), runtime == "sglang" ? "cache.hit" : "cache.usage", runtime == "sglang" ? "Cache hit rate" : "KV cache usage", "%", 100),
            new(prefix + "time_to_first_token_seconds_sum", "latency.ttft.sum", "TTFT cumulative duration", "s", Counter: true),
            new(prefix + "time_to_first_token_seconds_count", "latency.ttft.count", "TTFT observations", "count", Counter: true),
            new(prefix + "e2e_request_latency_seconds_sum", "latency.request.sum", "Request cumulative duration", "s", Counter: true),
            new(prefix + "e2e_request_latency_seconds_count", "latency.request.count", "Request observations", "count", Counter: true),
            new(prefix + "request_time_per_output_token_seconds_sum", "latency.decode.sum", "Decode cumulative duration", "s", Counter: true),
            new(prefix + "request_time_per_output_token_seconds_count", "latency.decode.count", "Decode observations", "count", Counter: true),
            new(prefix + "inter_token_latency_seconds_sum", "latency.inter_token.sum", "Inter-token cumulative duration", "s", Counter: true),
            new(prefix + "inter_token_latency_seconds_count", "latency.inter_token.count", "Inter-token observations", "count", Counter: true)
        };
        if (runtime == "vllm") result.AddRange([
            new(prefix + "time_per_output_token_seconds_sum", "latency.inter_token.sum", "Inter-token cumulative duration", "s", Counter: true),
            new(prefix + "time_per_output_token_seconds_count", "latency.inter_token.count", "Inter-token observations", "count", Counter: true)
        ]);
        return result.AsReadOnly();
    }
    public void Dispose() => client.Dispose();
}
