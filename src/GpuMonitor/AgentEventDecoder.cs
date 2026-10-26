using System.Text.Json;

namespace GpuMonitor;

/// <summary>AG-UI lifecycle metadata only. Message/state/tool argument bodies are discarded.</summary>
public sealed class AgentEventDecoder(string sourceId) : IMetadataDecoder
{
    public string SourceId => sourceId;
    public IReadOnlyList<MetricDefinition> Definitions => Array.Empty<MetricDefinition>();
    private readonly Dictionary<string, (DateTimeOffset Time, bool Estimated, string? Name)> started = [];
    private readonly HashSet<string> runs = [];
    public IMetadataDecoder Fresh() => new AgentEventDecoder(sourceId);
    public IMetadataDecoder Clone()
    {
        var copy = new AgentEventDecoder(sourceId);
        foreach (var pair in started) copy.started.Add(pair.Key, pair.Value);
        foreach (var run in runs) copy.runs.Add(run);
        return copy;
    }

    public TelemetryBatch Decode(JsonElement record, DateTimeOffset observedAt)
    {
        var root = record.TryGetProperty("event", out var wrapped) ? wrapped : record;
        string? type = MetadataFields.Text(root, "type");
        var device = new DeviceIdentity(SourceId, "agent-events", DeviceKind.Runtime, "Agent activity", "0");
        if (type is not ("RUN_STARTED" or "RUN_FINISHED" or "RUN_ERROR" or "STEP_STARTED" or "STEP_FINISHED" or "TOOL_CALL_START" or "TOOL_CALL_END"))
            return MetadataFields.Events(device, []);
        foreach (var old in started.Where(p => observedAt - p.Value.Time > TimeSpan.FromMinutes(30)).Select(p => p.Key).ToArray()) started.Remove(old);
        runs.RemoveWhere(r => !started.ContainsKey(r + "/run"));
        var suppliedRun = MetadataFields.Text(root, "runId") ?? MetadataFields.Text(record, "runId");
        string? run = suppliedRun == null ? null : MetadataFields.Id(suppliedRun);
        if (run == null && runs.Count > 1) throw new FormatException("Multiplexed AG-UI events require an explicit runId envelope.");
        run ??= runs.SingleOrDefault();
        bool estimated = !root.TryGetProperty("timestamp", out var timeValue) || timeValue.ValueKind == JsonValueKind.Null;
        var time = estimated ? observedAt : DateTimeOffset.FromUnixTimeMilliseconds(timeValue.GetInt64());
        string scope = type.StartsWith("RUN_", StringComparison.Ordinal) ? "run" : type.StartsWith("STEP_", StringComparison.Ordinal) ? "step" : "tool";
        string? rawName = scope == "tool" ? MetadataFields.Text(root, "toolCallName") : scope == "step" ? MetadataFields.Text(root, "stepName") : null;
        string? name = rawName;
        if (name?.Length > 128 || (name != null && name.Any(c => !char.IsAsciiLetterOrDigit(c) && !"_.:/-".Contains(c)))) name = null;
        string local = scope == "tool" ? MetadataFields.Text(root, "toolCallId") ?? throw new FormatException("Missing AG-UI toolCallId.") : scope == "step" ? rawName ?? throw new FormatException("Missing AG-UI stepName.") : suppliedRun ?? run ?? throw new FormatException("Missing AG-UI runId.");
        string key = scope == "run" ? (run ?? throw new FormatException("Missing AG-UI runId.")) + "/run" :
            (run ?? "unscoped") + "/" + scope + "/" + MetadataFields.Id(local);
        bool isStart = type is "RUN_STARTED" or "STEP_STARTED" or "TOOL_CALL_START";
        TimeSpan? duration = null;
        if (isStart)
        {
            if (started.Count >= 1024) throw new FormatException("Too many concurrent AG-UI activities.");
            started.TryAdd(key, (time, estimated, name));
            if (scope == "run" && run != null) runs.Add(run);
        }
        else
        {
            if (started.Remove(key, out var begin))
            {
                name ??= begin.Name;
                if (!estimated && !begin.Estimated && time >= begin.Time) duration = time - begin.Time;
            }
            if (scope == "run" && run != null)
            {
                runs.Remove(run);
                foreach (var other in started.Keys.Where(k => k.StartsWith(run + "/", StringComparison.Ordinal)).ToArray()) started.Remove(other);
            }
        }
        foreach (var old in started.Where(p => observedAt - p.Value.Time > TimeSpan.FromMinutes(30)).Select(p => p.Key).ToArray()) started.Remove(old);
        return MetadataFields.Events(device, [new(SourceId, key + "/" + type + "/" + time.UtcTicks, time,
            "agui." + type.ToLowerInvariant(), device.Id, run, duration, estimated,
            ParentId: scope == "run" || run == null ? null : run + "/run", Name: name, ActivityId: key)]);
    }
}
