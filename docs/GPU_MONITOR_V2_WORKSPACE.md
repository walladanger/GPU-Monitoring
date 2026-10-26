# Workspace presets and hardware recovery — Phase 06 foundation

The version-1 workspace data contract, named JSON persistence and binding resolver are now connected to native custom-panel controls below the primary GPU cards. Presets are loaded explicitly. Existing GPU card styling is retained; the new controls and their remaining limits are described below. Phase 06 is not complete.

## Contract

The [JSON Schema](workspace/workspace-v1.schema.json) describes the file format. `WorkspaceLayout` contains Version (1), Name and an ordered list of panels. Each panel has a unique stable Id, Title, WidthSlots (1–4), HeightRows (1–8), Chart (`line` or `area`) and 1–16 selected series. List order is panel order; dimensions are logical workspace units, not screen pixels or a commitment to a new dashboard design.

Each series binds Source, DeviceId and MetricKey, with Axis 0 (primary, default) or 1 (secondary). Keys are source-scoped IDs, never GPU display indices or names. The same series may be used in different panels but is rejected when duplicated within one panel, even on another axis. These are persisted selections, not a claim that referenced measurements exist.

Presets allow zero to 64 panels, names up to 80 characters and titles up to 96. Unknown JSON properties, unsupported versions, invalid sizes, unsupported chart identifiers, invalid/duplicate identities and control characters are rejected. Runtime validation complements the schema's structural rules, including rejecting whitespace-only text. Future versions require an explicit migration; files are not silently upgraded or overwritten.

## Named persistence API

Create `WorkspacePresetStore` with an explicit local preset directory. `SaveAsync("dual-3090", layout)` writes `dual-3090.json`; `LoadAsync("dual-3090")` returns a validated contract. Preset IDs use 1–64 ASCII letters, digits, hyphens or underscores, preventing traversal through the name. The caller chooses the directory and owns the operation; the monitor does not search a disk or sync layouts elsewhere.

Reads and writes are limited to 256 KiB. Read buffering is bounded even if a file grows. Saves validate before touching the preset and use a uniquely named temporary file in the same directory, then replace the destination. Cancellation/error cleanup removes the temporary file; validation failures and pre-cancelled writes preserve an existing valid preset. Each replacement is one file, not a transactional multi-file archive or a power-loss durability guarantee.

## Resolving measured bindings

`WorkspaceResolver.Resolve(layout, engine.Snapshot())` returns each panel's original selections plus states: SourceMissing, SourceStarting, SourceStale, DeviceMissing, MetricMissing, Unavailable or Available. It does not mutate the preset, replace a missing GPU with a different display index, synthesize values or create devices. Reappearing UUIDs resolve automatically the next time the resolver is called. This also distinguishes an absent source from an existing metric with an unavailable reading.

Unit definitions come from the selected provider. Different units on one axis produce an AxisError; assigning a second unit to the secondary axis is valid at the contract level. This does not implement a dual-axis renderer. Missing definitions defer unit checking until a source supplies them. Known units are checked even if the selected physical device is temporarily absent.

## Verification and remaining UI tasks

Portable checks round-trip real JSON files, preserve order/dimensions/bindings, reject invalid names/versions/duplicate identities, enforce bounded reads, preserve existing data on cancelled writes, and resolve normalized NVIDIA fixtures through UUID index changes, disappearance, stale state and reappearance. Separate tests cover missing definitions, unavailable values and incompatible-axis units.

The native controls below now connect this contract to the annotated Add Panel/metric picker and custom workspace, including menu/keyboard sizing and ordering. Mouse drag/resize grips and merge/split are implemented in the subsequent interaction increment; full target-machine interaction/visual acceptance remains open. Missing selections must remain clearly unavailable; the primary GPU region keeps one card per actual device. Do not mark the entire Phase 06 complete.

## Native panel controls

The Overview now connects this contract to a Custom panels region directly below the detected-GPU cards. The existing GPU cards, palette, navigation and comparison sources remain intact; the new region adds the annotated workspace controls and can increase scroll height.

- **Add panel:** opens a themed native sensor picker. Search by group, device, title, source or metric ID. Only real per-device advertised capabilities from enabled sources are offered. Unavailable sensors are labeled N/A; selection survives search filtering. Choose up to 16 sensors with the same unit and a line or area chart.
- **Panel options (⋮ or right-click header):** edit sensors/rename, move earlier/later, set width (1–4 slots), set height (1–8 rows), or remove. Width clamps to the current viewport without changing the saved preference. Keyboard alternatives are Ctrl+E, Alt+Up/Down and Ctrl+Delete when the panel is focused; the options button and menus are also keyboard accessible.
- **Presets:** save a named layout, load a saved layout, undo the last panel change, or clear custom panels. Saves use the existing bounded atomic JSON store under `%LOCALAPPDATA%\GpuMonitor\workspaces`. Presets are loaded explicitly; normal startup does not silently apply one. An invalid load leaves the current layout intact.
- **Charts:** show actual selected source histories, retain gaps/stale state, synchronize their inspection cursor with GPU plots and use the global 1/5/15/30-minute range. Right-click the chart for raw CSV/JSON export. Byte and MiB displays use GiB while exported samples retain their original units and values.

The default source set remains NVIDIA-only. Other hardware/runtime metrics become selectable when their providers are explicitly enabled with the existing `--sources` configuration. This increment does not add a source-configuration UI or make a runtime compulsory. It does not collect additional data just because a preset refers to an absent source.

Missing source/device/metric bindings remain in both the editor and saved layout. Reappearing UUIDs resolve again. Incompatible same-axis units are rejected by the picker or displayed as an explicit panel error on import. Version-1 secondary-axis selections are retained but show an unsupported-renderer message; this first UI increment does not silently plot incompatible units together or rewrite such selections.

The subsequent [panel interaction increment](GPU_MONITOR_V2_PANEL_INTERACTIONS.md) adds title dragging, snapped resize grips, merge/split and duplication while retaining these menu/keyboard controls. Chart pan/zoom, additional chart families and secondary-axis rendering remain later increments; target-machine interaction and visual acceptance are still open.

`--verify-workspace` runs the actual Add Panel modal, search, checkbox and Apply flow in WPF, then exercises chart projection, editing/order/sizing, undo, named save/load, malformed-file preservation and missing/reappearing GPU bindings. It captures `overview-verification/workspace-controls-1536.png` and validates actual colored native chart traces and selected raw sample counts. Data is explicitly synthetic. Existing 0/1/2/4/8-GPU and narrow-view checks still run. These checks are not physical dual-3090 validation or human visual signoff.
