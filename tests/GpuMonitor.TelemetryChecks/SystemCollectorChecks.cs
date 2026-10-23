using System.Diagnostics;
using System.Text.Json;
using GpuMonitor;

internal static class SystemCollectorChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(DiskCollector.BusyFromIdle(75) == 25 && DiskCollector.BusyFromIdle(null) == null && DiskCollector.BusyFromIdle(101) == null,
            "Physical disk busy time uses valid idle percentage and preserves unavailable data");
        check(DiskCollector.CounterValue(0, 12345) == 12345 && DiskCollector.CounterValue(1, 0) == 0 &&
            DiskCollector.CounterValue(0xC0000BC6, 100) == null && DiskCollector.CounterValue(0, double.NaN) == null,
            "Disk counters accept measured zero and reject invalid status/nonfinite readings");
        var rate = new CounterRate();
        check(rate.Observe(1000, 0) == null, "Counter warmup is unavailable, never a fabricated zero");
        check(rate.Observe(3048, Stopwatch.Frequency * 2) == 1024, "Counter rate reports bytes per monotonic second");
        check(rate.Observe(5, Stopwatch.Frequency * 3) == null, "Counter reset produces a gap rather than a negative spike");
        check(rate.Observe(105, Stopwatch.Frequency * 4) == 100, "Counter recovers using the reset baseline");
        check(rate.Observe(205, Stopwatch.Frequency * 4) == null, "Zero elapsed time cannot produce an infinite rate");
        check(WindowsSystemCollector.CpuUsage(100, 200, 100, 120, 250, 150) == 80,
            "CPU percentage correctly subtracts idle already included in kernel time");
        check(WindowsSystemCollector.CpuUsage(100, 200, 100, 100, 200, 100) == null &&
            WindowsSystemCollector.CpuUsage(100, 200, 100, 90, 250, 150) == null,
            "CPU warmup and counter regression remain unavailable");
        if (!OperatingSystem.IsWindows())
        {
            bool rejected = false;
            try { await new WindowsSystemCollector().ReadAsync(CancellationToken.None); }
            catch (PlatformNotSupportedException) { rejected = true; }
            check(rejected, "Windows collector fails explicitly on another operating system");
        }
        var directory = Path.Combine(Path.GetTempPath(), "gpu-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var device = new DeviceIdentity("test", "stable-id", DeviceKind.Network, "Test, \"NIC\"", "0");
            var time = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
            TelemetrySample[] samples = [new(device, "rx", time, 1.23456789012345), new(device, "rx", time.AddSeconds(1), null)];
            ProviderSnapshot[] snapshot = [new("test", ProviderHealth.Stale, "disconnected", time,
                new([device], [], samples, []), samples, [new("test", "rx", "Receive", "B/s")])];
            await HeadlessCollector.ExportAsync(directory, snapshot, CancellationToken.None);
            var csv = await File.ReadAllTextAsync(Path.Combine(directory, "telemetry.csv"));
            check(csv.Contains("\"Test, \"\"NIC\"\"\"") && csv.Contains("1.23456789012345") && csv.EndsWith(",\r\n"),
                "Headless CSV preserves quoted names, raw precision and missing-value gaps");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "telemetry.json")));
            check(json.RootElement[0].GetProperty("Health").GetString() == "Stale" &&
                json.RootElement[0].GetProperty("Definitions")[0].GetProperty("Unit").GetString() == "B/s" &&
                json.RootElement[0].GetProperty("History")[1].GetProperty("Value").ValueKind == JsonValueKind.Null,
                "JSON export retains source health, metric units and null samples");
            check((await File.ReadAllTextAsync(Path.Combine(directory, "telemetry.txt"))).Contains("disconnected") &&
                Directory.GetFiles(directory, "*.tmp").Length == 0, "Text report exposes failures and completed exports leave no temporary files");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
