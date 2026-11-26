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
        // Trace spans share a primary correlation lane, while NVIDIA process observations use
        // stable process identity. Overlapping intervals get subrows so every marker remains inspectable.
        string Lane(TelemetryEvent e) => e.Source + " · " + (e.Source == "nvidia-processes" ? e.DeviceId ?? e.Id : e.CorrelationId ?? e.DeviceId ?? "Events");
        var lanes = new List<string>();
        var entries = new List<TimelineEntry>();
        foreach (var group in matching.GroupBy(Lane))
        {
            int firstRow = lanes.Count;
            var rowEnds = new List<DateTimeOffset>();
            foreach (var e in group.OrderBy(Start).ThenByDescending(e => e.Time).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                int row = rowEnds.FindIndex(end => end < Start(e));
                if (row < 0)
                {
                    row = rowEnds.Count;
                    rowEnds.Add(DateTimeOffset.MinValue);
                    lanes.Add(row == 0 ? group.Key : group.Key + " · " + (row + 1));
                }
                rowEnds[row] = e.Time;
                entries.Add(new(e, Start(e), e.Time, firstRow + row));
                if (entries.Count == 512) break;
            }
            if (entries.Count == 512) break;
        }
        var ordered = entries.OrderBy(e => e.End).ThenBy(e => e.Event.Source, StringComparer.Ordinal)
            .ThenBy(e => e.Event.Id, StringComparer.Ordinal).ToArray();
        return new(range, lanes.AsReadOnly(), ordered, matching.Length - ordered.Length);
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
