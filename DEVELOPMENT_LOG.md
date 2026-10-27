# GPU Monitor development log

## 2026-10-26 — customizable desktop panels

Implemented panel surfaces for the existing GPU, CPU, disk and comparison views, keeping the dark theme. Panel actions select multiple sensors with compatible overlays or two unit axes. Default views can be restored without discarding telemetry. Surface dimensions and selections persist by GPU UUID; custom workspace layouts continue to use named presets.

Resize targets now sit directly on the panel's outer bottom-right edge, without an inset icon or control row. Chart subviews also resize at their display edge. Contents scroll automatically when the panel or native window is too small. Current measurements and scales appear inside the graph instead of consuming a separate value column.

Detached surfaces move the same live view into a real native window and restore it on close. Graph-only and custom panel windows render shared dashboard snapshots. They never instantiate collectors. The dashboard owns polling, pause/rescan and shutdown. Closing a dashboard closes its windows.

GPU process sections filter compute processes by GPU UUID and show measured per-process VRAM, including unavailable values. Missing collectors, failed/stale providers and empty inventories have distinct explanatory states. The current NVIDIA process collector does not supply graphics-process attribution or per-process GPU utilization; device-wide sensors cannot be attributed per process. These limitations are stated in the UI.

Added automatic startup diagnostics at `%LOCALAPPDATA%\GpuMonitor\logs\development.log`, with a Settings action to open it. Logs rotate at 2 MiB with one previous file and include panel lifecycle, resizing, selection, inventory changes, preset failures and unhandled exceptions. Verification runs keep their logs in the build directory. No raw telemetry or process inventory is logged.

Validation found and fixed a comparison-series mismatch after all GPUs disappeared and returned. Diagnostic logging also identified that the restricted test environment blocked temporary preset-file renaming; the workspace suite passes with normal file access.

Validation commands and outputs are described in `src/GpuMonitor/README.md`. The build and telemetry regression suite pass. Native panel, workspace, monitoring lifecycle and overview checks pass, including live measurements from this machine's three NVIDIA GPUs. Physical dragging across monitors and DPI changes still requires human verification. This work updates the repository build; it does not replace an installed application. Commit and push were explicitly authorized with the eight-data-type follow-up.

## 2026-10-26 — eight data types, point selection and frosted actions

Expanded the former two-unit limit to eight independent unit groups. Line and area overlays retain up to 16 sensors; stacked-lane graphs show up to eight sensors with separate scales and raw-unit readings. Added the graph type to sensor selection and saved panel settings. Incompatible units still cannot share a scale. Source values and raw exports remain unchanged.

Left-click now selects the nearest measured point within a 16-pixel radius, highlights it, and pins its exact timestamp and value through incoming readings and hover movement. A second click on the point, an outside click, application deactivation, or Escape clears selection. Details and hover cards have a sampled blurred backdrop with translucent rounded gradient chrome. Every graph has visible zoom in, zoom out and reset buttons.

Settings and other action menus, submenus, tooltips and the sensor editor use the rounded translucent treatment. Surface resize handles occupy the very outer corner and right/bottom edges; GPU graphs expand with the surface rather than leaving empty space. Detached windows remain independently movable native windows and share the same collectors.

Validation: 262 telemetry regression checks; native panel tests for eight distinct units, correct lane selection, pinned-sample persistence, deselection, frosted detail and menu surfaces, visible zoom controls, outer-edge resize geometry, live resizing/cancellation, shared detached view updates, settings persistence and process unavailable states. Existing workspace and chart interaction suites pass. Physical multi-monitor and DPI transitions remain a manual verification item.
