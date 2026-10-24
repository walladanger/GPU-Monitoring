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
        var dual = WorkspaceMetrics.Project(panel with { Series = [selection, catalog.Single(c => c.Group == "RAM").Selection with { Axis = 1 }] }, sources);
        check(dual.Error == null && dual.Series.Count == 2 && dual.Series[0].Samples.Single().Value == 25.125,
            "Different units on distinct axes render without changing measured values");
        check(dual.Axes.Count == 2 && dual.Axes[0].RawUnit == "%" && dual.Axes[0].Maximum == 100 &&
            dual.Axes[1].RawUnit == "B" && dual.Axes[1].Divisor == 1073741824 && dual.Axes[1].DisplayUnit.Trim() == "GiB",
            "Each comparison axis carries its own raw unit, scale and display conversion");
        var rightOnly = WorkspaceMetrics.Project(panel with { Series = [selection with { Axis = 1 }] }, sources);
        check(rightOnly.Error == null && rightOnly.Axes.Single().Index == 1,
            "A saved right-only panel retains its axis rather than inventing a left series");
        var assigned = WorkspaceMetrics.AssignAxes(catalog, []);
        check(assigned.Single(s => s.MetricKey == "cpu.usage").Axis == 0 && assigned.Single(s => s.MetricKey == "ram.used").Axis == 1,
            "Selecting two unit groups assigns distinct labeled axes");
        check(WorkspaceMetrics.AssignAxes(catalog, [selection with { Axis = 1 }]).Single(s => s.MetricKey == "ram.used").Axis == 0,
            "Editing preserves a saved right-axis selection and uses the free left axis for a new unit");
        bool rejected = false;
        try { WorkspaceMetrics.AssignAxes(catalog.Concat([catalog[0] with { Selection = selection with { MetricKey = "watts" }, Unit = "W" }]).ToArray(), []); }
        catch (FormatException) { rejected = true; }
        check(rejected, "A third unit requires another panel instead of silently sharing an incompatible axis");
        var unknown = WorkspaceMetrics.Project(panel with { Series = [selection with { Source = "offline", Axis = 1 }] }, []);
        check(unknown.Error == null && unknown.Axes.Single().RawUnit == "" && unknown.Series.Single().Samples.Count == 0,
            "Unknown source units remain unknown without fabricating data or a converted trace");

    }
}
