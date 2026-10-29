using GpuMonitor;

internal static class ProcessCollectorChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var time = DateTimeOffset.UtcNow;
        var started = time.UtcDateTime.AddMinutes(-1);
        string csv = "GPU-A, 42, \"C:\\llama,server.exe\", 1024.125\nGPU-B, 42, llama-server.exe, [N/A]";
        var batch = NvidiaProcessCollector.Parse(csv, time, _ => started);
        check(batch.Devices.Count == 2 && batch.Devices[0].Id != batch.Devices[1].Id &&
            batch.Samples[0].Value == 1024.125 && batch.Samples[1].Value == null,
            "Compute process VRAM stays per-GPU with exact measured values and WDDM N/A");
        check(batch.Devices[0].Name == "llama,server.exe" && batch.Events[0].CorrelationId == "GPU-A",
            "Compute process exports omit executable paths and retain GPU UUID correlation");
        var reused = NvidiaProcessCollector.Parse(csv, time.AddSeconds(1), _ => started.AddSeconds(10));
        check(reused.Devices[0].Id != batch.Devices[0].Id, "A reused PID starts a separate GPU process history");
        var unverified = NvidiaProcessCollector.Parse(csv, time, _ => null);
        var unverifiedNext = NvidiaProcessCollector.Parse(csv, time.AddSeconds(1), _ => null);
        check(unverified.Devices[0].Id != unverifiedNext.Devices[0].Id,
            "Unavailable process start times cannot join unrelated PID observations");
        bool rejected = false;
        try { NvidiaProcessCollector.Parse("GPU-A, 42, test.exe, 20\nGPU-A, 42, test.exe, 20", time, _ => started); }
        catch (FormatException) { rejected = true; }
        check(rejected, "Duplicate GPU/PID rows are rejected rather than double counted");
        check(NvidiaProcessCollector.Parse("", time, _ => null).Devices.Count == 0,
            "An empty successful compute-process query contains no invented processes");
        var collector = new ProcessCollector();
        if (OperatingSystem.IsWindows())
        {
            var processes = await collector.ReadAsync(CancellationToken.None);
            check(processes.Devices.Any(d => d.DisplayIndex == Environment.ProcessId.ToString()) &&
                processes.Samples.Where(s => s.MetricKey == "process.cpu").All(s => s.Value == null),
                "Real process discovery includes this test process and leaves first CPU rates unavailable");
        }
        else
        {
            bool unsupported = false;
            try { await collector.ReadAsync(CancellationToken.None); } catch (PlatformNotSupportedException) { unsupported = true; }
            check(unsupported, "Windows process collection is explicitly unsupported on another OS");
        }
        var provider = new Fixture(); var session = new TelemetrySession(provider);
        for (int i = 0; i < 200; i++) session.Apply(NvidiaProcessCollector.Parse("GPU-A, 42, test.exe, 20", time.AddMilliseconds(i), _ => started), time);
        check(session.HistorySnapshot().Count == 120, "Process history policy caps each series at 120 samples");
    }

    private sealed class Fixture : ITelemetryProvider, ITelemetryHistoryPolicy
    {
        public string SourceId => "nvidia-processes";
        public int HistoryCapacity => 120;
        public TimeSpan HistoryRetention => TimeSpan.FromMinutes(5);
        public IReadOnlyList<MetricDefinition> Definitions => new NvidiaProcessCollector().Definitions;
        public Task<TelemetryBatch> ReadAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
