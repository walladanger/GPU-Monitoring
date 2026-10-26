using GpuMonitor;

internal static class CpuCoreChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        var batch = CpuCoreCollector.Normalize(new Dictionary<string, double?> {
            ["_Total"] = 55, ["0,_Total"] = 50, ["0,0"] = 0,
            ["0,1"] = 81.23456789012345, ["1,0"] = null }, now);
        check(batch.Devices.Count == 3 && batch.Samples.Count == 3 &&
            batch.Samples.Single(s => s.Device.DisplayIndex == "0,1").Value == 81.23456789012345,
            "Logical CPU samples retain exact values and omit group/system aggregate instances");
        check(batch.Devices.Select(d => d.Id).Distinct().Count() == 3 &&
            batch.Devices.Any(d => d.Id == "group-0-cpu-0") && batch.Devices.Any(d => d.Id == "group-1-cpu-0"),
            "Logical CPU identities distinguish the same processor index in different groups");
        check(batch.Samples.Single(s => s.Device.DisplayIndex == "0,0").Value == 0 &&
            batch.Capabilities.Single(c => c.Device.DisplayIndex == "1,0").Availability == MetricAvailability.Unavailable,
            "Measured idle remains zero while unmeasured logical CPU usage stays unavailable");
        check(CpuCoreCollector.Normalize(new Dictionary<string, double?> {
            ["bad"] = 1, ["0,-1"] = 1, ["0,64"] = 1, ["65535,0"] = 1,
            ["0,1,2"] = 1, [" 0,1"] = 1 }, now).Devices.Count == 0,
            "Malformed logical processor identities cannot create phantom CPU series");
        check(CpuCoreCollector.Normalize(new Dictionary<string, double?> {
            ["0,0"] = -1, ["0,1"] = 101, ["0,2"] = double.NaN,
            ["0,3"] = double.PositiveInfinity }, now).Samples.All(s => s.Value == null),
            "Out-of-range and nonfinite CPU percentages remain gaps");
        bool duplicateRejected = false;
        try { CpuCoreCollector.Normalize(new Dictionary<string, double?> { ["0,1"] = 1, ["00,01"] = 2 }, now); }
        catch (ArgumentException) { duplicateRejected = true; }
        check(duplicateRejected, "Equivalent CPU instance names cannot double count a logical processor");
        using var provider = new CpuCoreCollector();
        var session = new TelemetrySession(provider);
        session.Apply(batch, now);
        session.Apply(CpuCoreCollector.Normalize(new Dictionary<string, double?> { ["0,1"] = 25 }, now.AddSeconds(1)), now.AddSeconds(1));
        check(session.HistorySnapshot().Count == 2 && session.Current!.Devices.Single().Id == "group-0-cpu-1",
            "Logical CPU removal drops only removed histories and preserves a surviving identity");
        using (var defaults = BackendProviderSet.Create(new() { CollectProcesses = false }))
            check(defaults.Providers.All(p => p.SourceId != "windows-cpu-cores"), "Per-logical-CPU polling remains disabled by default");
        using (var enabled = BackendProviderSet.Create(new() { CollectProcesses = false, CollectCpuCores = true }))
            check(enabled.Providers.Count(p => p.SourceId == "windows-cpu-cores") == 1,
                "Explicit configuration registers one independent logical CPU provider");
        check(CpuCoreCollector.Normalize(new Dictionary<string, double?>(), now).Devices.Count == 0,
            "CPU counter warmup does not invent logical processor inventory");
        using (var cancelledProvider = new CpuCoreCollector())
        {
            bool cancelled = false;
            try { await cancelledProvider.ReadAsync(new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Logical CPU cancellation is checked before native query creation");
        }
        if (OperatingSystem.IsWindows())
        {
            await provider.ReadAsync(CancellationToken.None);
            await Task.Delay(1100);
            var live = await provider.ReadAsync(CancellationToken.None);
            check(live.Devices.Count > 0 && live.Samples.Count == live.Devices.Count &&
                live.Samples.All(s => s.Value is >= 0 and <= 100),
                "Real Windows logical CPU counters produce measured percentages after warmup");
        }
        if (!OperatingSystem.IsWindows())
        {
            bool unsupported = false;
            try { await provider.ReadAsync(CancellationToken.None); } catch (PlatformNotSupportedException) { unsupported = true; }
            check(unsupported, "Logical CPU native collection explicitly requires Windows");
        }
        provider.Dispose();
        bool disposed = false;
        try { await provider.ReadAsync(CancellationToken.None); } catch (ObjectDisposedException) { disposed = true; }
        check(disposed, "Disposed logical CPU collection cannot reopen a native query");
    }
}
