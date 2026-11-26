namespace GpuMonitor;

public sealed record WorkspaceMetricChoice(WorkspaceSeries Selection, string Group, string DeviceName,
    string Title, string Unit, WorkspaceBindingState State)
{
    public string Label => $"{Group} · {DeviceName} · {Title} [{Unit}]";
}
public sealed record WorkspaceChartSeries(WorkspaceSeries Selection, string Name, WorkspaceBindingState State,
    IReadOnlyList<TelemetrySample> Samples);
public sealed record WorkspaceAxis(int Index, string RawUnit)
{
    public string DisplayUnit => RawUnit is "B" or "MiB" ? " GiB" : RawUnit == "%" ? "%" : " " + RawUnit;
    public double Divisor => RawUnit == "B" ? 1073741824 : RawUnit == "MiB" ? 1024 : 1;
    public double? MaximumOverride { get; init; }
    public double? Maximum => MaximumOverride ?? (RawUnit == "%" ? 100 : null);
}
public sealed record WorkspaceChartData(string Unit, string? Error, IReadOnlyList<WorkspaceChartSeries> Series,
    IReadOnlyList<WorkspaceAxis> Axes);

/// <summary>Presentation data comes from advertised per-device capabilities, never a synthetic sensor list.</summary>
public static class WorkspaceMetrics
{
    public static IReadOnlyList<WorkspaceMetricChoice> Catalog(IReadOnlyList<ProviderSnapshot> sources)
    {
        var result = new List<WorkspaceMetricChoice>();
        foreach (var source in sources)
        {
            if (source.Current == null) continue;
            var devices = source.Current.Devices.ToDictionary(d => d.Id);
            var definitions = source.Definitions.ToDictionary(d => d.Key);
            foreach (var capability in source.Current.Capabilities)
            {
                if (!devices.TryGetValue(capability.Device.Id, out var device) || !definitions.TryGetValue(capability.MetricKey, out var metric)) continue;
                string group = source.Source.StartsWith("agui:", StringComparison.Ordinal) ? "Agents" : device.Kind switch
                { DeviceKind.Gpu => "GPU", DeviceKind.Cpu => "CPU", DeviceKind.Memory => "RAM", DeviceKind.Disk => "Drives",
                    DeviceKind.Network => "Network", DeviceKind.Process => "Processes", DeviceKind.Runtime => "LLM", _ => "Other" };
                var state = source.Health == ProviderHealth.Stale ? WorkspaceBindingState.SourceStale :
                    capability.Availability == MetricAvailability.Available ? WorkspaceBindingState.Available : WorkspaceBindingState.Unavailable;
                result.Add(new(new(source.Source, device.Id, metric.Key), group, DeviceLabel(device), metric.Title, metric.Unit, state));
            }
        }
        return Array.AsReadOnly(result.DistinctBy(c => c.Selection).OrderBy(c => c.Group, StringComparer.Ordinal)
            .ThenBy(c => c.DeviceName, StringComparer.Ordinal).ThenBy(c => c.Title, StringComparer.Ordinal).ToArray());
    }

    public static IReadOnlyList<WorkspaceMetricChoice> Search(IReadOnlyList<WorkspaceMetricChoice> choices, string query) =>
        Array.AsReadOnly(choices.Where(c => (c.Label + " " + c.Selection.Source + " " + c.Selection.MetricKey + " " + c.Selection.DeviceId)
            .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray());

    public static WorkspaceChartData Project(WorkspacePanel panel, IReadOnlyList<ProviderSnapshot> snapshots)
    {
        var resolved = WorkspaceResolver.Resolve(new(1, "Chart projection", [panel]), snapshots).Single();
        var sources = snapshots.ToDictionary(s => s.Source);
        var series = resolved.Series.Select(binding =>
        {
            sources.TryGetValue(binding.Selection.Source, out var source);
            var device = source?.Current?.Devices.FirstOrDefault(d => d.Id == binding.Selection.DeviceId);
            var definition = source?.Definitions.FirstOrDefault(d => d.Key == binding.Selection.MetricKey);
            string label = (device == null ? binding.Selection.DeviceId : DeviceLabel(device)) + " · " + (definition?.Title ?? binding.Selection.MetricKey);
            var history = binding.State is WorkspaceBindingState.Available or WorkspaceBindingState.Unavailable or WorkspaceBindingState.SourceStale
                ? source?.Series(binding.Selection.DeviceId, binding.Selection.MetricKey) ?? [] : [];
            return new WorkspaceChartSeries(binding.Selection, label, binding.State, history);
        }).ToArray();
        var axes = resolved.Series.GroupBy(s => s.Selection.Axis).OrderBy(g => g.Key)
            .Select(g => new WorkspaceAxis(g.Key, g.FirstOrDefault(s => s.Unit != null)?.Unit ?? "")).ToArray();
        return new(axes.FirstOrDefault(a => a.Index == 0)?.RawUnit ?? "", resolved.AxisError,
            Array.AsReadOnly(series), Array.AsReadOnly(axes));
    }

    /// <summary>Assigns new unit groups to free axes while preserving saved selections' axis choices.</summary>
    public static IReadOnlyList<WorkspaceSeries> AssignAxes(IReadOnlyList<WorkspaceMetricChoice> choices,
        IReadOnlyList<WorkspaceSeries> existing)
    {
        var units = new Dictionary<int, string>();
        var result = new List<WorkspaceSeries>();
        foreach (var choice in choices.OrderByDescending(c => existing.Any(s => Same(s, c.Selection))))
        {
            var saved = existing.FirstOrDefault(s => Same(s, choice.Selection));
            int axis;
            if (saved != null) axis = saved.Axis;
            else if (units.Any(p => p.Value == choice.Unit)) axis = units.First(p => p.Value == choice.Unit).Key;
            else if (units.Count < 8) axis = Enumerable.Range(0, 8).First(i => !units.ContainsKey(i));
            else throw new FormatException("At most eight different unit groups can share a panel.");
            if (units.TryGetValue(axis, out var unit) && unit.Length > 0 && choice.Unit.Length > 0 && unit != choice.Unit)
                throw new FormatException("Saved sensors have incompatible units on the same axis. Remove a conflicting sensor first.");
            if (!units.ContainsKey(axis) || choice.Unit.Length > 0) units[axis] = choice.Unit;
            result.Add(choice.Selection with { Axis = axis });
        }
        return choices.Select(c => result.Single(s => Same(s, c.Selection))).ToArray();
    }
    private static bool Same(WorkspaceSeries a, WorkspaceSeries b) => a.Source == b.Source && a.DeviceId == b.DeviceId && a.MetricKey == b.MetricKey;

    public static string StateLabel(WorkspaceBindingState state) => state switch
    {
        WorkspaceBindingState.Available => "Live", WorkspaceBindingState.Unavailable => "N/A",
        WorkspaceBindingState.SourceStale => "Source stale", WorkspaceBindingState.SourceMissing => "Source missing",
        WorkspaceBindingState.SourceStarting => "Source starting", WorkspaceBindingState.DeviceMissing => "Device missing", _ => "Metric missing"
    };
    private static string DeviceLabel(DeviceIdentity device) => device.Kind == DeviceKind.Gpu ? $"GPU {device.DisplayIndex} · {device.Name}" : device.Name;
}
