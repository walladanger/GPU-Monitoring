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
    private TelemetryPlot? plot;
    private string? axisKey;
    private readonly Action openOptions;
    private Window? detachedWindow;
    private WorkspaceChartCard? detachedCard;
    private IReadOnlyList<ProviderSnapshot> latest = [];
    private DateTimeOffset latestTime;
    private int latestMinutes;
    private readonly TextBlock processText = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly Expander processSection;
    internal void CloseDetached() => detachedWindow?.Close();
    internal void OpenDetached()
    {
        if (detachedWindow != null) { detachedWindow.Activate(); return; }
        detachedCard = new(Specification, openOptions);
        detachedWindow = new Window { Title = Specification.Title, Width = 720, Height = 600, MinWidth = 360, MinHeight = 300,
            Owner = Window.GetWindow(this), Background = OverviewTheme.Canvas,
            Content = new ScrollViewer { Content = detachedCard, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
        detachedWindow.SizeChanged += (_, _) => { if (detachedCard != null && detachedWindow != null) detachedCard.Width = Math.Max(320, detachedWindow.ActualWidth - 32); };
        detachedWindow.Closed += (_, _) => { detachedCard?.CloseDetached(); detachedCard = null; detachedWindow = null; };
        detachedCard.Refresh(latest, latestTime, latestMinutes); detachedWindow.Show();
    }
    private WorkspacePanel? previewSize;
    private static readonly string[] Colors = ["#33DF32", "#2B82F8", "#A66CF2", "#FFBA4D", "#39BCD2", "#E977BD", "#F45A5A", "#DBCE32"];
    public WorkspacePanel Specification { get; }
    public TelemetryPlot? Plot => plot;
    internal Thumb TitleHandle { get; }
    internal Thumb ResizeGrip { get; }
    internal int LayoutWidthSlots => previewSize?.WidthSlots ?? Specification.WidthSlots;

    public WorkspaceChartCard(WorkspacePanel panel, Action openOptions)
    {
        Specification = panel; this.openOptions = openOptions;
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1);
        CornerRadius = new(9); Padding = new(0); Margin = new(0, 0, 14, 14);
        var surface = new Grid(); content.Margin = new(16); surface.Children.Add(content); Child = surface;
        var heading = new DockPanel { Margin = new(0, 0, 0, 10) };
        var options = OverviewTheme.Command("Options", openOptions, "Options for " + panel.Title);
        DockPanel.SetDock(options, Dock.Right); heading.Children.Add(options);
        var open = OverviewTheme.Command("↗ Open", OpenDetached, "Open in new window"); open.ToolTip = "Open in new window";
        DockPanel.SetDock(open, Dock.Right); heading.Children.Add(open);
        TitleHandle = Handle(panel.Title, 16, OverviewTheme.Text("", 16).Foreground, Cursors.SizeAll);
        TitleHandle.ToolTip = "Drag this title onto another panel to reorder. " + panel.Title;
        System.Windows.Automation.AutomationProperties.SetName(TitleHandle, "Move " + panel.Title);
        heading.Children.Add(TitleHandle); content.Children.Add(heading);
        state.TextWrapping = TextWrapping.Wrap; state.Margin = new(0, 0, 0, 8); content.Children.Add(state); content.Children.Add(legend);
        processText.TextWrapping = TextWrapping.Wrap;
        processSection = new Expander { Header = "GPU processes", Content = processText, Foreground = OverviewTheme.Muted };
        content.Children.Add(processSection);
        processSection.Expanded += (_, _) => Refresh(latest, latestTime, latestMinutes);
        var gripTemplate = new FrameworkElementFactory(typeof(Border)); gripTemplate.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        ResizeGrip = new Thumb { Cursor = Cursors.SizeNWSE, Focusable = true, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = gripTemplate } };
        ResizeGrip.Width = ResizeGrip.Height = 14; ResizeGrip.HorizontalAlignment = HorizontalAlignment.Right; ResizeGrip.VerticalAlignment = VerticalAlignment.Bottom; ResizeGrip.Margin = new(-1);
        ResizeGrip.ToolTip = "Drag to resize; dimensions snap to slots and rows. Escape cancels.";
        System.Windows.Automation.AutomationProperties.SetName(ResizeGrip, "Resize " + panel.Title); surface.Children.Add(ResizeGrip);
        ResizeGrip.DragDelta += (_, e) => { if (Window.GetWindow(this) == detachedWindow) return; if (Parent is ScrollViewer && plot != null) plot.Height = Math.Clamp(plot.Height + e.VerticalChange, 160, 800); };
    }

    public void Refresh(IReadOnlyList<ProviderSnapshot> sources, DateTimeOffset now, int minutes)
    {
        latest = sources; latestTime = now; latestMinutes = minutes;
        detachedCard?.Refresh(sources, now, minutes);
        var gpuIds = Specification.Series.Where(s => s.Source == NvidiaTelemetry.SourceId).Select(s => s.DeviceId).Distinct().ToArray();
        processSection.Visibility = gpuIds.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (processSection.IsExpanded) processText.Text = string.Join("\n\n", gpuIds.Select(id => id + "\n" + GpuSensorPanel.ProcessText(id, sources)));
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
            plot = new(data.Axes[0].DisplayUnit, data.Axes[0].Maximum) { Height = Math.Max(Specification.Chart == "lanes" ? Specification.Series.Count * 90 : 160, (previewSize?.HeightRows ?? Specification.HeightRows) * 100) };
            plot.SetSeries(data.Series.Select((s, i) => (s.Name, Colors[i % Colors.Length])).ToArray(), Specification.Chart == "area");
            plot.ConfigureAxes(data.Axes, data.Series.Select(s => s.Selection.Axis).ToArray()); plot.SetLaneMode(Specification.Chart == "lanes");
            OverviewTheme.PlotMenu(plot, data.Unit, Specification.Id); content.Children.Insert(2, plot);
        }
        plot.Visibility = Visibility.Visible;
        for (int i = 0; i < data.Series.Count; i++) plot.SetName(i, data.Series[i].Name);
        plot.Update(data.Series.Select(s => s.Samples).ToArray(), now, minutes);
        legend.Children.Clear();
        foreach (var pair in data.Series.Select((series, index) => (series, index)))
        {
            var axis = data.Axes.Single(a => a.Index == pair.series.Selection.Axis);
            string axisLabel = data.Axes.Count > 1 || axis.Index == 1 ? $" · {(axis.Index == 0 ? "Left" : axis.Index == 1 ? "Right" : "Scale " + (axis.Index + 1))} [{axis.RawUnit}]" : "";
            var label = OverviewTheme.Text($"● {pair.series.Name}{axisLabel} · {WorkspaceMetrics.StateLabel(pair.series.State)}", 11, OverviewTheme.Brush(Colors[pair.index % Colors.Length]));
            label.TextWrapping = TextWrapping.Wrap; label.Margin = new(0, 4, 0, 0);
            label.ToolTip = $"{pair.series.Selection.Source}\n{pair.series.Selection.DeviceId}\n{pair.series.Selection.MetricKey} [{axis.RawUnit}]";
            legend.Children.Add(label);
        }
    }

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
