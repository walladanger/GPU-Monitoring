# GPU Monitor development log

## 2026-10-27 — popup blur, themed scrollbars and responsive rows

Replaced the shared popup outline and light glass with borderless dark navy surfaces. Menus, submenus, combo dropdowns, tooltips, chart details and the sensor editor blur a separate backdrop layer and feather it through a 72-DIP surround. Popup templates reserve transparent space for the halo; foreground text stays sharp. Popups sample the desktop into memory before their first render, including areas outside the owner window, and release that sample on close. Explicit chart/owner samples remain live. Masks are cached until dimensions change.

Added application-wide WPF scrollbar and scroll-viewer templates for both directions, with rounded blue thumbs, hover/drag feedback and a matching dark corner. Verified page commands actually scroll overflowing content on both axes.

Dashboard GPU and hardware rows now fill the current viewport. Saved panel widths act as relative proportions, and partial final rows use the remaining space. Surface refreshes no longer restore fixed saved widths over responsive layout. Custom workspace columns are capped by the actual occupied slots.

Validation: Release build, native panel and scrollbar checks, native chart interaction and workspace/editor checks, nine native overview fixtures including saved widths and three-GPU layouts at 1200/1800/2480 DIPs, and all 262 telemetry checks. Captured and inspected the popup and wide dashboard renders. A local .NET 10 SDK under ignored artifacts/ was needed because the system SDK is .NET 8. NuGet vulnerability lookup was unavailable during the offline build. Installed application files have not been replaced.

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

## 2026-10-27 — Windows actions and chart-local navigation

Replaced the main window's traffic-light buttons with labeled Minimize, Maximize/Restore and Close actions at the top right. Panel Options/Open and the navigation Menu now have visible labels. Shared command buttons and selected menu items use a subtle five-pixel accent glow; Settings offers persisted accent colors that update the existing controls immediately.

Removed cross-chart cursor broadcasting. Hover and pinned inspection belong to the targeted graph, including detached views, with the normal arrow pointer and no graph-wide help tooltip. The information panel composites an opaque sampled chart backdrop before blur so sharp underlying traces cannot leak through transparent areas; text stays sharp.

Added a wrapping toolbar above each plot with labeled zoom, pan, pause/resume and live-reset controls. The readout shows zoom percentage, visible duration, exact start/end times and live/paused mode. Plot hit testing and scales use the chart's dimensions below the toolbar. Zoom/pan clears stale pinned details; raw data and exports remain unchanged.

Validation: Release build, 262 telemetry regression checks, native panel and chart checks, and overview fixtures from 760 to 2480 DIPs pass. Regression checks include narrow toolbar geometry, local hover cleanup, zoom readouts, selection after layout changes, and dynamic accent glow. NuGet vulnerability lookup was unavailable during the build (NU1900); compilation succeeded. The telemetry suite needed normal Windows performance-counter access. Existing unrelated working-tree edits were preserved. This updates the repository build, not an installed or already-running copy.
