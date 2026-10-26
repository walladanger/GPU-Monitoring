using GpuMonitor;

internal static class WorkspaceActionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var a = new WorkspaceSeries("nvidia-smi", "GPU-A", "utilization.gpu");
        var b = a with { DeviceId = "GPU-B" };
        var layout = new WorkspaceLayout(1, "Panels", [new("a", "GPU A", 1, 2, "line", [a]),
            new("b", "GPU B", 1, 3, "area", [b]), new("c", "Another A", 1, 2, "line", [a])]);
        check(WorkspaceActions.MoveTo(layout, "c", "a", false).Panels.Select(p => p.Id).SequenceEqual(new[] { "c", "a", "b" }) &&
            WorkspaceActions.MoveTo(layout, "a", "c", true).Panels.Select(p => p.Id).SequenceEqual(new[] { "b", "c", "a" }),
            "Title drops support before/after insertion without changing other panel order");
        check(ReferenceEquals(WorkspaceActions.MoveTo(layout, "a", "b", false), layout),
            "An unchanged drop does not create a false layout change");
        var resized = WorkspaceActions.Resize(layout.Panels[0], 510, 100, 500, 2);
        check(resized.WidthSlots == 2 && resized.HeightRows == 3 && resized.Series.Single() == a,
            "Resize snaps to panel slots/rows while retaining sensor identity");
        check(WorkspaceActions.Resize(resized, -100000, 100000, 500, 2) is { WidthSlots: 1, HeightRows: 8 },
            "Large resize deltas remain within persisted layout bounds");
        var wide = layout.Panels[0] with { WidthSlots = 4 };
        check(WorkspaceActions.Resize(wide, 0, 100, 500, 2).WidthSlots == 4 &&
            WorkspaceActions.Resize(wide, -510, 0, 500, 2).WidthSlots == 1,
            "Vertical resizing preserves a hidden width preference; horizontal resizing starts from the visible span");
        bool invalid = false;
        try { WorkspaceActions.Resize(wide, double.NaN, 0, 500, 2); } catch (ArgumentException) { invalid = true; }
        check(invalid, "Nonfinite gesture coordinates cannot corrupt saved dimensions");

        var definition = new MetricDefinition("nvidia-smi", "utilization.gpu", "GPU usage", "%", 100);
        ProviderSnapshot[] sources = [new("nvidia-smi", ProviderHealth.Stale, "offline", null, null, [], [definition])];
        var merged = WorkspaceActions.Merge(layout, "a", "b", sources);
        var target = merged.Panels.Single(p => p.Id == "b");
        check(merged.Panels.Count == 2 && target.Title == "GPU B" && target.Chart == "area" &&
            target.Series.SequenceEqual(new[] { b, a }) && target.WidthSlots == 2 && target.HeightRows == 3,
            "Merge retains target identity/style and both measured selections even when hardware is stale");
        var deduplicated = WorkspaceActions.Merge(layout, "a", "c", sources);
        check(deduplicated.Panels.Single(p => p.Id == "c").Series.Count == 1 && layout.Panels.Count == 3,
            "Merge deduplicates a physical series without mutating the input layout");
        invalid = false;
        try { WorkspaceActions.Merge(layout, "a", "b", []); } catch (FormatException) { invalid = true; }
        check(invalid && layout.Panels.Count == 3, "Unavailable unit definitions prevent an unsafe merge without removing panels");
        var mixed = layout with { Panels = [layout.Panels[0], layout.Panels[1] with { Series = [b with { MetricKey = "power.draw" }] }] };
        invalid = false;
        try { WorkspaceActions.Merge(mixed, "a", "b", [sources[0] with { Definitions = [definition, new("nvidia-smi", "power.draw", "Power", "W")] }]); }
        catch (FormatException) { invalid = true; }
        check(invalid, "Merge refuses incompatible units rather than silently overlaying them");
        var split = WorkspaceActions.Split(merged, "b");
        check(split.Panels.Count == 3 && split.Panels[0].Id == "b" && split.Panels.Take(2).All(p => p.Series.Count == 1) &&
            split.Panels.Take(2).SelectMany(p => p.Series).SequenceEqual(target.Series),
            "Split keeps each original binding and retains the first panel identity");
        var copy = WorkspaceActions.Duplicate(layout, "a");
        check(copy.Panels.Count == 4 && copy.Panels[1].Id != "a" && copy.Panels[1].Series.SequenceEqual(layout.Panels[0].Series),
            "Duplicate creates an independent panel identity with the same sensor bindings");
        var full = layout with { Panels = Enumerable.Range(0, 64).Select(i => layout.Panels[0] with { Id = "panel-" + i }).ToArray() };
        invalid = false;
        try { WorkspaceActions.Duplicate(full, "panel-0"); } catch (FormatException) { invalid = true; }
        check(invalid && full.Panels.Count == 64, "Duplicate cannot exceed the panel limit or change an existing full workspace");
        full = full with { Panels = full.Panels.Select((p, i) => i == 0 ? p with { Series = [a, b] } : p).ToArray() };
        invalid = false;
        try { WorkspaceActions.Split(full, "panel-0"); } catch (FormatException) { invalid = true; }
        check(invalid && full.Panels[0].Series.Count == 2, "A split that exceeds the panel limit leaves its original sensor group intact");
        var tooMany = layout with { Panels = [layout.Panels[0] with { Series = Enumerable.Range(0, 9).Select(i => a with { DeviceId = "GPU-" + i }).ToArray() },
            layout.Panels[1] with { Series = Enumerable.Range(9, 8).Select(i => a with { DeviceId = "GPU-" + i }).ToArray() }] };
        invalid = false;
        try { WorkspaceActions.Merge(tooMany, "a", "b", sources); } catch (FormatException) { invalid = true; }
        check(invalid && tooMany.Panels.Count == 2, "Merge rejects more than sixteen selected sensors before removing either panel");
    }
}
