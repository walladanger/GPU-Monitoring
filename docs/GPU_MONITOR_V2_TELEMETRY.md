# Phase 02 — telemetry contract increment

Based on the Phase 00 branch. The WPF dashboard now polls `ITelemetryProvider`
through `TelemetrySession` and draws from normalized per-series history. Existing
settings, GPU filtering, CSV export and the single polling timer remain in place.
No visual redesign is included.

## Implemented

- `DeviceIdentity`: provider + stable UUID/OS ID, kind, name, display index and optional bus.
- `MetricDefinition`: source, key, title, unit and optional scale.
- `TelemetrySample`: source device, metric, UTC timestamp and finite numeric value
  or explicit missing-value gap. Negative finite values are permitted for future
  signed metrics; the NVIDIA parser retains its existing nonnegative policy.
- `MetricCapability`: per-device available/unavailable status for the current
  snapshot. NVIDIA N/A does not establish permanent lack of hardware support.
- `TelemetryEvent`: discrete activity metadata and optional correlation/duration;
  no prompts, message bodies or tool arguments.
- `ITelemetryProvider`: metric discovery and asynchronous read-only snapshots.
- `NvidiaCollector` explicitly implements the interface using the **same existing
  subprocess read**. No extra process, polling loop, daemon or runtime dependency.
- `NvidiaTelemetry.Normalize` preserves exact legacy metric values/units and
  device count, retains the timestamp's instant in UTC and rejects duplicate UUIDs.
- `TelemetrySeriesBuffer`: fixed-capacity circular buffer with per-series identity,
  thirty-minute default retention, explicit gaps, immutable snapshots and rejection
  of duplicate/late timestamps. `Prune(now)` can expire history during outages.

Example provider consumption:

```csharp
ITelemetryProvider provider = new NvidiaCollector();
TelemetryBatch snapshot = await provider.ReadAsync(cancellationToken);
```

Collection failures still throw through the interface, including cancellation and
the existing eight-second timeout. Consumers must keep the last successful
snapshot, mark it stale, and retry. A **successful** empty batch is zero devices;
an exception must never be translated into zero devices.

`TelemetrySession` now owns Starting/Live/Stale health, the last successful
snapshot, successful poll time and error. It uses the existing provider timeout,
propagates caller cancellation, and retains device inventory when a poll fails.
Failed polls insert explicit missing-value samples into each active series so
charts do not connect measurements across an outage. Recovery retains those
gaps and UUID histories; a successful empty snapshot clears inventory/history.
History is pruned on success, failure and redraw, including during outages.
The session can run without WPF; it has no timer or background service of its own.

Buffers belong to one `(Source, Device.Id, MetricKey)` tuple. Changing a display
index does not change series identity. Keep access on the owning thread; the buffer
is not synchronized. The session bounds its histories to active device IDs and
advertised metric keys; each series retains the existing 3600-sample/30-minute cap.
The dashboard uses a single NVIDIA session. A multi-provider registry and optional
runtime/agent integrations remain future work.

`NvidiaSnapshot` projects normalized values back to the existing GPU cards and
raw-reading CSV exporter. Charts consume normalized history directly. CSV retains
its existing schema and exports successful raw readings; synthetic outage gaps
are currently chart-only and are not added to that legacy CSV.

## Evidence and remaining tasks

`dotnet run --project tests/GpuMonitor.TelemetryChecks -c Release` passes **64**
checks: the original 51 checks plus 13 session integration checks covering live,
stale, timeout, cancellation, recovery, removal, projection and invalid batches. Fixtures
cover 0/1/2/4/8 devices with deterministic timestamps and values. WPF Release
cross-build on Linux passes with zero warnings/errors. Windows runtime tests and
physical hardware validation are still pending; there is no native screenshot yet.

Master tasks 02.01–02.06 and 02.08–02.10 have implementation/regression evidence.
02.07 now also has fixture evidence for provider failures, timeout, retry/recovery
and caller cancellation. 02.11 has a UI-independent session used by the running
dashboard's polling path. Native Windows outage and physical GPU tests remain
release gates; do not treat portable fixtures as live hardware validation.

Related legacy issue: [#5](https://github.com/walladanger/GPU-Monitoring/issues/5).
Merge the Phase 00 checks first; this track does not require a shell/style merge.
