using System.Text.Json;
using GpuMonitor;

internal static class MetadataFeedChecks
{
    private static TelemetryBatch Decode(IMetadataDecoder decoder, object record, DateTimeOffset now)
    { using var doc = JsonDocument.Parse(JsonSerializer.Serialize(record)); return decoder.Decode(doc.RootElement, now); }
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        long ms = now.ToUnixTimeMilliseconds();
        var agent = new AgentEventDecoder("agui:test");
        Decode(agent, new { type = "RUN_STARTED", runId = "run1", timestamp = ms }, now);
        Decode(agent, new { type = "TOOL_CALL_START", toolCallId = "call1", toolCallName = "search", timestamp = ms + 10 }, now);
        var tool = Decode(agent, new { type = "TOOL_CALL_END", toolCallId = "call1", timestamp = ms + 1010 }, now);
        check(tool.Events.Single().Duration == TimeSpan.FromSeconds(1) && tool.Events.Single().CorrelationId != null,
            "AG-UI tool lifecycle retains actual duration and run correlation");
        check(tool.Events.Single().Name == "search" && tool.Events.Single().ActivityId != null && tool.Events.Single().ParentId != null,
            "Completed tools retain their start name and explicit activity/parent identity for timeline rendering");
        var finished = Decode(agent, new { type = "RUN_FINISHED", runId = "run1", timestamp = ms + 2010 }, now);
        check(finished.Events.Single().Duration == TimeSpan.FromMilliseconds(2010), "AG-UI run durations use matched source timestamps");
        var ignored = Decode(agent, new { type = "TEXT_MESSAGE_CONTENT", delta = "SECRET_PROMPT", rawEvent = "SECRET_TOKEN" }, now);
        check(ignored.Events.Count == 0 && !JsonSerializer.Serialize(ignored).Contains("SECRET"), "Agent content and raw payloads are never retained");
        Decode(agent, new { type = "RUN_STARTED", runId = "estimated" }, now);
        var estimated = Decode(agent, new { type = "RUN_FINISHED", runId = "estimated" }, now.AddSeconds(1));
        check(estimated.Events.Single().TimestampEstimated && estimated.Events.Single().Duration == null,
            "Missing AG-UI timestamps are labeled estimated and cannot invent precise durations");
        Decode(agent, new { type = "RUN_STARTED", runId = "a", timestamp = ms }, now);
        Decode(agent, new { type = "RUN_STARTED", runId = "b", timestamp = ms }, now);
        bool ambiguous = false;
        try { Decode(agent, new { type = "TOOL_CALL_START", toolCallId = "ambiguous", toolCallName = "search", timestamp = ms }, now); }
        catch (FormatException) { ambiguous = true; }
        check(ambiguous, "Interleaved AG-UI runs cannot silently misattribute unscoped tool calls");
        var usage = new UsageMetadataDecoder("usage:test");
        var usageBatch = Decode(usage, new { id = "req1", timestamp = now.ToString("O"), model = "local-model", provider = "local",
            durationMs = 2000, usage = new { prompt_tokens = 100, completion_tokens = 20, prompt_tokens_details = new { cached_tokens = 25 } },
            messages = "SECRET_PROMPT", api_key = "SECRET_KEY" }, now);
        check(usageBatch.Samples.Single(s => s.MetricKey == "usage.input").Value == 100 &&
            usageBatch.Samples.Single(s => s.MetricKey == "usage.cache_read").Value == 25 &&
            usageBatch.Samples.Single(s => s.MetricKey == "usage.output_rate").Value == 10,
            "Usage metadata separates cached input and measured request-average output rate without double counting");
        check(!JsonSerializer.Serialize(usageBatch).Contains("SECRET") && usageBatch.Events.Single().Measurements!.Count == 4,
            "Raw numeric request measurements survive as events while prompt/credential fields are discarded");
        string directory = Path.Combine(Path.GetTempPath(), "gpu-feed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "agent.jsonl");
            var provider = new JsonlTelemetryProvider("agui:file", path, new AgentEventDecoder("agui:file"));
            string start = JsonSerializer.Serialize(new { type = "RUN_STARTED", runId = "run", timestamp = ms });
            string end = JsonSerializer.Serialize(new { type = "RUN_FINISHED", runId = "run", timestamp = ms + 1000 });
            await File.WriteAllTextAsync(path, start); // incomplete append is not consumed
            check((await provider.ReadAsync(CancellationToken.None)).Events.Count == 0, "A partial JSONL record is held until newline termination");
            await File.AppendAllTextAsync(path, "\n");
            var first = await provider.ReadAsync(CancellationToken.None);
            check(first.Events.Count == 1 && (await provider.ReadAsync(CancellationToken.None)).Events.Count == 0,
                "JSONL cursor consumes each complete event once and accepts idle feeds");
            await File.AppendAllTextAsync(path, end + "\n{broken}\n");
            bool failed = false;
            try { await provider.ReadAsync(CancellationToken.None); } catch (JsonException) { failed = true; }
            await File.WriteAllTextAsync(path, start + "\n" + end + "\n");
            var recovered = await provider.ReadAsync(CancellationToken.None);
            check(failed && recovered.Events.Any(e => e.Duration == TimeSpan.FromSeconds(1)),
                "Malformed JSONL batches cannot commit partial decoder state or lose their cursor");
            var session = new TelemetrySession(provider);
            session.Apply(first, now); session.Apply(first, now);
            check(session.EventSnapshot().Count == 1, "Replayed discrete event IDs are deduplicated");
            session.Prune(now.AddMinutes(31));
            check(session.EventSnapshot().Count == 0, "Event history expires after its retention window");
        }
        finally { Directory.Delete(directory, true); }
    }
}
