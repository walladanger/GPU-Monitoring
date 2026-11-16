using GpuMonitor;

internal static class EventTimelineChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-11-15T12:00:00Z");
        var range = new PlotRange(now.AddMinutes(-1), now);
        var ended = new TelemetryEvent("otlp:local", "1", now.AddSeconds(-10), "span", "device", "trace", TimeSpan.FromSeconds(70), Name: "tool,exact");
        var point = new TelemetryEvent("agui:local", "2", now, "run.started", TimestampEstimated: true);
        var outside = ended with { Id = "3", Time = now.AddMinutes(-2) };
        var view = EventTimeline.Project([ended, point, ended, outside], range);
        check(view.Entries.Count == 2 && view.Entries[0].Start == now.AddSeconds(-80) && view.Entries[0].End == ended.Time,
            "Timeline keeps overlapping measured durations, deduplicates events and excludes out-of-window activity");
        check(view.Entries[1].Start == point.Time && view.Entries[1].Event.TimestampEstimated && view.Entries[1].Event.Duration == null,
            "Unknown and estimated activity times remain point events without fabricated durations");
        check(EventTimeline.Project([ended, point], range, "agui:local").Entries.Single().Event == point,
            "Timeline source filter never mixes unrelated providers");
        var busy = Enumerable.Range(0, 600).Select(i => ended with { Id = i.ToString() }).ToArray();
        check(EventTimeline.Project(busy, range).Entries.Count == 512 && EventTimeline.Project(busy, range).Omitted == 88,
            "Timeline bounds rendered event count and reports omitted activity");
        var lanes = Enumerable.Range(0, 30).Select(i => ended with { Id = i.ToString(), CorrelationId = i.ToString() });
        var limited = EventTimeline.Project(lanes, range);
        check(limited.Lanes.Count == 24 && limited.Omitted == 6, "Timeline bounds trace lanes without silently merging unrelated identities");
        using var csv = new StringWriter(); EventTimeline.Export(csv, view, "csv");
        check(csv.ToString().Contains("\"tool,exact\"") && csv.ToString().Contains("70000") && csv.ToString().Contains("otlp:local"),
            "Timeline CSV preserves quoted names, exact raw duration and source identity");
        using var json = new StringWriter(); EventTimeline.Export(json, view, "json");
        using var doc = System.Text.Json.JsonDocument.Parse(json.ToString());
        check(doc.RootElement.GetProperty("Entries").GetArrayLength() == 2, "Timeline JSON exports real selectable event metadata");
        using var text = new StringWriter(); EventTimeline.Export(text, view, "txt");
        check(text.ToString().Contains("duration=unknown") && text.ToString().Contains("estimated=True"), "Timeline text labels unknown durations and estimated timestamps");
    }
}
