namespace GpuMonitor;

/// <summary>Owns independent collection and provider lifetime; rendering only reads snapshots.</summary>
public sealed class DashboardTelemetry : IAsyncDisposable
{
    private readonly ITelemetryProvider[] providers;
    private readonly TelemetryEngine engine;
    private readonly SemaphoreSlim transitions = new(1, 1);
    private CancellationTokenSource? stop;
    private Task? polling;
    private TimeSpan interval = TimeSpan.FromSeconds(2);
    private bool disposed;
    private HistoryRecorder? history;
    private HistoryRecordingStatus historyStatus = new(false, false, null, 0, 0);
    public HistoryRecordingStatus HistoryStatus => Volatile.Read(ref history)?.Status ?? Volatile.Read(ref historyStatus);

    /// <summary>Transfers provider ownership on successful construction.</summary>
    public DashboardTelemetry(ITelemetryProvider[] providers)
    { this.providers = providers.ToArray(); engine = new(this.providers, (batch, definitions) => Volatile.Read(ref history)?.Record(batch, definitions)); }

    public static DashboardTelemetry Create(CollectorConfiguration? configuration = null)
    {
        var set = BackendProviderSet.Create(configuration);
        try { return new(set.Providers.ToArray()); } // Controller now owns the providers.
        catch { set.Dispose(); throw; }
    }

    public IReadOnlyList<ProviderSnapshot> Snapshot(string? sourceId = null) => engine.Snapshot(sourceId);

    public async Task ConfigureHistoryAsync(HistoryOptions? options)
    {
        await transitions.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var prior = Interlocked.Exchange(ref history, null);
            if (prior != null) await prior.DisposeAsync();
            historyStatus = new(options != null, false, null, 0, 0);
            if (options == null) return;
            var recorder = new HistoryRecorder(options);
            try { await recorder.Ready; Volatile.Write(ref history, recorder); }
            catch (Exception error) { await recorder.DisposeAsync(); historyStatus = new(true, false, error.Message, 0, 0); }
        }
        finally { transitions.Release(); }
    }

    public async Task SetMonitoringAsync(bool enabled, TimeSpan refreshInterval)
    {
        if (refreshInterval < TimeSpan.FromMilliseconds(100) || refreshInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        await transitions.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (enabled && polling is { IsCompleted: false } && interval == refreshInterval) return;
            await StopCoreAsync();
            interval = refreshInterval;
            if (enabled) StartCore();
        }
        finally { transitions.Release(); }
    }

    public async Task RefreshAsync()
    {
        await transitions.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool resume = polling != null;
            await StopCoreAsync();
            try { await engine.SampleOnceAsync(CancellationToken.None); }
            finally { if (resume) StartCore(); }
        }
        finally { transitions.Release(); }
    }

    private void StartCore()
    { stop = new(); polling = engine.RunAsync(interval, stop.Token); }

    private async Task StopCoreAsync()
    {
        if (polling == null) return;
        stop!.Cancel();
        try { await polling; }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { polling = null; stop.Dispose(); stop = null; }
    }

    public async ValueTask DisposeAsync()
    {
        await transitions.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            try { await StopCoreAsync(); }
            finally
            {
                try { var recorder = Interlocked.Exchange(ref history, null); if (recorder != null) await recorder.DisposeAsync(); }
                finally { foreach (var provider in providers.OfType<IDisposable>()) provider.Dispose(); }
            }
        }
        finally { transitions.Release(); }
    }
}
