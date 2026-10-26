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
