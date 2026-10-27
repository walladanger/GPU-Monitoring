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
    private readonly Dictionary<string, (Window Window, GpuOverviewCard Card)> gpuPopouts = [];
    private readonly Dictionary<bool, (Window Window, HardwareOverviewCard Card)> hardwarePopouts = [];
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
    private readonly GpuProcessView comparisonProcesses = new();
    private (Window Window, TelemetryPlot Plot, GpuProcessView Processes)? comparisonPopout;
    private bool comparisonAsArea;
    private bool? detachedComparisonArea;
    private string? comparisonProcessGpuId;
    private readonly Button comparisonSelectButton;
    private readonly HardwareOverviewCard cpuCard = new(true), diskCard = new(false);
    private readonly WrapPanel hardwareRow = new();
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer timer = new();
    private readonly Settings settings;
    private readonly System.Windows.Forms.NotifyIcon? tray;
    private readonly bool savePreferences, usesFixtureData;
    private bool reading, closed, closing, paused, details, collapsed;
    private string[] comparisonIds = [];
    private DateTimeOffset viewTime;
    private readonly List<Button> rangeButtons = [];
    private static readonly string[] Colors = ["#2B82F8", "#A66CF2", "#33DF32", "#FFBA4D", "#F45A5A", "#39BCD2", "#DBCE32", "#E977BD"];

    public OverviewWindow(bool startMonitoring = true, CollectorConfiguration? configuration = null, DashboardTelemetry? telemetry = null, string? workspaceDirectory = null, string? historyDirectory = null, Settings? preferences = null, bool fixtureData = false)
    {
        settings = preferences ?? (startMonitoring ? Settings.Load() : new Settings()); settings.Normalize(); savePreferences = startMonitoring && preferences == null; usesFixtureData = !startMonitoring || fixtureData;
        historyPath = historyDirectory == null ? Settings.HistoryPath : Path.Combine(historyDirectory, "telemetry.sqlite");
        Title = "GPU Monitor"; Width = 1536; Height = 1024; MinWidth = 680; MinHeight = 540;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0), CornerRadius = new(0) });
        root = new Grid { Background = OverviewTheme.Canvas }; root.RowDefinitions.Add(new() { Height = new(72) }); root.RowDefinitions.Add(new()); Content = root;
        root.Children.Add(CreateHeader());
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(rail); workspace.ColumnDefinitions.Add(new()); Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        var sidebar = new DockPanel { Background = OverviewTheme.Gradient("#15232E", "#101E29"), Margin = new(0, 1, 0, 0) };
        var quick = OverviewTheme.Card(summary, new(12)); quick.Margin = new(8); DockPanel.SetDock(quick, Dock.Bottom); sidebar.Children.Add(quick);
        sidebar.Children.Add(navigation); workspace.Children.Add(sidebar); BuildNavigation();
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Padding = new(17, 16, 2, 16) };
        Grid.SetColumn(scroll, 1); workspace.Children.Add(scroll);
        empty.Margin = new(0, 30, 0, 30); body.Children.Add(empty); body.Children.Add(gpuGrid);
        hardwareRow.Children.Add(cpuCard); hardwareRow.Children.Add(diskCard); body.Children.Add(hardwareRow);
        cpuCard.OpenWindowRequested += () => OpenHardwareWindow(true);
        diskCard.OpenWindowRequested += () => OpenHardwareWindow(false);
        cpuCard.Plot.CursorMoved += SetCursor; diskCard.Plot.CursorMoved += SetCursor;
        customWorkspace = new(WorkspaceSources, workspaceDirectory); customWorkspace.CursorMoved += SetCursor; body.Children.Add(customWorkspace);
        customWorkspace.LayoutChanged += () => customWorkspace.Visibility = customWorkspace.PanelCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        var comparisonContent = new StackPanel();
        var comparisonHeading = new DockPanel { Margin = new(0, 0, 0, 8) };
        var ranges = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(ranges, Dock.Right); comparisonHeading.Children.Add(ranges);
        foreach (int minutes in new[] { 1, 5, 15, 30 })
        {
            var button = OverviewTheme.Command($"{minutes}m", () => { settings.WindowMinutes = minutes; Save(); Render(viewTime); });
            button.Padding = new(8, 3, 8, 3); button.FontSize = 11; button.Tag = minutes; rangeButtons.Add(button); ranges.Children.Add(button);
        }
        comparisonSelectButton = OverviewTheme.Command("Select",
            () => OpenComparisonMenu(comparisonSelectButton, comparison), "Select comparison graph type and GPU sensors");
        comparisonSelectButton.Padding = new(8, 3, 8, 3);
        ranges.Children.Add(comparisonSelectButton);
        var processButton = OverviewTheme.Command("P", comparisonProcesses.Toggle, "Show or hide GPU process details");
        processButton.ToolTip = "Expand GPU processes"; processButton.Padding = new(8, 3, 8, 3);
        ranges.Children.Add(processButton);
        var popoutButton = OverviewTheme.Command("↗", OpenComparisonWindow, "Open GPU comparison in new window");
        popoutButton.Padding = new(8, 3, 8, 3); popoutButton.ToolTip = "Open in new window";
        ranges.Children.Add(popoutButton);
        comparisonHeading.Children.Add(OverviewTheme.Text("GPU Utilization (Selected GPUs)", 15)); comparisonContent.Children.Add(comparisonHeading);
        comparisonContent.Children.Add(comparison); comparisonContent.Children.Add(legend); comparisonContent.Children.Add(comparisonProcesses);
        var compareCard = OverviewTheme.Card(comparisonContent); compareCard.Margin = new(0, 0, 14, 14); body.Children.Add(compareCard);
        OverviewTheme.PlotMenu(comparison, "%", "gpu-comparison"); comparison.CursorMoved += SetCursor;
        var footer = OverviewTheme.Text(!usesFixtureData ? "Read-only monitoring · Right-click any graph to inspect or export its source data" : "Synthetic fixture data · actual Windows app render · not live hardware readings", 11, OverviewTheme.Muted);
        footer.TextWrapping = TextWrapping.Wrap; body.Children.Add(footer);
        gpuGrid.SizeChanged += (_, _) => Reflow(); hardwareRow.SizeChanged += (_, _) => Reflow();
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds); timer.Tick += (_, _) => Render(DateTimeOffset.UtcNow);
        if (startMonitoring)
        {
            // Default GPU process inventory shares NVIDIA polling; other hardware/runtime sources still require --sources.
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
        Closed += (_, _) => { closed = true; timer.Stop(); quickView?.Dispose(); tray?.Dispose(); };
    }

    private UIElement CreateHeader()
    {
        var bar = new DockPanel { Background = OverviewTheme.Gradient("#14212B", "#0B1923"), LastChildFill = true, Margin = new(0, 0, 0, 1) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(8) };
        health.FontSize = 11; health.Margin = new(10, 0, 12, 0); actions.Children.Add(health);
        var settingsButton = OverviewTheme.Command("⋮", OpenSettings, "Monitoring settings"); settingsButton.ToolTip = "Monitoring settings"; settingsButton.FontSize = 23;
        settingsButton.Background = Brushes.Transparent; settingsButton.BorderThickness = new(0); actions.Children.Add(settingsButton);
        DockPanel.SetDock(actions, Dock.Right); bar.Children.Add(actions);
        var chrome = new StackPanel { Width = 72, Margin = new(0, 8, 0, 0) };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var item in new[] { ("#F55050", "Close", (Action)Close), ("#F7BD40", "Minimize", (Action)(() => WindowState = WindowState.Minimized)),
            ("#26CA54", "Maximize or restore", (Action)(() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)) })
        {
            var dot = OverviewTheme.Command("", item.Item3, item.Item2); dot.Width = dot.Height = 13; dot.Padding = new(0); dot.Margin = new(3, 4, 3, 3);
            dot.Background = OverviewTheme.Brush(item.Item1); dot.BorderThickness = new(0); dot.ToolTip = item.Item2; dots.Children.Add(dot);
        }
        chrome.Children.Add(dots);
        var collapse = OverviewTheme.Command("‹", () => { collapsed = !collapsed; rail.Width = new(collapsed ? 72 : 222); BuildNavigation(); }, "Collapse or expand navigation");
        collapse.Background = Brushes.Transparent; collapse.BorderThickness = new(0); collapse.FontSize = 23; collapse.Padding = new(0); collapse.Height = 26; chrome.Children.Add(collapse);
        bar.Children.Add(chrome);
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
            button.Background = selected ? OverviewTheme.Gradient("#1E5892", "#134578") : Brushes.Transparent; button.BorderThickness = new(0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = OverviewTheme.Text(glyph, 18, selected ? Brushes.White : OverviewTheme.Muted); icon.FontFamily = new("Segoe MDL2 Assets"); row.Children.Add(icon);
            if (!collapsed) { var text = OverviewTheme.Text(label); text.Margin = new(14, 0, 0, 0); row.Children.Add(text); }
            button.Content = row; navigation.Children.Add(button);
        }
    }

    private void OpenSettings()
    {
        var menu = OverviewTheme.Menu();
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
        var minimize = OverviewTheme.MenuCommand("Minimize to tray", () => { settings.MinimizeToTray = !settings.MinimizeToTray; Save(); });
        minimize.IsCheckable = true; minimize.IsChecked = settings.MinimizeToTray; menu.Items.Add(minimize);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void Save() { if (!savePreferences) return; try { settings.Save(); } catch (Exception error) { MessageBox.Show(this, error.Message, "Settings could not be saved"); } }
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
        foreach (var id in cards.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            if (gpuPopouts.TryGetValue(id, out var popped)) popped.Window.Close();
            gpuGrid.Children.Remove(cards[id]); cards.Remove(id); deviceColors.Remove(id);
        }
        foreach (var gpu in readings)
        {
            if (!cards.TryGetValue(gpu.Id, out var card))
            {
                deviceColors[gpu.Id] = Colors[nextColor++ % Colors.Length];
                cards[gpu.Id] = card = new(); gpuGrid.Children.Add(card);
                string gpuId = gpu.Id;
                card.OpenWindowRequested += () => OpenGpuWindow(gpuId);
                card.SelectRequested += () => customWorkspace.OpenPicker(gpuId);
                foreach (var plot in card.Plots) plot.CursorMoved += SetCursor;
            }
            card.Update(gpu, session, snapshots, settings.WindowMinutes, now, details);
            if (gpuPopouts.TryGetValue(gpu.Id, out var popped))
                popped.Card.Update(gpu, session, snapshots, settings.WindowMinutes, now, details);
        }
        // Reorder existing visuals to match the current display indices without changing UUID history.
        for (int i = 0; i < readings.Length; i++)
            if (gpuGrid.Children[i] != cards[readings[i].Id]) { gpuGrid.Children.Remove(cards[readings[i].Id]); gpuGrid.Children.Insert(i, cards[readings[i].Id]); }
        bool comparisonChanged = !comparisonIds.SequenceEqual(readings.Select(g => g.Id));
        if (comparisonChanged)
        {
            comparisonIds = readings.Select(g => g.Id).ToArray();
            comparison.SetSeries(readings.Select(g => ($"GPU {g.Index}", deviceColors[g.Id])).ToArray(), comparisonAsArea);
        }
        comparison.Update(readings.Select(g => session.Series(g.Id, "utilization.gpu")).ToArray(), now, settings.WindowMinutes);
        comparison.Opacity = session.Health == ProviderHealth.Stale ? .5 : 1;
        comparisonProcessGpuId ??= readings.FirstOrDefault()?.Id;
        bool comparisonGpuSelected = comparisonProcessGpuId != null && ids.Contains(comparisonProcessGpuId);
        if (!comparisonGpuSelected) comparisonProcesses.IsShown = false;
        if (comparisonGpuSelected) comparisonProcesses.Update(comparisonProcessGpuId!, snapshots);
        if (comparisonPopout is { } detached)
        {
            if (comparisonChanged || detached.Plot.SeriesCount != readings.Length || detachedComparisonArea != comparisonAsArea)
            {
                detached.Plot.SetSeries(readings.Select(g => ($"GPU {g.Index}", deviceColors[g.Id])).ToArray(), comparisonAsArea);
                detachedComparisonArea = comparisonAsArea;
            }
            for (int i = 0; i < readings.Length; i++) detached.Plot.SetName(i, $"GPU {readings[i].Index}");
            detached.Plot.Update(readings.Select(g => session.Series(g.Id, "utilization.gpu")).ToArray(), now, settings.WindowMinutes);
            detached.Plot.Opacity = comparison.Opacity;
            if (!comparisonGpuSelected) detached.Processes.IsShown = false;
            if (comparisonGpuSelected) detached.Processes.Update(comparisonProcessGpuId!, snapshots);
        }
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
        if (usesFixtureData) { health.Text = "● Test fixture"; health.ToolTip = "Synthetic verification data; no hardware is polled."; }
        empty.Visibility = readings.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = session.Health == ProviderHealth.Starting ? "Discovering NVIDIA GPUs…" : session.Health == ProviderHealth.Stale ? "NVIDIA telemetry unavailable. Check the driver; retrying automatically." : "No NVIDIA GPUs detected.";
        summary.Text = collapsed ? $"{readings.Length} GPUs" : $"{readings.Length} GPUs detected\nLast update  {session.LastSuccess?.ToLocalTime():HH:mm:ss}\n{(paused ? "Paused" : session.Health.ToString())}";
        foreach (var button in rangeButtons) button.Background = (int)button.Tag == settings.WindowMinutes ? OverviewTheme.Brush("#184880") : OverviewTheme.Surface;
        cpuCard.Update(snapshots, now, settings.WindowMinutes); diskCard.Update(snapshots, now, settings.WindowMinutes);
        foreach (var hardwareDetached in hardwarePopouts.Values)
            hardwareDetached.Card.Update(snapshots, now, settings.WindowMinutes);
        customWorkspace.Visibility = customWorkspace.PanelCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        customWorkspace.Refresh(snapshots, now, settings.WindowMinutes);
        Reflow();
    }
    private void OpenComparisonMenu(Button target, TelemetryPlot plot)
    {
        var menu = OverviewTheme.Menu();
        var gpuChoices = WorkspaceSources().FirstOrDefault(s => s.Source == NvidiaTelemetry.SourceId)?.Current?.Devices
            .Where(d => d.Kind == DeviceKind.Gpu).ToArray() ?? [];
        var visible = new MenuItem { Header = "Visible GPU series" };
        for (int i = 0; i < Math.Min(plot.SeriesCount, gpuChoices.Length); i++)
        {
            int index = i;
            var gpu = gpuChoices[i];
            var item = OverviewTheme.MenuCommand($"GPU {gpu.DisplayIndex} · {gpu.Name}",
                () => plot.ShowSeries(index, !plot.IsSeriesVisible(index)));
            item.IsCheckable = true; item.IsChecked = plot.IsSeriesVisible(index); visible.Items.Add(item);
        }
        if (visible.Items.Count == 0) visible.IsEnabled = false;
        menu.Items.Add(visible);
        var chartMode = new MenuItem { Header = "Graph type" };
        foreach (bool area in new[] { false, true })
        {
            var item = OverviewTheme.MenuCommand(area ? "Area" : "Line", () =>
            {
                comparisonAsArea = area; comparisonIds = []; Render(viewTime);
            });
            item.IsCheckable = true; item.IsChecked = comparisonAsArea == area; chartMode.Items.Add(item);
        }
        menu.Items.Add(chartMode);
        var processes = new MenuItem { Header = "GPU for process details" };
        foreach (var gpu in gpuChoices)
        {
            var item = OverviewTheme.MenuCommand($"GPU {gpu.DisplayIndex} · {gpu.Name}",
                () => { comparisonProcessGpuId = gpu.Id; Render(viewTime); });
            item.IsCheckable = true; item.IsChecked = comparisonProcessGpuId == gpu.Id; processes.Items.Add(item);
        }
        if (processes.Items.Count == 0) processes.IsEnabled = false;
        menu.Items.Add(processes);
        menu.Items.Add(OverviewTheme.MenuCommand("Create custom GPU sensor panel…",
            () => customWorkspace.OpenPicker("utilization.gpu")));
        menu.PlacementTarget = target; menu.IsOpen = true;
    }

    private void OpenComparisonWindow()
    {
        if (comparisonPopout is { } old) { old.Window.Activate(); return; }
        var plot = new TelemetryPlot("%", 100) { Height = 270 };
        var processes = new GpuProcessView();
        var body = new StackPanel { Margin = new(14) };
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) };
        var select = OverviewTheme.Command("Select", () => { }, "Select comparison graph and GPU sensors");
        select.Click += (_, _) => OpenComparisonMenu(select, plot);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var showProcesses = OverviewTheme.Command("Processes", processes.Toggle, "Show or hide GPU process details");
        tools.Children.Add(showProcesses); tools.Children.Add(select);
        DockPanel.SetDock(tools, Dock.Right); heading.Children.Add(tools);
        heading.Children.Add(OverviewTheme.Text("GPU utilization comparison", 16));
        body.Children.Add(heading); body.Children.Add(plot); body.Children.Add(processes);
        OverviewTheme.PlotMenu(plot, "%", "detached-gpu-comparison");
        plot.CursorMoved += SetCursor;
        var window = new Window
        {
            Owner = this, Title = "GPU comparison · GPU Monitor",
            Width = 860, Height = 560, MinWidth = 340, MinHeight = 280,
            Background = OverviewTheme.Canvas, Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"), ResizeMode = ResizeMode.CanResize,
            Content = new ScrollViewer
            { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
              HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        window.SizeChanged += (_, _) =>
        {
            plot.Width = Math.Max(270, window.ActualWidth - 56);
            plot.Height = Math.Max(160, window.ActualHeight - 220);
        };
        window.Closed += (_, _) => { comparisonPopout = null; detachedComparisonArea = null; };
        comparisonPopout = (window, plot, processes);
        Render(viewTime);
        window.Show();
    }

    private void OpenHardwareWindow(bool cpu)
    {
        if (hardwarePopouts.TryGetValue(cpu, out var current)) { current.Window.Activate(); return; }
        var card = new HardwareOverviewCard(cpu);
        var window = new Window
        {
            Owner = this, Title = (cpu ? "CPU" : "Disk") + " · GPU Monitor",
            Width = 740, Height = 460, MinWidth = 340, MinHeight = 280,
            Background = OverviewTheme.Canvas, Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"), ResizeMode = ResizeMode.CanResize,
            Content = new ScrollViewer
            { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new(14) }
        };
        window.SizeChanged += (_, _) =>
        {
            card.Width = Math.Max(290, window.ActualWidth - 56);
            card.Plot.Height = Math.Max(112, window.ActualHeight - 230);
        };
        window.Closed += (_, _) => hardwarePopouts.Remove(cpu);
        hardwarePopouts.Add(cpu, (window, card));
        card.Update(WorkspaceSources(), viewTime, settings.WindowMinutes);
        window.Show();
    }

    private void OpenGpuWindow(string gpuId)
    {
        if (gpuPopouts.TryGetValue(gpuId, out var old)) { old.Window.Activate(); return; }
        var card = new GpuOverviewCard();
        card.SelectRequested += () => customWorkspace.OpenPicker(gpuId);
        var window = new Window
        {
            Title = "GPU Monitor · GPU details",
            Width = 800, Height = 730, MinWidth = 390, MinHeight = 330,
            Background = OverviewTheme.Canvas, Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"), ResizeMode = ResizeMode.CanResize,
            Content = new ScrollViewer
            { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(14) }
        };
        window.SizeChanged += (_, _) =>
        {
            card.Width = Math.Max(320, window.ActualWidth - 56);
            double chartHeight = Math.Max(128, (window.ActualHeight - 340) / 2);
            card.Usage.Height = card.Memory.Height = chartHeight;
        };
        window.Closed += (_, _) => gpuPopouts.Remove(gpuId);
        gpuPopouts.Add(gpuId, (window, card));
        var sources = WorkspaceSources();
        var source = sources.FirstOrDefault(s => s.Source == NvidiaTelemetry.SourceId);
        var reading = source?.Current == null ? null : NvidiaSnapshot.Readings(source.Current).FirstOrDefault(g => g.Id == gpuId);
        if (reading != null && source != null) card.Update(reading, source, sources, settings.WindowMinutes, viewTime, details);
        window.Show();
    }

    private void Reflow()
    {
        double width = gpuGrid.ActualWidth; if (width <= 0) return;
        int columns = Math.Min(Math.Max(1, cards.Count), Math.Max(1, (int)Math.Floor(width / 500)));
        foreach (var card in cards.Values) card.Width = Math.Max(280, width / columns - 14);
        int hardwareColumns = width >= 1000 ? 2 : 1;
        cpuCard.Width = diskCard.Width = Math.Max(280, width / hardwareColumns - 14);
    }
    private void SetCursor(DateTimeOffset? time) { comparison.SetCursor(time); foreach (var card in cards.Values) foreach (var plot in card.Plots) plot.SetCursor(time); customWorkspace.SetCursor(time); cpuCard.Plot.SetCursor(time); diskCard.Plot.SetCursor(time); }
    private ProviderSnapshot FixtureSnapshot() => new(NvidiaTelemetry.SourceId, fixtureSession.Health, fixtureSession.Error,
        fixtureSession.LastSuccess, fixtureSession.Current, fixtureSession.HistorySnapshot(), fixtureSession.Definitions);
    private IReadOnlyList<ProviderSnapshot> WorkspaceSources() => monitoring?.Snapshot() ?? workspaceFixtures ?? [FixtureSnapshot()];

    internal void ApplyFixture(IReadOnlyList<GpuReading> readings, DateTimeOffset now, bool render = true)
    { fixtureSession.Apply(NvidiaTelemetry.Normalize(readings), now); if (render) Render(now); }
    internal void VerifyInventory(int expected)
    {
        if (cards.Count != expected || comparison.SeriesCount != expected) throw new InvalidOperationException("Overview must show exactly the detected GPU count.");
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
