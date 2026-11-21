using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

/// <summary>Collapsed by default so the approved card footprint does not change until requested.</summary>
internal sealed class GpuProcessView : Expander
{
    private readonly TextBlock details = OverviewTheme.Text("", 12, OverviewTheme.Muted);

    public GpuProcessView()
    {
        Header = "GPU processes  ▾";
        IsExpanded = false;
        Visibility = Visibility.Collapsed;
        Foreground = Brushes.White;
        Margin = new(0, 10, 0, 0);
        details.TextWrapping = TextWrapping.Wrap;
        details.Margin = new(8, 8, 0, 6);
        Content = details;
        System.Windows.Automation.AutomationProperties.SetName(this, "Expand GPU process usage");
    }

    public bool IsShown
    {
        get => Visibility == Visibility.Visible;
        set
        {
            Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) IsExpanded = true;
        }
    }
    public void Toggle() => IsShown = !IsShown;

    public void Update(string gpuId, IReadOnlyList<ProviderSnapshot> sources)
    {
        var view = GpuProcessProjection.ForGpu(gpuId, sources);
        var rows = view.Processes.Take(64).Select(p =>
            $"PID {p.Pid}  ·  {p.Name}  ·  {(p.VramMiB.HasValue ? p.VramMiB.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MiB VRAM" : "VRAM N/A")}");
        details.Text = view.Status + (view.Processes.Count == 0 ? "" : "\n" + string.Join("\n", rows)) +
            (view.Processes.Count > 64 ? "\nMore than 64 compute processes; showing first 64." : "");
    }
}
