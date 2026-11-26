using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GpuMonitor;

// Views share the dashboard's immutable snapshots; opening a window starts no collector.
internal sealed class GpuSensorPanel : StackPanel
{
    private readonly GpuPanelSettings settings;
    private readonly Action save;
    private readonly TextBlock label = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock processes = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly Expander expander;
    private GpuReading? gpu;
    private ProviderSnapshot? session;
    private IReadOnlyList<ProviderSnapshot> sources = [];
    private DateTimeOffset now;
    private string? signature;
    private GpuSensorPanel? detached;
    private Window? window;
    private double? surfaceHeight;
    internal void SetViewHeight(double? height) { surfaceHeight = height; Refresh(); }
    public TelemetryPlot Plot { get; }

    public GpuSensorPanel(TelemetryPlot plot, GpuPanelSettings settings, Action save)
    {
        this.settings = settings; this.save = save; settings.Normalize(); Plot = plot;
        var heading = new DockPanel();
        var options = OverviewTheme.Command("Options", Options, "Select sensor, graph type and display settings");
        var open = OverviewTheme.Command("↗ Open", OpenWindow, "Open in new window");
        open.ToolTip = "Open in new window"; options.ToolTip = "Panel actions";
        DockPanel.SetDock(open, Dock.Right); heading.Children.Add(open);
        DockPanel.SetDock(options, Dock.Right); heading.Children.Add(options); heading.Children.Add(label);
        Children.Add(heading);
        var graphHost = new Grid(); graphHost.Children.Add(plot); Children.Add(graphHost);
        processes.TextWrapping = TextWrapping.Wrap;
        expander = new Expander { Header = "GPU processes", Foreground = OverviewTheme.Muted, Content = processes, IsExpanded = settings.Processes };
        expander.Expanded += (_, _) => { settings.Processes = true; save(); Refresh(); };
        expander.Collapsed += (_, _) => { settings.Processes = false; save(); };
        Children.Add(expander);
        var factory = new FrameworkElementFactory(typeof(Border)); factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var grip = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNS,
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory }, ToolTip = "Drag to resize graph" };
        System.Windows.Automation.AutomationProperties.SetName(grip, "Resize GPU graph");
        double initial = 0, pointer = 0;
        grip.DragStarted += (_, _) => { initial = settings.Height; pointer = PointToScreen(Mouse.GetPosition(this)).Y; };
        grip.DragDelta += (_, _) => { settings.Height = Math.Clamp(initial + (PointToScreen(Mouse.GetPosition(this)).Y - pointer) / VisualTreeHelper.GetDpi(this).DpiScaleY, 100, 800); Refresh(); };
        grip.DragCompleted += (_, e) => { if (e.Canceled) settings.Height = initial; save(); Refresh(); };
        grip.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) grip.CancelDrag(); };
        graphHost.Children.Add(grip);
    }

    public void Update(GpuReading reading, ProviderSnapshot snapshot, IReadOnlyList<ProviderSnapshot> all, DateTimeOffset time)
    { gpu = reading; session = snapshot; sources = all; now = time; Refresh(); detached?.Update(reading, snapshot, all, time); }

    private void Refresh()
    {
        Plot.Height = surfaceHeight ?? (settings.Lanes ? Math.Max(480, (settings.Sensors?.Count ?? 1) * 90) : settings.Height);
        if (gpu == null || session == null) return;
        var selections = settings.Sensors ?? [new(NvidiaTelemetry.SourceId, gpu.Id, settings.Sensor)];
        var panel = new WorkspacePanel("gpu-view", "GPU panel", 1, 1, settings.Lanes ? "lanes" : settings.Area ? "area" : "line", selections);
        var data = WorkspaceMetrics.Project(panel, sources.Count == 0 ? [session] : sources);
        if (data.Error != null) { label.Text = data.Error; Plot.Visibility = Visibility.Collapsed; return; }
        Plot.Visibility = Visibility.Visible;
        if (data.Series.Count == 1 && selections[0].DeviceId == gpu.Id && selections[0].MetricKey == "memory.used")
            data = data with { Axes = data.Axes.Select(a => a with { MaximumOverride = gpu.Metrics["memory.total"] / 1024 }).ToArray() };
        string next = string.Join("|", selections) + settings.Area + settings.Lanes + string.Join("|", data.Axes);
        if (signature != next || Plot.SeriesCount != data.Series.Count)
        {
            signature = next;
            string[] colors = ["#33DF32", "#2B82F8", "#A66CF2", "#FFBA4D", "#39BCD2", "#E977BD", "#F45A5A", "#DBCE32"];
            Plot.SetSeries(data.Series.Select((s, i) => (s.Name, s.Selection.MetricKey == "memory.used" && data.Series.Count == 1 ? "#2B82F8" : colors[i % colors.Length])).ToArray(), settings.Area);
            Plot.ConfigureAxes(data.Axes, selections.Select(s => s.Axis).ToArray()); Plot.SetLaneMode(settings.Lanes);
            OverviewTheme.PlotMenu(Plot, data.Unit, gpu.Id + "-panel");
        }
        for (int i = 0; i < data.Series.Count; i++) Plot.SetName(i, data.Series[i].Name);
        Plot.Update(data.Series.Select(s => s.Samples).ToArray(), now, settings.Minutes);
        label.Text = string.Join(" · ", data.Series.Select(s => s.Name.Split(" · ").Last() + " (" + WorkspaceMetrics.StateLabel(s.State) + ")"));
        label.TextWrapping = TextWrapping.Wrap;
        if (expander.IsExpanded) {
            var gpuIds = selections.Where(s => s.Source == NvidiaTelemetry.SourceId).Select(s => s.DeviceId).Distinct().ToArray();
            processes.Text = gpuIds.Length == 0 ? "No GPU resource selected; GPU process attribution unavailable." : string.Join("\n\n", gpuIds.Select(id => id + "\n" + ProcessText(id, sources)));
        }
    }

    internal static string ProcessText(string gpuId, IReadOnlyList<ProviderSnapshot> sources)
    {
        var source = sources.FirstOrDefault(s => s.Source == "nvidia-processes");
        const string limits = "Compute processes only; graphics processes and per-process GPU utilization are unavailable from this collector. Power, temperature, fan and clocks cannot be attributed per process.";
        if (source?.Current == null || source.Health != ProviderHealth.Live)
            return "GPU process data unavailable (collector disabled, driver unsupported, permissions, or stale readings).\n" + limits;
        var rows = source.Current.Samples.Where(s => s.Device.Id.StartsWith(gpuId + "/", StringComparison.Ordinal) && s.MetricKey == "process.gpu_memory")
            .OrderByDescending(s => s.Value).Select(s => $"{s.Device.Name} · PID {s.Device.DisplayIndex} · VRAM {(s.Value.HasValue ? $"{s.Value:0.#} MiB" : "unavailable")} · GPU utilization unavailable").ToArray();
        return (rows.Length == 0 ? "No compute processes reported for this GPU." : string.Join("\n", rows)) + "\n" + limits;
    }

    private void Options()
    {
        var menu = OverviewTheme.Menu();
        foreach (var metric in Metrics.All)
        {
            var item = OverviewTheme.MenuCommand(metric.Title, () => { settings.FollowGpuInventory = false; settings.Sensors = null; settings.Sensor = metric.Key; save(); Refresh(); });
            item.IsCheckable = true; item.IsChecked = settings.Sensor == metric.Key; menu.Items.Add(item);
        }
        menu.Items.Insert(0, OverviewTheme.MenuCommand("Select multiple sensors / axes…", () => {
            if (gpu == null) return;
            var panel = new WorkspacePanel("gpu-view", "GPU panel", 1, 1, settings.Lanes ? "lanes" : settings.Area ? "area" : "line", settings.Sensors ?? [new(NvidiaTelemetry.SourceId, gpu.Id, settings.Sensor)]);
            var editor = new WorkspacePanelEditor(Window.GetWindow(this), sources, panel);
            if (editor.ShowDialog() == true && editor.Result != null) { settings.FollowGpuInventory = false; settings.Sensors = editor.Result.Series.ToList(); settings.Area = editor.Result.Chart == "area"; settings.Lanes = editor.Result.Chart == "lanes"; save(); Refresh(); }
        }));
        menu.Items.Add(new Separator());
        foreach (bool area in new[] { false, true })
        {
            var item = OverviewTheme.MenuCommand(area ? "Area graph" : "Line graph", () => { settings.Area = area; settings.Lanes = false; save(); Refresh(); });
            item.IsCheckable = true; item.IsChecked = settings.Area == area; menu.Items.Add(item);
        }
        menu.Items.Add(OverviewTheme.MenuCommand("Stacked lanes (up to 8 sensors)", () => { if ((settings.Sensors?.Count ?? 1) <= 8) { settings.Lanes = true; save(); Refresh(); } }));
        foreach (int minutes in new[] { 1, 5, 15, 30 })
            menu.Items.Add(OverviewTheme.MenuCommand($"Show {minutes} minutes", () => { settings.Minutes = minutes; save(); Refresh(); }));
        menu.PlacementTarget = this; menu.IsOpen = true;
    }

    internal void OpenWindow()
    {
        if (window != null) { window.Activate(); return; }
        detached = new(new TelemetryPlot("%"), settings, save);
        window = new Window { Title = $"GPU {gpu?.Index} sensor", Width = 700, Height = 500, MinWidth = 360, MinHeight = 280,
            Background = OverviewTheme.Canvas, Foreground = Brushes.White, Content = new ScrollViewer { Content = detached, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, Owner = Window.GetWindow(this) };
        var view = detached;
        window.SizeChanged += (_, _) => { if (window != null) { settings.Height = Math.Clamp(window.ActualHeight - 190, 100, 800); view.Width = Math.Max(320, window.ActualWidth - 32); view.Refresh(); } };
        window.Closed += (_, _) => { view.CloseDetached(); window = null; detached = null; save(); };
        if (gpu != null && session != null) detached.Update(gpu, session, sources, now);
        window.Show();
    }
    internal void CloseDetached() => window?.Close();
}
