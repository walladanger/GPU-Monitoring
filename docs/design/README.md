# GPU Monitor v2 approved reference sources

The original image is the visual authority; the user's annotated image and
[text notes](ANNOTATED_REQUIREMENTS.md) override the specified layout/interaction
details. Both images were copied byte-for-byte from the 2026-10-23 handover ZIP.
No replacement images were generated. These files document a target, not an
executing application or a completed visual implementation.

| Asset | Native dimensions | Authority |
|---|---|---|
| `gpu-monitor-v2-reference.png` | 1536 × 1024 | Original eight-GPU navy/charcoal visual style |
| `gpu-monitor-v2-annotations.jpg` | 2047 × 623 | User's authoritative functional/layout corrections |

`reference-manifest.json` locks SHA-256 checksums. Verify unchanged reference bytes
from the repository root using `python tools/check_design_references.py`.
`reference-colors.json` records exact pixel samples and their coordinates; these
are starting color tokens from a raster image, not a claim that gradients have a
single flat color. White primary text, muted grey labels, navy/charcoal surfaces,
green GPU traces, blue VRAM traces and amber power accents remain the target.

## Reference geometry (image pixels, approximate)

| Region | Bounds / dimensions |
|---|---|
| Header | y 0–73; title x 81; title roughly 20 px, subtitle 14 px |
| Sidebar | x 0–222; 8 px horizontal inset; selected row y 91–141 |
| Main inset | x 239 (17 px past sidebar); right edge 1521 (15 px inset) |
| Summary row | y 90–204; four tiles separated by about 15 px |
| GPU grid | y 219–727; four columns / two rows; about 15 px gaps |
| First GPU card | x 239–549, y 219–466; roughly 310 × 247 px |
| Card styling | about 9 px corner radius, fine 1 px border, subtle surface shading |
| GPU heading/footer | headings about 16–18 px; labels 14 px; footer values 16 px |
| Comparison card | x 239–1521, y 742–998 |
| Chart treatment | thin green/blue lines, subtle matching area fill and low-contrast grid |

The reference font cannot be identified reliably from the raster alone. Windows'
installed Segoe UI is a candidate for a comparison build, without redistributing
a font file. Font choice, metrics, icons and shadow approximations need screenshot
review before any visible deviation is accepted. Do not call typography exact yet.
Navigation icons should retain the reference's small outline silhouettes; use
roughly 18 px icons with thin strokes and blue selected-row treatment. Pixel-level
stroke/shadow measurements remain pending visual implementation.

## Permitted differences from the original image

- Product title is GPU Monitor; screenshot H100 values must never become live data.
- Render exactly 0/1/2/4/8 cards for those detected fixture counts. Zero devices gets
  an honest empty state, one device gets one card, two get two large charts. No
  placeholder cards. Use two columns only when window content width and DPI allow;
  stack when narrow, with more columns only on genuinely wide content.
- Collapse navigation to icons; condense header controls into a settings menu.
- Relocate summary information as described in the annotations to free graph space.
- Enlarge the utilization and VRAM graphs; keep axes inside chart panels, compact
  power/temperature/fan data underneath, and configurable system panels below GPUs.
- Exactly two measured GPU utilization series for a two-GPU comparison, until more
  compatible measured series are explicitly selected. No synthetic extra traces.
- Implement the annotated synchronized crosshair, resizing, panel controls,
  exports, process timeline and tray quick view in their planned increments.

These permissions do not authorize an unrelated palette, shell or dashboard layout.

## Screenshot acceptance procedure (not yet executed)

Use deterministic 0/1/2/4/8 fixtures with a fixed UTC clock and fixed metric values.
Capture the **running WPF app**, recording viewport, DPI, Windows version, SDK,
font availability, commit and fixture identity. Start at 1536 × 1024 / 100% scaling;
then check narrow, ultrawide and 150%/200% scaling. Retain both full screenshots and
comparison crops for sidebar, header, GPU borders/headings, chart interiors and
compact footers. Compare static styling separately from annotated layout changes.
Mask only known dynamic text/cursor regions, never borders, spacing or whole cards.

The original live-looking traces are not deterministic numeric fixtures, so a raw
whole-image pixel comparison is not a sufficient acceptance test. Establish an
accepted deterministic native golden screenshot after the shell exists, then use
repeat captures to detect drift. Pixel-difference thresholds remain pending that
first native comparison; none are invented here. User visual signoff is required
before merging visible deviations. Reference checksums only protect the input
assets; they do not establish app fidelity.

Phase 01 progress: 01.01/01.02 input assets locked, 01.03/01.04 initial measurements
and colors recorded, 01.05–01.09 still require native font/icon/screenshot work,
01.10 remains the user signoff gate. Related legacy issue:
[#2](https://github.com/walladanger/GPU-Monitoring/issues/2).
