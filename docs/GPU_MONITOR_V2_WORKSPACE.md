# Workspace presets and hardware recovery — Phase 06 foundation

The version-1 workspace data contract, named JSON persistence and binding resolver are implemented. They are not automatically loaded by the dashboard, and this increment adds no visible controls, drag/resize behavior, new panels or chart types. The approved shell remains unchanged. Phase 06 is not complete.

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

The next UI increment must connect this contract to the annotated Add Panel/metric picker and panel workspace, then implement reorder/resize/merge and keyboard alternatives with native screenshot review. It must retain missing selections as clearly unavailable and keep one primary GPU card per actual device. Do not mark tasks 06.01–06.08 or layout visual acceptance complete from this backend foundation.
