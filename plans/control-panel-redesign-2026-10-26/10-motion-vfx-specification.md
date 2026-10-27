# 10 — Motion, VFX and micro-interaction specification

Status: PROPOSED; operator interfaces only. This extends 03 and is authoritative where the stronger Control Centre direction differs. These are Roche design targets, not Apple's exact shipping constants. A later review progressed to an isolated browser study (13); no application implementation has been performed.

Material refinement after user review: motion does not require glass everywhere. Satin scoring/choice buttons use restrained fill/depth feedback; matte information does not shimmer or refract. Glass-specific illumination and source-connected expansion belong chiefly to navigation, menus and sheets. The end-to-end first demonstration and proportions are specified in 11.

Latest motion clarification: document 14 adds genuine selection-shape deformation and coordinated density/typography transitions. The current browser study's sliding lens and immediate density switch do not yet implement those requirements. Research and design discussion continue while visualization access is unavailable; production implementation remains on hold.

## What makes the experience feel connected

A button, the surface it opens and the return path share an identity. A selection lens travels to its new place. A dragged object follows the pointer rather than chasing it. A reversal starts from what is currently visible. A successful edit settles into its result. Optical feedback and movement respond together, while scoring/model operations remain independent and immediate.

Apple's fluid-interface work treats responsiveness and interruptibility as foundations, not decorative extras. Its newer material guidance shows source-connected surfaces and interactive controls. The mapping below is our Windows sporting-workspace interpretation. [Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/), [Build a SwiftUI app with the new design](https://developer.apple.com/videos/play/wwdc2025/323/).

## Proposed motion language

| Role | Expressive proposal | Standard / Reduced |
|---|---|---|
| Pointer-down | Immediate state; inner surface to 0.975 scale over 70–80 ms, optical depth reduces | Standard 0.985; Reduced material change only |
| Release | Retarget to 1 with controlled settle, nominal 160–200 ms | Standard 140 ms; Reduced 80 ms colour change |
| Hover | Local interior illumination over 120 ms; leave over 180 ms | Standard simple tint; Reduced no travel |
| Choice/check | Selected fill/lens begins immediately; check draws over 180–220 ms | Reduced shows check immediately |
| Switch | Thumb tracks current state, nominal 240–280 ms settle; optional 1-DIP overshoot in Expressive only | Standard critically damped 220 ms; Reduced instant thumb position + brief tint |
| Navigation lens | Current position to target, nominal 240–300 ms; label remains stable | Standard 220 ms; Reduced snap lens |
| Source-connected menu | Circle/capsule becomes or anchors one surface, 260–320 ms open, 200–240 ms return | Standard 180/140 ms anchored reveal; Reduced 80–120 ms opacity |
| Integrated task sheet | Origin-linked surface expansion 300–360 ms, return 240–280 ms; focus scrim begins immediately | Standard 260/180 ms; Reduced short opacity |
| Sheet substep | Shared shell stays; body translates at most 8–12 DIP with 180–220 ms content transition | Reduced crossfade or immediate |
| Information row insert | New visible row settles over 160–180 ms, at most 6 DIP travel | No stagger across an entire growing history |
| Notification | One compact surface arrives 180–220 ms; dismissal 160–180 ms | Reduced opacity only |
| Reorder | Pointer follows 1:1; neighbours settle in 180 ms; release response approximately 0.30 s | Reduced direct placement, insertion marker preserved |
| Busy to success | Spinner stops and transitions to one drawn check in 220–300 ms | Reduced static state swap; text announces result |

These are target envelopes, not delays before a command executes. Default proposal is Expressive for deliberate pointer interaction, Standard as an alternative, with system Reduced motion respected. Keyboard repetitions and high-frequency scoring use the minimal feedback needed even in Expressive mode.

For physics-capable prototypes, start with damping ratio 1.0 and response approximately 0.30–0.38 s for position/expansion. A small momentum-driven reorder settle may trial damping 0.85. Response is not a fixed duration. Do not stack a scripted bounce on a spring. If the chosen native mechanism cannot preserve velocity, retarget position continuously with a controlled ease and declare that limitation rather than claiming an exact Apple spring.

For non-gesture transitions, existing proposed curves remain `cubic-bezier(0.23, 1, 0.32, 1)` for outward settling and `cubic-bezier(0.77, 0, 0.175, 1)` for deliberate repositioning. Centralize roles in existing operator motion infrastructure; do not create a competing generic animation layer.

## Optical and VFX contract

| Effect | Intended visual | Boundary / fallback |
|---|---|---|
| Light-catching edge | A coherent curved silhouette makes a control tangible | One material edge; not an extra white rectangle inside the button |
| Interior press light | Highlight gathers inside the pressed control as depth compresses | Clip to the control; never wash out a label or colour-code the whole workspace |
| Local hover response | A faint highlight adjusts within the hovered surface | No global cursor glow, no tilt on a scoring target, no permanent shine loop |
| Selection lens | Selected tab/segment appears raised and settled | One indicator per group, destination accessible immediately |
| Materialization | Boundary expands from source; content becomes readable as usable space exists | Keep text at final size; do not stretch or blur functional text through the expansion |
| Dematerialization | Content leaves, same surface returns toward source | Underlying commands cannot receive the closing click |
| Focus depth | Scrim quiets background for a modal task | No repeated full-window blur capture; live clocks continue independently |
| Scroll edge | Narrow fade/separation only where content meets floating tools | No persistent shadow slicing team names or editable text |
| Symbol continuity | One symbol changes state in a fixed slot | Do not animate entire labels or change their measured width |
| Success | One check/short tint confirms a real result | No confetti or fireworks after a routine score, save or checkbox |
| Drag elevation | Picked-up row has subtle separation and clear insertion point | Mouse offset retained, no layout jump when drag begins |
| Error emphasis | Local accessible error state, optionally one restrained 2-DIP nudge in a rare form | Never shake the live scoring page; no strobe; Reduced removes movement |

Real optical refraction, live backdrop sampling and dynamic light simulation are feasibility work, not already working effects. WPF BlurEffect blurs its target, and WPF Popup has a separate window boundary. First prove a sculpted vector/static material with the required feel. If a real material experiment misses the frame budget, retain the shape, depth, selection and interaction using the fallback. No public-output shader/material work is included.

## Six signature sequences

### 1. Scoring press

On down: illuminate and compress only the inner button surface. Hit rectangle, adjacent commands and labels do not move. On normal activation: existing scoring action commits immediately. The operator figures update through the current scoring contract; no new public score/reel animation is introduced. Release returns from the current state. A new press interrupts release rather than queueing another decorative sequence. Disabled scoring shows a reason and the existing readiness action.

### 2. Ellipsis to action menu

Use one visual source: the ellipsis capsule either expands into its menu or remains the clear anchor of a separate popover. Pick one per component; do not show both styles simultaneously. The source geometry and destination are measured once. Grow a clipped material boundary while text is laid out at final size; reveal text when it fits. Collision handling flips the menu inward before showing it. Escape/outside click returns along the same relationship, restores focus and consumes the dismissing input. If the originating player disappears/reset occurs, close safely to a neutral local point and restore the nearest valid focus target.

### 3. Navigation lens

On selection, destination state and accessible selection change immediately. A single lens retargets from its displayed position to the selected slot. Content may use a short opacity transition; no whole-page slide on every Match/Scorecard/Output change. Initial placement, resize and DPI changes place the lens without a spurious travel animation. Fast Match→Output→Settings input ends at Settings with no stale callback returning to Output.

### 4. Choice and toss

Hover lights the tile, down compresses, selected fill and a clear check settle together. The check has a reserved slot and does not push the team name. Selecting the other team retargets one group state; there is no native checkbox inside a custom radio tile. Confirm retains the existing toss/player workflow. The sheet exits only when its command succeeds; invalid selection stays in context. Underlying scoring never receives the confirmation click.

### 5. Editor and validation

An editable scorecard value or history event opens a compact anchored editor or integrated sheet. The field is crisp at rest, and the selected value is obvious. Apply validates immediately. A mismatch opens a consequence step within the same host with Go back and the existing permitted continuation action; structural invalidity blocks commit. Do not close the editor merely to open an unrelated OS dialog. Cancel restores the promised preview and returns focus; it never silently commits a pending value.

### 6. Sheet to child step and back

Keep the sheet boundary and title hierarchy coherent. A child form replaces its body and adds a Back command; retain entered values and scroll anchor. Done/Apply is a primary capsule; Close/Cancel is clearly subordinate and distinct. The outer workspace stays spatially stable. If a new sheet request supersedes one that is entering, retarget one host instead of stacking scrims or replaying all entrances.

## Micro-interaction map across the complete operator surface

| Pattern | Relevant application use | Decision |
|---|---|---|
| Press/release illumination | All actionable buttons, toolbar tools, score controls | Core |
| Integrated selected check | Presets, club favourite, toss team, selectable rules | Core; state readable without animation |
| Sliding selection | Navigation, innings, Batting/Bowling, settings choices | Core; distinguish tabs from ordinary action buttons |
| Toggle thumb | Delivery rules, appearance, automation preferences, ticker slot enable | Core; current persisted state authoritative |
| Slider direct manipulation | Colour saturation/brightness, existing continuous media controls | Core; typed equivalent and real-time preview where promised |
| Text/value change | Status, count, settings summary | Fixed footprint, minimal transition; no whole-label wobble |
| Number reel | Optional operator-only value feedback | Prototype only; never animate every timer tick or touch public reels |
| Dropdown/menu origin | Player actions, long fielder lists, category overflow | Core; preserve keyboard/type-ahead where applicable |
| Circle-to-menu expansion | Options/add actions with short action sets | Signature candidate, collision tested |
| Disclosure | Advanced rules, settings groups, history over headers | Core; preserve outer scroll anchor |
| Page continuity | Wizard next/back, finder list/detail | Core; direction has meaning, fields retain state |
| Search clear | Club search and settings search | Immediate clear; tiny icon swap, no flying words |
| Tooltip | Icon-only tools and truncated labels | Delayed on hover, focus-accessible; never sole access to a command |
| Loading placeholder | Remote club search and update checks when there is real delay | Quiet bounded placeholder; no fake loading screen |
| Progress to check | Download/verification/import/save | Real states, not decorative completion after a failed operation |
| Reorder and insertion | Squad batting order, training queue | Core; alternative keyboard movement |
| Row arrival | Newly added delivery/match-log event, new timer | Bounded visible item only; respect reading position |
| Row removal | Queue item/preset deletion | Preserve neighbour/scroll anchor, Undo when supported |
| Favourite response | Club preset favourite | Small check/star fill and short settle, no particle burst |
| Asset arrival | Loaded team logo/media thumbnail | Preserve aspect/selection, quiet reveal, no 3D image bend |
| Toast/banner | Real completion, warning, update state | Shared deduplicated surface; important failures persist |
| Disabled guidance | Pre-toss/player readiness, paused break, innings limits | Full actionable guidance where applicable, not a dead grey panel |
| Return-to-source | Cancel, back, close on every transient surface | Core; safe focus and click-through protection |
| Error state | Bad input, unavailable media, invalid historical edit | Local clear reason and recovery, not a generic alarm |
| Notification stack | Multiple real events | Collapse/deduplicate, do not cover scoring controls |
| Theme change | Operator appearance preference | Controlled short colour change; no whole-screen flash |

The MicroKit catalog and Transitions.dev recipes are references for interaction patterns. Their React/CSS snippets are not installed or pasted into this native WPF app. The local Transitions catalog includes source-aware menus, sliding tabs, check drawing and switches; this proposal adapts those ideas instead of copying web timing indiscriminately. [Transitions.dev](https://transitions.dev/). The MicroKit website was unavailable through the research tool; its local catalog was available and inspected.

Recipes deliberately not adopted as default operator behaviour: 3D hover tilt, global cursor glow, confetti, smoky deletion, endless shimmer, thinking/reasoning streams, random matrix loaders, animated gradient headings and staggered letter reveals on routine controls. More animation coverage means more considered responses, not every effect playing together. A sporting operator panel is not a promotional landing page.

## Interruption and state ownership

Each transient surface uses Closed → Opening → Open → Closing with an instance/revision identity. Repeated Open/Close changes target state from the current displayed geometry; stale completion callbacks cannot hide a newer instance. New match/reset/unload cancels subscriptions and releases scrims, pointer capture and keyboard focus. Exceptions settle to a usable state and expose the real operation result.

Pointer-down is visual feedback, not an alternative duplicate scoring command. Preserve one activation path for mouse/keyboard. A drag away can cancel a press using normal control semantics. Hover over one button must not move its neighbour into the cursor. Focus indicators remain visible when navigating by keyboard and never masquerade as a second persistent selection box.

## Frame and reliability budget for a later prototype

- Target consistent presentation on 60-Hz hardware; 16.7 ms is the total frame interval, not a guaranteed animation budget. 120-Hz displays require tighter work. Measure frame distribution and input latency on representative Windows hardware.
- Prefer bounded render transforms/opacity and precomputed clipped material. Do not claim web compositor behaviour automatically applies to WPF.
- No per-frame layout of a whole score console, no live blur on every historical row, no repeated full-window captures or animated shadow radii.
- Stop effects when hidden/minimized/unloaded. Virtualize long histories without losing event identity, focused editors or scroll anchor.
- Verify double clicks, rapid alternating tabs, Escape during entry, open while closing, reset during a sheet, media/dialog coexistence, mixed DPI and keyboard use.
- Public output stays visually and behaviourally unchanged while operator prototypes/implementation are profiled. Resource scoping is an acceptance gate.

## What must be demonstrated rather than promised

Static boards establish style, not feel. The first later prototype should demonstrate: (1) one scoring press under repeated input; (2) a reversible source-connected menu; (3) rapid navigation-lens changes; (4) toss selection and sheet cancellation; (5) direct colour-slider manipulation; (6) a bounded log insertion while scrolling. Record normal speed and slow motion. Review Expressive/Standard/Reduced and opaque/high-contrast variants. Do not implement every screen until those sequences feel coherent.

Demonstration order after review: first show a short complete scoring → contextual action → navigation → return journey, in AFL and cricket variants. Use that to evaluate the system together. Then test sheets/validation, continuous controls and exceptional interruption states separately. Do not begin with a disconnected showcase of decorative effects.
