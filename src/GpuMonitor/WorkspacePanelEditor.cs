using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed class WorkspacePanelEditor : Window
{
    private readonly WorkspacePanel original;
    private readonly IReadOnlyList<WorkspaceMetricChoice> catalog;
    private readonly HashSet<WorkspaceSeries> selected;
    private readonly TextBox title, search;
    private readonly StackPanel choices = new();
    private readonly TextBlock status = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly RadioButton area = new() { Content = "Area", Foreground = Brushes.White, Margin = new(16, 0, 0, 0), GroupName = "ChartType" };
    internal readonly Dictionary<WorkspaceSeries, CheckBox> VisibleChoices = [];
    internal readonly System.Windows.Controls.Button ApplyButton;
    public WorkspacePanel? Result { get; private set; }

    public WorkspacePanelEditor(Window owner, IReadOnlyList<ProviderSnapshot> snapshots, WorkspacePanel? panel = null)
    {
        Owner = owner; Title = panel == null ? "Add monitoring panel" : "Edit monitoring panel";
        Width = 650; Height = 580; MinWidth = 450; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI");
        original = panel ?? new(Guid.NewGuid().ToString("N"), "Custom panel", 1, 2, "line", []);
        selected = panel?.Series.ToHashSet() ?? [];
        var available = WorkspaceMetrics.Catalog(snapshots).ToList();
        var savedBindings = panel == null ? new Dictionary<WorkspaceSeries, WorkspaceSeriesResolution>() :
            WorkspaceResolver.Resolve(new(1, "Saved selections", [panel]), snapshots).Single().Series.ToDictionary(s => s.Selection);
        // Keep missing selections visible when editing rather than silently dropping a saved binding.
        foreach (var binding in selected)
        {
            int existing = available.FindIndex(c => c.Selection.Source == binding.Source && c.Selection.DeviceId == binding.DeviceId && c.Selection.MetricKey == binding.MetricKey);
            if (existing >= 0) available[existing] = available[existing] with { Selection = binding };
            else available.Add(new(binding, "Saved selection", binding.DeviceId, binding.MetricKey,
                savedBindings[binding].Unit ?? "", savedBindings[binding].State));
        }
        catalog = available.AsReadOnly();
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(OverviewTheme.DialogHeading(this));
        header.Children.Add(OverviewTheme.Text("Panel title", 13, OverviewTheme.Muted));
        title = Input(original.Title, "Panel title"); title.MaxLength = 96; header.Children.Add(title);
        header.Children.Add(OverviewTheme.Text("Search sensors by device, metric, group or source", 13, OverviewTheme.Muted));
        search = Input("", "Search monitoring sensors"); header.Children.Add(search);
        var mode = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 4, 0, 12) };
        mode.Children.Add(new RadioButton { Content = "Line", IsChecked = original.Chart == "line", Foreground = Brushes.White, GroupName = "ChartType" });
        area.IsChecked = original.Chart == "area"; mode.Children.Add(area); header.Children.Add(mode);
        var footer = new DockPanel { Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        ApplyButton = OverviewTheme.Command("Apply", () => { if (TryApply()) DialogResult = true; });
        ApplyButton.IsDefault = true; DockPanel.SetDock(ApplyButton, Dock.Right); footer.Children.Add(ApplyButton);
        var cancel = OverviewTheme.Command("Cancel", () => DialogResult = false); cancel.IsCancel = true;
        DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        search.TextChanged += (_, _) => Filter(); Filter();
    }

    private static TextBox Input(string value, string name)
    {
        var input = new TextBox { Text = value, Foreground = Brushes.White, Background = OverviewTheme.Surface,
            BorderBrush = OverviewTheme.Border, Padding = new(8), Margin = new(0, 6, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(input, name); return input;
    }
    private void Filter()
    {
        choices.Children.Clear(); VisibleChoices.Clear();
        foreach (var metric in WorkspaceMetrics.Search(catalog, search.Text))
        {
            var checkbox = new CheckBox { Content = new TextBlock { Text = metric.Label + " · " + WorkspaceMetrics.StateLabel(metric.State), TextWrapping = TextWrapping.Wrap },
                IsChecked = selected.Contains(metric.Selection), Foreground = Brushes.White, Margin = new(4, 7, 4, 7),
                ToolTip = $"{metric.Selection.Source}\n{metric.Selection.DeviceId}\n{metric.Selection.MetricKey}" };
            checkbox.Checked += (_, _) => { selected.Add(metric.Selection); Count(); };
            checkbox.Unchecked += (_, _) => { selected.Remove(metric.Selection); Count(); };
            choices.Children.Add(checkbox); VisibleChoices[metric.Selection] = checkbox;
        }
        if (choices.Children.Count == 0) choices.Children.Add(OverviewTheme.Text("No matching sensors from the enabled sources.", 13, OverviewTheme.Muted));
        Count();
    }
    private void Count() => status.Text = $"{selected.Count} selected · up to 16 sensors with the same unit";
    private bool TryApply()
    {
        var series = catalog.Where(c => selected.Contains(c.Selection)).Select(c => c.Selection).ToArray();
        var candidate = original with { Title = title.Text.Trim(), Chart = area.IsChecked == true ? "area" : "line", Series = series };
        try
        {
            if (series.Length is < 1 or > 16) throw new FormatException("Select between 1 and 16 sensors.");
            if (candidate.Title.Length == 0) throw new FormatException("Enter a panel title.");
            WorkspacePresetStore.Validate(new(1, "Editor", [candidate]));
            if (series.Any(s => s.Axis != 0)) throw new FormatException("Secondary-axis presets are retained, but cannot be edited in this build.");
            if (catalog.Where(c => selected.Contains(c.Selection) && c.Unit.Length > 0).Select(c => c.Unit).Distinct(StringComparer.Ordinal).Count() > 1)
                throw new FormatException("Choose sensors with the same unit. Separate axes will be available in a later increment.");
            Result = candidate; return true;
        }
        catch (FormatException error) { status.Text = error.Message; return false; }
    }
    internal void VerifySearch(string value) => search.Text = value;
}
