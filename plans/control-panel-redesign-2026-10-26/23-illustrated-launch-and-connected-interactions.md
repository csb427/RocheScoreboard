# Illustrated launch and connected interactions

27 October 2026. Native WPF changes following the latest operator review. This is an implementation record, not a release announcement.

## Launch and navigation

The sports screen now uses three original illustrated scenes instead of isolated sport icons. Large, rounded AFL, cricket and training rows retain full-card native input targets. Text, supporting copy and scene detail adapt to available height; at 320 × 240 the three sport actions and toolbar remain reachable. The illustrations are frozen native drawings, with no network or image decoding on launch and no continuous idle animation.

Returning quickly from setup no longer leaves hover presentation in a reset state under a stationary pointer. The screen reacquires pointer state after its actual return, guarded by visibility and generation. Selecting a sport still dispatches immediately rather than waiting for an exit animation.

Navigation hints default to off for new preferences. Existing saved preferences remain respected. The switch now updates inherited operator guidance and tooltip availability on existing controls, including native launch buttons and custom action borders. Validation and match-readiness messages remain visible.

The release browser retains its rendered controls while its version, filter, query and width class are unchanged. Reopening it avoids rebuilding the same changelog tree. A changed filter or responsive layout invalidates that cache.

## Setup and selection

Both sport wizards use the same rounded club-preset composition: contained crest, full club name, abbreviation, team-tinted dark surface and a drawn selection check. Existing tile containers are retained when the underlying presets have not changed. White-primary clubs keep readable surfaces. Dragging, keyboard focus and existing selection handlers remain attached to the original containers.

Cricket player entry is an integrated sheet opened by a full-width Add player action. It accepts first and last names and independently animated Captain and Wicketkeeper choices. A blank name cannot be added. Cancel and navigation-away paths do not add players. Choosing a captain clears the existing captain; multiple wicketkeepers remain supported. Role property notifications update existing rows without refreshing the whole list or interrupting text/drag interaction. The old fields, their labels and the loose colour strip are hidden; rounded team headers replace that strip.

The selection language now includes an animated filled state and inset selection dot for command choices. Batter and bowler selection no longer draws a stray straight border below its rounded surface. Keyboard focus remains distinct from pointer selection.

Settled native screenshots revealed white role text on an inherited pale checkbox background. The new role template supplies its own neutral background and theme-aware selection fill. Additional native checks require selected-label contrast of at least 4.5:1 in dark and light palettes. The screenshot capture now waits for the sheet and selection motion to finish.

## Live operation and performance

Cricket reset occupies the full scoring footer row, matching the AFL footer alignment. The confirmation workflow is retained. Break/resume banner labels inherit the action's actual contrast colour, fixing pale text over its light background.

Cricket options offer Limited overs and Multi-innings, without a duplicate Multi-day button. Legacy stored formats are normalized for editing without dropping compatibility. Duckworth-Lewis is the label for supplied revised overs and target entry; no DLS calculation engine has been added. Hidden over-limit fields do not block changing to an unlimited format.

The batting/bowling scorecard selector has a stronger selected surface and border. Its current two figure trees are retained between view switches. Delivery revisions, edits, team/name metadata and responsive width changes invalidate relevant content. Existing editable controls and correction calculations are preserved.

Shared surface entries use a short unequal-axis expansion and translation, with an origin related to the initiating control for integrated decisions. Re-entry continues from the current presentation state. Completion and unload restore transforms and caches. These are finite WPF animations, and reduced motion remains supported. Scoring and navigation commits do not wait for decorative motion.

Pointer illumination and the press wash interpolate instead of jumping to a bright frame. Captain and wicketkeeper changes in the live lineup editor update existing role controls rather than reconstructing the editor.

## Narrow public-output correction

The reported cricket overlay background jump had a separate cause from the earlier overlay-shell entry ordering. The score rail's parent background was jumping to the new layout while its individual foreground cells animated. The rail now preserves its current visible position, and its parent and cells animate coherently. A full-height bound backplate covers the moving rail; rapid cancellation reverses from the visible position.

This pass changes that movement in `CricketScorebugControl.xaml.cs`. The earlier entry-order corrections in `CricketOverlayHost.cs` and `CricketOverlayHost.FreeHit.cs` remain. The other 45 public-rendering files match the recorded 48-file baseline. Public presentation layouts, score fonts, digit reservations and scheduling have not been redesigned in this pass.

## Research and implementation sources

Applied the installed Apple design, SF Symbols, MicroKit and project WPF guidance. Their native/web examples inform interaction principles; the app retains its WPF architecture and original vector artwork. No new skill installation or web framework dependency was needed.

- [Apple motion](https://developer.apple.com/design/human-interface-guidelines/motion): feedback should explain state and remain interruptible.
- [Apple materials](https://developer.apple.com/design/human-interface-guidelines/materials): balance material expression with foreground clarity.
- [Apple layout](https://developer.apple.com/design/human-interface-guidelines/layout): stable grouping and adaptable hierarchy.
- [Apple design principles](https://developer.apple.com/design/human-interface-guidelines/design-principles): direct manipulation and clear feedback.
- [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/359/): connected transitions and separation of control layers from content.

Local MicroKit checkbox and pointer-edge examples were inspected for drawn checks and finite, eased pointer feedback. React implementations were not copied into WPF.

## Evidence and review

Current reports and actual native screenshots are under `artifacts/illustrated-operator/`. `verification-summary.json` identifies the final reports, build results and source-boundary comparison. Suites overlap, so their counts are not unique coverage. WPF rendering-event intervals are local observations, not a monitor frame-rate measurement or a guarantee on other hardware.

Both final Debug and Release builds passed with zero warnings and errors. The full Release interface run passed 367 checks. The latest focused Debug run passed 183, including the final live label-colour bindings, narrow player-sheet footer and bowling-name cache invalidation. Cricket workflows passed 350 checks in Debug and 294 in Release. The separate operator suite passed 49, startup checks passed, and AFL stress completed 120,000 operations and 120,067 assertions with zero failures. Real appearance-preference file integrity was verified. Graphify was updated and the whitespace check passed.

Review the illustrated launch screen, rapid return to it, hint switching, preset selection, player entry and role selection, scorecard view switching, break-banner contrast, format options and mid-transition cricket overlay cancellation. Functional checks do not establish that the visual style matches every subjective Apple reference; the actual application remains the review surface.

No commit, push, release publication or version bump is part of this pass.
