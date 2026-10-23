using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GpuMonitor;

/// <summary>Read-only PhysicalDisk performance counters; no diskperf or device-control commands.</summary>
public sealed class DiskCollector : ITelemetryProvider, IDisposable
{
    public string SourceId => "windows-disks";
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("windows-disks", "disk.read", "Disk read", "B/s"),
        new("windows-disks", "disk.write", "Disk write", "B/s"),
        new("windows-disks", "disk.busy", "Disk active time", "%", 100),
        new("windows-disks", "disk.queue", "Disk queue depth", "count") });
    private static readonly string[] Paths = ["Disk Read Bytes/sec", "Disk Write Bytes/sec", "% Idle Time", "Current Disk Queue Length"];
    private SafeQuery? query;
    private readonly IntPtr[] counters = new IntPtr[4];
    private bool disposed;

    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Physical disk counters require Windows.");
        cancellation.ThrowIfCancellationRequested();
        try
        {
            if (query == null)
            {
                Require(PdhOpenQuery(null, UIntPtr.Zero, out var opened)); query = opened;
                for (int i = 0; i < Paths.Length; i++)
                    Require(PdhAddEnglishCounter(query, @"\PhysicalDisk(*)\" + Paths[i], UIntPtr.Zero, out counters[i]));
            }
            Require(PdhCollectQueryData(query));
            var values = counters.Select(ReadValues).ToArray();
            var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
            // The numeric PhysicalDisk instance prefix is the Windows physical disk number.
            foreach (var instance in values.SelectMany(v => v.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                var number = instance.Split(' ')[0];
                if (number.Length == 0 || !number.All(char.IsAsciiDigit)) continue; // excludes _Total
                var device = new DeviceIdentity(SourceId, "PhysicalDrive" + number, DeviceKind.Disk, instance, number);
                if (devices.Any(d => d.Id == device.Id)) continue;
                devices.Add(device);
                var now = DateTimeOffset.UtcNow;
                for (int i = 0; i < Definitions.Count; i++)
                {
                    values[i].TryGetValue(instance, out var value);
                    if (i == 2) value = BusyFromIdle(value);
                    samples.Add(new(device, Definitions[i].Key, now, value));
                }
            }
            return Task.FromResult(WindowsSystemCollector.Batch(devices.AsReadOnly(), samples));
        }
        catch { query?.Dispose(); query = null; throw; }
    }

    internal static double? BusyFromIdle(double? idle) => idle is >= 0 and <= 100 ? 100 - idle : null;
    internal static double? CounterValue(uint status, double value) => status is 0 or 1 && double.IsFinite(value) && value >= 0 ? value : null;
    internal static bool IsWarmupStatus(uint status) => status is 0xC0000BBA or 0xC0000BC6 or 0x800007D5;

    private static Dictionary<string, double?> ReadValues(IntPtr counter)
    {
        var result = new Dictionary<string, double?>(StringComparer.Ordinal);
        for (int retry = 0; retry < 3; retry++)
        {
            uint bytes = 0;
            uint status = PdhGetFormattedCounterArray(counter, 0x8200, ref bytes, out _, IntPtr.Zero);
            if (IsWarmupStatus(status)) return result; // Keep the query alive for its second rate observation.
            if (status != 0x800007D2) Require(status);
            if (bytes == 0) return result;
            if (bytes > 4 * 1024 * 1024) throw new IOException("Physical disk counter response exceeds limit.");
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                status = PdhGetFormattedCounterArray(counter, 0x8200, ref bytes, out uint count, buffer);
                if (IsWarmupStatus(status)) return result;
                if (status == 0x800007D2) continue; // hotplug changed the required size
                Require(status);
                int size = Marshal.SizeOf<CounterItem>();
                if ((long)count * size > bytes) throw new IOException("Invalid physical disk counter response.");
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * size));
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (name != null) result[name] = CounterValue(item.Value.Status, item.Value.Value);
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("Physical disk inventory changed repeatedly during collection.");
    }
    private static void Require(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status), $"Physical disk counters unavailable (0x{status:X8}).");
    }
    public void Dispose() { disposed = true; query?.Dispose(); query = null; }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public IntPtr Name; public FormattedValue Value; }
    private sealed class SafeQuery : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeQuery() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, UIntPtr user, out SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(SafeQuery query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(SafeQuery query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
