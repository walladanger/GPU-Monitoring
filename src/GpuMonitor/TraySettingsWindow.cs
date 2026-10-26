using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class TraySettingsWindow : Window
{
    private readonly HashSet<WorkspaceSeries> selected;
    private readonly IReadOnlyList<WorkspaceMetricChoice> catalog;
    private readonly StackPanel choices = new();
    private readonly TextBox search = new() { MaxLength = 128, Margin = new(0, 8, 0, 8), Padding = new(8) };
    private readonly TextBlock state = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private bool automatic;
    internal readonly CheckBox Hover = new() { Content = "Show quick-view when hovering the tray icon", Foreground = Brushes.White, Margin = new(0, 8, 0, 10) };
    internal readonly Slider OpacitySlider = new() { Minimum = .65, Maximum = 1, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    internal readonly Dictionary<WorkspaceSeries, CheckBox> VisibleChoices = [];
    internal readonly System.Windows.Controls.Button Apply;
    public TraySettingsWindow(Window owner, Settings preferences, IReadOnlyList<ProviderSnapshot> sources, Action<bool, double, List<WorkspaceSeries>?> apply)
    {
        Owner = owner; Title = "Tray quick-view settings"; Width = 690; Height = 640; MinWidth = 480; MinHeight = 440;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        automatic = preferences.TraySensors == null;
        selected = (preferences.TraySensors ?? TrayReadings.Project(sources, null).Select(r => r.Selection).ToList()).ToHashSet();
        var available = WorkspaceMetrics.Catalog(sources).ToList();
        foreach (var selection in selected)
            if (!available.Any(c => c.Selection == selection))
                available.Add(new(selection, "Saved selection", selection.DeviceId, selection.MetricKey, "", WorkspaceBindingState.Unavailable));
        catalog = available.AsReadOnly();
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(OverviewTheme.DialogHeading(this)); Hover.IsChecked = preferences.TrayHoverEnabled; heading.Children.Add(Hover);
        heading.Children.Add(OverviewTheme.Text("Surface opacity (Windows backdrop when available)", 12, OverviewTheme.Muted));
        OpacitySlider.Value = preferences.TrayOpacity; heading.Children.Add(OpacitySlider);
        heading.Children.Add(OverviewTheme.Text("Search enabled sensors · choose up to eight; different units are shown individually", 12, OverviewTheme.Muted));
        search.Background = OverviewTheme.Surface; search.Foreground = Brushes.White; search.BorderBrush = OverviewTheme.Border; heading.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(state);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) }; footer.Children.Add(actions);
        actions.Children.Add(OverviewTheme.Command("Automatic GPU defaults", () =>
        {
            selected.Clear(); foreach (var reading in TrayReadings.Project(sources, null)) selected.Add(reading.Selection);
            automatic = true; Filter();
        }));
        Apply = OverviewTheme.Command("Apply", () =>
        {
            if (selected.Count > 8) { state.Text = "Choose at most eight sensors."; return; }
            apply(Hover.IsChecked == true, OpacitySlider.Value, automatic ? null : catalog.Where(c => selected.Contains(c.Selection)).Select(c => c.Selection).ToList());
            DialogResult = true;
        }); actions.Children.Add(Apply);
        var cancel = OverviewTheme.Command("Cancel", Close); cancel.IsCancel = true; actions.Children.Add(cancel);
        root.Children.Add(new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        search.TextChanged += (_, _) => Filter(); Filter();
    }
    private void Filter()
    {
        choices.Children.Clear(); VisibleChoices.Clear();
        foreach (var choice in WorkspaceMetrics.Search(catalog, search.Text))
        {
            var checkbox = new CheckBox { Content = choice.Label, Foreground = Brushes.White, Margin = new(4, 6, 4, 6), IsChecked = selected.Contains(choice.Selection) };
            checkbox.Checked += (_, _) => { automatic = false; selected.Add(choice.Selection); Count(); };
            checkbox.Unchecked += (_, _) => { automatic = false; selected.Remove(choice.Selection); Count(); };
            choices.Children.Add(checkbox); VisibleChoices[choice.Selection] = checkbox;
        }
        if (VisibleChoices.Count == 0) choices.Children.Add(OverviewTheme.Text("No matching enabled sensors.", 12, OverviewTheme.Muted)); Count();
    }
    private void Count() => state.Text = $"{selected.Count}/8 selected · {(automatic ? "automatic actual GPU capabilities" : "saved stable sensor identities")}";
}
