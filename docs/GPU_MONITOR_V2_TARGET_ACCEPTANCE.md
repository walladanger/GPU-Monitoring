# Target-machine validation

The release gates need actual Windows execution and human approval. This runner collects evidence; it never substitutes synthetic GPU data for unavailable hardware and never grants visual or physical-pointer signoff.

Install/extract the current PR's Windows package. Double-click **Validate-GpuMonitor.vbs** beside GpuMonitor.exe. It runs without a console and opens acceptance.txt when complete. It needs no administrator rights and changes no GPU settings. Allow several minutes for the bounded checks. Its small native verification windows may appear temporarily.

Results go to `D:\DEBUGGING LOGS\GPU-Monitor\<UTC-run-id>` when D: exists, otherwise `%LOCALAPPDATA%\GpuMonitor\validation\<UTC-run-id>`. Each run has a separate folder. If the preferred output is unwritable, it falls back to the local app-data folder (or temporary folder when necessary) and records that choice in output-location-note.txt. Nothing is uploaded automatically.

The default expects two RTX 3090 GPUs. It checks distinct stable GPU UUIDs, live NVIDIA health, at least two distinct measured timestamps for each GPU's utilization and VRAM, and actual CPU/RAM data. It also captures the live native Overview using the real NVIDIA engine; that capture records the actual DPI scale and viewport bounds. Failure to detect the expected hardware is a failure, never a successful fixture fallback.

For another machine, use PowerShell with explicit expected hardware:

```powershell
.\Validate-GpuMonitor.ps1 -ExpectedGpuCount 1 -ExpectedGpuName 'RTX 4090' -OpenReport
```

`-ExpectedGpuCount 0` is reserved for an explicit GPU-free diagnostic/CI run. Its hardware result is N/A and it skips the live GPU window. It cannot establish dual-3090 acceptance.

The runner also exercises native synthetic overview, engine lifecycle, workspace and chart-interaction checks in separate processes with deadlines and exit-code checks. Synthetic captures/results and actual collection/native captures are in separate folders. Old generated verification output is removed before testing, so stale captures cannot count as new evidence. Raw actual collection remains available as JSON/CSV/TXT. Short-lived process CPU/memory measurements are included separately; they are not a sustained idle or performance-budget result.

The report always leaves these checks PENDING:

- Physical input: drag/reorder; resize preview, Escape/cancel and commit; one-step undo; click a custom chart then Ctrl+Space/Left/Right/Home; Ctrl+wheel; merge/split and saved presets.
- Actual display transitions: move between monitors at their configured scaling; ultrawide/fullscreen resize; precise hit targets; readable axes; suspend/resume.
- Human comparison with the approved design, shortcut/tray behavior and target-machine installer/uninstall.

Mark those only after carrying them out on the target machine and recording the observations. A green automated report does not approve a release. The registered desktop was offline when this increment was prepared; no target-machine result is claimed.
