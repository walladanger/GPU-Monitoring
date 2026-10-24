using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GpuMonitor;

internal sealed partial class MonitoringWorkspace : StackPanel
{
    private readonly Func<IReadOnlyList<ProviderSnapshot>> sources;
    private readonly WorkspacePresetStore store;
    private readonly WrapPanel panels = new();
    private readonly Dictionary<string, WorkspaceChartCard> cards = [];
    private WorkspaceLayout? previous;
    private DateTimeOffset time = DateTimeOffset.UtcNow;
    private int minutes = 5;
    public WorkspaceLayout Layout { get; private set; } = new(1, "Custom workspace", []);
    public int PanelCount => Layout.Panels.Count;
    public IEnumerable<TelemetryPlot> Plots => cards.Values.Select(c => c.Plot).OfType<TelemetryPlot>();
    public event Action<DateTimeOffset?>? CursorMoved;
    public event Action? LayoutChanged;
    internal System.Windows.Controls.Button AddButton { get; }
    internal System.Windows.Controls.Button PresetsButton { get; }
    internal Func<Point> PointerPosition { get; set; }

    public MonitoringWorkspace(Func<IReadOnlyList<ProviderSnapshot>> sources, string? presetDirectory = null)
    {
        this.sources = sources;
        PointerPosition = () => Mouse.GetPosition(panels);
        store = new(presetDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitor", "workspaces"));
        var heading = new DockPanel { Margin = new(0, 0, 14, 12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton = OverviewTheme.Command("Add panel", () => Edit(null)); actions.Children.Add(AddButton);
        PresetsButton = OverviewTheme.Command("Presets", OpenPresets); actions.Children.Add(PresetsButton);
        DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
        heading.Children.Add(OverviewTheme.Text("Custom panels", 16)); Children.Add(heading); Children.Add(panels);
        panels.SizeChanged += (_, _) => Reflow();
    }

    internal void OpenPicker(string initialQuery = "") => Edit(null, initialQuery);

    private void Edit(WorkspacePanel? panel, string initialQuery = "")
    {
        var editor = new WorkspacePanelEditor(Window.GetWindow(this), sources(), panel, initialQuery);
        if (editor.ShowDialog() == true && editor.Result != null) PutPanel(editor.Result);
    }

    internal void PutPanel(WorkspacePanel panel)
    {
        var next = Layout.Panels.ToList(); int index = next.FindIndex(p => p.Id == panel.Id);
        if (index < 0) next.Add(panel); else next[index] = panel;
        ApplyLayout(Layout with { Panels = next.ToArray() });
    }

    internal void ApplyLayout(WorkspaceLayout layout)
    {
        WorkspacePresetStore.Validate(layout);
        previous = Layout; Layout = layout;
        panels.Children.Clear(); cards.Clear();
        foreach (var panel in layout.Panels)
        {
            var card = new WorkspaceChartCard(panel, () => OpenPanelMenu(panel.Id)) { Focusable = true };
            card.CursorMoved += value => CursorMoved?.Invoke(value);
            card.MouseLeftButtonDown += (_, _) => card.Focus();
            card.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Up or Key.Down)
                { Move(panel.Id, key == Key.Up ? -1 : 1); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Delete)
                { Remove(panel.Id); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.E)
                { Edit(panel); e.Handled = true; }
            };
            cards[panel.Id] = card; panels.Children.Add(card);
            WireGestures(card);
            card.ContextMenu = PanelMenu(panel.Id);
        }
        AddButton.IsEnabled = layout.Panels.Count < 64;
        Refresh(sources(), time, minutes); Reflow(); LayoutChanged?.Invoke();
    }

    internal void Remove(string id) => ApplyLayout(Layout with { Panels = Layout.Panels.Where(p => p.Id != id).ToArray() });
    internal void Move(string id, int direction)
    {
        var items = Layout.Panels.ToList(); int index = items.FindIndex(p => p.Id == id);
        if (index < 0 || index + direction < 0 || index + direction >= items.Count) return;
        (items[index], items[index + direction]) = (items[index + direction], items[index]);
        ApplyLayout(Layout with { Panels = items.ToArray() });
        cards[id].Focus();
    }
    internal void Undo()
    {
        if (previous == null) return;
        var target = previous; ApplyLayout(target);
    }

    private void OpenPanelMenu(string id)
    {
        var menu = PanelMenu(id); menu.PlacementTarget = cards[id]; menu.IsOpen = true;
    }
    private ContextMenu PanelMenu(string id)
    {
        var panel = Layout.Panels.Single(p => p.Id == id);
        var menu = OverviewTheme.Menu();
        menu.Items.Add(OverviewTheme.MenuCommand("Edit sensors / rename…   Ctrl+E", () => Edit(panel)));
        menu.Items.Add(OverviewTheme.MenuCommand("Move earlier   Alt+Up", () => Move(id, -1)));
        menu.Items.Add(OverviewTheme.MenuCommand("Move later   Alt+Down", () => Move(id, 1)));
        var width = new MenuItem { Header = "Width" }; var height = new MenuItem { Header = "Height" };
        foreach (int value in Enumerable.Range(1, 4)) width.Items.Add(OverviewTheme.MenuCommand($"{value} slot(s)", () => PutPanel(panel with { WidthSlots = value })));
        foreach (int value in Enumerable.Range(1, 8)) height.Items.Add(OverviewTheme.MenuCommand($"{value} row(s)", () => PutPanel(panel with { HeightRows = value })));
        menu.Items.Add(width); menu.Items.Add(height);
        var duplicate = OverviewTheme.MenuCommand("Duplicate panel", () => Change(() => WorkspaceActions.Duplicate(Layout, id)));
        duplicate.IsEnabled = Layout.Panels.Count < 64; menu.Items.Add(duplicate);
        var merge = new MenuItem { Header = "Merge into…", IsEnabled = Layout.Panels.Count > 1 };
        merge.SubmenuOpened += (_, _) =>
        {
            merge.Items.Clear(); var snapshots = sources();
            foreach (var target in Layout.Panels.Where(p => p.Id != id))
            {
                var item = OverviewTheme.MenuCommand(target.Title, () => Change(() => WorkspaceActions.Merge(Layout, id, target.Id, sources())));
                try { WorkspaceActions.Merge(Layout, id, target.Id, snapshots); item.ToolTip = target.Id; }
                catch (Exception error) when (error is ArgumentException or FormatException) { item.IsEnabled = false; item.ToolTip = error.Message; }
                merge.Items.Add(item);
            }
        };
        menu.Items.Add(merge);
        var split = OverviewTheme.MenuCommand("Split into one panel per sensor", () => Change(() => WorkspaceActions.Split(Layout, id)));
        split.IsEnabled = panel.Series.Count > 1 && Layout.Panels.Count + panel.Series.Count - 1 <= 64;
        menu.Items.Add(split);
        menu.Items.Add(OverviewTheme.MenuCommand("Remove panel   Ctrl+Delete", () => Remove(id)));
        return menu;
    }

    public void Refresh(IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset now, int windowMinutes)
    {
        time = now; minutes = windowMinutes;
        foreach (var card in cards.Values) card.Refresh(snapshots, now, windowMinutes);
    }
    public void SetCursor(DateTimeOffset? value) { foreach (var plot in Plots) plot.SetCursor(value); }
    private void Reflow()
    {
        double width = panels.ActualWidth; if (width <= 0) return;
        int columns = Math.Max(1, (int)Math.Floor(width / 500));
        foreach (var card in cards.Values) card.Width = Math.Max(260, width / columns * Math.Min(columns, card.LayoutWidthSlots) - 14);
    }

    private void OpenPresets()
    {
        var menu = OverviewTheme.Menu();
        menu.Items.Add(OverviewTheme.MenuCommand("Save named preset…", () =>
        {
            var id = WorkspacePresetName.Ask(Window.GetWindow(this));
            if (id != null) _ = FileAction(() => SavePresetAsync(id));
        }));
        var load = new MenuItem { Header = "Load preset" };
        try
        {
            foreach (var id in store.ListPresets()) load.Items.Add(OverviewTheme.MenuCommand(id, () => { _ = FileAction(() => LoadPresetAsync(id)); }));
            if (load.Items.Count == 0) load.Items.Add(new MenuItem { Header = "No saved presets", IsEnabled = false });
        }
        catch (Exception error) { load.Items.Add(new MenuItem { Header = "Presets unavailable", ToolTip = error.Message, IsEnabled = false }); }
        menu.Items.Add(load);
        var undo = OverviewTheme.MenuCommand("Undo last panel change", Undo); undo.IsEnabled = previous != null; menu.Items.Add(undo);
        menu.Items.Add(OverviewTheme.MenuCommand("Clear custom panels", () => ApplyLayout(Layout with { Panels = [] })));
        PresetsButton.ContextMenu = menu; menu.PlacementTarget = PresetsButton; menu.IsOpen = true;
    }
    private async Task FileAction(Func<Task> action)
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        try { await action(); }
        catch (Exception error) { if (IsLoaded) MessageBox.Show(Window.GetWindow(this), error.Message, "Workspace preset could not be saved or loaded"); }
        finally { IsEnabled = true; }
    }
    internal Task SavePresetAsync(string id) => store.SaveAsync(id, Layout);
    internal async Task LoadPresetAsync(string id) => ApplyLayout(await store.LoadAsync(id));
}

internal static class WorkspacePresetName
{
    public static string? Ask(Window owner)
    {
        var window = new Window { Owner = owner, Title = "Save workspace preset", Width = 430, Height = 250,
            Background = OverviewTheme.Canvas, Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new(18) }; window.Content = content;
        content.Children.Add(OverviewTheme.DialogHeading(window));
        content.Children.Add(OverviewTheme.Text("Preset name (letters, digits, - or _)", 13, OverviewTheme.Muted));
        var name = new TextBox { MaxLength = 64, Margin = new(0, 12, 0, 12), Padding = new(8), Background = OverviewTheme.Surface,
            Foreground = Brushes.White, BorderBrush = OverviewTheme.Border };
        System.Windows.Automation.AutomationProperties.SetName(name, "Workspace preset name"); content.Children.Add(name);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var error = OverviewTheme.Text("", 11, OverviewTheme.Muted); content.Children.Add(error);
        var save = OverviewTheme.Command("Save", () =>
        {
            if (name.Text.Length > 0 && name.Text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) window.DialogResult = true;
            else error.Text = "Use letters, digits, hyphens or underscores.";
        }); save.IsDefault = true; actions.Children.Add(save);
        var cancel = OverviewTheme.Command("Cancel", () => window.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel); content.Children.Add(actions);
        window.Loaded += (_, _) => name.Focus(); return window.ShowDialog() == true ? name.Text.Trim() : null;
    }
}
