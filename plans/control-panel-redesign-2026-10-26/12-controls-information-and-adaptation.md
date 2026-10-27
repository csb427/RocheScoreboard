# 12 — Apple character beyond glass

26 October 2026. Latest design refinement: improve buttons, information presentation and symbols without increasing glass everywhere. Keep the accepted floating navigation and essential scoring geography. This document extends 09/11; public output remains excluded.

## Research findings applied

Apple's desktop guidance distinguishes control sizes, shapes and prominence rather than giving every command the same visual weight. It also advises removing glass backing from passive toolbar information that otherwise looks clickable. These principles explain why a black-and-glossy skin alone did not resolve the earlier concepts. [Build an AppKit app with the new design](https://developer.apple.com/videos/play/wwdc2025/310/).

Symbol weight, scale and baseline should work with neighbouring type, not merely fit a square. Apple's symbol system supplies those relationships; our Windows design needs its own consistent licensed implementation. [Configuring and displaying symbol images](https://developer.apple.com/documentation/uikit/configuring-and-displaying-symbol-images-in-your-ui).

The specific Roche layouts and sizes below are proposals. No Apple framework, font or symbol asset is installed or bundled.

## Controls: refine the entire family

- Make frequently used scoring buttons large rounded actions, with clear text/number hierarchy and restrained tonal depth. They should not look like glossy jelly, rectangular web cards or identical generic pills.
- Use compact rounded rectangles for ordinary editors and secondary commands. Use capsules where the surrounding toolbar/transport grouping makes them concentric.
- Primary actions use a confident neutral or meaningful team fill. Secondary actions have lower visual weight while retaining the same readable target size. Consequential actions are separated by position and wording; red is not a default decoration for End innings.
- Group related tools on one surface instead of giving each an independent glass bezel. Do not put a tiny rectangular native-looking control inside a larger custom control.
- Selection is a settled value/lens/check; hover and keyboard focus remain different states. Text measurement and outer target geometry do not change on hover.
- Scoring feedback begins immediately within the button. The surrounding panel, log and navigation do not twitch in response.

## Information: make it feel deliberately structured

- Use inset grouped player/settings surfaces with aligned rows and quiet separators. Keep reading surfaces opaque.
- Score and timer figures are the primary information. Names carry weight; supporting goals/behinds, balls and dates have a quieter but readable role. Use tabular figures and reserve enough width.
- Passive quarter/clock/on-air labels should read as information, not a row of shiny buttons. Do not put every value inside its own pill.
- Logs use a clear team marker, meaningful event title and aligned time/ball notation. Hover reveals editability without expanding the whole app.
- Avoid oversized identity monograms or repeated background logos in the operator workspace. Real team logos remain an asset decision, not a replacement by generated letters.
- Use spacing, alignment, type size and weight before adding borders or more card layers.

## Symbol system

Use a consistent optical grid, rounded joins, stroke weight suited to the label, and a restrained filled/selected treatment. Keep navigation labels. Settings should use a recognisable gear; Output a display; Media a playback surface; Scorecard a structured list. Avoid arbitrary icon changes between sports.

The interactive study uses the host's existing vector library with coordinated sizing/weight. It is not an SF Symbols export, and it does not settle the final WPF icon implementation. A later icon proof should compare all navigation, transport, scoring, status, editor and alert glyphs together at actual control size; selected/focused/disabled states must remain readable.

## Adaptive structure

The interface reflows; it does not scale down one large bitmap/canvas.

| Space | Live scoring | Supporting content | Navigation / decisions |
|---|---|---|---|
| Wide, about 960+ DIP | Paired AFL controls or cricket keypad/player context on left | Independently bounded history on right | Shared floating dock; anchored menus stay inside work area |
| Medium, about 640–960 DIP | Same command order, generous labels and targets | History moves below when its minimum readable width cannot be preserved | Dock remains labelled; form groups wrap |
| Compact, about 320–640 DIP | AFL teams retain paired commands where readable; cricket retains two-by-four run order | Supporting log/table scrolls or follows scoring; no giant fixed side pane | Dock labels can move below icons; sheets and action rows reflow |
| Short height | Preserve readable commands and explicit clock/readiness | Main content scrolls in the available body; avoid expanding the native window | Reserve fixed navigation/action access; keep sheet footer and focused field reachable |

These ranges are starting points, not claims of universal fit. At an unusably tiny window, enforce a practical minimum and offer scrolling rather than clipping or shrinking all text. Test width AND height, text scaling and DPI before choosing native production breakpoints.

The browser study demonstrates width reflow. Its auto-height conversation canvas is not proof of short-height native WPF behaviour. Mixed-DPI, real window resize, caption/snap, UI Automation and native input need a later WPF component study.

## Stability details identified during the study

- Reserve scrollbar space so a taller destination does not move centred navigation sideways.
- Keep history height bounded; inserting many events must not increase the outer layout height.
- Animate one selection lens from its current presentation value; page rendering must not snap it to the destination first.
- Defer layout writes out of resize-notification callbacks and ignore height-only notifications when only width adaptation is needed.
- Closing a menu consumes the dismissing interaction and restores focus to a valid source. Modal sheets trap focus and expose Cancel/Close.
- Update score/figure text in place so repeated scoring preserves the button element and its position.

These were improvements to the isolated design study. They are not claims that those exact causes have been diagnosed or repaired in the production app.

## Stage progression

Completed: research refresh, refined button/information/symbol contracts, and a first interactive operator study. Next design work should extend integrated sheets, input/choice families and compact-height states after review of the shared interaction feel. Native application integration remains a later explicitly authorised implementation task.
