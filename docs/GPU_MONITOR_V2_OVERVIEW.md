# Native Overview UI alpha

This increment builds on the LiveCharts track (PR #20) and the live-session track
(PR #19). The new native WPF Overview is the default launch on this branch;
`GpuMonitor.exe --legacy-ui` retains the previous dashboard. No browser host or
cloud dependency has been introduced. This remains a draft visual implementation
until Windows screenshots are reviewed against the approved original and annotations.

Implemented in this increment:

- Navy/charcoal surfaces, blue selected navigation, green utilization and blue
  VRAM traces following the approved image's palette.
- Borderless resizable window with minimize/maximize/close, draggable title,
  collapsible icon navigation and retained single-instance/tray lifecycle.
- One large card per actual discovered GPU, two wide columns where space allows,
  vertical stacking when narrow, compact power/core-temperature/fan footer.
- GPU details with UUID, PCI bus and optional clocks.
- Compact settings for pause/resume, rescan, refresh interval and minimize-to-tray.
- 1/5/15/30-minute chart ranges, axes inside cards, exact source-sample readouts
  and synchronized cursor time across visible plots.
- GPU comparison with exactly one series per current device and colors retained
  by UUID during index reorder within a session.
- Right-click CSV/JSON export of the identical visible raw samples. VRAM graph
  uses GiB while exported numeric data remains in source MiB with explicit units.
- Honest empty/disconnected/stale states. No simulated readings in normal launch.

For a two-GPU machine the new view has five plots: utilization and VRAM for each
card, plus a combined utilization plot. These use LiveCharts2 line/filled-line
series; no second chart library or advanced graph families have been added yet.

`GpuMonitor.exe --verify-overview` runs synthetic, deterministic 0/1/2/4/8-GPU
fixtures and a narrow two-GPU view. It creates real WPF screenshots and verifies
actual colored chart strokes inside the first card's chart interiors. Results
go beside the executable under `overview-verification/`; it never polls hardware
or saves preferences. CI runs this check with a two-minute timeout and uploads
screenshots/results independently of installer artifacts. Fixtures and screenshots
are test data, not evidence of physical dual-3090 operation.

Remaining scope and acceptance:

- Precise font/icon/spacing fidelity still needs native screenshot comparison;
  this is not a claim of pixel-perfect acceptance.
- CPU/RAM/storage/network/process pages, custom drag/resize/merge workspace,
  frosted tray-hover mini-view, chart image export and advanced graph families
  remain later phases. No nonfunctional navigation entries were added for them.
- Retention is session-only. Saved layouts and a persistent history database
  remain planned.
- Cursor/keyboard behavior, scaling, multi-monitor maximize, suspend/resume,
  physical hardware data and sustained rendering cost still need validation.
- Do not merge visible changes before the required visual signoff.

The original design source is preserved independently in PR #17. Use that exact
image and the user's annotation notes; do not generate replacement mockups.
