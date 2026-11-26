using System.Windows;

namespace GpuMonitor;

/// <summary>One dashboard's loaded charts, including its owned pop-out windows.
/// Only user interaction is broadcast; rendering and collection never feed back.</summary>
internal sealed class ChartSynchronization
{
    private readonly HashSet<TelemetryPlot> plots = [];
    private bool applying;
    public bool Enabled { get; set; } = true;
    internal event Action<DateTimeOffset?>? CursorChanged;
    internal event Action<PlotRange?>? RangeChanged;
    internal int Count => plots.Count;

    public void Add(TelemetryPlot plot)
    {
        if (!plots.Add(plot)) return;
        plot.CursorMoved += Cursor;
        plot.NavigationChanged += Navigation;
    }
    public void Remove(TelemetryPlot plot)
    {
        if (!plots.Remove(plot)) return;
        plot.CursorMoved -= Cursor;
        plot.NavigationChanged -= Navigation;
    }
    private void Cursor(TelemetryPlot origin, DateTimeOffset? time)
    {
        if (!Enabled || applying) return;
        applying = true;
        try { foreach (var plot in plots) if (plot != origin) plot.SetCursor(time); CursorChanged?.Invoke(time); }
        finally { applying = false; }
    }
    private void Navigation(TelemetryPlot origin, PlotRange? range)
    {
        if (!Enabled || applying) return;
        applying = true;
        try { foreach (var plot in plots) if (plot != origin) plot.ApplySynchronizedRange(range); RangeChanged?.Invoke(range); }
        finally { applying = false; }
    }

    internal void Inspect(DateTimeOffset? time)
    {
        if (!Enabled || applying) return;
        applying = true;
        try { foreach (var plot in plots) plot.SetCursor(time); CursorChanged?.Invoke(time); }
        finally { applying = false; }
    }

    internal static ChartSynchronization? For(DependencyObject element)
    {
        for (var window = Window.GetWindow(element); window != null; window = window.Owner)
            if (window is OverviewWindow overview) return overview.ChartSynchronization;
        return null;
    }
}
