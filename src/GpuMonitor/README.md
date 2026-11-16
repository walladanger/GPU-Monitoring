# GPU Monitor — native Windows app

C# / .NET 10 WPF desktop monitor for NVIDIA GPUs. No Python or browser is needed.

## Hardware discovery

Every launch immediately queries all NVIDIA GPUs through `nvidia-smi`. Each subsequent refresh rescans the full inventory. GPU panels and the GPU filter are generated from the returned devices, with no hard-coded two- or three-card limit. A third NVIDIA GPU appears automatically once its driver exposes it.

History belongs to the stable GPU UUID, so renumbering does not mix card data. Removed devices leave the active panels and filter. Failed queries retain the previous panels dimmed and mark them stale; polling recovers automatically. This version monitors NVIDIA GPUs only; AMD and Intel telemetry need separate collectors.

Supported charts: utilization, VRAM used, core temperature, power, fan percentage, graphics clock, and memory clock. Optional sensors reported unavailable by the driver do not receive charts. Essential metrics display N/A when unavailable. Fan percentage is the driver's reported aggregate, not individual fan RPM. Hotspot and VRAM junction temperatures are not included.

## Features

The v2 development branch opens the new native Overview with larger LiveCharts2
GPU/VRAM plots, synchronized inspection and a GPU comparison. Use `--legacy-ui`
to open the earlier dashboard. The new view remains subject to visual signoff;
see `docs/GPU_MONITOR_V2_OVERVIEW.md` in the repository for scope and limits.
Right-click an Overview graph to export visible samples as CSV, JSON or readable text, or capture the chart as PNG. Collection runs independently of rendering, with pause/rescan and awaited shutdown. Normal launch collects NVIDIA, CPU/RAM, storage, network and supported process metrics through one shared collector per source. Failures remain isolated. CPU/RAM/network/storage navigation opens the sensor picker.

**Settings → Monitoring sources** configures optional SGLang, vLLM and llama.cpp metrics endpoints, AG-UI/usage/OTLP JSONL feeds, and Token Monitor auto-export JSON. Import custom Prometheus mappings where needed. Saving source changes requires restarting the app; editing sends no requests. Live health is shown for each active source. `--sources <configuration.json>` remains available for explicit per-launch overrides. ForgeCore-specific API support remains unimplemented.

For **Token Monitor**, enable its Settings → Collection → Data export → Auto-export to a folder, then select `token-monitor-export.json` in GPU Monitor's `tokenmonitor` source. See `config/Token-Monitor-README.txt` for measured totals, rate semantics and limits. No Token Monitor application code, transcript parsers or account readers are bundled.

**Activity** shows actual process observations and instrumented agent/trace spans on the chart timeline, with source filtering and CSV/JSON/text exports. Unknown durations remain points and estimated timestamps are labeled. The view reports its 24-lane/512-event display limits. It does not attribute device-wide GPU usage to an individual request.

- All-GPU and per-GPU views
- 1, 2, 5 or 10 second refresh intervals
- 5, 15 or 30 minute chart windows
- CSV export of the active GPUs' session history
- System tray integration with Open and Exit commands
- Saved refresh, history-window and minimize-to-tray settings
- Bounded in-memory history; resets on exit and is discarded for removed GPUs
- Read-only monitoring; no GPU configuration changes

## Install or run

Extract the entire Windows ZIP. Run `Install.cmd` to install for the current user, create a Start menu shortcut, and register the uninstaller in Windows Installed Apps. No administrator rights are required. Close GPU Monitor before installing an update. You can also run `GpuMonitor.exe` directly without installing.

Self-contained packages include .NET. NVIDIA drivers and `nvidia-smi` are still required. Settings live in `%LOCALAPPDATA%\GpuMonitor\settings.json`. Uninstall preserves preferences. Packages are unsigned.

## Build

Install the .NET 10 SDK on Windows:

```powershell
dotnet build GpuMonitor.csproj -c Release
dotnet publish GpuMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Run `GpuMonitor.exe --self-test` to validate inventory changes and parsing. It writes `self-test-result.txt` beside the executable. `--verify-ui` validates panels/options against real detected GPUs, saves a preview and exits; requires at least one NVIDIA GPU.

The repository's Windows workflow builds, checks the inventory behavior, and produces a self-contained ZIP plus an Inno Setup installer as downloadable workflow artifacts.

## Customizable panel surfaces

Every default GPU, CPU, disk and comparison surface has an actions button, a real separate-window action and a live resize grip. The actions menu selects multiple measured sensors (line/area overlays with up to eight independent unit groups, or up to eight stacked sensor lanes), restores the default view, and resets sizing. Graph-level controls select line/area/stacked-lane views and time ranges. Current readings and scales sit inside the chart. Surface and window scrollbars appear automatically when content does not fit. Closing a detached surface restores the same live view; collection remains owned by the dashboard. Closing the dashboard closes its detached windows.

Default surface dimensions and sensor choices persist by GPU UUID in settings.json. The current custom layout saves automatically and restores after restart, retaining missing-source selections. Named presets remain available. GPU process expanders show only matching NVIDIA compute processes, measured VRAM where available, and explicit disabled/stale/unsupported/empty states. Graphics-process attribution and per-process GPU utilization are not supplied by the current collector. Device-wide power, temperatures, fan and clocks cannot be attributed to individual processes.

Development logging starts with the executable and rotates at 2 MiB, retaining one previous file. Open **Settings → Open development log** or `%LOCALAPPDATA%\GpuMonitor\logs\development.log`. It records startup, panel creation, selection, resizing, detachment/restoration, inventory changes, preset failures and unhandled errors. It does not record process inventories or raw telemetry payloads. Verification runs write logs beside the build output instead of user settings.

`--verify-panels` checks panel window lifecycle, shared snapshot updates, multiple axes, live resize/cancel, scrollbars, persistence, readouts, logging and process availability states using synthetic fixtures. Existing `--verify-workspace`, `--verify-monitoring`, `--verify-overview` and `--verify-charts` exercise the surrounding UI. Physical pointer behavior across monitors and DPI changes still needs human verification.

Inspection time synchronizes across dashboard and owned detached graphs by default. Settings can disable synchronization. Hover details use a translucent rounded card with a blurred chart backdrop. Left-click within 16 display pixels of a measured point to pin and highlight it. Telemetry updates preserve that exact sample; click again, outside, leave the application, or press Escape to deselect. Navigating to another range clears the pinned point.

Each chart has compact accessible zoom, pause/resume and live-reset controls, with pan commands in its context menu and keyboard shortcuts. Status shows live/paused mode, zoom percentage, visible duration and start/end times. Zoom is bounded from 10 seconds to 30 minutes. Linked charts freeze their own samples at the same exact time range; Live resumes current data. Raw exports preserve source values and units, including in stacked lanes.

The main title bar uses right-aligned Windows Minimize, Maximize/Restore and Close actions. Panel actions have visible Options and Open labels. Hovered, pressed, keyboard-focused and selected action buttons use a subtle edge glow. Settings → Accent color changes that highlight throughout the app and saves the choice.

Settings, chart and panel action menus (including nested menus), sensor selection and tooltips share borderless dark navy surfaces. The backdrop is blurred separately from sharp text and controls, with a feathered 72-DIP surround (about three quarters of an inch at reference DPI). Chart/owner backdrops remain live; separate popups sample the desktop into memory when opened so areas beyond the app window also blur. Desktop samples are discarded when the popup closes and are never saved. Both scrollbar directions use slim blue tracks and rounded thumbs throughout the WPF app, including popup and detached-window content. Dashboard rows fill the available width as the window grows, with saved widths used as relative proportions. Resize targets sit on the outer corner and right/bottom surface edges. Native detached windows also retain normal Windows border resizing.
