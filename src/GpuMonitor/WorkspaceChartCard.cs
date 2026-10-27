using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class WorkspaceChartCard : Border
{
    private readonly StackPanel content = new(), legend = new();
    private readonly TextBlock state = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly GpuProcessView processView = new();
    private TelemetryPlot? plot;
    private string? axisKey;
    private WorkspacePanel? previewSize;
    private static readonly string[] Colors = ["#33DF32", "#2B82F8", "#A66CF2", "#FFBA4D", "#39BCD2", "#E977BD"];
    public WorkspacePanel Specification { get; }
    public TelemetryPlot? Plot => plot;
    internal Thumb TitleHandle { get; }
    internal Thumb ResizeGrip { get; }
    internal int LayoutWidthSlots => previewSize?.WidthSlots ?? Specification.WidthSlots;
    public event Action<DateTimeOffset?>? CursorMoved;

    public WorkspaceChartCard(WorkspacePanel panel, Action openOptions, Action? openSelection = null, Action? openWindow = null)
    {
        Specification = panel;
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(16); Margin = new(0, 0, 14, 14); Child = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var open = OverviewTheme.Command("↗", openWindow ?? (() => { }), "Open " + panel.Title + " in new window");
        open.IsEnabled = openWindow != null;
        open.ToolTip = "Open in new window"; open.Padding = new(8, 4, 8, 4); actions.Children.Add(open);
        var select = OverviewTheme.Command("Select", openSelection ?? (() => { }), "Select graph and sensors for " + panel.Title);
        select.IsEnabled = openSelection != null;
        select.ToolTip = "Choose graph type and sensors"; select.Padding = new(8, 4, 8, 4); actions.Children.Add(select);
        var options = OverviewTheme.Command("⋮", openOptions, "Options for " + panel.Title);
        actions.Children.Add(options); DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
        TitleHandle = Handle(panel.Title, 16, OverviewTheme.Text("", 16).Foreground, Cursors.SizeAll);
        TitleHandle.ToolTip = "Drag this title onto another panel to reorder. " + panel.Title;
        System.Windows.Automation.AutomationProperties.SetName(TitleHandle, "Move " + panel.Title);
        heading.Children.Add(TitleHandle); content.Children.Add(heading);
        state.TextWrapping = TextWrapping.Wrap; state.Margin = new(0, 0, 0, 8); content.Children.Add(state); content.Children.Add(legend); content.Children.Add(processView);
        ResizeGrip = Handle("◢", 14, OverviewTheme.Muted, Cursors.SizeNWSE);
        ResizeGrip.Width = 20; ResizeGrip.Height = 18; ResizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
        ResizeGrip.ToolTip = "Drag to resize; dimensions snap to slots and rows. Escape cancels.";
        System.Windows.Automation.AutomationProperties.SetName(ResizeGrip, "Resize " + panel.Title); content.Children.Add(ResizeGrip);
    }

    public void Refresh(IReadOnlyList<ProviderSnapshot> sources, DateTimeOffset now, int minutes)
    {
        var gpu = Specification.Series.FirstOrDefault(s => s.Source == NvidiaTelemetry.SourceId && s.DeviceId.StartsWith("GPU-", StringComparison.Ordinal));
        processView.Visibility = gpu == null ? Visibility.Collapsed : Visibility.Visible;
        if (gpu != null) processView.Update(gpu.DeviceId, sources);
        var data = WorkspaceMetrics.Project(Specification, sources);
        state.Text = data.Error ?? string.Join(" · ", data.Series.Select(s => WorkspaceMetrics.StateLabel(s.State)).Distinct());
        if (data.Error != null)
        {
            if (plot != null) { plot.SetCursor(null); plot.Visibility = Visibility.Collapsed; }
            legend.Children.Clear(); return;
        }
        string nextAxisKey = string.Join("|", data.Axes.Select(a => $"{a.Index}:{a.RawUnit}"));
        if (plot == null || axisKey != nextAxisKey)
        {
            if (plot != null) content.Children.Remove(plot);
            axisKey = nextAxisKey;
            plot = new(data.Axes[0].DisplayUnit, data.Axes[0].Maximum) { Height = Math.Max(160, (previewSize?.HeightRows ?? Specification.HeightRows) * 100) };
            plot.SetSeries(data.Series.Select((s, i) => (s.Name, Colors[i % Colors.Length])).ToArray(), Specification.Chart == "area");
            plot.ConfigureAxes(data.Axes, data.Series.Select(s => s.Selection.Axis).ToArray());
            plot.CursorMoved += time => CursorMoved?.Invoke(time);
            OverviewTheme.PlotMenu(plot, data.Unit, Specification.Id); content.Children.Insert(2, plot);
        }
        plot.Visibility = Visibility.Visible;
        for (int i = 0; i < data.Series.Count; i++) plot.SetName(i, data.Series[i].Name);
        plot.Update(data.Series.Select(s => s.Samples).ToArray(), now, minutes);
        legend.Children.Clear();
        foreach (var pair in data.Series.Select((series, index) => (series, index)))
        {
            var axis = data.Axes.Single(a => a.Index == pair.series.Selection.Axis);
            string axisLabel = data.Axes.Count > 1 || axis.Index == 1 ? $" · {(axis.Index == 0 ? "Left" : "Right")} [{axis.RawUnit}]" : "";
            var label = OverviewTheme.Text($"● {pair.series.Name}{axisLabel} · {WorkspaceMetrics.StateLabel(pair.series.State)}", 11, OverviewTheme.Brush(Colors[pair.index % Colors.Length]));
            label.TextWrapping = TextWrapping.Wrap; label.Margin = new(0, 4, 0, 0);
            label.ToolTip = $"{pair.series.Selection.Source}\n{pair.series.Selection.DeviceId}\n{pair.series.Selection.MetricKey} [{axis.RawUnit}]";
            legend.Children.Add(label);
        }
    }

    internal bool ProcessExpanded { get => processView.IsExpanded; set => processView.IsExpanded = value; }

    internal void PreviewSize(WorkspacePanel? dimensions)
    {
        previewSize = dimensions;
        if (plot != null) plot.Height = Math.Max(160, (dimensions?.HeightRows ?? Specification.HeightRows) * 100);
        ResizeGrip.ToolTip = dimensions == null ? "Drag to resize; dimensions snap to slots and rows. Escape cancels." :
            $"{dimensions.WidthSlots} slot(s) × {dimensions.HeightRows} row(s). Escape cancels.";
    }
    private static Thumb Handle(string text, double fontSize, Brush foreground, Cursor cursor)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.TextProperty, text); label.SetValue(TextBlock.FontSizeProperty, fontSize);
        label.SetValue(TextBlock.ForegroundProperty, foreground); label.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        return new Thumb { Cursor = cursor, Focusable = true, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = label } };
    }
}
