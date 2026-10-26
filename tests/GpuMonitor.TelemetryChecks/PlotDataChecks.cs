using GpuMonitor;

internal static class PlotDataChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        var d = new DeviceIdentity("nvidia-smi", "GPU-A", DeviceKind.Gpu, "NVIDIA, \"Test\"", "0");
        TelemetrySample Sample(int seconds, double? value) => new(d, "utilization.gpu", now.AddSeconds(seconds), value);
        var samples = new[] { Sample(-61, 50), Sample(-60, 30), Sample(-20, null), Sample(0, 20.125), Sample(1, 99) };
        var window = PlotData.Window(samples, now, 1);
        check(window.Count == 3 && window[0] == samples[1] && window[^1] == samples[3], "Plot window includes exact boundaries and excludes old/future samples");
        check(PlotData.Nearest(window, now.AddSeconds(-19))?.Value == null, "Inspection selects an unavailable sample rather than interpolating across a gap");
        check(PlotData.Nearest(window, now)?.Value == 20.125, "Inspection retains exact measured precision");
        check(PlotData.Nearest([], now) == null, "Empty plot has no invented inspection value");
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        PlotData.WriteCsv(writer, window, "%");
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        check(lines.Length == 4 && CsvTelemetry.Fields(lines[1])[3] == d.Name, "Plot CSV exports only visible samples and preserves quoted device names");
        check(CsvTelemetry.Fields(lines[2])[6] == "" && CsvTelemetry.Fields(lines[3])[6] == "20.125", "Plot CSV preserves gaps and exact numeric data");
    }
}
