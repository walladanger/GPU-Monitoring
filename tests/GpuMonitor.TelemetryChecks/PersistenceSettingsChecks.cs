using GpuMonitor;

internal static class PersistenceSettingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "gpu-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"RefreshSeconds\":5,\"WindowMinutes\":15,\"MinimizeToTray\":false}");
            var legacy = Settings.Load(path);
            check(!legacy.HistoryEnabled && legacy.TraySensors == null && legacy.RefreshSeconds == 5 && legacy.WindowMinutes == 15 && !legacy.MinimizeToTray,
                "Legacy preferences preserve refresh/range/tray behavior and keep new SQLite recording off");
            legacy.HistoryEnabled = true; legacy.HistoryDays = 3; legacy.TrayHoverEnabled = false; legacy.TrayOpacity = .75;
            legacy.TraySensors = [new("nvidia-smi", "GPU-stable", "power.draw")]; legacy.Save(path);
            var loaded = Settings.Load(path);
            check(loaded.HistoryEnabled && loaded.HistoryDays == 3 && !loaded.TrayHoverEnabled && loaded.TrayOpacity == .75 && loaded.TraySensors!.Single().DeviceId == "GPU-stable",
                "History and tray preferences persist stable sensor IDs and conservative display choices atomically");
            loaded.TraySensors = []; loaded.Save(path);
            check(Settings.Load(path).TraySensors?.Count == 0, "An intentionally empty tray selection remains empty instead of silently reverting to other hardware");
            loaded.HistoryDays = 999; loaded.TrayOpacity = -1; loaded.RefreshSeconds = 0; loaded.Normalize();
            check(loaded.HistoryDays == 7 && loaded.TrayOpacity == .65 && loaded.RefreshSeconds == 2, "Invalid retention/opacity/refresh preferences remain within supported bounds");
        }
        finally { Directory.Delete(root, true); }
    }
}
