# iPhone controls and navigation refinement

26 October 2026. A further implementation pass in the existing WPF app, based on the user's request for stronger iPhone, Control Centre, search and interaction design. Version remains 4.1.3. No commit, push, package installation or release.

## Implemented

### Search as one component

Settings, What's New and both sport-specific club libraries now use the same native `OperatorSearchBox`. It is one editable TextBox, with a leading vector search symbol, capsule material, contextual clear target and an animated returning placeholder. Typing and clearing filter results immediately. Only decoration animates; the query never waits for it.

The first Escape clears a nonempty editable query; Escape on an empty query remains available to the containing sheet/window. The clear button preserves keyboard focus and two-way text binding. Read-only fields cannot be cleared. Rapid clears cannot let an old placeholder animation interfere with newly typed text. Old duplicate release-search controls were removed.

### Direct manipulation and commands

- Shared switches now compress and broaden their knob during a press. Their track, label and hit target remain stationary. Release resumes the spring from its current pose, preserving the native checked state.
- Shared range controls now show the filled and remaining track. The grip responds to hover and drag without changing native drag geometry, keyboard operation or accessibility range values. Horizontal and vertical ranges remain native WPF Tracks.
- Colour-picker saturation/brightness sliders retain their full colour gradients and live adjustment. Their grips use the same interaction behaviour. Colour-picker labels and fields now use the shared hierarchy and theme resources.
- Native command menus have a shared glass surface, source-aware entry origin, check state, icon, shortcut, separator and submenu support. Commands execute immediately through native menu semantics. Dismissal does not introduce an artificial command delay.
- Three-dot buttons render a stateful vector symbol without changing their underlying command content. Existing handlers and menu lookup paths remain valid. Search clear, favourite, copy, edit, replace, swap and undo symbols use the same original vector family. Filled paths animate their actual fill opacity; gears return to an equivalent pose rather than jumping between unequal teeth.
- Cricket player-action menus use these vector symbols in place of the old MDL2 characters. Existing dismissal, keyboard navigation, stale-match guards and anchored placement remain in place.

### Club selection

Club rows use quieter grouped-list surfaces, larger team names, a stable selection check and a separate favourite symbol. Filtering, sorting, sport separation, logo handling and preset storage continue through their existing services. The favourite command explicitly says Save to presets or Remove from presets, with an accessible saved state. Header, rows, detail and search adapt to shared light/dark resources.

### Setup handoff stability

Native review found an older AFL setup-to-match transition still moving the entire scoring interface and forcibly activating/focusing the window at completion. It is replaced with a cancellable optical handoff using shared motion timing. Scoring hit targets stay in their final positions; the retiring wizard relinquishes input immediately. Reduced motion completes the handoff directly. Re-entry, leaving the match view and window closure invalidate old callbacks. A bounded dispatcher fallback retires the old input layer when composition frames stop. There is no delayed focus grab.

The setup capture test now waits for the handoff's actual completion before measuring the live dock. Previously it sometimes photographed the retiring setup screen after a fixed 220 ms delay.

## Scope and safeguards

The 48 public-rendering source files are checked against a fresh pre-pass SHA-256 baseline. Public output windows, presentations, overlay layouts and scoring animation are excluded. Essential scoring geography, sport rules, accounting, recovery schemas, target framework and package dependencies are retained.

Animation clocks/subscriptions are released on unload, cancellation or reduced motion. Slider template replacement reattaches to the current native thumb; old grips do not retain state. No continuously captured backdrop or new rendering dependency was introduced.

## Research and interpretation

- [Apple: Design intuitive search experiences, WWDC26](https://developer.apple.com/videos/play/wwdc2026/292/) — recognisable search anatomy, clear and cancellation behaviour, placement appropriate to the content being searched.
- [Apple: Communicate your brand identity on iOS, WWDC26](https://developer.apple.com/videos/play/wwdc2026/251/) — consistent, recognisable custom symbols, purposeful colour and restrained branding.
- [Apple: Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/356/) — floating functional layers, grouping, distinct primary actions and source-related presentation.
- [Apple: SF Symbols](https://developer.apple.com/sf-symbols/) — layered state feedback, variable rendering and symbol motion.
- [Apple: Materials](https://developer.apple.com/design/human-interface-guidelines/materials) — glass belongs to the functional control/navigation layer, with legible content above it.

The installed Apple Design, MicroKit, Transitions and WPF references were used with the approved motion atlas. The web input-clear and menu examples were adapted to bounded native interactions. Their DOM/React implementation and long decorative clear delays were not introduced. No Apple fonts, marks or SF Symbols assets are bundled.

## Verification evidence

Debug and Release builds completed with zero warnings and errors. Final results:

- Native operator material/layout/interaction checks: 252, zero failures.
- Operator navigation and interaction checks: 49, zero failures.
- Release cricket workflows: 294, zero failures.
- Debug focused interaction checks: 64, zero failures.
- Debug cricket workflows including developer paths: 350, zero failures.
- Startup: one main window, no startup exception and correct essentials/advanced navigation.
- AFL stress: 120,000 operations and 120,067 assertions, zero failures.
- Public-rendering baseline: all 48 SHA-256 hashes unchanged.
- Source graph refreshed after code changes.

Final verification is recorded in `artifacts/apple-iphone-refinement/`. Final reports use the `complete-` prefix and native captures are in `complete-native/`. Earlier intermediate runs are retained separately; they include failures caught during implementation and are not the final result.

The focused Debug report is `debug-focused/apple-interaction-self-test.json`. These suites overlap; their counts should not be summed as independent coverage.

The new native checks exercise two-way search binding, repeated clear/type cycles, Escape/read-only behaviour, range automation and keyboard control, drag cancellation, switch press geometry, native menu icon/shortcut/submenu invocation, narrow and light-theme search, both club libraries, and setup transition cancellation/re-entry/reduced motion. Tests use process-isolated preferences, recovery and diagnostics.

The native run caught a custom-control TextChanged delegate hookup problem in the club library; an exact event signature corrected it. A narrow search assertion was also corrected to measure the clear target while visible, rather than using its cached collapsed geometry. The release empty-state check now invokes the real shared clear button and verifies that release content returns.

These checks cover measured layout and behaviour on this Windows machine. Visual intensity and the remaining individual surface details still benefit from review in the real app; passing geometry checks is not a promise of perfect appearance or frame rate on every machine.

## Review in the app

1. Search settings, What's New and the club library. Clear by mouse and Escape, then immediately type again.
2. Hold/release a switch; drag a slider and use its arrow keys. Try colour-picker saturation and brightness.
3. Open three-dot menus, inspect icon feedback and use keyboard navigation. Check that commands still execute once.
4. Search both sport libraries, select a club and add/remove its preset favourite.
5. Complete AFL setup, including with reduced motion. Leave or restart setup quickly and check for stale screens, input blocking or focus jumps.
6. Resize setup, settings and live controls. Confirm public output still looks as before.
