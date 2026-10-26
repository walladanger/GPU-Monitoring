using GpuMonitor;

internal static class TelemetrySessionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var time = DateTimeOffset.UtcNow.AddSeconds(-20);
        TelemetryBatch Batch(int second, params string[] ids) => NvidiaTelemetry.Normalize(
            CsvTelemetry.Parse(string.Join('\n', ids.Select((id, index) =>
                $"{index}, NVIDIA GeForce RTX 3090, GPU-{id}, 00000000:01:00.0, 24576, 30, 1024, 45, 120, [N/A], 1500, 9750")), time.AddSeconds(second)));
        var provider = new FixtureProvider { Batch = Batch(0, "A", "B") };
        var session = new TelemetrySession(provider);
        check(session.Health == ProviderHealth.Starting && session.Current == null, "Session starts without invented telemetry");
        await session.RefreshAsync(CancellationToken.None);
        check(provider.ReadCount == 1 && session.Health == ProviderHealth.Live && session.Current!.Devices.Count == 2,
            "One provider read populates live session with two real fixture devices");
        var projected = NvidiaSnapshot.Readings(session.Current!);
        check(projected.Count == 2 && projected[0].Metrics["memory.used"] == 1024 && projected[0].Metrics["fan.speed"] == null && projected[0].Time == time,
            "GPU card projection preserves exact values, N/A and source timestamp");
        var frozen = session.History("GPU-A", "utilization.gpu");
        provider.Failure = new InvalidOperationException("driver unavailable");
        bool failed = false;
        try { await session.RefreshAsync(CancellationToken.None); } catch (InvalidOperationException) { failed = true; }
        check(failed && session.Health == ProviderHealth.Stale && session.Current!.Devices.Count == 2 && session.Error == "driver unavailable",
            "Failed poll retains inventory and marks provider stale instead of reporting zero GPUs");
        var history = session.History("GPU-A", "utilization.gpu");
        check(history.Count == 2 && history[1].Value == null && frozen.Count == 1,
            "Failure inserts a chart gap while earlier snapshots remain stable");
        provider.Failure = null; provider.Batch = Batch(30, "B", "A");
        await session.RefreshAsync(CancellationToken.None);
        check(session.Health == ProviderHealth.Live && session.Error == null && session.LastSuccess.HasValue,
            "Recovery clears stale health and records successful poll time");
        history = session.History("GPU-A", "utilization.gpu");
        check(history.Count == 3 && history[1].Value == null && history[2].Value == 30 && history[2].Device.DisplayIndex == "1",
            "Recovery retains outage gap and UUID history after GPU index reorder");
        provider.Batch = Batch(31, "B"); await session.RefreshAsync(CancellationToken.None);
        check(session.History("GPU-A", "utilization.gpu").Count == 0 && session.History("GPU-B", "utilization.gpu").Count == 4,
            "Successful removal deletes only absent device histories");
        var previous = session.Current;
        var invalid = provider.Batch with { Samples = provider.Batch.Samples.Concat(provider.Batch.Samples.Take(1)).ToArray() };
        bool rejected = false;
        try { session.Apply(invalid, time.AddSeconds(32)); } catch (ArgumentException) { rejected = true; }
        check(rejected && ReferenceEquals(previous, session.Current) && session.History("GPU-B", "utilization.gpu").Count == 4,
            "Invalid duplicate series cannot partially change active histories");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool cancelled = false;
        try { await session.RefreshAsync(stop.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && session.Health == ProviderHealth.Live && ReferenceEquals(previous, session.Current),
            "Caller cancellation preserves last snapshot and does not invent an outage");
        provider.Failure = new OperationCanceledException("provider timeout");
        bool timedOut = false;
        try { await session.RefreshAsync(CancellationToken.None); } catch (OperationCanceledException) { timedOut = true; }
        check(timedOut && session.Health == ProviderHealth.Stale, "Provider timeout marks stale when caller has not cancelled");
        session.Prune(time.AddHours(1));
        check(session.History("GPU-B", "utilization.gpu").Count == 0 && session.Current!.Devices.Count == 1,
            "Outage history expires without fabricating device removal");
        provider.Failure = null; provider.Batch = Batch(32);
        await session.RefreshAsync(CancellationToken.None);
        check(session.Health == ProviderHealth.Live && session.Current!.Devices.Count == 0 && session.History("GPU-B", "utilization.gpu").Count == 0,
            "Successful empty snapshot clears devices and histories");
    }

    private sealed class FixtureProvider : ITelemetryProvider
    {
        public string SourceId => NvidiaTelemetry.SourceId;
        public IReadOnlyList<MetricDefinition> Definitions => NvidiaTelemetry.Definitions;
        public required TelemetryBatch Batch { get; set; }
        public Exception? Failure { get; set; }
        public int ReadCount { get; private set; }
        public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
        {
            ReadCount++;
            cancellation.ThrowIfCancellationRequested();
            return Failure == null ? Task.FromResult(Batch) : Task.FromException<TelemetryBatch>(Failure);
        }
    }
}
