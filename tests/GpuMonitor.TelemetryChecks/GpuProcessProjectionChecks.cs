using GpuMonitor;

internal static class GpuProcessProjectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
        var collector = new NvidiaProcessCollector();
        var batch = NvidiaProcessCollector.Parse(
            "GPU-fixture-0,101,model-worker.exe,2048\nGPU-fixture-1,202,other-worker.exe,1024\nGPU-fixture-0,303,memory-unknown.exe,N/A",
            now, _ => now.UtcDateTime);
        ProviderSnapshot snapshot = new("nvidia-processes", ProviderHealth.Live, null, now,
            batch, batch.Samples, collector.Definitions);
        var first = GpuProcessProjection.ForGpu("GPU-fixture-0", [snapshot]);
        check(first.Processes.Count == 2 && first.Processes.All(p => p.Pid != 202),
            "Per-GPU process view never mixes GPU 0 and GPU 1 processes");
        check(first.Processes[0].Pid == 101 && first.Processes[0].VramMiB == 2048 &&
            first.Processes[0].Name == "model-worker.exe",
            "Per-process PID, name and measured VRAM are preserved");
        check(first.Processes.Single(p => p.Pid == 303).VramMiB == null,
            "Unavailable process VRAM is not fabricated as zero");
        var second = GpuProcessProjection.ForGpu("GPU-fixture-1", [snapshot]);
        check(second.Processes.Count == 1 && second.Processes[0].Pid == 202,
            "Filtering by stable GPU UUID continues to work independently");
        var unavailable = GpuProcessProjection.ForGpu("GPU-fixture-0", []);
        check(unavailable.Processes.Count == 0 && unavailable.Status.Contains("not enabled", StringComparison.OrdinalIgnoreCase),
            "Missing NVIDIA process collector is shown as not enabled");
        var stale = GpuProcessProjection.ForGpu("GPU-fixture-0", [snapshot with { Health = ProviderHealth.Stale, Error = "collector failed" }]);
        check(stale.Processes.Count == 0 && stale.Status.Contains("stale", StringComparison.OrdinalIgnoreCase),
            "Stale process inventory is not shown as live");
        var emptyBatch = NvidiaProcessCollector.Parse("", now, _ => null);
        var emptySource = snapshot with { Current = emptyBatch };
        check(GpuProcessProjection.ForGpu("GPU-fixture-0", [emptySource]).Status.Contains("No NVIDIA compute", StringComparison.Ordinal),
            "Empty compute-process inventory is distinguished from telemetry failure");
        check(GpuProcessProjection.ForGpu("cpu-total", [snapshot]).Processes.Count == 0,
            "Non-GPU panels do not mistakenly claim NVIDIA GPU processes");
    }
}
