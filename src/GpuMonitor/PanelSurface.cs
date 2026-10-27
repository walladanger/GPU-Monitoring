using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GpuMonitor;

// A surface owns its view. Detaching reparents that same live view and restores it on close.
internal sealed class PanelSurface : Border
{
    private readonly DockPanel layout = new();
    private readonly Grid surfaceRoot = new();
    private readonly ContentControl view = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer viewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly FrameworkElement original;
    private readonly GpuPanelSettings settings;
    private readonly Func<IReadOnlyList<ProviderSnapshot>> sources;
    private readonly Action save;
    private readonly string title;
    private readonly IReadOnlyList<WorkspaceSeries> defaults;
    private WorkspaceChartCard? selected;
    private Window? window;
    internal Window? DetachedWindow => window;
    internal Func<Point> PointerPosition { get; set; }
    internal Thumb ResizeHandle { get; }
    public event Action? Resized;
    public double PreferredWidth => settings.SurfaceWidth;

    public PanelSurface(string title, FrameworkElement original, GpuPanelSettings settings,
        Func<IReadOnlyList<ProviderSnapshot>> sources, Action save, IReadOnlyList<WorkspaceSeries>? defaults = null)
    {
        PointerPosition = () => PointToScreen(Mouse.GetPosition(this));
        this.title = title; this.original = original; this.settings = settings; this.sources = sources; this.save = save; this.defaults = defaults ?? [];
        DevelopmentLog.Write("panel-create", title);
        settings.Normalize(); Child = surfaceRoot; surfaceRoot.Children.Add(layout); Margin = new(0, 0, 14, 14);
        VerticalAlignment = VerticalAlignment.Top;
        Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border; BorderThickness = new(1); CornerRadius = new(9);
        original.Margin = new(0);
        if (original is Border border) border.BorderThickness = new(0);
        DockPanel actions;
        if (original is Border card && card.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is DockPanel heading) {
            stack.Children.Remove(heading); actions = heading; actions.Margin = new(14, 8, 10, 4);
        } else {
            actions = new DockPanel { Margin = new(10, 2, 10, 0) };
            actions.Children.Add(OverviewTheme.Text(title, 12, OverviewTheme.Muted));
        }
        var open = OverviewTheme.Command("↗", OpenDetached, "Open " + title + " in new window"); open.ToolTip = "Open in new window";
        var select = OverviewTheme.Command("⋮", Options, "Select data and display settings for " + title); select.ToolTip = "Select data and panel settings";
        DockPanel.SetDock(open, Dock.Right); actions.Children.Insert(0, open); DockPanel.SetDock(select, Dock.Right); actions.Children.Insert(0, select);
        DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        var factory = new FrameworkElementFactory(typeof(Border)); factory.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        ResizeHandle = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(-1), Cursor = Cursors.SizeNWSE,
            ToolTip = "Drag to resize panel surface. Escape cancels.", Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory } };
        System.Windows.Automation.AutomationProperties.SetName(ResizeHandle, "Resize " + title + " panel surface");
        surfaceRoot.Children.Add(ResizeHandle);
        view.MinWidth = 320; viewport.Content = view;
        viewport.ScrollChanged += (_, _) => { double width = viewport.ViewportWidth; if (double.IsFinite(width) && width > 0) view.Width = Math.Max(320, width); }; layout.Children.Add(viewport); ApplyView();
        bool resizeX = true, resizeY = true;
        Point start = new(); double width = 0, height = 0, oldWidth = 0, oldHeight = 0;
        ResizeHandle.DragStarted += (_, _) => { resizeX = resizeY = true; start = PointerPosition(); width = ActualWidth; height = viewport.ActualHeight; oldWidth = settings.SurfaceWidth; oldHeight = settings.SurfaceHeight; };
        ResizeHandle.DragDelta += (_, _) => {
            var point = PointerPosition(); var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            settings.SurfaceWidth = resizeX ? Math.Clamp(width + (point.X - start.X) / dpi.DpiScaleX, 320, 2000) : oldWidth;
            settings.SurfaceHeight = resizeY ? Math.Clamp(height + (point.Y - start.Y) / dpi.DpiScaleY, 180, 1600) : oldHeight;
            ApplySize(); Resized?.Invoke();
        };
        ResizeHandle.DragCompleted += (_, e) => { if (e.Canceled) { settings.SurfaceWidth = oldWidth; settings.SurfaceHeight = oldHeight; } ApplySize(); Resized?.Invoke(); DevelopmentLog.Write("panel-resize", $"{title}: {settings.SurfaceWidth:0} x {settings.SurfaceHeight:0}; cancelled={e.Canceled}"); save(); };
        foreach (bool horizontal in new[] { true, false }) {
            var edge = new Thumb { Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS, Width = horizontal ? 6 : double.NaN, Height = horizontal ? double.NaN : 6,
                HorizontalAlignment = horizontal ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Bottom,
                Margin = horizontal ? new Thickness(0, 0, -1, 14) : new Thickness(0, 0, 14, -1), Template = ResizeHandle.Template, ToolTip = horizontal ? "Resize panel width" : "Resize panel height", Focusable = true };
            System.Windows.Automation.AutomationProperties.SetName(edge, horizontal ? "Resize panel right edge" : "Resize panel bottom edge");
            surfaceRoot.Children.Add(edge);
            edge.DragStarted += (_, _) => { ResizeHandle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); resizeX = horizontal; resizeY = !horizontal; };
            edge.DragDelta += (_, e) => ResizeHandle.RaiseEvent(new DragDeltaEventArgs(e.HorizontalChange, e.VerticalChange) { RoutedEvent = Thumb.DragDeltaEvent });
            edge.DragCompleted += (_, e) => ResizeHandle.RaiseEvent(new DragCompletedEventArgs(e.HorizontalChange, e.VerticalChange, e.Canceled) { RoutedEvent = Thumb.DragCompletedEvent });
            edge.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && edge.IsDragging) { edge.CancelDrag(); e.Handled = true; } };
        }
        ResizeHandle.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && ResizeHandle.IsDragging) { ResizeHandle.CancelDrag(); e.Handled = true; } };
    }
    private void ApplySize()
    {
        if (settings.SurfaceWidth > 0 && window == null) Width = settings.SurfaceWidth;
        viewport.Height = settings.SurfaceHeight > 0 ? settings.SurfaceHeight : double.NaN;
        if (original is GpuOverviewCard gpu) gpu.ResizePlots(settings.SurfaceHeight > 0 ? settings.SurfaceHeight : null);
        if (original is HardwareOverviewCard hardware && settings.SurfaceHeight > 0) hardware.Plot.Height = Math.Max(100, settings.SurfaceHeight - 100);
        if (selected?.Plot is { } selectedPlot && settings.SurfaceHeight > 0) selectedPlot.Height = Math.Max(160, settings.SurfaceHeight - 120);
        if (window != null && settings.SurfaceWidth > 0) window.Width = settings.SurfaceWidth + 32;
    }
    private void ApplyView()
    {
        selected?.CloseDetached(); selected = null;
        if (settings.Sensors is { Count: > 0 }) {
            selected = new(new("surface-view", title, 1, Math.Clamp((int)(settings.Height / 100), 1, 8), settings.Lanes ? "lanes" : settings.Area ? "area" : "line", settings.Sensors), Edit);
            view.Content = selected;
        } else view.Content = original;
        ApplySize();
    }
    private void Edit()
    {
        var panel = new WorkspacePanel("surface-view", title, 1, 2, settings.Lanes ? "lanes" : settings.Area ? "area" : "line", settings.Sensors ?? defaults);
        var editor = new WorkspacePanelEditor(window ?? Window.GetWindow(this), sources(), panel.Series.Count == 0 ? null : panel);
        if (editor.ShowDialog() == true && editor.Result != null) { DevelopmentLog.Write("panel-select", $"{title}: {editor.Result.Series.Count} sensors; {editor.Result.Chart}"); settings.Sensors = editor.Result.Series.ToList(); settings.Area = editor.Result.Chart == "area"; settings.Lanes = editor.Result.Chart == "lanes"; save(); ApplyView(); Refresh(sources(), DateTimeOffset.UtcNow, settings.Minutes); }
    }
    private void Options()
    {
        var menu = OverviewTheme.Menu();
        menu.Items.Add(OverviewTheme.MenuCommand("Select multiple sensors / axes…", Edit));
        menu.Items.Add(OverviewTheme.MenuCommand("Restore default data view", () => { settings.Sensors = null; save(); ApplyView(); }));
        menu.Items.Add(OverviewTheme.MenuCommand("Reset panel size", () => { settings.SurfaceWidth = settings.SurfaceHeight = 0; Width = double.NaN; ApplySize(); Resized?.Invoke(); save(); }));
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    internal void OpenDetached()
    {
        if (window != null) { window.Activate(); return; }
        DevelopmentLog.Write("panel-detach", title);
        var owner = Window.GetWindow(this);
        Child = OverviewTheme.Command("Show detached " + title, () => window?.Activate());
        window = new Window { Title = title, Width = Math.Max(480, ActualWidth), Height = Math.Clamp(ActualHeight + 40, 320, 1000),
            MinWidth = 360, MinHeight = 280, Owner = owner, Background = OverviewTheme.Canvas,
            Content = new ScrollViewer { Content = surfaceRoot, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.SizeChanged += (_, _) => {
            if (window == null) return;
            surfaceRoot.Width = Math.Max(320, window.ActualWidth - 32);
            settings.SurfaceWidth = surfaceRoot.Width;
            settings.SurfaceHeight = Math.Clamp(window.ActualHeight - 100, 180, 1600);
            ApplySize();
        };
        window.Closed += (_, _) => {
            DevelopmentLog.Write("panel-restore", title); selected?.CloseDetached(); ((ScrollViewer)window!.Content).Content = null; window.Content = null; window = null; surfaceRoot.Width = double.NaN; Child = surfaceRoot; ApplySize(); Resized?.Invoke(); save();
        };
        window.Show();
    }
    internal void CloseDetached() { selected?.CloseDetached(); window?.Close(); }
    public void Refresh(IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset now, int minutes) => selected?.Refresh(snapshots, now, minutes);
}
