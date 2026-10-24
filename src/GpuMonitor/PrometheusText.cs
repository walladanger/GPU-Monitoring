using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GpuMonitor;

public sealed record PrometheusPoint(string Name, IReadOnlyDictionary<string, string> Labels, double? Value, long? TimestampMilliseconds);

/// <summary>Bounded Prometheus text/OpenMetrics sample reader. Exemplars are ignored, not retained.</summary>
public static class PrometheusText
{
    private static readonly Regex Line = new("""^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?:\{(?<labels>(?:[^"{}]|"(?:[^"\\]|\\.)*")*)\})?\s+(?<value>[^\s]+)(?:\s+(?<time>-?\d+))?(?:\s+#.*)?$""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Label = new("\\G\\s*(?<key>[a-zA-Z_][a-zA-Z0-9_]*)\\s*=\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"\\s*(?:,|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static IReadOnlyList<PrometheusPoint> Parse(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new IOException("Metrics response exceeds 4 MiB.");
        var points = new List<PrometheusPoint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Length > 16384 || points.Count >= 20000) throw new FormatException("Metrics sample limit exceeded.");
            var match = Line.Match(line);
            if (!match.Success) throw new FormatException("Malformed Prometheus sample.");
            string name = match.Groups["name"].Value;
            var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var labelText = match.Groups["labels"].Value;
            int offset = 0;
            while (offset < labelText.Length)
            {
                if (string.IsNullOrWhiteSpace(labelText[offset..])) break;
                var label = Label.Match(labelText, offset);
                if (!label.Success || label.Index != offset || labels.Count >= 32) throw new FormatException("Malformed metric labels.");
                string value = Unescape(label.Groups["value"].Value);
                if (!labels.TryAdd(label.Groups["key"].Value, value)) throw new FormatException("Duplicate metric label.");
                offset += label.Length;
            }
            var number = match.Groups["value"].Value;
            double? valueNumber = null;
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var numberValue))
                valueNumber = double.IsFinite(numberValue) ? numberValue : null;
            else if (number is not ("+Inf" or "-Inf" or "Inf" or "NaN")) throw new FormatException("Malformed metric value.");
            long? timestamp = match.Groups["time"].Success ? long.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture) : null;
            if (!seen.Add(name + "\0" + Canonical(labels))) throw new FormatException("Duplicate metric series.");
            points.Add(new(name, labels, valueNumber, timestamp));
        }
        return points.AsReadOnly();
    }

    internal static string Canonical(IEnumerable<KeyValuePair<string, string>> labels) => string.Concat(
        labels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key.Length + ":" + p.Key + p.Value.Length + ":" + p.Value));

    private static string Unescape(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { result.Append(value[i]); continue; }
            if (++i == value.Length) throw new FormatException("Incomplete metric escape.");
            result.Append(value[i] switch { 'n' => '\n', '\\' => '\\', '"' => '"', _ => throw new FormatException("Invalid metric escape.") });
        }
        return result.ToString();
    }
}

/// <summary>Rates of cumulative counters. Duplicate/replayed observations cannot reset the baseline.</summary>
public sealed class CumulativeRate(double ticksPerSecond)
{
    private (double Value, long Tick, string Epoch)? previous;
    public CumulativeRate Clone() => new(ticksPerSecond) { previous = previous };
    public double? Observe(double? value, long tick, string epoch = "")
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value < 0) { previous = null; return null; }
        var old = previous;
        if (old.HasValue && tick <= old.Value.Tick && epoch == old.Value.Epoch) return null;
        previous = (value.Value, tick, epoch);
        if (old == null || epoch != old.Value.Epoch || value < old.Value.Value || tick <= old.Value.Tick) return null;
        // Subtract as integers before converting: epoch ticks exceed double's exact
        // integer range, so converting each timestamp first loses sub-interval precision.
        ulong elapsedTicks = unchecked((ulong)tick - (ulong)old.Value.Tick);
        double rate = (value.Value - old.Value.Value) / elapsedTicks * ticksPerSecond;
        return double.IsFinite(rate) && rate >= 0 ? rate : null;
    }
}
