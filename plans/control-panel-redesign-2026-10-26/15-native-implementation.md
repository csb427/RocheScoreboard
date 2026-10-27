# Native operator redesign — review

26 October 2026. Implemented in the existing Visual Studio WPF solution. App version remains 4.1.3. No commit, push or release was performed.

## Implemented

- Sports home keeps full-screen sport choices and floating utility actions. Both setup wizards use shared controls, keyboard-accessible navigation and symmetric responsive fields.
- AFL, cricket and training use matte information surfaces and satin commands. Floating navigation has labelled vector symbols and an interruptible liquid selection lens. Essential scoring geography and command handlers remain familiar.
- Pages scroll independently of navigation. Logs and queues remain bounded. Narrow consoles stack secondary regions; settings navigation moves above content where necessary.
- The shared family covers global/operator settings, scorecard figures, resume/updater prompts, delivery/score editors, colour picker, club library, logo crop, integrated cricket sheets and cloud launcher.
- Feedback starts on pointer-down. Inner surfaces compress while outer hit targets remain fixed. Springs retain visible position and velocity during redirection and use elapsed time rather than assumed frame counts.
- Switches, selection checks, dropdown entry, contextual surfaces and focus indicators share motion roles. First-time checks draw correctly; focus loss releases pressed buttons; retemplating cannot resurrect disposed controllers.
- Density changes update operator type, padding and control size without recreating controls or resetting caret/selection. A bounded snapshot transition bridges reflow without animating whole-console layout every frame.
- Appearance/density settings remain functional. Selected contrast colours use paired system highlight colours, and reduced motion settles controls directly. Existing preferences are retained; new preference files default to dark.
- Changelog and integrated decisions restore focus without stealing it from a following sheet or another window.

## Preserved boundaries

`Resources/ControlCentre.xaml` is merged locally into operator roots. It is not an application-wide theme replacement. Separate public output windows do not inherit it.

48 protected rendering files were compared with hashes captured before implementation and remain identical. These include public AFL/cricket/training scorebugs, presentations, overlay rendering/scheduling, rolling public scores, screensaver rendering and output-window geometry. Scoring, timing, match formats, persistence schemas and release packaging were retained. Storage-path edits outside the UI affect process-isolated self-tests only.

The architecture, target framework and dependencies remain unchanged. Materials and motion are native WPF adaptations, not SwiftUI or Apple's optical rendering APIs. No Apple fonts or symbol assets were bundled.

## Verification

| Gate | Result |
| --- | --- |
| Debug build, no restore | 0 warnings, 0 errors |
| Release build, no restore | 0 warnings, 0 errors |
| Native operator interaction suite | 49 checks, 0 failures |
| Native Release design/setup/layout/dialog suite | 165 checks, 0 failures |
| Cricket workflow suite | 350 checks, 0 failures |
| Startup | No exception; one main window; sports home and essentials/advanced navigation correct |
| AFL reliability stress | 120,000 operations; 120,067 assertions; 0 failures |
| Public rendering source boundary | 48 of 48 unchanged |

Coverage includes narrow/short, portrait and wide windows: 420 px-wide consoles, settings at 640×480, and wizard fixtures down to 360 px wide. Real WPF screenshots were inspected and used to correct clipping, compact targets, hierarchy and contrast. Native keyboard/automation semantics, rapid reversal, focus recovery and final animation states are exercised. Background desktop composition is allowed a bounded settling interval; these tests are not a frame-rate benchmark.

Self-test storage is latched to a process-local temporary directory before preferences/recovery/diagnostics are read. Tests do not overwrite normal match recovery, presets, colours, timer history or operator preferences. The updater screenshot uses a fixture release; no real update was downloaded or installed.

These results do not replace visual review of motion/taste on the user's hardware or verify every mixed-DPI monitor arrangement.

Reports and screenshots are under `artifacts/control-centre/`. The application graph was refreshed and queried successfully: 6,758 nodes, 16,832 edges and 303 communities. The source scan targets the application rather than bundled reference repositories. A duplicate generated graph was retained under review artifacts.

## Actual app captures

- [AFL console](../../artifacts/control-centre/release-final/afl-control-1550-tab-0.png)
- [Cricket console](../../artifacts/control-centre/release-final/cricket-control-1000-tab-0.png)
- [Compact settings](../../artifacts/control-centre/release-final/global-settings-640x480.png)
- [Integrated decision](../../artifacts/control-centre/release-final/integrated-decision.png)
- [Resume decision](../../artifacts/control-centre/release-final/resume-decision.png)
- [Updater](../../artifacts/control-centre/release-final/update-decision.png)
- [Colour picker](../../artifacts/control-centre/release-final/colour-decision.png)
- [Training](../../artifacts/control-centre/release-final/training-control-420.png)

## Research used

- [Apple — Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/): immediate response, interruption and physical continuity.
- [Apple — Adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass): selective material hierarchy and accessibility alternatives.
- [Microsoft — WPF transforms](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/transforms-overview): render transforms preserve layout during feedback.
- [Microsoft — Layout performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-layout-and-design): avoid unnecessary layout work.
- Installed Apple Design, native WPF, MicroKit and Transitions references informed selection, switches, contextual surfaces and state transitions. Their web/Swift examples were translated to WPF.

## User review checklist

1. Hover/press and keyboard-select sports and preset teams; quickly go forward/back in both wizards.
2. Reverse direction while switching floating tabs, then resize to narrow/short dimensions. Navigation and necessary controls should remain reachable.
3. Score, undo and edit several AFL/cricket events. Targets should stay still and commands apply immediately.
4. Try appearance/density changes while using settings search. Check legibility, typing, focus and scrolling.
5. Open/cancel colour, club, wicket/extras, resume and options interfaces. Check selection feedback, hierarchy and focus return.
6. Try training transport/queue controls and the compact remote launcher.
7. Confirm public output layouts still look as before; their redesign remains a separate task.
