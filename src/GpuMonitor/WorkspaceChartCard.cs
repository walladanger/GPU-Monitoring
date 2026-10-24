using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class WorkspaceChartCard : Border
{
    private readonly StackPanel content = new(), legend = new();
    private readonly TextBlock state = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private TelemetryPlot? plot;
    private string? unit;
    private static readonly string[] Colors = ["#33DF32", "#2B82F8", "#A66CF2", "#FFBA4D", "#39BCD2", "#E977BD"];
    public WorkspacePanel Specification { get; }
    public TelemetryPlot? Plot => plot;
    public event Action<DateTimeOffset?>? CursorMoved;

    public WorkspaceChartCard(WorkspacePanel panel, Action openOptions)
    {
        Specification = panel;
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(16); Margin = new(0, 0, 14, 14); Child = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 10) };
        var options = OverviewTheme.Command("⋮", openOptions, "Options for " + panel.Title);
        DockPanel.SetDock(options, Dock.Right); heading.Children.Add(options);
        var title = OverviewTheme.Text(panel.Title, 16); title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = panel.Title; heading.Children.Add(title); content.Children.Add(heading);
        state.TextWrapping = TextWrapping.Wrap; state.Margin = new(0, 0, 0, 8); content.Children.Add(state); content.Children.Add(legend);
    }

    public void Refresh(IReadOnlyList<ProviderSnapshot> sources, DateTimeOffset now, int minutes)
    {
        var data = WorkspaceMetrics.Project(Specification, sources);
        state.Text = data.Error ?? string.Join(" · ", data.Series.Select(s => WorkspaceMetrics.StateLabel(s.State)).Distinct());
        if (data.Error != null)
        {
            if (plot != null) { plot.SetCursor(null); plot.Visibility = Visibility.Collapsed; }
            legend.Children.Clear(); return;
        }
        if (plot == null || unit != data.Unit)
        {
            if (plot != null) content.Children.Remove(plot);
            unit = data.Unit;
            string displayUnit = unit is "B" or "MiB" ? " GiB" : unit == "%" ? "%" : " " + unit;
            plot = new(displayUnit, unit == "%" ? 100 : null) { Height = Math.Max(160, Specification.HeightRows * 100) };
            plot.SetSeries(data.Series.Select((s, i) => (s.Name, Colors[i % Colors.Length])).ToArray(), Specification.Chart == "area");
            plot.CursorMoved += time => CursorMoved?.Invoke(time);
            OverviewTheme.PlotMenu(plot, unit, Specification.Id); content.Children.Insert(2, plot);
        }
        plot.Visibility = Visibility.Visible;
        for (int i = 0; i < data.Series.Count; i++) plot.SetName(i, data.Series[i].Name);
        plot.Update(data.Series.Select(s => s.Samples).ToArray(), now, minutes, unit == "B" ? 1073741824 : unit == "MiB" ? 1024 : 1);
        legend.Children.Clear();
        foreach (var pair in data.Series.Select((series, index) => (series, index)))
        {
            var label = OverviewTheme.Text($"● {pair.series.Name} · {WorkspaceMetrics.StateLabel(pair.series.State)}", 11, OverviewTheme.Brush(Colors[pair.index % Colors.Length]));
            label.TextWrapping = TextWrapping.Wrap; label.Margin = new(0, 4, 0, 0);
            label.ToolTip = $"{pair.series.Selection.Source}\n{pair.series.Selection.DeviceId}\n{pair.series.Selection.MetricKey} [{data.Unit}]";
            legend.Children.Add(label);
        }
    }
}
