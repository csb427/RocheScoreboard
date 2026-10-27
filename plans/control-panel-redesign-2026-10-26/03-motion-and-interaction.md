# 03 — Motion and interaction specification

Status: PROPOSED. This document describes the desired experience; none of these timings have been applied to the app in this planning pass.

Revision: document 10 defines the new Control Centre motion/VFX layer and supersedes differing expressive defaults below. All motion in this package is operator-only. Existing public display animations, scheduling and timing remain untouched.

## The governing rules

1. Respond visually on pointer-down and commit at the normal input activation point. A deliberate score must not wait for a spring, sheen, toast or sheet entrance.
2. Fixed hit targets. Move or tint an inner surface, icon, selection marker or indicator. Do not grow a command over its neighbour.
3. Retarget from the displayed value. Interrupted motion must not restart from a prescribed hidden pose or jump to its logical target.
4. Preserve velocity for genuine direct manipulation when the eventual native animator supports it. Do not invent momentum for an ordinary button click.
5. Enter and exit through a consistent spatial relationship. Anchored menus belong to their trigger; sheets belong to their workspace.
6. UI state is authoritative independently of motion. Unload, reset, focus loss, cancellation and stale completion callbacks must settle correctly.
7. Frequent actions feel immediate. Occasional sheets and rare completion moments carry more motion. Existing public broadcast behaviour is excluded and unchanged.
8. At rest, the interface is quiet. No permanent shimmering controls, pointer-following background glows or bouncing scores in the operator console.

## Proposed motion tokens

Use the existing shared motion infrastructure as the later implementation seam. Review and deliberately revise its contract rather than introducing another overlapping motion stack.

| Role | Proposed value / shape |
|---|---|
| Press onset | Immediate state; inner compression to `.985` over 80 ms when movement is allowed |
| Press release | 140 ms controlled ease-out, no routine bounce |
| Hover in / out | 120 / 180 ms tint or restrained material response |
| Focus / state colour | 140–160 ms, with immediate visible focus boundary |
| Selected marker | 160 ms fill, up to 250 ms check draw; marker footprint fixed |
| Switch | 220 ms from current thumb position; no repeated overshoot |
| Tab indicator | 220 ms between fixed headers; initial placement and resize snap |
| Tab content | Optional 100 ms crossfade; never a full-window slide |
| Small menu/popover | 180 ms open, 140 ms close; `.97→1`, up to 4 DIP travel from anchor |
| Task sheet | 260 ms open, 180 ms close; up to 12 DIP travel, controlled opacity |
| Scrim | 140 ms, independent of model activity |
| Disclosure | 220–250 ms; small bounded measured region only |
| New visible history event | 180 ms local tint; optional 4 DIP entrance only when not reading older history |
| Toast | 180 ms in, 140 ms out; up to 8 DIP travel, bounded stack |
| Reorder displacement | 180 ms for affected neighbours; 220 ms settle after drop |
| Gentle reduced-motion feedback | Static state or 80–120 ms colour/opacity transition; no spatial travel |

Suggested fallback ease-out: cubic Bézier `(0.23,1,0.32,1)`. On-screen reposition fallback: `(0.77,0,0.175,1)`. Drawer fallback: `(0.32,0.72,0,1)`. These define design curves; a WPF adapter must reproduce them or document its approximation. Do not label a generic `CubicEase` as the exact same Bézier.

For a future velocity-aware native spring experiment, start with critical damping ratio 1.0 and response around 0.3–0.4 s; use only on selected navigation/direct manipulation where it improves continuity. These are proposed response parameters, not a fixed duration guarantee. A deterministic easing fallback is required. A web/SwiftUI spring API is not automatically available in WPF.

## Interaction catalogue

| Interaction | Visible sequence | Boundaries and interruption |
|---|---|---|
| Goal/Behind/run key | Press wash/compression, immediate activation, settle | No lift of the outer target; no score commit delay; each deliberate activation remains an event |
| Keyboard scoring | Immediate state response and optional tiny wash | No pointer-only affordance required; no focus hijack or artificial delay |
| Navigation tab | Selected capsule travels to destination, content swaps | New tab takes effect immediately; fast retargets start from current indicator position |
| Team/preset tile | Fill resolves, fixed check draws | Label, border thickness and allocated size stay fixed; radio/checkbox semantics preserved |
| Binary switch | Thumb tracks changed state and settles | State updates immediately; repeated flips reverse from current position |
| Select / dropdown | Surface opens from trigger, current selection obvious | Long options scroll; close remains input-owning until removed; return focus |
| Player ellipsis | Anchored contextual menu | Menu never covers primary scoring commands where a better placement exists |
| Text field focus | One coherent boundary/caret treatment | No second inset rectangle; never move the caret or trim text while typing |
| Number/time editor | Stable digits, step/typing feedback | Units explicit; Enter applies valid input, Escape cancels; no extra start/stop control |
| Search | Clear button appears in reserved space; local result state changes | Do not shift the input; asynchronous results cannot replace a newer query |
| Validation | Field and nearby error become visible; corrective action clear | No repeating shake or blinking; error persists until corrected |
| Live colour edit | Swatch/preview tracks wheel/slider directly | No delayed easing between pointer and chosen colour; Cancel restores original live preview |
| Eyedropper | Picker pauses/temporarily yields to a screen-sampling layer | Escape restores the same editor; selection returns without reopening duplicate windows |
| Slider | Thumb follows the pointer 1:1; small active halo | Capture loss settles safely; keys and fine adjustments remain native |
| Menu child step | Parent menu/sheet content transforms into the next level | Keep Back and Close distinct; do not stack unrelated tiny windows |
| Sheet open | Material and scrim establish depth together | First meaningful input is available as soon as ready; no dead waiting period |
| Sheet reverse/reopen | Retarget visible pose and preserve context | Old close completion cannot dismiss a newly opened sheet |
| Destructive decision | Stable warning symbol/title, safe and destructive actions | Safe default focus, explicit consequence; no dramatic movement towards the destructive target |
| Save/update progress | Stage label and honest progress | Cancellation is stage-aware; never show success before actual completion |
| Completion | Short check/state replacement, optional toast | No full-screen celebration for routine settings saves |
| Toast stack | A small fixed corner/edge anchor; affected neighbours settle | Never under a frequent scoring target; errors remain as banners rather than timed disappearance |
| Disclosure | Chevron and body reveal share one motion | Reverse current extent; do not collapse a newer open state via stale callback |
| Scrollbar | Reserved track, handle appears during scroll/focus/hover as needed | No content-width change; keyboard discoverability retained |
| History add | Local highlight of the new visible event | If reading older history, preserve position and show a New events affordance |
| History edit | Stable row highlights then opens editor | Hover reveal does not increase row height or move the row under the pointer |
| Drag roster/queue | Dedicated handle, captured grab offset, insertion marker | Pointer follows 1:1; auto-scroll bounded; Escape cancels; keyboard reorder alternative |
| Drop | Affected rows settle into place | Commit once; no repeated full-list rebuilding during pointer movement |
| Resize/reflow | Change width class once, preserve focus/scroll | No continuous elastic resizing of the whole application |
| Output source switch | Operator selected state changes immediately; public transition is separate | On-air status reflects actual source, not just the last pressed button |
| Output waiting/blocked | Clear queued/reason status in operator UI | No fabricated public state; manual cancellation and fallback available |
| Idle/background | No operator decorative clock | Hidden animations/timers are suspended where safe; match clocks remain authoritative |

## Surface state machine

`Closed → Opening → Open → Closing → Closed`, with explicit `Opening ↔ Closing` reversal. Logical selection may update while visuals settle. Each presentation has an identity/revision; completion is allowed to finalize only the matching current presentation.

For a modal sheet, the scrim consumes pointer input until the sheet is removed, preventing click-through. It does not freeze match clocks, saving or legitimate background operations. Closing restores the originating control if it still exists, otherwise a predictable workspace fallback.

If the match changes or resets while an editor is open, the editor must not commit to an unrelated new match. Show a clear state-change message and return safely. Do not silently apply an old selection, run value or team change after asynchronous loading.

## Physical material behaviour

Subtle illumination belongs inside the active material surface and has one cause. The default hover has a small tint/edge response rather than a moving spotlight following every mouse move. Selected navigation is a settled capsule. Larger menus feel more substantial through controlled opacity/shadow, not a huge zoom.

No animation may require a second white outline over a control's own template. A template either supplies the declared action parts or uses a deliberate fallback; automated component checks should catch missing ownership/parts.

## Performance contract for the later prototype

- Prefer render transforms and opacity on bounded surfaces. Avoid per-frame layout of the entire scoring page.
- A small disclosure may need measured height changes; a large settings body should settle layout once and animate a clipped inner visual.
- Do not synchronously capture the whole UI or four large bitmaps when opening a menu/sheet.
- Static glass approximation comes first. True backdrop sampling/refraction is a separate measured experiment, including WPF Popup/window boundaries and video surfaces.
- Virtualize large histories; animate only affected visible rows. Preserve grouping, edit identity, focus and scroll anchor.
- Stop invisible decorative clocks and detach temporary rendering handlers/animations.
- Measure with both operator and output windows active, scoring bursts, video where supported, large histories and lower rendering capability.
- At 60 Hz the theoretical frame interval is 16.7 ms; at 120 Hz it is 8.3 ms. A later implementation must measure missed frames and input latency on representative hardware. This planning pass does not establish those measurements.

## Motion accessibility

Reduced motion keeps a clear check, selected fill, focus indicator and explicit status while removing travel/elasticity. Reduced transparency uses solid materials. Increased contrast strengthens meaningful boundaries. These are independent preferences. Do not infer a user's desired intensity solely from ADHD or any diagnosis; provide the expressive default requested here with user-controlled alternatives.

Operator notifications do not strobe. Public celebration/alert effects are outside this redesign and remain unchanged. No audio cue is required to understand any action; optional operator sounds would be a separately reviewed preference.

## Motion review method

For every interaction: inspect default, press, midpoint, settled, reversed and cancelled frames. Then test rapid repetition, keyboard activation, resizing, closing/reopening, focus loss, reduced motion and a state change during the transition. Review at normal speed and slowed playback. Passing static geometry assertions does not prove that motion feels fluid; both are acceptance gates.
