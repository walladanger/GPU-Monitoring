using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GpuMonitor;

/// <summary>Counter differences use elapsed monotonic time, never the wall clock.</summary>
public sealed class CounterRate
{
    private (long Value, long Tick)? previous;
    public double? Observe(long value, long tick)
    {
        var old = previous;
        previous = (value, tick);
        if (value < 0 || old == null || old.Value.Value < 0 || value < old.Value.Value || tick <= old.Value.Tick) return null;
        return (value - (double)old.Value.Value) * Stopwatch.Frequency / (tick - (double)old.Value.Tick);
    }
}

public sealed class WindowsSystemCollector : ITelemetryProvider
{
    public string SourceId => "windows-system";
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("windows-system", "cpu.usage", "CPU usage", "%", 100),
        new("windows-system", "cpu.logical", "Logical processors", "count"),
        new("windows-system", "ram.total", "Physical RAM total", "B"),
        new("windows-system", "ram.available", "Physical RAM available", "B"),
        new("windows-system", "ram.used", "Physical RAM used", "B") });
    private (ulong Idle, ulong Kernel, ulong User)? previous;

    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows hardware collection requires Windows.");
        var now = DateTimeOffset.UtcNow;
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var cpu = new DeviceIdentity(SourceId, "cpu-total", DeviceKind.Cpu,
            (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "CPU", "0");
        var ram = new DeviceIdentity(SourceId, "physical-memory", DeviceKind.Memory, "Physical RAM", "0");
        var samples = new List<TelemetrySample>();
        uint logicalProcessors = GetActiveProcessorCount(0xffff);
        double? usage = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (logicalProcessors is > 0 and <= 64 && previous is { } old) usage = CpuUsage(old.Idle, old.Kernel, old.User, idle, kernel, user);
            previous = (idle, kernel, user);
        }
        else previous = null;
        samples.Add(new(cpu, "cpu.usage", now, usage));
        samples.Add(new(cpu, "cpu.logical", now, logicalProcessors > 0 ? logicalProcessors : null));
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool available = GlobalMemoryStatusEx(ref memory);
        samples.Add(new(ram, "ram.total", now, available ? memory.TotalPhysical : null));
        samples.Add(new(ram, "ram.available", now, available ? memory.AvailablePhysical : null));
        samples.Add(new(ram, "ram.used", now, available ? memory.TotalPhysical - memory.AvailablePhysical : null));
        return Task.FromResult(Batch([cpu, ram], samples));
    }

    // GetSystemTimes includes idle in kernel. More than 64 logical CPUs span processor groups:
    // return unavailable rather than presenting one group's measurement as the entire system.
    internal static double? CpuUsage(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong idle, ulong kernel, ulong user)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        double total = (double)(kernel - oldKernel) + (user - oldUser);
        double idleDelta = idle - oldIdle;
        return total <= 0 || idleDelta > total ? null : 100 * (total - idleDelta) / total;
    }

    internal static TelemetryBatch Batch(IReadOnlyList<DeviceIdentity> devices, List<TelemetrySample> samples) =>
        new(devices, Array.AsReadOnly(samples.Select(s => new MetricCapability(s.Device, s.MetricKey, s.Availability)).ToArray()),
            samples.AsReadOnly(), Array.Empty<TelemetryEvent>());

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}

public sealed class NetworkCollector : ITelemetryProvider
{
    public string SourceId => "network";
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("network", "network.receive", "Network receive", "B/s"),
        new("network", "network.send", "Network send", "B/s") });
    private readonly Dictionary<string, (CounterRate Rx, CounterRate Tx)> rates = [];
    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            cancellation.ThrowIfCancellationRequested();
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var device = new DeviceIdentity(SourceId, nic.Id, DeviceKind.Network, nic.Name, nic.Id);
            devices.Add(device);
            double? rx = null, tx = null;
            try
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    var stat = nic.GetIPStatistics(); var tick = Stopwatch.GetTimestamp();
                    if (!rates.TryGetValue(nic.Id, out var rate)) rates[nic.Id] = rate = (new(), new());
                    rx = rate.Rx.Observe(stat.BytesReceived, tick); tx = rate.Tx.Observe(stat.BytesSent, tick);
                }
                else rates.Remove(nic.Id);
            }
            catch (NetworkInformationException) { rates.Remove(nic.Id); }
            samples.Add(new(device, "network.receive", DateTimeOffset.UtcNow, rx));
            samples.Add(new(device, "network.send", DateTimeOffset.UtcNow, tx));
        }
        foreach (var id in rates.Keys.Except(devices.Select(d => d.Id)).ToArray()) rates.Remove(id);
        return Task.FromResult(WindowsSystemCollector.Batch(devices.AsReadOnly(), samples));
    }
}

/// <summary>Logical fixed-volume capacity only; does not claim physical disk I/O or busy time.</summary>
public sealed class VolumeCollector : ITelemetryProvider
{
    public string SourceId => "volumes";
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("volumes", "volume.total", "Volume capacity", "B"),
        new("volumes", "volume.free", "Volume free", "B"),
        new("volumes", "volume.used", "Volume used", "B") });
    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                // Windows volume GUID survives drive-letter changes; skip unresolved volumes.
                var id = new System.Text.StringBuilder(1024);
                if (!OperatingSystem.IsWindows() || !GetVolumeNameForVolumeMountPoint(drive.Name, id, id.Capacity)) continue;
                var device = new DeviceIdentity(SourceId, id.ToString(), DeviceKind.Disk, drive.Name, drive.Name);
                devices.Add(device);
                double? total = null, free = null;
                try { if (drive.IsReady) { total = drive.TotalSize; free = drive.TotalFreeSpace; } }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                var now = DateTimeOffset.UtcNow;
                samples.Add(new(device, "volume.total", now, total));
                samples.Add(new(device, "volume.free", now, free));
                samples.Add(new(device, "volume.used", now, total - free));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Task.FromResult(WindowsSystemCollector.Batch(devices.AsReadOnly(), samples));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mount, System.Text.StringBuilder name, int length);
}
