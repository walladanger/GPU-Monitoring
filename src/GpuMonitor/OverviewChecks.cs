using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

namespace GpuMonitor;

/// <summary>Runs the real WPF/Skia chart view with deterministic, explicitly synthetic fixtures.</summary>
internal static class OverviewChecks
{
    public static void Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "overview-verification"); Directory.CreateDirectory(output);
        var app = new Application();
        var preferences = new Settings { GpuPanels = new() { ["surface/GPU-fixture-0"] = new() { SurfaceWidth = 430 }, ["surface/GPU-fixture-1"] = new() { SurfaceWidth = 600 } } };
        var window = new OverviewWindow(startMonitoring: false, preferences: preferences) { Left = 0, Top = 0 };
        // Preserve the customizable panel UI, but default to exactly one graph per GPU.
        var configured = new GpuPanelSettings();
        var card = new GpuOverviewCard(configured);
        if (card.Plots.Count() != 1 || configured.Sensor != "utilization.gpu")
            throw new InvalidOperationException("The GPU panel default must show one configurable utilization graph.");
        var memoryConfiguration = new GpuPanelSettings { Sensor = "memory.used" };
        if (new GpuOverviewCard(memoryConfiguration).Plots.Count() != 1 || memoryConfiguration.Sensor != "memory.used")
            throw new InvalidOperationException("Selecting memory data must replace, not duplicate, the GPU graph.");
        var viewportRoot = window.VerificationRoot; window.Content = null;
        HwndSource? host = null;
        var cases = new[] { (0, 1536), (1, 1536), (2, 1536), (4, 1536), (8, 1536), (2, 760), (3, 1200), (3, 1800), (3, 2480) };
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        int index = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        void Fail(Exception error)
        {
            timer.Stop(); File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL: " + error);
            Environment.ExitCode = 1; app.Shutdown(1);
        }
        void ApplyCase()
        {
            var (count, width) = cases[index];
            // The runner's desktop can be smaller than the reference. Lay out a real
            // WPF viewport at the requested dimensions, independently of HWND limits.
            window.VerificationRoot.Width = width; window.VerificationRoot.Height = 1024;
            for (int tick = 0; tick <= 30; tick++)
            {
                string rows = string.Join('\n', Enumerable.Range(0, count).Select(gpu =>
                    $"{gpu}, NVIDIA GeForce RTX 3090, GPU-fixture-{gpu}, 00000000:{gpu + 1:00}:00.0, 24576, {62 + gpu * 7 + (int)(5 * Math.Sin(tick * .7 + gpu) + 3 * Math.Sin(tick * 2.3))}, {17408 + gpu * 512 + (int)(512 * Math.Sin(tick * .12 + gpu))}, 65, 300, 62, 1500, 9750"));
                var time = now.AddSeconds(-300 + tick * 10);
                window.ApplyFixture(CsvTelemetry.Parse(rows, time), time, render: tick == 30);
            }
            window.SetWorkspaceFixtures(new[] { window.VerificationGpuSnapshot }.Concat(HardwareFixtures(now)).ToArray(), now);
            window.VerifyInventory(count);
        }
        timer.Tick += (_, _) =>
        {
            try
            {
                window.UpdateLayout();
                var viewport = window.VerificationRoot;
                var viewportSize = new Size(cases[index].Item2, 1024);
                viewport.Measure(viewportSize); viewport.Arrange(new Rect(viewportSize)); viewport.UpdateLayout();
                window.VerifyResponsiveLayout();
                var bitmap = Capture(window.VerificationRoot);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var (count, width) = cases[index];
                if (bitmap.PixelWidth != width || bitmap.PixelHeight != 1024)
                    throw new InvalidOperationException("Fixture viewport dimensions do not match the requested reference size.");
                using (var stream = File.Create(Path.Combine(output, $"fixture-{count}-gpus-{width}.png"))) encoder.Save(stream);
                if (count == 2 && width == 1536)
                {
                    var gpuTop = window.VerifyPlots[0].TranslatePoint(new Point(0, 0), viewport);
                    var hardwareTop = window.VerificationHardwareRow.TranslatePoint(new Point(0, 0), viewport);
                    var comparisonTop = window.VerificationComparison.TranslatePoint(new Point(0, 0), viewport);
                    if (gpuTop.X < 239 || hardwareTop.Y <= gpuTop.Y || comparisonTop.Y <= hardwareTop.Y)
                        throw new InvalidOperationException("Annotated GPU / CPU-disk / comparison composition has an invalid panel order.");
                    if (window.VerificationHardwarePlots.Any(p => p.VisibleSamples.Count != 31))
                        throw new InvalidOperationException("Reference hardware row must render its actual fixture samples.");
                }
                // Inspect chart interiors, excluding all card headings and numeric labels.
                if (count > 0)
                {
                    var plots = window.VerifyPlots;
                    for (int chartIndex = 0; chartIndex < 2; chartIndex++)
                    {
                        var plot = plots[chartIndex];
                        var origin = plot.TranslatePoint(new Point(0, 0), viewport);
                        int plotWidth = (int)plot.ActualWidth, plotHeight = (int)plot.ActualHeight;
                        int stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
                        int colored = 0;
                        for (int y = (int)origin.Y + 16; y < Math.Min(bitmap.PixelHeight, origin.Y + plotHeight - 32); y++)
                            for (int x = (int)origin.X + 54; x < Math.Min(bitmap.PixelWidth, origin.X + plotWidth - 16); x++)
                        {
                            int p = y * stride + x * 4;
                            bool matches = chartIndex == 0 ?
                                pixels[p + 1] > 140 && pixels[p + 1] > pixels[p] * 1.5 && pixels[p + 1] > pixels[p + 2] * 1.5 :
                                pixels[p] > 140 && pixels[p] > pixels[p + 2] * 1.5 && pixels[p] > pixels[p + 1] * 1.2;
                            if (matches) colored++;
                        }
                        if (colored < 20) throw new InvalidOperationException($"Native chart {chartIndex} strokes missing: {colored} pixels.");
                    }
                }
                index++;
                if (index == cases.Length)
                {
                    timer.Stop(); File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: 0/1/2/3/4/8 GPU native overview inventory, exact comparison-series counts, colored chart rendering, saved widths and filled panel rows from 760 to 2480 DIPs. Fixtures are synthetic; no physical GPU validation.");
                    window.Close(); return;
                }
                ApplyCase();
            }
            catch (Exception error) { Fail(error); }
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        window.Loaded += (_, _) =>
        {
            try
            {
                // A top-level Window is constrained by the runner's small desktop.
                // Host the exact same native view in a fixed-size HWND for viewport QA.
                var viewport = viewportRoot;
                viewport.HorizontalAlignment = HorizontalAlignment.Left; viewport.VerticalAlignment = VerticalAlignment.Top;
                host = new HwndSource(new HwndSourceParameters("GPU Monitor fixture viewport")
                { Width = 1536, Height = 1024, PositionX = 0, PositionY = 0, WindowStyle = unchecked((int)0x90000000) });
                host.RootVisual = viewport; window.Hide();
                ApplyCase(); timer.Start();
            }
            catch (Exception error) { Fail(error); }
        };
        try { app.Run(window); } catch (Exception error) { Fail(error); }
        finally { host?.Dispose(); }
    }

    private static IEnumerable<ProviderSnapshot> HardwareFixtures(DateTimeOffset now)
    {
        var cpu = new DeviceIdentity("windows-system", "cpu-total", DeviceKind.Cpu, "AMD Ryzen 7 7800X3D", "0");
        var ram = new DeviceIdentity("windows-system", "physical-memory", DeviceKind.Memory, "Physical RAM", "0");
        var disk = new DeviceIdentity("windows-disks", "PhysicalDrive0", DeviceKind.Disk, "0 C:", "0");
        var cpuSamples = new List<TelemetrySample>(); var diskSamples = new List<TelemetrySample>();
        for (int tick = 0; tick <= 30; tick++)
        {
            var time = now.AddSeconds(-300 + tick * 10);
            cpuSamples.Add(new(cpu, "cpu.usage", time, 28 + 5 * Math.Sin(tick * 1.5) + 3 * Math.Sin(tick * .4)));
            cpuSamples.Add(new(cpu, "cpu.logical", time, 16));
            cpuSamples.Add(new(ram, "ram.used", time, 24L * 1073741824));
            diskSamples.Add(new(disk, "disk.busy", time, 62 + 4 * Math.Sin(tick * 2.1)));
            diskSamples.Add(new(disk, "disk.read", time, 152 * 1048576));
            diskSamples.Add(new(disk, "disk.write", time, 298 * 1048576));
        }
        ProviderSnapshot Snapshot(string source, DeviceIdentity[] devices, List<TelemetrySample> history, IReadOnlyList<MetricDefinition> definitions)
        {
            var current = history.Where(s => s.Time == now).ToArray();
            return new(source, ProviderHealth.Live, null, now,
                new(devices, current.Select(s => new MetricCapability(s.Device, s.MetricKey, s.Availability)).ToArray(), current, []), history, definitions);
        }
        yield return Snapshot(cpu.Source, [cpu, ram], cpuSamples, new WindowsSystemCollector().Definitions);
        using var disks = new DiskCollector();
        yield return Snapshot(disk.Source, [disk], diskSamples, disks.Definitions);
    }

    internal static RenderTargetBitmap Capture(FrameworkElement view)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
        var brush = new VisualBrush(view)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds,
            Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
        };
        using (var context = drawing.RenderOpen()) context.DrawRectangle(brush, null, bounds);
        // VisualBrush captures in local coordinates instead of including layout offsets.
        bitmap.Render(drawing); return bitmap;
    }
}
