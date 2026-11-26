using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GpuMonitor;

/// <summary>Explicit opt-in sources. Editing never polls an endpoint. Changes
/// take effect on the next launch, preserving the active collector lifecycle.</summary>
internal sealed class SourceSettingsWindow : Window
{
    private readonly Settings settings;
    private readonly Func<IReadOnlyList<ProviderSnapshot>> snapshots;
    private readonly Action saveSettings;
    private readonly StackPanel rows = new();
    private readonly TextBlock status = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly TextBlock live = OverviewTheme.Text("", 12, OverviewTheme.Muted);
    private readonly CheckBox cores = new() { Content = "Collect per-logical-CPU channels", Foreground = Brushes.White, Margin = new(0, 8, 0, 8) };
    private readonly CheckBox processes = new() { Content = "Collect Windows and NVIDIA compute processes", Foreground = Brushes.White, Margin = new(0, 0, 0, 12) };
    private readonly List<(RuntimeSourceConfiguration Value, CheckBox Enabled, TextBox Endpoint, bool Configured)> runtimes = [];
    private readonly List<(MetadataFeedConfiguration Value, CheckBox Enabled, TextBox Path)> feeds = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CollectorConfiguration configuration = new();

    public SourceSettingsWindow(Window owner, Settings settings, Func<IReadOnlyList<ProviderSnapshot>> snapshots, Action saveSettings)
    {
        Owner = owner; this.settings = settings; this.snapshots = snapshots; this.saveSettings = saveSettings;
        Title = "Monitoring sources"; Width = 760; Height = 780; MinWidth = 560; MinHeight = 400;
        Background = OverviewTheme.Canvas; Foreground = Brushes.White; FontFamily = new("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new(0, 12, 0, 8); footer.Children.Add(status);
        var actions = new WrapPanel(); footer.Children.Add(actions);
        actions.Children.Add(OverviewTheme.Command("Import configuration…", Import));
        actions.Children.Add(OverviewTheme.Command("Save for next launch", Save));
        actions.Children.Add(OverviewTheme.Command("Use hardware defaults", () => { settings.SourceConfigurationPath = null; saveSettings(); status.Text = "Hardware defaults saved. Optional sources will be disabled on the next launch."; }));
        actions.Children.Add(OverviewTheme.Command("Close", Close));
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        try
        {
            if (settings.SourceConfigurationPath is { Length: > 0 } path)
            {
                var candidate = CollectorConfiguration.Load(path);
                using var validation = BackendProviderSet.Create(candidate);
                configuration = candidate;
            }
        }
        catch (Exception error) { configuration = new(); status.Text = "Saved sources unavailable; using hardware defaults: " + error.Message; }
        Populate(); timer.Tick += (_, _) => UpdateHealth(); Loaded += (_, _) => { UpdateHealth(); timer.Start(); }; Closed += (_, _) => timer.Stop();
    }

    private void Populate()
    {
        rows.Children.Clear(); runtimes.Clear(); feeds.Clear();
        rows.Children.Add(OverviewTheme.Text("Active source health", 17)); live.TextWrapping = TextWrapping.Wrap; live.Margin = new(0, 8, 0, 14); rows.Children.Add(live);
        var explanation = OverviewTheme.Text("Optional runtimes and metadata feeds are read only and explicitly enabled here. Save, then restart GPU Monitor to apply. Active charts keep running while you edit. Endpoints are scraped only after restart. Prompts and message bodies are excluded by the adapters.", 13, OverviewTheme.Muted);
        explanation.TextWrapping = TextWrapping.Wrap; rows.Children.Add(explanation);
        cores.IsChecked = configuration.CollectCpuCores; processes.IsChecked = configuration.CollectProcesses; rows.Children.Add(cores); rows.Children.Add(processes);
        var configuredRuntimeIds = configuration.Runtimes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var runtimeList = configuration.Runtimes.ToList();
        foreach (var (kind, endpoint) in new[] { ("sglang", RuntimeMetricsCollector.DefaultSglangEndpoint), ("vllm", RuntimeMetricsCollector.DefaultVllmEndpoint), ("llamacpp", RuntimeMetricsCollector.DefaultLlamaCppEndpoint) })
            if (!runtimeList.Any(r => r.Kind == kind) && runtimeList.Count + configuration.Feeds.Count < 16) runtimeList.Add(new() { Id = kind + "-local", Kind = kind, Endpoint = endpoint.AbsoluteUri });
        rows.Children.Add(OverviewTheme.Text("Inference runtimes", 16));
        foreach (var item in runtimeList)
        {
            var enabled = Toggle(item.Kind + " · " + item.Id, item.Enabled); var endpoint = Input(item.Endpoint, item.Kind + " metrics URL");
            rows.Children.Add(enabled); rows.Children.Add(endpoint); runtimes.Add((item, enabled, endpoint, configuredRuntimeIds.Contains(item.Id)));
        }
        var feedList = configuration.Feeds.ToList();
        foreach (string kind in new[] { "agui", "usage", "otlp", "tokenmonitor" })
            if (!feedList.Any(f => f.Kind == kind) && runtimeList.Count + feedList.Count < 16) feedList.Add(new() { Id = kind + "-local", Kind = kind });
        rows.Children.Add(OverviewTheme.Text("Local metadata feeds", 16));
        rows.Children.Add(OverviewTheme.Text("AG-UI / usage / OTLP JSONL · Token Monitor auto-export JSON", 12, OverviewTheme.Muted));
        foreach (var item in feedList)
        {
            var enabled = Toggle(item.Kind + " · " + item.Id, item.Enabled); var path = Input(item.Path, item.Kind + " feed path");
            rows.Children.Add(enabled);
            var row = new DockPanel(); var browse = OverviewTheme.Command("Browse…", () => { var dialog = new OpenFileDialog { Filter = "Metadata feeds|*.jsonl;*.ndjson;*.json|All files|*.*" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; });
            DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(path); rows.Children.Add(row); feeds.Add((item, enabled, path));
        }
    }
    private static CheckBox Toggle(string name, bool value) => new() { Content = name, IsChecked = value, Foreground = Brushes.White, Margin = new(0, 12, 0, 4) };
    private static TextBox Input(string value, string name)
    {
        var box = new TextBox { Text = value, Foreground = Brushes.White, Background = OverviewTheme.Surface, BorderBrush = OverviewTheme.Border, Padding = new(7), Margin = new(0, 0, 0, 6), MaxLength = 4096 };
        System.Windows.Automation.AutomationProperties.SetName(box, name); return box;
    }
    private void UpdateHealth()
    {
        live.Text = string.Join("\n", snapshots().Select(s => $"{s.Source} · {s.Health} · {s.LastSuccess?.ToLocalTime().ToString("HH:mm:ss") ?? "No reading"}" + (s.Error == null ? "" : " · " + s.Error)));
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Source configuration|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var candidate = CollectorConfiguration.Load(dialog.FileName); using var validation = BackendProviderSet.Create(candidate);
            configuration = candidate; Populate(); status.Text = "Configuration loaded for editing. Save to use it on the next launch.";
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void Save()
    {
        try
        {
            var candidate = BuildConfiguration();
            using var validation = BackendProviderSet.Create(candidate);
            string json = JsonSerializer.Serialize(candidate, new JsonSerializerOptions { WriteIndented = true });
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 65536) throw new FormatException("Source configuration exceeds 64 KiB.");
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.FilePath)!, "sources.json");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, path, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            settings.SourceConfigurationPath = path; saveSettings();
            status.Text = "Saved. Restart GPU Monitor to apply these sources. Then add their measured sensors from Add monitoring panel. Activity feeds appear in Activity.";
        }
        catch (Exception error) { status.Text = "Sources were not applied: " + error.Message; }
    }
    internal CollectorConfiguration BuildConfiguration()
    {
        var candidate = new CollectorConfiguration { CollectProcesses = processes.IsChecked == true, CollectCpuCores = cores.IsChecked == true,
            Runtimes = runtimes.Where(r => r.Configured || r.Enabled.IsChecked == true).Select(r => new RuntimeSourceConfiguration { Enabled = r.Enabled.IsChecked == true, Id = r.Value.Id, Kind = r.Value.Kind, Endpoint = r.Endpoint.Text.Trim(), Metrics = r.Value.Metrics }).ToList(),
            Feeds = feeds.Select(f => new MetadataFeedConfiguration { Enabled = f.Enabled.IsChecked == true, Id = f.Value.Id, Kind = f.Value.Kind, Path = f.Path.Text.Trim() }).Where(f => f.Enabled || f.Path.Length > 0).ToList() };
        return candidate;
    }
    internal void VerifyRuntime(string kind, bool enabled, string endpoint)
    { var row = runtimes.First(r => r.Value.Kind == kind); row.Enabled.IsChecked = enabled; row.Endpoint.Text = endpoint; }
}
