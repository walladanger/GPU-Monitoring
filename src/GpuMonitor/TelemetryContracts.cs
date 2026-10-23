namespace GpuMonitor;

public enum DeviceKind { Gpu, Cpu, Memory, Disk, Network, Process, Runtime }
public enum MetricAvailability { Available, Unavailable }

/// <summary>Provider-scoped stable identity; display indices must never be used as keys.</summary>
public sealed record DeviceIdentity(string Source, string Id, DeviceKind Kind, string Name,
    string DisplayIndex, string? Bus = null);

public sealed record MetricDefinition(string Source, string Key, string Title, string Unit, double? Scale = null);
public sealed record MetricCapability(DeviceIdentity Device, string MetricKey, MetricAvailability Availability);

/// <summary>A measured value or an explicit gap, retaining the source timestamp.</summary>
public sealed record TelemetrySample
{
    public DeviceIdentity Device { get; }
    public string MetricKey { get; }
    public DateTimeOffset Time { get; }
    public double? Value { get; }
    public MetricAvailability Availability => Value.HasValue ? MetricAvailability.Available : MetricAvailability.Unavailable;

    public TelemetrySample(DeviceIdentity device, string metricKey, DateTimeOffset time, double? value)
    {
        if (value.HasValue && !double.IsFinite(value.Value)) throw new ArgumentOutOfRangeException(nameof(value), "Samples must be finite or absent.");
        Device = device; MetricKey = metricKey; Time = time.ToUniversalTime(); Value = value;
    }
}

/// <summary>Metadata-only discrete activity. No prompt, message or tool payload field.</summary>
public sealed record EventMeasurement(string MetricKey, string Unit, double Value);
public sealed record TelemetryEvent(string Source, string Id, DateTimeOffset Time, string Kind,
    string? DeviceId = null, string? CorrelationId = null, TimeSpan? Duration = null,
    bool TimestampEstimated = false, string? ParentId = null, string? Name = null,
    IReadOnlyList<EventMeasurement>? Measurements = null, string? ActivityId = null);

public sealed record TelemetryBatch(IReadOnlyList<DeviceIdentity> Devices,
    IReadOnlyList<MetricCapability> Capabilities, IReadOnlyList<TelemetrySample> Samples,
    IReadOnlyList<TelemetryEvent> Events);

/// <summary>Read-only snapshot boundary. Failures throw; a failure is never an empty inventory.</summary>
public interface ITelemetryProvider
{
    string SourceId { get; }
    IReadOnlyList<MetricDefinition> Definitions { get; }
    Task<TelemetryBatch> ReadAsync(CancellationToken cancellation);
}

/// <summary>Maps existing NVIDIA values without conversions, interpolation or extra devices.</summary>
public static class NvidiaTelemetry
{
    public const string SourceId = "nvidia-smi";
    public static IReadOnlyList<MetricDefinition> Definitions { get; } = Array.AsReadOnly(
        Metrics.All.Select(m => new MetricDefinition(SourceId, m.Key, m.Title, m.Unit, m.Scale))
            .Append(new(SourceId, "memory.total", "VRAM total", "MiB")).ToArray());

    public static TelemetryBatch Normalize(IReadOnlyList<GpuReading> readings)
    {
        var devices = new List<DeviceIdentity>();
        var capabilities = new List<MetricCapability>();
        var samples = new List<TelemetrySample>();
        foreach (var reading in readings)
        {
            var device = new DeviceIdentity(SourceId, reading.Id, DeviceKind.Gpu, reading.Name, reading.Index, reading.Bus);
            devices.Add(device);
            foreach (var metric in Definitions)
            {
                reading.Metrics.TryGetValue(metric.Key, out var value);
                var sample = new TelemetrySample(device, metric.Key, reading.Time, value);
                samples.Add(sample);
                capabilities.Add(new(device, metric.Key, sample.Availability));
            }
        }
        if (devices.Select(d => d.Id).Distinct().Count() != devices.Count)
            throw new ArgumentException("Duplicate NVIDIA device identity.", nameof(readings));
        return new(devices.AsReadOnly(), capabilities.AsReadOnly(), samples.AsReadOnly(), Array.Empty<TelemetryEvent>());
    }
}

/// <summary>Single-series circular history. Access from the owning collection/UI thread.</summary>
public sealed class TelemetrySeriesBuffer
{
    private readonly TelemetrySample?[] items;
    private readonly TimeSpan retention;
    private int head;
    private int count;
    private DateTimeOffset? lastAcceptedTime;
    private (string Source, string Device, string Metric)? identity;
    public int Count => count;

    public TelemetrySeriesBuffer(int capacity = 3600, TimeSpan? retention = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.retention = retention ?? TimeSpan.FromMinutes(30);
        if (this.retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        items = new TelemetrySample[capacity];
    }

    // Reject late/duplicate timestamps so reconnects cannot silently corrupt chart order.
    public bool Append(TelemetrySample sample)
    {
        var key = (sample.Device.Source, sample.Device.Id, sample.MetricKey);
        if (identity.HasValue && identity.Value != key) throw new ArgumentException("Buffer belongs to a different series.", nameof(sample));
        if (lastAcceptedTime.HasValue && sample.Time <= lastAcceptedTime.Value) return false;
        identity = key;
        lastAcceptedTime = sample.Time;
        if (count == items.Length) RemoveOldest();
        items[(head + count) % items.Length] = sample;
        count++;
        Prune(sample.Time);
        return true;
    }

    /// <summary>Also expires samples when collection stops. Does not synthesize zeroes.</summary>
    public void Prune(DateTimeOffset now)
    {
        while (count > 0 && now - items[head]!.Time > retention) RemoveOldest();
    }

    public IReadOnlyList<TelemetrySample> Snapshot() =>
        Array.AsReadOnly(Enumerable.Range(0, count).Select(i => items[(head + i) % items.Length]!).ToArray());

    private void RemoveOldest()
    {
        items[head] = null;
        head = (head + 1) % items.Length;
        count--;
    }
}
