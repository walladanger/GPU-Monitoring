namespace GpuMonitor;

public sealed partial class OverviewWindow
{
    private readonly string historyPath;
    private void OpenStoredHistory() => new HistoryWindow(this, historyPath).Show();
    private void OpenHistorySettings()
    {
        HistorySettingsWindow? dialog = null;
        dialog = new(this, settings.HistoryEnabled, settings.HistoryDays, historyPath,
            monitoring?.HistoryStatus ?? new(false, false, null, 0, 0), ConfigureHistoryRecordingAsync,
            () => new HistoryWindow(dialog!, historyPath).ShowDialog());
        dialog.ShowDialog();
    }
    internal async Task<HistoryRecordingStatus> ConfigureHistoryRecordingAsync(bool enabled, int days)
    {
        settings.HistoryEnabled = enabled; settings.HistoryDays = days; settings.Normalize(); Save();
        if (monitoring == null) return new(enabled, false, "Monitoring owner is unavailable.", 0, 0);
        await monitoring.ConfigureHistoryAsync(enabled ? new HistoryOptions(historyPath, settings.HistoryDays) : null);
        return monitoring.HistoryStatus;
    }
}
