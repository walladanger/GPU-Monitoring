using System.Text.Json;
using GpuMonitor;

internal static class WorkspaceLayoutChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var selection = new WorkspaceSeries("nvidia-smi", "GPU-A", "utilization.gpu");
        var layout = new WorkspaceLayout(1, "Dual GPU workspace", [
            new("gpu-a", "Selected GPU", 2, 3, "line", [selection]),
            new("gpu-b", "Other GPU", 1, 2, "area", [selection with { DeviceId = "GPU-B" }]) ]);
        string directory = Path.Combine(Path.GetTempPath(), "gpu-layout-" + Guid.NewGuid().ToString("N"));
        var store = new WorkspacePresetStore(directory);
        try
        {
            await store.SaveAsync("dual-3090", layout);
            check(store.ListPresets().SequenceEqual(new[] { "dual-3090" }), "Preset menus discover validated saved names from the explicit local directory");
            var restored = await store.LoadAsync("dual-3090");
            check(restored.Panels.Select(p => p.Id).SequenceEqual(new[] { "gpu-a", "gpu-b" }) &&
                restored.Panels[0].WidthSlots == 2 && restored.Panels[0].HeightRows == 3 && restored.Panels[0].Series.Single() == selection,
                "Named workspace JSON round-trips order, dimensions and stable source/device/metric bindings");
            bool rejected = false;
            string outside = "outside-" + Guid.NewGuid().ToString("N");
            try { await store.SaveAsync("../" + outside, layout); } catch (ArgumentException) { rejected = true; }
            check(rejected && !File.Exists(Path.Combine(directory, "..", outside + ".json")), "Workspace preset names cannot traverse outside their directory");
            rejected = false;
            try { await store.SaveAsync("dual-3090", layout with { Version = 2 }); } catch (FormatException) { rejected = true; }
            check(rejected && (await store.LoadAsync("dual-3090")).Version == 1,
                "An invalid future layout cannot overwrite a valid saved preset");
            rejected = false;
            try { await store.SaveAsync("dual-3090", layout with { Name = "Cancelled" }, new CancellationToken(true)); } catch (OperationCanceledException) { rejected = true; }
            check(rejected && (await store.LoadAsync("dual-3090")).Name == layout.Name,
                "Cancelled preset save preserves the existing named layout");
            rejected = false;
            try { WorkspacePresetStore.Validate(layout with { Panels = [layout.Panels[0], layout.Panels[0]] }); } catch (FormatException) { rejected = true; }
            check(rejected, "Duplicate panel identities are rejected");
            rejected = false;
            try { WorkspacePresetStore.Validate(layout with { Panels = [layout.Panels[0] with { Series = [selection, selection with { Axis = 1 }] }] }); } catch (FormatException) { rejected = true; }
            check(rejected, "A physical series cannot be counted twice in one panel on different axes");
            await File.WriteAllTextAsync(Path.Combine(directory, "unknown.json"), "{\"Version\":1,\"Name\":\"Test\",\"Panels\":[],\"Surprise\":true}");
            rejected = false;
            try { await store.LoadAsync("unknown"); } catch (JsonException) { rejected = true; }
            check(rejected, "Workspace import rejects unknown fields instead of silently changing intended layout");
            await File.WriteAllBytesAsync(Path.Combine(directory, "oversized.json"), new byte[WorkspacePresetStore.MaximumBytes + 1]);
            rejected = false;
            try { await store.LoadAsync("oversized"); } catch (IOException) { rejected = true; }
            check(rejected && Directory.GetFiles(directory, "*.tmp").Length == 0, "Workspace import is bounded and completed saves leave no temporary files");

            var provider = new NvidiaCollector(); var session = new TelemetrySession(provider);
            var time = DateTimeOffset.UtcNow;
            string Row(string index, string id) => $"{index}, NVIDIA fixture, GPU-{id}, bus, 24576, 40, 8192, 65, 300, 62, 1500, 9750";
            ProviderSnapshot Snapshot() => new(provider.SourceId, session.Health, session.Error, session.LastSuccess,
                session.Current, session.HistorySnapshot(), provider.Definitions);
            session.Apply(NvidiaTelemetry.Normalize(CsvTelemetry.Parse(Row("9", "A"), time)), time);
            var resolved = WorkspaceResolver.Resolve(layout, [Snapshot()]);
            check(resolved[0].Series.Single().State == WorkspaceBindingState.Available &&
                resolved[1].Series.Single().State == WorkspaceBindingState.DeviceMissing,
                "Workspace restore follows GPU UUID across index changes and retains an absent device binding");
            check(WorkspaceResolver.Resolve(layout, [])[0].Series.Single().State == WorkspaceBindingState.SourceMissing,
                "A missing optional provider yields an unresolved binding without fabricated telemetry");
            session.MarkStale("fixture offline", time.AddSeconds(1));
            check(WorkspaceResolver.Resolve(layout, [Snapshot()])[0].Series.Single().State == WorkspaceBindingState.SourceStale,
                "A stale source cannot make a restored series appear live");
            session.Apply(NvidiaTelemetry.Normalize(CsvTelemetry.Parse(Row("0", "A") + "\n" + Row("1", "B"), time.AddSeconds(2))), time.AddSeconds(2));
            check(WorkspaceResolver.Resolve(layout, [Snapshot()]).All(p => p.Series.Single().State == WorkspaceBindingState.Available),
                "Reappearing hardware resolves the original binding without rewriting the preset");
            check(WorkspaceResolver.Resolve(layout, [Snapshot() with { Health = ProviderHealth.Starting }])[0].Series.Single().State == WorkspaceBindingState.SourceStarting &&
                WorkspaceResolver.Resolve(layout, [Snapshot() with { Definitions = [] }])[0].Series.Single().State == WorkspaceBindingState.MetricMissing,
                "Starting sources and missing metric definitions remain distinct restore states");
            var unavailable = Snapshot();
            unavailable = unavailable with { Current = unavailable.Current! with {
                Samples = unavailable.Current!.Samples.Select(s => new TelemetrySample(s.Device, s.MetricKey, s.Time, null)).ToArray() } };
            check(WorkspaceResolver.Resolve(layout, [unavailable])[0].Series.Single().State == WorkspaceBindingState.Unavailable,
                "A restored metric with no measured value remains unavailable rather than zero");
            var mixed = layout with { Panels = [layout.Panels[0] with { Series = [selection, selection with { MetricKey = "power.draw" }] }] };
            check(WorkspaceResolver.Resolve(mixed, [Snapshot()]).Single().AxisError != null,
                "Different physical units on one axis are reported rather than combined misleadingly");
            mixed = mixed with { Panels = [mixed.Panels[0] with { Series = [selection, selection with { MetricKey = "power.draw", Axis = 1 }] }] };
            check(WorkspaceResolver.Resolve(mixed, [Snapshot()]).Single().AxisError == null,
                "A separately labeled secondary axis can represent a different unit");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
