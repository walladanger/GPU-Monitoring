# GPU Monitor 2.0 resume audit — 2026-10-23

The uploaded START_HERE, Chat Handover and master 15-phase plan were read before implementation. They predate the pushed implementation branches. Do not restart from main or treat planning notes as evidence of completed code.

## Repository baseline

- Main: `febc7e37a66e0fd1f3156a9b1d9627fa17866aba`.
- Latest disk/process/AI implementation: `7f6bc5479d19dc2c60c94d7d63c5311b6b8a91b8`, branch `feat/gm2-disk-process-ai`, PR #24 targeting `feat/gm2-backend-engine`.
- Fresh checkout was clean. Resume branch: `codex/gm2-resume-audit`, based on #24, preserving its backend and native shell.
- Nine open PRs were inspected. Nothing was merged, closed or deleted.

| PR | Track | Target |
|---|---|---|
| [#16](https://github.com/walladanger/GPU-Monitoring/pull/16) | Baseline audit/checks | main |
| [#17](https://github.com/walladanger/GPU-Monitoring/pull/17) | Locked design references | main |
| [#18](https://github.com/walladanger/GPU-Monitoring/pull/18) | Typed telemetry contracts | #16 branch |
| [#19](https://github.com/walladanger/GPU-Monitoring/pull/19) | Live NVIDIA session | #18 branch |
| [#20](https://github.com/walladanger/GPU-Monitoring/pull/20) | LiveCharts2 | #19 branch |
| [#21](https://github.com/walladanger/GPU-Monitoring/pull/21) | Native Overview | #20 branch |
| [#22](https://github.com/walladanger/GPU-Monitoring/pull/22) | Independent backend engine | #21 branch |
| [#23](https://github.com/walladanger/GPU-Monitoring/pull/23) | Earlier llama.cpp/vLLM adapter track | #22 branch |
| [#24](https://github.com/walladanger/GPU-Monitoring/pull/24) | Disk/process/SGLang/vLLM/metadata feeds | #22 branch |

#23 is a sibling of #24, not part of its ancestry. Its earlier llama.cpp work must be reconciled explicitly if adopted; merging both blindly would duplicate/overlap runtime changes. #17's unchanged design assets were cherry-picked into this resume branch because they were absent from #24. Both images match their locked hashes. No dashboard source or style was altered.

## Source-backed status

| Area / master phase | Evidence | Remaining limits |
|---|---|---|
| NVIDIA foundation / 00,02 | `Telemetry.cs`: original 12-field parser, UUID inventory, hidden subprocess, 8-second deadline; normalized provider and bounded histories | No actual dual-3090 run in this session |
| Design / 01 | Original + annotated images, measured tokens, manifest and checker now included | Native typography/icons and human visual signoff open |
| Chart engine / 03 | LiveCharts2 WPF 2.0.5 pinned; real samples, gaps, raw CSV/JSON exports | Sustained resource measurements and reference fidelity open |
| Shell / 04,05 | `Program.cs` defaults to `OverviewWindow`; dynamic UUID cards, adaptive width, settings menu, tray, synchronized cursor | Existing shell preserved; not declared screenshot approved |
| Workspace / 06 | No general persisted drag/resize/merge workspace | Open |
| Interaction / 07 | Nearest-sample inspection, synchronized cursor, 1/5/15/30 ranges, comparison trace per detected GPU | General selection, zoom/pan and incompatible-unit axes open |
| Hardware / 08 | CPU total, RAM, per-interface network, fixed-volume capacity, PDH physical-disk activity | Logical CPU provider added in this resume increment; physical core topology, per-core UI and optional temperatures/fans still open |
| Processes / 09 | System CPU/memory, PID/start-time identities, NVIDIA compute-process VRAM | Native NVML and target WDDM validation open |
| Runtime / 10 | Named SGLang/vLLM HTTP metrics adapters, bounded parsing, reset-safe rates, queues/cache/latency means | Real servers and dedicated private ForgeCore protocol unverified |
| Agent/token / 11 | Explicit AG-UI and usage JSONL feeds, bounded metadata/events | Third-party Token Monitor compatibility and agent timeline UI open |
| OTel / 12 | Supported OTLP JSON file metrics/spans, IDs and numeric metadata | No OTLP network receiver or trace UI |
| Chart families / 13 | Existing line/area charts only | Additional chart families open |
| Release / 14 | Packaging, portable ZIP, Inno Setup, CI fixture screenshots | SQLite, tray hover, target-machine install/uninstall, performance budget and visual signoff open |

The native Overview and legacy Dashboard still poll their own NVIDIA TelemetrySession. The independent engine is used by headless collection; CPU/disk/network/process/AI providers are not yet displayed in the dashboard. Do not confuse backend availability with UI integration.

## Verification

Before this increment, all 147 portable production-source regression checks passed here with .NET SDK 10.0.402. The existing Windows-targeted app cross-built in Release with zero warnings/errors using cached dependencies. Cross-compilation is not a Windows execution test.

GitHub [run 40026502595](https://github.com/walladanger/GPU-Monitoring/actions/runs/40026502595), at #24 head above, was independently inspected: Ubuntu checks and Windows package jobs passed, including loopback runtime tests, native app checks, actual CPU/RAM/disk/process headless collection, configured usage ingestion, fixture screenshot capture, portable ZIP and Inno installer compilation. This is historical evidence for that exact head, not validation of later changes or the user's machine.

Existing issues #2–#15 remain open. Their mapping in [the original audit](GPU_MONITOR_V2_BASELINE.md#existing-issue-mapping-read-on-github-2026-10-23) is still applicable; legacy issue numbers are not current master phase numbers. The master plan remains authoritative scope, with completion evidence in these audit/feature documents rather than blanket checkbox completion.

## Resume increment and next tasks

Added opt-in `windows-cpu-cores` backend provider for master 08.02, configured with `CollectCpuCores: true`. Existing defaults, NVIDIA collector, dashboard and styling are preserved. See [logical CPU setup](GPU_MONITOR_V2_BACKEND.md#optional-logical-cpu-measurements).

Portable checks now total 160; the updated Release cross-build must also pass. Windows CI additionally requires actual native per-logical-CPU samples and their appearance in headless exports. Target dual-3090 validation and human design acceptance remain open.

Next increments:

1. Reconcile #23's llama.cpp adapter with the current runtime API without dropping SGLang/vLLM mappings.
2. Connect engine snapshots to a dashboard presentation boundary, preserving the approved styling and independent source health.
3. Implement versioned workspace layout data, hardware-change recovery, then the annotated panel interactions with native screenshot review.
4. Complete advanced chart interactions/families, persistence and tray hover under their existing phase IDs.

No release milestone is declared complete by this audit.
