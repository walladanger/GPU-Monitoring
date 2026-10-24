namespace GpuMonitor;

public sealed record WorkspaceMetricChoice(WorkspaceSeries Selection, string Group, string DeviceName,
    string Title, string Unit, WorkspaceBindingState State)
{
    public string Label => $"{Group} · {DeviceName} · {Title} [{Unit}]";
}
public sealed record WorkspaceChartSeries(WorkspaceSeries Selection, string Name, WorkspaceBindingState State,
    IReadOnlyList<TelemetrySample> Samples);
public sealed record WorkspaceChartData(string Unit, string? Error, IReadOnlyList<WorkspaceChartSeries> Series);

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
        return new(resolved.Series.FirstOrDefault(s => s.Unit != null)?.Unit ?? "", resolved.AxisError ??
            (panel.Series.Any(s => s.Axis != 0) ? "Secondary-axis rendering is not available in this build. Saved selections are retained." : null),
            Array.AsReadOnly(series));
    }

    public static string StateLabel(WorkspaceBindingState state) => state switch
    {
        WorkspaceBindingState.Available => "Live", WorkspaceBindingState.Unavailable => "N/A",
        WorkspaceBindingState.SourceStale => "Source stale", WorkspaceBindingState.SourceMissing => "Source missing",
        WorkspaceBindingState.SourceStarting => "Source starting", WorkspaceBindingState.DeviceMissing => "Device missing", _ => "Metric missing"
    };
    private static string DeviceLabel(DeviceIdentity device) => device.Kind == DeviceKind.Gpu ? $"GPU {device.DisplayIndex} · {device.Name}" : device.Name;
}
