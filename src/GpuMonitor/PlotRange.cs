namespace GpuMonitor;

/// <summary>Time-only navigation; raw measurements are never interpolated or rewritten.</summary>
public sealed record PlotRange(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
    public PlotRange Zoom(double factor, double anchor, PlotRange bounds)
    {
        Validate(bounds);
        if (!double.IsFinite(factor) || factor <= 0 || !double.IsFinite(anchor) || anchor is < 0 or > 1)
            throw new ArgumentException("Invalid zoom input.");
        double length = Math.Clamp(Duration.Ticks * factor, TimeSpan.FromSeconds(10).Ticks, bounds.Duration.Ticks);
        return Fit((Start - bounds.Start).Ticks + anchor * (Duration.Ticks - length), length, bounds);
    }
    public PlotRange Pan(double fraction, PlotRange bounds)
    {
        Validate(bounds);
        if (!double.IsFinite(fraction)) throw new ArgumentException("Invalid pan input.");
        double length = Math.Min(Duration.Ticks, bounds.Duration.Ticks);
        return Fit((Start - bounds.Start).Ticks + length * fraction, length, bounds);
    }
    private void Validate(PlotRange bounds)
    {
        if (End <= Start || bounds.Duration < TimeSpan.FromSeconds(10)) throw new ArgumentException("Invalid time bounds.");
    }
    private static PlotRange Fit(double offset, double length, PlotRange bounds)
    {
        var start = bounds.Start.AddTicks((long)Math.Clamp(offset, 0, bounds.Duration.Ticks - length));
        return new(start, start.AddTicks((long)length));
    }
}
