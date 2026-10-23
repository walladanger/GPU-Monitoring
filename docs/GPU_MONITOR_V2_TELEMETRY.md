# Phase 02 — telemetry contract increment

Based on the Phase 00 branch (`b74c2aa`). This increment is additive: the v1 WPF
dashboard still calls `NvidiaCollector.ReadAsync` and keeps its existing settings,
inventory, stale state and timer. No visual redesign is included.

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

Example consumption (the existing UI remains on its legacy call until chart migration):

```csharp
ITelemetryProvider provider = new NvidiaCollector();
TelemetryBatch snapshot = await provider.ReadAsync(cancellationToken);
```

Collection failures still throw through the interface, including cancellation and
the existing eight-second timeout. Consumers must keep the last successful
snapshot, mark it stale, and retry. A **successful** empty batch is zero devices;
an exception must never be translated into zero devices. This increment adds no
new provider-health coordinator, persistent database or background service.

Buffers belong to one `(Source, Device.Id, MetricKey)` tuple. Changing a display
index does not change series identity. Keep access on the owning thread; the buffer
is not synchronized. Consumers create/remove buffers as inventory changes and own
their total series-count limit. This increment does not yet wire these buffers into
the v1 dashboard or implement a multi-provider history registry.

## Evidence and remaining tasks

`dotnet run --project tests/GpuMonitor.TelemetryChecks -c Release` passes **51**
checks: the original 20 baseline checks plus 31 provider normalization, finite-value,
identity, gap, circular-buffer, retention, timestamp and snapshot checks. Fixtures
cover 0/1/2/4/8 devices with deterministic timestamps and values. WPF Release
cross-build on Linux passes with zero warnings/errors. Windows runtime tests and
physical hardware validation are still pending; there is no native screenshot yet.

Master tasks 02.01–02.06 and 02.08–02.10 have implementation/regression evidence.
02.07 retains the original timeout/stale/retry path by source inspection; no live
Windows outage test was performed. 02.11's UI-independent provider boundary exists,
but a new collection coordinator operating without any dashboard has not been
implemented. Do not mark all of Phase 02 complete.

Related legacy issue: [#5](https://github.com/walladanger/GPU-Monitoring/issues/5).
Merge the Phase 00 checks first; this track does not require a shell/style merge.
