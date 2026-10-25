using System.Threading.Channels;

namespace GpuMonitor;

public sealed record HistoryRecordingStatus(bool Requested, bool Ready, string? Error, long Samples, long DroppedBatches);

/// <summary>One background writer, sixteen bounded batches. Collection never waits for SQLite.</summary>
public sealed class HistoryRecorder : IAsyncDisposable
{
    private readonly Channel<(TelemetrySample[] Samples, MetricDefinition[] Definitions)> queue = Channel.CreateBounded<(TelemetrySample[], MetricDefinition[])>(
        new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task worker;
    private string? error;
    private long samples, dropped;
    private int stopping;
    public Task Ready => ready.Task;
    public HistoryRecordingStatus Status => new(true, ready.Task.IsCompletedSuccessfully && error == null, Volatile.Read(ref error), Interlocked.Read(ref samples), Interlocked.Read(ref dropped));

    public HistoryRecorder(HistoryOptions options) => worker = Task.Run(async () =>
    {
        try
        {
            using var store = new HistoryStore(options);
            Interlocked.Exchange(ref samples, store.Count); ready.TrySetResult();
            await foreach (var batch in queue.Reader.ReadAllAsync())
            {
                store.Append(batch.Samples, batch.Definitions, DateTimeOffset.UtcNow);
                Interlocked.Exchange(ref samples, store.Count);
            }
        }
        catch (Exception failure)
        {
            Volatile.Write(ref error, failure.Message); ready.TrySetException(failure);
            Interlocked.Increment(ref dropped); queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out _)) Interlocked.Increment(ref dropped);
        }
    });

    public void Record(TelemetryBatch batch, IReadOnlyList<MetricDefinition> definitions)
    {
        if (Volatile.Read(ref stopping) != 0) return;
        if (batch.Samples.Count > 8192 || definitions.Count > 8192)
        { Interlocked.Increment(ref dropped); Volatile.Write(ref error, "History batch exceeded 8192 measurements/definitions."); return; }
        if (!queue.Writer.TryWrite((batch.Samples.ToArray(), definitions.ToArray()))) Interlocked.Increment(ref dropped);
    }
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref stopping, 1); queue.Writer.TryComplete(); await worker;
    }
}
