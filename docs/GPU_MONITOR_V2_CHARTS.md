# Native chart engine increment

LiveChartsCore.SkiaSharpView.WPF is pinned exactly to **2.0.5**, the MIT-licensed
release identified in its NuGet metadata. The package's repository commit is
`e8a67f59bae283d2e88b1e8d30ef1bf7bcf5b5d5`; its MIT notice and the installed
SkiaSharp/HarfBuzzSharp notices are included under `src/GpuMonitor/licenses/`
and copied into build/publish output. No commercial chart extensions are used.

The WPF target now specifies Windows 10 build 19041 to select the matching modern
SkiaSharp/LiveCharts assets. The product's Windows 11 target remains supported.
CI and the development launcher paths follow the new target framework. Existing
collectors, tray lifecycle and installer remain in place.

`TelemetryPlot` uses native CartesianChart, thin custom colors, subtle area fill,
fixed percent axes, timestamp labels and missing-value gaps. Animation is disabled;
GPU acceleration is not enabled. Normalized histories remain bounded by the
existing session. Chart projections include only samples in the visible window.
The plot supports synchronized timestamp inspection through a cursor callback,
and exposes the identical visible raw samples for export. The view never invents
additional device series. VRAM may be displayed in GiB while export retains MiB.

`PlotData` is portable and tested independently of WPF. Six new checks establish
time boundaries, future-sample exclusion, exact measured lookup, explicit gaps,
empty inspection, CSV quoting, numeric precision and export row selection.
Together with the live-session branch this gives **70 passing checks**. Release
cross-compilation succeeds with zero warnings/errors. Interactive behavior,
native chart rendering, performance budgets and visual fidelity require Windows
validation; these results are not a completed visual acceptance test.

This track builds on PR #19 (live telemetry session), itself stacked on #18/#16.
The new shell is a separate increment and must consume this chart engine.

Sources:
- https://www.nuget.org/packages/LiveChartsCore.SkiaSharpView.WPF/2.0.5
- https://github.com/Live-Charts/LiveCharts2/blob/e8a67f59bae283d2e88b1e8d30ef1bf7bcf5b5d5/LICENSE
- https://livecharts.dev/docs/wpf/2.0.0/overview.installation
