using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GpuMonitor;

/// <summary>Reads Token Monitor's documented, auto-written JSON export. No tool
/// transcripts, account stores or network APIs are accessed.</summary>
public sealed class TokenMonitorExportCollector(string sourceId, string path) : ITelemetryProvider, ITelemetryHistoryPolicy
{
    public string SourceId => sourceId;
    public int HistoryCapacity => 900;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(30);
    public IReadOnlyList<MetricDefinition> Definitions { get; } = [
        new(sourceId, "tokenmonitor.total", "Exported token total", "tokens"),
        new(sourceId, "tokenmonitor.rate", "Export-interval token rate (all time only)", "tokens/s")];
    private readonly Dictionary<string, (DateTimeOffset Time, double Total)> previous = [];
    private DateTimeOffset? latestAcceptedSnapshot;
    private TelemetryBatch? latestAcceptedBatch;

    public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > 4 * 1024 * 1024) throw new FormatException("Token Monitor export exceeds 4 MiB.");
            using var memory = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (memory.Length + read > 4 * 1024 * 1024) throw new FormatException("Token Monitor export exceeds 4 MiB.");
                memory.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            return Normalize(document.RootElement);
        }
        catch { previous.Clear(); throw; }
    }

    internal TelemetryBatch Normalize(JsonElement root)
    {
        if (root.GetProperty("app").GetProperty("name").GetString() != "token-monitor") throw new FormatException("Not a Token Monitor export.");
        string? generated = root.GetProperty("generatedAt").GetString();
        bool offset = generated != null && (generated.EndsWith('Z') || generated.Length >= 6 && generated[^3] == ':' && generated[^6] is '+' or '-');
        if (!offset || !DateTimeOffset.TryParse(generated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            throw new FormatException("Token Monitor export needs a generatedAt timestamp with offset.");
        if (latestAcceptedSnapshot is { } latestSnapshot && time < latestSnapshot) return latestAcceptedBatch!;
        var snapshot = root.GetProperty("snapshot");
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
        var next = new Dictionary<string, (DateTimeOffset Time, double Total)>();
        foreach (string period in new[] { "today", "month", "allTime" })
        {
            if (!snapshot.TryGetProperty(period, out var totals) || totals.ValueKind != JsonValueKind.Object) continue;
            foreach (string dimension in new[] { "clients", "models" })
            {
                if (!totals.TryGetProperty(dimension, out var breakdown) || breakdown.ValueKind != JsonValueKind.Object) continue;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in breakdown.EnumerateObject())
                {
                    if (devices.Count >= 256) throw new FormatException("Token Monitor export exceeds 256 period/breakdown series.");
                    if (property.Name.Length is < 1 or > 256 || !names.Add(property.Name)) throw new FormatException("Invalid or duplicate Token Monitor dimension.");
                    double? total = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 ? value : null;
                    string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(period + "\0" + dimension + "\0" + property.Name)));
                    var device = new DeviceIdentity(sourceId, id, DeviceKind.Runtime, $"{property.Name} · {period} · {(dimension == "clients" ? "tool" : "model")}", devices.Count.ToString(CultureInfo.InvariantCulture));
                    double? rate = null;
                    if (period == "allTime" && total is { } current)
                    {
                        if (previous.TryGetValue(id, out var prior) && time > prior.Time && current >= prior.Total)
                        {
                            rate = (current - prior.Total) / (time - prior.Time).TotalSeconds;
                            if (!double.IsFinite(rate.Value)) rate = null;
                        }
                        next[id] = previous.TryGetValue(id, out var latest) && time <= latest.Time ? latest : (time, current);
                    }
                    devices.Add(device); samples.Add(new(device, "tokenmonitor.total", time, total)); samples.Add(new(device, "tokenmonitor.rate", time, rate));
                }
            }
        }
        if (devices.Count == 0) throw new FormatException("Token Monitor export has no tool/model totals.");
        previous.Clear(); foreach (var pair in next) previous.Add(pair.Key, pair.Value);
        latestAcceptedSnapshot = time;
        latestAcceptedBatch = WindowsSystemCollector.Batch(devices.AsReadOnly(), samples);
        return latestAcceptedBatch;
    }

}
