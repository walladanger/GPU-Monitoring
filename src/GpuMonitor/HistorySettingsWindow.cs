using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class HistorySettingsWindow : Window
{
    internal readonly CheckBox Enabled = new() { Content = "Save numeric telemetry history on this computer", Foreground = Brushes.White, Margin = new(0, 10, 0, 12) };
    internal readonly ComboBox Days = new() { ItemsSource = new[] { 1, 3, 7, 14, 30 }, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    internal readonly System.Windows.Controls.Button Apply;
    private bool closed;
    public HistorySettingsWindow(Window owner, bool enabled, int days, string path, HistoryRecordingStatus status,
        Func<bool, int, Task<HistoryRecordingStatus>> apply, Action browse)
    {
        Owner = owner; Title = "Persistent history"; Width = 620; Height = 480; MinWidth = 500; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        var root = new StackPanel { Margin = new(20) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(OverviewTheme.DialogHeading(this));
        Enabled.IsChecked = enabled; root.Children.Add(Enabled);
        root.Children.Add(OverviewTheme.Text("Keep measurements for (days)", 13, OverviewTheme.Muted));
        Days.SelectedItem = days; if (Days.SelectedItem == null) Days.SelectedItem = 7; root.Children.Add(Days);
        var note = OverviewTheme.Text("Off by default · up to 250,000 samples / 128 MiB database. Numeric values, units, device IDs and timestamps only. Collection continues while minimized; closing stops collection. Disabling keeps existing records.", 12, OverviewTheme.Muted);
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new(0, 14, 0, 12); root.Children.Add(note);
        var location = OverviewTheme.Text(path, 11, OverviewTheme.Muted); location.TextWrapping = TextWrapping.Wrap; root.Children.Add(location);
        var state = OverviewTheme.Text(status.Error ?? (enabled ? $"{status.Samples:N0} stored samples · {status.DroppedBatches} dropped batches" : "Recording is off."), 12, OverviewTheme.Muted);
        state.TextWrapping = TextWrapping.Wrap; state.Margin = new(0, 10, 0, 10); root.Children.Add(state);
        var buttons = new WrapPanel(); root.Children.Add(buttons);
        buttons.Children.Add(OverviewTheme.Command("Browse saved history", browse));
        Apply = OverviewTheme.Command("Apply", async () =>
        {
            Apply!.IsEnabled = false;
            try
            {
                var result = await apply(Enabled.IsChecked == true, (int)Days.SelectedItem);
                if (closed) return;
                if (result.Error != null) state.Text = "History unavailable: " + result.Error;
                else DialogResult = true;
            }
            catch (Exception error) { if (!closed) state.Text = error.Message; }
            finally { if (!closed) Apply!.IsEnabled = true; }
        }); buttons.Children.Add(Apply);
        var cancel = OverviewTheme.Command("Close", Close); cancel.IsCancel = true; buttons.Children.Add(cancel);
        Closed += (_, _) => closed = true;
    }
}
