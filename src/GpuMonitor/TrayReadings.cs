using System.Globalization;

namespace GpuMonitor;

public sealed record TrayReading(WorkspaceSeries Selection, string Label, string Unit, double? Value, DateTimeOffset? Time, WorkspaceBindingState State)
{
    public string DisplayValue => Value is not double value ? "N/A" : Unit switch
    {
        "B" => (value / 1073741824).ToString("0.##", CultureInfo.InvariantCulture) + " GiB",
        "MiB" => (value / 1024).ToString("0.##", CultureInfo.InvariantCulture) + " GiB",
        "B/s" => (value / 1048576).ToString("0.##", CultureInfo.InvariantCulture) + " MiB/s",
        "%" => value.ToString("0.##", CultureInfo.InvariantCulture) + "%",
        _ => value.ToString("0.##", CultureInfo.InvariantCulture) + " " + Unit
    };
}
public sealed record TrayRectangle(double Left, double Top, double Width, double Height)
{
    public bool Contains(double x, double y, double margin = 0) => x >= Left - margin && x <= Left + Width + margin && y >= Top - margin && y <= Top + Height + margin;
}
public static class TrayReadings
{
    public static IReadOnlyList<TrayReading> Project(IReadOnlyList<ProviderSnapshot> sources, IReadOnlyList<WorkspaceSeries>? selected)
    {
        var catalog = WorkspaceMetrics.Catalog(sources);
        var choices = selected ?? catalog.Where(c => c.Group == "GPU" && c.Selection.MetricKey is "utilization.gpu" or "memory.used" or "temperature.gpu" or "power.draw")
            .Take(8).Select(c => c.Selection).ToArray();
        var unique = choices.Select(s => s with { Axis = 0 }).Distinct().Take(8).ToArray();
        if (unique.Length == 0) return [];
        var resolved = WorkspaceResolver.Resolve(new(1, "Tray readings", [new("tray", "Tray", 1, 1, "line", unique)]), sources).Single();
        return resolved.Series.Select(binding =>
        {
            var source = sources.SingleOrDefault(s => s.Source == binding.Selection.Source);
            var sample = source?.Current?.Samples.LastOrDefault(s => s.Device.Id == binding.Selection.DeviceId && s.MetricKey == binding.Selection.MetricKey);
            var choice = catalog.FirstOrDefault(c => Same(c.Selection, binding.Selection));
            string label = choice == null ? binding.Selection.DeviceId + " · " + binding.Selection.MetricKey : choice.DeviceName + " · " + choice.Title;
            return new TrayReading(binding.Selection, label, binding.Unit ?? "", sample?.Value, sample?.Time, binding.State);
        }).ToArray();
    }
    private static bool Same(WorkspaceSeries a, WorkspaceSeries b) => a.Source == b.Source && a.DeviceId == b.DeviceId && a.MetricKey == b.MetricKey;
    /// <summary>Coordinates are in the same native screen space as cursor, window and monitor working area.</summary>
    public static TrayRectangle Place(double anchorX, double anchorY, double width, double height, TrayRectangle work)
    {
        if (new[] { anchorX, anchorY, width, height, work.Left, work.Top, work.Width, work.Height }.Any(v => !double.IsFinite(v)) ||
            width <= 0 || height <= 0 || work.Width <= 0 || work.Height <= 0) throw new ArgumentException("Invalid tray placement bounds.");
        width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
        return new(Math.Clamp(anchorX - width + 20, work.Left, work.Left + work.Width - width),
            Math.Clamp(anchorY - height - 12, work.Top, work.Top + work.Height - height), width, height);
    }
}
