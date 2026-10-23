# GPU Monitor 2.0 baseline — 2026-10-23

Baseline main: `febc7e37a66e0fd1f3156a9b1d9627fa17866aba`.
The checkout was clean before work started. No open PRs were returned by the
connected GitHub search. The other remote branch, `claude/hopeful-ride-twvv0g`,
contains launcher changes only; it has no v2 telemetry or shell implementation.

The [15-phase master plan](GPU_MONITOR_V2_MASTER_PLAN.md) from the uploaded handover
is now the current scope. The [earlier plan](GPU_MONITOR_V2_PRODUCTION_PLAN.md)
and issues #2–#15 remain historical task references. Do not equate their phase
numbers with the new phase numbers or mark whole issues done from this audit.

## Source audit and validation matrix

| Area | Existing implementation | Validation / limits |
|---|---|---|
| Inventory | `GpuReading` UUID, mutable index, model, bus, timestamp, nullable metric dictionary | Source reviewed; executable regression checks cover 0/1/2/4/8, reorder, addition and removal |
| NVIDIA parser | Twelve CSV fields; seven metrics plus memory total; invariant numeric parsing | Exact values, quoted commas/quotes, N/A, NaN, infinity, negative numbers and malformed rows checked |
| Collector | `NvidiaCollector.ReadAsync(CancellationToken)`; hidden `nvidia-smi` process; eight-second timeout; exit-code validation | Source reviewed; NVIDIA driver, subprocess timeout and recovery require Windows runtime validation |
| History | UUID queues; max 3600 readings and 30 minutes; removal discards history | Retention boundary and fast sampling cap checked |
| Dashboard | Dynamic panels/filter; 1/2/5/10-second refresh; 5/15/30-minute history; CSV; saved settings | Source reviewed; existing Windows `--self-test` still required for native controls |
| Failure state | `RefreshAsync` keeps prior readings and dims panels on errors; next success recovers | Source assertion only; no live failure/recovery run in this Linux environment |
| Tray | Open, Exit, double click, minimize-to-tray; timer continues while hidden | Source reviewed, not interactively exercised |
| Single instance | `Local\GpuMonitor` mutex in normal startup | Source reviewed; Windows runtime validation pending |
| Packaging | Self-contained ZIP, Inno Setup per-user installer, Start menu shortcut | Source reviewed; install/upgrade/uninstall not run here |
| Desktop shortcut | Separate repository `Run-GPU-Monitor.ps1` creates shortcut and may fall back to browser prototype | Installer does not itself create desktop shortcut; verify packaged launch path before release |
| Screenshot | Existing `--verify-ui` needs real NVIDIA hardware | No executing WPF screenshot captured here; no claim of v2 fidelity |
| v2 integrations | No LiveCharts2, hardware providers beyond NVIDIA, OTel or AG-UI in baseline | Still planned |

## Repeatable baseline checks

From the repository root with .NET 10 SDK:

```text
dotnet run --project tests/GpuMonitor.TelemetryChecks -c Release
dotnet build src/GpuMonitor/GpuMonitor.csproj -c Release
```

The first command compiles the production `Telemetry.cs` directly into a portable
console check runner. It does not use NVIDIA hardware or WPF, has no test-package
dependencies and exits nonzero on the first failed assertion. All 20 baseline
checks passed on Linux with SDK 10.0.402. This complements the existing Windows
`GpuMonitor.exe --self-test`; it does not replace native UI checks.

The unchanged WPF application also cross-built in Release on Linux using
`-p:EnableWindowsTargeting=true`: zero warnings and zero errors. This verifies
compilation, not execution of the Windows desktop runtime.

Windows CI now runs the portable checks before its existing WPF build, native
self-test, ZIP packaging and installer compilation. CI execution/results are
separate evidence; a configured workflow alone is not a passing build.

## Existing issue mapping (read on GitHub 2026-10-23)

| Existing issue | Original track | New master phase(s) |
|---|---|---|
| [#2](https://github.com/walladanger/GPU-Monitoring/issues/2) | Design contract | 01 |
| [#3](https://github.com/walladanger/GPU-Monitoring/issues/3) | UI foundation | 00, 02, 04 |
| [#4](https://github.com/walladanger/GPU-Monitoring/issues/4) | Shell | 04 |
| [#5](https://github.com/walladanger/GPU-Monitoring/issues/5) | Discovery | 02, 08, 09 |
| [#6](https://github.com/walladanger/GPU-Monitoring/issues/6) | GPU cards | 04, 05 |
| [#7](https://github.com/walladanger/GPU-Monitoring/issues/7) | Adaptive layout | 05, 06 |
| [#8](https://github.com/walladanger/GPU-Monitoring/issues/8) | Panel workspace | 06 |
| [#9](https://github.com/walladanger/GPU-Monitoring/issues/9) | Charts | 03, 07 |
| [#10](https://github.com/walladanger/GPU-Monitoring/issues/10) | Overlays | 07, 13 |
| [#11](https://github.com/walladanger/GPU-Monitoring/issues/11) | CPU/RAM | 08 |
| [#12](https://github.com/walladanger/GPU-Monitoring/issues/12) | Storage | 08 |
| [#13](https://github.com/walladanger/GPU-Monitoring/issues/13) | Layout persistence | 06, 14 |
| [#14](https://github.com/walladanger/GPU-Monitoring/issues/14) | Reliability | 02, 14 |
| [#15](https://github.com/walladanger/GPU-Monitoring/issues/15) | Release QA | 01, 14 |

New phases 10–12 (runtime/token/agent/OTel adapters) have no equivalent complete
legacy issue. Keep them in the master plan rather than silently reusing an unrelated
issue. No legacy issue was closed or its checklist rewritten by this audit.

## Phase 00 progress

00.01–00.03, 00.08 and 00.09 have source/audit evidence above. Portable parser and
inventory checks for 00.04 pass; the existing native smoke suite remains a Windows
gate. 00.05 has a successful Release cross-build; native execution remains pending.
00.06 has packaging source evidence only,
and 00.07 (actual native screenshot) remains pending. Phase 00 is not fully complete.
