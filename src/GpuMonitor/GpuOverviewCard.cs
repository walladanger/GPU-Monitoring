using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class GpuOverviewCard : Border
{
    private readonly TextBlock title = OverviewTheme.Text("", 18), model = OverviewTheme.Text("", 13, OverviewTheme.Muted);
    private readonly TextBlock usageValue = OverviewTheme.Text("N/A", 20, OverviewTheme.Green), memoryValue = OverviewTheme.Text("N/A", 20, OverviewTheme.Blue);
    private readonly TextBlock memoryAmount = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock power = OverviewTheme.Text("N/A", 15), temp = OverviewTheme.Text("N/A", 15), fan = OverviewTheme.Text("N/A", 15);
    private readonly TextBlock details = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly GpuProcessView processView = new();
    public event Action? OpenWindowRequested;
    public event Action? SelectRequested;
    public TelemetryPlot Usage { get; } = new("%", 100) { Height = 128 };
    public TelemetryPlot Memory { get; } = new(" GiB") { Height = 128 };
    public IEnumerable<TelemetryPlot> Plots => new[] { Usage, Memory };

    public GpuOverviewCard()
    {
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(16); Margin = new(0, 0, 14, 14);
        title.FontWeight = FontWeights.SemiBold;
        var content = new StackPanel(); Child = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) };
        var dot = OverviewTheme.Text("●", 16, OverviewTheme.Green); dot.Margin = new(0, 0, 8, 0); heading.Children.Add(dot);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(model);
        var processes = OverviewTheme.Command("P", processView.Toggle, "Show or hide GPU process details");
        processes.ToolTip = "Expand GPU processes"; processes.Padding = new(7, 3, 7, 3); right.Children.Add(processes);
        var select = OverviewTheme.Command("Select", () => SelectRequested?.Invoke(), "Select sensors for GPU panel");
        select.ToolTip = "Select this GPU's charts and sensors"; select.Padding = new(7, 3, 7, 3); right.Children.Add(select);
        var open = OverviewTheme.Command("↗", () => OpenWindowRequested?.Invoke(), "Open GPU panel in new window");
        open.ToolTip = "Open GPU panel in new window"; open.Padding = new(7, 3, 7, 3); right.Children.Add(open);
        DockPanel.SetDock(right, Dock.Right); heading.Children.Add(right); heading.Children.Add(title); content.Children.Add(heading);
        content.Children.Add(MetricRegion("Utilization", Usage, usageValue));
        var memoryNumbers = new StackPanel(); memoryNumbers.Children.Add(memoryValue); memoryAmount.Margin = new(0, 5, 0, 0); memoryNumbers.Children.Add(memoryAmount);
        content.Children.Add(MetricRegion("Memory", Memory, memoryNumbers));
        var footer = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new(0, 6, 0, 0) };
        foreach (var pair in new[] { ("Power", power), ("Core temperature", temp), ("Fan", fan) })
        {
            var column = new StackPanel(); column.Children.Add(OverviewTheme.Text(pair.Item1 == "Core temperature" ? "Temp" : pair.Item1, 12, OverviewTheme.Muted));
            pair.Item2.Margin = new(0, 5, 0, 0); column.Children.Add(pair.Item2); footer.Children.Add(column);
        }
        content.Children.Add(footer); details.Margin = new(0, 12, 0, 0); details.TextWrapping = TextWrapping.Wrap;
        details.Visibility = Visibility.Collapsed; content.Children.Add(details); content.Children.Add(processView);
        OverviewTheme.PlotMenu(Usage, "%", "gpu-utilization"); OverviewTheme.PlotMenu(Memory, "MiB", "gpu-memory");
    }
    private static UIElement MetricRegion(string label, TelemetryPlot plot, UIElement value)
    {
        var area = new StackPanel { Margin = new(0, 0, 0, 8) };
        area.Children.Add(OverviewTheme.Text(label, 13, OverviewTheme.Muted));
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(90) });
        row.Children.Add(plot);
        var valueHost = new Border { Child = value, Padding = new(10, 12, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(valueHost, 1); row.Children.Add(valueHost); area.Children.Add(row); return area;
    }
    public void Update(GpuReading gpu, ProviderSnapshot session, IReadOnlyList<ProviderSnapshot> sources, int minutes, DateTimeOffset now, bool showDetails)
    {
        title.Text = $"GPU {gpu.Index}"; model.Text = gpu.Name;
        static string Value(double? value, string unit) => value.HasValue ? $"{value:0.#}{unit}" : "N/A";
        usageValue.Text = Value(gpu.Metrics["utilization.gpu"], "%");
        memoryValue.Text = gpu.Metrics["memory.total"] is > 0 ? Value(gpu.Metrics["memory.used"] / gpu.Metrics["memory.total"] * 100, "%") : "N/A";
        memoryAmount.Text = $"{Value(gpu.Metrics["memory.used"] / 1024, "")} / {Value(gpu.Metrics["memory.total"] / 1024, " GiB")}";
        power.Text = Value(gpu.Metrics["power.draw"], " W"); temp.Text = Value(gpu.Metrics["temperature.gpu"], " °C"); fan.Text = Value(gpu.Metrics["fan.speed"], "%");
        if (Usage.SeriesCount == 0) { Usage.SetSeries(new[] { ($"GPU {gpu.Index}", "#33DF32") }); Memory.SetSeries(new[] { ($"GPU {gpu.Index}", "#2B82F8") }); }
        Usage.SetName(0, $"GPU {gpu.Index}"); Memory.SetName(0, $"GPU {gpu.Index}");
        Memory.SetMaximum(gpu.Metrics["memory.total"] / 1024);
        Usage.Update(new[] { session.Series(gpu.Id, "utilization.gpu") }, now, minutes);
        Memory.Update(new[] { session.Series(gpu.Id, "memory.used") }, now, minutes, 1024);
        details.Text = $"{gpu.Id} · PCI {gpu.Bus}\nGraphics {Value(gpu.Metrics["clocks.current.graphics"], " MHz")} · Memory {Value(gpu.Metrics["clocks.current.memory"], " MHz")}";
        details.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        processView.Update(gpu.Id, sources);
        Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
    }
}
