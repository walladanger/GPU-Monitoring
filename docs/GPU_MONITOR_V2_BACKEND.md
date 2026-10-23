# Backend engine and GUI integration

The backend can now run without creating a WPF Application, dashboard, tray icon or UI mutex. GUI styling is unchanged by this branch.

Disk/process collection and optional inference/agent/OTel adapters are now implemented. See [adapter setup and source contracts](GPU_MONITOR_V2_ADAPTERS.md) and [disabled-by-default configuration](backend-sources.example.json) for the current provider factory and four export files. The original three-file example below describes the first backend increment; `events.csv` is now additionally exported.

## Running without the GUI

From PowerShell, wait for the Windows executable and inspect its exit status:

```powershell
$p = Start-Process .\GpuMonitor.exe -ArgumentList @('--collect', 'C:\GpuCapture', '--duration', '30', '--interval', '2') -Wait -PassThru
$p.ExitCode
```

Use a new output directory for each run. Paths containing spaces must be quoted within the argument passed to Start-Process. Duration is 1–86400 seconds; interval is 1–10 seconds. Default duration is 30 seconds and interval is 2 seconds. Three files are written on completion: telemetry.json (health, definitions/units, inventory, capabilities, latest values and retained history), telemetry.csv (raw timestamped values; missing values are empty), and telemetry.txt (provider health, errors and inventory). Each file is replaced atomically, but the three-file set is not a transactional archive. Output replaces an existing file with the same name. Collection history is capped at 3600 samples/series and 30 minutes; this is not unlimited recording.

Exit 0 means at least one provider is live, 1 means none are live, 2 means arguments or output failed. Inspect individual provider health; exit 0 does not mean NVIDIA is available. A provider still waiting on its first read is Starting, not Live. No HTTP listener, upload, administrator prompt, remote endpoint or optional AI service is created.

## Connecting a replacement GUI

```csharp
var engine = new TelemetryEngine(new ITelemetryProvider[] {
    new NvidiaCollector(), new WindowsSystemCollector(),
    new NetworkCollector(), new VolumeCollector()
});
using var stop = new CancellationTokenSource();
Task collection = engine.RunAsync(TimeSpan.FromSeconds(2), stop.Token);
// From a UI timer: var sources = engine.Snapshot();
// Dispatch rendering onto the UI thread. Do not do provider I/O in render callbacks.
// On shutdown: stop.Cancel(); await collection, catching OperationCanceledException.
```

Each provider has its own worker loop, lock, current snapshot, error and last-success time. A failed or slow NVIDIA poll cannot stop CPU/RAM/network collection. Snapshot collections are copied/read-only and may be read across threads. The engine rejects a second simultaneous RunAsync. Providers must honor cancellation and bound their own external I/O; arbitrary third-party blocking code cannot be force-stopped safely. NVIDIA retains its existing eight-second timeout and process-tree cancellation.

Key by (Source, Device.Id, MetricKey), never display index or display name. Use Definitions for units and Current.Capabilities for availability. First rate samples and reset counters are unavailable, not zero. Histories remain bounded and expire even during outages. Provider failures retain inventory and insert gaps. Successful removal drops only removed-device histories. The existing GUI keeps its existing single-provider polling path until the replacement GUI explicitly adopts the engine.

## Implemented sources

| Source | Actual measurements | Identity / limitations |
| --- | --- | --- |
| nvidia-smi | GPU utilization, used/total VRAM, core temperature, power, fan and clocks | GPU UUID; existing collector and fallback behavior retained |
| windows-system | CPU model, logical processor count, total CPU busy percentage, total/available/used physical RAM | CPU and physical-memory identities; CPU first reading unavailable; total CPU unavailable above 64 logical processors to avoid reporting one processor group as the whole machine |
| network | Receive/send bytes per second, separately per non-loopback interface | OS interface ID; monotonic elapsed time; disconnected interfaces and counter resets yield gaps |
| volumes | Total/free/used bytes of mounted fixed logical volumes | Windows volume GUID, not drive letter; unresolved GUIDs skipped; inaccessible capacity unavailable |

CPU usage uses GetSystemTimes (kernel includes idle), RAM uses GlobalMemoryStatusEx, and volume identity uses GetVolumeNameForVolumeMountPointW. References:
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes
- https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumenameforvolumemountpointw

## Remaining backend work

This does not complete all 15 phases. Per-core samples, physical core topology, privileged temperature/fan sensors, a direct NVML adapter, persistent database history and a dedicated private ForgeCore protocol remain outside this implementation. Disk throughput, readable system-process monitoring, NVIDIA compute-process VRAM and optional Prometheus/AG-UI/usage/OTel file adapters are implemented as documented in the adapter guide. Logical volume capacity is not physical-disk activity. No UI, layout editing or additional chart families are implemented here.

## Verification

Portable regression checks cover existing GPU parser/session behavior plus independent source polling, blocked/failing-source isolation, cancellation, duplicate engine start, counter warmup/reset/rates, CPU arithmetic, and raw JSON/CSV/TXT export. Windows CI additionally runs six seconds of real headless collection and requires actual RAM and CPU samples before packaging acceptance. A Windows runner has no substitute for validating the user's dual-RTX3090 machine.
