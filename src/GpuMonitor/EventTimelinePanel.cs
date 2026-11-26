using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace GpuMonitor;

internal sealed class EventTimelinePanel : Border
{
    private readonly Canvas canvas = new() { Background = OverviewTheme.Canvas, MinHeight = 140, ClipToBounds = true };
    private readonly TextBlock status = OverviewTheme.Text("No recorded activity in this time window.", 12, OverviewTheme.Muted);
    private readonly TextBlock inspection = OverviewTheme.Text("", 11, OverviewTheme.Muted);
    private Line? cursorLine;
    private readonly ComboBox source = new() { MinWidth = 130, MaxWidth = 240 };
    private IReadOnlyList<TelemetryEvent> events = [];
    private IReadOnlyList<TelemetryEvent>? frozen;
    private DateTimeOffset now;
    private int minutes = 5;
    private PlotRange? range;
    private DateTimeOffset? cursor;
    private bool changingSource;
    internal TimelineView View { get; private set; } = EventTimeline.Project([], new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5)));
    public event Action<DateTimeOffset?>? CursorMoved;

    public EventTimelinePanel()
    {
        BorderBrush = OverviewTheme.Border; BorderThickness = new(1); CornerRadius = new(9); Background = OverviewTheme.Surface; Padding = new(14); Margin = new(0, 0, 14, 14);
        var root = new StackPanel(); Child = root;
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
        source.Foreground = Brushes.White; source.Background = OverviewTheme.Surface; controls.Children.Add(source);
        source.ItemsSource = new[] { "All sources" }; source.SelectedIndex = 0;
        foreach (string format in new[] { "csv", "json", "txt" }) controls.Children.Add(OverviewTheme.Command(format.ToUpperInvariant(), () => Export(format), "Export activity as " + format.ToUpperInvariant()));
        header.Children.Add(OverviewTheme.Text("Process / agent activity", 15)); root.Children.Add(header);
        status.TextWrapping = TextWrapping.Wrap; root.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content = canvas, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        inspection.TextWrapping = TextWrapping.Wrap; root.Children.Add(inspection);
        canvas.SizeChanged += (_, _) => Draw();
        source.SelectionChanged += (_, _) => { if (!changingSource) Render(); };
        canvas.MouseMove += (_, e) => { var time = PlotData.CursorTime((e.GetPosition(canvas).X - 4) / Math.Max(1, canvas.ActualWidth - 8), View.Range.Start, View.Range.End); SetCursor(time); CursorMoved?.Invoke(time); };
        canvas.MouseLeave += (_, _) => { SetCursor(null); CursorMoved?.Invoke(null); };
    }
    public void Update(IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset time, int windowMinutes)
    {
        now = time; minutes = windowMinutes; events = snapshots.SelectMany(s => s.EventHistory).ToArray();
        string selected = source.SelectedItem as string ?? "All sources";
        var visibleEvents = frozen ?? events;
        var names = new[] { "All sources" }.Concat(visibleEvents.Select(e => e.Source)).Distinct().ToArray();
        if (!source.Items.Cast<string>().SequenceEqual(names))
        { changingSource = true; source.ItemsSource = names; source.SelectedItem = names.Contains(selected) ? selected : names[0]; changingSource = false; }
        Render();
    }
    public void SetRange(PlotRange? value)
    {
        if (value == null) frozen = null; else frozen ??= events.ToArray();
        range = value; Render();
    }
    public void SetCursor(DateTimeOffset? value) { cursor = value; UpdateCursorOverlay(); }
    private void Render()
    {
        if (now == default) return;
        View = EventTimeline.Project(frozen ?? events, range ?? new(now.AddMinutes(-minutes), now), source.SelectedIndex <= 0 ? null : source.SelectedItem as string);
        status.Text = View.Entries.Count == 0 ? "No recorded activity in this time window · configure an AG-UI/OTLP feed for agent traces." :
            $"{View.Entries.Count} measured events · {View.Omitted} omitted · {(range == null ? "Live" : "Paused")} · Hover a bar for source metadata";
        Draw();
    }
    private void Draw()
    {
        canvas.Children.Clear(); cursorLine = null; double width = canvas.ActualWidth; if (width < 20) return;
        canvas.Height = Math.Max(140, View.Lanes.Count * 40 + 30);
        double X(DateTimeOffset time) => 4 + Math.Clamp((time - View.Range.Start).TotalMilliseconds / View.Range.Duration.TotalMilliseconds, 0, 1) * (width - 8);
        for (int i = 0; i <= 4; i++)
        {
            double x = 4 + (width - 8) * i / 4;
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = canvas.Height - 20, Stroke = OverviewTheme.Border, StrokeThickness = .5 });
            var label = OverviewTheme.Text(View.Range.Start.AddTicks(View.Range.Duration.Ticks * i / 4).ToLocalTime().ToString("HH:mm:ss"), 10, OverviewTheme.Muted);
            Canvas.SetLeft(label, Math.Clamp(x - 20, 0, width - 48)); Canvas.SetTop(label, canvas.Height - 18); canvas.Children.Add(label);
        }
        for (int i = 0; i < View.Lanes.Count; i++)
        {
            var label = OverviewTheme.Text(View.Lanes[i], 10, OverviewTheme.Muted); label.MaxWidth = width - 12; label.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(label, 6); Canvas.SetTop(label, i * 40); canvas.Children.Add(label);
        }
        foreach (var item in View.Entries)
        {
            var bar = new Border { Width = Math.Max(3, X(item.End) - X(item.Start)), Height = 13, CornerRadius = new(2),
                Background = item.Event.Kind.Contains("error", StringComparison.OrdinalIgnoreCase) ? OverviewTheme.Brush("#F45A5A") : item.Event.Source.StartsWith("agui:", StringComparison.Ordinal) ? OverviewTheme.Green : OverviewTheme.Blue,
                Opacity = item.Event.TimestampEstimated ? .5 : .85,
                ToolTip = $"{item.Event.Kind} · {item.Event.Name}\n{item.Start:O} – {item.End:O}\n{item.Event.Source} · {item.Event.DeviceId}\nCorrelation: {item.Event.CorrelationId ?? "none"}\nDuration: {item.Event.Duration?.TotalMilliseconds.ToString("0.###") ?? "unknown"} ms\nTimestamp estimated: {item.Event.TimestampEstimated}" };
            Canvas.SetLeft(bar, X(item.Start)); Canvas.SetTop(bar, item.Lane * 40 + 17); canvas.Children.Add(bar);
        }
        cursorLine = new Line { Y1 = 0, Y2 = canvas.Height - 20, Stroke = Brushes.LightGray, StrokeThickness = 1, IsHitTestVisible = false };
        canvas.Children.Add(cursorLine);
        UpdateCursorOverlay();
    }
    private void UpdateCursorOverlay()
    {
        inspection.Text = cursor == null ? "" : $"Shared cursor · {cursor.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}";
        if (cursorLine == null) return;
        bool visible = cursor is { } time && time >= View.Range.Start && time <= View.Range.End && canvas.ActualWidth >= 20;
        cursorLine.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        double x = 4 + Math.Clamp((cursor!.Value - View.Range.Start).TotalMilliseconds / View.Range.Duration.TotalMilliseconds, 0, 1) * (canvas.ActualWidth - 8);
        cursorLine.X1 = x; cursorLine.X2 = x; cursorLine.Y2 = canvas.Height - 20;
    }
    private void Export(string format)
    {
        var dialog = new SaveFileDialog { Filter = format.ToUpperInvariant() + " files|*." + format, FileName = "activity." + format };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { using var writer = new StreamWriter(dialog.FileName); EventTimeline.Export(writer, View, format); }
        catch (Exception error) { MessageBox.Show(Window.GetWindow(this), error.Message, "Activity export failed"); }
    }
}
