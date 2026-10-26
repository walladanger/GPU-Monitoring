using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GpuMonitor;

internal static class TrayChecks
{
    public static void Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "overview-verification"); Directory.CreateDirectory(output);
        var app = new Application(); var owner = new Window { Title = "Synthetic tray verification", Width = 600, Height = 400 };
        using var icon = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = false };
        var preferences = new Settings { TraySensors = [] };
        var now = DateTimeOffset.UtcNow;
        var batch = NvidiaTelemetry.Normalize(CsvTelemetry.Parse("0, Synthetic RTX 3090, GPU-tray-fixture, bus, 24576, 42.125, 8192, 65, 300, 62, 1500, 9750", now));
        IReadOnlyList<ProviderSnapshot> sources = [new(NvidiaTelemetry.SourceId, ProviderHealth.Live, null, now, batch, [], NvidiaTelemetry.Definitions)];
        using var controller = new TrayQuickController(icon, preferences, () => sources, () => null, () => { }, () => { });
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Fail(Exception error) { File.WriteAllText(Path.Combine(output, "tray-result.txt"), "FAIL: " + error); Environment.ExitCode = 1; controller.Dispose(); owner.Close(); }
        owner.Loaded += async (_, _) =>
        {
            try
            {
                var dialog = new TraySettingsWindow(owner, preferences, sources, (hover, opacity, sensors) =>
                { preferences.TrayHoverEnabled = hover; preferences.TrayOpacity = opacity; preferences.TraySensors = sensors; });
                Exception? settingsFailure = null;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        foreach (var choice in dialog.VisibleChoices.Where(c => c.Key.MetricKey is "memory.used" or "power.draw" or "temperature.gpu")) choice.Value.IsChecked = true;
                        dialog.Hover.IsChecked = true; dialog.OpacitySlider.Value = .75;
                        dialog.Apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    catch (Exception error) { settingsFailure = error; dialog.Close(); }
                });
                Require(dialog.ShowDialog() == true && settingsFailure == null && preferences.TraySensors?.Count == 3 && preferences.TrayOpacity == .75,
                    "Native quick-view settings did not save three differently unit-ed sensors.");
                var cursor = System.Windows.Forms.Cursor.Position;
                controller.ShowAt(cursor.X, cursor.Y); await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Require(controller.Window.IsVisible && !controller.Window.ShowInTaskbar && !controller.Window.ShowActivated &&
                    GetForegroundWindow() != new WindowInteropHelper(controller.Window).Handle, "Hover popover activated or became a separate taskbar window.");
                Require(controller.Window.Readings.Count == 3 && controller.Window.Readings.Any(r => r.DisplayValue == "8 GiB") &&
                    controller.Window.Readings.Any(r => r.DisplayValue == "300 W"), "Native quick-view did not show the selected real-unit fixture values.");
                var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea; var bounds = controller.Window.ScreenBounds;
                Require(bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Left + bounds.Width <= area.Right + 1 && bounds.Top + bounds.Height <= area.Bottom + 1,
                    "Native quick-view escaped the cursor monitor's working area.");
                sources = [sources[0] with { Health = ProviderHealth.Stale }]; controller.ShowAt(cursor.X, cursor.Y);
                Require(controller.Window.Readings.All(r => r.State == WorkspaceBindingState.SourceStale && r.Time == now), "Stale quick-view lost the sample timestamp or claimed freshness.");
                sources = []; controller.ShowAt(cursor.X, cursor.Y);
                Require(controller.Window.Readings.All(r => r.Value == null && r.DisplayValue == "N/A" && r.Selection.DeviceId == "GPU-tray-fixture"), "Missing quick-view hardware was replaced by other readings.");
                sources = [new(NvidiaTelemetry.SourceId, ProviderHealth.Live, null, now, batch, [], NvidiaTelemetry.Definitions)];
                controller.ShowAt(cursor.X, cursor.Y); await Task.Delay(250);
                var bitmap = OverviewChecks.Capture((FrameworkElement)controller.Window.Content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, "tray-quick-view.png"))) encoder.Save(stream);
                controller.SetMenuOpen(true); Require(!controller.Window.IsVisible, "Opening the tray menu did not dismiss the popover.");
                controller.SetMenuOpen(false); controller.ShowAt(cursor.X, cursor.Y); controller.Hide();
                Require(!controller.Window.IsVisible, "Quick-view dismissal failed.");
                File.WriteAllText(Path.Combine(output, "tray-result.txt"), "PASS: Actual native quick-view settings Apply with independent units, non-activating/no-taskbar popup, monitor working-area placement, live/stale/missing bindings, menu suppression and explicit dismissal. Synthetic snapshots and programmatic show; real shell hover, physical input, multi-monitor DPI transitions and backdrop visual approval remain target-machine gates.");
                controller.Dispose(); owner.Close();
            }
            catch (Exception error) { Fail(error); }
        };
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Fail(e.Exception); };
        app.Run(owner);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
