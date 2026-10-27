namespace GpuMonitor;

/// <summary>One NVIDIA compute-process inventory scoped to a stable GPU UUID.</summary>
public sealed record GpuProcessEntry(int Pid, string Name, double? VramMiB);
public sealed record GpuProcessOverview(string Status, IReadOnlyList<GpuProcessEntry> Processes);

public static class GpuProcessProjection
{
    public static GpuProcessOverview ForGpu(string gpuId, IReadOnlyList<ProviderSnapshot> sources)
    {
        if (!gpuId.StartsWith("GPU-", StringComparison.Ordinal))
            return new("Choose a GPU sensor to inspect GPU processes.", []);
        var source = sources.FirstOrDefault(s => s.Source == "nvidia-processes");
        if (source == null) return new("GPU process collection is not enabled.", []);
        if (source.Health == ProviderHealth.Starting || source.Current == null)
            return new("Waiting for GPU process collection.", []);
        if (source.Health == ProviderHealth.Stale)
            return new("GPU process data is stale or unavailable; cannot show a live inventory.", []);

        string prefix = gpuId + "/";
        var samples = source.Current.Samples.Where(s => s.MetricKey == "process.gpu_memory")
            .ToDictionary(s => s.Device.Id, StringComparer.Ordinal);
        var processes = source.Current.Devices
            .Where(d => d.Kind == DeviceKind.Process && d.Id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(d =>
            {
                bool validPid = int.TryParse(d.DisplayIndex, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int pid) && pid > 0;
                samples.TryGetValue(d.Id, out var sample);
                return validPid ? new GpuProcessEntry(pid, string.IsNullOrWhiteSpace(d.Name) ? "Unknown process" : d.Name,
                    sample?.Value) : null;
            })
            .OfType<GpuProcessEntry>()
            .OrderByDescending(p => p.VramMiB ?? -1).ThenBy(p => p.Pid)
            .ToArray();
        return processes.Length == 0
            ? new("No NVIDIA compute processes reported. Graphics processes may not be included.", [])
            : new("NVIDIA compute processes only · per-process GPU utilization is unavailable.", Array.AsReadOnly(processes));
    }
}
