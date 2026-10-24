using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GpuMonitor;

/// <summary>Read-only native verification against the actual configured NVIDIA collector. No fixture substitution.</summary>
internal static class LiveOverviewChecks
{
    public static void Run(int expectedCount)
    {
        if (expectedCount is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        var output = Path.Combine(AppContext.BaseDirectory, "target-verification"); Directory.CreateDirectory(output);
        var app = new Application(); var window = new OverviewWindow();
        var elapsed = Stopwatch.StartNew(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        bool finished = false;
        void Finish(Exception? error)
        {
            if (finished) return; finished = true; timer.Stop();
            File.WriteAllText(Path.Combine(output, "live-overview-result.txt"), error == null ?
                $"PASS: Actual NVIDIA measurements populated {expectedCount} native Overview GPU cards and their measured comparison series. Screenshot captured on this machine. Physical pointer, multi-monitor/scaling and human visual approval are separate manual gates." : "FAIL: " + error);
            if (error != null) Environment.ExitCode = 1;
            window.Close();
        }
        timer.Tick += (_, _) =>
        {
            try
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException($"Expected {expectedCount} actual GPUs; native discovery did not reach that inventory.");
                var snapshot = window.VerificationGpuSnapshot;
                if (elapsed.Elapsed < TimeSpan.FromSeconds(5) || snapshot.Health != ProviderHealth.Live || window.VerificationDeviceCount != expectedCount) return;
                window.VerifyInventory(expectedCount);
                if (window.VerifyPlots.Any(p => !PlotData.HasMeasurements(p.VisibleSamples))) return;
                var comparison = (TelemetryPlot)window.VerificationComparison;
                var measuredDevices = comparison.VisibleSamples.Where(s => s.Value.HasValue).Select(s => s.Device.Id).Distinct().Count();
                if (measuredDevices != expectedCount) return;
                var bitmap = OverviewChecks.Capture(window.VerificationRoot);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "live-overview.png"))) encoder.Save(stream);
                var dpi = VisualTreeHelper.GetDpi(window.VerificationRoot);
                File.WriteAllText(Path.Combine(output, "live-overview-info.json"), JsonSerializer.Serialize(new {
                    captured_utc = DateTimeOffset.UtcNow, source = snapshot.Source, last_success = snapshot.LastSuccess,
                    devices = snapshot.Current!.Devices, dpi_scale_x = dpi.DpiScaleX, dpi_scale_y = dpi.DpiScaleY,
                    viewport_width_dip = window.VerificationRoot.ActualWidth, viewport_height_dip = window.VerificationRoot.ActualHeight,
                    note = "Actual local NVIDIA window capture; no simulated devices. Pointer/multi-monitor/scaling transitions and human approval are not automated."
                }, new JsonSerializerOptions { WriteIndented = true }));
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        };
        window.Loaded += (_, _) => timer.Start();
        window.Closed += (_, _) => { timer.Stop(); if (!finished) { Environment.ExitCode = 1; File.WriteAllText(Path.Combine(output, "live-overview-result.txt"), "FAIL: Verification window was closed before actual telemetry checks completed."); } };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Finish(e.Exception); };
        app.Run(window);
    }
}
