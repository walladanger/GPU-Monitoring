# Annotated Screenshot — Accurate Text Requirements

**Primary source:** `05_IMAGES/ANNOTATED_AND_REVISIONS/User_Annotated_Screenshot_v2.jpg`  
**Important:** These notes change layout and behavior while LOCKING the original eight-GPU screenshot's established styling.

## Purple: taskbar mini monitor and left navigation

- Add a Windows tray/taskbar icon. Mouse hover reveals a semi-transparent blurred/frosted pop-up quick view. User selects which quick metrics appear and in what arrangement; this is a lightweight alternative to the main dashboard.
- Right-click menu on tray icon provides settings and specialized quick-monitor panels (just CPU, just GPU, etc.); configuration can also be changed through the main app Settings view.
- Sidebar collapses to icon-only. All controls remain usable. Existing Overview is the main dashboard; add buttons for CPU, RAM, network, disk, logs and process views rather than duplicate Overview.
- Processes page should support Windows Task Manager-like overview, individual process-specific panels, unlimited selectable process watch views, debugging events and exports.

## Purple / blue: header settings

- Refresh, Pause, Clear, Theme consume too much permanent header space. Relocate to accessible compact menu or settings drawer.
- Refresh interval is preferably configurable globally unless per-panel intervals have a clear demonstrable purpose.
- Connected indicator should show meaningful live connection health without taking primary content space.

## Green: summary status popover

- Summary tiles at top were useful at a glance but consume space that should be used by big real charts. Move/condense quick stats into the sidebar or tray quick-view when appropriate; preserve good information hierarchy.
- Quick popover could show GPU0/GPU1 status, CPU data, memory, throughput, and clocks, with optional graph detail; the user selects contents.

## Yellow / grey: scales and axes INSIDE data charts

- Time is on horizontal X axis; value and unit are on vertical Y axis. Both are visible within chart rather than at screen extremes.
- Small labels should be readable but not dominate usable graph width/height. The graph itself should get the largest possible share of each panel.
- Every relevant panel needs a selectable time window (1m, 5m, 15m, 30m etc) and source export.

## Black: crosshair and inspection interaction

- Mouse pointer over chart triggers precise crosshair and local overlay showing exact reading for that particular sample point.
- Crosshair synchronizes across graph panels, so a selected time is inspectable in all metrics. Pointer leaves -> non-disruptive state.
- Right clicking a chart can open a richer separate panel for that metric, further detail, comparison or debugging data.
- Real chart samples are mandatory; do not rely on mock visual traces in production.

## Orange: compact GPU supplementary metrics

- Power, temperature and fan information beneath GPU graph should remain easy to read yet compact; allow more usable space for GPU usage/VRAM graphs.
- Each primary GPU panel should be independently resizable and support larger horizontal/vertical graphs. CPU/system custom panels go below the GPU row.
- Provide quick access to alternate metrics (GPU compute, fan, RAM, CPU, disk, network, benchmarks) using selectors, per-panel settings or popouts.

## Red: comparisons, series integrity and synchronized process events

- The highlighted full-width GPU comparison graph incorrectly appeared to show >2 GPU lines despite only 2 detected. Show **only actual selected real series** by default: GPU0 + GPU1 means exactly 2 lines.
- Optionally allow extra percentage metrics on same chart (memory utilization, compute etc.), but never mix different physical units without labeled additional axes.
- Allow CPU/system utilization to be overlaid for bottleneck diagnostics only with clear labeling.
- Add synchronized process/debug event timeline at exactly the same time scale; correlate events and hardware utilization to find resource demands.

## Implementation verification

- Two real RTX3090 GPUs -> 2 primary GPU panels and 2 real series in GPU0/GPU1 comparison. No H100/hallucinated numbers.
- Narrow window -> vertical arrangement; ultrawide -> adaptive columns.
- Crosshair hover -> timestamped exact sample value with unit; same time cursor across graphs.
- Right click -> actual metric-specific menu; tray hover -> actually working popover.
- All data and plans exist in actual machine-readable TXT/MD/CSV/JSON formats besides any visualization.
