using GpuMonitor;

internal static class TelemetryEngineChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bad = new Provider("blocked", async token => { blocked.TrySetResult(); await Task.Delay(Timeout.Infinite, token); });
        var good = new Provider("healthy", _ => { healthy.TrySetResult(); return Task.CompletedTask; });
        var engine = new TelemetryEngine([bad, good]);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var running = engine.RunAsync(TimeSpan.FromMilliseconds(100), stop.Token);
        await Task.WhenAll(blocked.Task, healthy.Task).WaitAsync(stop.Token);
        // Signal comes from the read; wait for the snapshot to be applied, with a bounded deadline.
        while (engine.Snapshot().Single(s => s.Source == "healthy").Health != ProviderHealth.Live)
            await Task.Delay(10, stop.Token);
        check(engine.Snapshot().Single(s => s.Source == "blocked").Health == ProviderHealth.Starting,
            "A blocked provider cannot prevent another source from becoming live");
        bool overlap = false;
        try { await engine.RunAsync(TimeSpan.FromSeconds(1), stop.Token); }
        catch (InvalidOperationException) { overlap = true; }
        check(overlap, "Concurrent engine start is rejected");
        overlap = false;
        try { await engine.SampleOnceAsync(stop.Token); } catch (InvalidOperationException) { overlap = true; }
        check(overlap, "One-shot refresh cannot overlap a running engine");
        check(engine.Snapshot("healthy").Count == 1 && engine.Snapshot("missing").Count == 0,
            "Source-specific dashboard snapshots exclude unrelated histories and nonexistent sources");
        stop.Cancel();
        try { await running; } catch (OperationCanceledException) { }
        check(engine.Snapshot().Single(s => s.Source == "healthy").Health == ProviderHealth.Live,
            "Shutdown cancellation does not invent a provider outage");

        var failure = new Provider("failed", _ => Task.FromException(new IOException("offline")));
        var isolated = new TelemetryEngine([failure, good]);
        using var end = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = isolated.RunAsync(TimeSpan.FromMilliseconds(100), end.Token);
        while (isolated.Snapshot().Any(s => s.Health == ProviderHealth.Starting)) await Task.Delay(10, end.Token);
        var snapshot = isolated.Snapshot();
        check(snapshot.Single(s => s.Source == "failed").Error == "offline" &&
            snapshot.Single(s => s.Source == "healthy").Health == ProviderHealth.Live,
            "Provider failures remain isolated from healthy collection");
        end.Cancel(); try { await loop; } catch (OperationCanceledException) { }
        await isolated.SampleOnceAsync(CancellationToken.None);
        check(isolated.Snapshot("failed").Single().Health == ProviderHealth.Stale &&
            isolated.Snapshot("healthy").Single().Health == ProviderHealth.Live,
            "One-shot refresh isolates a failed source from successful source snapshots");
    }

    private sealed class Provider(string id, Func<CancellationToken, Task> read) : ITelemetryProvider
    {
        public string SourceId => id;
        public IReadOnlyList<MetricDefinition> Definitions => [];
        public async Task<TelemetryBatch> ReadAsync(CancellationToken token)
        {
            await read(token);
            return new([], [], [], []);
        }
    }
}
