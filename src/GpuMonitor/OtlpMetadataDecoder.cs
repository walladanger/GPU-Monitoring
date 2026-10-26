using System.Globalization;
using System.Text.Json;

namespace GpuMonitor;

/// <summary>OTLP JSON file-exporter metrics and spans. Only GenAI numerical fields and trace IDs survive decoding.</summary>
public sealed class OtlpMetadataDecoder(string sourceId) : IMetadataDecoder
{
    public string SourceId => sourceId;
    private readonly Dictionary<string, CumulativeRate> rates = [];
    public IReadOnlyList<MetricDefinition> Definitions { get; } = System.Array.AsReadOnly(
        new[] { "input", "output" }.SelectMany(k => new[] {
            new MetricDefinition(sourceId, "tokens." + k + ".cumulative", "Cumulative " + k + " tokens", "tokens"),
            new MetricDefinition(sourceId, "tokens." + k + ".delta", "Interval " + k + " tokens", "tokens"),
            new MetricDefinition(sourceId, "tokens." + k + ".rate", k + " token throughput", "tokens/s") })
        .Concat(new[] { "input", "output", "cache_read", "cache_write" }.Select(k =>
            new MetricDefinition(sourceId, "request.tokens." + k, "Request " + k + " tokens", "tokens")))
        .Append(new(sourceId, "latency.operation.mean", "Interval mean operation latency", "s")).ToArray());
    public IMetadataDecoder Fresh() => new OtlpMetadataDecoder(sourceId);
    public IMetadataDecoder Clone()
    {
        var copy = new OtlpMetadataDecoder(sourceId);
        foreach (var pair in rates) copy.rates.Add(pair.Key, pair.Value.Clone());
        return copy;
    }

    public TelemetryBatch Decode(JsonElement root, DateTimeOffset observedAt)
    {
        var devices = new Dictionary<string, DeviceIdentity>();
        var samples = new Dictionary<(string Device, string Metric), TelemetrySample>();
        var events = new List<TelemetryEvent>();
        void Add(TelemetrySample sample)
        {
            var key = (sample.Device.Id, sample.MetricKey);
            if (!samples.TryGetValue(key, out var old) || sample.Time > old.Time) samples[key] = sample;
            else if (sample.Time == old.Time && sample.Value != old.Value) throw new FormatException("Ambiguous OTLP series at the same timestamp.");
        }
        foreach (var resource in Array(root, "resourceMetrics"))
        {
            var resourceAttributes = resource.TryGetProperty("resource", out var r) ? Attributes(r) : new Dictionary<string, string>();
            foreach (var scope in Array(resource, "scopeMetrics"))
                foreach (var metric in Array(scope, "metrics"))
                {
                    string? name = MetadataFields.Text(metric, "name");
                    if (name is not ("gen_ai.client.token.usage" or "gen_ai.client.operation.duration")) continue;
                    string? unit = MetadataFields.Text(metric, "unit");
                    if (name == "gen_ai.client.token.usage" && unit is not ("{token}" or "tokens" or "1")) throw new FormatException("Unsupported OTLP token unit.");
                    if (name == "gen_ai.client.operation.duration" && unit != "s") throw new FormatException("OTLP duration must be seconds.");
                    bool histogram = metric.TryGetProperty("histogram", out var data);
                    if (!histogram && !metric.TryGetProperty("sum", out data)) continue;
                    if (!histogram && (!data.TryGetProperty("isMonotonic", out var monotonic) || !monotonic.GetBoolean()))
                        throw new FormatException("OTLP token sums must be monotonic counters.");
                    int temporality = Temporality(data);
                    foreach (var point in Array(data, "dataPoints"))
                    {
                        var attributes = Attributes(point);
                        var device = Device(resourceAttributes, attributes, ScopeIdentity(scope)); devices[device.Id] = device;
                        var time = NanoTime(point, "timeUnixNano");
                        var start = point.TryGetProperty("startTimeUnixNano", out var startValue) && startValue.ToString() != "0" ? NanoTime(point, "startTimeUnixNano") : (DateTimeOffset?)null;
                        string epoch = start?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "unknown";
                        double? sum = histogram ? MetadataFields.Number(point, "sum") : MetadataFields.Number(point, "asInt") ?? MetadataFields.Number(point, "asDouble");
                        if (point.TryGetProperty("flags", out var flags) && (flags.GetUInt32() & 1) != 0) sum = null;
                        double? Rate(string key, double? value)
                        {
                            if (temporality == 1) return start.HasValue && time > start ? value / (time - start.Value).TotalSeconds : null;
                            string rateKey = device.Id + "/" + key;
                            if (!rates.TryGetValue(rateKey, out var rate))
                            {
                                if (rates.Count >= 1024) throw new FormatException("OTLP rate cardinality limit exceeded.");
                                rates[rateKey] = rate = new(TimeSpan.TicksPerSecond);
                            }
                            return rate.Observe(value, time.UtcTicks, epoch);
                        }
                        if (name == "gen_ai.client.token.usage")
                        {
                            if (!attributes.TryGetValue("gen_ai.token.type", out var tokenType) || tokenType is not ("input" or "output")) continue;
                            string key = "tokens." + tokenType;
                            Add(new(device, key + (temporality == 1 ? ".delta" : ".cumulative"), time, sum));
                            var rateValue = Rate(key, sum);
                            if (rateValue.HasValue && !double.IsFinite(rateValue.Value)) rateValue = null;
                            Add(new(device, key + ".rate", time, rateValue));
                        }
                        else
                        {
                            double? count = MetadataFields.Number(point, "count");
                            double? sumRate = Rate("latency.sum", sum), countRate = Rate("latency.count", count);
                            Add(new(device, "latency.operation.mean", time, countRate > 0 ? sumRate / countRate : null));
                        }
                        if (devices.Count > 64 || samples.Count > 2048) throw new FormatException("OTLP cardinality limit exceeded.");
                    }
                }
        }
        foreach (var resource in Array(root, "resourceSpans"))
        {
            var resourceAttributes = resource.TryGetProperty("resource", out var r) ? Attributes(r) : new Dictionary<string, string>();
            foreach (var scope in Array(resource, "scopeSpans"))
                foreach (var span in Array(scope, "spans"))
                {
                    if (events.Count >= 4096) throw new FormatException("OTLP span batch limit exceeded.");
                    var attributes = Attributes(span);
                    var device = Device(resourceAttributes, attributes, ScopeIdentity(scope)); devices[device.Id] = device;
                    string trace = HexId(span, "traceId", 32), id = HexId(span, "spanId", 16);
                    string? parent = MetadataFields.Text(span, "parentSpanId");
                    if (!string.IsNullOrEmpty(parent)) parent = HexId(span, "parentSpanId", 16);
                    var start = NanoTime(span, "startTimeUnixNano"); var end = NanoTime(span, "endTimeUnixNano");
                    if (end < start) throw new FormatException("OTLP span ends before it starts.");
                    var measurements = new List<EventMeasurement>();
                    foreach (var pair in new[] { ("gen_ai.usage.input_tokens", "input"), ("gen_ai.usage.output_tokens", "output"),
                        ("gen_ai.usage.cache_read.input_tokens", "cache_read"), ("gen_ai.usage.cache_creation.input_tokens", "cache_write") })
                    {
                        if (attributes.TryGetValue(pair.Item1, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0)
                        {
                            Add(new(device, "request.tokens." + pair.Item2, end, value));
                            measurements.Add(new("request.tokens." + pair.Item2, "tokens", value));
                        }
                    }
                    string operation = attributes.TryGetValue("gen_ai.operation.name", out var op) && op is "chat" or "execute_tool" or "invoke_agent" or "embeddings" or "generate_content" ? op : "span";
                    events.Add(new(SourceId, trace + "/" + id, end, "otel." + operation, device.Id, trace, end - start,
                        ParentId: parent, Measurements: measurements.AsReadOnly(), ActivityId: id));
                    if (devices.Count > 64) throw new FormatException("OTLP resource limit exceeded.");
                }
        }
        if (!root.TryGetProperty("resourceMetrics", out _) && !root.TryGetProperty("resourceSpans", out _))
            throw new FormatException("Expected an OTLP JSON metrics or traces envelope.");
        var batch = WindowsSystemCollector.Batch(System.Array.AsReadOnly(devices.Values.ToArray()), samples.Values.ToList());
        return batch with { Events = events.AsReadOnly() };
    }

    private DeviceIdentity Device(IReadOnlyDictionary<string, string> resource, IReadOnlyDictionary<string, string> attributes, string scope)
    {
        // Content attributes are never copied to the normalized model, including span names.
        string identity = scope + "\0" + string.Concat(new[] { "service.name", "service.instance.id", "host.id", "host.name", "process.pid" }.Select(k => k + "=" + resource.GetValueOrDefault(k, "") + "\0")) +
            string.Concat(new[] { "gen_ai.request.model", "gen_ai.response.model", "gen_ai.provider.name", "gen_ai.operation.name", "server.address", "server.port", "error.type" }
                .Select(k => k + "=" + attributes.GetValueOrDefault(k, "") + "\0"));
        string name = attributes.GetValueOrDefault("gen_ai.response.model", attributes.GetValueOrDefault("gen_ai.request.model", resource.GetValueOrDefault("service.name", "OTel source")));
        if (name.Length > 256) name = name[..256];
        return new(SourceId, MetadataFields.Id(identity), DeviceKind.Runtime, name, "0");
    }
    private static Dictionary<string, string> Attributes(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in Array(root, "attributes"))
        {
            string? key = MetadataFields.Text(attribute, "key");
            // Allow only identifiers and numeric token usage, never prompt, message or tool bodies.
            if (key == null || !(key is "service.name" or "service.instance.id" or "host.id" or "host.name" or "process.pid" or "error.type" or "server.address" or "server.port" or "gen_ai.token.type" or
                "gen_ai.request.model" or "gen_ai.response.model" or "gen_ai.provider.name" or "gen_ai.operation.name" || key.StartsWith("gen_ai.usage.", StringComparison.Ordinal))) continue;
            if (!attribute.TryGetProperty("value", out var value)) continue;
            string? text = MetadataFields.Text(value, "stringValue") ?? MetadataFields.Text(value, "intValue");
            if (text == null && value.TryGetProperty("intValue", out var integer) && integer.ValueKind == JsonValueKind.Number) text = integer.GetRawText();
            if (text == null && value.TryGetProperty("doubleValue", out var number) && number.ValueKind == JsonValueKind.Number) text = number.GetRawText();
            if (text != null && text.Length <= 512 && !result.TryAdd(key, text)) throw new FormatException("Duplicate OTLP attribute.");
        }
        return result;
    }
    private static IEnumerable<JsonElement> Array(JsonElement root, string key) => root.TryGetProperty(key, out var array) ? array.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static string ScopeIdentity(JsonElement root) => root.TryGetProperty("scope", out var scope) ?
        MetadataFields.Id((MetadataFields.Text(scope, "name") ?? "") + "\0" + (MetadataFields.Text(scope, "version") ?? "")) : "";
    private static int Temporality(JsonElement root)
    {
        if (!root.TryGetProperty("aggregationTemporality", out var value)) throw new FormatException("Missing OTLP aggregation temporality.");
        int result = value.ValueKind == JsonValueKind.Number ? value.GetInt32() : value.GetString() switch
        { "AGGREGATION_TEMPORALITY_DELTA" => 1, "AGGREGATION_TEMPORALITY_CUMULATIVE" => 2, _ => 0 };
        return result is 1 or 2 ? result : throw new FormatException("Unsupported OTLP aggregation temporality.");
    }
    internal static DateTimeOffset NanoTime(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) throw new FormatException("Missing OTLP timestamp.");
        ulong nanos = value.ValueKind == JsonValueKind.String ? ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetUInt64();
        if (nanos == 0) throw new FormatException("OTLP timestamp is unspecified.");
        return DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));
    }
    private static string HexId(JsonElement root, string key, int length)
    {
        var value = MetadataFields.Text(root, key);
        return value?.Length == length && value.All(char.IsAsciiHexDigit) && value.Any(c => c != '0') ? value.ToLowerInvariant() : throw new FormatException("Invalid OTLP trace/span identifier.");
    }
}
