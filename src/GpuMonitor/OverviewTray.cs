using System.Windows;
using Forms = System.Windows.Forms;

namespace GpuMonitor;

public sealed partial class OverviewWindow
{
    private TrayQuickController? quickView;
    private IReadOnlyList<ProviderSnapshot> QuickSources() => monitoring?.Snapshot(includeHistory: false) ?? WorkspaceSources();
    private void InitializeTray()
    {
        quickView = new(tray!, settings, QuickSources, () => monitoring?.HistoryStatus, Restore, OpenTraySettings, () => paused);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open GPU Monitor", null, (_, _) => Restore());
        menu.Items.Add("Show quick-view", null, (_, _) => { var point = Forms.Cursor.Position; quickView.ShowAt(point.X, point.Y); });
        menu.Items.Add("Quick-view settings…", null, (_, _) => OpenTraySettings());
        menu.Items.Add("Saved history…", null, (_, _) => { Restore(); OpenStoredHistory(); });
        var presets = new Forms.ToolStripMenuItem("Workspace presets"); menu.Items.Add(presets);
        menu.Items.Add("GPU details", null, (_, _) => { Restore(); details = true; BuildNavigation(); Render(viewTime); });
        menu.Items.Add("System panels", null, (_, _) => { Restore(); hardwareRow.BringIntoView(); });
        menu.Items.Add("Exit", null, (_, _) => Close());
        menu.Opening += (_, _) =>
        {
            quickView.SetMenuOpen(true); presets.DropDownItems.Clear();
            try
            {
                foreach (string id in customWorkspace.PresetNames())
                    presets.DropDownItems.Add(id, null, async (_, _) =>
                    {
                        try { Restore(); await customWorkspace.LoadPresetAsync(id); }
                        catch (Exception error) { MessageBox.Show(this, error.Message, "Workspace preset could not be loaded"); }
                    });
                if (presets.DropDownItems.Count == 0) presets.DropDownItems.Add(new Forms.ToolStripMenuItem("No saved presets") { Enabled = false });
            }
            catch (Exception error) { presets.DropDownItems.Add(new Forms.ToolStripMenuItem("Presets unavailable") { Enabled = false, ToolTipText = error.Message }); }
        };
        menu.Closed += (_, _) => quickView.SetMenuOpen(false); tray!.ContextMenuStrip = menu;
    }
    private void OpenTraySettings()
    {
        Restore();
        var dialog = new TraySettingsWindow(this, settings, QuickSources(), (hover, opacity, sensors) =>
        { settings.TrayHoverEnabled = hover; settings.TrayOpacity = opacity; settings.TraySensors = sensors; Save(); quickView?.Hide(); });
        dialog.ShowDialog();
    }
}
