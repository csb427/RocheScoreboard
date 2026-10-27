# 01 — Foundations

Status: PROPOSED. Values below are Roche design targets, not claims that these are Apple's exact product values.

Revision: read documents 09 and 10 first. The user selected bolder iOS Control Centre controls after rejecting the initial restrained dashboard appearance. Those documents supersede material, geometry and expressive-motion defaults below. This system is OPERATOR-ONLY; no output-window resources, fonts, templates or effects may inherit it.

## Personality

Confident, tactile and calm at rest. The important command is easy to find. A selected control feels settled. An opened menu has an obvious source. A successful operation feels complete. Team identity contributes character without colouring every surface.

## Material stack

| Layer | Appearance | Use | Behaviour |
|---|---|---|---|
| Canvas | `#09090B`, opaque | Main window, backgrounds | Stationary; no ambient animated wallpaper during live operation |
| Content | `#141416`, opaque | Score figures, tables, history, form groups | Quiet separators; no permanent drop shadow per panel |
| Sculpted action material | Smoked or meaningfully tinted glass appearance | Large scoring commands and choice controls | Physical inner response and coherent edge light; stable outer target; see 09/10 |
| Raised content | `#202024`, opaque | Editable rows, selected groups, local tool wells | Clear shape and contrast; no fake background blur |
| Navigation material | Dark smoked material over a controlled background | App navigation, compact toolbars, settings navigation | One material layer, restrained upper highlight, modest shadow only when floating |
| Transient material | Heavier dark material with a controlled scrim | Menus, anchored popovers, task sheets | Text/icons remain crisp and opaque; content underneath cannot interfere with reading |
| Feedback | Short tint/check/focus treatment | Press, selected, completion, validation | One owner per control; never a second rectangle around the label |

Glass is an enhancement to hierarchy. Its reduced-transparency and low-capability versions use an opaque fill with the same geometry, typography and behaviour. True backdrop refraction is a feasibility experiment, not a dependency of this design. WPF `BlurEffect` is not a drop-in background material.

Do not stack glass cards inside a glass sheet. Controls inside a sheet use fills, separators and selection states belonging to that material. Avoid strong rim-light animation, chromatic fringes and whole-screen blur during scoring.

## Colour roles

| Token | Proposed value | Use |
|---|---|---|
| Canvas | `#09090B` | Background |
| Surface | `#141416` | Main content |
| Raised surface | `#202024` | Local tools and editable controls |
| Primary text | `#F6F6F7` | Main labels, scores, values |
| Secondary text | `#B6B6BF` | Supporting labels and descriptions |
| Decorative divider | `#303034` | Nonessential grouping only |
| Functional outline | `#73737D` | Boundary when shape is required to identify a control |
| Action / focus | `#6BB6FF` | Primary action, focus and selected navigation |
| Action ink | `#07121E` | Text on the light blue action fill |
| Success | `#6BE2A2` | Confirmed save/connection/readiness with text/icon |
| Warning | `#F4C96B` | Caution, pending decision, unusual condition |
| Error / destructive | `#FF6B73` | Failure or a destructive command with explicit wording |

These are starting tokens, not measured certification for every composition. Validate actual text, icon and boundary combinations, including team gradients and the brightest possible logo content. Aim for at least 4.5:1 for ordinary text and 3:1 for large text; use stronger contrast for critical live figures. Decorative dividers are not the only control boundary.

On-air uses a broadcast symbol and text, not a red recording-style dot as its sole signal. Match running/paused, public display source and saved state each have their own label and meaning.

## Team theming

- Team name, logo/monogram and a narrow accent establish identity.
- AFL's two scoring panels retain corresponding team identities. The Goal action can use an accessible team-colour fill; Behind stays visually secondary and clearly labelled.
- Cricket batting/bowling identities use quieter colour context. Run values retain consistent semantics even when teams change.
- Choose accessible text after evaluating the full fill. If a team colour cannot support clear content, keep the command neutral and carry team identity in the accent/logo/name.
- Never rely on colour alone to communicate team, selection, wicket, error, current player or disabled state.
- Operator forms and live scoring backgrounds do not need giant faded logos. Team-specific sheets may use a controlled logo in a header area, with the reading surface protected.
- Public output theming is excluded and remains unchanged.

## Typography

Proposed Windows operator face: Segoe UI Variable where available, Segoe UI fallback. Use one family consistently rather than tall condensed broadcast faces in forms. Existing public-output fonts remain unchanged.

| Role | Starting size in WPF DIPs | Weight |
|---|---:|---|
| Screen title | 28–32 | Semibold |
| Section title | 18–20 | Semibold |
| Primary command | 17–20 | Semibold |
| Standard label/body | 15–16 | Regular / medium |
| Supporting label | 13–14 | Regular |
| Operator total / timer | 40–56 | Semibold / bold, tabular figures |
| Shortcut badge | 12–13 | Medium |

These are operator dimensions, not output-window pixels. Avoid uppercase microtext for every caption. Use sentence case, short labels, strong alignment and weight before adding more boxes. Reserve tabular digit geometry for clocks and figures so ticks do not shift neighbouring text.

Text scaling must cause layout reflow or scrolling, not universal shrinking. A long team name truncates predictably in the live operator strip and remains fully available in its editor or tooltip; do not reinstate long-name marquees by default.

## Geometry and density

- Base spacing: 4, 8, 12, 16, 24, 32 DIPs.
- Revised geometry: see 09. Large action controls use continuous rounded shapes and capsules; dense editors keep smaller rounded rectangles. Do not turn every field into an oversized tile.
- Nested radii are concentric: inner radius roughly outer radius minus inset, clamped at zero.
- Main pointer targets: at least 44×44 DIPs. Frequent scoring commands are substantially larger. Compact secondary tools may target 36–40 DIPs when labelled and well separated; validate keyboard/touch alternatives.
- Keep a reserved footprint for hover edit affordances and scrollbar tracks. Their visibility can change without moving content.
- Comfortable/Compact alter spacing and density within limits, not the semantic hierarchy or the positions of critical scoring commands.

## Component catalogue

Every component must specify default, hover, pressed, selected, keyboard-focus, disabled, busy, error and success states where applicable.

| Family | Design contract |
|---|---|
| Primary button | One clear verb; immediate press response; one shaped boundary; no idle animation |
| Secondary button | Neutral fill/outline; same target and motion language |
| Destructive button | Specific destructive verb; isolated from frequent commands; safe default focus elsewhere |
| Icon button | Recognizable icon, accessible name, tooltip, full-size hit target |
| Split action | Main operation and disclosure are separate targets with clear labels |
| Navigation segment | Stable width; one moving selected capsule; icon plus label at useful widths |
| Choice tile | Whole tile selectable; selected fill plus fixed check; one outline |
| Switch | Binary label describing the enabled state; native toggle semantics |
| Checkbox | Independent selection; drawn check within fixed marker |
| Radio group | Mutually exclusive options with explicit group label and arrow-key behaviour |
| Text field | Persistent label; clear focus; validation adjacent to the field; caret and selection preserved |
| Number/time field | Direct typing, clear unit, bounded stepping, selection-aware keyboard editing |
| Dropdown | Used for long option sets; selected value remains readable; custom menu appearance |
| Small option set | Visible selection buttons instead of a dropdown |
| Slider | Pointer follows 1:1; value visible; fine keyboard adjustment; no delayed live preview |
| Colour control | Wheel, saturation/brightness, hex, defaults and recents; explicit preview/apply/cancel policy |
| Search | Search icon, clear action, empty/loading/no-results/error states |
| List / table | Fixed row geometry; clear selection/focus; virtualized when long; no full-list entrance replay |
| Editable cell | Reading style at rest; hover/focus identifies editing; Enter/click opens a consistent editor |
| Disclosure | One header and chevron; measured limited expansion; state survives navigation |
| Popover | Anchored to trigger; close to affected data; Escape/outside dismissal; focus returns |
| Task sheet | Title, close, bounded scroll body, persistent action footer |
| Confirmation | Consequence stated plainly; clear safe and consequential actions |
| Banner | Persistent local/subsystem status; retry/details action; never hides essential commands |
| Toast | Brief completed action or reversible operation; deduplicated; errors do not evaporate |
| Tooltip | Delayed first hint, quick movement within a tool group; never the only source of essential information |
| Drag handle | Visible affordance; insertion preview; keyboard reorder alternative |
| Progress | Known stage plus honest progress; no fake percentage; cancellation rules depend on stage |
| Empty / unavailable | State and next useful action; no unexplained grey box |

## Responsive model

Reference canvases are 1440×900, 1280×800, 960×640, 640×600 and 420×520 DIPs. These are review cases, not a claim that every conceivable size can show the entire dashboard simultaneously.

- Wide: essential scoring left, bounded history right. Keep team scoring columns symmetric.
- Medium: reduce gutters and optional descriptions before reducing target size. History can become a narrower rail or explicit drawer.
- Narrow: retain the scoring order; stack team panels or player bands predictably, make body regions scrollable and keep a clear return path. Show history as a named destination/drawer rather than crushing it beside scoring.
- Short: allow scrolling without covering controls with a giant sticky bar. Sticky controls must have reserved space.
- Sports selection must keep all three sport choices and entry navigation reachable without cropped cards; compact horizontal cards can replace the tall concept tiles.
- Wizard: equal team columns reflow together into a single column; navigation remains reachable outside the body scroller.
- Settings: sidebar becomes a named category picker or compact horizontal list, not unreadable miniature text.
- Preserve focused field, caret, selection, scroll anchor and active category during reflow. Do not animate continuous window resizing.

## Accessibility modes

System-respecting default, expressive operator motion, reduced motion, reduced transparency, increased contrast, and scalable text are distinct settings. Reduced motion retains state and feedback. Reduced transparency keeps geometry unchanged. Increased contrast gives solid backgrounds and identifiable boundaries. Every icon-only command needs a name; every state needs a non-colour explanation. Destructive confirmation, focus trapping and UI Automation remain functional requirements.
