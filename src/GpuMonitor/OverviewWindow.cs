using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace GpuMonitor;

public sealed class OverviewWindow : Window
{
    private readonly TelemetrySession fixtureSession = new(new NvidiaCollector());
    private readonly DashboardTelemetry? monitoring;
    private readonly Grid root;
    private readonly Dictionary<string, GpuOverviewCard> cards = [];
    private readonly Dictionary<string, string> deviceColors = [];
    private int nextColor;
    private readonly WrapPanel gpuGrid = new();
    private readonly StackPanel navigation = new(), body = new();
    private readonly ColumnDefinition rail = new() { Width = new(222) };
    private readonly TextBlock health = OverviewTheme.Text("Discovering…", 13, OverviewTheme.Muted);
    private readonly TextBlock summary = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock empty = OverviewTheme.Text("Discovering NVIDIA GPUs…", 16, OverviewTheme.Muted);
    private readonly WrapPanel legend = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TelemetryPlot comparison = new("%", 100) { Height = 220 };
    private readonly DispatcherTimer timer = new();
    private readonly Settings settings;
    private readonly System.Windows.Forms.NotifyIcon? tray;
    private readonly bool savePreferences;
    private bool reading, closed, closing, paused, details, collapsed;
    private string[] comparisonIds = [];
    private DateTimeOffset viewTime;
    private readonly List<Button> rangeButtons = [];
    private static readonly string[] Colors = ["#2B82F8", "#A66CF2", "#33DF32", "#FFBA4D", "#F45A5A", "#39BCD2", "#DBCE32", "#E977BD"];

    public OverviewWindow(bool startMonitoring = true, CollectorConfiguration? configuration = null, DashboardTelemetry? telemetry = null)
    {
        settings = startMonitoring ? Settings.Load() : new Settings(); savePreferences = startMonitoring;
        Title = "GPU Monitor"; Width = 1536; Height = 1024; MinWidth = 680; MinHeight = 540;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0), CornerRadius = new(0) });
        root = new Grid { Background = OverviewTheme.Canvas }; root.RowDefinitions.Add(new() { Height = new(72) }); root.RowDefinitions.Add(new()); Content = root;
        root.Children.Add(CreateHeader());
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(rail); workspace.ColumnDefinitions.Add(new()); Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        var sidebar = new DockPanel { Background = OverviewTheme.Brush("#171D25"), Margin = new(0, 1, 0, 0) };
        var quick = OverviewTheme.Card(summary, new(12)); quick.Margin = new(8); DockPanel.SetDock(quick, Dock.Bottom); sidebar.Children.Add(quick);
        sidebar.Children.Add(navigation); workspace.Children.Add(sidebar); BuildNavigation();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Padding = new(17, 16, 2, 16) };
        Grid.SetColumn(scroll, 1); workspace.Children.Add(scroll);
        var controls = new DockPanel { Margin = new(0, 0, 14, 12) };
        var ranges = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(ranges, Dock.Right); controls.Children.Add(ranges);
        foreach (int minutes in new[] { 1, 5, 15, 30 })
        {
            var button = OverviewTheme.Command($"{minutes}m", () => { settings.WindowMinutes = minutes; Save(); Render(viewTime); });
            button.Tag = minutes; rangeButtons.Add(button); ranges.Children.Add(button);
        }
        controls.Children.Add(OverviewTheme.Text("GPU Overview", 21)); body.Children.Add(controls);
        empty.Margin = new(0, 30, 0, 30); body.Children.Add(empty); body.Children.Add(gpuGrid);
        var comparisonContent = new StackPanel(); comparisonContent.Children.Add(OverviewTheme.Text("GPU utilization comparison", 16));
        comparisonContent.Children.Add(comparison); comparisonContent.Children.Add(legend);
        var compareCard = OverviewTheme.Card(comparisonContent); compareCard.Margin = new(0, 0, 14, 14); body.Children.Add(compareCard);
        OverviewTheme.PlotMenu(comparison, "%", "gpu-comparison"); comparison.CursorMoved += SetCursor;
        var footer = OverviewTheme.Text("Read-only monitoring  ·  Right-click a graph to export its measured data", 12, OverviewTheme.Muted);
        footer.TextWrapping = TextWrapping.Wrap; body.Children.Add(footer);
        gpuGrid.SizeChanged += (_, _) => Reflow();
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds); timer.Tick += (_, _) => Render(DateTimeOffset.UtcNow);
        if (startMonitoring)
        {
            // Preserve NVIDIA-only normal launch. Extra hardware/runtime sources require --sources.
            monitoring = telemetry ?? (configuration == null ? new DashboardTelemetry([new NvidiaCollector()]) : DashboardTelemetry.Create(configuration));
            tray = new System.Windows.Forms.NotifyIcon { Text = "GPU Monitor", Icon = System.Drawing.SystemIcons.Application, Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Open GPU Monitor", null, (_, _) => Dispatcher.Invoke(Restore));
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close)); tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
            Loaded += async (_, _) => await UpdateMonitoringAsync();
        }
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && settings.MinimizeToTray) Hide(); };
        Closing += async (_, e) =>
        {
            if (closing || monitoring == null) return;
            e.Cancel = true;
            if (closed) return;
            closed = true; timer.Stop();
            try { await monitoring.DisposeAsync(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            finally { closing = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
        };
        Closed += (_, _) => { closed = true; timer.Stop(); tray?.Dispose(); };
    }

    private UIElement CreateHeader()
    {
        var bar = new DockPanel { Background = OverviewTheme.Brush("#11181F"), LastChildFill = true, Margin = new(0, 0, 0, 1) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
        health.Margin = new(10, 0, 16, 0); actions.Children.Add(health);
        var settingsButton = OverviewTheme.Command("⚙", () => OpenSettings(), "Monitoring settings"); settingsButton.ToolTip = "Monitoring settings"; actions.Children.Add(settingsButton);
        actions.Children.Add(OverviewTheme.Command("—", () => WindowState = WindowState.Minimized, "Minimize"));
        actions.Children.Add(OverviewTheme.Command("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, "Maximize or restore"));
        actions.Children.Add(OverviewTheme.Command("×", Close, "Close")); DockPanel.SetDock(actions, Dock.Right); bar.Children.Add(actions);
        var title = new StackPanel { Margin = new(22, 12, 0, 8) }; title.Children.Add(OverviewTheme.Text("GPU Monitor", 20));
        title.Children.Add(OverviewTheme.Text(savePreferences ? "Real-time GPU monitoring" : "Fixture preview · synthetic data", 13, OverviewTheme.Muted));
        title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); };
        bar.Children.Add(title); return bar;
    }

    private void BuildNavigation()
    {
        navigation.Children.Clear();
        var toggle = OverviewTheme.Command(collapsed ? "☰" : "☰   Collapse navigation", () => { collapsed = !collapsed; rail.Width = new(collapsed ? 72 : 222); BuildNavigation(); });
        navigation.Children.Add(toggle);
        Add("\uE80F", "Overview", () => { details = false; BuildNavigation(); Render(viewTime); }, !details);
        Add("\uE7F4", "GPU Details", () => { details = true; BuildNavigation(); Render(viewTime); }, details);
        Add("\uE713", "Settings", OpenSettings, false);
        void Add(string glyph, string label, Action action, bool selected)
        {
            var button = OverviewTheme.Command(label, action); button.ToolTip = label; button.Margin = new(8, 4, 8, 4); button.Height = 48;
            button.Background = selected ? OverviewTheme.Brush("#184880") : Brushes.Transparent; button.BorderThickness = new(0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = OverviewTheme.Text(glyph, 18, selected ? Brushes.White : OverviewTheme.Muted); icon.FontFamily = new("Segoe MDL2 Assets"); row.Children.Add(icon);
            if (!collapsed) { var text = OverviewTheme.Text(label); text.Margin = new(14, 0, 0, 0); row.Children.Add(text); }
            button.Content = row; navigation.Children.Add(button);
        }
    }

    private void OpenSettings()
    {
        var menu = OverviewTheme.Menu();
        menu.Items.Add(OverviewTheme.MenuCommand(paused ? "Resume monitoring" : "Pause monitoring", async () =>
        { paused = !paused; await UpdateMonitoringAsync(); }));
        menu.Items.Add(OverviewTheme.MenuCommand("Rescan now", async () => await RefreshAsync()));
        var refresh = new MenuItem { Header = "Refresh interval" };
        foreach (int seconds in new[] { 1, 2, 5, 10 })
        {
            var item = OverviewTheme.MenuCommand($"{seconds} second{(seconds == 1 ? "" : "s")}", async () => { settings.RefreshSeconds = seconds; Save(); await UpdateMonitoringAsync(); });
            item.IsCheckable = true; item.IsChecked = settings.RefreshSeconds == seconds; refresh.Items.Add(item);
        }
        menu.Items.Add(refresh);
        var minimize = OverviewTheme.MenuCommand("Minimize to tray", () => { settings.MinimizeToTray = !settings.MinimizeToTray; Save(); });
        minimize.IsCheckable = true; minimize.IsChecked = settings.MinimizeToTray; menu.Items.Add(minimize);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void Save() { if (!savePreferences) return; try { settings.Save(); } catch (Exception error) { MessageBox.Show(this, error.Message, "Settings could not be saved"); } }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }

    private async Task UpdateMonitoringAsync()
    {
        if (closed) return;
        timer.Stop();
        try { if (monitoring != null) await monitoring.SetMonitoringAsync(!paused, TimeSpan.FromSeconds(settings.RefreshSeconds)); }
        catch (Exception error) { if (!closed) MessageBox.Show(this, error.Message, "Monitoring could not be updated"); }
        if (closed) return;
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        if (!paused) timer.Start();
        Render(paused ? viewTime : DateTimeOffset.UtcNow);
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (reading || closed) return; reading = true;
        try { if (monitoring != null) await monitoring.RefreshAsync(); }
        catch (Exception error) { if (!closed) MessageBox.Show(this, error.Message, "Monitoring refresh failed"); }
        finally { reading = false; }
        if (!closed) Render(DateTimeOffset.UtcNow);
    }

    private void Render(DateTimeOffset now)
    {
        if (now == default) now = DateTimeOffset.UtcNow; viewTime = now;
        var session = monitoring?.Snapshot(NvidiaTelemetry.SourceId).Single() ?? new ProviderSnapshot(
            NvidiaTelemetry.SourceId, fixtureSession.Health, fixtureSession.Error, fixtureSession.LastSuccess,
            fixtureSession.Current, fixtureSession.HistorySnapshot(), fixtureSession.Definitions);
        var readings = session.Current == null ? Array.Empty<GpuReading>() : NvidiaSnapshot.Readings(session.Current).ToArray();
        var ids = readings.Select(g => g.Id).ToHashSet();
        foreach (var id in cards.Keys.Where(id => !ids.Contains(id)).ToArray()) { gpuGrid.Children.Remove(cards[id]); cards.Remove(id); deviceColors.Remove(id); }
        foreach (var gpu in readings)
        {
            if (!cards.TryGetValue(gpu.Id, out var card))
            {
                deviceColors[gpu.Id] = Colors[nextColor++ % Colors.Length];
                cards[gpu.Id] = card = new(); gpuGrid.Children.Add(card);
                foreach (var plot in card.Plots) plot.CursorMoved += SetCursor;
            }
            card.Update(gpu, session, settings.WindowMinutes, now, details);
        }
        // Reorder existing visuals to match the current display indices without changing UUID history.
        for (int i = 0; i < readings.Length; i++)
            if (gpuGrid.Children[i] != cards[readings[i].Id]) { gpuGrid.Children.Remove(cards[readings[i].Id]); gpuGrid.Children.Insert(i, cards[readings[i].Id]); }
        if (!comparisonIds.SequenceEqual(readings.Select(g => g.Id)))
        {
            comparisonIds = readings.Select(g => g.Id).ToArray();
            comparison.SetSeries(readings.Select(g => ($"GPU {g.Index}", deviceColors[g.Id])).ToArray());
        }
        comparison.Update(readings.Select(g => session.Series(g.Id, "utilization.gpu")).ToArray(), now, settings.WindowMinutes);
        comparison.Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
        legend.Children.Clear();
        for (int i = 0; i < readings.Length; i++)
        {
            comparison.SetName(i, $"GPU {readings[i].Index}");
            var label = OverviewTheme.Text($"● GPU {readings[i].Index}", 12, OverviewTheme.Brush(deviceColors[readings[i].Id]));
            label.Margin = new(12, 0, 12, 0); legend.Children.Add(label);
        }
        health.Text = paused ? "● Paused" : session.Health switch { ProviderHealth.Live => "● Connected", ProviderHealth.Stale => "● Telemetry unavailable", _ => "● Discovering…" };
        health.Foreground = paused || session.Health == ProviderHealth.Stale ? OverviewTheme.Brush("#F9B84D") : OverviewTheme.Green;
        health.ToolTip = session.Error ?? $"Last update: {session.LastSuccess?.ToLocalTime():HH:mm:ss}";
        if (!savePreferences) { health.Text = "● Test fixture"; health.ToolTip = "Synthetic verification data; no hardware is polled."; }
        empty.Visibility = readings.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = session.Health == ProviderHealth.Starting ? "Discovering NVIDIA GPUs…" : session.Health == ProviderHealth.Stale ? "NVIDIA telemetry unavailable. Check the driver; retrying automatically." : "No NVIDIA GPUs detected.";
        summary.Text = collapsed ? $"{readings.Length} GPUs" : $"{readings.Length} GPUs detected\nLast update  {session.LastSuccess?.ToLocalTime():HH:mm:ss}\n{(paused ? "Paused" : session.Health.ToString())}";
        foreach (var button in rangeButtons) button.Background = (int)button.Tag == settings.WindowMinutes ? OverviewTheme.Brush("#184880") : OverviewTheme.Surface;
        Reflow();
    }
    private void Reflow()
    {
        double width = gpuGrid.ActualWidth; if (width <= 0) return;
        int columns = Math.Min(Math.Max(1, cards.Count), Math.Max(1, (int)Math.Floor(width / 500)));
        foreach (var card in cards.Values) card.Width = Math.Max(280, width / columns - 14);
    }
    private void SetCursor(DateTimeOffset? time) { comparison.SetCursor(time); foreach (var card in cards.Values) foreach (var plot in card.Plots) plot.SetCursor(time); }

    internal void ApplyFixture(IReadOnlyList<GpuReading> readings, DateTimeOffset now, bool render = true)
    { fixtureSession.Apply(NvidiaTelemetry.Normalize(readings), now); if (render) Render(now); }
    internal void VerifyInventory(int expected)
    {
        if (cards.Count != expected || comparison.SeriesCount != expected) throw new InvalidOperationException("Overview must show exactly the detected GPU count.");
    }
    internal IReadOnlyList<TelemetryPlot> VerifyPlots => cards.Values.SelectMany(c => c.Plots).ToArray();
    internal FrameworkElement VerificationRoot => root;
    internal int VerificationDeviceCount => cards.Count;
    internal async Task VerifyPauseAsync(bool value) { paused = value; await UpdateMonitoringAsync(); }
    internal Task VerifyRescanAsync() => RefreshAsync();
}
