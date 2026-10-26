using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GpuMonitor;

internal static class WorkspaceChecks
{
    public static void Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "overview-verification"); Directory.CreateDirectory(output);
        var directory = Path.Combine(Path.GetTempPath(), "gpu-workspace-ui-" + Guid.NewGuid().ToString("N"));
        var app = new Application();
        var window = new OverviewWindow(startMonitoring: false, workspaceDirectory: directory);
        var viewport = window.VerificationRoot; window.Content = null;
        HwndSource? host = null;
        var now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        var capture = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        void Fail(Exception error)
        {
            capture.Stop(); Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(output, "workspace-result.txt"), "FAIL: " + error); app.Shutdown(1);
        }
        capture.Tick += (_, _) =>
        {
            try
            {
                capture.Stop(); viewport.Measure(new Size(1536, 1600)); viewport.Arrange(new Rect(0, 0, 1536, 1600)); viewport.UpdateLayout();
                var bitmap = OverviewChecks.Capture(viewport);
                foreach (var plot in window.VerifyWorkspace.Plots)
                {
                    if (plot.VisibleSamples.Count != 31) throw new InvalidOperationException("Custom chart did not receive exactly its selected measured samples.");
                    var point = plot.TranslatePoint(new Point(0, 0), viewport);
                    int stride = bitmap.PixelWidth * 4, colored = 0; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
                    for (int y = Math.Max(0, (int)point.Y + 16); y < Math.Min(bitmap.PixelHeight, point.Y + plot.ActualHeight - 32); y++)
                        for (int x = Math.Max(0, (int)point.X + 54); x < Math.Min(bitmap.PixelWidth, point.X + plot.ActualWidth - 16); x++)
                        {
                            int pixel = y * stride + x * 4;
                            if (pixels[pixel + 1] > 140 && pixels[pixel + 1] > pixels[pixel] * 1.5 && pixels[pixel + 1] > pixels[pixel + 2] * 1.5) colored++;
                        }
                    if (colored < 20) throw new InvalidOperationException("Custom chart native trace was not rendered in its panel.");
                }
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "workspace-controls-1536.png"))) encoder.Save(stream);
                File.WriteAllText(Path.Combine(output, "workspace-result.txt"), "PASS: Actual Add Panel modal/search/checkbox/Apply, measured GPU and CPU charts, title-handle events with deterministic drop hit-testing, resize preview/cancel/commit, merge/split/duplicate menus, undo, named preset save/load, malformed preset preservation and missing/reappearing GPU bindings. Native screenshot uses synthetic fixtures; no hardware or visual signoff claim.");
                window.Close();
            }
            catch (Exception error) { Fail(error); }
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                viewport.Width = 1536; viewport.Height = 1600;
                host = new HwndSource(new HwndSourceParameters("GPU Monitor workspace fixture viewport")
                { Width = 1536, Height = 1600, PositionX = 0, PositionY = 0, WindowStyle = unchecked((int)0x90000000) });
                host.RootVisual = viewport; window.Hide();
                var sources = Sources(now, 2);
                window.ApplyFixture(NvidiaSnapshot.Readings(sources[0].Current!), now);
                window.SetWorkspaceFixtures(sources, now);
                var workspace = window.VerifyWorkspace;
                Exception? pickerError = null;
                var drivePicker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                drivePicker.Tick += (_, _) =>
                {
                    var editor = app.Windows.OfType<WorkspacePanelEditor>().FirstOrDefault(w => w.IsLoaded);
                    if (editor == null) return;
                    drivePicker.Stop();
                    try
                    {
                        editor.VerifySearch("utilization.gpu");
                        var selected = editor.VisibleChoices.First(c => c.Key.DeviceId == "GPU-fixture-0"); selected.Value.IsChecked = true;
                        editor.VerifySearch("no matches at all"); editor.VerifySearch("utilization.gpu");
                        if (editor.VisibleChoices[selected.Key].IsChecked != true) throw new InvalidOperationException("Search discarded an existing sensor selection.");
                        editor.ApplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    catch (Exception error) { pickerError = error; editor.Close(); }
                };
                drivePicker.Start(); workspace.AddButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); drivePicker.Stop();
                if (pickerError != null) throw pickerError;
                if (workspace.PanelCount != 1) throw new InvalidOperationException("Add Panel did not commit a measured sensor selection.");
                if (workspace.Visibility != Visibility.Visible) throw new InvalidOperationException("Adding a panel did not reveal the workspace immediately.");
                var gpu = workspace.Layout.Panels.Single();
                workspace.PutPanel(new("cpu-panel", "CPU usage", 1, 2, "area", [new("windows-system", "cpu-total", "cpu.usage")]));
                workspace.Move("cpu-panel", -1);
                if (workspace.Layout.Panels[0].Id != "cpu-panel") throw new InvalidOperationException("Panel reorder failed.");
                workspace.PutPanel(gpu with { Title = "Selected GPU usage", WidthSlots = 2, HeightRows = 1 });
                workspace.PresetsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var presetMenu = workspace.PresetsButton.ContextMenu!; presetMenu.IsOpen = false;
                Exception? nameError = null;
                var driveName = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                driveName.Tick += (_, _) =>
                {
                    var dialog = app.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Save workspace preset" && w.IsLoaded);
                    if (dialog == null) return;
                    driveName.Stop();
                    try
                    {
                        var form = (StackPanel)dialog.Content;
                        form.Children.OfType<TextBox>().Single().Text = "fixture-layout";
                        form.Children.OfType<StackPanel>().Single().Children.OfType<System.Windows.Controls.Button>()
                            .Single(button => (string)button.Content == "Save").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    catch (Exception error) { nameError = error; dialog.Close(); }
                };
                driveName.Start();
                presetMenu.Items.OfType<MenuItem>().First(item => ((string)item.Header).StartsWith("Save named", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                driveName.Stop(); if (nameError != null) throw nameError;
                using var filesDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!workspace.IsEnabled) await Task.Delay(10, filesDeadline.Token);
                if (!File.Exists(Path.Combine(directory, "fixture-layout.json"))) throw new InvalidOperationException("Preset Save dialog did not save its named workspace.");
                workspace.Remove(gpu.Id); workspace.Undo();
                if (workspace.PanelCount != 2) throw new InvalidOperationException("Undo did not restore the removed panel.");
                workspace.ApplyLayout(new(1, "Empty", []));
                if (workspace.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Clearing the workspace did not hide its empty region.");
                workspace.PresetsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                presetMenu = workspace.PresetsButton.ContextMenu!; presetMenu.IsOpen = false;
                presetMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Load preset").Items.OfType<MenuItem>()
                    .Single(item => (string)item.Header == "fixture-layout").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                while (!workspace.IsEnabled) await Task.Delay(10, filesDeadline.Token);
                if (workspace.PanelCount != 2 || workspace.Layout.Panels.Single(p => p.Id == gpu.Id).WidthSlots != 2)
                    throw new InvalidOperationException("Named preset did not restore edited panel dimensions.");
                if (workspace.Visibility != Visibility.Visible) throw new InvalidOperationException("Loading a preset did not reveal its panels immediately.");
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                viewport.Measure(new Size(1536, 1600)); viewport.Arrange(new Rect(0, 0, 1536, 1600)); viewport.UpdateLayout();
                var moving = workspace.FindCard(gpu.Id).TitleHandle;
                var originalPointer = workspace.PointerPosition;
                var pointer = workspace.PanelPoint(gpu.Id);
                workspace.PointerPosition = () => pointer;
                moving.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                pointer = workspace.PanelPoint("cpu-panel", .25);
                moving.RaiseEvent(new DragDeltaEventArgs(10, 10) { RoutedEvent = Thumb.DragDeltaEvent });
                var dropDiagnostics = workspace.DropDiagnostics + $"; pointer={pointer}; source size={workspace.FindCard(gpu.Id).RenderSize}; target size={workspace.FindCard("cpu-panel").RenderSize}";
                moving.RaiseEvent(new DragCompletedEventArgs(10, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
                if (workspace.Layout.Panels[0].Id != gpu.Id) throw new InvalidOperationException("Native title-handle drop did not reorder the panel. " + dropDiagnostics);
                workspace.PutPanel(gpu with { Id = "offscreen-fixture", HeightRows = 8 });
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                viewport.UpdateLayout();
                var beforeOutside = workspace.Layout;
                moving = workspace.FindCard(gpu.Id).TitleHandle; pointer = workspace.PanelPoint(gpu.Id);
                moving.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                pointer = workspace.PanelPoint("offscreen-fixture", .75);
                moving.RaiseEvent(new DragDeltaEventArgs(10, 10) { RoutedEvent = Thumb.DragDeltaEvent });
                moving.RaiseEvent(new DragCompletedEventArgs(10, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
                if (!ReferenceEquals(beforeOutside, workspace.Layout)) throw new InvalidOperationException("A drop outside the visible viewport targeted an invisible panel.");
                workspace.Remove("offscreen-fixture");
                var grip = workspace.FindCard("cpu-panel").ResizeGrip;
                grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); pointer += new Vector(0, 100); grip.RaiseEvent(new DragDeltaEventArgs(0, 100) { RoutedEvent = Thumb.DragDeltaEvent });
                if (workspace.Layout.Panels.Single(p => p.Id == "cpu-panel").HeightRows != 2) throw new InvalidOperationException("Resize preview committed before completion.");
                grip.RaiseEvent(new DragCompletedEventArgs(0, 100, true) { RoutedEvent = Thumb.DragCompletedEvent });
                if (workspace.Layout.Panels.Single(p => p.Id == "cpu-panel").HeightRows != 2) throw new InvalidOperationException("Cancelled resize changed saved dimensions.");
                if (workspace.FindCard("cpu-panel").Plot!.Height != 200) throw new InvalidOperationException("Cancelled resize left its chart preview active.");
                grip.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); pointer += new Vector(0, 100); grip.RaiseEvent(new DragDeltaEventArgs(0, 100) { RoutedEvent = Thumb.DragDeltaEvent }); grip.RaiseEvent(new DragCompletedEventArgs(0, 100, false) { RoutedEvent = Thumb.DragCompletedEvent });
                if (workspace.Layout.Panels.Single(p => p.Id == "cpu-panel").HeightRows != 3) throw new InvalidOperationException("Native resize grip did not commit snapped dimensions.");
                workspace.Undo();
                if (workspace.Layout.Panels.Single(p => p.Id == "cpu-panel").HeightRows != 2) throw new InvalidOperationException("Resize was not one undoable change.");
                workspace.PointerPosition = originalPointer;
                await workspace.SavePresetAsync("gesture-layout");
                var panelMenu = workspace.FindCard(gpu.Id).ContextMenu!;
                var mergeMenu = panelMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Merge into…");
                mergeMenu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
                mergeMenu.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (workspace.PanelCount != 1 || workspace.Layout.Panels.Single().Series.Count != 2) throw new InvalidOperationException("Native merge command lost sensor bindings.");
                workspace.FindCard("cpu-panel").ContextMenu!.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Split into one panel per sensor")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (workspace.PanelCount != 2 || workspace.Layout.Panels.Any(p => p.Series.Count != 1)) throw new InvalidOperationException("Native split command did not preserve each sensor.");
                workspace.FindCard("cpu-panel").ContextMenu!.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Duplicate panel")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (workspace.PanelCount != 3 || workspace.Layout.Panels.Select(p => p.Id).Distinct().Count() != 3) throw new InvalidOperationException("Native duplicate command reused a panel identity.");
                workspace.Undo(); await workspace.LoadPresetAsync("gesture-layout");
                await File.WriteAllTextAsync(Path.Combine(directory, "bad.json"), "{bad");
                try { await workspace.LoadPresetAsync("bad"); throw new InvalidOperationException("Malformed preset was accepted."); }
                catch (System.Text.Json.JsonException) { }
                if (workspace.PanelCount != 2) throw new InvalidOperationException("Failed load destroyed the working layout.");
                window.SetWorkspaceFixtures(Sources(now, 0), now); window.VerifyInventory(0);
                if (workspace.Layout.Panels.Single(p => p.Id == gpu.Id).Series.Single().DeviceId != "GPU-fixture-0" ||
                    workspace.Plots.SelectMany(p => p.VisibleSamples).Any(s => s.Device.Kind == DeviceKind.Gpu))
                    throw new InvalidOperationException("Missing hardware was remapped or a trace was fabricated.");
                window.SetWorkspaceFixtures(sources, now); window.VerifyInventory(2);
                capture.Start();
            }
            catch (Exception error) { Fail(error); }
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        try { app.Run(window); }
        finally { host?.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static IReadOnlyList<ProviderSnapshot> Sources(DateTimeOffset now, int count)
    {
        var gpu = new TelemetrySession(new NvidiaCollector());
        for (int tick = 0; tick <= 30; tick++)
        {
            var time = now.AddSeconds(-300 + tick * 10);
            string rows = string.Join('\n', Enumerable.Range(0, count).Select(i =>
                $"{i}, Synthetic RTX 3090, GPU-fixture-{i}, bus, 24576, {40 + (tick * 7 + i * 13) % 48}, 8192, 65, 300, 62, 1500, 9750"));
            gpu.Apply(NvidiaTelemetry.Normalize(CsvTelemetry.Parse(rows, time)), time);
        }
        var cpu = new DeviceIdentity("windows-system", "cpu-total", DeviceKind.Cpu, "Synthetic CPU", "0");
        var history = Enumerable.Range(0, 31).Select(tick => new TelemetrySample(cpu, "cpu.usage", now.AddSeconds(-300 + tick * 10), 20 + tick % 17)).ToArray();
        return [new(NvidiaTelemetry.SourceId, gpu.Health, gpu.Error, gpu.LastSuccess, gpu.Current, gpu.HistorySnapshot(), gpu.Definitions),
            new("windows-system", ProviderHealth.Live, null, now, WindowsSystemCollector.Batch([cpu], [history[^1]]), history,
                [new("windows-system", "cpu.usage", "CPU usage", "%", 100)])];
    }
}
