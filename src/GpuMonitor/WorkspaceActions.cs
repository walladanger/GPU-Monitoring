namespace GpuMonitor;

/// <summary>Atomic layout transformations. Sensor identities and raw telemetry are never rewritten.</summary>
public static class WorkspaceActions
{
    public static WorkspaceLayout MoveTo(WorkspaceLayout layout, string sourceId, string targetId, bool after)
    {
        WorkspacePresetStore.Validate(layout);
        var source = Find(layout, sourceId); Find(layout, targetId);
        if (sourceId == targetId) return layout;
        var panels = layout.Panels.Where(p => p.Id != sourceId).ToList();
        int target = panels.FindIndex(p => p.Id == targetId);
        panels.Insert(target + (after ? 1 : 0), source);
        return panels.Select(p => p.Id).SequenceEqual(layout.Panels.Select(p => p.Id)) ? layout : Checked(layout, panels);
    }

    public static WorkspacePanel Resize(WorkspacePanel original, double horizontal, double vertical, double slotWidth, int visibleColumns)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical) || !double.IsFinite(slotWidth) || slotWidth <= 0 || visibleColumns < 1)
            throw new ArgumentException("Invalid resize geometry.");
        WorkspacePresetStore.Validate(new(1, "Resize validation", [original]));
        int columns = (int)Math.Round(Math.Clamp(horizontal / slotWidth, -8, 8), MidpointRounding.AwayFromZero);
        int rows = (int)Math.Round(Math.Clamp(vertical / 100, -8, 8), MidpointRounding.AwayFromZero);
        return original with { WidthSlots = columns == 0 ? original.WidthSlots : Math.Clamp(Math.Min(original.WidthSlots, visibleColumns) + columns, 1, 4),
            HeightRows = Math.Clamp(original.HeightRows + rows, 1, 8) };
    }

    public static WorkspaceLayout Merge(WorkspaceLayout layout, string sourceId, string targetId, IReadOnlyList<ProviderSnapshot> sources)
    {
        WorkspacePresetStore.Validate(layout);
        if (sourceId == targetId) throw new ArgumentException("Choose two different panels to merge.");
        var source = Find(layout, sourceId); var target = Find(layout, targetId);
        var series = target.Series.Concat(source.Series).DistinctBy(s => (s.Source, s.DeviceId, s.MetricKey)).ToArray();
        if (source.Series.Concat(target.Series).Any(s => s.Axis != 0)) throw new FormatException("Merge currently supports primary-axis panels only.");
        if (series.Length > 16) throw new FormatException("A merged panel can contain at most 16 distinct sensors.");
        var merged = target with { Series = series, WidthSlots = Math.Min(4, source.WidthSlots + target.WidthSlots), HeightRows = Math.Max(source.HeightRows, target.HeightRows) };
        var resolved = WorkspaceResolver.Resolve(new(1, "Merge validation", [merged]), sources).Single();
        if (resolved.Series.Any(s => s.Unit == null)) throw new FormatException("Connect the sources so their units can be checked before merging.");
        if (resolved.AxisError != null) throw new FormatException(resolved.AxisError);
        return Checked(layout, layout.Panels.Where(p => p.Id != sourceId).Select(p => p.Id == targetId ? merged : p));
    }

    public static WorkspaceLayout Split(WorkspaceLayout layout, string id)
    {
        WorkspacePresetStore.Validate(layout); var panel = Find(layout, id);
        if (panel.Series.Count < 2) throw new FormatException("This panel has only one sensor.");
        if (panel.Series.Any(s => s.Axis != 0)) throw new FormatException("Split currently supports primary-axis panels only.");
        if (layout.Panels.Count + panel.Series.Count - 1 > 64) throw new FormatException("Splitting would exceed the 64-panel limit.");
        var pieces = panel.Series.Select((series, index) => panel with {
            Id = index == 0 ? panel.Id : Guid.NewGuid().ToString("N"), WidthSlots = 1,
            Title = Short(panel.Title + " · " + series.MetricKey), Series = new[] { series } }).ToArray();
        return Checked(layout, layout.Panels.SelectMany(p => p.Id == id ? pieces : new[] { p }));
    }

    public static WorkspaceLayout Duplicate(WorkspaceLayout layout, string id)
    {
        WorkspacePresetStore.Validate(layout); var panel = Find(layout, id);
        if (layout.Panels.Count == 64) throw new FormatException("The workspace already contains 64 panels.");
        var copy = panel with { Id = Guid.NewGuid().ToString("N"), Title = Short(panel.Title, 91) + " copy", Series = panel.Series.ToArray() };
        return Checked(layout, layout.Panels.SelectMany(p => p.Id == id ? new[] { p, copy } : new[] { p }));
    }

    private static WorkspacePanel Find(WorkspaceLayout layout, string id) => layout.Panels.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("The selected panel no longer exists.");
    private static WorkspaceLayout Checked(WorkspaceLayout layout, IEnumerable<WorkspacePanel> panels)
    {
        var next = layout with { Panels = Array.AsReadOnly(panels.ToArray()) };
        WorkspacePresetStore.Validate(next); return next;
    }
    private static string Short(string text, int limit = 96) => text.Length <= limit ? text : text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
}
