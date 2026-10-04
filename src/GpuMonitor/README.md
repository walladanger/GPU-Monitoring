# GPU Monitor — native Windows app

C# / .NET 10 WPF desktop monitor for NVIDIA GPUs. No Python or browser is needed.

## Hardware discovery

Every launch immediately queries all NVIDIA GPUs through `nvidia-smi`. Each subsequent refresh rescans the full inventory. GPU panels and the GPU filter are generated from the returned devices, with no hard-coded two- or three-card limit. A third NVIDIA GPU appears automatically once its driver exposes it.

History belongs to the stable GPU UUID, so renumbering does not mix card data. Removed devices leave the active panels and filter. Failed queries retain the previous panels dimmed and mark them stale; polling recovers automatically. This version monitors NVIDIA GPUs only; AMD and Intel telemetry need separate collectors.

Supported charts: utilization, VRAM used, core temperature, power, fan percentage, graphics clock, and memory clock. Optional sensors reported unavailable by the driver do not receive charts. Essential metrics display N/A when unavailable. Fan percentage is the driver's reported aggregate, not individual fan RPM. Hotspot and VRAM junction temperatures are not included.

## Features

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
