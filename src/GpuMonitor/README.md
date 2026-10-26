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
the scope and limits are described in `docs/GPU_MONITOR_V2_OVERVIEW.md` in the
source repository (that file is not included in this package).
Right-click an Overview graph to export its visible samples as CSV or JSON. Collection now runs independently of rendering, with pause/rescan and awaited shutdown. An explicit `--sources <configuration.json>` enables the configured backend sources; extra hardware/runtime panels remain planned. Normal launch retains NVIDIA-only collection.

- All-GPU and per-GPU views
- 1, 2, 5 or 10 second refresh intervals
- 5, 15 or 30 minute chart windows
- CSV export of the active GPUs' session history
- System tray integration with Open and Exit commands
- Saved refresh, history-window and minimize-to-tray settings
- Bounded in-memory history; resets on exit and is discarded for removed GPUs
- Read-only monitoring; no GPU configuration changes

## Package contents

Extract the entire ZIP into one folder. Keep these items together:

| Item | Purpose |
| --- | --- |
| `GpuMonitor.exe` | The application. |
| `Install.cmd`, `Install.ps1` | Per-user install (see Install or run). |
| `Uninstall.ps1` | Per-user uninstall, also registered in Windows Installed Apps. |
| `Validate-GpuMonitor.vbs`, `Validate-GpuMonitor.ps1` | Read-only target-machine validation (see Validation launcher). |
| `config\` | Optional runtime/adapter presets and their instructions (see Optional adapter presets). |
| `licenses\` | Third-party license notices (see Licenses). |
| `runtime\` | Bundled .NET runtime. Present only in runtime-bundled packages. |
| `README.md` | This file. |

## Install or run

Extract the entire Windows ZIP. Run `Install.cmd` to install for the current user, create a Start menu shortcut, and register the uninstaller in Windows Installed Apps. No administrator rights are required. Close GPU Monitor (including its tray icon) before installing an update. You can also run `GpuMonitor.exe` directly without installing.

The installer copies the application files, `Uninstall.ps1`, the validation launcher, and the `config`, `licenses` and (when present) `runtime` folders to `%LOCALAPPDATA%\Programs\GpuMonitor`. It stops with a clear message if `GpuMonitor.exe`, `config` or `licenses` is missing from the extracted folder. An update overwrites the files in the installed `config` folder, so keep a copy of any preset you edited.

Self-contained packages include .NET. NVIDIA drivers and `nvidia-smi` are still required. Settings live in `%LOCALAPPDATA%\GpuMonitor\settings.json`. Packages are unsigned.

## Uninstall

Use Windows Settings > Apps > Installed apps > GPU Monitor, or run `Uninstall.ps1` from `%LOCALAPPDATA%\Programs\GpuMonitor` (it refuses to run from anywhere else). Close GPU Monitor first, then type `YES` when asked. This removes the installation folder, the Start menu shortcut and the Installed Apps entry. Your saved preferences in `%LOCALAPPDATA%\GpuMonitor\settings.json` and the validation run folders (see Validation launcher) are kept. Working files the validator wrote beside `GpuMonitor.exe` are removed with the installation folder.

## Validation launcher

`Validate-GpuMonitor.vbs` runs read-only checks on the target machine. It changes no GPU settings and needs no administrator rights.

1. Double-click `Validate-GpuMonitor.vbs` in the folder that contains `GpuMonitor.exe` (the extracted ZIP or the installed folder). No console window appears.
2. Wait several minutes. Small verification windows may appear briefly. Do not close them.
3. When the checks finish, `acceptance.txt` opens in Notepad. The report starts with the count of automated failures; every line is `[PASS]`, `[FAIL]`, `[N/A]` or `[PENDING]`.

Results are saved in a new folder per run, named with a UTC run id:

- `D:\DEBUGGING LOGS\GPU-Monitor\<run-id>` when drive `D:` exists.
- Otherwise `%LOCALAPPDATA%\GpuMonitor\validation\<run-id>`.
- If that folder cannot be written, the runner falls back to `%LOCALAPPDATA%\GpuMonitor\validation` (or the temporary folder) and records the reason in `output-location-note.txt`.

Nothing is uploaded. Each run folder holds `acceptance.txt`, `acceptance.json`, `process-measurements.json`, the raw actual collection (`actual-collection`), and separate `synthetic-fixture-results` and `actual-native-results` folders. The checks also write their working output next to `GpuMonitor.exe` (`overview-verification`, `target-verification` and `monitoring-verification.txt`), so run them from a folder you can write to.

By default the validator expects two RTX 3090 GPUs. It checks distinct GPU UUIDs, live NVIDIA health, at least two measured utilization and VRAM timestamps per GPU, actual CPU/RAM data, and a live native Overview capture. A missing GPU is a failure, never a fallback to fixture data. For other hardware, run it from PowerShell instead:

```powershell
.\Validate-GpuMonitor.ps1 -ExpectedGpuCount 1 -ExpectedGpuName 'RTX 4090' -OpenReport
```

Optional parameters are `-DurationSeconds` (6-120, default 12), `-OutputRoot` and `-Executable`. `-ExpectedGpuCount 0` is a GPU-free diagnostic run; its hardware result is N/A and it is not hardware acceptance. The script exits with code 1 when any automated check fails.

Three manual gates always remain `[PENDING]`: physical pointer/keyboard checks, DPI/monitor/suspend checks, and human visual approval plus installer/uninstall on the target machine. A passing report is not release signoff.

## Optional adapter presets

The `config` folder holds optional presets for reading runtime metrics from servers you already run. Normal launch enables none of them; a preset is used only when you pass it with `--sources`.

| File | Purpose |
| --- | --- |
| `llamacpp.sources.json` | llama.cpp metrics at `http://127.0.0.1:8080/metrics`. |
| `llama.cpp-README.txt` | How to use the llama.cpp preset. |
| `sglang-vllm.sources.json` | SGLang at `http://127.0.0.1:30000/metrics` and vLLM at `http://127.0.0.1:8000/metrics`. |
| `SGLang-vLLM-README.txt` | How to use the SGLang/vLLM preset. |

Example, from PowerShell in the app folder:

```powershell
.\GpuMonitor.exe --collect ".\capture" --duration 60 --interval 2 --sources ".\config\llamacpp.sources.json"
```

Edit `Endpoint` in the preset if your server uses another address or port. GPU Monitor never starts, installs or reconfigures those servers, and an exit code of 0 does not prove a given runtime was live. See the two README text files in `config` for details and limits.

## Licenses

The `licenses` folder contains the notices for bundled third-party components: `LiveCharts2.txt`, `SkiaSharp.txt` and `HarfBuzzSharp.txt`. Runtime-bundled packages also carry the .NET `LICENSE.txt` and `ThirdPartyNotices.txt` in `runtime`. Keep these folders with any copy you redistribute.

## Build

Install the .NET 10 SDK on Windows:

```powershell
dotnet build GpuMonitor.csproj -c Release
dotnet publish GpuMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

To build the ZIP, run `packaging\Build-Package.ps1` (add `-UseInstalledRuntime` for the runtime-bundled layout). It writes `GPU-Monitor-Windows-x64.zip` next to the project file.

Run `GpuMonitor.exe --self-test` to validate inventory changes and parsing. It writes `self-test-result.txt` beside the executable. `--verify-ui` validates panels/options against real detected GPUs, saves a preview and exits; requires at least one NVIDIA GPU.

The repository's Windows workflow builds, checks the inventory behavior, and produces a self-contained ZIP plus an Inno Setup installer as downloadable workflow artifacts.
