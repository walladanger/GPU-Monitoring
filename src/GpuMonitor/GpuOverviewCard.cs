using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class GpuOverviewCard : Border
{
    private readonly TextBlock title = OverviewTheme.Text("", 18), model = OverviewTheme.Text("", 13, OverviewTheme.Muted);
    private readonly TextBlock power = OverviewTheme.Text("N/A", 15), temp = OverviewTheme.Text("N/A", 15), fan = OverviewTheme.Text("N/A", 15);
    private readonly TextBlock details = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    // A GPU overview surface starts with one configurable chart. Its Options menu
    // can switch to memory or add other sensors without creating a second window.
    public TelemetryPlot Usage { get; } = new("%", 100) { Height = 128 };
    public IEnumerable<TelemetryPlot> Plots => [Usage];

    private readonly GpuSensorPanel sensorPanel;
    internal void ResizePlots(double? height) =>
        sensorPanel.SetViewHeight(height.HasValue ? Math.Max(100, height.Value - 110) : null);
    public void CloseDetached() => sensorPanel.CloseDetached();
    public GpuOverviewCard(GpuPanelSettings? sensorSettings = null, Action? save = null)
    {
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(16); Margin = new(0, 0, 14, 14);
        title.FontWeight = FontWeights.SemiBold;
        var content = new StackPanel(); Child = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) };
        var dot = OverviewTheme.Text("●", 16, OverviewTheme.Green); dot.Margin = new(0, 0, 8, 0); heading.Children.Add(dot);
        DockPanel.SetDock(model, Dock.Right); heading.Children.Add(model); heading.Children.Add(title); content.Children.Add(heading);
        sensorPanel = new(Usage, sensorSettings ?? new(), save ?? (() => {}));
        content.Children.Add(sensorPanel);
        var footer = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new(0, 6, 0, 0) };
        foreach (var pair in new[] { ("Power", power), ("Core temperature", temp), ("Fan", fan) })
        {
            var column = new StackPanel(); column.Children.Add(OverviewTheme.Text(pair.Item1 == "Core temperature" ? "Temp" : pair.Item1, 12, OverviewTheme.Muted));
            pair.Item2.Margin = new(0, 5, 0, 0); column.Children.Add(pair.Item2); footer.Children.Add(column);
        }
        content.Children.Add(footer); details.Margin = new(0, 12, 0, 0); details.TextWrapping = TextWrapping.Wrap;
        details.Visibility = Visibility.Collapsed; content.Children.Add(details);
        OverviewTheme.PlotMenu(Usage, "%", "gpu-utilization");
    }
    public void Update(GpuReading gpu, ProviderSnapshot session, int minutes, DateTimeOffset now, bool showDetails, IReadOnlyList<ProviderSnapshot>? sources = null)
    {
        title.Text = $"GPU {gpu.Index}"; model.Text = gpu.Name;
        static string Value(double? value, string unit) => value.HasValue ? $"{value:0.#}{unit}" : "N/A";
        power.Text = Value(gpu.Metrics["power.draw"], " W"); temp.Text = Value(gpu.Metrics["temperature.gpu"], " °C"); fan.Text = Value(gpu.Metrics["fan.speed"], "%");
        sensorPanel.Update(gpu, session, sources ?? [], now);
        details.Text = $"{gpu.Id} · PCI {gpu.Bus}\nGraphics {Value(gpu.Metrics["clocks.current.graphics"], " MHz")} · Memory {Value(gpu.Metrics["clocks.current.memory"], " MHz")}";
        details.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
    }
}
