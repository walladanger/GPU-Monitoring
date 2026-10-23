namespace GpuMonitor;

public enum ProviderHealth { Starting, Live, Stale }

/// <summary>One provider's live state and bounded chart histories, owned by the polling/UI thread.</summary>
public sealed class TelemetrySession(ITelemetryProvider provider)
{
    private readonly Dictionary<(string Device, string Metric), TelemetrySeriesBuffer> histories = [];
    public ProviderHealth Health { get; private set; } = ProviderHealth.Starting;
    public TelemetryBatch? Current { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<MetricDefinition> Definitions => provider.Definitions;

    internal Task<TelemetryBatch> ReadAsync(CancellationToken cancellation) => provider.ReadAsync(cancellation);

    public async Task RefreshAsync(CancellationToken cancellation)
    {
        try
        {
            var batch = await provider.ReadAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            Apply(batch, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            MarkStale(error.Message, DateTimeOffset.UtcNow);
            throw;
        }
    }

    /// <summary>Accepts a complete provider snapshot. Invalid batches leave the last snapshot intact.</summary>
    public void Apply(TelemetryBatch batch, DateTimeOffset observedAt)
    {
        var devices = batch.Devices.ToDictionary(d => d.Id);
        var definitions = provider.Definitions.Select(d => d.Key).ToHashSet();
        var keys = new HashSet<(string Device, string Metric)>();
        if (batch.Devices.Any(d => d.Source != provider.SourceId))
            throw new ArgumentException("Snapshot contains a device from another provider.", nameof(batch));
        foreach (var sample in batch.Samples)
        {
            if (!devices.TryGetValue(sample.Device.Id, out var device) || sample.Device != device ||
                !definitions.Contains(sample.MetricKey) || !keys.Add((device.Id, sample.MetricKey)))
                throw new ArgumentException("Snapshot contains unknown or duplicate series.", nameof(batch));
        }
        var capabilityKeys = new HashSet<(string Device, string Metric)>();
        foreach (var capability in batch.Capabilities)
            if (!devices.TryGetValue(capability.Device.Id, out var device) || capability.Device != device ||
                !definitions.Contains(capability.MetricKey) || !capabilityKeys.Add((device.Id, capability.MetricKey)))
                throw new ArgumentException("Snapshot contains unknown or duplicate capabilities.", nameof(batch));
        if (batch.Events.Any(e => e.Source != provider.SourceId ||
            (e.DeviceId != null && !devices.ContainsKey(e.DeviceId)) || e.Duration < TimeSpan.Zero))
            throw new ArgumentException("Snapshot contains an invalid event.", nameof(batch));
        // Bound total histories to this provider's current devices and advertised metrics.
        foreach (var key in histories.Keys.Where(k => !devices.ContainsKey(k.Device)).ToArray()) histories.Remove(key);
        foreach (var sample in batch.Samples)
        {
            var key = (sample.Device.Id, sample.MetricKey);
            if (!histories.TryGetValue(key, out var history)) histories[key] = history = provider is ITelemetryHistoryPolicy policy
                ? new(policy.HistoryCapacity, policy.HistoryRetention) : new();
            history.Append(sample);
        }
        Prune(observedAt);
        // Own the collections: a provider must not mutate published snapshots after a poll.
        Current = new(Array.AsReadOnly(batch.Devices.ToArray()), Array.AsReadOnly(batch.Capabilities.ToArray()),
            Array.AsReadOnly(batch.Samples.ToArray()), Array.AsReadOnly(batch.Events.ToArray()));
        LastSuccess = observedAt.ToUniversalTime(); Error = null; Health = ProviderHealth.Live;
    }

    /// <summary>Retains inventory, inserting gaps so charts cannot join across failed polls.</summary>
    public void MarkStale(string error, DateTimeOffset observedAt)
    {
        if (Current != null)
            foreach (var sample in Current.Samples)
                if (histories.TryGetValue((sample.Device.Id, sample.MetricKey), out var history))
                    history.Append(new(sample.Device, sample.MetricKey, observedAt, null));
        Prune(observedAt);
        Health = ProviderHealth.Stale; Error = error;
    }

    public void Prune(DateTimeOffset now)
    {
        foreach (var history in histories.Values) history.Prune(now);
    }

    public IReadOnlyList<TelemetrySample> History(string deviceId, string metricKey) =>
        histories.TryGetValue((deviceId, metricKey), out var history) ? history.Snapshot() : Array.Empty<TelemetrySample>();

    public IReadOnlyList<TelemetrySample> HistorySnapshot() => Array.AsReadOnly(
        histories.Values.SelectMany(h => h.Snapshot()).ToArray());
}

/// <summary>Compatibility projection for the existing GPU cards/export during chart migration.</summary>
public static class NvidiaSnapshot
{
    public static IReadOnlyList<GpuReading> Readings(TelemetryBatch batch)
    {
        var byDevice = batch.Samples.ToLookup(s => s.Device.Id);
        return batch.Devices.Where(d => d.Source == NvidiaTelemetry.SourceId && d.Kind == DeviceKind.Gpu)
            .Select(d => new GpuReading(d.DisplayIndex, d.Name, d.Id, d.Bus ?? "",
                byDevice[d.Id].ToDictionary(s => s.MetricKey, s => s.Value),
                byDevice[d.Id].Max(s => s.Time))).ToArray();
    }
}
