using System.Text.Json;

namespace GpuMonitor;

/// <summary>Explicit local usage records, including OpenAI-compatible usage objects. Never reads conversation fields.</summary>
public sealed class UsageMetadataDecoder(string sourceId) : IMetadataDecoder
{
    public string SourceId => sourceId;
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new(sourceId, "usage.input", "Request input tokens", "tokens"),
        new(sourceId, "usage.output", "Request output tokens", "tokens"),
        new(sourceId, "usage.cache_read", "Request cached input tokens", "tokens"),
        new(sourceId, "usage.cache_write", "Request cache-write tokens", "tokens"),
        new(sourceId, "usage.output_rate", "Request-average output throughput", "tokens/s") });
    public IMetadataDecoder Fresh() => new UsageMetadataDecoder(sourceId);
    public IMetadataDecoder Clone() => Fresh();

    public TelemetryBatch Decode(JsonElement root, DateTimeOffset observedAt)
    {
        string? id = MetadataFields.Text(root, "id");
        if (id == null) throw new FormatException("Usage records require a stable id.");
        string? timestamp = MetadataFields.Text(root, "timestamp");
        bool hasOffset = timestamp != null && (timestamp.EndsWith('Z') || timestamp.Length >= 6 && timestamp[^3] == ':' && timestamp[^6] is '+' or '-');
        if (!hasOffset || !DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var time)) throw new FormatException("Usage records require an ISO-8601 timestamp with offset.");
        string model = MetadataFields.Text(root, "model") ?? "Unknown model";
        if (model.Length > 256) throw new FormatException("Usage model name exceeds limit.");
        string provider = MetadataFields.Text(root, "provider") ?? "local";
        string session = MetadataFields.Text(root, "sessionId") ?? "";
        var device = new DeviceIdentity(sourceId, MetadataFields.Id(provider + "\0" + model + "\0" + session), DeviceKind.Runtime, model, "0");
        var usage = root.TryGetProperty("usage", out var nested) ? nested : root;
        double? input = MetadataFields.Number(usage, "inputTokens") ?? MetadataFields.Number(usage, "input_tokens") ?? MetadataFields.Number(usage, "prompt_tokens");
        double? output = MetadataFields.Number(usage, "outputTokens") ?? MetadataFields.Number(usage, "output_tokens") ?? MetadataFields.Number(usage, "completion_tokens");
        double? cache = MetadataFields.Number(usage, "cacheReadTokens") ?? MetadataFields.Number(usage, "cache_read_input_tokens");
        if (usage.TryGetProperty("prompt_tokens_details", out var details) || usage.TryGetProperty("input_tokens_details", out details))
            cache ??= MetadataFields.Number(details, "cached_tokens");
        double? cacheWrite = MetadataFields.Number(usage, "cacheWriteTokens") ?? MetadataFields.Number(usage, "cache_creation_input_tokens");
        if (input == null && output == null && cache == null && cacheWrite == null) throw new FormatException("Usage record contains no measured tokens.");
        double? milliseconds = MetadataFields.Number(root, "durationMs");
        double? rate = milliseconds > 0 ? output / (milliseconds / 1000) : null;
        if (rate.HasValue && !double.IsFinite(rate.Value)) rate = null;
        var samples = new List<TelemetrySample> {
            new(device, "usage.input", time, input), new(device, "usage.output", time, output),
            new(device, "usage.cache_read", time, cache), new(device, "usage.cache_write", time, cacheWrite),
            new(device, "usage.output_rate", time, rate) };
        var batch = WindowsSystemCollector.Batch(Array.AsReadOnly(new[] { device }), samples);
        return batch with { Events = Array.AsReadOnly(new[] { new TelemetryEvent(sourceId, MetadataFields.Id(id), time, "usage.request.completed", device.Id,
            session.Length == 0 ? null : MetadataFields.Id(session), milliseconds.HasValue ? TimeSpan.FromMilliseconds(milliseconds.Value) : null,
            Measurements: Array.AsReadOnly(samples.Where(s => s.Value.HasValue).Select(s => new EventMeasurement(s.MetricKey,
                Definitions.Single(d => d.Key == s.MetricKey).Unit, s.Value!.Value)).ToArray())) }) };
    }
}
