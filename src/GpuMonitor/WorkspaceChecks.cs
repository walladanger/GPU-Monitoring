using System.IO;
using System.Windows;
using System.Windows.Controls;
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
                File.WriteAllText(Path.Combine(output, "workspace-result.txt"), "PASS: Actual Add Panel modal/search/checkbox/Apply, measured GPU and CPU charts, menu-backed edits/order/sizing, undo, named preset save/load, malformed preset preservation and missing/reappearing GPU bindings. Native screenshot uses synthetic fixtures; no hardware or visual signoff claim.");
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
                var gpu = workspace.Layout.Panels.Single();
                workspace.PutPanel(new("cpu-panel", "CPU usage", 1, 2, "area", [new("windows-system", "cpu-total", "cpu.usage")]));
                workspace.Move("cpu-panel", -1);
                if (workspace.Layout.Panels[0].Id != "cpu-panel") throw new InvalidOperationException("Panel reorder failed.");
                workspace.PutPanel(gpu with { Title = "Selected GPU usage", WidthSlots = 2, HeightRows = 1 });
                await workspace.SavePresetAsync("fixture-layout");
                workspace.Remove(gpu.Id); workspace.Undo();
                if (workspace.PanelCount != 2) throw new InvalidOperationException("Undo did not restore the removed panel.");
                workspace.ApplyLayout(new(1, "Empty", [])); await workspace.LoadPresetAsync("fixture-layout");
                if (workspace.PanelCount != 2 || workspace.Layout.Panels.Single(p => p.Id == gpu.Id).WidthSlots != 2)
                    throw new InvalidOperationException("Named preset did not restore edited panel dimensions.");
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
