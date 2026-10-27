using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GpuMonitor;

public sealed partial class TelemetryPlot
{
    private readonly Ellipse selectedMarker = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, Fill = OverviewTheme.Blue, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas selectionOverlay = new() { IsHitTestVisible = false };
    private FrostedPanel detailPanel = null!;
    private (int Series, TelemetrySample Sample)? selectedPoint;
    private Window? selectionWindow;
    internal TelemetrySample? SelectedSample => selectedPoint?.Sample;

    private void WireSelection()
    {
        selectionOverlay.Children.Add(selectedMarker); Children.Add(selectionOverlay);
        inspection.Margin = new(9); inspection.TextWrapping = TextWrapping.Wrap;
        detailPanel = new FrostedPanel { Backdrop = chart, Child = inspection, MaxWidth = 430, Margin = new(42, 36, 10, 26),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Children.Add(detailPanel);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 0, 4, 0) };
        foreach (var entry in new[] { ("+", "Zoom in", (Action)(() => Zoom(.5))), ("−", "Zoom out", (Action)(() => Zoom(2))), ("↺", "Reset to live", (Action)ResetView) }) {
            var button = OverviewTheme.Command(entry.Item1, entry.Item3, entry.Item2); button.ToolTip = entry.Item2; button.Padding = new(6, 1, 6, 1); button.FontSize = 11; button.Margin = new(1); zoom.Children.Add(button);
        }
        Children.Add(zoom);
        chart.Cursor = Cursors.Cross;
        chart.PreviewMouseLeftButtonDown += (_, e) => { SelectNearestPoint(e.GetPosition(chart)); Focus(); e.Handled = true; };
        Loaded += (_, _) => { selectionWindow = Window.GetWindow(this); if (selectionWindow != null) { selectionWindow.PreviewMouseDown += OutsideClick; selectionWindow.Deactivated += WindowDeactivated; } };
        Unloaded += (_, _) => { if (selectionWindow != null) { selectionWindow.PreviewMouseDown -= OutsideClick; selectionWindow.Deactivated -= WindowDeactivated; selectionWindow = null; } };
        SizeChanged += (_, _) => RefreshSelectedPoint();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { ClearSelection(); e.Handled = true; } };
    }
    private void WindowDeactivated(object? sender, EventArgs e) => ClearSelection();
    private void OutsideClick(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node == this) return;
        ClearSelection();
    }
    private Point SamplePosition(int index, TelemetrySample sample)
    {
        double fraction = (sample.Time.ToUnixTimeMilliseconds() - timeAxis.MinLimit!.Value) / (timeAxis.MaxLimit!.Value - timeAxis.MinLimit.Value);
        int axis = laneMode ? 0 : series[index].ScalesYAt;
        double y = DisplayValue(index, sample.Value) ?? 0;
        return new(4 + fraction * Math.Max(1, ActualWidth - 8), 8 + (1 - y / (valueAxes[axis].MaxLimit ?? 1)) * Math.Max(1, ActualHeight - 32));
    }
    internal void SelectNearestPoint(Point pointer)
    {
        if (timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        (int Index, TelemetrySample Sample, double Distance)? nearest = null;
        for (int i = 0; i < visible.Count; i++) {
            if (!series[i].IsVisible) continue;
            foreach (var sample in visible[i].Where(s => s.Value.HasValue)) {
                double distance = (SamplePosition(i, sample) - pointer).Length;
                if (distance <= 16 && (nearest == null || distance < nearest.Value.Distance)) nearest = (i, sample, distance);
            }
        }
        if (nearest == null || selectedPoint is { } current && current.Series == nearest.Value.Index && current.Sample == nearest.Value.Sample) { ClearSelection(); return; }
        selectedPoint = (nearest.Value.Index, nearest.Value.Sample); RefreshSelectedPoint();
    }
    internal void ClearSelection()
    {
        selectedPoint = null; selectedMarker.Visibility = Visibility.Collapsed;
        inspection.Text = ""; detailPanel.Visibility = Visibility.Collapsed; cursor.Visibility = Visibility.Collapsed;
    }
    private void RefreshSelectedPoint()
    {
        if (selectedPoint is not { } point || timeAxis.MinLimit == null || point.Series >= series.Count) return;
        var location = SamplePosition(point.Series, point.Sample);
        bool visiblePoint = series[point.Series].IsVisible && location.X >= 4 && location.X <= ActualWidth - 4;
        selectedMarker.Visibility = visiblePoint ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(selectedMarker, location.X - 6); Canvas.SetTop(selectedMarker, location.Y - 6);
        inspection.Text = $"Selected · {ReadingName(point.Series)}\n{point.Sample.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\n{point.Sample.Value / SeriesDivisor(point.Series):R}{SeriesUnit(point.Series)}\nClick again, outside the graph, or Escape to deselect";
        detailPanel.Visibility = Visibility.Visible; detailPanel.RefreshBackdrop();
    }
    internal Point VerificationSamplePosition(int index, TelemetrySample sample) => SamplePosition(index, sample);
}
