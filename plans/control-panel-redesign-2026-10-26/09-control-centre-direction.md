# 09 — Revised direction: sculpted Control Centre

Status: DESIGN PROPOSAL, revised after the user rejected the first understated concepts. This is the current visual authority. No application implementation has been performed.

Latest review: the user supports this Apple direction, the familiar scoring geography and the floating navigation. Refine material intensity and proportions before implementation. Use the material allocation below; earlier high-gloss boards are an intensity study, not a requirement to make every button glass. The first interaction demonstration is specified in 11.

## The revised brief

The selected reference is **iOS Control Centre: bolder glass tiles and more sculpted controls**. Apple influence should be evident in structure, geometry, depth, selection, direct manipulation and transitions, not just a dark theme and rounded cards. Black remains the foundation. Colour makes commands, team identities, categories and state legible.

The refined balance is **glass for floating tools; satin for actions; matte for information**. Motion remains expressive on appropriate interactions even when the surface is opaque. Reducing gloss does not mean losing tactile response.

Essential AFL scoring stays in paired team columns with history to the right. Cricket retains player context above its familiar two-row run keypad and delivery history to the right. These are broad spatial commitments, not pixel-locked legacy layouts. Supporting screens and tab arrangements have full redesign freedom.

**Public output is excluded.** Do not redesign or change scorebugs, presentations, overlay rendering/algorithms, goal/boundary/wicket effects, ticker behaviour, quarter transitions, fonts, geometry, timing, screensaver, video or fallback output. The operator Output tab and existing preference editors may be redesigned, invoking their current commands/settings. Excluded public renderers must not inherit new operator resources.

## What changes from the first concepts

| Earlier direction | Revised direction |
|---|---|
| Thin outlined charcoal cards everywhere | Fewer enclosing boxes; action surfaces carry depth, content groups use space and quiet separators |
| Glass chiefly in a narrow navigation strip | Clearly sculpted navigation, primary action controls, selectors, menus and sheets |
| Similar rectangular treatment for all controls | A deliberate family of circular utility controls, capsule commands, squircle action tiles and compact editor fields |
| Blue border as the principal selected state | Settled white/tinted lens, clear value/check and material change; no duplicate inner rectangle |
| Conventional rectangular tab row | Floating navigation island with a travelling selection lens and a reserved footprint |
| Independent pop-up windows and generic alerts | An integrated task surface with an origin, focus hierarchy and return path |
| Fade/translate as the main motion vocabulary | Press deformation, internal light response, source-connected expansion, selection lenses and continuity during interruption |

The new boards deliberately make material depth easy to judge. Their bright rims are an optical study, not a mandate to outline every production control brightly. Final highlight intensity must be judged in an interactive Windows prototype.

## Material allocation after user review

| Material | Use | Avoid |
|---|---|---|
| Matte, opaque | Scores, player figures, batting/bowling context, match/delivery logs, scorecard tables, form bodies and ordinary settings groups | Glass borders around every data row; glossy background logos; reflections behind figures |
| Satin, opaque with a restrained tonal highlight | AFL Goal/Behind, cricket run/extras keypad, Pause/Undo, ordinary choice tiles and routine form buttons | Transparent/refracting scoring buttons, bright continuous rims, candy/chrome finishes |
| Smoked glass | Floating navigation, small floating utility clusters, anchored menus and integrated task sheets | Glass inside glass; using blur instead of a legible reading surface |
| Selection lens | A settled light or meaningful tinted segment/check on the material that owns it | A second white rectangle, a different control shape on focus, or a separate refracting layer on each inner element |

Sports tiles keep the sculpted shape but use a quieter satin body. Settings categories may occupy one glass navigation island; settings rows remain matte. Sheet bodies become more opaque where needed to protect long text and forms. Team colours remain purposeful, with accessible foregrounds throughout a control's full fill.

This allocation applies across both sports and Training. It supersedes earlier wording that could be read as requiring all primary action controls to be glass.

## Three visual planes

1. **Black workspace.** Nearly black, stable, full-window canvas. Team identity may contribute a local static tonal wash; no moving aurora or colour-cycle wallpaper.
2. **Information.** Scores, figures, names, logs, tables and form content stay crisp and calm. A delivery row is information with an edit affordance, not another glass object.
3. **Interaction.** Sculpted actions and floating tools have one coherent material boundary. A control looks pressable; a menu looks connected to its trigger; a selected segment looks physically settled.

Apple explicitly distinguishes material-bearing controls/navigation from the content layer. This proposal uses that separation even with the stronger Control Centre emphasis. It does not turn every table into glass. [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/).

## Material recipe for a later prototype

These are proposed Roche starting parameters, not extracted Apple shader values.

| Property | Starting proposal | Constraint |
|---|---|---|
| Canvas | Existing proposal `#09090B` | Operator-only |
| Smoked body | Controlled charcoal fill with approximately 0.72–0.90 opacity, adjusted per surface | Contrast assessed after compositing, never from alpha alone |
| Edge | One 1-DIP directional light-catching boundary, strongest on the upper arc | Not a second focus rectangle; no white box around text |
| Interior light | Soft clipped upper highlight and faint lower separation | Never crosses or obscures labels |
| Tint | Team/action hue carried through a small light/dark tonal range | Do not mix unrelated team primary/secondary hues into an accidental third colour |
| Elevation | Soft local separation, approximately 6–16 DIP shadow extent on transient surfaces | Pre-render/static treatment first; no animated expensive shadow radius |
| Selected lens | White/silver fill with dark text, or accessible meaningful tint | Separate from keyboard focus; checks/value remain visible |
| Background sampling | Optional operator-only measured experiment | A static sculpted material is the dependable baseline |

Dark glass should have shape and light without looking like chrome, candy plastic, a gaming HUD or a collection of floating 3D renders. The glyph and label remain two-dimensional and sharply readable.

Controls within one sheet belong to that sheet's material: use opaque/translucent fills and selection marks rather than stacking independently refracting glass on glass. Coordinate adjacent materials and light direction. Exact Apple optical rendering is not a native WPF feature and is not promised by a concept image.

## Geometry

- Circular utility commands: 40–44 DIP visual diameter with at least a 44-DIP primary target where practical. Label via tooltip/accessibility and show text for ambiguous actions.
- Large command capsules: roughly 48–60 DIP high, with radius equal to half the height. AFL Goal/Behind targets can be taller where the live composition supports it.
- Run keypad tiles: continuous rounded squares/rectangles, roughly 20–28 DIP corner radius at comfortable sizes. Exact size follows available width; keep the established command order.
- Task sheets and large choices: roughly 24–32 DIP outer radius. Dense fields retain smaller radii; not every element is a pill.
- Keep concentric insets: inner corner radius approximately outer radius minus inset. A simple WPF rounded rectangle is an approximation of a continuous corner; do not claim an exact Apple curve without a measured geometry implementation.
- Reserve the dock's footprint. It may look floating but cannot cover a scoring control, table row, focused field or bottom action.

Apple's shape guidance connects capsules, fixed shapes and concentric shapes; the desktop adaptation here is our proposal. [Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/356/).

## Typography and icons

Use a bold, clean system-like hierarchy. On Windows the planned family remains Segoe UI Variable, then Segoe UI. Do not install/bundle San Francisco or SF Symbols merely because the concepts evoke Apple. A later implementation should use licensed, consistent vector glyphs with a shared stroke/weight grid and proper accessible names.

Keep major operator scores and timers prominent, tabular and stationary. Use weight and spacing rather than ultra-condensed faces. Control labels never change width, tracking or font weight on hover. Retain meaningful text alongside sporting commands; volunteers should not have to decode icons.

Category icons may use small coloured squares as in iOS Settings. Navigation glyphs are generally monochrome; selection carries the primary emphasis. Team colours do not become the error/success language.

## Cross-screen integration

| Surface family | Control Centre treatment | Operational rule |
|---|---|---|
| Sports home | Three generous sporting tiles and a small floating utility cluster | All sports reachable at small sizes; saved-match action only after deferral |
| Setup | Floating step lens, symmetric team editors, strong Find club action, quiet grouped fields | Do not shrink only Home; both team columns reflow together |
| Club finder | Search capsule, list/detail, integrated favourite check, sport-specific scope | Local/offline/error states preserve entered choices |
| Global/live settings | Coloured category glyphs, grouped opaque rows, sculpted value controls | Clearly distinguish Application defaults from This match |
| AFL Match | Large team action capsules, calm scores, compact clock adjustment, bounded log | Frequent commands keep fixed targets and current shortcut semantics |
| Cricket Match | Shared material family, familiar keypad, calm player rows, anchored ellipsis menus | Preserve readiness/toss/innings workflow and all delivery rules |
| Scorecard | Strong innings and Batting/Bowling lenses, readable editable table, anchored value editors | Transaction validation before Apply; no decorative animation delays a commit |
| Output operator tab | Existing source choices as control tiles, automation switch, manual action tools, clear actual state | No change to public presentation/overlay implementation or scheduler |
| AFL Media | Sculpted transport tools and explicit existing takeover controls | Loading local media must not be mistaken for an on-air action |
| Training | Large calm timer, sculpted transport, direct queue manipulation | No gratuitous scorecard/match tabs or new recovery implied |
| Toss/player/wicket/extras | Shared integrated sheet, meaningful selection lenses, contextual primary action | Larger lists may use custom dropdowns; disabled choices explain why |
| Recovery/updater/reset/error | Same material and type family, hierarchy appropriate to consequence | Do not make dangerous actions visually equivalent to routine actions |

The detailed functional coverage remains in 02/04. This styling contract applies to every in-scope operator surface in that register, not only the pictured examples.

## Layout adaptation

Use meaningful layout classes rather than a giant scaled canvas. At wide widths preserve the familiar scoring/history split; at medium widths narrow or temporarily disclose supporting tools; at compact widths make supporting regions scroll/reflow while retaining readable scoring targets. Home tiles may become a vertical list of large horizontal sport controls. Setup pairs stack together. Settings categories become a compact chooser or horizontal category surface. Sheets fit the available work area and keep their action footer reachable.

Reference test sizes remain 1440×900, 1280×800, 960×640, 640×600 and 420×520 DIPs, plus text scaling and mixed DPI. They are review samples, not a guarantee that an arbitrarily tiny window can show all controls at once. Define a practical minimum and scrolling; never silently crop or globally shrink text.

## Accessibility and platform fidelity

Maintain independent Reduced motion, Reduced transparency and Increased contrast paths. Glass-free mode retains the same geometry, selected values, keyboard paths and command placement. Contrast is checked against the actual composited background and all meaningful states; decorative rim light is not the only control boundary. Keep Windows resize, snap, caption buttons, keyboard operation, IME and screen-reader semantics.

Control Centre's long-press expansion becomes an explicit ellipsis/click/keyboard action on desktop. No essential command requires a hidden long press, swipe, drag, touch gesture or hover. No interface haptics are assumed on a laptop.

## Direction review before execution

The user has selected Control Centre as the reference, but has not approved these precise shapes, brightness or compositions. Review the revised boards, then prototype a few interactions in isolation if requested. That is the point to tune the optical weight and motion—not by editing every production screen first.
