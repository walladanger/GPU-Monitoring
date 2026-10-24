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
    private readonly Canvas scales = new() { IsHitTestVisible = false };
    private readonly TextBlock inspection = new() { Foreground = Brushes.White, FontSize = 11, Margin = new(35, 4, 8, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private IReadOnlyList<IReadOnlyList<TelemetrySample>> visible = Array.Empty<IReadOnlyList<TelemetrySample>>();
    private readonly string unit;
    private double divisor = 1;
    private double? fixedMaximum;
    private int windowMinutes = 5;
    public event Action<DateTimeOffset?>? CursorMoved;

    public TelemetryPlot(string unit, double? maximum = null)
    {
        this.unit = unit; fixedMaximum = maximum;
        timeAxis = new Axis
        {
            TextSize = 10, LabelsPaint = null,
            SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#213746"), .6f),
            Labeler = value => DateTimeOffset.FromUnixTimeMilliseconds((long)value).ToLocalTime().ToString("HH:mm:ss"),
            UnitWidth = 1000, MinStep = 60000, ForceStepToMin = true, SubseparatorsPaint = null
        };
        valueAxis = new Axis
        {
            MinLimit = 0, MaxLimit = maximum, TextSize = 11, ForceStepToMin = true, SubseparatorsPaint = null,
            LabelsPaint = null,
            SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#213746"), .6f),
            Labeler = value => $"{value:0.#}{unit}"
        };
        chart = new CartesianChart
        {
            Background = new SolidColorBrush(Color.FromRgb(16, 33, 45)),
            XAxes = new[] { timeAxis }, YAxes = new[] { valueAxis }, Series = series,
            AnimationsSpeed = TimeSpan.Zero, LegendPosition = LegendPosition.Hidden,
            TooltipPosition = TooltipPosition.Hidden,
            DrawMargin = new Margin(4, 8, 4, 24)
        };
        Children.Add(chart); Children.Add(scales); SizeChanged += (_, _) => RefreshScales();
        var overlay = new Canvas { IsHitTestVisible = false }; overlay.Children.Add(cursor); Children.Add(overlay);
        Children.Add(inspection);
        chart.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(chart);
            if (p.X < 4 || p.X > ActualWidth - 4 || p.Y < 8 || p.Y > ActualHeight - 24 ||
                timeAxis.MinLimit == null || timeAxis.MaxLimit == null) { CursorMoved?.Invoke(null); return; }
            // The draw margin and explicit limits are already known before LiveCharts
            // initializes its internal scaler (mouse input can arrive during first render).
            CursorMoved?.Invoke(PlotData.CursorTime((p.X - 4) / (ActualWidth - 8),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MinLimit.Value),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MaxLimit.Value)));
        };
        chart.MouseLeave += (_, _) => CursorMoved?.Invoke(null);
    }

    public void SetSeries(IReadOnlyList<(string Name, string Color)> definitions, bool? filled = null)
    {
        series.Clear();
        foreach (var definition in definitions)
        {
            var color = SKColor.Parse(definition.Color);
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = definition.Name, Values = Array.Empty<ObservablePoint>(),
                GeometrySize = 0, LineSmoothness = 0,
                Stroke = new SolidColorPaint(color, 1.4f),
                Fill = (filled ?? definitions.Count == 1) ? new LinearGradientPaint(color.WithAlpha(60), color.WithAlpha(0), new SKPoint(0, 0), new SKPoint(0, 1)) : null
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
        this.divisor = divisor; windowMinutes = minutes;
        visible = histories.Select(h => PlotData.Window(h, now, minutes)).ToArray();
        timeAxis.MinLimit = start.ToUnixTimeMilliseconds();
        timeAxis.MaxLimit = now.ToUnixTimeMilliseconds();
        timeAxis.MinStep = minutes * 60000d / (minutes == 1 ? 6 : 5);
        valueAxis.MaxLimit = fixedMaximum ?? Math.Max(1, (visible.SelectMany(s => s).Max(s => s.Value / divisor) ?? 1) * 1.1);
        valueAxis.MinStep = valueAxis.MaxLimit.Value / 2;
        for (int i = 0; i < series.Count; i++)
            series[i].Values = visible[i]
                .Select(s => new ObservablePoint(s.Time.ToUnixTimeMilliseconds(), s.Value / divisor)).ToArray();
        RefreshScales();
    }

    public void SetCursor(DateTimeOffset? time)
    {
        inspection.Text = ""; cursor.Visibility = Visibility.Collapsed;
        if (time == null || timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        double fraction = (time.Value.ToUnixTimeMilliseconds() - timeAxis.MinLimit.Value) /
            (timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value);
        if (fraction < 0 || fraction > 1) return;
        cursor.X1 = cursor.X2 = 4 + fraction * Math.Max(0, ActualWidth - 8);
        cursor.Y1 = 8; cursor.Y2 = Math.Max(8, ActualHeight - 24); cursor.Visibility = Visibility.Visible;
        inspection.Text = string.Join("\n", visible.Select((samples, i) =>
        {
            var sample = PlotData.Nearest(samples, time.Value);
            return sample == null ? "" : $"{series[i].Name} · {sample.Time.ToLocalTime():HH:mm:ss.fff} · " +
                (sample.Value is double value ? $"{value / divisor:R} {unit}" : "N/A");
        }));
    }

    private void RefreshScales()
    {
        scales.Children.Clear();
        if (ActualWidth < 60 || ActualHeight < 40) return;
        double maximum = valueAxis.MaxLimit ?? Math.Max(1, visible.SelectMany(s => s).Max(s => s.Value / divisor) ?? 1);
        for (int i = 0; i < 3; i++)
        {
            var label = OverviewTheme.Text($"{maximum * (2 - i) / 2:0.#}{unit}", 10, OverviewTheme.Muted);
            label.Opacity = .8; Canvas.SetLeft(label, 6); Canvas.SetTop(label, Math.Min(ActualHeight - 38, Math.Max(8, 3 + i * (ActualHeight - 32) / 2))); scales.Children.Add(label);
        }
        if (timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        int intervals = windowMinutes == 1 ? 6 : 5;
        for (int i = 0; i <= intervals; i++)
        {
            double fraction = (double)i / intervals;
            var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(timeAxis.MinLimit.Value + fraction * (timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value)));
            var label = OverviewTheme.Text(time.ToLocalTime().ToString(windowMinutes == 1 ? "HH:mm:ss" : "HH:mm"), 10, OverviewTheme.Muted);
            Canvas.SetLeft(label, Math.Clamp(4 + fraction * (ActualWidth - 8) - 15, 4, ActualWidth - 35));
            Canvas.SetTop(label, ActualHeight - 18); scales.Children.Add(label);
        }
    }

    public IReadOnlyList<TelemetrySample> VisibleSamples => visible.SelectMany(s => s).OrderBy(s => s.Time).ToArray();

    public int SeriesCount => series.Count;
    public void SetMaximum(double? maximum) { fixedMaximum = maximum; valueAxis.MaxLimit = maximum; RefreshScales(); }
    public void SetName(int index, string name) => series[index].Name = name;
}
