# Materials, setup and selection corrections

27 October 2026. Native WPF operator interface refinement following the user's app review. Version remains 4.1.3. No commit, push, release or public-output redesign.

## What the review identified

The previous material could read as a grey panel rather than glass. Settings category icons exceeded their intended slots. Team forms, weather controls, the club library and toss sheet retained older structures. Cricket format selection mixed a mouse-driven panel with an independently clickable radio label.

## Research and skills

Used the installed Apple Design and Fluid Design skills, with the project WPF guidance. Used Skill Scout to look for additional relevant skills before adding duplicates. Reviewed Apple's [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/) and [Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/356/). The applicable ideas are a distinct functional layer, light and colour from the environment, geometry-aware highlights, adaptive contrast, and material applied to the whole control.

Additional search candidates included OpenAI's SwiftUI Liquid Glass skill, browser displacement-map skills and a Compose/KMP glass skill. Their implementation targets are different from this WPF app. No further skill or application package was installed during this pass; existing references cover the needed motion principles. Apple-native APIs are reference material, not APIs available to this Windows app.

## Implemented

### Glass and background

- The shared operator button now uses the glass material and theme-aware ink.
- Regular surfaces admit more of the scene. White primary actions retain their emphasis with a small amount of transmission.
- The static backdrop has more spatial light and colour variation, giving the material something visible to refract without adding an idle animation loop.
- Local glass samples its local backdrop rather than accidentally capturing unrelated content from an owner window. Explicit sources for genuine menus and dialog backplates remain available.
- The packaged PS2 shader now bends straight edges perpendicular to the edge, rather than using a diagonal corner normal everywhere. Hover illumination remains pointer-responsive.
- Shader compilation still targets PS2 and uses the existing Windows compiler during development only. The compiled material is packaged; users do not need a shader compiler or extra runtime package.
- Solid surfaces, reduced motion and high contrast remain supported. High contrast removes the coloured scene pools.

### Settings navigation

- Category chips are 26 DIPs with a smaller, centred symbol.
- The underlying bug was `OperatorSymbol.MeasureOverride`: it always returned 24×24 even when a smaller size was available. Measurement now respects its constraints.
- Symbol rendering uses one uniform scale and an optical margin for stroke caps and rotating gear teeth.
- Below 560 DIPs, category selection and search use separate full-width rows. The content remains scrollable.

### A shared team editor

`WizardTeamEditor` composes both AFL and cricket team setup from the existing live input instances. Their bindings, event handlers, name-scope references, presets and match results remain connected.

The form now has a clear home/away heading, a full-width club-search action, saved teams, aligned name/abbreviation fields, colour controls, crest selection and preset actions. The responsive pair stacks both teams symmetrically at narrow widths. Legacy decoration and duplicate container structures are removed from the visible team form.

Venue-weather setup uses the same form treatment. AFL live weather controls and cricket weather preferences also use the shared controls and theme-aware text.

### Native full-card format selection

Each format is now one full-size native radio control. The outer display border has no separate mouse handler. Mouse hit testing covers the whole card, keyboard/accessibility selection uses the native radio pattern, and checked state updates the existing format logic. The old corner marker and inner-label-only selection are removed.

### Toss and club library

The toss uses a glass sheet with a clear sequence: winner, decision, summary, confirm. It shares the native animated selection marker and button response. Team tint is subtle; monograms fit their slots, and team-name ink follows selection and light/dark appearance. Invalid team-colour input has a neutral fallback.

The club library separates its backdrop from its glass toolbar and panes. Search and competition filters remain accessible, the list retains recycling/virtualization, and the selected-club actions remain distinct. The list can become shorter in a short window rather than forcing its previous large minimum height.

## Verification

Evidence is under `artifacts/apple-review-refinement/`. Native app captures cover the team editors, narrow layouts, venue weather, format selection, toss, settings and club library. They do not depend on the earlier inline browser visualization.

The regression work checks complete-card mouse hit regions, native radio accessibility selection, data retention across setup navigation, shared editor structure, small-window overflow, icon bounds, local material separation and light-theme toss ink. Existing suites also cover selection reversal, reduced motion, shader fallback, operator controls, scoring and cricket workflows.

The intermediate icon check caught the fixed 24-DIP measurement error. The build also caught an over-broad weather markup replacement before execution; neighbouring presentation control markup was corrected and included in the following native checks. Intermediate reports are retained separately from final reports.

Final measured results are recorded in `artifacts/apple-review-refinement/verification-summary.json`. Suites overlap; counts should not be summed as unique coverage. Visual feel on the user's display remains a review item.

| Final verification | Result |
|---|---|
| Release native Control Centre suite | 295 checks; zero failures |
| Final focused Apple interaction suite | 107 checks; zero failures |
| Debug cricket workflows | 350 checks; zero failures |
| Operator workflows | 49 checks; zero failures |
| AFL reliability stress | 120,000 operations; 120,067 assertions; zero failures |
| Startup | One main window; sport selection visible; no startup exception |
| Debug and Release builds | Zero warnings and zero errors |
| Public rendering source comparison | 48 files checked; zero changes against the preceding refinement baseline |
| Whitespace validation | `git diff --check` passed |

Native capture review also corrected the name/abbreviation alignment, missing spacing under format choices, incomplete capture timing for selection ticks, and light-theme team-name ink. The project graph was refreshed after the code changes.

## User review

1. Open global, AFL and cricket settings at wide and narrow sizes. Category icons should be small, centred and uncut; narrow search should occupy its own row.
2. Set up AFL and cricket teams. Check preset selection, club search, colours, logos, forward/back navigation and saved field values.
3. Click the title, description, empty space and edges of both cricket format cards. Selection should respond throughout; also try keyboard navigation.
4. Review the toss in both appearances. Winner and Bat/Bowl selection should be clear, with complete animated ticks and readable names.
5. Review club search and venue-weather controls, including a short window. Scroll to all actions and confirm selection still applies.
6. Try glass/solid and reduced motion. Check hover/press responses and rapid navigation, then confirm public output retains its existing design.
