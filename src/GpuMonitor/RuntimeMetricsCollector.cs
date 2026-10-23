using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GpuMonitor;

public enum RuntimeMetricsKind { LlamaCpp, Vllm }

/// <summary>Explicit local Prometheus endpoint; never sends inference requests or stores labels.</summary>
public sealed class RuntimeMetricsCollector : ITelemetryProvider, IDisposable
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Uri endpoint;
    private readonly RuntimeMetricsReader reader;
    public string SourceId { get; }
    public IReadOnlyList<MetricDefinition> Definitions => reader.Definitions;

    public RuntimeMetricsCollector(RuntimeMetricsKind kind, Uri endpoint) : this(kind, endpoint, null) { }

    internal RuntimeMetricsCollector(RuntimeMetricsKind kind, Uri endpoint, HttpClient? client)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https") ||
            !(endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              (IPAddress.TryParse(endpoint.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip))) ||
            endpoint.AbsolutePath != "/metrics" || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Use an explicit loopback HTTP(S) /metrics URL without credentials, query or fragment.", nameof(endpoint));
        this.endpoint = endpoint;
        SourceId = $"llm:{kind}:{endpoint.AbsoluteUri}";
        reader = new(kind, SourceId);
        ownsClient = client == null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }

    public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("text/plain; version=0.0.4");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new IOException($"Runtime metrics returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaximumBytes) throw new IOException("Runtime metrics response exceeds 1 MiB.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (bytes.Length + count > MaximumBytes) throw new IOException("Runtime metrics response exceeds 1 MiB.");
                bytes.Write(buffer, 0, count);
            }
            return reader.Observe(Encoding.UTF8.GetString(bytes.ToArray()), DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
        }
        catch
        {
            // Do not average a recovered sample across a failed scrape interval.
            reader.Reset();
            throw;
        }
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }
}

/// <summary>Fixed allowlist and endpoint-wide additive totals; labels are used only for counter continuity.</summary>
internal sealed class RuntimeMetricsReader
{
    private readonly string source;
    private readonly string name;
    private readonly string[] families;
    private Dictionary<string, Dictionary<string, double?>>? previous;
    private long previousTick;
    public IReadOnlyList<MetricDefinition> Definitions { get; }
    private static readonly Regex Line = new(
        "^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?:\\{(?<labels>(?:[^\"{}]|\"(?:[^\"\\\\]|\\\\.)*\")*)\\})?[ \\t]+(?<value>[^ \\t]+)(?:[ \\t]+[+-]?[0-9]+)?[ \\t]*$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Label = new(
        "\\G[ \\t]*(?<key>[a-zA-Z_][a-zA-Z0-9_]*)[ \\t]*=[ \\t]*\"(?<value>(?:[^\"\\\\]|\\\\[\\\\\"n])*)\"[ \\t]*(?:,|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public RuntimeMetricsReader(RuntimeMetricsKind kind, string source)
    {
        this.source = source;
        name = kind switch { RuntimeMetricsKind.LlamaCpp => "llama.cpp", RuntimeMetricsKind.Vllm => "vLLM", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        families = kind == RuntimeMetricsKind.LlamaCpp
            ? ["llamacpp:tokens_predicted_total", "llamacpp:prompt_tokens_total", "llamacpp:requests_processing", "llamacpp:requests_deferred"]
            : ["vllm:generation_tokens_total", "vllm:prompt_tokens_total", "vllm:num_requests_running", "vllm:num_requests_waiting"];
        Definitions = Array.AsReadOnly(new MetricDefinition[] {
            new(source, "llm.generation.total", "Generated tokens (runtime total)", "tokens"),
            new(source, "llm.generation.rate", "Generation rate (scrape interval)", "tokens/s"),
            new(source, "llm.prompt.total", "Prompt tokens (runtime total)", "tokens"),
            new(source, "llm.prompt.rate", "Prompt rate (scrape interval)", "tokens/s"),
            new(source, "llm.requests.running", "Running requests", "requests"),
            new(source, "llm.requests.waiting", "Waiting requests", "requests") });
    }

    public void Reset() { previous = null; }

    public TelemetryBatch Observe(string text, DateTimeOffset time, long tick)
    {
        var current = new Dictionary<string, Dictionary<string, double?>>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // Ignore all non-allowlisted families, including arbitrary metadata and histograms.
            int end = line.IndexOfAny(['{', ' ', '\t']);
            if (end < 0 && families.Contains(line, StringComparer.Ordinal))
                throw new FormatException("Missing supported runtime metric value.");
            if (end < 0 || !families.Contains(line[..end], StringComparer.Ordinal)) continue;
            var match = Line.Match(line);
            if (!match.Success) throw new FormatException("Malformed supported runtime metric.");
            string family = match.Groups["name"].Value;
            string labels = CanonicalLabels(match.Groups["labels"].Value);
            double? value = double.TryParse(match.Groups["value"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed >= 0 ? parsed : null;
            if (!current.TryGetValue(family, out var series)) current[family] = series = new(StringComparer.Ordinal);
            if (!series.TryAdd(labels, value)) throw new FormatException("Duplicate runtime metric series.");
        }
        if (current.Count == 0) throw new FormatException("No supported runtime metrics. Enable the runtime metrics endpoint.");
        double? Total(int i)
        {
            if (!current.TryGetValue(families[i], out var values) || values.Values.Any(v => v == null)) return null;
            double total = values.Values.Sum(v => v!.Value);
            return double.IsFinite(total) ? total : null;
        }
        double? Rate(int i)
        {
            string family = families[i];
            if (previous == null || tick <= previousTick || !current.TryGetValue(family, out var values) ||
                !previous.TryGetValue(family, out var old) || values.Count != old.Count) return null;
            double delta = 0;
            foreach (var pair in values)
            {
                if (pair.Value == null || !old.TryGetValue(pair.Key, out var value) || value == null || pair.Value < value) return null;
                delta += pair.Value.Value - value.Value;
            }
            double rate = delta / ((tick - (double)previousTick) / Stopwatch.Frequency);
            return double.IsFinite(rate) ? rate : null;
        }
        double?[] numbers = [Total(0), Rate(0), Total(1), Rate(1), Total(2), Total(3)];
        previous = current; previousTick = tick;
        var device = new DeviceIdentity(source, "runtime-total", DeviceKind.Runtime, name + " endpoint total", "0");
        var samples = Definitions.Select((definition, i) => new TelemetrySample(device, definition.Key, time, numbers[i])).ToList();
        return WindowsSystemCollector.Batch([device], samples);
    }

    private static string CanonicalLabels(string text)
    {
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int position = 0;
        while (position < text.Length)
        {
            if (string.IsNullOrWhiteSpace(text[position..])) break;
            var match = Label.Match(text, position);
            if (!match.Success) throw new FormatException("Malformed runtime metric labels.");
            if (!labels.TryAdd(match.Groups["key"].Value, match.Groups["value"].Value))
                throw new FormatException("Duplicate runtime metric label.");
            position += match.Length;
        }
        return JsonSerializer.Serialize(labels);
    }
}
