# GPU Monitor 2.0 — Faithful UI Reproduction & Adaptive Hardware Monitoring

**Repository:** [walladanger/GPU-Monitoring](https://github.com/walladanger/GPU-Monitoring)  
**Status:** Production backlog (not implemented)  
**Authoritative reference:** The originally generated **“SGLang Monitor”** 1536×1024 screenshot selected in ChatGPT on 2026-10-21. Add it unmodified at `docs/design/gpu-monitor-v2-reference.png` before visual implementation. **Do not substitute** the simpler browser-dashboard preview or generate a different mockup. The image itself is not yet committed to this repo.

## Product contract

Build a **standalone, offline-capable Windows GPU/system monitor**, not an SGLang benchmark. Reproduce the selected screenshot’s visual language **faithfully**: navy/charcoal surfaces, dark sidebar, blue selection and memory lines, green activity lines, amber power indicators, fine borders, typography, spacing, radii, icons, charts, header and system status. Preserve the UI design unless user explicitly authorizes a deviation. Preserve the existing NVIDIA data collection, startup and tray behavior, installer, CSV export, and current settings.

**Hardware-aware viewport:**
- Inventory, identify, and monitor only real connected hardware; the eight H100s shown in the reference are sample data, not fixed placeholders.
- With two installed GPUs, show two GPU cards side by side **if they fit**. On narrow windows stack them vertically. On larger 21:9/32:9 windows show additional columns only when room permits.
- Put **user-configurable sensor panels immediately beneath** the auto-discovered GPU cards; continue primarily vertically.
- Offer add/remove/reorder/resize, half-width/full-width merged panels, comparative/overlaid multiple series, and saved profiles.
- Essential sample use case: combine GPU0 and GPU1 utilization into one full-width chart on the same percent axis. Mixed units require an explicitly labeled dual axis.
- No invented metrics: unavailable/unsupported data gets N/A, hidden optional sensors, or a clearly explained unavailable state.
- Always adapt to **window width + DPI**, not solely monitor width or the number of installed GPUs.

**Implementation guardrails:**
- Existing app is **.NET 10 WPF**, NVIDIA readings currently come from `nvidia-smi`, history is session-only, and an Inno Setup installer is already present.
- Prefer preserving the WPF host, tray, installer and collectors. Phase 02 explicitly selects between carefully styled WPF and **offline WebView2 UI hosted in WPF** using a quick visual-fidelity and resource-use spike. Do not silently rewrite the app or create a cloud dependency.
- Hardware provider abstraction: stable device IDs; metric key/unit/time; supported/unavailable; bounded history; local collectors; mock fixtures for CI. AMD/Intel telemetry may follow later; detect them without faking coverage now.
- New visual UI must work with test-fixture data without requiring eight physical GPUs. Preserve read-only operation.
- Keep the approved reference image under source control; make reproducible screenshots with deterministic fixture values, and compare reference/image side by side at release gate.

## Suggested initial layout

1. Screenshot-matched top bar and sidebar.
2. Compact summary tiles: GPUs detected, aggregate utilization, VRAM totals, aggregate GPU power (from actual data).
3. Auto-created GPU cards, typically 2 across for two GPUs.
4. Configurable panel grid directly below GPU cards; start with `+ Add panel` tiles and optional sensible CPU/RAM suggestions.
5. Example merged 2-column chart: `GPU0 utilization vs GPU1 utilization`, with fixed GPU colors and a legend.
6. Additional CPU utilization, RAM, storage capacity, disk I/O, CPU temperature etc., only when sensors exist.

## Subagent rules

- One task-package branch/PR per phase; small checkbox commits are encouraged.
- Assign ownership by subsystem below; touch shared shell or data contracts only after the relevant base phase merges.
- No direct merges without CI and UI acceptance for that phase. Do not rebuild or restyle other areas casually.
- Each checklist step should be independently demonstrable (test, screenshot, or documented verification).
- Update checkboxes in GitHub issues, attach screenshots/fixtures to PRs, and document deviations.
- Parallelize phases **04 and 03 after 02**, phases **08 and 10/11 after contract phase 04**, and widget work after 06/07. Merge in dependency order.

## Definition of done

1. Reference fidelity accepted using screenshot-based comparison.
2. 0/1/2/4/8 GPU fixtures render with **no phantom cards**.
3. Dual-3090 UI has two GPU panels plus real selectable widgets below, with no artificial eight-card whitespace.
4. GPU0+GPU1 overlay works, can merge/split, and remembers user preferences.
5. CPU/RAM and disk/drive widgets show supported metrics accurately or explain unavailable readings.
6. Window resizing, widescreens, Windows DPI, suspend/resume and hotplug have passing tests.
7. Signed/not-signed package status accurately documented; Windows x64 ZIP/installer install and launch.
8. No background console, network analytics, fabricated sensors, or silent errors.

## Detailed production checklists

### Phase 01 — Freeze the approved design reference

**Owner:** Design/UI  
**Depends on:** None  
**Suggested branch:** `feat/monitor-v2-design-contract`

- [ ] 01.01 Add the original generated SGLang Monitor PNG at `docs/design/gpu-monitor-v2-reference.png` (obtain it from the original chat attachment; do not regenerate it).
- [ ] 01.02 Record the screenshot’s native 1536×1024 reference resolution.
- [ ] 01.03 Document the sidebar, top bar, summary cards, GPU cards, chart area and footer.
- [ ] 01.04 Sample the reference’s colors into named design tokens.
- [ ] 01.05 Measure card radii, borders, shadows, spacing and chart strokes.
- [ ] 01.06 Identify the closest licensable font; document unavoidable font uncertainty.
- [ ] 01.07 Document icon sizes, states, text weights and text scales.
- [ ] 01.08 Define screenshot comparison crops that exclude simulated live values.
- [ ] 01.09 Mark the screenshot as the non-negotiable visual baseline.

### Phase 02 — Set up a safe UI foundation

**Owner:** Desktop infrastructure  
**Depends on:** 01  
**Suggested branch:** `feat/monitor-v2-ui-foundation`

- [ ] 02.01 Preserve the existing `main` monitoring executable and installer.
- [ ] 02.02 Create a new v2 UI feature branch; do not rework telemetry during styling.
- [ ] 02.03 Choose WPF-native styling or offline WebView2 through an explicit visual-fidelity and resource-use spike.
- [ ] 02.04 Keep the existing .NET 10 host, tray operations and installer in either approach.
- [ ] 02.05 Define a typed read-only telemetry-to-UI interface.
- [ ] 02.06 Add fixture input mode for developing without NVIDIA hardware.
- [ ] 02.07 Add a toggle to launch the old UI during migration.
- [ ] 02.08 Add a deterministic screenshot capture command for visual regression.
- [ ] 02.09 Document build and smoke-test instructions.

### Phase 03 — Create the adaptive application shell

**Owner:** UI shell  
**Depends on:** 01,02  
**Suggested branch:** `feat/monitor-v2-shell`

- [ ] 03.01 Recreate the screenshot’s dark window background.
- [ ] 03.02 Recreate the exact left navigation rail and selected-row appearance.
- [ ] 03.03 Recreate the title/header strip and right-side status controls.
- [ ] 03.04 Preserve functional Windows close, minimize and maximize actions.
- [ ] 03.05 Add the GPU Overview, GPU Details, Processes, System and Settings destinations.
- [ ] 03.06 Implement responsive content width that reacts to WINDOW size, not screen resolution alone.
- [ ] 03.07 Account for Windows DPI scaling and browser/webview zoom if applicable.
- [ ] 03.08 Allow normal vertical scrolling when panels exceed viewport height.
- [ ] 03.09 Add clear empty, loading, stale, and disconnected states.
- [ ] 03.10 Check shell appearance side by side with the source image.

### Phase 04 — Implement hardware and sensor discovery contracts

**Owner:** Telemetry contracts  
**Depends on:** 02  
**Suggested branch:** `feat/monitor-v2-device-registry`

- [ ] 04.01 Define stable device identity for GPU, CPU, memory and disks.
- [ ] 04.02 Define metric keys, units, ranges, timestamps and supported/unavailable states.
- [ ] 04.03 Build an observable device inventory stream.
- [ ] 04.04 Continue using NVIDIA GPU UUIDs rather than mutable GPU indices.
- [ ] 04.05 Identify Windows CPU model, core/thread count and installed RAM.
- [ ] 04.06 Discover physical disks and logical volumes separately.
- [ ] 04.07 Detect graphics adapters beyond NVIDIA without promising unsupported readings.
- [ ] 04.08 Rescan when devices appear or disappear and on manual request.
- [ ] 04.09 Never fabricate a value when a sensor is inaccessible.
- [ ] 04.10 Add zero/one/two/four/eight-GPU fixtures and discovery tests.

### Phase 05 — Recreate the GPU cards and summary metrics

**Owner:** GPU frontend  
**Depends on:** 03,04  
**Suggested branch:** `feat/monitor-v2-gpu-cards`

- [ ] 05.01 Reproduce the reference GPU-card shell exactly.
- [ ] 05.02 Bind the card title to actual GPU index and model.
- [ ] 05.03 Show live utilization and a green trend sparkline.
- [ ] 05.04 Show VRAM in use/total and a blue sparkline.
- [ ] 05.05 Show supported power, core temperature and fan readings.
- [ ] 05.06 Hide unsupported optional sensor rows without breaking the card.
- [ ] 05.07 Match NVIDIA GPU indicator and chart colors to reference.
- [ ] 05.08 Render exactly one card per detected GPU; never create placeholder GPUs.
- [ ] 05.09 Place two cards side by side when card minimum widths fit.
- [ ] 05.10 Wrap additional GPUs into lower rows instead of leaving horizontal gaps.
- [ ] 05.11 Display a graceful no-GPU state.

### Phase 06 — Build responsive vertical layout and panel slots

**Owner:** Layout engine  
**Depends on:** 03,05  
**Suggested branch:** `feat/monitor-v2-adaptive-grid`

- [ ] 06.01 Measure usable viewport width after the sidebar is excluded.
- [ ] 06.02 Use one GPU column in narrow windows.
- [ ] 06.03 Use two GPU columns at ordinary desktop widths when cards fit.
- [ ] 06.04 Permit three or more GPU columns on sufficiently wide viewports.
- [ ] 06.05 Allow user override for preferred maximum column count.
- [ ] 06.06 Ensure 1–2 GPUs do not leave empty card placeholders.
- [ ] 06.07 Place the customizable panel region immediately BELOW the GPU cards.
- [ ] 06.08 Create add-panel slots in the first available row below the GPU region.
- [ ] 06.09 Support half-width, full-row and multi-row cards where space permits.
- [ ] 06.10 Allow all panels to stack vertically when narrow.
- [ ] 06.11 Test 16:9, 21:9, 32:9 and 4K scaling.

### Phase 07 — Create user-selected panel management

**Owner:** Panel workspace  
**Depends on:** 04,06  
**Suggested branch:** `feat/monitor-v2-panel-workspace`

- [ ] 07.01 Add a clearly visible `+ Add panel` control below GPU cards.
- [ ] 07.02 Provide a searchable panel/metric picker.
- [ ] 07.03 Group options under GPU, CPU, RAM, Disk and Combined.
- [ ] 07.04 Insert the chosen panel into the selected empty slot.
- [ ] 07.05 Add drag-to-reorder via a panel title/handle.
- [ ] 07.06 Add card width and height resize controls.
- [ ] 07.07 Support remove, duplicate, rename and reset actions.
- [ ] 07.08 Support full-width merging of two adjacent panel slots.
- [ ] 07.09 Keep read-only GPU detection cards separate from customizable widget cards.
- [ ] 07.10 Show a preview of each panel before adding it.
- [ ] 07.11 Add keyboard-accessible equivalents for movement and resize.

### Phase 08 — Build the reusable telemetry chart engine

**Owner:** Charts  
**Depends on:** 04,07  
**Suggested branch:** `feat/monitor-v2-chart-engine`

- [ ] 08.01 Create a time-series data model independent of chart widgets.
- [ ] 08.02 Draw charts in the original visual style and stroke thickness.
- [ ] 08.03 Implement shared X-axis time windows.
- [ ] 08.04 Support 1, 2, 5 and 10 second sampling where the provider allows.
- [ ] 08.05 Provide 5, 15 and 30 minute views.
- [ ] 08.06 Handle missing values as gaps, not interpolated false data.
- [ ] 08.07 Add legend colors bound to stable device IDs.
- [ ] 08.08 Support hover/focus readouts and an optional chart pause.
- [ ] 08.09 Bound ring-buffer memory and chart repaint work.
- [ ] 08.10 Make axis labels and units visible at different card sizes.

### Phase 09 — Implement merged and overlaid comparison panels

**Owner:** Chart composition  
**Depends on:** 07,08  
**Suggested branch:** `feat/monitor-v2-overlays`

- [ ] 09.01 Add `Compare metrics` to the panel picker.
- [ ] 09.02 Allow two or more metric series in the same panel.
- [ ] 09.03 Implement GPU 0 vs GPU 1 utilization on the same 0–100% axis.
- [ ] 09.04 Allow additional series from different GPUs.
- [ ] 09.05 Support a chart that expands to span two normal card widths.
- [ ] 09.06 Add distinct series colors, on/off toggles and a readable legend.
- [ ] 09.07 Align series by timestamps and show gaps for missing samples.
- [ ] 09.08 Require explicit dual-axis setup when combining incompatible units.
- [ ] 09.09 Add a one-click split action to restore separate cards.
- [ ] 09.10 Save and restore combined-panel series selections.
- [ ] 09.11 Add comparison tests for 1, 2 and 4 GPU fixtures.

### Phase 10 — Add CPU and system-memory metrics

**Owner:** CPU/RAM collectors  
**Depends on:** 04,08  
**Suggested branch:** `feat/monitor-v2-cpu-ram`

- [ ] 10.01 Read CPU model and logical/core counts.
- [ ] 10.02 Collect whole-CPU utilization through a supported Windows counter.
- [ ] 10.03 Add optional per-core or per-logical-processor graphs.
- [ ] 10.04 Collect total, in-use and available system memory.
- [ ] 10.05 Define permissions/unavailable handling for temperatures.
- [ ] 10.06 Evaluate an optional local hardware-sensor library for CPU temperature/fan data.
- [ ] 10.07 Do not ship a new always-running privileged service by default.
- [ ] 10.08 Add CPU and RAM cards to the panel picker.
- [ ] 10.09 Keep CPU and RAM panels available on machines with zero NVIDIA GPUs.
- [ ] 10.10 Test collection CPU overhead and failure recovery.

### Phase 11 — Add drives, disks and storage metrics

**Owner:** Storage collectors  
**Depends on:** 04,08  
**Suggested branch:** `feat/monitor-v2-storage`

- [ ] 11.01 Discover physical SSDs, HDDs and NVMe devices.
- [ ] 11.02 Map logical volumes to capacity/free-space readings.
- [ ] 11.03 Collect read and write throughput on supported devices.
- [ ] 11.04 Collect disk active/busy metrics where Windows exposes them.
- [ ] 11.05 Attempt temperature/SMART only where safely accessible.
- [ ] 11.06 Label sensor provenance and unknown/unavailable values.
- [ ] 11.07 Offer disk selection in the custom-panel picker.
- [ ] 11.08 Add a combined read vs write comparison panel.
- [ ] 11.09 Handle removable, offline and changing drives safely.
- [ ] 11.10 Add test fixtures for multiple and missing drives.

### Phase 12 — Implement configuration and saved layouts

**Owner:** Workspace persistence  
**Depends on:** 07,09,10,11  
**Suggested branch:** `feat/monitor-v2-layout-persistence`

- [ ] 12.01 Define a versioned workspace schema.
- [ ] 12.02 Persist widget order, type, size and title.
- [ ] 12.03 Persist device/metric selections by stable IDs where possible.
- [ ] 12.04 Persist chart ranges, colors and enabled comparison series.
- [ ] 12.05 Keep a per-machine default layout that adapts to available hardware.
- [ ] 12.06 Support multiple named user layouts.
- [ ] 12.07 Allow Export/Import of a layout JSON with schema validation.
- [ ] 12.08 Provide Reset to recommended hardware-aware layout.
- [ ] 12.09 Handle a saved layout referencing a missing GPU or disk.
- [ ] 12.10 Preserve existing refresh and tray preferences during migration.

### Phase 13 — Add reliability, security and performance checks

**Owner:** Quality engineering  
**Depends on:** 04–12  
**Suggested branch:** `feat/monitor-v2-robustness`

- [ ] 13.01 Keep the monitoring app local-only; no outbound analytics.
- [ ] 13.02 Separate collector failures from UI failures.
- [ ] 13.03 Report stale data and a retry path.
- [ ] 13.04 Constrain subprocess and sensor-library execution time.
- [ ] 13.05 Sanitize device strings displayed by UI and exported data.
- [ ] 13.06 Ensure collected values and chart data are bounded in memory.
- [ ] 13.07 Measure idle CPU, RAM, startup time and repaint latency.
- [ ] 13.08 Measure overhead with 2 GPUs and 8-GPU mock fixtures.
- [ ] 13.09 Test suspend/resume, device hotplug and app minimization.
- [ ] 13.10 Verify no terminal window appears during ordinary launch.

### Phase 14 — Visual QA, installer and release

**Owner:** Release/QA  
**Depends on:** 01–13  
**Suggested branch:** `feat/monitor-v2-release-qa`

- [ ] 14.01 Capture a deterministic screenshot at 1536×1024 using fixture data.
- [ ] 14.02 Compare typography, colors, spacing and chart/card details against the source PNG.
- [ ] 14.03 Review high-DPI and widescreen screenshot variants.
- [ ] 14.04 Verify cards add/remove and reflow without blank gaps.
- [ ] 14.05 Verify merged GPU 0 vs GPU 1 graphs.
- [ ] 14.06 Run unit tests and end-to-end UI smoke checks.
- [ ] 14.07 Build a self-contained Windows x64 ZIP.
- [ ] 14.08 Build the conventional installer with Start menu/desktop shortcuts.
- [ ] 14.09 Verify install, upgrade, uninstall and saved settings behavior.
- [ ] 14.10 Attach installer, screenshots and test results to CI artifacts.
- [ ] 14.11 Keep release behind an explicit visual sign-off gate.

## Production release order

**Design asset and architectural gate:** 01 → 02. **Foundations in parallel:** 03 + 04. **Visible first build:** 05 → 06 → 07. **Composable data:** 08 → 09. **Additional hardware collectors:** 10 + 11. **Polish/ship:** 12 → 13 → 14.

**Change tracking:** Every branch/PR must reference its phase issue and this document. All tasks start unchecked. Completion is only claimed when implemented and verified, not when planned.
