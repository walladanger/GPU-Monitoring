using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace GpuMonitor;

internal static class PersistenceChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "gpu-persistence-ui-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "telemetry.sqlite");
        string output = Path.Combine(AppContext.BaseDirectory, "overview-verification"); Directory.CreateDirectory(output);
        var app = new Application(); var monitor = new DashboardTelemetry([new Provider()]);
        var settings = new Settings { RefreshSeconds = 1 };
        var window = new OverviewWindow(telemetry: monitor, preferences: settings, historyDirectory: root, workspaceDirectory: Path.Combine(root, "workspace"));
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Fail(Exception error)
        {
            File.WriteAllText(Path.Combine(output, "persistence-result.txt"), "FAIL: " + error);
            Environment.ExitCode = 1; window.Close();
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                Require(!File.Exists(path), "Default UI launch created a history database before opt-in.");
                var dialog = new HistorySettingsWindow(window, false, 7, path, monitor.HistoryStatus,
                    window.ConfigureHistoryRecordingAsync, () => { });
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    dialog.Enabled.IsChecked = true; dialog.Days.SelectedItem = 3;
                    dialog.Apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                });
                Require(dialog.ShowDialog() == true && settings.HistoryEnabled && settings.HistoryDays == 3 && monitor.HistoryStatus.Ready,
                    "Actual history settings Apply did not configure the independent writer.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                while (monitor.HistoryStatus.Samples < 27) await Task.Delay(50, timeout.Token);
                await window.VerifyPauseAsync(true);
                await window.ConfigureHistoryRecordingAsync(false, 3);
                var rows = HistoryStore.ReadFile(path, new(NvidiaTelemetry.SourceId, "GPU-history-fixture", "utilization.gpu"), "%", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
                Require(rows.Count >= 3 && rows.All(r => r.Sample.Value == 42.125), "Disabling history lost raw measured samples or their precision.");
                var browser = new HistoryWindow(window, path); browser.Show();
                while (!browser.Sensors.IsEnabled || browser.Sensors.Items.Count == 0) await Task.Delay(20, timeout.Token);
                browser.Sensors.SelectedItem = browser.Sensors.Items.OfType<HistorySensor>().Single(s => s.Selection.MetricKey == "utilization.gpu");
                browser.Ending.Text = rows[^1].Sample.Time.AddSeconds(5).ToString("O"); browser.Minutes.SelectedItem = 1;
                await browser.LoadMeasurementsAsync();
                Require(browser.Plot.VisibleSamples.Count >= 2 && browser.Plot.VisibleSamples.All(s => s.Value == 42.125) && browser.Status.Contains("archived"),
                    "Saved-history browser did not load its selected recorded device/time range.");
                await Task.Delay(1500);
                browser.Plot.SavePng(Path.Combine(output, "stored-history-chart.png"));
                var bitmap = OverviewChecks.Capture((FrameworkElement)browser.Content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "stored-history-browser.png"))) encoder.Save(stream);
                browser.Close();
                File.WriteAllText(Path.Combine(output, "persistence-result.txt"), "PASS: Actual native history opt-in/settings Apply, independent bounded writer, disable/drain, SQLite reopen, exact raw samples/units, historical picker/time-range chart and PNG capture. Synthetic fixture; no target hardware or visual signoff.");
                window.Close();
            }
            catch (Exception error) { Fail(error); }
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        try { app.Run(window); }
        finally { monitor.DisposeAsync().AsTask().GetAwaiter().GetResult(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Provider : ITelemetryProvider
    {
        private readonly DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-5);
        private int tick;
        public string SourceId => NvidiaTelemetry.SourceId;
        public IReadOnlyList<MetricDefinition> Definitions => NvidiaTelemetry.Definitions;
        public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
        {
            var time = start.AddSeconds(Interlocked.Increment(ref tick) * 15);
            return Task.FromResult(NvidiaTelemetry.Normalize(CsvTelemetry.Parse("0, Synthetic GPU history fixture, GPU-history-fixture, bus, 24576, 42.125, 8192, 65, 300, 62, 1500, 9750", time)));
        }
    }
}
