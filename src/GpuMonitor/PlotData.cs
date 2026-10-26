using System.Globalization;
using System.IO;

namespace GpuMonitor;

/// <summary>The same measured window is used for rendering, inspection and export.</summary>
public sealed record PlotExportRow(TelemetrySample Sample, string Unit);

public static class PlotData
{
    /// <summary>Maps pointer fraction using the explicit view window, even before native chart initialization.</summary>
    public static DateTimeOffset? CursorTime(double fraction, DateTimeOffset start, DateTimeOffset end)
    {
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1 || end <= start) return null;
        return start.AddTicks((long)((end - start).Ticks * fraction));
    }

    public static IReadOnlyList<TelemetrySample> Window(IReadOnlyList<TelemetrySample> samples,
        DateTimeOffset now, int minutes)
    {
        if (minutes <= 0) throw new ArgumentOutOfRangeException(nameof(minutes));
        var start = now.AddMinutes(-minutes);
        return samples.Where(s => s.Time >= start && s.Time <= now).ToArray();
    }

    public static bool HasMeasurements(IEnumerable<TelemetrySample> samples) => samples.Any(s => s.Value.HasValue);

    public static TelemetrySample? Nearest(IReadOnlyList<TelemetrySample> samples, DateTimeOffset time) =>
        samples.Count == 0 ? null : samples.MinBy(s => Math.Abs((s.Time - time).Ticks));

    public static void WriteCsv(TextWriter writer, IEnumerable<TelemetrySample> samples, string unit)
        => WriteCsv(writer, samples.Select(s => new PlotExportRow(s, unit)));

    public static void WriteCsv(TextWriter writer, IEnumerable<PlotExportRow> rows)
    {
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        writer.WriteLine("timestamp_utc,source,device_id,device_name,metric,unit,value");
        foreach (var row in rows)
        {
            var s = row.Sample;
            writer.WriteLine(string.Join(',', new[] { s.Time.ToString("O"), s.Device.Source, s.Device.Id,
                s.Device.Name, s.MetricKey, row.Unit, s.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "" }.Select(Csv)));
        }
    }
}
