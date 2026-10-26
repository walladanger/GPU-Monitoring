using GpuMonitor;

internal static class TrayReadingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var reading = CsvTelemetry.Parse("0, Fixture GPU, GPU-stable, bus, 24576, 42.125, 8192, 65, 300, 62, 1500, 9750", now);
        var batch = NvidiaTelemetry.Normalize(reading);
        ProviderSnapshot[] sources = [new(NvidiaTelemetry.SourceId, ProviderHealth.Live, null, now, batch, [], NvidiaTelemetry.Definitions)];
        var memory = new WorkspaceSeries(NvidiaTelemetry.SourceId, "GPU-stable", "memory.used");
        var projected = TrayReadings.Project(sources, [memory]);
        check(projected.Single() is { Value: 8192, Unit: "MiB", Time: var time, DisplayValue: "8 GiB", State: WorkspaceBindingState.Available } && time == now,
            "Tray quick-view formats real VRAM while retaining raw units/value and the actual sample timestamp");
        var stale = TrayReadings.Project([sources[0] with { Health = ProviderHealth.Stale }], [memory]).Single();
        check(stale.State == WorkspaceBindingState.SourceStale && stale.Value == 8192 && stale.Time == now,
            "A stale tray reading retains its last measured value and timestamp with explicit stale health");
        check(TrayReadings.Project([], [memory]).Single() is { State: WorkspaceBindingState.SourceMissing, Value: null, DisplayValue: "N/A" } &&
            TrayReadings.Project(sources, [memory with { DeviceId = "GPU-missing" }]).Single().State == WorkspaceBindingState.DeviceMissing,
            "Disconnected tray selections remain unavailable without being rebound to another GPU");
        check(TrayReadings.Project([], null).Count == 0 && TrayReadings.Project(sources, []).Count == 0 && TrayReadings.Project(sources, null).Count == 4,
            "Automatic tray defaults require actual GPU capabilities and an explicit empty selection stays empty");
        var placed = TrayReadings.Place(-10, 1070, 380, 540, new(-1920, 0, 1920, 1040));
        check(placed.Left == -380 && placed.Top == 500 && placed.Left + placed.Width <= 0 && placed.Top + placed.Height <= 1040,
            "Tray placement clamps to a negative-coordinate monitor's working area without covering the taskbar");
        var small = TrayReadings.Place(0, 0, 380, 540, new(0, 20, 320, 300));
        check(small == new TrayRectangle(0, 20, 320, 300), "Tray placement bounds oversized popovers on small/scaled working areas");
    }
}
