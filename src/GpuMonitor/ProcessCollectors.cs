using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GpuMonitor;

/// <summary>Processes use short histories to bound aggregate memory independently of GPU charts.</summary>
public interface ITelemetryHistoryPolicy
{
    int HistoryCapacity { get; }
    TimeSpan HistoryRetention { get; }
}

public sealed class ProcessCollector : ITelemetryProvider, ITelemetryHistoryPolicy
{
    public string SourceId => "windows-processes";
    public int HistoryCapacity => 120;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(5);
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("windows-processes", "process.cpu", "Process CPU usage", "%", 100),
        new("windows-processes", "process.working_set", "Working set", "B"),
        new("windows-processes", "process.private", "Private memory", "B") });
    private readonly Dictionary<string, CounterRate> rates = [];

    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows process collection requires Windows.");
        var processes = Process.GetProcesses();
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
        try
        {
            if (processes.Length > 2048) throw new IOException("Process inventory exceeds the 2048-process limit.");
            foreach (var process in processes)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var started = process.StartTime.ToUniversalTime();
                    var id = Identity(process.Id, started);
                    var device = new DeviceIdentity(SourceId, id, DeviceKind.Process, process.ProcessName,
                        process.Id.ToString(CultureInfo.InvariantCulture));
                    var cpuTicks = process.TotalProcessorTime.Ticks;
                    long working = process.WorkingSet64, privateBytes = process.PrivateMemorySize64;
                    if (process.HasExited) continue;
                    if (!rates.TryGetValue(id, out var rate)) rates[id] = rate = new();
                    var percent = rate.Observe(cpuTicks, Stopwatch.GetTimestamp()) / TimeSpan.TicksPerSecond * 100 / Environment.ProcessorCount;
                    if (percent < 0 || percent > 100) percent = null;
                    var now = DateTimeOffset.UtcNow;
                    devices.Add(device);
                    samples.Add(new(device, "process.cpu", now, percent));
                    samples.Add(new(device, "process.working_set", now, working));
                    samples.Add(new(device, "process.private", now, privateBytes));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        foreach (var id in rates.Keys.Except(devices.Select(d => d.Id)).ToArray()) rates.Remove(id);
        return Task.FromResult(WindowsSystemCollector.Batch(devices.AsReadOnly(), samples));
    }

    internal static string Identity(int pid, DateTime startedUtc) => pid.ToString(CultureInfo.InvariantCulture) + "/" + startedUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
}

public sealed class NvidiaProcessCollector : ITelemetryProvider, ITelemetryHistoryPolicy
{
    public string SourceId => "nvidia-processes";
    public int HistoryCapacity => 120;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(5);
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("nvidia-processes", "process.gpu_memory", "Compute process VRAM", "MiB") });

    public async Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        var text = await NvidiaCommand.RunAsync(["--query-compute-apps=gpu_uuid,pid,process_name,used_gpu_memory", "--format=csv,noheader,nounits"], cancellation);
        return Parse(text, DateTimeOffset.UtcNow, pid =>
        {
            try { using var process = Process.GetProcessById(pid); return process.StartTime.ToUniversalTime(); }
            catch (Exception error) when (error is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException) { return null; }
        });
    }

    internal static TelemetryBatch Parse(string text, DateTimeOffset now, Func<int, DateTime?> startTime)
    {
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>(); var events = new List<TelemetryEvent>();
        var seen = new HashSet<(string Gpu, int Pid)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = CsvTelemetry.Fields(line);
            if (fields.Length != 4 || !fields[0].StartsWith("GPU-", StringComparison.Ordinal) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 || !seen.Add((fields[0], pid)))
                throw new FormatException("Unexpected NVIDIA compute-process format.");
            var started = startTime(pid);
            // With no accessible start time, never join observations of a potentially reused PID.
            string lifetime = started.HasValue ? ProcessCollector.Identity(pid, started.Value) : pid + "/unverified/" + now.UtcTicks;
            var device = new DeviceIdentity("nvidia-processes", fields[0] + "/" + lifetime, DeviceKind.Process,
                Path.GetFileName(fields[2].Replace('\\', '/')), fields[1]);
            double? memory = double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : null;
            devices.Add(device); samples.Add(new(device, "process.gpu_memory", now, memory));
            events.Add(new("nvidia-processes", device.Id + "/" + now.UtcTicks, now, "gpu.process.observed", device.Id, fields[0]));
        }
        return new(devices.AsReadOnly(), Array.AsReadOnly(samples.Select(s => new MetricCapability(s.Device, s.MetricKey, s.Availability)).ToArray()), samples.AsReadOnly(), events.AsReadOnly());
    }
}

internal static class NvidiaCommand
{
    public static async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var start = new ProcessStartInfo(NvidiaCollector.FindExecutable()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start NVIDIA process collector.");
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            // A bounded-reader failure must terminate the child instead of waiting on a full pipe.
            var exit = process.WaitForExitAsync(timeout.Token);
            var first = await Task.WhenAny(output, errors, exit);
            if (first.IsFaulted || first.IsCanceled) await first;
            await Task.WhenAll(output, errors, exit);
            if (process.ExitCode != 0) throw new IOException("NVIDIA compute-process query failed; driver support or permissions may be unavailable.");
            return await output;
        }
        finally { if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } } }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            if (result.Length + count > 1024 * 1024) throw new IOException("NVIDIA compute-process output exceeds limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
