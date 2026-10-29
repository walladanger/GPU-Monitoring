using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GpuMonitor;

public sealed partial class TelemetryPlot
{
    private IReadOnlyList<IReadOnlyList<TelemetrySample>> latestHistories = [];
    private IReadOnlyList<IReadOnlyList<TelemetrySample>>? frozenHistories;
    private DateTimeOffset latestTime, frozenTime;
    private PlotRange? viewRange;
    private readonly TextBlock viewState = new() { FontSize = 10, Foreground = OverviewTheme.Muted,
        Margin = new(60, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    public bool IsViewPaused => viewRange != null;
    public PlotRange? ViewRange => latestHistories.Count == 0 ? null : viewRange ?? new(latestTime.AddMinutes(-windowMinutes), latestTime);
    public IReadOnlyList<string> SeriesNames => series.Select(s => s.Name ?? "Series").ToArray();
    public bool IsSeriesVisible(int index) => series[index].IsVisible;

    private void WireNavigation()
    {
        Children.Add(viewState);
        ToolTip = "Ctrl+wheel / Ctrl+plus/minus: zoom · Ctrl+Left/Right: pan · Ctrl+Space: pause · Ctrl+Home: live/reset. Right-click for exports and series.";
        PreviewMouseDown += (_, _) => Focus();
        MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) { Focus(); e.Handled = true; } };
        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            double anchor = Math.Clamp((e.GetPosition(chart).X - 4) / Math.Max(1, ActualWidth - 8), 0, 1);
            Zoom(e.Delta > 0 ? .5 : 2, anchor); e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (e.Key)
            {
                case Key.Add: case Key.OemPlus: Zoom(.5); break;
                case Key.Subtract: case Key.OemMinus: Zoom(2); break;
                case Key.Left: Pan(-.25); break;
                case Key.Right: Pan(.25); break;
                case Key.Space: if (IsViewPaused) ResetView(); else PauseView(); break;
                case Key.Home: ResetView(); break;
                default: return;
            }
            e.Handled = true;
        };
    }
    public void PauseView()
    {
        if (IsViewPaused || latestHistories.Count == 0) return;
        frozenTime = latestTime;
        frozenHistories = latestHistories.Select(h => (IReadOnlyList<TelemetrySample>)h.ToArray()).ToArray();
        viewRange = new(latestTime.AddMinutes(-windowMinutes), latestTime);
        RefreshViewState();
    }
    public void Zoom(double factor, double anchor = .5)
    {
        PauseView(); if (viewRange == null) return;
        viewRange = viewRange.Zoom(factor, anchor, new(frozenTime.AddMinutes(-30), frozenTime));
        SetCursor(null); CursorMoved?.Invoke(null); RenderView();
    }
    public void Pan(double fraction)
    {
        PauseView(); if (viewRange == null) return;
        viewRange = viewRange.Pan(fraction, new(frozenTime.AddMinutes(-30), frozenTime));
        SetCursor(null); CursorMoved?.Invoke(null); RenderView();
    }
    public void ResetView()
    {
        viewRange = null; frozenHistories = null;
        SetCursor(null); CursorMoved?.Invoke(null);
        if (latestHistories.Count > 0) RenderView(); else RefreshViewState();
    }
    public void ShowSeries(int index, bool show)
    {
        series[index].IsVisible = show;
        SetCursor(null); CursorMoved?.Invoke(null);
        if (latestHistories.Count > 0) RenderView();
    }
    private void RefreshViewState()
    {
        viewState.Visibility = IsViewPaused ? Visibility.Visible : Visibility.Collapsed;
        viewState.Text = "View paused · reset to live";
    }
    public void SavePng(string path)
    {
        if (ActualWidth < 1 || ActualHeight < 1) throw new InvalidOperationException("Chart has not rendered yet.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(this) { Stretch = Stretch.Fill }, null, new Rect(0, 0, ActualWidth, ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
