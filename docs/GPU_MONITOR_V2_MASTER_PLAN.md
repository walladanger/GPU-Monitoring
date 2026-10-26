# GPU Monitor 2.0 — MASTER 15-Phase Production Implementation Plan

> **Agent execution:** Each task is a small, independently verifiable checkbox. Use subagent-driven execution or incremental implementation with test-first development, focused PRs and review. Phase and task IDs are stable in this plan; map them to older GitHub issues rather than duplicating work.

**Goal:** Complete an installable standalone Windows hardware+optional LLM/agent monitor matching the approved SGLang Monitor dashboard screenshot (except the user's annotated modifications), with real backend measurements, expandable monitoring panels, diverse charts and actual text/machine-readable exports.

**Architecture:** Preserve .NET 10 WPF/nvidia-smi app and current installer. Use typed optional telemetry providers (NVIDIA, Windows, ForgeCore, SGLang/vLLM Prometheus, Token Monitor, AG-UI, OTel), bounded time-series buffers, LiveCharts2 WPF and saved layout configuration. GPU Monitor must function fully offline without an LLM provider.

**Tech stack:** C#/.NET 10/WPF, LiveCharts2 (version/license validated before integration), Windows hardware telemetry, optional NVML, optional SQLite, optional OTel Collector/OTLP, .NET tests and existing Inno Setup packaging.

**Visual spec:** `05_IMAGES/APPROVED_BASELINE/Original_SGLang_Monitor_Eight_GPU.png` + user modifications `03_REQUIREMENTS/ANNOTATED_SCREENSHOT_NOTES_TEXT.md`.

**User hard locks:**
- Exact visual style of original approved image; no generic replacement UI.
- Automatic hardware inventory, 2 real GPU cards for dual-3090, responsive vertical expansion.
- Larger chart areas; chart axes inside panels; exact crosshair data inspection.
- Drag, resize, merge/split/add widgets and persist workspace layouts.
- Correlate GPU, CPU, disk, LLM tokens/s, agent tools, processes and logs only where actual data exists.
- No phantom GPUs/data/series; unavailable metrics clearly labeled.
- Offline-first, read-only, no hidden uploads and no terminal window.
- Real TXT/MD and CSV/JSON for status/history beside optional image exports.

**Review focus:**
1. 0/1/2/8 GPU fixtures must never show invented hardware.
2. Interrupted NVIDIA/LLM provider does not blank another provider's existing data.
3. Counter reset / cached tokens / wall-clock differences do not produce false token rate spikes.
4. Layout loaded on a different machine handles missing device IDs without crashing.
5. Crosshair times and process/trace correlation use consistently mapped timestamps across sources.

**Status:** Detailed scoped plan. The listed steps are not implemented merely because they appear here. See [the current repository resume audit](GPU_MONITOR_V2_RESUME_AUDIT.md) for branch reconciliation, implementation evidence and remaining gates.

## Phase 00 — Baseline audit and project protection

**Owner:** Repository / Release  
**Depends on:** None  
**Suggested branch:** `feat/gm2-p00-baseline-audit-and-project-protectio`

- [ ] **00.01** Record the current main branch commit and the worktree status.
- [ ] **00.02** Audit src/GpuMonitor/Telemetry.cs and record existing interfaces.
- [ ] **00.03** Audit src/GpuMonitor/Program.cs and record existing functionality.
- [ ] **00.04** Run existing parser and inventory smoke tests.
- [ ] **00.05** Build the current Windows executable in Release mode.
- [ ] **00.06** Verify installer packaging, tray behavior and shortcuts.
- [ ] **00.07** Capture and archive a current native v1 screenshot.
- [ ] **00.08** Publish a baseline matrix: verified, partial, missing, not locally tested.
- [ ] **00.09** Compare existing GitHub issues #2–#15 with this newer plan without deleting history.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 01 — Freeze the faithful visual design

**Owner:** UI Design / Visual QA  
**Depends on:** 00  
**Suggested branch:** `feat/gm2-p01-freeze-the-faithful-visual-design`

- [ ] **01.01** Commit the ORIGINAL approved eight-GPU image unchanged as the visual style source.
- [ ] **01.02** Commit the user-annotated two-GPU image as the functional correction source.
- [ ] **01.03** Record image dimensions, sidebar width, grid, margins and typography.
- [ ] **01.04** Extract navy, charcoal, blue, bright green, amber and neutral design tokens.
- [ ] **01.05** Identify a licensed font matching the reference and document any approximation.
- [ ] **01.06** Document icon silhouettes, stroke widths, outlines, shadows and controls.
- [ ] **01.07** Measure the compact header, main summary tiles, graph containers and footers.
- [ ] **01.08** Write explicit expected differences for 0/1/2/4/8-GPU layouts.
- [ ] **01.09** Implement deterministic fixture-based screenshot comparison criteria.
- [ ] **01.10** Obtain approval for any visible visual deviation BEFORE merging.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 02 — Create stable telemetry contracts and fixtures

**Owner:** Telemetry Backend  
**Depends on:** 00  
**Suggested branch:** `feat/gm2-p02-create-stable-telemetry-contracts-an`

- [ ] **02.01** Define DeviceIdentity with stable UUID or OS device identifiers.
- [ ] **02.02** Define MetricDefinition: key, unit, scale, source and capability state.
- [ ] **02.03** Define TelemetrySample: timestamp, device, metric and finite numeric value.
- [ ] **02.04** Define TelemetryEvent for discrete process or inference activity.
- [ ] **02.05** Define ITelemetryProvider and provider capability discovery.
- [ ] **02.06** Adapt existing NvidiaCollector to the provider interface, keeping nvidia-smi.
- [ ] **02.07** Preserve existing timeouts, stale states and automatic recovery.
- [ ] **02.08** Add bounded per-series circular buffers and sample retention limits.
- [ ] **02.09** Create deterministic test fixtures for zero, one, two, four and eight GPUs.
- [ ] **02.10** Write regression tests comparing old and new Nvidia sample values.
- [ ] **02.11** Keep collection running without the dashboard open or any LLM integration.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 03 — Integrate the production chart engine

**Owner:** Chart / Performance  
**Depends on:** 02  
**Suggested branch:** `feat/gm2-p03-integrate-the-production-chart-engin`

- [ ] **03.01** Select and pin an appropriate LiveCharts2 WPF release and dependencies.
- [ ] **03.02** Implement typed reusable chart view models independent of data source.
- [ ] **03.03** Match the reference green GPU utilization trace and subtle fill.
- [ ] **03.04** Match the blue VRAM series and unobtrusive gridline design.
- [ ] **03.05** Draw readable time and value axes inside the chart card, not screen edges.
- [ ] **03.06** Implement axis unit formatting for %, GiB, watts, °C, MHz and tokens/s.
- [ ] **03.07** Optimize frequent updates by bounding points and disabling harmful animation.
- [ ] **03.08** Handle missing values as gaps; do not fabricate samples.
- [ ] **03.09** Test sustained 1/2/5/10-second sampling and worst-case chart count.
- [ ] **03.10** Export one example plot and its identical underlying CSV rows.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 04 — Build the screenshot-faithful Windows shell

**Owner:** WPF UI / Design QA  
**Depends on:** 01,02,03  
**Suggested branch:** `feat/gm2-p04-build-the-screenshot-faithful-window`

- [ ] **04.01** Preserve .NET 10 WPF host, one-instance rules and tray shutdown behavior.
- [ ] **04.02** Recreate navy-black surfaces and fine bordered card styling.
- [ ] **04.03** Recreate navigation rail, selected blue Overview row and exact icon treatment.
- [ ] **04.04** Make sidebar collapsible to icon-only state without losing navigation.
- [ ] **04.05** Recreate top title area and compact non-distracting connection indicator.
- [ ] **04.06** Relocate refresh/pause/clear/theme options into compact settings/menu controls.
- [ ] **04.07** Create real data-bound GPU count, utilization, VRAM and power summary tiles.
- [ ] **04.08** Implement loading, disconnected, stale and unsupported states.
- [ ] **04.09** Capture a deterministic screenshot in the original reference viewport.
- [ ] **04.10** Review screenshot side-by-side and correct material visual differences.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 05 — Implement hardware-adaptive GPU panel layout

**Owner:** Responsive GPU UI  
**Depends on:** 02,04  
**Suggested branch:** `feat/gm2-p05-implement-hardware-adaptive-gpu-pane`

- [ ] **05.01** Display exactly one primary GPU panel per detected device.
- [ ] **05.02** Show two wide primary GPU cards side by side if the window fits.
- [ ] **05.03** Stack cards on narrow windows and add columns only when genuinely spacious.
- [ ] **05.04** Use window content width and Windows DPI, not only screen resolution.
- [ ] **05.05** Create larger GPU utilization and VRAM charts in each primary card.
- [ ] **05.06** Place compact real power, core temperature and fan stats underneath.
- [ ] **05.07** Keep large center graph areas unobstructed by oversized titles/axes.
- [ ] **05.08** Create a default custom-widget region immediately below primary GPU cards.
- [ ] **05.09** Validate no phantom GPUs, blank placeholder rows or incorrect device IDs.
- [ ] **05.10** Exercise 0/1/2/4/8-GPU fixtures, hotplug and index reorder.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 06 — Implement flexible customizable monitoring panels

**Owner:** Workspace / Layout  
**Depends on:** 04,05  
**Suggested branch:** `feat/gm2-p06-implement-flexible-customizable-moni`

- [ ] **06.01** Add a visible Add Panel command and searchable metric picker.
- [ ] **06.02** Group available metrics into GPU, CPU, RAM, Disk, Network, LLM, Agents and Compare.
- [ ] **06.03** Enable dragging by title bar or dedicated handle.
- [ ] **06.04** Enable resizing in width and height with sensible minimum dimensions.
- [ ] **06.05** Enable reorder, duplicate, remove, rename and restore operations.
- [ ] **06.06** Enable a panel to span the combined width of two normal card slots.
- [ ] **06.07** Support full-height vertical flow and overflow scrolling.
- [ ] **06.08** Provide accessible keyboard alternatives for dragging/resizing.
- [ ] **06.09** Define a versioned JSON layout schema and named presets.
- [ ] **06.10** Restore saved layouts correctly when hardware changes or a device disappears.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 07 — Build precise synchronized graph interaction

**Owner:** Charts / Interaction  
**Depends on:** 03,06  
**Suggested branch:** `feat/gm2-p07-build-precise-synchronized-graph-int`

- [ ] **07.01** Implement precise pointer crosshair with actual timestamp and exact data value.
- [ ] **07.02** Synchronize crosshair time across compatible graphs in the workspace.
- [ ] **07.03** Add legend and per-series visibility options.
- [ ] **07.04** Enable graph time ranges of 1, 5, 15 and 30 minutes.
- [ ] **07.05** Implement selected-point hover and consistent nearest-sample behavior.
- [ ] **07.06** Implement GPU0-vs-GPU1 utilization overlay with exactly two active series.
- [ ] **07.07** Allow >2 series only when those real sources are selected.
- [ ] **07.08** Require labeled distinct axes for different measurement units.
- [ ] **07.09** Implement zoom, pan, pause, reset and chart context menu.
- [ ] **07.10** Export a chart as PNG and its real time series as CSV/JSON.
- [ ] **07.11** Add tests for missing samples, counter resets and timestamp alignment.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 08 — Add CPU, memory, disk and network data

**Owner:** System Sensors  
**Depends on:** 02,03  
**Suggested branch:** `feat/gm2-p08-add-cpu-memory-disk-and-network-data`

- [ ] **08.01** Implement CPU model/core/thread discovery.
- [ ] **08.02** Implement total CPU usage and optional per-logical-core samples.
- [ ] **08.03** Collect total, used and available system RAM.
- [ ] **08.04** Evaluate optional LibreHardwareMonitor values for supported temperatures/fans.
- [ ] **08.05** Discover physical disks and logical volumes separately.
- [ ] **08.06** Collect throughput and busy/active-time metrics from supported Windows counters.
- [ ] **08.07** Collect interface-specific network receive/send throughput.
- [ ] **08.08** Provide unit-correct metric definitions to the panel picker.
- [ ] **08.09** Prevent privileged sensor access requirements from blocking basic monitoring.
- [ ] **08.10** Use N/A, unsupported or stale rather than simulated live measurements.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 09 — Add GPU/process and diagnostic monitoring

**Owner:** GPU Diagnostics  
**Depends on:** 02,08  
**Suggested branch:** `feat/gm2-p09-add-gpu-process-and-diagnostic-monit`

- [ ] **09.01** Assess optional NVML adapter and compatibility with the current driver.
- [ ] **09.02** Keep nvidia-smi as working fallback.
- [ ] **09.03** Implement GPU Details and Processes views without changing the original style.
- [ ] **09.04** Identify compute processes and per-process VRAM where supported.
- [ ] **09.05** Add utilization-by-process and system-process diagnostic filters.
- [ ] **09.06** Implement separate configurable panels for chosen processes.
- [ ] **09.07** Align events, process activity and GPU metrics on a shared timeline.
- [ ] **09.08** Handle permission limits, disappearing PIDs and process ID reuse.
- [ ] **09.09** Keep diagnostics read-only with no automatic process termination.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 10 — Connect ForgeCore and inference runtimes

**Owner:** LLM Runtime Adapters  
**Depends on:** 02,07  
**Suggested branch:** `feat/gm2-p10-connect-forgecore-and-inference-runt`

- [ ] **10.01** Inspect and document existing ForgeCore API/data contracts on its private repo.
- [ ] **10.02** Implement optional ForgeCore metrics adapter; do not require its server.
- [ ] **10.03** Create a bounded and timeout-safe Prometheus text metrics reader.
- [ ] **10.04** Implement optional SGLang metrics parser/mapper.
- [ ] **10.05** Implement optional vLLM metrics parser/mapper.
- [ ] **10.06** Provide runtime source enable/disable and connection-health status.
- [ ] **10.07** Map prompt vs generation throughput and total token counters correctly.
- [ ] **10.08** Map queue depth, KV-cache occupancy and request concurrency where exposed.
- [ ] **10.09** Map TTFT, decode latency and request latency distributions where exposed.
- [ ] **10.10** Add GPU usage vs token/s comparison with honest attribution limits.
- [ ] **10.11** Prevent disconnection from freezing existing hardware plots.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 11 — Implement token monitor and AG-UI event ingestion

**Owner:** Token / Agent Adapters  
**Depends on:** 02,10  
**Suggested branch:** `feat/gm2-p11-implement-token-monitor-and-ag-ui-ev`

- [ ] **11.01** Evaluate the Token Monitor repository license, APIs, supported file parsers and terms.
- [ ] **11.02** Implement compatible local token-usage adapter without secret collection.
- [ ] **11.03** Distinguish prompt/output/cache token measurements per provider/model/session.
- [ ] **11.04** Calculate instantaneous token rate correctly from cumulative-counter differences.
- [ ] **11.05** Implement AG-UI run, step, streaming, state and tool-call event decoding.
- [ ] **11.06** Map agent and tool lifecycle events to normalized events and durations.
- [ ] **11.07** Build agent and tool activity timeline panels.
- [ ] **11.08** Offer per-model/per-provider/per-session token-rate charts.
- [ ] **11.09** Default to metric metadata only; do not save prompts or message bodies.
- [ ] **11.10** Test rate correctness under resets, reconnects and duplicate events.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 12 — Support OpenTelemetry observability ingestion

**Owner:** OTel / Tracing  
**Depends on:** 02,10,11  
**Suggested branch:** `feat/gm2-p12-support-opentelemetry-observability-`

- [ ] **12.01** Define an OTel-to-normalized metric and event mapping document.
- [ ] **12.02** Add optional local OTLP connection via configured collector or receiver.
- [ ] **12.03** Accept supported metrics, traces/spans and selected event data.
- [ ] **12.04** Map GenAI token semantic convention fields when actually instrumented.
- [ ] **12.05** Correlate trace span IDs with model requests and agent/tool events when available.
- [ ] **12.06** Create trace timing panels and instrumented-source coverage indicators.
- [ ] **12.07** Bound ingestion queues and protect dashboard from volume spikes.
- [ ] **12.08** Never imply opaque uninstrumented providers reveal inaccessible token counts.
- [ ] **12.09** Default to no prompt/response content capture; enforce secret redaction.
- [ ] **12.10** Test collector absent, malformed payloads and reconnect.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 13 — Add multiple graph families and derived analytics

**Owner:** Charts / Analytics  
**Depends on:** 07,08,10,11,12  
**Suggested branch:** `feat/gm2-p13-add-multiple-graph-families-and-deri`

- [ ] **13.01** Add line, filled area and stepped line chart options.
- [ ] **13.02** Add stacked bar/area charts for distribution and allocations.
- [ ] **13.03** Add histogram charts for latency/request distributions.
- [ ] **13.04** Add heatmaps for CPU cores/GPU busy times or requests over time.
- [ ] **13.05** Add scatter plots for VRAM vs tokens/s or GPU watts vs throughput.
- [ ] **13.06** Add gauges for supported utilization, power and temperature values.
- [ ] **13.07** Add agent/tool event timelines and request trace waterfall views.
- [ ] **13.08** Add comparative process/trace lanes synchronized with hardware plots.
- [ ] **13.09** Expose correct units, derivation, provenance and confidence of computed metrics.
- [ ] **13.10** Persist chart-type presets and provide text/CSV/JSON exports.
- [ ] **13.11** Benchmark chart performance and limit expensive chart forms.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.


## Phase 14 — Quality, tray quick-view, persistence and Windows release

**Owner:** Release / Security / Visual QA  
**Depends on:** 00–13  
**Suggested branch:** `feat/gm2-p14-quality-tray-quick-view-persistence-`

- [ ] **14.01** Implement taskbar/tray hover mini-dashboard with configurable metrics.
- [ ] **14.02** Implement taskbar right-click presets and quick-launch navigation.
- [ ] **14.03** Keep main screen settings compact, with advanced options in the settings page.
- [ ] **14.04** Add optional SQLite history with bounded retention and schema migration.
- [ ] **14.05** Persist layout, enabled sources and history-window preferences.
- [ ] **14.06** Implement separate PNG chart, CSV/JSON raw data and TXT/MD report exports.
- [ ] **14.07** Check local-only privacy, safe endpoint configuration and secret handling.
- [ ] **14.08** Test Windows scaling, fullscreen, ultrawide, multi-monitor and suspend/resume.
- [ ] **14.09** Measure idle CPU/RAM/disk overhead and chart load for 2/8 GPU fixtures.
- [ ] **14.10** Run unit, integration and UI smoke tests on an actual Windows build.
- [ ] **14.11** Compare screenshot crops with the exact approved reference; fail on unapproved drift.
- [ ] **14.12** Confirm signed/unsigned installer disclosures, no visible terminal, clean uninstall.
- [ ] **14.13** Build versioned x64 installer and portable ZIP, with readable release notes.
- [ ] **14.14** Approve release only when all four milestones and required user UI signoff pass.

**Verification gate:** All tasks demonstrate a test, source assertion, real runtime screenshot, or explicit documented validation. Build and focused tests must pass; do not merge failing branches.

## Release milestones

- **M1 UI Alpha (Phases 00–05):** Real native exe with validated existing Nvidia backend and screenshot-faithful two-GPU UI, responsive panes, readable plots.
- **M2 Hardware Beta (Phases 06–09):** User-defined panels, precise crosshair, multi-series comparisons, CPU/RAM/disk/network/process support and real exports.
- **M3 AI/Agent Beta (Phases 10–13):** Optional ForgeCore/SGLang/vLLM/Token Monitor/AG-UI/OTel sources and rich graphs with actual data, not inferred metrics.
- **M4 Windows Release (Phase 14):** Validated installer, persistent layout/history, tray quick-view, clean shutdown, performance and screenshot signoff.

## Parallel execution / dependencies

Phase 00 baseline audit precedes changes. Design 01 and telemetry 02 can be prepared independently following 00. Chart 03 uses 02. Shell 04 requires 01/02/03. Responsive 05 and workspace 06 follow. Independent system hardware work 08 can proceed once telemetry contract 02 is stable; chart interaction 07 uses 03/06. GPU process diagnostics 09 uses 02/08. LLM adapters 10 use 02/07, agent/token 11 builds on 10, OTel 12 on source conventions; advanced charts 13 use measured source data. Phase 14 release integrates all tracks.

## Reuse / legacy issue migration

The earlier 14-phase/143-task GitHub plan is at `https://github.com/walladanger/GPU-Monitoring/blob/main/docs/GPU_MONITOR_V2_PRODUCTION_PLAN.md`; GitHub issues #2–#15 exist. Do not delete them or assert completed work. Before implementation, choose canonical tracker IDs and map old issues to these 15 phase IDs with explicit links. The earlier local 11-phase MD/DOCX is archived under `06_PRIOR_DOCUMENTS/`.

## Definition of done

1. Real source-backed chart data works on Windows 11 dual RTX3090 machine and mock 0/1/2/4/8 GPU tests pass.
2. UI matches approved original style after permitted annotated changes; human visually signs off.
3. Custom graphs, chart types, exact crosshairs, synchronized timelines, CSV/JSON/text and optional PNG export are functional.
4. Additional hardware/LLM/agent sources are optional with independent health indicators; no invented data.
5. Installer launches from shortcut with tray operations and no extra console; clean shutdown/uninstall works.
6. Security/privacy and performance budgets are documented and measured, not merely assumed.
7. Existing backend, GitHub issue history and old docs are preserved; no unsupported "completed" claims.
