using GpuMonitor;

internal static class HistoryRecorderChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "gpu-recorder-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "archive.sqlite");
        var provider = new Provider();
        await using var monitor = new DashboardTelemetry([provider]);
        try
        {
            await monitor.RefreshAsync();
            check(!monitor.HistoryStatus.Requested && !Directory.Exists(root), "Disabled history performs no database or directory writes");
            await monitor.ConfigureHistoryAsync(new(path));
            await monitor.SetMonitoringAsync(true, TimeSpan.FromMilliseconds(100));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (monitor.HistoryStatus.Samples < 2) await Task.Delay(10, timeout.Token);
            provider.Fail = true; await monitor.RefreshAsync(); provider.Fail = false;
            await monitor.ConfigureHistoryAsync(null);
            int reads = provider.Reads;
            while (provider.Reads <= reads) await Task.Delay(10, timeout.Token);
            await monitor.SetMonitoringAsync(false, TimeSpan.FromMilliseconds(100));
            var rows = HistoryStore.ReadFile(path, new("history-fixture", "cpu-stable", "usage"), "%", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
            check(rows.Count >= 2 && rows.Where(r => r.Sample.Value.HasValue).All(r => r.Sample.Value == 25.125) && rows.Any(r => r.Sample.Value == null) && !monitor.HistoryStatus.Requested,
                "Archive enable/disable drains exact measured samples while the independent collector continues");
            var broken = Path.Combine(root, "broken.sqlite"); File.WriteAllText(broken, "keep this file");
            await monitor.ConfigureHistoryAsync(new(broken)); await monitor.RefreshAsync();
            check(monitor.HistoryStatus.Error != null && monitor.Snapshot().Single().Health == ProviderHealth.Live && File.ReadAllText(broken) == "keep this file",
                "SQLite initialization failure remains visible without stopping telemetry or replacing the history file");
            await monitor.ConfigureHistoryAsync(null);
            var observed = new TelemetryEngine([new Provider()], (_, _) => throw new IOException("observer failed"));
            await observed.SampleOnceAsync(CancellationToken.None);
            check(observed.Snapshot().Single().Health == ProviderHealth.Live, "A failing optional observer cannot mark a successfully sampled provider stale");
            await using var bounded = new HistoryRecorder(new(Path.Combine(root, "bounded.sqlite")));
            await bounded.Ready;
            var batch = await provider.ReadAsync(CancellationToken.None);
            for (int i = 0; i < 2000; i++) bounded.Record(batch, provider.Definitions);
            await bounded.DisposeAsync();
            check(bounded.Status.DroppedBatches > 0 && bounded.Status.Samples == 1,
                "A saturated bounded archive reports dropped batches and flushes accepted measurements without duplicating them");
        }
        finally { await monitor.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Provider : ITelemetryProvider
    {
        public string SourceId => "history-fixture";
        public IReadOnlyList<MetricDefinition> Definitions => [new(SourceId, "usage", "CPU usage", "%", 100)];
        public int Reads; public bool Fail;
        public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
        {
            Interlocked.Increment(ref Reads); if (Fail) throw new IOException("fixture disconnected");
            var device = new DeviceIdentity(SourceId, "cpu-stable", DeviceKind.Cpu, "Fixture CPU", "0");
            return Task.FromResult(WindowsSystemCollector.Batch([device], [new(device, "usage", DateTimeOffset.UtcNow, 25.125)]));
        }
    }
}
