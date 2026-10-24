using GpuMonitor;

internal static class WorkspacePresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var device = new DeviceIdentity("windows-system", "cpu-total", DeviceKind.Cpu, "Fixture CPU", "0");
        var ram = new DeviceIdentity("windows-system", "physical-memory", DeviceKind.Memory, "Fixture RAM", "0");
        MetricDefinition[] definitions = [new("windows-system", "cpu.usage", "CPU usage", "%", 100), new("windows-system", "ram.used", "RAM used", "B")];
        TelemetrySample[] samples = [new(device, "cpu.usage", now, 25.125), new(ram, "ram.used", now, null)];
        var batch = WindowsSystemCollector.Batch([device, ram], samples.ToList());
        ProviderSnapshot[] sources = [new("windows-system", ProviderHealth.Live, null, now, batch, samples, definitions)];
        var catalog = WorkspaceMetrics.Catalog(sources);
        check(catalog.Count == 2 && catalog.All(c => c.Selection.DeviceId != "cpu-total" || c.Selection.MetricKey == "cpu.usage"),
            "Metric picker advertises only capabilities actually belonging to each measured device");
        check(WorkspaceMetrics.Search(catalog, "fixture ram").Single().Selection.DeviceId == "physical-memory" &&
            WorkspaceMetrics.Search(catalog, "CPU").Single().Group == "CPU",
            "Metric picker searches actual device names and metric groups without case sensitivity");
        check(catalog.Single(c => c.Selection.MetricKey == "ram.used").State == WorkspaceBindingState.Unavailable,
            "An unavailable hardware capability remains selectable and labeled unavailable");
        var selection = catalog.Single(c => c.Group == "CPU").Selection;
        var panel = new WorkspacePanel("cpu", "CPU", 1, 2, "line", [selection]);
        var projected = WorkspaceMetrics.Project(panel, sources);
        check(projected.Error == null && projected.Unit == "%" && projected.Series.Single().Samples.Single().Value == 25.125,
            "Custom chart projection preserves measured source values and raw units");
        var missing = WorkspaceMetrics.Project(panel with { Series = [selection with { DeviceId = "absent" }] }, sources);
        check(missing.Series.Single().State == WorkspaceBindingState.DeviceMissing && missing.Series.Single().Samples.Count == 0,
            "Custom panels keep missing bindings without substituting another device or inventing a trace");
        var stale = WorkspaceMetrics.Project(panel, [sources[0] with { Health = ProviderHealth.Stale }]);
        check(stale.Series.Single().State == WorkspaceBindingState.SourceStale && stale.Series.Single().Samples.Count == 1,
            "Stale custom panels retain their historical samples with explicit stale health");
        var mixed = WorkspaceMetrics.Project(panel with { Series = [selection, catalog.Single(c => c.Group == "RAM").Selection] }, sources);
        check(mixed.Error != null, "Custom panels refuse incompatible units on one axis");
        check(WorkspaceMetrics.Project(panel with { Series = [selection with { Axis = 1 }] }, sources).Error != null,
            "Unsupported secondary-axis presets are retained with an honest renderer limitation");
    }
}
