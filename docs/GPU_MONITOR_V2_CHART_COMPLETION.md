# Chart interaction completion

Base: PR #31. Preserve PR #30 dashboard composition, existing backend, single-axis chart paints and default ranges.

1. Add left/right unit-aware axes to custom charts; retain raw samples and units for inspection/export. Edit and persist two unit groups, reject incompatible units on a shared axis. Keep unavailable bindings. Verify projection, independent GiB conversion and actual native traces/labels/editor/preset recovery.
2. Add bounded time zoom, pan, pause/reset and per-series visibility through chart context menus and keyboard/pointer alternatives. Share the displayed window with inspection and raw exports; do not pause collectors. Add PNG export. Verify limits, live updates while paused, raw-unit exports, missing samples and native routes.
3. Prepare packaged Windows acceptance entry point: bounded actual dual-GPU/system collection, UUID checks, native checks and readable results. Mark physical pointer, scaling, monitor and human visual signoff as pending unless tested on the target machine. Do not simulate hardware evidence.
4. Run all portable checks, Release build, design hash lock, Windows native CI and installer. Compare default screenshots to PR #30; independent final code review. Push and open focused stacked PR(s), no merge.

These tasks complete this chart/acceptance increment, not all phases of the master plan. Advanced chart families, trace/process views, SQLite history and tray quick view retain their existing phase IDs and remain separate work.

## Execution evidence

232 portable production-source checks pass, including unit allocation, raw-unit CSV and bounded time navigation. A Release cross-build has zero warnings/errors. The first Windows run at `edcbba970c8894dddb56fbbf5789d284795aa724` passed actual native mixed-unit editor, labeled axes, independent scale conversions, exact inspection, zoom/pan/pause/reset, visibility and PNG/raw export checks. Independent review found a panel focus route; a real promoted mouse-event regression reproduced it on Windows run 40118714484. The fix retains descendant chart focus; final green evidence belongs on PR #32.

[Target acceptance runner and manual gates](GPU_MONITOR_V2_TARGET_ACCEPTANCE.md) keep actual hardware, synthetic fixture and pending human results separate. This session cannot complete machine-dependent gates while the registered desktop is offline.
