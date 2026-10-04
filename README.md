# GPU Monitor

Native C#/.NET Windows desktop monitor for NVIDIA GPUs. Automatically detects all cards on every launch and refresh, so adding a third GPU generates its panel and filter option without configuration.

## Install

Download **GPU-Monitor-Setup.exe** from the latest successful [Windows application workflow](https://github.com/walladanger/GPU-Monitoring/actions/workflows/windows-build.yml), then run it. The installer includes .NET, creates a Start menu shortcut, and supports removal through Windows Installed Apps. Administrator rights are not required. Packages are unsigned.

Alternatively extract the entire Windows ZIP and launch `GpuMonitor.exe`, or use `Install.cmd` for a per-user script installation. NVIDIA drivers providing `nvidia-smi` are required. Close the app, including its tray icon, before updating.

## Quick launch with desktop shortcut

Double-click `Run GPU Monitor.cmd` (or run `Run-GPU-Monitor.ps1`). It starts the app and creates a **GPU Monitor** shortcut on your desktop. It prefers the installed native app, then a `GpuMonitor.exe` beside the script or a local build, and otherwise falls back to the Python dashboard. Use `-ShortcutOnly` to create the shortcut without launching, or `-NoShortcut` to skip it.

## Features

- GPU panels and filter options reflect detected NVIDIA devices, with no fixed card count
- UUID identity keeps history attached to the same GPU after index changes
- Utilization, VRAM, core temperature, power, supported fan and clock readings
- Optional unsupported sensors are omitted
- Adjustable refresh and history windows, CSV export, system tray and saved preferences
- Query timeouts, stale-reading indication and automatic recovery

History is session-only and limited to 30 minutes. GPU hotspot and VRAM junction temperatures, AMD/Intel collectors, persistent history, alerts and automatic updates are not included in this version. Monitoring does not change GPU settings.

## Source and build

The native application is under [src/GpuMonitor](src/GpuMonitor). See its README for build and verification commands. The Windows workflow produces the runtime-bundled ZIP and conventional installer.

The original Python/browser prototype remains in `gpu_dashboard.py` with `Start GPU Dashboard.cmd`. It can still be run using Python and viewed at http://127.0.0.1:8765/.
