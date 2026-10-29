using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpuMonitor;

public static class HeadlessCollector
{
    /// <summary>Finite, window-free collection with bounded histories and local exports.</summary>
    public static async Task<int> RunAsync(string directory, int durationSeconds, int intervalSeconds,
        CancellationToken cancellation = default, CollectorConfiguration? configuration = null, IEnumerable<ITelemetryProvider>? additionalProviders = null)
    {
        if (durationSeconds is < 1 or > 86400) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (intervalSeconds is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        Directory.CreateDirectory(directory);
        using var providers = BackendProviderSet.Create(configuration);
        var engine = new TelemetryEngine(providers.Providers.Concat(additionalProviders ?? []).ToArray());
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        duration.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        try { await engine.RunAsync(TimeSpan.FromSeconds(intervalSeconds), duration.Token); }
        catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
        var snapshot = engine.Snapshot();
        await ExportAsync(directory, snapshot, CancellationToken.None);
        return snapshot.Any(s => s.Health == ProviderHealth.Live) ? 0 : 1;
    }

    public static async Task ExportAsync(string directory, IReadOnlyList<ProviderSnapshot> snapshot, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        var json = new JsonSerializerOptions { WriteIndented = true };
        json.Converters.Add(new JsonStringEnumConverter());
        await WriteAsync(Path.Combine(directory, "telemetry.json"), JsonSerializer.Serialize(snapshot, json), cancellation);
        var csv = new StringBuilder("source,device_id,device_name,metric,time_utc,value\r\n");
        static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        foreach (var sample in snapshot.SelectMany(s => s.History).OrderBy(s => s.Time))
            csv.Append(Quote(sample.Device.Source)).Append(',').Append(Quote(sample.Device.Id)).Append(',')
                .Append(Quote(sample.Device.Name)).Append(',').Append(Quote(sample.MetricKey)).Append(',')
                .Append(sample.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "").Append("\r\n");
        await WriteAsync(Path.Combine(directory, "telemetry.csv"), csv.ToString(), cancellation);
        var eventsCsv = new StringBuilder("source,event_id,kind,time_utc,device_id,correlation_id,parent_id,activity_id,duration_ms,timestamp_estimated,name,metric,unit,value\r\n");
        foreach (var activity in snapshot.SelectMany(s => s.EventHistory).OrderBy(e => e.Time))
        {
            IEnumerable<EventMeasurement?> measurements = activity.Measurements?.Count > 0
                ? activity.Measurements.Select(m => (EventMeasurement?)m) : Enumerable.Repeat<EventMeasurement?>(null, 1);
            foreach (var measurement in measurements)
            {
                eventsCsv.Append(Quote(activity.Source)).Append(',').Append(Quote(activity.Id)).Append(',').Append(Quote(activity.Kind)).Append(',')
                    .Append(activity.Time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Quote(activity.DeviceId ?? "")).Append(',').Append(Quote(activity.CorrelationId ?? "")).Append(',')
                    .Append(Quote(activity.ParentId ?? "")).Append(',').Append(Quote(activity.ActivityId ?? "")).Append(',').Append(activity.Duration?.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(activity.TimestampEstimated ? "true" : "false").Append(',').Append(Quote(activity.Name ?? "")).Append(',')
                    .Append(Quote(measurement?.MetricKey ?? "")).Append(',').Append(Quote(measurement?.Unit ?? "")).Append(',')
                    .Append(measurement?.Value.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            }
        }
        await WriteAsync(Path.Combine(directory, "events.csv"), eventsCsv.ToString(), cancellation);
        var report = new StringBuilder("GPU Monitor collection report\n");
        foreach (var source in snapshot)
        {
            report.AppendLine($"{source.Source}: {source.Health}; last success: {source.LastSuccess:O}; error: {source.Error ?? "none"}");
            foreach (var device in source.Current?.Devices ?? []) report.AppendLine($"  {device.Kind}: {device.Name} [{device.Id}]");
        }
        await WriteAsync(Path.Combine(directory, "telemetry.txt"), report.ToString(), cancellation);
    }

    private static async Task WriteAsync(string path, string content, CancellationToken cancellation)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellation);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
