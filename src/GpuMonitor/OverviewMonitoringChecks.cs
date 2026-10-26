using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GpuMonitor;

/// <summary>Runs the actual window lifecycle against explicitly synthetic provider measurements.</summary>
internal static class OverviewMonitoringChecks
{
    public static void Run()
    {
        string result = Path.Combine(AppContext.BaseDirectory, "monitoring-verification.txt");
        var gpu = new FixtureProvider(NvidiaTelemetry.SourceId);
        var failed = new FixtureProvider("fixture-unavailable");
        var monitoring = new DashboardTelemetry([gpu, failed]);
        var app = new Application();
        var window = new OverviewWindow(telemetry: monitoring);
        bool busy = false, completed = false, failedCheck = false;
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        void Fail(Exception error)
        {
            failedCheck = true; timer.Stop(); Environment.ExitCode = 1;
            File.WriteAllText(result, "FAIL: " + error); window.Close();
        }
        timer.Tick += async (_, _) =>
        {
            if (busy) return;
            if (elapsed.Elapsed > TimeSpan.FromSeconds(20)) { Fail(new TimeoutException("Engine measurements did not populate the native dashboard.")); return; }
            if (window.VerificationDeviceCount == 0) return;
            busy = true;
            try
            {
                window.VerifyInventory(1);
                if (monitoring.Snapshot().Single(s => s.Source == failed.SourceId).Health != ProviderHealth.Stale)
                    throw new InvalidOperationException("The failed source did not retain independent health.");
                await window.VerifyPauseAsync(true);
                int pausedReads = gpu.Reads;
                await Task.Delay(200);
                if (gpu.Reads != pausedReads) throw new InvalidOperationException("Pausing the window left collection running.");
                await window.VerifyRescanAsync();
                if (gpu.Reads != pausedReads + 1) throw new InvalidOperationException("Paused rescan did not take exactly one reading.");
                window.VerifyInventory(1);
                await window.VerifyPauseAsync(false);
                completed = true; timer.Stop(); window.Close();
            }
            catch (Exception error) { Fail(error); }
        };
        window.Loaded += (_, _) => timer.Start();
        window.Closed += (_, _) =>
        {
            timer.Stop();
            if (failedCheck) return;
            if (!completed || !gpu.Disposed || !failed.Disposed || gpu.Active != 0 || failed.Active != 0)
            {
                Environment.ExitCode = 1; File.WriteAllText(result, "FAIL: Window closed before collection and provider disposal completed.");
            }
            else File.WriteAllText(result, "PASS: Real WPF window rendered synthetic engine GPU data alongside a failed source; pause, one-shot rescan, resume and awaited shutdown passed. No physical GPU validation.");
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        try { app.Run(window); }
        catch (Exception error) { Environment.ExitCode = 1; File.WriteAllText(result, "FAIL: " + error); }
    }

    private sealed class FixtureProvider(string source) : ITelemetryProvider, IDisposable
    {
        public string SourceId => source;
        public IReadOnlyList<MetricDefinition> Definitions => source == NvidiaTelemetry.SourceId ? NvidiaTelemetry.Definitions : [];
        public int Reads, Active;
        public bool Disposed;
        public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Interlocked.Increment(ref Reads); Interlocked.Increment(ref Active);
            try
            {
                await Task.Delay(25, cancellation);
                if (source != NvidiaTelemetry.SourceId) throw new IOException("Synthetic unavailable source.");
                return NvidiaTelemetry.Normalize(CsvTelemetry.Parse(
                    "0, Synthetic RTX 3090 fixture, GPU-window-fixture, 00000000:01:00.0, 24576, 45, 8192, 65, 300, 62, 1500, 9750", DateTimeOffset.UtcNow));
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        public void Dispose() => Disposed = true;
    }
}
