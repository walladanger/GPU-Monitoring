using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class HistoryWindow : Window
{
    private readonly string path;
    private readonly ComboBox sensors = new() { MinWidth = 240, MaxWidth = 750 };
    private readonly TextBox search = new() { Width = 260, MaxLength = 128 };
    private readonly TextBox ending = new() { Width = 250, Text = DateTimeOffset.UtcNow.ToString("O") };
    private readonly ComboBox minutes = new() { Width = 75, ItemsSource = new[] { 1, 5, 15, 30 }, SelectedItem = 30 };
    private readonly TextBlock status = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TelemetryPlot plot = new("", null) { Height = 360, Margin = new(0, 16, 0, 12) };
    private readonly StackPanel root = new() { Margin = new(20) };
    private bool closed, busy;
    internal TelemetryPlot Plot => plot;
    internal ComboBox Sensors => sensors;
    internal TextBox Ending => ending;
    internal ComboBox Minutes => minutes;
    internal string Status => status.Text;
    public HistoryWindow(Window owner, string path)
    {
        this.path = path; Owner = owner; Title = "Stored history"; Width = 1050; Height = 690; MinWidth = 720; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI"); Content = root;
        root.Children.Add(OverviewTheme.DialogHeading(this));
        root.Children.Add(OverviewTheme.Text("Archived measurements · never live readings. Search shows up to 256 saved sensors; refine the search if needed.", 12, OverviewTheme.Muted));
        var find = new WrapPanel { Margin = new(0, 12, 0, 8) }; find.Children.Add(search);
        find.Children.Add(OverviewTheme.Command("Search saved sensors", async () => await RefreshCatalogAsync())); root.Children.Add(find); root.Children.Add(sensors);
        var range = new WrapPanel { Margin = new(0, 10, 0, 0) };
        range.Children.Add(OverviewTheme.Text("Ending (UTC/offset): ", 12, OverviewTheme.Muted)); range.Children.Add(ending);
        range.Children.Add(OverviewTheme.Text(" Minutes: ", 12, OverviewTheme.Muted)); range.Children.Add(minutes);
        range.Children.Add(OverviewTheme.Command("Load measurements", async () => await LoadMeasurementsAsync())); root.Children.Add(range);
        root.Children.Add(plot); status.TextWrapping = TextWrapping.Wrap; root.Children.Add(status);
        root.Children.Add(OverviewTheme.Text("Right-click the loaded chart to export PNG or its displayed raw CSV/JSON. At most 10,000 newest samples are loaded per query. Missing devices retain their recorded identity.", 12, OverviewTheme.Muted));
        root.Children.Add(OverviewTheme.Command("Close", Close));
        plot.CursorMoved += plot.SetCursor;
        Loaded += async (_, _) => await RefreshCatalogAsync(); Closed += (_, _) => closed = true;
    }
    internal async Task RefreshCatalogAsync()
    {
        if (busy) return;
        await QueryAsync(async () =>
        {
            string text = search.Text;
            var catalog = await Task.Run(() => File.Exists(path) ? HistoryStore.CatalogFile(path, text) : []);
            if (closed) return;
            sensors.ItemsSource = catalog; sensors.SelectedIndex = catalog.Count > 0 ? 0 : -1;
            status.Text = catalog.Count == 0 ? "No saved sensors. Enable persistent history to record measurements." : $"{catalog.Count} saved sensors. Choose one and load a time range.";
        });
    }
    internal async Task LoadMeasurementsAsync()
    {
        if (busy || sensors.SelectedItem is not HistorySensor selected) return;
        await QueryAsync(async () =>
        {
            string text = ending.Text.Trim();
            bool explicitOffset = text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:\d{2}$");
            if (!explicitOffset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var to))
                throw new FormatException("Enter an ISO timestamp with Z or an explicit offset.");
            int range = (int)minutes.SelectedItem;
            var rows = await Task.Run(() => HistoryStore.ReadFile(path, selected.Selection, selected.Unit, to.AddMinutes(-range), to));
            if (closed) return;
            plot.SetSeries([(selected.ToString(), "#33DF32")], false); plot.ConfigureAxes([new(0, selected.Unit)], [0]);
            plot.Update([rows.Select(r => r.Sample).ToArray()], to, range); OverviewTheme.PlotMenu(plot, selected.Unit, "stored-history");
            status.Text = $"{rows.Count:N0} archived samples · newest 10,000 maximum · {to.AddMinutes(-range):u} to {to:u} · raw unit [{selected.Unit}]";
        });
    }
    private async Task QueryAsync(Func<Task> query)
    {
        busy = true; root.IsEnabled = false; status.Text = "Reading saved history…";
        try { await query(); }
        catch (Exception error) { if (!closed) status.Text = "History unavailable: " + error.Message; }
        finally { busy = false; if (!closed) root.IsEnabled = true; }
    }
}
