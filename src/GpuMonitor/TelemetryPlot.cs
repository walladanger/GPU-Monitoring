using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GpuMonitor;

/// <summary>Native chart view; the owning session retains the source samples.</summary>
public sealed class TelemetryPlot : Grid
{
    private readonly CartesianChart chart;
    private readonly Axis timeAxis;
    private readonly Axis valueAxis;
    private readonly List<LineSeries<ObservablePoint>> series = [];
    private readonly Line cursor = new() { Stroke = Brushes.LightGray, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock inspection = new() { Foreground = Brushes.White, FontSize = 11, Margin = new(58, 4, 16, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private IReadOnlyList<IReadOnlyList<TelemetrySample>> visible = Array.Empty<IReadOnlyList<TelemetrySample>>();
    private readonly string unit;
    private double divisor = 1;
    public event Action<DateTimeOffset?>? CursorMoved;

    public TelemetryPlot(string unit, double? maximum = null)
    {
        this.unit = unit;
        timeAxis = new Axis
        {
            TextSize = 11, LabelsPaint = new SolidColorPaint(SKColor.Parse("#A5AEB7")),
            SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#26313D"), 1),
            Labeler = value => DateTimeOffset.FromUnixTimeMilliseconds((long)value).ToLocalTime().ToString("HH:mm:ss"),
            UnitWidth = 1000, MinStep = 1000
        };
        valueAxis = new Axis
        {
            MinLimit = 0, MaxLimit = maximum, TextSize = 11,
            LabelsPaint = new SolidColorPaint(SKColor.Parse("#A5AEB7")),
            SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#26313D"), 1),
            Labeler = value => $"{value:0.#}{unit}"
        };
        chart = new CartesianChart
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 30, 39)),
            XAxes = new[] { timeAxis }, YAxes = new[] { valueAxis }, Series = series,
            AnimationsSpeed = TimeSpan.Zero, LegendPosition = LegendPosition.Hidden,
            TooltipPosition = TooltipPosition.Hidden,
            DrawMargin = new Margin(52, 12, 14, 30)
        };
        Children.Add(chart);
        var overlay = new Canvas { IsHitTestVisible = false }; overlay.Children.Add(cursor); Children.Add(overlay);
        Children.Add(inspection);
        chart.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(chart);
            if (p.X < 52 || p.X > ActualWidth - 14 || p.Y < 12 || p.Y > ActualHeight - 30 ||
                timeAxis.MinLimit == null || timeAxis.MaxLimit == null) { CursorMoved?.Invoke(null); return; }
            // The draw margin and explicit limits are already known before LiveCharts
            // initializes its internal scaler (mouse input can arrive during first render).
            CursorMoved?.Invoke(PlotData.CursorTime((p.X - 52) / (ActualWidth - 66),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MinLimit.Value),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MaxLimit.Value)));
        };
        chart.MouseLeave += (_, _) => CursorMoved?.Invoke(null);
    }

    public void SetSeries(IReadOnlyList<(string Name, string Color)> definitions)
    {
        series.Clear();
        foreach (var definition in definitions)
        {
            var color = SKColor.Parse(definition.Color);
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = definition.Name, Values = Array.Empty<ObservablePoint>(),
                GeometrySize = 0, LineSmoothness = 0,
                Stroke = new SolidColorPaint(color, 1.6f),
                Fill = definitions.Count == 1 ? new SolidColorPaint(color.WithAlpha(24)) : null
            });
        }
        chart.Series = series.ToArray();
    }

    public void Update(IReadOnlyList<IReadOnlyList<TelemetrySample>> histories,
        DateTimeOffset now, int minutes, double divisor = 1)
    {
        if (histories.Count != series.Count) throw new ArgumentException("Series count does not match histories.");
        if (!double.IsFinite(divisor) || divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var start = now.AddMinutes(-minutes);
        this.divisor = divisor;
        visible = histories.Select(h => PlotData.Window(h, now, minutes)).ToArray();
        timeAxis.MinLimit = start.ToUnixTimeMilliseconds();
        timeAxis.MaxLimit = now.ToUnixTimeMilliseconds();
        for (int i = 0; i < series.Count; i++)
            series[i].Values = visible[i]
                .Select(s => new ObservablePoint(s.Time.ToUnixTimeMilliseconds(), s.Value / divisor)).ToArray();
    }

    public void SetCursor(DateTimeOffset? time)
    {
        inspection.Text = ""; cursor.Visibility = Visibility.Collapsed;
        if (time == null || timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        double fraction = (time.Value.ToUnixTimeMilliseconds() - timeAxis.MinLimit.Value) /
            (timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value);
        if (fraction < 0 || fraction > 1) return;
        cursor.X1 = cursor.X2 = 52 + fraction * Math.Max(0, ActualWidth - 66);
        cursor.Y1 = 12; cursor.Y2 = Math.Max(12, ActualHeight - 30); cursor.Visibility = Visibility.Visible;
        inspection.Text = string.Join("\n", visible.Select((samples, i) =>
        {
            var sample = PlotData.Nearest(samples, time.Value);
            return sample == null ? "" : $"{series[i].Name} · {sample.Time.ToLocalTime():HH:mm:ss.fff} · " +
                (sample.Value is double value ? $"{value / divisor:R} {unit}" : "N/A");
        }));
    }

    public IReadOnlyList<TelemetrySample> VisibleSamples => visible.SelectMany(s => s).OrderBy(s => s.Time).ToArray();

    public int SeriesCount => series.Count;
    public void SetMaximum(double? maximum) => valueAxis.MaxLimit = maximum;
    public void SetName(int index, string name) => series[index].Name = name;
}
