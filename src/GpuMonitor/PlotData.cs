using System.Globalization;
using System.IO;

namespace GpuMonitor;

/// <summary>The same measured window is used for rendering, inspection and export.</summary>
public static class PlotData
{
    public static IReadOnlyList<TelemetrySample> Window(IReadOnlyList<TelemetrySample> samples,
        DateTimeOffset now, int minutes)
    {
        if (minutes <= 0) throw new ArgumentOutOfRangeException(nameof(minutes));
        var start = now.AddMinutes(-minutes);
        return samples.Where(s => s.Time >= start && s.Time <= now).ToArray();
    }

    public static TelemetrySample? Nearest(IReadOnlyList<TelemetrySample> samples, DateTimeOffset time) =>
        samples.Count == 0 ? null : samples.MinBy(s => Math.Abs((s.Time - time).Ticks));

    public static void WriteCsv(TextWriter writer, IEnumerable<TelemetrySample> samples, string unit)
    {
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        writer.WriteLine("timestamp_utc,source,device_id,device_name,metric,unit,value");
        foreach (var s in samples)
            writer.WriteLine(string.Join(',', new[] { s.Time.ToString("O"), s.Device.Source, s.Device.Id,
                s.Device.Name, s.MetricKey, unit, s.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "" }.Select(Csv)));
    }
}
