# Design QA

- Visual source: user-provided STM32CubeMX screenshot
- Release capture: `artifacts\final-app.png`
- Side-by-side comparison: `artifacts\design-comparison.png`
- Captured window: 1570 x 1130 physical pixels at 144 DPI (150% scaling)

## Checks

- The release window carries the source application's blue, white, and cyan technical-tool language without cloning unrelated CubeMX content.
- Installation selection, version, running state, localization state, primary actions, progress, and logs are all visible in the first window.
- Text and controls remain inside their containers; no clipping, overlap, or unintended horizontal scrolling is visible.
- Status colors add green, amber, and red semantics while preserving the source palette as the dominant product identity.
- Buttons use the Windows MDL2 icon library, have stable dimensions, and expose tooltips for icon-only controls.
- Cards use 6 px corners, are not nested, and retain consistent spacing at 150% Windows scaling.
- Disabled rollback and enabled localization actions are visibly distinct and legible.

final result: passed
