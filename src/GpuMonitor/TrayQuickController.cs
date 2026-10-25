using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GpuMonitor;

internal sealed class TrayQuickController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Settings settings;
    private readonly Func<IReadOnlyList<ProviderSnapshot>> sources;
    private readonly Func<HistoryRecordingStatus?> history;
    private readonly Func<bool> paused;
    private readonly TrayQuickWindow window;
    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private System.Drawing.Point anchor;
    private DateTimeOffset? outside;
    private DateTimeOffset refreshed;
    private bool disposed, menuOpen;
    internal TrayQuickWindow Window => window;
    public TrayQuickController(Forms.NotifyIcon icon, Settings settings, Func<IReadOnlyList<ProviderSnapshot>> sources,
        Func<HistoryRecordingStatus?> history, Action restore, Action configure, Func<bool>? paused = null)
    {
        this.icon = icon; this.settings = settings; this.sources = sources; this.history = history; this.paused = paused ?? (() => false);
        window = new(restore, configure);
        icon.MouseMove += OnMove; icon.MouseDown += OnDown;
        hover.Tick += (_, _) =>
        {
            hover.Stop(); var point = Forms.Cursor.Position;
            if (settings.TrayHoverEnabled && !menuOpen && Forms.Control.MouseButtons == Forms.MouseButtons.None && Math.Abs(point.X - anchor.X) <= 8 && Math.Abs(point.Y - anchor.Y) <= 8) ShowAt(anchor.X, anchor.Y);
        };
        watch.Tick += (_, _) =>
        {
            if (!window.IsVisible) { watch.Stop(); return; }
            var point = Forms.Cursor.Position;
            if (window.ScreenBounds.Contains(point.X, point.Y, 6) || Math.Abs(point.X - anchor.X) <= 18 && Math.Abs(point.Y - anchor.Y) <= 18) outside = null;
            else outside ??= DateTimeOffset.UtcNow;
            if (outside.HasValue && DateTimeOffset.UtcNow - outside > TimeSpan.FromMilliseconds(700)) { Hide(); return; }
            if (DateTimeOffset.UtcNow - refreshed >= TimeSpan.FromSeconds(1)) Refresh();
        };
    }
    private void OnMove(object? sender, Forms.MouseEventArgs e)
    {
        if (disposed || menuOpen || !settings.TrayHoverEnabled || window.IsVisible) return;
        anchor = Forms.Cursor.Position; if (!hover.IsEnabled) hover.Start();
    }
    private void OnDown(object? sender, Forms.MouseEventArgs e) => Hide();
    internal void SetMenuOpen(bool open) { menuOpen = open; if (open) Hide(); }
    internal void ShowAt(int x, int y)
    {
        if (disposed) return;
        anchor = new(x, y); outside = null; new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle(); Refresh(); window.Show(); window.Position(x, y); watch.Start();
    }
    private void Refresh()
    {
        window.Refresh(TrayReadings.Project(sources(), settings.TraySensors), settings.TrayOpacity, history(), paused()); if (window.IsVisible) window.Position(anchor.X, anchor.Y); refreshed = DateTimeOffset.UtcNow;
    }
    public void Hide() { hover.Stop(); watch.Stop(); outside = null; if (!disposed) window.Hide(); }
    public void Dispose()
    {
        if (disposed) return;
        Hide(); disposed = true; icon.MouseMove -= OnMove; icon.MouseDown -= OnDown; window.Close();
    }
}
