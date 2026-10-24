using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpuMonitor;

public sealed record WorkspaceSeries(string Source, string DeviceId, string MetricKey, int Axis = 0);
public sealed record WorkspacePanel(string Id, string Title, int WidthSlots, int HeightRows,
    string Chart, IReadOnlyList<WorkspaceSeries> Series);
public sealed record WorkspaceLayout(int Version, string Name, IReadOnlyList<WorkspacePanel> Panels);

/// <summary>Opt-in named JSON presets. No application layout is loaded or changed automatically.</summary>
public sealed class WorkspacePresetStore(string directory)
{
    public const int MaximumBytes = 256 * 1024;
    private readonly string root = Path.GetFullPath(directory);
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };

    public async Task SaveAsync(string presetId, WorkspaceLayout layout, CancellationToken cancellation = default)
    {
        string path = PresetPath(presetId);
        Validate(layout);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(layout, Json);
        if (bytes.Length > MaximumBytes) throw new IOException("Workspace preset exceeds 256 KiB.");
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<WorkspaceLayout> LoadAsync(string presetId, CancellationToken cancellation = default)
    {
        await using var file = new FileStream(PresetPath(presetId), FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[MaximumBytes + 1]; int length = 0, read;
        while (length < buffer.Length && (read = await file.ReadAsync(buffer.AsMemory(length), cancellation)) != 0) length += read;
        if (length > MaximumBytes) throw new IOException("Workspace preset exceeds 256 KiB.");
        var layout = JsonSerializer.Deserialize<WorkspaceLayout>(buffer.AsSpan(0, length), Json)
            ?? throw new FormatException("Workspace preset is empty.");
        Validate(layout);
        return layout;
    }

    private string PresetPath(string id)
    {
        if (!Identifier(id)) throw new ArgumentException("Preset IDs require 1–64 ASCII letters, digits, hyphens or underscores.", nameof(id));
        return Path.Combine(root, id + ".json");
    }

    public static void Validate(WorkspaceLayout layout)
    {
        if (layout == null || layout.Version != 1 || !Text(layout.Name, 80) || layout.Panels == null || layout.Panels.Count > 64)
            throw new FormatException("Unsupported or invalid workspace layout.");
        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in layout.Panels)
        {
            if (panel == null || !Identifier(panel.Id) || !panelIds.Add(panel.Id) || !Text(panel.Title, 96) ||
                panel.WidthSlots is < 1 or > 4 || panel.HeightRows is < 1 or > 8 || panel.Chart is not ("line" or "area") ||
                panel.Series == null || panel.Series.Count is < 1 or > 16)
                throw new FormatException("Invalid workspace panel or duplicate panel identity.");
            var selected = new HashSet<(string, string, string)>();
            foreach (var series in panel.Series)
                if (series == null || !Text(series.Source, 128) || !Text(series.DeviceId, 512) || !Text(series.MetricKey, 128) ||
                    series.Axis is < 0 or > 1 || !selected.Add((series.Source, series.DeviceId, series.MetricKey)))
                    throw new FormatException("Invalid or duplicate workspace series.");
        }
    }

    private static bool Identifier(string? text) => text is { Length: > 0 and <= 64 } &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum && !text.Any(char.IsControl);
}

public enum WorkspaceBindingState { SourceMissing, SourceStarting, SourceStale, DeviceMissing, MetricMissing, Unavailable, Available }
public sealed record WorkspaceSeriesResolution(WorkspaceSeries Selection, string? Unit, WorkspaceBindingState State);
public sealed record WorkspacePanelResolution(WorkspacePanel Panel, IReadOnlyList<WorkspaceSeriesResolution> Series, string? AxisError);

public static class WorkspaceResolver
{
    /// <summary>Retains original selections when providers/devices disappear. Never substitutes another display index.</summary>
    public static IReadOnlyList<WorkspacePanelResolution> Resolve(WorkspaceLayout layout, IReadOnlyList<ProviderSnapshot> snapshots)
    {
        WorkspacePresetStore.Validate(layout);
        var sources = snapshots.ToDictionary(s => s.Source, StringComparer.Ordinal);
        WorkspaceSeriesResolution ResolveSeries(WorkspaceSeries selection)
        {
            if (!sources.TryGetValue(selection.Source, out var source)) return new(selection, null, WorkspaceBindingState.SourceMissing);
            var definition = source.Definitions.SingleOrDefault(d => d.Key == selection.MetricKey);
            var state = source.Health switch
            {
                ProviderHealth.Starting => WorkspaceBindingState.SourceStarting,
                ProviderHealth.Stale => WorkspaceBindingState.SourceStale,
                _ when definition == null => WorkspaceBindingState.MetricMissing,
                _ when source.Current?.Devices.Any(d => d.Id == selection.DeviceId) != true => WorkspaceBindingState.DeviceMissing,
                _ when source.Current?.Samples.Any(s => s.Device.Id == selection.DeviceId && s.MetricKey == selection.MetricKey && s.Value.HasValue) == true => WorkspaceBindingState.Available,
                _ => WorkspaceBindingState.Unavailable
            };
            return new(selection, definition?.Unit, state);
        }
        return Array.AsReadOnly(layout.Panels.Select(panel =>
        {
            var series = panel.Series.Select(ResolveSeries).ToArray();
            var conflict = series.Where(s => s.Unit != null).GroupBy(s => s.Selection.Axis)
                .FirstOrDefault(axis => axis.Select(s => s.Unit).Distinct(StringComparer.Ordinal).Count() > 1);
            return new WorkspacePanelResolution(panel, Array.AsReadOnly(series), conflict == null ? null :
                $"Axis {conflict.Key} contains incompatible units: {string.Join(", ", conflict.Select(s => s.Unit).Distinct(StringComparer.Ordinal))}.");
        }).ToArray());
    }
}
