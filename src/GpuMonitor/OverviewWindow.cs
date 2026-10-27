using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace GpuMonitor;

public sealed partial class OverviewWindow : Window
{
    private readonly TelemetrySession fixtureSession = new(new NvidiaCollector());
    private readonly DashboardTelemetry? monitoring;
    private readonly MonitoringWorkspace customWorkspace;
    private IReadOnlyList<ProviderSnapshot>? workspaceFixtures;
    private readonly Grid root;
    private readonly Dictionary<string, GpuOverviewCard> cards = [];
    private readonly Dictionary<string, PanelSurface> gpuSurfaces = [];
    private readonly List<PanelSurface> surfaces = [];
    private readonly Dictionary<string, string> deviceColors = [];
    private int nextColor;
    private readonly WrapPanel gpuGrid = new();
    private readonly StackPanel navigation = new(), body = new();
    private readonly ColumnDefinition rail = new() { Width = new(222) };
    private readonly TextBlock health = OverviewTheme.Text("Discovering…", 13, OverviewTheme.Muted);
    private readonly TextBlock summary = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock empty = OverviewTheme.Text("Discovering NVIDIA GPUs…", 16, OverviewTheme.Muted);
    private readonly WrapPanel legend = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TelemetryPlot comparison = new("%", 100) { Height = 148 };
    private readonly HardwareOverviewCard cpuCard = new(true), diskCard = new(false);
    private readonly WrapPanel hardwareRow = new();
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer timer = new();
    private readonly Settings settings;
    private readonly System.Windows.Forms.NotifyIcon? tray;
    private readonly bool savePreferences, usesFixtureData;
    private bool reading, closed, closing, paused, details, collapsed;
    private readonly GpuSensorPanel comparisonPanel;
    private readonly GpuPanelSettings comparisonSettings;
    private DateTimeOffset viewTime;
    private readonly List<Button> rangeButtons = [];
    private static readonly string[] Colors = ["#2B82F8", "#A66CF2", "#33DF32", "#FFBA4D", "#F45A5A", "#39BCD2", "#DBCE32", "#E977BD"];

    public OverviewWindow(bool startMonitoring = true, CollectorConfiguration? configuration = null, DashboardTelemetry? telemetry = null, string? workspaceDirectory = null, string? historyDirectory = null, Settings? preferences = null, bool fixtureData = false)
    {
        settings = preferences ?? (startMonitoring ? Settings.Load() : new Settings()); settings.Normalize(); savePreferences = startMonitoring && preferences == null; usesFixtureData = !startMonitoring || fixtureData;
        OverviewTheme.ApplyAccent(settings.AccentColor);
        historyPath = historyDirectory == null ? Settings.HistoryPath : Path.Combine(historyDirectory, "telemetry.sqlite");
        DevelopmentLog.Write("window", "Creating overview and panel surfaces");
        Title = "GPU Monitor"; Width = 1536; Height = 1024; MinWidth = 680; MinHeight = 540;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0), CornerRadius = new(0) });
        Dispatcher.UnhandledException += (_, e) => DevelopmentLog.Write("ui-error", e.Exception.ToString());
        root = new Grid { Background = OverviewTheme.Canvas }; root.RowDefinitions.Add(new() { Height = new(72) }); root.RowDefinitions.Add(new()); Content = root;
        root.Children.Add(CreateHeader());
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(rail); workspace.ColumnDefinitions.Add(new()); Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        var sidebar = new DockPanel { Background = OverviewTheme.Gradient("#15232E", "#101E29"), Margin = new(0, 1, 0, 0) };
        var quick = OverviewTheme.Card(summary, new(12)); quick.Margin = new(8); DockPanel.SetDock(quick, Dock.Bottom); sidebar.Children.Add(quick);
        sidebar.Children.Add(navigation); workspace.Children.Add(sidebar); BuildNavigation();
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Padding = new(17, 16, 2, 16) };
        Grid.SetColumn(scroll, 1); workspace.Children.Add(scroll);
        empty.Margin = new(0, 30, 0, 30); body.Children.Add(empty); body.Children.Add(gpuGrid);
        hardwareRow.Children.Add(Surface("CPU", cpuCard)); hardwareRow.Children.Add(Surface("Disk", diskCard)); body.Children.Add(hardwareRow);
        customWorkspace = new(WorkspaceSources, workspaceDirectory); body.Children.Add(customWorkspace);
        customWorkspace.LayoutChanged += () => customWorkspace.Visibility = customWorkspace.PanelCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        var comparisonContent = new StackPanel();
        var comparisonHeading = new DockPanel { Margin = new(0, 0, 0, 8) };
        var ranges = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(ranges, Dock.Right); comparisonHeading.Children.Add(ranges);
        foreach (int minutes in new[] { 1, 5, 15, 30 })
        {
            var button = OverviewTheme.Command($"{minutes}m", () => { settings.WindowMinutes = minutes; Save(); Render(viewTime); });
            button.Padding = new(8, 3, 8, 3); button.FontSize = 11; button.Tag = minutes; rangeButtons.Add(button); ranges.Children.Add(button);
        }
        comparisonHeading.Children.Add(OverviewTheme.Text("GPU Utilization (Selected GPUs)", 15)); comparisonContent.Children.Add(comparisonHeading);
        if (!settings.GpuPanels.TryGetValue("comparison", out var savedComparison)) settings.GpuPanels["comparison"] = savedComparison = new() { FollowGpuInventory = true };
        comparisonSettings = savedComparison;
        comparisonPanel = new(comparison, comparisonSettings, Save);
        comparisonContent.Children.Add(comparisonPanel); comparisonContent.Children.Add(legend);
        var compareCard = OverviewTheme.Card(comparisonContent); compareCard.Margin = new(0, 0, 14, 14); body.Children.Add(Surface("GPU comparison", compareCard));
        OverviewTheme.PlotMenu(comparison, "%", "gpu-comparison");
        var footer = OverviewTheme.Text(!usesFixtureData ? "Read-only monitoring · Right-click any graph to inspect or export its source data" : "Synthetic fixture data · actual Windows app render · not live hardware readings", 11, OverviewTheme.Muted);
        footer.TextWrapping = TextWrapping.Wrap; body.Children.Add(footer);
        gpuGrid.SizeChanged += (_, _) => Reflow(); hardwareRow.SizeChanged += (_, _) => Reflow();
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds); timer.Tick += (_, _) => Render(DateTimeOffset.UtcNow);
        if (startMonitoring)
        {
            // One shared NVIDIA/process collector pair; optional hardware/runtime sources require --sources.
            monitoring = telemetry ?? (configuration == null ? new DashboardTelemetry([new NvidiaCollector(), new NvidiaProcessCollector()]) : DashboardTelemetry.Create(configuration));
            tray = new System.Windows.Forms.NotifyIcon { Text = "GPU Monitor", Icon = System.Drawing.SystemIcons.Application, Visible = true };
            InitializeTray();
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
            Loaded += async (_, _) =>
            {
                if (settings.HistoryEnabled) await ConfigureHistoryRecordingAsync(true, settings.HistoryDays);
                await UpdateMonitoringAsync();
            };
        }
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && settings.MinimizeToTray) Hide(); };
        Closing += async (_, e) =>
        {
            if (closing || monitoring == null) return;
            e.Cancel = true;
            if (closed) return;
            closed = true; timer.Stop(); quickView?.Hide();
            try { await monitoring.DisposeAsync(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            finally { closing = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
        };
        Closed += (_, _) => { closed = true; timer.Stop(); foreach (var surface in surfaces) surface.CloseDetached(); foreach (var card in cards.Values) card.CloseDetached(); comparisonPanel.CloseDetached(); quickView?.Dispose(); tray?.Dispose(); };
    }

    private UIElement CreateHeader()
    {
        var bar = new DockPanel { Background = OverviewTheme.Gradient("#14212B", "#0B1923"), LastChildFill = true, Margin = new(0, 0, 0, 1) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
        health.FontSize = 11; health.Margin = new(10, 0, 12, 0); actions.Children.Add(health);
        var settingsButton = OverviewTheme.Command("Settings", OpenSettings, "Monitoring settings"); settingsButton.ToolTip = "Monitoring settings"; settingsButton.FontSize = 12;
        settingsButton.Background = Brushes.Transparent; settingsButton.BorderThickness = new(0); actions.Children.Add(settingsButton);
        DockPanel.SetDock(actions, Dock.Right); bar.Children.Add(actions);
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var minimize = OverviewTheme.Command("—  Minimize", () => WindowState = WindowState.Minimized);
        var maximize = OverviewTheme.Command("□  Maximize", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        var close = OverviewTheme.Command("×  Close", Close);
        foreach (var button in new[] { minimize, maximize, close }) {
            button.Padding = new(10, 10, 10, 10); button.Margin = new(1); button.FontSize = 11;
            windowButtons.Children.Add(button);
        }
        StateChanged += (_, _) => {
            maximize.Content = WindowState == WindowState.Maximized ? "❐  Restore" : "□  Maximize";
            System.Windows.Automation.AutomationProperties.SetName(maximize, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
        };
        DockPanel.SetDock(windowButtons, Dock.Right); bar.Children.Insert(0, windowButtons);
        var collapse = OverviewTheme.Command("☰  Menu", () => { collapsed = !collapsed; rail.Width = new(collapsed ? 72 : 222); BuildNavigation(); }, "Collapse or expand navigation");
        collapse.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(collapse);
        var title = new StackPanel { Margin = new(8, 8, 0, 8) }; var name = OverviewTheme.Text("GPU Monitor", 21); name.FontWeight = FontWeights.SemiBold; title.Children.Add(name);
        title.Children.Add(OverviewTheme.Text("Real-time GPU Monitoring", 13, OverviewTheme.Muted));
        title.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); };
        bar.Children.Add(title); return bar;
    }

    private void BuildNavigation()
    {
        navigation.Children.Clear();
        navigation.Margin = new(0, 14, 0, 0);
        Add("\uE80F", "Overview", () => { details = false; BuildNavigation(); Render(viewTime); }, !details);
        Add("\uE7F4", "GPU Details", () => { details = true; BuildNavigation(); Render(viewTime); }, details);
        Add("\uE71D", "Processes", () => customWorkspace.OpenPicker("Process"), false);
        Add("\uE9D9", "System", () => hardwareRow.BringIntoView(), false);
        Add("\uE713", "Settings", OpenSettings, false);
        void Add(string glyph, string label, Action action, bool selected)
        {
            var button = OverviewTheme.Command(label, action); button.ToolTip = label; button.Margin = new(8, 4, 8, 4); button.Height = 48; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent; button.BorderThickness = new(0);
            if (selected) button.SetResourceReference(Control.BackgroundProperty, "ActionSelection");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = OverviewTheme.Text(glyph, 18, selected ? Brushes.White : OverviewTheme.Muted); icon.FontFamily = new("Segoe MDL2 Assets"); row.Children.Add(icon);
            if (!collapsed) { var text = OverviewTheme.Text(label); text.Margin = new(14, 0, 0, 0); row.Children.Add(text); }
            button.Content = row; OverviewTheme.SetSelected(button, selected); navigation.Children.Add(button);
        }
    }

    private void OpenSettings()
    {
        var menu = OverviewTheme.Menu();
        menu.Items.Add(OverviewTheme.MenuCommand("Open development log", () => {
            DevelopmentLog.Write("user", "Opened development log");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe") { ArgumentList = { DevelopmentLog.Path }, UseShellExecute = false });
        }));
        menu.Items.Add(OverviewTheme.MenuCommand("Add monitoring panel…", () => customWorkspace.OpenPicker()));
        menu.Items.Add(OverviewTheme.MenuCommand("Workspace presets…", () => customWorkspace.PresetsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        menu.Items.Add(OverviewTheme.MenuCommand("Tray quick-view settings…", OpenTraySettings));
        menu.Items.Add(OverviewTheme.MenuCommand("Persistent history…", OpenHistorySettings));
        menu.Items.Add(OverviewTheme.MenuCommand("Browse saved history…", OpenStoredHistory));
        if (monitoring?.HistoryStatus is { Requested: true } archive && (archive.Error != null || archive.DroppedBatches > 0))
            menu.Items.Add(new MenuItem { Header = archive.Error == null ? $"History: {archive.DroppedBatches} dropped batches" : "History unavailable: " + archive.Error, IsEnabled = false });
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
        var accent = new MenuItem { Header = "Accent color" };
        foreach (var choice in new[] { ("Blue", "#229DFF"), ("Green", "#35E544"), ("Purple", "#A66CF2"), ("Amber", "#FFBA4D"), ("Teal", "#39BCD2") }) {
            var item = OverviewTheme.MenuCommand(choice.Item1, () => { settings.AccentColor = choice.Item2; OverviewTheme.ApplyAccent(choice.Item2); Save(); });
            item.IsCheckable = true; item.IsChecked = settings.AccentColor == choice.Item2; accent.Items.Add(item);
        }
        menu.Items.Add(accent);
        var minimize = OverviewTheme.MenuCommand("Minimize to tray", () => { settings.MinimizeToTray = !settings.MinimizeToTray; Save(); });
        minimize.IsCheckable = true; minimize.IsChecked = settings.MinimizeToTray; menu.Items.Add(minimize);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void Save() { DevelopmentLog.Write("settings", "Panel or display preferences changed"); if (!savePreferences) return; try { settings.Save(); } catch (Exception error) { MessageBox.Show(this, error.Message, "Settings could not be saved"); } }
    private void Restore() { quickView?.Hide(); Show(); WindowState = WindowState.Normal; Activate(); }

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
        var snapshots = WorkspaceSources();
        var session = snapshots.Single(s => s.Source == NvidiaTelemetry.SourceId);
        var readings = session.Current == null ? Array.Empty<GpuReading>() : NvidiaSnapshot.Readings(session.Current).ToArray();
        var ids = readings.Select(g => g.Id).ToHashSet();
        if (!ids.SetEquals(cards.Keys)) DevelopmentLog.Write("inventory", $"GPU panel inventory changed: {cards.Count} -> {ids.Count}; health={session.Health}");
        foreach (var id in cards.Keys.Where(id => !ids.Contains(id)).ToArray()) { cards[id].CloseDetached(); gpuSurfaces[id].CloseDetached(); surfaces.Remove(gpuSurfaces[id]); gpuGrid.Children.Remove(gpuSurfaces[id]); gpuSurfaces.Remove(id); cards.Remove(id); deviceColors.Remove(id); }
        foreach (var gpu in readings)
        {
            if (!cards.TryGetValue(gpu.Id, out var card))
            {
                deviceColors[gpu.Id] = Colors[nextColor++ % Colors.Length];
                GpuPanelSettings Panel(string key, string sensor) {
                    string id = gpu.Id + "/" + key;
                    if (!settings.GpuPanels.TryGetValue(id, out var value)) settings.GpuPanels[id] = value = new() { Sensor = sensor, Minutes = settings.WindowMinutes };
                    return value;
                }
                cards[gpu.Id] = card = new(Panel("usage", "utilization.gpu"), Panel("memory", "memory.used"), Save); gpuSurfaces[gpu.Id] = Surface("GPU " + gpu.Index, card, gpu.Id, [new(NvidiaTelemetry.SourceId, gpu.Id, "utilization.gpu"), new(NvidiaTelemetry.SourceId, gpu.Id, "memory.used", 1)]); gpuGrid.Children.Add(gpuSurfaces[gpu.Id]);
            }
            card.Update(gpu, session, settings.WindowMinutes, now, details, snapshots);
        }
        foreach (var surface in surfaces) surface.Refresh(snapshots, now, settings.WindowMinutes);
        // Reorder existing visuals to match the current display indices without changing UUID history.
        for (int i = 0; i < readings.Length; i++)
            if (gpuGrid.Children[i] != gpuSurfaces[readings[i].Id]) { gpuGrid.Children.Remove(gpuSurfaces[readings[i].Id]); gpuGrid.Children.Insert(i, gpuSurfaces[readings[i].Id]); }
        if (readings.Length > 0) {
            if (comparisonSettings.FollowGpuInventory) comparisonSettings.Sensors = readings.Take(16).Select(g => new WorkspaceSeries(NvidiaTelemetry.SourceId, g.Id, "utilization.gpu")).ToList();
            comparisonPanel.Update(readings[0], session, snapshots, now);
        } else { comparison.SetSeries([]); comparisonPanel.CloseDetached(); }
        comparison.Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
        legend.Children.Clear();
        string[] panelColors = ["#33DF32", "#2B82F8", "#A66CF2", "#FFBA4D", "#39BCD2", "#E977BD", "#F45A5A", "#DBCE32"];
        for (int i = 0; i < comparison.SeriesCount; i++) {
            var parts = comparison.SeriesNames[i].Split(" · ");
            var label = OverviewTheme.Text("● " + parts[0] + (parts.Length > 1 ? " · " + parts[^1] : ""), 12, OverviewTheme.Brush(panelColors[i % panelColors.Length]));
            label.Margin = new(12, 0, 12, 0); legend.Children.Add(label);
        }
        health.Text = paused ? "● Paused" : session.Health switch { ProviderHealth.Live => "● Connected", ProviderHealth.Stale => "● Telemetry unavailable", _ => "● Discovering…" };
        health.Foreground = paused || session.Health == ProviderHealth.Stale ? OverviewTheme.Brush("#F9B84D") : OverviewTheme.Green;
        health.ToolTip = session.Error ?? $"Last update: {session.LastSuccess?.ToLocalTime():HH:mm:ss}";
        if (usesFixtureData) { health.Text = "● Test fixture"; health.ToolTip = "Synthetic verification data; no hardware is polled."; }
        empty.Visibility = readings.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = session.Health == ProviderHealth.Starting ? "Discovering NVIDIA GPUs…" : session.Health == ProviderHealth.Stale ? "NVIDIA telemetry unavailable. Check the driver; retrying automatically." : "No NVIDIA GPUs detected.";
        summary.Text = collapsed ? $"{readings.Length} GPUs" : $"{readings.Length} GPUs detected\nLast update  {session.LastSuccess?.ToLocalTime():HH:mm:ss}\n{(paused ? "Paused" : session.Health.ToString())}";
        foreach (var button in rangeButtons) OverviewTheme.SetSelected(button, (int)button.Tag == settings.WindowMinutes);
        cpuCard.Update(snapshots, now, settings.WindowMinutes); diskCard.Update(snapshots, now, settings.WindowMinutes);
        customWorkspace.Visibility = customWorkspace.PanelCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        customWorkspace.Refresh(snapshots, now, settings.WindowMinutes);
        Reflow();
    }
    private PanelSurface Surface(string title, FrameworkElement view, string? identity = null, IReadOnlyList<WorkspaceSeries>? defaults = null) {
        string key = "surface/" + (identity ?? title);
        if (!settings.GpuPanels.TryGetValue(key, out var preferences)) settings.GpuPanels[key] = preferences = new();
        var surface = new PanelSurface(title, view, preferences, WorkspaceSources, Save, defaults);
        surface.Resized += Reflow; surfaces.Add(surface); return surface;
    }
    private void Reflow()
    {
        double width = gpuGrid.ActualWidth; if (width <= 0) return;
        int columns = Math.Min(Math.Max(1, cards.Count), Math.Max(1, (int)Math.Floor(width / 500)));
        FillRows(gpuSurfaces.Values.ToArray(), columns, width);
        int hardwareColumns = width >= 1000 ? 2 : 1;
        FillRows(hardwareRow.Children.Cast<PanelSurface>().ToArray(), hardwareColumns, width);
    }
    private static void FillRows(PanelSurface[] panels, int columns, double width)
    {
        // Saved widths remain proportions, while each row fills the current viewport.
        // Fill the last row too, instead of leaving an empty column beside its panels.
        foreach (var row in panels.Chunk(columns)) {
            double available = Math.Max(1, width - row.Length * 14);
            double minimum = Math.Min(320, available / row.Length);
            double extra = Math.Max(0, available - minimum * row.Length);
            double total = row.Sum(panel => panel.PreferredWidth > 0 ? panel.PreferredWidth : 500);
            foreach (var panel in row)
                panel.Width = minimum + extra * (panel.PreferredWidth > 0 ? panel.PreferredWidth : 500) / total;
        }
    }
    private ProviderSnapshot FixtureSnapshot() => new(NvidiaTelemetry.SourceId, fixtureSession.Health, fixtureSession.Error,
        fixtureSession.LastSuccess, fixtureSession.Current, fixtureSession.HistorySnapshot(), fixtureSession.Definitions);
    private IReadOnlyList<ProviderSnapshot> WorkspaceSources() => monitoring?.Snapshot() ?? workspaceFixtures ?? [FixtureSnapshot()];

    internal void ApplyFixture(IReadOnlyList<GpuReading> readings, DateTimeOffset now, bool render = true)
    { fixtureSession.Apply(NvidiaTelemetry.Normalize(readings), now); if (render) Render(now); }
    internal void VerifyInventory(int expected)
    {
        if (cards.Count != expected || comparison.SeriesCount != expected) throw new InvalidOperationException("Overview must show exactly the detected GPU count.");
    }
    internal void VerifyResponsiveLayout()
    {
        foreach (var group in gpuSurfaces.Values.GroupBy(panel => Math.Round(panel.TranslatePoint(new Point(), gpuGrid).Y))) {
            var row = group.ToArray();
            double right = row.Max(panel => panel.TranslatePoint(new Point(), gpuGrid).X + panel.ActualWidth + panel.Margin.Right);
            if (Math.Abs(right - gpuGrid.ActualWidth) > 2)
                throw new InvalidOperationException($"GPU panels must fill each available row: {right} / {gpuGrid.ActualWidth}.");
        }
    }
    internal IReadOnlyList<TelemetryPlot> VerifyPlots => cards.Values.SelectMany(c => c.Plots).ToArray();
    internal FrameworkElement VerificationRoot => root;
    internal FrameworkElement VerificationHardwareRow => hardwareRow;
    internal FrameworkElement VerificationComparison => comparison;
    internal IReadOnlyList<TelemetryPlot> VerificationHardwarePlots => [cpuCard.Plot, diskCard.Plot];
    internal ProviderSnapshot VerificationGpuSnapshot => monitoring?.Snapshot().Single(s => s.Source == NvidiaTelemetry.SourceId) ?? FixtureSnapshot();
    internal int VerificationDeviceCount => cards.Count;
    internal async Task VerifyPauseAsync(bool value) { paused = value; await UpdateMonitoringAsync(); }
    internal Task VerifyRescanAsync() => RefreshAsync();
    internal MonitoringWorkspace VerifyWorkspace => customWorkspace;
    internal void SetWorkspaceFixtures(IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset now)
    {
        if (monitoring != null) throw new InvalidOperationException("Workspace fixtures require a non-collecting verification window.");
        workspaceFixtures = snapshots; Render(now);
    }
}
