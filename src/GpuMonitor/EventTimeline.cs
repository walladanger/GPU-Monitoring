using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GpuMonitor;

public sealed record TimelineEntry(TelemetryEvent Event, DateTimeOffset Start, DateTimeOffset End, int Lane);
public sealed record TimelineView(PlotRange Range, IReadOnlyList<string> Lanes, IReadOnlyList<TimelineEntry> Entries, int Omitted);

/// <summary>Projects real lifecycle metadata into bounded, shared UTC time lanes.</summary>
public static class EventTimeline
{
    public static TimelineView Project(IEnumerable<TelemetryEvent> events, PlotRange range, string? source = null)
    {
        if (range.End <= range.Start) throw new ArgumentException("Timeline requires a positive time range.");
        DateTimeOffset Start(TelemetryEvent e) => e.Duration is { } duration && duration > TimeSpan.Zero && duration.Ticks <= e.Time.UtcTicks
            ? e.Time - duration : e.Time;
        var matching = events.Where(e => (source == null || e.Source == source) && e.Time >= range.Start && Start(e) <= range.End)
            .DistinctBy(e => (e.Source, e.Id, e.Kind)).OrderByDescending(e => e.Time).ToArray();
        // Group trace spans by correlation and uncorrelated processes by stable
        // identity. Never merge equal PIDs from unrelated provider lifetimes.
        string Lane(TelemetryEvent e) => e.Source + " · " + (e.CorrelationId ?? e.DeviceId ?? "Events");
        var lanes = matching.Select(Lane).Distinct().Take(24).ToArray();
        var indices = lanes.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index);
        var entries = matching.Where(e => indices.ContainsKey(Lane(e))).Take(512)
            .OrderBy(e => e.Time).Select(e => new TimelineEntry(e, Start(e), e.Time, indices[Lane(e)])).ToArray();
        return new(range, lanes, entries, matching.Length - entries.Length);
    }

    public static void Export(TextWriter writer, TimelineView view, string format)
    {
        if (format == "json") { writer.Write(JsonSerializer.Serialize(view, new JsonSerializerOptions { WriteIndented = true })); return; }
        static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        if (format == "csv") writer.WriteLine("source,id,kind,start_utc,end_utc,duration_ms,estimated,device_id,correlation_id,parent_id,activity_id,name,measurements_json");
        else if (format == "txt") writer.WriteLine($"GPU Monitor activity timeline · {view.Range.Start:O} – {view.Range.End:O}\n{view.Entries.Count} events; {view.Omitted} omitted by display limits.");
        else throw new ArgumentException("Unsupported timeline export format.");
        foreach (var item in view.Entries)
        {
            var e = item.Event;
            if (format == "csv") writer.WriteLine(string.Join(',', new[] { e.Source, e.Id, e.Kind, item.Start.ToString("O"), item.End.ToString("O"),
                e.Duration?.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture), e.TimestampEstimated.ToString(), e.DeviceId, e.CorrelationId, e.ParentId, e.ActivityId, e.Name,
                JsonSerializer.Serialize(e.Measurements) }.Select(Csv)));
            else writer.WriteLine($"{item.Start:O} – {item.End:O} | {e.Source} | {e.Kind} | {e.Name ?? e.DeviceId ?? e.Id} | duration={e.Duration?.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture) ?? "unknown"} ms | estimated={e.TimestampEstimated} | correlation={e.CorrelationId ?? "none"}");
        }
    }
}
