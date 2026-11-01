# Revised Windows preview verification

This is the first-feedback revision of the261db075 Windows preview, not a native desktop certification.

| Check | Result |
| --- | --- |
| Combined affected-workflow Linux/Avalonia run |45 passed,0 failed/skipped;1m12s |
| Final activation follow-up |5 passed,0 failed/skipped;10s; adds actual revealed-Edit pointer activation |
| Distinct cases across both runs |46 (four overlap; not a single46-case run) |
| Windows Release cross-build |0 warnings/errors;25.40s |
| Windows Debug cross-build |0 warnings/errors;25.55s |
| Native translated test-project build |0 errors;190 existing warnings, unchanged count from before this pass |
| Test-only final rebuild |0 warnings/errors |
| Source translation/parity |49 XAML surfaces,307 C# files,575 UI event bindings |

Coverage: shape geometry and actual sport choices, footer/back/cancel, Training pointer hover, delivery-row height/full-width/header separation/hover/reduced motion/keyboard, active and stale editor guards/cancel, separate overlay content/grouping/rebind/presentation eligibility, compact/large output typography, four actual player-picker flows, existing combined scorecard corrections/reload/rejected operations/settings/saved selection/tabular/large corrected figures/AFL scoring-undo-reload invariants. Product source did not change after the Windows builds and45-case run; the final follow-up added one test input.

Native Windows WPF appearance, cold-start timing, physical input/UIA/DPI/audio/video/monitor and OS behavior are unrun here. Native Mac desktop acceptance is unrun. First-use animation skipping remains unresolved. Historical font-host/selector/Mac-freeze limitations are retained in KNOWN-LIMITATIONS.md. No broad/global suite or fresh Mac CI/package run is claimed for this publication.

SOURCE-SNAPSHOT.json records exact included bytes, source baseline and protected previous snapshots. All425 Windows project source/assets match the tested working revision. Publication-only documentation and manifest do not change application source. No private test captures/logs, operator saves, credentials or generated build archives are included.
