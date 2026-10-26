using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class GpuOverviewCard : Border
{
    private readonly TextBlock title = OverviewTheme.Text("", 18), model = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock usageValue = OverviewTheme.Text("N/A", 20, OverviewTheme.Green), memoryValue = OverviewTheme.Text("N/A", 17, OverviewTheme.Blue);
    private readonly TextBlock power = OverviewTheme.Text("N/A", 15), temp = OverviewTheme.Text("N/A", 15), fan = OverviewTheme.Text("N/A", 15);
    private readonly TextBlock details = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    public TelemetryPlot Usage { get; } = new("%", 100) { Height = 165 };
    public TelemetryPlot Memory { get; } = new(" GiB") { Height = 165 };
    public IEnumerable<TelemetryPlot> Plots => new[] { Usage, Memory };

    public GpuOverviewCard()
    {
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(16); Margin = new(0, 0, 14, 14);
        var content = new StackPanel(); Child = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) };
        var dot = OverviewTheme.Text("●", 16, OverviewTheme.Green); dot.Margin = new(0, 0, 8, 0); heading.Children.Add(dot);
        DockPanel.SetDock(model, Dock.Right); heading.Children.Add(model); heading.Children.Add(title); content.Children.Add(heading);
        content.Children.Add(MetricHeading("Utilization", usageValue)); content.Children.Add(Usage);
        content.Children.Add(MetricHeading("VRAM", memoryValue)); content.Children.Add(Memory);
        var footer = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new(0, 6, 0, 0) };
        foreach (var pair in new[] { ("Power", power), ("Core temperature", temp), ("Fan", fan) })
        {
            var column = new StackPanel(); column.Children.Add(OverviewTheme.Text(pair.Item1, 12, OverviewTheme.Muted));
            pair.Item2.Margin = new(0, 5, 0, 0); column.Children.Add(pair.Item2); footer.Children.Add(column);
        }
        content.Children.Add(footer); details.Margin = new(0, 12, 0, 0); details.TextWrapping = TextWrapping.Wrap;
        details.Visibility = Visibility.Collapsed; content.Children.Add(details);
        OverviewTheme.PlotMenu(Usage, "%", "gpu-utilization"); OverviewTheme.PlotMenu(Memory, "MiB", "gpu-memory");
    }
    private static DockPanel MetricHeading(string label, TextBlock value)
    {
        var row = new DockPanel(); DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
        row.Children.Add(OverviewTheme.Text(label, 13, OverviewTheme.Muted)); return row;
    }
    public void Update(GpuReading gpu, ProviderSnapshot session, int minutes, DateTimeOffset now, bool showDetails)
    {
        title.Text = $"GPU {gpu.Index}"; model.Text = gpu.Name.Replace("NVIDIA GeForce ", "");
        static string Value(double? value, string unit) => value.HasValue ? $"{value:0.#}{unit}" : "N/A";
        usageValue.Text = Value(gpu.Metrics["utilization.gpu"], "%");
        memoryValue.Text = $"{Value(gpu.Metrics["memory.used"] / 1024, "")} / {Value(gpu.Metrics["memory.total"] / 1024, " GiB")}";
        power.Text = Value(gpu.Metrics["power.draw"], " W"); temp.Text = Value(gpu.Metrics["temperature.gpu"], " °C"); fan.Text = Value(gpu.Metrics["fan.speed"], "%");
        if (Usage.SeriesCount == 0) { Usage.SetSeries(new[] { ($"GPU {gpu.Index}", "#33DF32") }); Memory.SetSeries(new[] { ($"GPU {gpu.Index}", "#2B82F8") }); }
        Usage.SetName(0, $"GPU {gpu.Index}"); Memory.SetName(0, $"GPU {gpu.Index}");
        Memory.SetMaximum(gpu.Metrics["memory.total"] / 1024);
        Usage.Update(new[] { session.Series(gpu.Id, "utilization.gpu") }, now, minutes);
        Memory.Update(new[] { session.Series(gpu.Id, "memory.used") }, now, minutes, 1024);
        details.Text = $"{gpu.Id} · PCI {gpu.Bus}\nGraphics {Value(gpu.Metrics["clocks.current.graphics"], " MHz")} · Memory {Value(gpu.Metrics["clocks.current.memory"], " MHz")}";
        details.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
    }
}
