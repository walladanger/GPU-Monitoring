using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GpuMonitor;

/// <summary>The CPU/disk row from the annotated reference. Values come only from configured providers.</summary>
internal sealed class HardwareOverviewCard : Border
{
    private readonly bool cpu;
    private readonly TextBlock state = OverviewTheme.Text("Source not enabled", 12, OverviewTheme.Muted);
    private readonly TextBlock[] values = Enumerable.Range(0, 3).Select(_ => OverviewTheme.Text("—", 14)).ToArray();
    private readonly TelemetryPlot plot = new("%", 100) { Height = 112 };
    private readonly System.Windows.Controls.Button selector;
    private IReadOnlyList<ProviderSnapshot> sources = [];
    private string? deviceId;
    public TelemetryPlot Plot => plot;

    public HardwareOverviewCard(bool cpu)
    {
        this.cpu = cpu; Background = OverviewTheme.Surface; BorderBrush = OverviewTheme.Border;
        BorderThickness = new(1); CornerRadius = new(8); Padding = new(14, 10, 14, 10); Margin = new(0, 0, 14, 14);
        var content = new StackPanel(); Child = content;
        var header = new DockPanel();
        selector = OverviewTheme.Command(cpu ? "CPU Usage  ⌄" : "Disk Usage  ⌄", ChooseDevice);
        selector.Padding = new(9, 4, 9, 4); selector.FontSize = 11;
        DockPanel.SetDock(selector, Dock.Right); header.Children.Add(selector);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = OverviewTheme.Text(cpu ? "\uE950" : "\uEDA2", 19, OverviewTheme.Blue); icon.FontFamily = new("Segoe MDL2 Assets"); icon.Margin = new(0, 0, 10, 0);
        title.Children.Add(icon); title.Children.Add(OverviewTheme.Text(cpu ? "CPU Usage (Custom Panel)" : "Disk Usage (Custom Panel)", 14));
        header.Children.Add(title); content.Children.Add(header);
        var chart = new Grid(); chart.Children.Add(plot); state.HorizontalAlignment = HorizontalAlignment.Center; state.IsHitTestVisible = false; chart.Children.Add(state); content.Children.Add(chart);
        var footer = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new(0, 4, 0, 0) };
        var labels = cpu ? new[] { "Usage", "Logical processors", "RAM used" } : new[] { "Active time", "Read", "Write" };
        for (int i = 0; i < labels.Length; i++)
        {
            var column = new StackPanel(); column.Children.Add(OverviewTheme.Text(labels[i], 11, OverviewTheme.Muted));
            values[i].Margin = new(0, 3, 0, 0); column.Children.Add(values[i]); footer.Children.Add(column);
        }
        content.Children.Add(footer); plot.SetSeries([(cpu ? "CPU" : "Disk", cpu ? "#229DFF" : "#AB53EA")]);
        OverviewTheme.PlotMenu(plot, "%", cpu ? "cpu-usage" : "disk-active-time");
    }

    private IEnumerable<DeviceIdentity> Devices() => sources.SelectMany(s => s.Current?.Devices ?? [])
        .Where(d => cpu ? d.Source == "windows-system" && d.Kind == DeviceKind.Cpu : d.Source == "windows-disks" && d.Kind == DeviceKind.Disk);

    private void ChooseDevice()
    {
        var menu = OverviewTheme.Menu();
        foreach (var device in Devices())
        {
            var item = OverviewTheme.MenuCommand(device.Name, () => deviceId = device.Id);
            item.IsCheckable = true; item.IsChecked = device.Id == deviceId; menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Enable this hardware source to select a device", IsEnabled = false });
        menu.PlacementTarget = selector; menu.IsOpen = true;
    }

    public void Update(IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset now, int minutes)
    {
        sources = snapshots;
        deviceId ??= Devices().FirstOrDefault()?.Id;
        var device = Devices().FirstOrDefault(d => d.Id == deviceId);
        var source = sources.FirstOrDefault(s => s.Source == (cpu ? "windows-system" : "windows-disks"));
        var history = device == null || source == null ? Array.Empty<TelemetrySample>() : source.Series(device.Id, cpu ? "cpu.usage" : "disk.busy");
        plot.Update([history], now, minutes); plot.Opacity = source?.Health == ProviderHealth.Stale ? .5 : 1;
        state.Visibility = device == null || history.All(s => s.Value == null) ? Visibility.Visible : Visibility.Collapsed;
        state.Text = device == null ? "Source not enabled · no simulated readings" : "Waiting for a measured sample";
        double? Sample(string key, DeviceKind? kind = null) => source?.Current?.Samples
            .FirstOrDefault(s => s.MetricKey == key && (kind.HasValue ? s.Device.Kind == kind : s.Device.Id == deviceId))?.Value;
        static string Format(double? value, string suffix) => value.HasValue ? $"{value:0.#}{suffix}" : "—";
        values[0].Text = Format(Sample(cpu ? "cpu.usage" : "disk.busy"), "%");
        values[1].Text = cpu ? Format(Sample("cpu.logical"), "") : Format(Sample("disk.read") / 1048576, " MiB/s");
        values[2].Text = cpu ? Format(Sample("ram.used", DeviceKind.Memory) / 1073741824, " GiB") : Format(Sample("disk.write") / 1048576, " MiB/s");
        selector.ToolTip = device?.Name ?? "Optional hardware collection is not enabled";
    }
}
