# Panel interaction increment

This branch builds on [PR #30's reference-render corrections](https://github.com/walladanger/GPU-Monitoring/pull/30), preserving that dashboard composition, palette and existing collectors. It adds interaction to custom panels; primary GPU cards and built-in hardware panels remain unchanged.

## Dragging and resizing

Drag a custom panel's title onto another custom panel. The highlighted border is the drop target: its upper half inserts before it and its lower half inserts after it. Reordering stays in the existing responsive grid; this is not a free-position canvas. Dropping outside a target, cancelling, or clicking without movement leaves the layout unchanged. The existing Alt+Up/Down and menu commands remain available for ordering, including panels outside the current viewport.

The bottom-right grip previews a resize. Width snaps to 1–4 slots and chart height to 1–8 rows, using the same limits and preset dimensions as the existing menus. Pointer movement is measured in workspace coordinates, so moving the grip itself does not feed back into the size calculation. Vertical-only resizing preserves a saved width preference that is wider than the viewport; an actual horizontal step starts from the visible span. Narrow viewports still clamp the rendered width.

Releasing the grip commits one undoable change. Escape cancels an active physical drag through WPF Thumb.CancelDrag; cancelled resize restores the original chart preview. Preview movements do not write presets or repeatedly rebuild the layout. Named preset save/load persists completed changes through the existing version-1 schema.

## Merge, split and duplicate

The panel options menu now offers:

- **Merge into…:** choose the target panel. Its ID, title and chart type are retained. Selections are combined by stable source/device/metric identity, deduplicated, and limited to 16 sensors. Width grows up to four slots; height retains the larger row count. The source panel is removed only after validation succeeds. Known matching units can be checked even while a device is missing or its provider is stale; absent unit definitions, incompatible units and secondary-axis selections prevent the operation. Disabled destinations explain why in their tooltip.
- **Split into one panel per sensor:** replaces a multi-sensor primary-axis panel with individual sensor panels in the same position. The first retains the panel ID; additional panels receive new IDs. Each keeps its sensor binding, chart type and height, with a one-slot width. This is not a reconstruction of old pre-merge titles or sizes; Undo immediately restores the exact previous layout.
- **Duplicate panel:** creates a new independent panel ID with the same sensor bindings and dimensions, inserted after the original. It does not create another telemetry provider or sampler.

Split and duplicate obey the 64-panel limit. Failed transformations do not modify the input layout. Undo reverses the last committed layout change. Layout files remain version 1; GPU UUIDs, provider IDs, metric keys, measurements and timestamps are never rewritten by these operations.

## Verification

Portable production-source checks cover before/after drops and unchanged-drop behavior, snapping/minimum/maximum sizes, nonfinite coordinates, narrow-width preferences, merge identity/style/binding retention, duplicate-sensor elimination, incompatible or unknown units, split bindings, independent duplicate IDs and panel/sensor limits.

The existing Windows `--verify-workspace` gate additionally raises the actual title/resize Thumb event routes with deterministic workspace pointer coordinates. It checks drop hit-testing, deferred resize commit, cancellation restoring the visual preview, one-step undo, and the real merge/split/duplicate menu commands. The existing picker, preset dialog/menu, missing/reappearing hardware, native chart traces, screenshots, packaging and installer gates continue to run. The fixture uses synthetic measurements; it does not prove physical pointer behavior on the user's monitors or dual-3090 operation. High-DPI/multi-monitor interaction, visual signoff and performance acceptance remain target-machine checks.

Secondary-axis rendering, graph zoom/pan, broader chart families, persistent telemetry storage and tray hover remain separate production tasks.

## Chart interaction continuation

The [chart completion increment](GPU_MONITOR_V2_CHART_COMPLETION.md) enables two labeled unit groups for comparisons and merges. Merge preserves the target selections' axes and assigns new units only to compatible or free axes; a third unit or unknown source definition is rejected. Split normalizes each resulting single-sensor chart to its own primary axis. The earlier primary-only limits describe the original PR #31 increment.
