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
    private readonly TextBlock viewState = new() { FontSize = 11, Foreground = OverviewTheme.Muted,
        Margin = new(4, 2, 4, 5), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
    private System.Windows.Controls.Button pauseButton = null!, zoomInButton = null!, zoomOutButton = null!;
    internal string NavigationStatus => viewState.Text;
    internal string InspectionText => inspection.Text;
    internal bool IsInspecting => detailPanel.Visibility == Visibility.Visible;
    public bool IsViewPaused => viewRange != null;
    public PlotRange? ViewRange => latestHistories.Count == 0 ? null : viewRange ?? new(latestTime.AddMinutes(-windowMinutes), latestTime);
    public IReadOnlyList<string> SeriesNames => series.Select(s => s.Name ?? "Series").ToArray();
    public bool IsSeriesVisible(int index) => series[index].IsVisible;

    private void WireNavigation()
    {
        var toolbar = new StackPanel();
        var actions = new WrapPanel { Margin = new(0, 0, 0, 2) };
        System.Windows.Controls.Button Add(string label, Action action, string help) {
            var button = OverviewTheme.Command(label, action, label == "↺ Live" ? "Reset to live" : label.Trim('+', '−', '←', '→', ' ')); button.ToolTip = help;
            button.Padding = new(6, 4, 6, 4); button.Margin = new(2); button.FontSize = 11;
            actions.Children.Add(button); return button;
        }
        zoomInButton = Add("+ Zoom in", () => Zoom(.5), "Zoom in · Ctrl++ or Ctrl+wheel");
        zoomOutButton = Add("− Zoom out", () => Zoom(2), "Zoom out · Ctrl+− or Ctrl+wheel");
        Add("← Earlier", () => Pan(-.25), "Pan earlier · Ctrl+Left");
        Add("Later →", () => Pan(.25), "Pan later · Ctrl+Right");
        pauseButton = Add("Pause", () => { if (IsViewPaused) ResetView(); else PauseView(); }, "Pause or resume chart · Ctrl+Space");
        Add("↺ Live", ResetView, "Reset zoom and return to live · Ctrl+Home");
        toolbar.Children.Add(actions); toolbar.Children.Add(viewState); Children.Add(toolbar);
        RefreshViewState();
        PreviewMouseDown += (_, _) => Focus();
        MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) { Focus(); e.Handled = true; } };
        chart.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            double anchor = Math.Clamp((e.GetPosition(chart).X - 4) / Math.Max(1, chart.ActualWidth - 8), 0, 1);
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
        ClearSelection(); RenderView();
    }
    public void Pan(double fraction)
    {
        PauseView(); if (viewRange == null) return;
        viewRange = viewRange.Pan(fraction, new(frozenTime.AddMinutes(-30), frozenTime));
        ClearSelection(); RenderView();
    }
    public void ResetView()
    {
        viewRange = null; frozenHistories = null; ClearSelection();
        if (latestHistories.Count > 0) RenderView(); else RefreshViewState();
    }
    public void ShowSeries(int index, bool show)
    {
        series[index].IsVisible = show; ClearSelection();
        if (latestHistories.Count > 0) RenderView();
    }
    private void RefreshViewState()
    {
        var range = ViewRange;
        double seconds = range?.Duration.TotalSeconds ?? windowMinutes * 60;
        double percent = windowMinutes * 6000d / seconds;
        string span = seconds >= 60 ? $"{seconds / 60:0.##} min" : $"{seconds:0.#} sec";
        viewState.Text = $"{(IsViewPaused ? "Paused" : "Live")} · Zoom {percent:0.#}% · {span}" +
            (range == null ? " · Waiting for data" : $"\n{range.Start.ToLocalTime():HH:mm:ss} – {range.End.ToLocalTime():HH:mm:ss}");
        if (pauseButton == null) return;
        pauseButton.Content = IsViewPaused ? "Resume" : "Pause";
        System.Windows.Automation.AutomationProperties.SetName(pauseButton, IsViewPaused ? "Resume" : "Pause");
        OverviewTheme.SetSelected(pauseButton, IsViewPaused);
        zoomInButton.IsEnabled = range != null && seconds > 10;
        zoomOutButton.IsEnabled = range != null && seconds < 1800;
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
