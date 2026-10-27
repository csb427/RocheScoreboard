# Apple interaction refinement

26 October 2026. Native WPF implementation following review of the first Liquid Glass pass. Version remains 4.1.3. No commit, push or release was performed.

## This pass

- Sports selection dispatches its action immediately. Decorative tile feedback no longer holds navigation behind a timer. The settings launcher no longer adds an inner rectangular feedback surface inside its curved shell.
- What's New is now a release browser: a selected version, date and release spotlight, short New/Fixed sections, search, keyboard navigation and a compact version selector at narrow widths. Only one release's notes occupy the content viewport at a time.
- AFL and cricket setup share a floating glass step selector, independently scrolling body and fixed navigation footer. Labels and icons adapt together for small windows. Teams, clock/format, players, weather and other options use the same surface hierarchy. The AFL quarter-length editor and cricket over-limit editor now have large values, consistent native buttons and full-width preset rows.
- Live navigation uses one floating capsule and one moving selection body. Duplicate legacy press ownership has been removed. Display labels use sentence case; existing logical headers and shortcuts are retained. The selection lens stops requesting frames once it has settled to a visually insignificant fraction of a pixel.
- Original vector symbols respond to their purpose: gears turn, displays illuminate, chart bars fill, light/dark symbols change state, density rows respond, clocks turn and update trays fill. Native buttons, toggles and tabs drive the states. Symbols never transform their hit targets. Hidden/unloaded controls and reduced motion release animation clocks.
- Density changes reflow actual native text and then ease its optical bounds into place. This replaces the preceding screenshot crossfade. Text input, focus and selection remain native; rapid reversals start from the current visible pose and clean up temporary transforms.
- Glass shells have stronger surface highlights and a consistent radius. Low-alpha white backgrounds retain their intended translucency rather than becoming pale blocks. Opaque and light-theme fallbacks retain contrast.
- Settings now include saved Liquid glass and Reduce motion preferences, independent of public-output animation pacing. Native high-contrast and Windows motion preferences remain respected. Preference writes use the existing atomic storage helper.
- Scorecard numeric edits use labelled, full-height numeric inputs and a reachable primary action. The generic cricket form sheet sizes its body to the available space, keeps actions outside the roster's scrolling viewport and falls back to whole-form scrolling at exceptionally short heights.
- Home's analytical spring now uses elapsed time consistently, including delayed frames. Closing the home sheet has a bounded dispatcher fallback, so an occluded compositor cannot leave an invisible sheet intercepting input indefinitely.

## Boundaries

All 48 public-rendering files match the SHA-256 baseline captured before this pass. Public scorebugs, output presentations, overlays, score animation and screensaver rendering were not redesigned. Essential scoring controls retain their familiar geography. Match algorithms, accounting, persistence schemas, framework and packages were preserved.

The working tree already contained substantial earlier work. These notes describe this pass, rather than attributing the whole working-tree diff to it. Existing changes were preserved.

## Verification

Debug and Release builds completed with zero warnings and errors. The final Release runs passed:

- Native material, layout and interaction suite: 219 checks, zero failures.
- Operator navigation and interaction suite: 49 checks, zero failures.
- Cricket workflows: 294 checks, zero failures.
- Debug cricket workflows, including developer-only paths: 350 checks, zero failures (`final-debug-workflows.json`).
- Startup: one main window, no startup exception, correct sports selection and essentials/advanced navigation.
- AFL stress: 240 seeds, 120,000 operations, 120,067 assertions, zero failures.

Reports and native captures are in `artifacts/apple-interaction-refinement/`. The final Release reports use the `final-verified-` prefix; native captures are in `final-verified-native/`. Earlier intermediate reports are retained and include failures subsequently corrected; they are not the final result.

The checks include native layouts at 420×320, portrait and wide dimensions; wizard step-label ink bounds; rapid selection/density reversals; score-edit reachability and cancellation; shader and software fallback; reduced motion; and rapid home sheet closing/reopening. Tests use process-isolated preferences, recovery and diagnostics.

Review caught a legacy corner badge still being enabled by a format-change handler. It was removed while retaining the native radio selection and accent surface. The team editor's pinned-save test was corrected to scroll its actual body viewport, rather than the newly added outer fallback viewport. The break-resume test now waits for the actual bounded transition state instead of assuming background composition finishes at a fixed millisecond deadline. Its match-state assertions remain intact.

These checks verify behaviour and measured layout on this Windows machine. They do not establish a frame-rate guarantee on every laptop or replace the user's visual judgement of the stronger design.

## Research applied

The installed Apple Design, MicroKit, Transitions, WPF and team-orchestration references were used. The approved motion atlas and Control Centre concept boards guided the material and interaction hierarchy. Web and SwiftUI examples were adapted to the existing WPF architecture; no web UI framework was added.

- [Apple: SF Symbols 7](https://developer.apple.com/videos/play/wwdc2025/337/) — meaningful stateful symbol animation and continuity.
- [Apple: SF Symbols](https://developer.apple.com/sf-symbols/) — symbol families and motion as part of interaction feedback.
- [Apple: Adopting Liquid Glass](https://developer.apple.com/documentation/TechnologyOverviews/adopting-liquid-glass) — functional material hierarchy, legibility and accessibility.
- Local MicroKit icon-swap and Transitions icon-swap examples — small, bounded state changes rather than moving an entire hit target.

All vector glyphs in this implementation are original WPF drawings. No Apple fonts or SF Symbols assets are bundled.

## User review

1. Hover the home settings button and immediately enter/leave each sport.
2. Browse, search and filter What's New, then close and immediately reopen it.
3. Change theme and density repeatedly; try Liquid glass off and Reduce motion on.
4. Resize both setup wizards, visit every step, select presets and change clock/over values.
5. Switch AFL/cricket live tabs rapidly and inspect selected-symbol feedback.
6. Edit/cancel cricket scorecard runs at small sizes; scroll a team roster and confirm Save stays reachable.
7. Confirm public output presentations retain their prior appearance.
