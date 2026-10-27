# Response, performance and form refinement

27 October 2026. Native WPF implementation following operator feedback.

## Concrete causes and changes

### Blue focus outlines

Several operator templates treated `IsKeyboardFocused` as proof of keyboard navigation. Ordinary mouse clicks also give controls keyboard focus, so clicking produced a second blue outline. `OperatorInputFocus` now tracks pointer/touch versus navigation-key input on operator roots. Buttons, fields, segmented choices, tabs and sliders show a neutral, correctly shaped focus indicator for keyboard use. Pointer selection retains its selected state without that extra ring. Native focus and automation remain enabled.

Batter/bowler lists also inherited the default Windows focus visual. Their list/item templates now suppress that default and provide their own neutral rounded keyboard indicator.

### Scorecard and material work

Returning to an unchanged cricket scorecard recreated the draft, innings navigation and figure controls. Re-entry now retains them when innings, revision and delivery counts match. A new delivery invalidates the cache. Explicit edit/refresh paths continue to force a fresh draft. Selecting the already active batting/bowling view is a no-op; a real switch renders once and uses a shared finite transition.

Transparent, borderless figure buttons previously still allocated a glass shader/sample pass because their border brush was nontransparent. Material participation now accounts for zero border thickness. Native foregrounds and hit targets are unaffected.

Live backdrop brushes use WPF render caching. The frosted decorative layer has a bitmap cache; text remains outside it. Shared surface transitions temporarily cache their composite, animate its transform/opacity, then restore its original cache and transform at completion or unload. These changes reduce repeated work rather than inserting an input delay.

### Shared AFL settings and output controls

The AFL settings rail was a separate legacy implementation. Existing live controls are moved, preserving their instances, names and event handlers, into `SettingsWorkspace`. It supplies the shared searchable categories, scroll ownership, small-window navigation, material preferences and glass rail.

The forms have a consistent font, larger labels, softer grouped surfaces, symmetric narrow team stacking and readable copy. Live clock choices are full native radio cards bound to the existing clock modes. Their selected fill is separate from keyboard focus. Legacy ticks/glows are hidden. Finals selection gains a gold surface and clearer ink. The output tab's presentation controls use semantic vector icons and simpler labels while retaining their existing routing handlers. Reset match spans the scoring footer's width and retains its confirmation workflow.

### Setup, icons and selection variety

- Sports use original coloured vector symbols; cricket, training, scorecard, player and plus meanings are explicitly supported rather than falling through to a generic document glyph.
- Player-add symbols are centred vectors instead of font-dependent plus characters.
- Saved presets have a bounded scrolling region. The crest preview occupies a stable row beside its action, avoiding a new preview block changing the form height after selection.
- The AFL countdown card uses the correct base row in its responsive pair. The quarter-length section has explicit separation. Wide and stacked bounds are checked.
- Setup Finals mode fills with gold instead of adding a tick badge.
- Toss uses filled native choices with a short selection rail and animated state fill, contained club crests and a clear confirm action. The cropped background crest was removed from these buttons.
- Idle screensaver uses an animated power-state control; other settings retain switches where appropriate. Selection does not use one universal tick treatment.

## Narrow public-output correction

The user explicitly reported a black gap during cricket overlay entry. The complete overlay shell now receives its first pose before synchronous scorebug compaction starts the panel clocks. This prevents the scorebug beginning its move while the shell still has zero height. The duration, easing, overlay content, scorebug geometry, scheduling and match calculations are unchanged.

The same sequencing correction applies to the free-hit alarm. The public rendering hash comparison shows two changed files: `Views/CricketOverlayHost.cs` and `Views/CricketOverlayHost.FreeHit.cs`. The other 46 files match the previous baseline. Output presentations were not redesigned.

## Research and skills

Used the installed Apple design/motion principles and project WPF guidance, preserving the framework and architecture. Relevant primary references:

- [Apple SF Symbols](https://developer.apple.com/sf-symbols/) and [HIG symbols](https://developer.apple.com/design/human-interface-guidelines/sf-symbols): semantic state, consistent optical framing and animation tied to meaning.
- [Microsoft WPF graphics performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging): caching static brush content and separating drawing from layout.
- [Microsoft WPF layout performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-layout-and-design): avoid unnecessary layout work and update existing render transforms.

The requested skill search found and installed [sf-symbols](https://github.com/rshankras/claude-code-apple-skills/tree/main/skills/design/sf-symbols) in the user's Codex skills directory. It is available for discovery on the next turn. It provides additional icon-selection/rendering vocabulary; its Apple-native APIs are not WPF APIs. The app uses original artwork. Another WPF reference found in the search contained unsupported/mistaken API examples, so it was not installed or used to change the app.

## Verification and limits

Reports and actual native screenshots live under `artifacts/operator-response-refinement/`. `verification-summary.json` identifies the final reports. The native suites overlap; their counts must not be summed as unique coverage. Release omits developer-only scenario checks, explaining its smaller count than Debug.

Targeted checks verify input-modality focus, neutral keyboard rings, transparent figure material bypass, filled-selection interpolation and rapid reversal, unchanged scorecard reuse, new-delivery invalidation, one rebuild per actual batting/bowling switch, shared AFL settings ownership and clock/quarter-length bounds at wide and narrow widths.

The frame observation report samples distinct WPF rendering timestamps during repeated tab changes. It is a local observation, not GPU profiling, proof that frames reached the monitor, or a frame-rate guarantee for other hardware. Native screenshots were reviewed in addition to assertions; they exposed lingering small typography and weak contrast that were corrected after the first passing run.

Actual saved operator appearance preferences remain unchanged after isolated testing. Existing unrelated working-tree changes are preserved. No release, version bump, commit or push was performed.

| Final verification | Result |
|---|---|
| Release native operator suite | 345 checks; zero failures |
| Focused interaction suite | 157 checks; zero failures |
| Debug cricket workflow suite, including developer previews | 350 checks; zero failures |
| Release cricket suite after the final free-hit sequencing correction | 294 checks; zero failures |
| Operator workflows | 49 checks; zero failures |
| AFL reliability stress | 120,000 operations; 120,067 assertions; zero failures |
| Startup | One main window, sports selection visible, no exception |
| Debug and Release builds | Zero warnings and zero errors |
| Source boundaries | 46 of 48 public rendering files unchanged; two overlay entry-sequencing corrections |
| Actual appearance preferences | SHA-256 unchanged |
| Repository checks | Whitespace validation passed; scoped project graph refreshed |

## User review

1. Click setup tabs, Back, fields and player-selection rows with the mouse, then navigate with Tab/arrows. Mouse clicks should not add blue keyboard boxes; keyboard focus should remain visible.
2. Repeatedly switch cricket Match/Scorecard and Batting/Bowling, then add a delivery. Check responsiveness, transition consistency and fresh figures.
3. Review the toss, choose either team and Bat/Bowl, and confirm. Club crests should fit; selection should fill smoothly and be obvious.
4. Resize AFL clock setup, select Count down, and adjust quarter length. The sections should remain separated and reachable by scrolling.
5. Review AFL settings categories and search, output presentation tiles and the full-width reset action at wide/narrow sizes.
6. Change a preset/logo, use player-add buttons and toggle idle screensaver. Check stable panel sizing and clearer symbols/state feedback.
7. Repeatedly show/cancel cricket overlays while scoring. Check for gaps during entry and confirm the existing scoring/output behaviour is retained.
