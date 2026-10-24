using GpuMonitor;

internal static class DashboardTelemetryChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var provider = new Provider();
        var monitor = new DashboardTelemetry([provider]);
        check(monitor.Snapshot().Single().Health == ProviderHealth.Starting && provider.Reads == 0,
            "Dashboard engine starts without collecting or inventing measurements");
        await monitor.SetMonitoringAsync(true, TimeSpan.FromMilliseconds(100));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (monitor.Snapshot().Single().Health != ProviderHealth.Live) await Task.Delay(10, deadline.Token);
        await monitor.SetMonitoringAsync(false, TimeSpan.FromMilliseconds(100));
        int paused = provider.Reads;
        var history = monitor.Snapshot().Single().History;
        check(monitor.Snapshot("dashboard-test").Single().Series("cpu", "usage").Count == history.Count &&
            monitor.Snapshot("dashboard-test").Single().Series("missing", "usage").Count == 0,
            "Dashboard chart projection retains only the selected measured device series");
        await Task.Delay(150, deadline.Token);
        check(provider.Reads == paused && history.Count > 0 && monitor.Snapshot().Single().History.Count == history.Count,
            "Dashboard pause drains collection while retaining exact session history");
        await monitor.RefreshAsync();
        check(provider.Reads == paused + 1 && monitor.Snapshot().Single().History.Count == history.Count + 1,
            "Paused dashboard rescan takes one measured snapshot without starting a polling loop");
        provider.Fail = true;
        await monitor.RefreshAsync();
        check(monitor.Snapshot().Single().Health == ProviderHealth.Stale && monitor.Snapshot().Single().Current!.Devices.Count == 1,
            "Dashboard refresh preserves prior inventory and isolates provider failure");
        provider.Fail = false;
        await monitor.SetMonitoringAsync(true, TimeSpan.FromMilliseconds(100));
        while (monitor.Snapshot().Single().Health != ProviderHealth.Live) await Task.Delay(10, deadline.Token);
        await monitor.SetMonitoringAsync(true, TimeSpan.FromMilliseconds(200));
        await monitor.DisposeAsync();
        check(provider.Disposed && provider.Active == 0 && provider.MaximumActive == 1,
            "Dashboard interval changes and disposal cancel reads before disposing providers, without overlapping polls");
        await monitor.DisposeAsync();
        bool disposed = false;
        try { await monitor.RefreshAsync(); } catch (ObjectDisposedException) { disposed = true; }
        check(disposed, "Disposed dashboard monitoring cannot restart or rescan");
    }

    private sealed class Provider : ITelemetryProvider, IDisposable
    {
        public string SourceId => "dashboard-test";
        public IReadOnlyList<MetricDefinition> Definitions { get; } = [new("dashboard-test", "usage", "Usage", "%", 100)];
        public int Reads, Active, MaximumActive;
        public bool Disposed, Fail;
        public async Task<TelemetryBatch> ReadAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Interlocked.Increment(ref Reads);
            int active = Interlocked.Increment(ref Active); MaximumActive = Math.Max(MaximumActive, active);
            try
            {
                await Task.Delay(15, token);
                if (Fail) throw new IOException("fixture offline");
                var device = new DeviceIdentity(SourceId, "cpu", DeviceKind.Cpu, "Fixture CPU", "0");
                return WindowsSystemCollector.Batch([device], [new(device, "usage", DateTimeOffset.UtcNow, 25)]);
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        public void Dispose() => Disposed = true;
    }
}
