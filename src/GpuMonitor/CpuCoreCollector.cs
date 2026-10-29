using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GpuMonitor;

/// <summary>Optional read-only logical CPU busy time, with independent PDH health and lifetime.</summary>
public sealed class CpuCoreCollector : ITelemetryProvider, ITelemetryHistoryPolicy, IDisposable
{
    public string SourceId => "windows-cpu-cores";
    public int HistoryCapacity => 900;
    public TimeSpan HistoryRetention => TimeSpan.FromMinutes(30);
    public IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(new MetricDefinition[] {
        new("windows-cpu-cores", "cpu.core.usage", "Logical processor busy time", "%", 100) });
    private SafeQuery? query;
    private IntPtr counter;
    private bool disposed;

    public Task<TelemetryBatch> ReadAsync(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Logical CPU counters require Windows.");
        try
        {
            if (query == null)
            {
                Require(PdhOpenQuery(null, UIntPtr.Zero, out var opened)); query = opened;
                Require(PdhAddEnglishCounter(query, @"\Processor Information(*)\% Processor Time", UIntPtr.Zero, out counter));
            }
            Require(PdhCollectQueryData(query));
            var values = ReadValues();
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(Normalize(values, DateTimeOffset.UtcNow));
        }
        catch { query?.Dispose(); query = null; throw; }
    }

    internal static TelemetryBatch Normalize(IReadOnlyDictionary<string, double?> values, DateTimeOffset time)
    {
        if (values.Count > 8192) throw new IOException("Logical CPU counter inventory exceeds limit.");
        var devices = new List<DeviceIdentity>(); var samples = new List<TelemetrySample>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            // Processor Information identifies a logical processor by group,index; totals are not processors.
            var parts = entry.Key.Split(',');
            if (parts.Length != 2 || !ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var group) ||
                group == ushort.MaxValue || !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= 64) continue;
            string id = $"group-{group}-cpu-{index}";
            if (!identities.Add(id)) throw new ArgumentException("Duplicate logical processor identity.", nameof(values));
            if (devices.Count == 4096) throw new IOException("Logical processor count exceeds limit.");
            var device = new DeviceIdentity("windows-cpu-cores", id, DeviceKind.Cpu,
                $"CPU group {group}, logical processor {index}", $"{group},{index}");
            devices.Add(device);
            double? value = entry.Value is >= 0 and <= 100 ? entry.Value : null;
            samples.Add(new(device, "cpu.core.usage", time, value));
        }
        return WindowsSystemCollector.Batch(devices.AsReadOnly(), samples);
    }

    private Dictionary<string, double?> ReadValues()
    {
        for (int retry = 0; retry < 3; retry++)
        {
            uint bytes = 0;
            uint status = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out _, IntPtr.Zero);
            if (DiskCollector.IsWarmupStatus(status)) return []; // Rate counters need two observations.
            if (status != 0x800007D2) Require(status);
            if (bytes == 0) return [];
            if (bytes > 4 * 1024 * 1024) throw new IOException("Logical CPU counter response exceeds limit.");
            uint allocated = bytes;
            var buffer = Marshal.AllocHGlobal((int)allocated);
            try
            {
                status = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, out uint count, buffer);
                if (DiskCollector.IsWarmupStatus(status)) return [];
                if (status == 0x800007D2) continue; // Re-probe size after a topology change.
                Require(status);
                int size = Marshal.SizeOf<CounterItem>();
                if (bytes > allocated || count > 8192 || (long)count * size > bytes)
                    throw new IOException("Invalid logical CPU counter response.");
                var result = new Dictionary<string, double?>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * size));
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (name != null && !result.TryAdd(name, DiskCollector.CounterValue(item.Value.Status, item.Value.Value)))
                        throw new IOException("Duplicate logical CPU counter instance.");
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("Logical CPU inventory changed repeatedly during collection.");
    }

    private static void Require(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status), $"Logical CPU counters unavailable (0x{status:X8}).");
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
