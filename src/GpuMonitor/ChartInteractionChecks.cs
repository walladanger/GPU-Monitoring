using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;

namespace GpuMonitor;

/// <summary>Real WPF charts and editor, synthetic measurements explicitly identified in all reports.</summary>
internal static class ChartInteractionChecks
{
    public static void Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "overview-verification"); Directory.CreateDirectory(output);
        var app = new Application();
        var root = new StackPanel { Width = 1200, Height = 650, Background = OverviewTheme.Canvas };
        var window = new Window { Title = "Synthetic chart interaction verification", Width = 1200, Height = 650, Content = root };
        var now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        HwndSource? host = null;
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Fail(Exception error)
        {
            File.WriteAllText(Path.Combine(output, "charts-result.txt"), "FAIL: " + error);
            Environment.ExitCode = 1; app.Shutdown(1);
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Content = null;
                host = new HwndSource(new HwndSourceParameters("GPU Monitor chart interaction fixture")
                { Width = 1200, Height = 650, WindowStyle = unchecked((int)0x90000000) });
                host.RootVisual = root; window.Hide();
                var device = new DeviceIdentity("fixture", "gpu", DeviceKind.Gpu, "Synthetic GPU", "0");
                var usage = Enumerable.Range(0, 31).Select(i => new TelemetrySample(device, "usage", now.AddSeconds(-300 + i * 10), 20 + i % 17)).ToArray();
                var memory = Enumerable.Range(0, 31).Select(i => new TelemetrySample(device, "memory", now.AddSeconds(-300 + i * 10), (8 + i / 4d) * 1073741824)).ToArray();
                var last = new[] { usage[^1], memory[^1] };
                ProviderSnapshot[] sources = [new("fixture", ProviderHealth.Live, null, now,
                    new([device], last.Select(s => new MetricCapability(device, s.MetricKey, MetricAvailability.Available)).ToArray(), last, []),
                    usage.Concat(memory).ToArray(), [new("fixture", "usage", "GPU utilization", "%", 100), new("fixture", "memory", "VRAM used", "B")])];
                var primary = new WorkspaceSeries("fixture", "gpu", "usage");
                var original = new WorkspacePanel("comparison", "Synthetic comparison · independent units", 2, 4, "line", [primary]);
                // Exercise the actual modal picker: a second unit must be accepted and assigned to the other axis.
                var editor = new WorkspacePanelEditor(window, sources, original);
                Exception? editorFailure = null;
                editor.Loaded += (_, _) => editor.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        editor.VisibleChoices.Single(c => c.Key.MetricKey == "memory").Value.IsChecked = true;
                        editor.ApplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        Require(editor.Result?.Series.Single(s => s.MetricKey == "memory").Axis == 1, "Picker did not assign the second unit to the right axis.");
                    }
                    catch (Exception error) { editorFailure = error; editor.Close(); }
                });
                editor.ShowDialog(); if (editorFailure != null) throw editorFailure;
                Require(editor.Result != null, "Comparison editor did not commit.");
                var card = new WorkspaceChartCard(editor.Result!, () => { }); root.Children.Add(card); card.Refresh(sources, now, 5);
                root.Measure(new Size(1200, 650)); root.Arrange(new Rect(0, 0, 1200, 650)); root.UpdateLayout();
                await Task.Delay(1500);
                var plot = card.Plot!; var native = plot.Children.OfType<CartesianChart>().Single();
                var traces = native.Series.Cast<LineSeries<ObservablePoint>>().ToArray();
                Require(native.YAxes.Count() == 2 && traces[0].ScalesYAt == 0 && traces[1].ScalesYAt == 1, "Native series share an incompatible scale.");
                Require(traces[0].Values!.Last().Y == 33 && traces[1].Values!.Last().Y == 15.5, "Native series did not apply independent raw-to-display conversion.");
                Require(native.YAxes.First().MaxLimit == 100 && native.YAxes.Last().MaxLimit > 15.5 && native.YAxes.Last().MaxLimit < 18, "Native axes do not scale independently.");
                var scaleLabels = plot.Children.OfType<Canvas>().SelectMany(c => c.Children.OfType<TextBlock>()).ToArray();
                Require(scaleLabels.Any(l => l.Text.Contains('%') && !double.IsNaN(Canvas.GetLeft(l))) &&
                    scaleLabels.Any(l => l.Text.Contains("GiB") && !double.IsNaN(Canvas.GetRight(l))), "Inside left/right unit labels are absent.");
                plot.SetCursor(now);
                Require(plot.InspectionText.Contains("33") && plot.InspectionText.Contains("15.5") && plot.InspectionText.Contains("GiB"), "Crosshair inspection lost independently converted values.");
                plot.SetCursor(null); plot.SavePng(Path.Combine(output, "chart-dual-axis.png"));
                var bitmap = new BitmapImage(new Uri(Path.Combine(output, "chart-dual-axis.png")));
                Require(bitmap.PixelWidth >= 1100 && bitmap.PixelHeight == 400, "PNG export did not capture the rendered chart bounds.");
                // Freeze, accept later telemetry, and verify view data remains unchanged until reset.
                var menu = plot.ContextMenu!;
                void Command(string prefix) => menu.Items.OfType<MenuItem>().Single(i => ((string)i.Header).StartsWith(prefix, StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Command("Pause chart"); var frozen = plot.VisibleSamples.ToArray();
                var later = now.AddMinutes(1);
                plot.Update([usage.Concat([new(device, "usage", later, 91)]).ToArray(), memory.Concat([new(device, "memory", later, 16L * 1073741824)]).ToArray()], later, 5);
                Require(plot.IsViewPaused && plot.ViewRange!.End == now && plot.VisibleSamples.SequenceEqual(frozen), "Incoming telemetry changed a paused view.");
                Command("Zoom in");
                Require(plot.ViewRange!.Start == now.AddSeconds(-225) && plot.ViewRange.End == now.AddSeconds(-75) && plot.VisibleSamples.Count == 30,
                    "Native zoom did not use its measured bounded time window.");
                Command("Pan earlier"); Require(plot.ViewRange!.End == now.AddSeconds(-112.5), "Native pan did not move the complete time window.");
                using var csv = new StringWriter(); PlotData.WriteCsv(csv, plot.ExportRows("wrong-fallback"));
                Require(csv.ToString().Contains("\"B\"") && csv.ToString().Contains("\"%\"") && !csv.ToString().Contains("wrong-fallback"), "Mixed-unit export mislabeled raw measurements.");
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                var visibility = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Visible series");
                visibility.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Require(!traces[1].IsVisible && plot.ExportRows("unused").All(r => r.Unit == "%"), "Hidden series still renders or leaks into visible-data export.");
                Command("Reset to live"); Require(!plot.IsViewPaused && plot.VisibleSamples.Any(s => s.Value == 91), "Reset did not resume from the latest independently collected samples.");
                plot.ShowSeries(1, true); plot.Update([usage, memory], now, 5);
                plot.ConfigureAxes([new(0, "%"), new(1, "MiB")], [0, 1]);
                plot.Update([usage, memory.Select(s => new TelemetrySample(s.Device, s.MetricKey, s.Time, s.Value / 1048576)).ToArray()], now, 5);
                Require(traces[1].Values!.Last().Y == 15.5 && plot.ExportRows("unused").Last(r => r.Unit == "MiB").Sample.Value == 15872,
                    "MiB display conversion changed raw exported measurements.");
                File.WriteAllText(Path.Combine(output, "charts-result.txt"), "PASS: Native mixed-unit picker, left/right inside labels, independent percent/bytes/MiB scales, exact inspection, menu zoom/pan, paused snapshot under incoming telemetry, reset to live, series visibility, raw-unit CSV and PNG capture. Synthetic fixtures only; no physical pointer, target-DPI or hardware signoff.");
                // Exercise the production synchronization boundary with distinct
                // data, including a gap, rather than just calling both cursors.
                var follower = new TelemetryPlot("%", 100) { Height = 240 };
                follower.SetSeries([("Other device", "#33DF32")]);
                var otherSamples = usage.Select((s, i) => new TelemetrySample(s.Device with { Id = "other" }, s.MetricKey, s.Time, i == 30 ? null : 70)).ToArray();
                follower.Update([otherSamples], now, 5); root.Children.Add(follower);
                var linked = new ChartSynchronization(); linked.Add(plot); linked.Add(follower); linked.Add(plot);
                Require(linked.Count == 2, "Synchronization registered a chart twice.");
                plot.InspectAt(plot.VerificationSamplePosition(0, usage[^1]));
                Require(follower.IsInspecting && follower.InspectionText.Contains("N/A"), "Synchronized inspection fabricated a value over a missing measurement.");
                plot.InspectAt(new Point(-1, -1));
                Require(!follower.IsInspecting, "Leaving the originating plot retained a remote cursor.");
                plot.Zoom(.5);
                Require(follower.ViewRange == plot.ViewRange && follower.IsViewPaused, "Zoom did not synchronize exact time limits.");
                plot.Pan(-.25);
                Require(follower.ViewRange == plot.ViewRange, "Pan timelines drifted.");
                follower.Update([otherSamples.Concat([new(otherSamples[0].Device, "usage", later, 99)]).ToArray()], later, 5);
                Require(follower.VisibleSamples.All(s => s.Value != 99), "Incoming measurements changed a linked frozen view.");
                plot.ResetView();
                Require(!follower.IsViewPaused && follower.VisibleSamples.Any(s => s.Value == 99), "Linked reset did not resume the follower's own telemetry.");
                linked.Enabled = false; plot.PauseView();
                Require(!follower.IsViewPaused, "Disabled synchronization still propagated a pause.");
                linked.Remove(plot); linked.Remove(follower);
                Require(linked.Count == 0, "Unloaded charts retained synchronization subscriptions.");
                var timeline = new EventTimelinePanel(); root.Children.Clear(); root.Children.Add(timeline);
                var trace = new TelemetryEvent("fixture", "span", now.AddSeconds(-10), "tool.finished", CorrelationId: "trace-1", Duration: TimeSpan.FromSeconds(20), Name: "synthetic-tool");
                timeline.Update([sources[0] with { EventHistory = [trace] }], now, 5);
                root.Measure(new Size(1200, 650)); root.Arrange(new Rect(0, 0, 1200, 650)); root.UpdateLayout();
                Require(timeline.View.Entries.Single().Start == now.AddSeconds(-30), "Native timeline did not show the measured span duration.");
                timeline.SetRange(new(now.AddSeconds(-25), now.AddSeconds(-5)));
                Require(timeline.View.Range.Duration == TimeSpan.FromSeconds(20) && timeline.View.Entries.Count == 1, "Timeline did not retain overlapping events under shared zoom limits.");
                timeline.Update([sources[0] with { EventHistory = [] }], later, 5);
                Require(timeline.View.Entries.Count == 1, "Paused event timeline lost its frozen metadata.");
                timeline.SetCursor(now.AddSeconds(-15));
                var timelineBitmap = new RenderTargetBitmap(1200, 650, 96, 96, PixelFormats.Pbgra32); timelineBitmap.Render(root);
                var timelineEncoder = new PngBitmapEncoder(); timelineEncoder.Frames.Add(BitmapFrame.Create(timelineBitmap));
                using (var stream = File.Create(Path.Combine(output, "activity-timeline.png"))) timelineEncoder.Save(stream);
                timeline.SetRange(null); Require(timeline.View.Entries.Count == 0, "Timeline did not resume the latest event snapshot.");
                File.AppendAllText(Path.Combine(output, "charts-result.txt"), "\nPASS: Cross-chart exact cursors, missing-value inspection, zoom/pan/pause/reset, frozen independent samples, opt-out and subscription cleanup.");
                File.AppendAllText(Path.Combine(output, "charts-result.txt"), "\nPASS: Native activity timeline span geometry, overlapping range, independent frozen event metadata, shared cursor and reset. Synthetic fixture capture: activity-timeline.png.");
                var sourceSettings = new SourceSettingsWindow(window, new Settings(), () => sources, () => { });
                var defaults = sourceSettings.BuildConfiguration();
                Require(defaults.Runtimes.Count == 3 && defaults.Runtimes.All(s => !s.Enabled) && defaults.Feeds.Count == 0, "Source editor enabled optional adapters without consent.");
                sourceSettings.VerifyRuntime("vllm", true, "http://127.0.0.1:8000/metrics");
                using (var enabledSources = BackendProviderSet.Create(sourceSettings.BuildConfiguration()))
                    Require(enabledSources.Providers.Any(p => p.SourceId == "runtime:vllm-local"), "Source editor did not configure the production vLLM collector.");
                sourceSettings.VerifyRuntime("vllm", true, "http://user:secret@127.0.0.1:8000/metrics");
                bool rejected = false;
                try { using var invalid = BackendProviderSet.Create(sourceSettings.BuildConfiguration()); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Source editor accepted embedded credentials."); sourceSettings.Close();
                File.AppendAllText(Path.Combine(output, "charts-result.txt"), "\nPASS: Source editor defaults, explicit vLLM configuration and credential rejection without polling endpoints.");
                window.Close(); app.Shutdown();
            }
            catch (Exception error) { Fail(error); }
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        try { app.Run(window); } finally { host?.Dispose(); }
    }
}
