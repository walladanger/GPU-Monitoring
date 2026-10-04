using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GpuMonitor;

public sealed record GpuReading(string Index, string Name, string Id, string Bus,
    IReadOnlyDictionary<string, double?> Metrics, DateTimeOffset Time);

public sealed record Metric(string Key, string Title, string Unit, double? Scale = null);

public static class Metrics
{
    public static readonly Metric[] All = [
        new("utilization.gpu", "GPU utilization", "%", 100),
        new("memory.used", "VRAM used", "MiB"),
        new("temperature.gpu", "Core temperature", "°C", 110),
        new("power.draw", "Power draw", "W"),
        new("fan.speed", "Fan speed", "%", 100),
        new("clocks.current.graphics", "Graphics clock", "MHz"),
        new("clocks.current.memory", "Memory clock", "MHz")];
}

public static class CsvTelemetry
{
    // NVIDIA quotes names containing commas. Do not split CSV with string.Split.
    public static string[] Fields(string line)
    {
        var fields = new List<string>(); var current = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        if (quoted) throw new FormatException("Incomplete CSV row");
        fields.Add(current.ToString().Trim()); return fields.ToArray();
    }
    public static IReadOnlyList<GpuReading> Parse(string text, DateTimeOffset time)
    {
        var result = new List<GpuReading>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = Fields(line); if (f.Length != 12 || !f[2].StartsWith("GPU-")) throw new FormatException("Unexpected NVIDIA telemetry format");
            var values = new Dictionary<string, double?>();
            for (int i = 0; i < Metrics.All.Length; i++) values[Metrics.All[i].Key] = Number(f[i + 5]);
            values["memory.total"] = Number(f[4]);
            result.Add(new(f[0], f[1], f[2], f[3], values, time));
        }
        if (result.Select(g => g.Id).Distinct().Count() != result.Count) throw new FormatException("Duplicate GPU UUID");
        return result;
    }
    private static double? Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0 ? n : null;
}

public sealed class Inventory
{
    public Dictionary<string, Queue<GpuReading>> History { get; } = [];
    public IReadOnlyList<GpuReading> Current { get; private set; } = [];
    public void Apply(IReadOnlyList<GpuReading> readings)
    {
        var ids = readings.Select(g => g.Id).ToHashSet();
        foreach (var id in History.Keys.Where(id => !ids.Contains(id)).ToArray()) History.Remove(id);
        foreach (var g in readings)
        {
            if (!History.TryGetValue(g.Id, out var history)) History[g.Id] = history = new();
            history.Enqueue(g);
            while (history.Count > 3600 || (history.Count > 1 && g.Time - history.Peek().Time > TimeSpan.FromMinutes(30))) history.Dequeue();
        }
        Current = readings;
    }
}

public sealed class NvidiaCollector
{
    private readonly string executable = FindExecutable();
    private static string FindExecutable()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        var vendor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        return File.Exists(system) ? system : File.Exists(vendor) ? vendor : "nvidia-smi.exe";
    }
    public async Task<IReadOnlyList<GpuReading>> ReadAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--query-gpu=index,name,uuid,pci.bus_id,memory.total," + string.Join(',', Metrics.All.Select(m => m.Key)));
        start.ArgumentList.Add("--format=csv,noheader,nounits");
        using var process = Process.Start(start) ?? throw new IOException("Could not start NVIDIA telemetry");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output; var error = await errors;
            if (process.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(error) ? text.Trim() : error.Trim());
            return CsvTelemetry.Parse(text, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
    }
}
