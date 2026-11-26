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
public sealed partial class TelemetryPlot : Grid
{
    private readonly CartesianChart chart;
    internal FrameworkElement VerificationChart => chart;
    private readonly Axis timeAxis;
    private readonly Axis valueAxis;
    private readonly List<LineSeries<ObservablePoint>> series = [];
    private readonly Line cursor = new() { Stroke = Brushes.LightGray, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas scales = new() { IsHitTestVisible = false };
    private readonly TextBlock inspection = new() { Foreground = Brushes.White, FontSize = 11, Margin = new(35, 4, 8, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private IReadOnlyList<IReadOnlyList<TelemetrySample>> visible = Array.Empty<IReadOnlyList<TelemetrySample>>();
    private readonly TextBlock currentReadings = new() { Foreground = Brushes.White, FontSize = 12, Margin = new(40, 8, 12, 26), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false, ClipToBounds = true };
    internal string CurrentReadingsText => currentReadings.Text;
    private readonly string unit;
    private double divisor = 1;
    private IReadOnlyList<WorkspaceAxis>? axisDefinitions;
    private int[] axisAssignments = [];
    private Axis[] valueAxes = [];
    private double SeriesDivisor(int index) => axisDefinitions == null ? divisor : axisDefinitions.Single(a => a.Index == axisAssignments[index]).Divisor;
    private string SeriesUnit(int index) => axisDefinitions == null ? unit : axisDefinitions.Single(a => a.Index == axisAssignments[index]).DisplayUnit;

    private bool laneMode;
    private double[] laneMaxima = [];
    public void SetLaneMode(bool enabled) { laneMode = enabled; if (enabled) foreach (var line in series) line.ScalesYAt = 0; }
    private double? DisplayValue(int index, double? raw) => laneMode ? (series.Count - index - 1) * 100 + raw / SeriesDivisor(index) / laneMaxima[index] * 85 : raw / SeriesDivisor(index);
    private double? fixedMaximum;
    private int windowMinutes = 5;

    public TelemetryPlot(string unit, double? maximum = null)
    {
        OverviewTheme.InstallPopupStyles();
        this.unit = unit; fixedMaximum = maximum;
        Focusable = true; ClipToBounds = true; MinHeight = 210;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
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
        valueAxes = [valueAxis];
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
        Children.Add(currentReadings); WireSelection();
        foreach (UIElement child in Children) Grid.SetRow(child, 1);
        chart.SizeChanged += (_, _) => { RefreshScales(); RefreshSelectedPoint(); };
        chart.MouseMove += (_, e) => InspectAt(e.GetPosition(chart));
        chart.MouseLeave += (_, _) => SetCursor(null);
        WireNavigation();
    }

    internal void InspectAt(Point p)
    {
            if (p.X < 4 || p.X > chart.ActualWidth - 4 || p.Y < 8 || p.Y > chart.ActualHeight - 24 ||
                timeAxis.MinLimit == null || timeAxis.MaxLimit == null) { SetCursor(null); return; }
            // The draw margin and explicit limits are already known before LiveCharts
            // initializes its internal scaler (mouse input can arrive during first render).
            SetCursor(PlotData.CursorTime((p.X - 4) / (chart.ActualWidth - 8),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MinLimit.Value),
                DateTimeOffset.FromUnixTimeMilliseconds((long)timeAxis.MaxLimit.Value)));
    }

    public void SetSeries(IReadOnlyList<(string Name, string Color)> definitions, bool? filled = null)
    {
        ClearSelection(); series.Clear(); viewRange = null; frozenHistories = null; latestHistories = [];
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

    public void ConfigureAxes(IReadOnlyList<WorkspaceAxis> definitions, IReadOnlyList<int> assignments)
    {
        if (definitions.Count is < 1 or > 8 || definitions.Select(a => a.Index).Distinct().Count() != definitions.Count ||
            definitions.Any(a => a.Index is < 0 or > 7) || assignments.Count != series.Count ||
            assignments.Any(i => !definitions.Any(a => a.Index == i))) throw new ArgumentException("Invalid chart axes.");
        axisDefinitions = definitions.ToArray(); axisAssignments = assignments.ToArray();
        valueAxes = definitions.Select(a => new Axis {
            MinLimit = 0, MaxLimit = a.Maximum, TextSize = 11, ForceStepToMin = true, SubseparatorsPaint = null,
            Position = a.Index % 2 == 0 ? AxisPosition.Start : AxisPosition.End, LabelsPaint = null,
            SeparatorsPaint = a.Index == 0 ? new SolidColorPaint(SKColor.Parse("#213746"), .6f) : null
        }).ToArray();
        chart.YAxes = valueAxes;
        for (int i = 0; i < series.Count; i++) series[i].ScalesYAt = Array.FindIndex(definitions.ToArray(), a => a.Index == assignments[i]);
    }

    public void Update(IReadOnlyList<IReadOnlyList<TelemetrySample>> histories,
        DateTimeOffset now, int minutes, double divisor = 1)
    {
        if (histories.Count != series.Count) throw new ArgumentException("Series count does not match histories.");
        if (!double.IsFinite(divisor) || divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        if (minutes <= 0 || minutes > 30) throw new ArgumentOutOfRangeException(nameof(minutes));
        this.divisor = divisor; windowMinutes = minutes;
        latestHistories = histories; latestTime = now;
        RenderView();
    }

    private string ReadingName(int index) {
        var parts = (series[index].Name ?? "Sensor").Split(" · ");
        return parts.Length > 2 ? parts[0] + " · " + parts[^1] : string.Join(" · ", parts);
    }
    private void RenderView()
    {
        var range = viewRange ?? new PlotRange(latestTime.AddMinutes(-windowMinutes), latestTime);
        var histories = frozenHistories ?? latestHistories;
        visible = histories.Select(h => (IReadOnlyList<TelemetrySample>)h.Where(s => s.Time >= range.Start && s.Time <= range.End).ToArray()).ToArray();
        timeAxis.MinLimit = range.Start.ToUnixTimeMilliseconds();
        timeAxis.MaxLimit = range.End.ToUnixTimeMilliseconds();
        timeAxis.MinStep = range.Duration.TotalMilliseconds / (range.Duration.TotalMinutes <= 1 ? 6 : 5);
        laneMaxima = visible.Select((h, i) => Math.Max(1, (h.Select(s => s.Value / SeriesDivisor(i)).Max() ?? 1) * 1.1)).ToArray();
        for (int axis = 0; axis < valueAxes.Length; axis++)
        {
            var samples = visible.SelectMany((h, i) => series[i].IsVisible && (axisDefinitions == null || series[i].ScalesYAt == axis) ? h.Select(s => s.Value / SeriesDivisor(i)) : []);
            valueAxes[axis].MaxLimit = (axisDefinitions == null ? fixedMaximum : axisDefinitions[axis].Maximum) ?? Math.Max(1, (samples.Max() ?? 1) * 1.1);
            valueAxes[axis].MinStep = valueAxes[axis].MaxLimit!.Value / 2;
        }
        if (laneMode) foreach (var axis in valueAxes) { axis.MaxLimit = series.Count * 100; axis.MinStep = 100; }
        for (int i = 0; i < series.Count; i++)
            series[i].Values = visible[i]
                .Select(s => new ObservablePoint(s.Time.ToUnixTimeMilliseconds(), DisplayValue(i, s.Value))).ToArray();
        currentReadings.Text = string.Join("\n", visible.Select((samples, i) => !series[i].IsVisible ? "" : ReadingName(i) + " · " +
            (samples.LastOrDefault()?.Value is double value ? $"{value / SeriesDivisor(i):0.#}{SeriesUnit(i)}" : "Unavailable")).Where(s => s.Length > 0));
        RefreshScales();
        RefreshViewState(); RefreshSelectedPoint();
    }

    public void SetCursor(DateTimeOffset? time)
    {
        if (selectedPoint != null) { RefreshSelectedPoint(); return; }
        inspection.Text = ""; detailPanel.Visibility = Visibility.Collapsed; cursor.Visibility = Visibility.Collapsed;
        if (time == null || timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        double fraction = (time.Value.ToUnixTimeMilliseconds() - timeAxis.MinLimit.Value) /
            (timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value);
        if (fraction < 0 || fraction > 1) return;
        cursor.X1 = cursor.X2 = 4 + fraction * Math.Max(0, chart.ActualWidth - 8);
        cursor.Y1 = 8; cursor.Y2 = Math.Max(8, chart.ActualHeight - 24); cursor.Visibility = Visibility.Visible;
        inspection.Text = string.Join("\n", visible.Select((samples, i) =>
        {
            var sample = series[i].IsVisible ? PlotData.Nearest(samples, time.Value) : null;
            return sample == null ? "" : $"{series[i].Name} · {sample.Time.ToLocalTime():HH:mm:ss.fff} · " +
                (sample.Value is double value ? $"{value / SeriesDivisor(i):0.###}{SeriesUnit(i)}" : "N/A");
        }));
        detailPanel.Visibility = string.IsNullOrWhiteSpace(inspection.Text) ? Visibility.Collapsed : Visibility.Visible; detailPanel.RefreshBackdrop();
    }

    private void RefreshScales()
    {
        detailPanel.MaxWidth = Math.Max(100, chart.ActualWidth - 24);
        currentReadings.MaxWidth = Math.Max(80, chart.ActualWidth * .55); currentReadings.MaxHeight = Math.Max(30, chart.ActualHeight * .45);
        scales.Children.Clear();
        if (chart.ActualWidth < 60 || chart.ActualHeight < 40) return;
        currentReadings.Visibility = laneMode ? Visibility.Collapsed : Visibility.Visible;
        if (laneMode) {
            for (int i = 0; i < series.Count && i < laneMaxima.Length; i++) {
                var label = OverviewTheme.Text($"{ReadingName(i)} · {(visible[i].LastOrDefault()?.Value / SeriesDivisor(i))?.ToString("0.#") ?? "N/A"}{SeriesUnit(i)} · scale 0–{laneMaxima[i]:0.#}", 10, OverviewTheme.Muted);
                Canvas.SetLeft(label, 6); Canvas.SetTop(label, 8 + i * (chart.ActualHeight - 32) / Math.Max(1, series.Count)); scales.Children.Add(label);
            }
        }
        for (int axis = 0; !laneMode && axis < valueAxes.Length; axis++)
        {
            double maximum = valueAxes[axis].MaxLimit ?? 1;
            string suffix = axisDefinitions == null ? unit : axisDefinitions[axis].DisplayUnit;
            bool right = axisDefinitions != null && axisDefinitions[axis].Index % 2 == 1;
            double inset = valueAxes.Length <= 2 ? 6 : 6 + (axis / 2) * Math.Max(45, chart.ActualWidth / 9);
            for (int i = 0; i < 3; i++)
            {
                var label = OverviewTheme.Text($"{maximum * (2 - i) / 2:0.#}{suffix}", 10, OverviewTheme.Muted);
                label.Opacity = .8;
                if (right) Canvas.SetRight(label, inset); else Canvas.SetLeft(label, inset);
                Canvas.SetTop(label, Math.Min(chart.ActualHeight - 38, Math.Max(8, 3 + i * (chart.ActualHeight - 32) / 2))); scales.Children.Add(label);
            }
        }
        if (timeAxis.MinLimit == null || timeAxis.MaxLimit == null) return;
        double span = timeAxis.MinLimit.HasValue && timeAxis.MaxLimit.HasValue ? timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value : windowMinutes * 60000d;
        int intervals = span <= 60000 ? 6 : 5;
        for (int i = 0; i <= intervals; i++)
        {
            double fraction = (double)i / intervals;
            var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(timeAxis.MinLimit.Value + fraction * (timeAxis.MaxLimit.Value - timeAxis.MinLimit.Value)));
            var label = OverviewTheme.Text(time.ToLocalTime().ToString(span < 300000 ? "HH:mm:ss" : "HH:mm"), 10, OverviewTheme.Muted);
            Canvas.SetLeft(label, Math.Clamp(4 + fraction * (chart.ActualWidth - 8) - 15, 4, chart.ActualWidth - 35));
            Canvas.SetTop(label, chart.ActualHeight - 18); scales.Children.Add(label);
        }
    }

    public IReadOnlyList<TelemetrySample> VisibleSamples => visible.Where((_, i) => series[i].IsVisible).SelectMany(s => s).OrderBy(s => s.Time).ToArray();

    public IReadOnlyList<PlotExportRow> ExportRows(string fallbackUnit) => visible.SelectMany((samples, i) => (series[i].IsVisible ? samples : []).Select(s =>
        new PlotExportRow(s, axisDefinitions == null ? fallbackUnit : axisDefinitions.Single(a => a.Index == axisAssignments[i]).RawUnit)))
        .OrderBy(r => r.Sample.Time).ToArray();

    public int SeriesCount => series.Count;
    public void SetMaximum(double? maximum) { fixedMaximum = maximum; valueAxis.MaxLimit = maximum; RefreshScales(); }
    public void SetName(int index, string name) => series[index].Name = name;
}
