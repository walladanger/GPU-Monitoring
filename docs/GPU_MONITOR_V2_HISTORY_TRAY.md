# Persistent history and tray quick-view

Implementation starts from PR #33 (`e1a9db3`). Existing collectors and approved Overview composition/paints are preserved. Master tasks: 14.01/14.02 (tray quick-view), 14.04 (optional bounded SQLite history), and the related preferences/exports portions of 14.05/14.06.

## Implementation slices

1. Opt-in SQLite numeric-measurement archive: versioned schema, raw units/values/timestamps/UUIDs, idempotent writes, bounded retention/rows/database, readonly queries and failure isolation. Test actual SQLite files including reopen, pruning, schema refusal and null measurements.
2. Integrate an independent bounded writer with collection while the app is open/minimized; add compact-menu settings and a historical sensor browser with timestamp/range, charts and real raw exports. Storage failure must never stop live collection; disabled history must create no database.
3. Add a configurable tray hover mini-dashboard using actual enabled sensor capabilities and stable IDs, with health/timestamps, dismissal, monitor-aware placement, Windows backdrop where supported and graceful fallback. Keep tray right-click navigation/presets/settings accessible.
4. Add portable/native Windows checks, captures and packaging evidence. Compare all default Overview fixtures against PR #30, push focused PRs and leave target hardware/physical input/DPI/human visual approval explicit.

Numeric measurements only are archived in this increment; request/agent/trace event persistence remains part of its separate correlation work. No prompt/message bodies or API credentials are stored. Closed means collection stops; minimized/tray means collection continues. SQLite history is off by default. Chart history is shown separately and never masquerades as a live reading.

## History behavior

Enable through the existing compact settings menu → **Persistent history…**. Default recording is off. Storage is `%LOCALAPPDATA%\GpuMonitor\history\telemetry.sqlite`; preferences remain in the existing settings file. The writer has sixteen queued batches, up to 8,192 measurements per batch, 1–30 day retention (default seven), 250,000 rows and a 128 MiB database page limit. SQLite WAL/shared-memory files are additional transient files. Full queues report dropped batches; initialization or write failure is visible without changing provider health. Existing records remain when recording is disabled; retention is enforced on writer open/write, not while the app is closed or recording is off.

**Browse saved history…** searches actual archived source/device/metric/unit groups, including disconnected hardware. Choose a sensor, a timestamp with UTC/offset and a 1/5/15/30-minute window. It loads at most 10,000 newest matching measurements and clearly labels them archived. Its chart context menu exports the displayed raw CSV/JSON or PNG. Different historical unit revisions remain separate. No stored readings are restored as live hardware data.

Schema 0 (empty/new database) migrates transactionally to schema 1, with an application identity. Unknown/newer/unrelated schemas and corrupt files are reported rather than replaced. All sensor identities, timestamps, gaps and raw values/units survive reopening. The normal NVIDIA-only launch and optional-source configuration remain unchanged. The archive observes normalized numeric batches; it does not collect another copy of hardware data.

Checks: portable real-SQLite regression cases and `--verify-persistence` native settings/browser gate. Windows CI and final screenshot evidence are recorded on the PR. Target-machine physical input, actual DPI transitions, sustained overhead and human visual acceptance remain explicit release gates.

## Tray quick-view behavior

Hover the existing tray icon for 350 ms, or choose **Show quick-view** from its right-click menu. The popover uses current source snapshots (without copying historical arrays), does not activate itself on show, and has no separate taskbar entry. Moving away from both icon and popover dismisses it after a short grace period; its close button, Escape when focused, main-window restore and tray-menu opening also dismiss it. Closing the app disposes the popup/timers and stops collection.

**Tray quick-view settings…** is accessible from the existing compact settings menu and tray menu. Choose up to eight actual advertised sensors, including mixed units, and a surface opacity. Missing saved source/device IDs stay unavailable, never substituted. Automatic defaults use only actual GPU utilization, VRAM, temperature and power capabilities; zero GPUs produce zero invented readings. An explicitly empty selection stays empty. Live, stale, unavailable and paused readings keep their real sample timestamps; exact raw values/units remain in each row's tooltip.

The popover requests Windows 11's system transient backdrop where available; it uses an opaque navy fallback elsewhere, and system transparency policy still controls the result. Placement uses native cursor/window/monitor working-area coordinates and clamps against the taskbar/monitor bounds. DPI changes re-evaluate placement. Native fixture evidence is not a substitute for actual shell-hover/multi-monitor/high-DPI or human backdrop acceptance.

Tray right-click also provides saved workspace presets, history, GPU details, system panels and exit. All settings/quick views reuse the existing navy/green/blue theme; no default Overview layout or paints are changed. `--verify-tray` covers actual native settings, independent units, non-activation/taskbar flags, bounds, stale/missing bindings and dismissal using synthetic snapshots/programmatic opening.
