using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GpuMonitor;

internal static class PanelSurfaceChecks
{
    public static void Run()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "panel-verification"); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var results = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); results.Add("PASS: " + message); }
        try
        {
            string logPath = Path.Combine(directory, "development.log");
            DevelopmentLog.Start(logPath); DevelopmentLog.Write("verification", "Panel checks started");
            Check(File.ReadAllText(logPath).Contains("[startup]") && File.ReadAllText(logPath).Contains("Panel checks started"), "Development logging starts automatically and writes diagnostic events");
            var now = DateTimeOffset.UtcNow;
            var gpu = CsvTelemetry.Parse("0, NVIDIA fixture, GPU-A, 00000000:01:00.0, 24576, 35, 4096, 48, 120, 40, 1500, 9750", now).Single();
            var batch = NvidiaTelemetry.Normalize([gpu]);
            var snapshot = new ProviderSnapshot(NvidiaTelemetry.SourceId, ProviderHealth.Live, null, now, batch, batch.Samples, NvidiaTelemetry.Definitions);
            var processBatch = NvidiaProcessCollector.Parse("GPU-A, 42, example.exe, 512\nGPU-B, 43, other.exe, [N/A]", now, _ => now.UtcDateTime);
            var process = new ProviderSnapshot("nvidia-processes", ProviderHealth.Live, null, now, processBatch, processBatch.Samples, new NvidiaProcessCollector().Definitions);
            IReadOnlyList<ProviderSnapshot> sources = [snapshot, process];
            var text = GpuSensorPanel.ProcessText("GPU-A", sources);
            Check(text.Contains("example.exe") && text.Contains("512 MiB") && !text.Contains("other.exe"), "Process attribution filters by GPU UUID and preserves measured VRAM");
            Check(GpuSensorPanel.ProcessText("GPU-B", sources).Contains("VRAM unavailable"), "Unavailable per-process memory is not reported as zero");
            Check(GpuSensorPanel.ProcessText("GPU-C", sources).Contains("No compute processes"), "Empty process inventory is explicit");
            Check(GpuSensorPanel.ProcessText("GPU-A", [snapshot, process with { Health = ProviderHealth.Stale }]).Contains("data unavailable"), "Stale processes cannot appear live");
            Check(GpuSensorPanel.ProcessText("GPU-A", [snapshot]).Contains("collector disabled"), "Disabled process collection has an explicit state");
            var preferences = new GpuPanelSettings { Sensors = [new(NvidiaTelemetry.SourceId, "GPU-A", "utilization.gpu"), new(NvidiaTelemetry.SourceId, "GPU-A", "memory.used", 1)], SurfaceWidth = 500, SurfaceHeight = 240 };
            var plot = new TelemetryPlot("%"); var sensor = new GpuSensorPanel(plot, preferences, () => {});
            sensor.Update(gpu, snapshot, sources, now);
            Check(plot.SeriesCount == 2, "GPU graph renders multiple sensors with independent unit axes");
            int saves = 0;
            var surface = new PanelSurface("GPU fixture", sensor, new GpuPanelSettings { SurfaceWidth = 500, SurfaceHeight = 240 }, () => sources, () => saves++);
            var host = new Window { Title = "Synthetic panel verification", Width = 850, Height = 650, Content = surface, Background = OverviewTheme.Canvas };
            host.Show(); host.UpdateLayout();
            var edge = surface.ResizeHandle.TranslatePoint(new Point(surface.ResizeHandle.ActualWidth, surface.ResizeHandle.ActualHeight), surface);
            Check(Math.Abs(edge.X - surface.ActualWidth) <= 2 && Math.Abs(edge.Y - surface.ActualHeight) <= 2, "Resize grab area touches the outer panel corner without an inset control row");
            surface.Refresh(sources, now, 5);
            var pointer = new Point(100, 100); surface.PointerPosition = () => pointer;
            surface.ResizeHandle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            pointer = new(220, 40); surface.ResizeHandle.RaiseEvent(new DragDeltaEventArgs(120, -60) { RoutedEvent = Thumb.DragDeltaEvent });
            Check(surface.PreferredWidth > 500, "Drag resizes panel surface before the gesture completes");
            surface.ResizeHandle.RaiseEvent(new DragCompletedEventArgs(120, -60, true) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(surface.PreferredWidth == 500, "Cancelled resize restores the previous saved size");
            var rightEdge = Descendants<Thumb>(surface).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Resize panel right edge");
            pointer = new(100, 100); rightEdge.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            pointer = new(180, 160); rightEdge.RaiseEvent(new DragDeltaEventArgs(80, 60) { RoutedEvent = Thumb.DragDeltaEvent });
            Check(surface.PreferredWidth > 500 && scrollHeight(surface) == 240, "Right-edge drag changes width while preserving panel height");
            rightEdge.RaiseEvent(new DragCompletedEventArgs(80, 60, true) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(plot.CurrentReadingsText.Contains("35%") && plot.CurrentReadingsText.Contains("4 GiB"), "Multi-sensor current values render inside the graph with correct unit conversion");
            var sample = snapshot.Series("GPU-A", "utilization.gpu").Single();
            var samplePoint = plot.VerificationSamplePosition(0, sample);
            plot.SelectNearestPoint(samplePoint);
            Check(plot.SelectedSample == sample, "Left-click hit testing selects the actual measured sample");
            sensor.Update(gpu, snapshot, sources, now.AddSeconds(2)); plot.SetCursor(now.AddSeconds(-20));
            Check(plot.SelectedSample == sample, "Selected sample stays pinned during hover movement and telemetry refresh");
            plot.SelectNearestPoint(plot.VerificationSamplePosition(0, sample));
            Check(plot.SelectedSample == null, "Clicking a selected point again deselects it");
            plot.SelectNearestPoint(plot.VerificationSamplePosition(0, sample));
            host.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent, Source = host });
            Check(plot.SelectedSample == null, "Clicking outside the graph clears pinned selection");
            Check(Descendants<System.Windows.Shapes.Rectangle>(plot).Any(r => r.Effect is System.Windows.Media.Effects.BlurEffect), "Point information blurs its sampled backdrop while keeping text sharp");
            surface.OpenDetached();
            var detached = surface.DetachedWindow;
            Check(detached != null && detached.IsVisible && detached.Owner == host, "Open in new window creates a real owned native window");
            int count = app.Windows.Count; surface.OpenDetached();
            Check(app.Windows.Count == count, "Repeated open activates the existing panel window");
            double oldWindowWidth = detached!.Width; detached.Width += 80; detached.Height += 80; detached.UpdateLayout();
            Check(detached.ActualWidth > oldWindowWidth && Window.GetWindow(sensor) == detached, "Detached native window resizes independently and retains its live view");
            Check(Window.GetWindow(sensor) == detached, "Detaching moves the same live view without a second collector");
            sensor.Update(gpu with { Index = "2" }, snapshot, sources, now.AddSeconds(1));
            Check(plot.SeriesCount == 2, "Detached view continues receiving shared snapshot updates");
            surface.CloseDetached(); host.UpdateLayout();
            Check(Window.GetWindow(sensor) == host && surface.DetachedWindow == null, "Closing detached window restores the original panel view");
            var scrolls = Descendants<ScrollViewer>(surface).ToArray();
            Check(scrolls.Any(s => s.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && s.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto), "Panel surface enables automatic scrollbars on both axes");
            var settingsPath = Path.Combine(directory, "settings.json");
            new Settings { GpuPanels = new() { ["GPU-A"] = preferences } }.Save(settingsPath);
            var loaded = Settings.Load(settingsPath).GpuPanels["GPU-A"];
            Check(loaded.Sensors?.Count == 2 && loaded.SurfaceWidth == 500 && loaded.SurfaceHeight == 240, "Panel size and multiple sensor settings survive persistence");
            var invalid = new GpuPanelSettings { Height = double.NaN, SurfaceWidth = double.PositiveInfinity, SurfaceHeight = -2, Sensor = "missing", Minutes = 900 };
            invalid.Normalize(); Check(invalid.Height == 128 && invalid.SurfaceWidth == 0 && invalid.SurfaceHeight == 0 && invalid.Minutes == 5, "Invalid saved dimensions and range recover safely");
            var image = new RenderTargetBitmap(850, 650, 96, 96, PixelFormats.Pbgra32); image.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(Path.Combine(directory, "panels.png"))) encoder.Save(file);
            host.Content = null;
            string[] units = ["%", "B", "MiB", "W", "°C", "MHz", "rpm", "tokens/s"];
            var multi = new TelemetryPlot("multi") { Height = 800 };
            multi.SetSeries(units.Select((u, i) => ($"Sensor {i + 1} [{u}]", "#33DF32")).ToArray(), false);
            multi.ConfigureAxes(units.Select((u, i) => new WorkspaceAxis(i, u)).ToArray(), Enumerable.Range(0, 8).ToArray()); multi.SetLaneMode(true);
            var device = new DeviceIdentity("fixture", "eight", DeviceKind.Gpu, "Eight-type fixture", "0");
            var histories = units.Select((u, i) => (IReadOnlyList<TelemetrySample>)Enumerable.Range(0, 31).Select(t => new TelemetrySample(device, "metric-" + i, now.AddSeconds(-300 + t * 10), (i + 1) * 10 + t)).ToArray()).ToArray();
            multi.Update(histories, now, 5); host.Content = multi; host.Height = 950; host.UpdateLayout();
            Check(multi.SeriesCount == 8 && multi.ExportRows("multi").Select(r => r.Unit).Distinct().Count() == 8, "Eight distinct unit types render together in stacked lanes and export their original raw units");
            var laneSample = histories[6][15]; multi.SelectNearestPoint(multi.VerificationSamplePosition(6, laneSample));
            Check(multi.SelectedSample == laneSample, "Point selection resolves the correct sensor in an eight-type lane graph");
            var zoomIn = Descendants<System.Windows.Controls.Button>(multi).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Zoom in");
            zoomIn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(multi.IsViewPaused && multi.ViewRange!.Duration.TotalMinutes < 5, "Visible zoom-in button narrows the chart range");
            var reset = Descendants<System.Windows.Controls.Button>(multi).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Reset to live"); reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!multi.IsViewPaused && multi.ViewRange!.Duration.TotalMinutes == 5, "Visible reset button returns the chart to live data");
            var menu = OverviewTheme.Menu(); menu.Items.Add(OverviewTheme.MenuCommand("Panel action", () => {})); menu.PlacementTarget = multi; menu.IsOpen = true; menu.ApplyTemplate();
            Check(Descendants<FrostedPanel>(menu).Any(), "Action menus use rounded translucent frosted pop-out panels"); menu.IsOpen = false;
            if (multi.SelectedSample == null) multi.SelectNearestPoint(multi.VerificationSamplePosition(6, laneSample));
            var frame = new DispatcherFrame(); var paint = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            paint.Tick += (_, _) => { paint.Stop(); frame.Continue = false; }; paint.Start(); Dispatcher.PushFrame(frame);
            var multiImage = new RenderTargetBitmap(850, 900, 96, 96, PixelFormats.Pbgra32); multiImage.Render(host);
            var multiEncoder = new PngBitmapEncoder(); multiEncoder.Frames.Add(BitmapFrame.Create(multiImage)); using (var file = File.Create(Path.Combine(directory, "eight-types.png"))) multiEncoder.Save(file);
            multi.ClearSelection(); host.Close();
            File.WriteAllLines(Path.Combine(directory, "result.txt"), results);
        }
        catch (Exception error) { File.WriteAllLines(Path.Combine(directory, "result.txt"), results.Append("FAIL: " + error)); Environment.ExitCode = 1; }
        finally { app.Shutdown(Environment.ExitCode); }
    }
    private static double scrollHeight(DependencyObject root) => Descendants<ScrollViewer>(root).First().Height;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
