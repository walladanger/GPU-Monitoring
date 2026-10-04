using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace GpuMonitor;

public sealed class Settings
{
    public int RefreshSeconds { get; set; } = 2;
    public int WindowMinutes { get; set; } = 5;
    public bool MinimizeToTray { get; set; } = true;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitor", "settings.json");
    public static Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); s.RefreshSeconds = new[] { 1, 2, 5, 10 }.Contains(s.RefreshSeconds) ? s.RefreshSeconds : 2; s.WindowMinutes = new[] { 5, 15, 30 }.Contains(s.WindowMinutes) ? s.WindowMinutes : 5; return s; }
        catch { return new(); }
    }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
}

public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            try { SmokeChecks.Run(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "FAIL: " + e); Environment.ExitCode = 1; }
            return;
        }
        using var instance = new Mutex(true, "Local\\GpuMonitor", out bool firstInstance);
        if (!firstInstance) { System.Windows.MessageBox.Show("GPU Monitor is already running. Open it from its tray icon.", "GPU Monitor"); return; }
        var app = new Application(); var dashboard = new Dashboard();
        if (args.Contains("--verify-ui"))
        {
            var verify = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            verify.Tick += (_, _) => { verify.Stop(); dashboard.VerifyAndCapture(); dashboard.Close(); };
            dashboard.Loaded += (_, _) => verify.Start();
        }
        try { app.Run(dashboard); }
        finally { instance.ReleaseMutex(); }
    }
}

public sealed class Dashboard : Window
{
    private readonly Inventory inventory = new();
    private readonly NvidiaCollector collector = new();
    private readonly Settings settings = Settings.Load();
    private readonly CancellationTokenSource stop = new();
    private readonly WrapPanel panels = new();
    private readonly TextBlock status = new() { Text = "Discovering NVIDIA GPUs…", Foreground = Brushes.LightGray, Margin = new(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox gpuChoice = new() { MinWidth = 220 };
    private readonly Dictionary<string, GpuPanel> gpuPanels = [];
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new();
    private bool reading;
    private bool closed;
    private bool telemetryAvailable;
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(11, 16, 24));

    public Dashboard(bool startMonitoring = true)
    {
        Title = "GPU Monitor"; Width = 1180; Height = 850; MinWidth = 650; MinHeight = 500; Background = BackgroundBrush; Foreground = Brushes.White;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "GPU Monitor", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Native Windows monitoring · automatic NVIDIA GPU discovery", Foreground = Brushes.LightSlateGray, Margin = new(0, 5, 0, 14) });
        var toolbar = new WrapPanel(); header.Children.Add(toolbar);
        AddLabel(toolbar, "Show"); toolbar.Children.Add(gpuChoice); gpuChoice.SelectionChanged += (_, _) => FilterPanels();
        AddLabel(toolbar, "Refresh"); var refresh = new ComboBox { Width = 90, ItemsSource = new[] { 1, 2, 5, 10 }, SelectedItem = settings.RefreshSeconds }; toolbar.Children.Add(refresh);
        refresh.SelectionChanged += (_, _) => { settings.RefreshSeconds = (int)refresh.SelectedItem; timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds); SaveSettings(); };
        AddLabel(toolbar, "History (min)"); var range = new ComboBox { Width = 70, ItemsSource = new[] { 5, 15, 30 }, SelectedItem = settings.WindowMinutes }; toolbar.Children.Add(range);
        range.SelectionChanged += (_, _) => { settings.WindowMinutes = (int)range.SelectedItem; SaveSettings(); Redraw(); };
        var scan = new Button { Content = "Rescan now", Margin = new(12, 0, 0, 0), Padding = new(10, 4, 10, 4) }; toolbar.Children.Add(scan); scan.Click += async (_, _) => await RefreshAsync();
        var export = new Button { Content = "Export CSV", Margin = new(8, 0, 0, 0), Padding = new(10, 4, 10, 4) }; toolbar.Children.Add(export); export.Click += (_, _) => Export();
        var trayOption = new System.Windows.Controls.CheckBox { Content = "Minimize to tray", IsChecked = settings.MinimizeToTray, Margin = new(12, 5, 0, 0), Foreground = Brushes.White }; toolbar.Children.Add(trayOption);
        trayOption.Click += (_, _) => { settings.MinimizeToTray = trayOption.IsChecked == true; SaveSettings(); };
        header.Children.Add(status);
        var footer = new TextBlock { Text = "Read-only · 30-minute session history · N/A means the driver does not expose this reading. Core temperature excludes hotspot and memory junction.", Foreground = Brushes.LightSlateGray, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = panels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tray = new System.Windows.Forms.NotifyIcon { Text = "GPU Monitor", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Open GPU Monitor", null, (_, _) => Dispatcher.Invoke(Restore)); menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && settings.MinimizeToTray) Hide(); };
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds); timer.Tick += async (_, _) => await RefreshAsync();
        if (startMonitoring) Loaded += async (_, _) => { await RefreshAsync(); if (!closed) timer.Start(); };
        Closed += (_, _) => { closed = true; timer.Stop(); stop.Cancel(); tray.Dispose(); };
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    public void VerifyAndCapture()
    {
        if (inventory.Current.Count == 0 || gpuPanels.Count != inventory.Current.Count || gpuChoice.Items.Count != inventory.Current.Count + 1)
            throw new InvalidOperationException("Live GPU discovery did not populate the native UI");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "dashboard-preview.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), $"PASS: {gpuPanels.Count} real GPU panels; {gpuChoice.Items.Count} filter choices; live telemetry loaded.");
    }
    internal void VerifyInventory(int expectedCount)
    {
        if (gpuPanels.Count != expectedCount || gpuChoice.Items.Count != expectedCount + 1)
            throw new InvalidOperationException("GPU panel/filter reconciliation failed");
    }
    internal void ApplyReadings(IReadOnlyList<GpuReading> readings)
    {
        inventory.Apply(readings);
        telemetryAvailable = true;
        var ids = readings.Select(g => g.Id).ToHashSet();
        foreach (var id in gpuPanels.Keys.Where(id => !ids.Contains(id)).ToArray()) { panels.Children.Remove(gpuPanels[id]); gpuPanels.Remove(id); }
        foreach (var g in readings) if (!gpuPanels.ContainsKey(g.Id)) { var p = new GpuPanel(); gpuPanels[g.Id] = p; panels.Children.Add(p); }
        var selected = (gpuChoice.SelectedItem as GpuOption)?.Id;
        var options = new List<GpuOption> { new(null, "All GPUs") }; options.AddRange(readings.Select(g => new GpuOption(g.Id, $"GPU {g.Index} · {g.Name}")));
        gpuChoice.ItemsSource = options; gpuChoice.SelectedItem = options.FirstOrDefault(o => o.Id == selected) ?? options[0];
        status.Foreground = Brushes.MediumAquamarine;
        status.Text = readings.Count == 0 ? "No NVIDIA GPUs detected. Connect a supported GPU and rescan." : $"Live · {readings.Count} GPU{(readings.Count == 1 ? "" : "s")} detected · last scan {DateTime.Now:T}";
        Redraw();
    }
    private void SaveSettings() { try { settings.Save(); } catch (Exception e) { status.Text = "Could not save settings: " + e.Message; } }
    private static void AddLabel(Panel parent, string text) => parent.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray, Margin = new(10, 0, 8, 0) });
    private async Task RefreshAsync()
    {
        if (reading || closed) return; reading = true;
        try
        {
            var readings = await collector.ReadAsync(stop.Token); if (closed) return;
            ApplyReadings(readings);
        }
        catch (OperationCanceledException) { if (!closed) SetError("NVIDIA query timed out. Retrying automatically."); }
        catch (Exception e) { if (!closed) SetError("Telemetry unavailable: " + e.Message + "\nCheck the NVIDIA driver. Retrying automatically; previous readings are stale."); }
        finally { reading = false; }
    }
    private void SetError(string message) { telemetryAvailable = false; status.Foreground = Brushes.Salmon; status.Text = message; foreach (var panel in gpuPanels.Values) panel.Opacity = .45; }
    private void Redraw() { foreach (var g in inventory.Current) { gpuPanels[g.Id].Opacity = telemetryAvailable ? 1 : .45; gpuPanels[g.Id].Update(g, inventory.History[g.Id], settings.WindowMinutes); } FilterPanels(); }
    private void FilterPanels() { var id = (gpuChoice.SelectedItem as GpuOption)?.Id; foreach (var pair in gpuPanels) pair.Value.Visibility = id == null || pair.Key == id ? Visibility.Visible : Visibility.Collapsed; }
    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = "CSV files|*.csv", FileName = "gpu-history.csv" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName);
            writer.WriteLine("timestamp,uuid,index,name,pci_bus_id,memory.total," + string.Join(',', Metrics.All.Select(m => m.Key)));
            foreach (var g in inventory.History.Values.SelectMany(h => h).OrderBy(g => g.Time))
                writer.WriteLine(string.Join(',', new[] { g.Time.ToString("O"), g.Id, g.Index, g.Name, g.Bus, g.Metrics["memory.total"]?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "" }.Concat(Metrics.All.Select(m => g.Metrics[m.Key]?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")).Select(Csv)));
            status.Text = "CSV exported to " + dialog.FileName;
        }
        catch (Exception e) { System.Windows.MessageBox.Show(this, e.Message, "Export failed"); }
    }
    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    private sealed record GpuOption(string? Id, string Label) { public override string ToString() => Label; }
}

public sealed class GpuPanel : Border
{
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MediumAquamarine };
    private readonly TextBlock identity = new() { Foreground = Brushes.LightSlateGray, FontSize = 11, Margin = new(0, 5, 0, 14), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel metrics = new();
    public GpuPanel()
    {
        Width = 520; Padding = new(20); Margin = new(0, 0, 16, 16); CornerRadius = new(12); Background = new SolidColorBrush(Color.FromRgb(20, 30, 43));
        var root = new StackPanel(); Child = root; root.Children.Add(title); root.Children.Add(identity); root.Children.Add(metrics);
    }
    public void Update(GpuReading gpu, IEnumerable<GpuReading> history, int minutes)
    {
        title.Text = $"GPU {gpu.Index} · {gpu.Name}"; identity.Text = $"{gpu.Id}\nPCI {gpu.Bus} · {gpu.Metrics["memory.total"] / 1024:0.#} GiB VRAM";
        metrics.Children.Clear();
        foreach (var metric in Metrics.All)
        {
            var value = gpu.Metrics[metric.Key];
            // Unsupported optional sensors get no chart or control; essential values remain visible as N/A.
            if (value == null && metric.Key is "fan.speed" or "clocks.current.graphics" or "clocks.current.memory") continue;
            var row = new DockPanel { Margin = new(0, 4, 0, 0) };
            var text = new TextBlock { Text = value == null ? "N/A" : $"{value:0.#} {metric.Unit}", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(text, Dock.Right); row.Children.Add(text); row.Children.Add(new TextBlock { Text = metric.Title, Foreground = Brushes.LightGray }); metrics.Children.Add(row);
            metrics.Children.Add(new HistoryChart(history.ToArray(), metric, minutes, gpu.Metrics["memory.total"]) { Height = 65, Margin = new(0, 4, 0, 10) });
        }
    }
}

public sealed class HistoryChart(GpuReading[] samples, Metric metric, int minutes, double? memoryTotal) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        var now = DateTimeOffset.Now; var start = now.AddMinutes(-minutes); var points = samples.Where(s => s.Time >= start).ToArray();
        if (points.Length == 0) return;
        double max = metric.Scale ?? (metric.Key == "memory.used" ? memoryTotal ?? 1 : Math.Max(1, points.Max(s => s.Metrics[metric.Key] ?? 0) * 1.15));
        for (int i = 0; i < 3; i++) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(40, 54, 72)), 1), new(0, h * i / 2), new(w, h * i / 2));
        Point? previous = null;
        foreach (var sample in points)
        {
            var value = sample.Metrics[metric.Key]; if (value == null) { previous = null; continue; }
            var point = new Point((sample.Time - start).TotalSeconds / (minutes * 60) * w, h - Math.Clamp(value.Value / Math.Max(max, 1), 0, 1) * h);
            if (previous is Point p) dc.DrawLine(new Pen(Brushes.MediumAquamarine, 1.8), p, point);
            else dc.DrawEllipse(Brushes.MediumAquamarine, null, point, 2, 2);
            previous = point;
        }
    }
}
