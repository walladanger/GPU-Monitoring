using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace GpuMonitor;

internal sealed class TrayQuickWindow : Window
{
    private readonly StackPanel rows = new();
    private readonly Border surface;
    private readonly ScrollViewer scroll;
    private int anchorX, anchorY;
    private readonly TextBlock footer = OverviewTheme.Text("", 11, OverviewTheme.Muted);
    internal IReadOnlyList<TrayReading> Readings { get; private set; } = [];
    internal bool BackdropRequested { get; private set; }
    internal TrayRectangle ScreenBounds
    {
        get { GetWindowRect(new WindowInteropHelper(this).Handle, out var rect); return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top); }
    }
    public TrayQuickWindow(Action restore, Action configure)
    {
        Title = "GPU Monitor quick-view"; Width = 390; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new(0), GlassFrameThickness = new(-1), CornerRadius = new(10) });
        var content = new StackPanel { Margin = new(15) };
        var heading = new DockPanel(); var close = OverviewTheme.Command("× Close", Hide, "Dismiss quick-view"); close.Padding = new(7, 1, 7, 1); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(OverviewTheme.Text("GPU Monitor · Quick view", 16)); content.Children.Add(heading);
        scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 500, Margin = new(0, 10, 0, 8) }; content.Children.Add(scroll);
        footer.TextWrapping = TextWrapping.Wrap; footer.MaxHeight = 34; footer.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(footer);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) };
        actions.Children.Add(OverviewTheme.Command("Open monitor", restore)); actions.Children.Add(OverviewTheme.Command("Configure", configure)); content.Children.Add(actions);
        surface = new Border { Child = content, Background = OverviewTheme.Surface, BorderBrush = OverviewTheme.Border, BorderThickness = new(1), CornerRadius = new(10) }; Content = surface;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Hide(); e.Handled = true; } };
        DpiChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(() => Position(anchorX, anchorY)); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x80); // Tool window: no separate taskbar/Alt+Tab entry.
            int dark = 1, backdrop = 3; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            BackdropRequested = DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) == 0;
            if (BackdropRequested && PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
        };
    }
    internal void Refresh(IReadOnlyList<TrayReading> values, double opacity, HistoryRecordingStatus? history = null, bool paused = false)
    {
        Readings = values; rows.Children.Clear();
        surface.Background = BackdropRequested ? new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(opacity, .65, 1) * 255), 16, 33, 45)) : OverviewTheme.Surface;
        foreach (var reading in values)
        {
            var item = new DockPanel { Margin = new(0, 0, 0, 10) };
            var value = OverviewTheme.Text(reading.DisplayValue, 19, reading.State == WorkspaceBindingState.Available ? OverviewTheme.Green : OverviewTheme.Muted);
            value.MaxWidth = 160; value.TextTrimming = TextTrimming.CharacterEllipsis; value.Margin = new(12, 0, 0, 0); DockPanel.SetDock(value, Dock.Right); item.Children.Add(value);
            var label = new StackPanel(); var name = OverviewTheme.Text(reading.Label, 12); name.TextWrapping = TextWrapping.Wrap; label.Children.Add(name);
            label.Children.Add(OverviewTheme.Text($"{(paused && reading.State == WorkspaceBindingState.Available ? "Paused" : WorkspaceMetrics.StateLabel(reading.State))} · {reading.Time?.ToLocalTime():HH:mm:ss}", 10, OverviewTheme.Muted)); item.Children.Add(label);
            item.ToolTip = $"{reading.Selection.Source}\n{reading.Selection.DeviceId}\n{reading.Selection.MetricKey}\n{reading.Time:O}\nRaw: {reading.Value:R} [{reading.Unit}]"; rows.Children.Add(item);
        }
        if (values.Count == 0) rows.Children.Add(OverviewTheme.Text("No selected sensor readings. Configure sensors from enabled sources.", 12, OverviewTheme.Muted));
        footer.Text = history is { Requested: true } ? history.Error == null ? $"History: {history.Samples:N0} samples · {history.DroppedBatches} dropped batches" : "History unavailable: " + history.Error : "History recording is off.";
        if (history is { Requested: true, Ready: false, Error: null }) footer.Text = "History starting…";
        if (paused) footer.Text = "Monitoring paused · " + footer.Text;
        footer.ToolTip = footer.Text + "\n" + (BackdropRequested ? "Windows system backdrop requested; appearance follows system transparency settings." : "System backdrop unavailable; using an opaque background.");
    }
    internal void Position(int anchorX, int anchorY)
    {
        this.anchorX = anchorX; this.anchorY = anchorY;
        var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchorX, anchorY)).WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // Move into the target monitor first so per-monitor DPI can settle before clamping the actual native rectangle.
        SetWindowPos(handle, new IntPtr(-1), anchorX, anchorY, 0, 0, 0x0010 | 0x0001);
        var dpi = VisualTreeHelper.GetDpi(this); MaxWidth = Math.Max(120, work.Width / dpi.DpiScaleX); MaxHeight = Math.Max(120, work.Height / dpi.DpiScaleY); scroll.MaxHeight = Math.Max(60, MaxHeight - 160);
        UpdateLayout(); var bounds = ScreenBounds;
        var placed = TrayReadings.Place(anchorX, anchorY, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), new(work.Left, work.Top, work.Width, work.Height));
        SetWindowPos(handle, new IntPtr(-1), (int)placed.Left, (int)placed.Top, (int)placed.Width, (int)placed.Height, 0x0010);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(IntPtr window, int index, nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int bytes);
}
