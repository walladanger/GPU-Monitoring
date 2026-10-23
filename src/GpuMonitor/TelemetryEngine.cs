using System.Collections.ObjectModel;

namespace GpuMonitor;

public sealed record ProviderSnapshot(string Source, ProviderHealth Health, string? Error,
    DateTimeOffset? LastSuccess, TelemetryBatch? Current, IReadOnlyList<TelemetrySample> History,
    IReadOnlyList<MetricDefinition> Definitions);

/// <summary>Dashboard-independent polling. Each source has its own loop, lock and failure state.</summary>
public sealed class TelemetryEngine
{
    private sealed class Source(ITelemetryProvider provider)
    {
        public readonly object Gate = new();
        public readonly TelemetrySession Session = new(provider);
    }

    private readonly Dictionary<string, Source> sources;
    private int running;

    public TelemetryEngine(IEnumerable<ITelemetryProvider> providers)
    {
        sources = providers.ToDictionary(p => p.SourceId, p => new Source(p), StringComparer.Ordinal);
        if (sources.Count == 0) throw new ArgumentException("At least one provider is required.", nameof(providers));
    }

    /// <summary>One owner runs collection; readers may snapshot from any thread. Cancellation stops all loops.</summary>
    public async Task RunAsync(TimeSpan interval, CancellationToken cancellation)
    {
        if (interval < TimeSpan.FromMilliseconds(100) || interval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Collection is already running.");
        try { await Task.WhenAll(sources.Values.Select(source => Task.Run(() => PollAsync(source, interval, cancellation), cancellation))); }
        finally { Volatile.Write(ref running, 0); }
    }

    private static async Task PollAsync(Source source, TimeSpan interval, CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                // Provider I/O never holds the read lock or blocks another source's loop.
                var batch = await source.Session.ReadAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                lock (source.Gate) source.Session.Apply(batch, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                lock (source.Gate) source.Session.MarkStale(error.Message, DateTimeOffset.UtcNow);
            }
        } while (await timer.WaitForNextTickAsync(cancellation));
    }

    public IReadOnlyList<ProviderSnapshot> Snapshot()
    {
        var result = new List<ProviderSnapshot>();
        foreach (var pair in sources)
        {
            lock (pair.Value.Gate)
            {
                var session = pair.Value.Session;
                session.Prune(DateTimeOffset.UtcNow);
                var current = session.Current;
                result.Add(new(pair.Key, session.Health, session.Error, session.LastSuccess, current,
                    session.HistorySnapshot(), Array.AsReadOnly(session.Definitions.ToArray())));
            }
        }
        return new ReadOnlyCollection<ProviderSnapshot>(result);
    }
}
