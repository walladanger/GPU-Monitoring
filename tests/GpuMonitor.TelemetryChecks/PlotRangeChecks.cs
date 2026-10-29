using GpuMonitor;

internal static class PlotRangeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var end = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        var bounds = new PlotRange(end.AddMinutes(-30), end);
        var range = new PlotRange(end.AddMinutes(-5), end);
        var zoom = range.Zoom(.5, .5, bounds);
        check(zoom.Start == end.AddSeconds(-225) && zoom.End == end.AddSeconds(-75), "Zoom retains the pointer anchor and halves the visible time span");
        var pan = zoom.Pan(-1, bounds);
        check(pan.Start == end.AddSeconds(-375) && pan.End == end.AddSeconds(-225), "Panning shifts the complete measured view without changing its span");
        check(range.Pan(1000, bounds) == range && zoom.Zoom(10000, .5, bounds) == bounds,
            "Pan and zoom cannot escape the retained thirty-minute time domain");
        check(range.Zoom(.000001, 1, bounds).End == end && range.Zoom(.000001, 1, bounds).Duration == TimeSpan.FromSeconds(10),
            "Zoom at the right edge clamps to a usable ten-second minimum");
        bool rejected = false;
        try { range.Zoom(double.NaN, .5, bounds); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Nonfinite zoom input cannot corrupt chart view limits");
        rejected = false;
        try { range.Pan(double.PositiveInfinity, bounds); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Nonfinite pan input cannot corrupt chart view limits");
    }
}
