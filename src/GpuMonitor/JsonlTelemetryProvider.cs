using System.IO;
using System.Text;
using System.Text.Json;

namespace GpuMonitor;

public interface IMetadataDecoder
{
    IReadOnlyList<MetricDefinition> Definitions { get; }
    IMetadataDecoder Clone();
    IMetadataDecoder Fresh();
    TelemetryBatch Decode(JsonElement record, DateTimeOffset observedAt);
}

/// <summary>Explicitly configured local metadata feed. Cursor and decoder commit together after a valid batch.</summary>
public sealed class JsonlTelemetryProvider(string sourceId, string path, IMetadataDecoder decoder) : ITelemetryProvider, ITelemetryHistoryPolicy
{
    public string SourceId => sourceId;
    public IReadOnlyList<MetricDefinition> Definitions => decoder.Definitions;
    public int HistoryCapacity => 900;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(30);
    private long offset;
    private long? creation;
    private readonly Dictionary<string, DeviceIdentity> devices = [];
    private readonly Dictionary<(string Device, string Metric), TelemetrySample> latest = [];

    public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Configured metadata feed is unavailable.");
        long stamp = info.CreationTimeUtc.Ticks;
        bool rotated = creation.HasValue && (creation != stamp || info.Length < offset);
        long start = rotated ? 0 : offset;
        // New/rotated large files start at their tail; never scan an unbounded historical log.
        bool skipPartial = start == 0 && info.Length > 4 * 1024 * 1024;
        if (skipPartial) start = info.Length - 4 * 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = start;
        var bytes = new byte[4 * 1024 * 1024];
        int read = 0, count;
        while (read < bytes.Length && (count = await stream.ReadAsync(bytes.AsMemory(read), cancellation)) != 0) read += count;
        int end = read == 0 ? -1 : Array.LastIndexOf(bytes, (byte)'\n', read - 1, read);
        if (end < 0)
        {
            if (read > 1024 * 1024) throw new FormatException("Metadata record exceeds 1 MiB or is not newline terminated.");
            return Build([], rotated ? [] : latest.Values, rotated ? [] : devices.Values);
        }
        int begin = skipPartial ? Array.IndexOf(bytes, (byte)'\n', 0, end + 1) + 1 : 0;
        var candidate = rotated ? decoder.Fresh() : decoder.Clone();
        var nextDevices = rotated ? new Dictionary<string, DeviceIdentity>() : new(devices);
        var nextSamples = rotated ? new Dictionary<(string Device, string Metric), TelemetrySample>() : new(latest);
        var activities = new List<TelemetryEvent>();
        int records = 0;
        while (begin <= end)
        {
            cancellation.ThrowIfCancellationRequested();
            int newline = Array.IndexOf(bytes, (byte)'\n', begin, end - begin + 1);
            if (newline - begin > 1024 * 1024 || ++records > 4096) throw new FormatException("Metadata batch limit exceeded.");
            string line = Encoding.UTF8.GetString(bytes, begin, newline - begin).Trim().TrimStart('\uFEFF');
            begin = newline + 1;
            if (line.Length == 0) continue;
            using var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
            var batch = candidate.Decode(json.RootElement, DateTimeOffset.UtcNow);
            foreach (var device in batch.Devices) nextDevices[device.Id] = device;
            foreach (var sample in batch.Samples)
            {
                var key = (sample.Device.Id, sample.MetricKey);
                if (!nextSamples.TryGetValue(key, out var old) || sample.Time > old.Time) nextSamples[key] = sample;
            }
            activities.AddRange(batch.Events);
            if (nextDevices.Count > 64 || nextSamples.Count > 2048 || activities.Count > 4096) throw new FormatException("Metadata cardinality limit exceeded.");
        }
        // Explicit UTC samples age out rather than being relabeled with the latest poll time.
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var key in nextSamples.Where(p => p.Value.Time < cutoff).Select(p => p.Key).ToArray()) nextSamples.Remove(key);
        var output = Build(activities, nextSamples.Values, nextDevices.Values);
        cancellation.ThrowIfCancellationRequested();
        decoder = candidate; offset = start + end + 1; creation = stamp;
        devices.Clear(); foreach (var pair in nextDevices) devices.Add(pair.Key, pair.Value);
        latest.Clear(); foreach (var pair in nextSamples) latest.Add(pair.Key, pair.Value);
        return output;
    }

    private static TelemetryBatch Build(IEnumerable<TelemetryEvent> events, IEnumerable<TelemetrySample> samples, IEnumerable<DeviceIdentity> devices)
    {
        var values = samples.ToArray();
        return new(Array.AsReadOnly(devices.ToArray()), Array.AsReadOnly(values.Select(s => new MetricCapability(s.Device, s.MetricKey, s.Availability)).ToArray()),
            Array.AsReadOnly(values), Array.AsReadOnly(events.ToArray()));
    }
}

internal static class MetadataFields
{
    public static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static double? Number(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        double number;
        bool parsed = value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number) :
            double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
        return parsed && double.IsFinite(number) && number >= 0 ? number : null;
    }
    public static string Id(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static TelemetryBatch Events(DeviceIdentity device, IEnumerable<TelemetryEvent> events) =>
        new(Array.AsReadOnly(new[] { device }), Array.Empty<MetricCapability>(), Array.Empty<TelemetrySample>(), Array.AsReadOnly(events.ToArray()));
}
