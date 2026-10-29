using System.IO;
using System.Text.Json;

namespace GpuMonitor;

public sealed class Settings
{
    public int RefreshSeconds { get; set; } = 2;
    public int WindowMinutes { get; set; } = 5;
    public bool MinimizeToTray { get; set; } = true;
    public bool HistoryEnabled { get; set; }
    public int HistoryDays { get; set; } = 7;
    public bool TrayHoverEnabled { get; set; } = true;
    public double TrayOpacity { get; set; } = .88;
    public List<WorkspaceSeries>? TraySensors { get; set; }
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitor", "settings.json");
    public static string HistoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitor", "history", "telemetry.sqlite");
    public void Normalize()
    {
        RefreshSeconds = new[] { 1, 2, 5, 10 }.Contains(RefreshSeconds) ? RefreshSeconds : 2;
        WindowMinutes = new[] { 1, 5, 15, 30 }.Contains(WindowMinutes) ? WindowMinutes : 5;
        HistoryDays = HistoryDays is >= 1 and <= 30 ? HistoryDays : 7;
        TrayOpacity = double.IsFinite(TrayOpacity) ? Math.Clamp(TrayOpacity, .65, 1) : .88;
        if (TraySensors != null)
        {
            try
            {
                TraySensors = TraySensors.Take(8).Select(s => s with { Axis = 0 }).Distinct().ToList();
                if (TraySensors.Count > 0) WorkspacePresetStore.Validate(new(1, "Tray", [new("tray", "Tray", 1, 1, "line", TraySensors)]));
            }
            catch (Exception error) when (error is FormatException or NullReferenceException) { TraySensors = null; }
        }
    }
    public static Settings Load(string? path = null)
    {
        try
        {
            using var file = File.OpenRead(path ?? FilePath); var bytes = new byte[65537]; int length = 0, read;
            while (length < bytes.Length && (read = file.Read(bytes, length, bytes.Length - length)) > 0) length += read;
            if (length > 65536) return new();
            var settings = JsonSerializer.Deserialize<Settings>(bytes.AsSpan(0, length)) ?? new(); settings.Normalize(); return settings;
        }
        catch { return new(); }
    }
    public void Save(string? path = null)
    {
        Normalize(); path ??= FilePath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
