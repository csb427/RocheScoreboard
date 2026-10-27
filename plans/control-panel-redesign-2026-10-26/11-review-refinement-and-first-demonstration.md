# 11 — Review refinement and first interaction demonstration

26 October 2026. Design-only update following the user's review of the Control Centre boards.

Subsequent progression: the user requested continued research and movement through the stages. Documents 12/13 now record the refined control/information design and the isolated interactive study of this journey. The original brief below remains useful; its pre-prototype status is historical.

## Decisions retained

- The Apple/Control Centre direction is supported.
- Essential scoring geography is familiar enough in both sports.
- Floating navigation is supported and should remain a shared feature.
- Material intensity needs selective reduction, and panel/button proportions need refinement.
- Navigation must remain easy for a volunteer, including at smaller window sizes.
- Public output windows and all presentation visuals/behaviour remain excluded.

The user invited the designer to choose the best first demonstration. The recommendation is a short complete operator journey rather than isolated button effects. This document is that demonstration's specification; it is not an executable prototype or permission to modify production code.

## Material refinement

**Glass for floating tools; satin for actions; matte for information.**

Keep recognisable Apple geometry and fluid response while lowering reflected light on frequently used commands. A Goal or run button remains tactile through a tiny bounded press, local fill response and controlled release. It does not need a transparent body or brilliant edge. The score, player figures and log remain still and opaque. A navigation capsule, contextual menu or task sheet carries the strongest glass character because it is a separate functional layer.

This follows the general separation between material-bearing navigation and content in [Apple's Liquid Glass guidance](https://developer.apple.com/videos/play/wwdc2025/219/); the exact Roche material allocation is our design choice.

The revised [AFL material study](assets/06-refined-material-balance.png) illustrates the balance. It is still a generated concept, not pixel-perfect layout or contrast proof. Apply the same material allocation to cricket, setup, settings and Training even where an older board appears glossier.

## Proportion changes to evaluate

These are starting points for an isolated prototype, not final application measurements.

| Area | Refined target | Why |
|---|---|---|
| Window/title/status region | Compact chrome; combine context where it removes duplicate bars | Preserve useful work area without hiding match state |
| AFL wide composition | Trial approximately 68–72% scoring / 28–32% bounded history, then tune to long labels and real data | Avoid a dominant empty history while preserving easy correction access |
| AFL team columns | Equal widths, aligned names/figures, matching command heights | Scoring targets should feel predictable and balanced |
| Cricket composition | Preserve exact established keypad ordering; give readiness/context a stable reserved area | Avoid shifting targets when a bowler/player selection is needed |
| Logos | Smaller flat identity marks in the operator workspace | Team identity supports the task rather than competing with figures |
| Scoring actions | Goal/run commands primary; Behind/extras subordinate through material and grouping, still large and readable | Less decorative bulk without tiny buttons |
| Transport | One obvious current action such as Pause; Undo and end-period actions distinctly grouped | Prevent incidental end/reset clicks |
| Floating navigation | Trial 56–64 DIP reserved footer at comfortable desktop sizes | Keep the floating appearance without an oversized dock or content overlap |
| Field and row density | Compact grouped fields; preserve readable text and target sizes | Reduce empty framing rather than shrinking labels |

The percentages apply only to a wide operator layout. At compact sizes use reflow/disclosure/scrolling instead of forcing those ratios. Test the existing reference window classes in 09 with text scaling and keyboard focus. Sheet action footers and focused input fields must remain reachable.

## Navigation refinement

- Keep one consistently positioned dock across AFL and cricket. Match is the first destination; use the established sport-specific destination labels.
- Show both icon and text. Do not replace volunteer-facing destinations with unexplained symbols.
- Selected state remains unmistakable through the settled lens, contrast and accessible selection, without a second white outline.
- Keep dock targets fixed while the lens moves; initial placement and resize do not animate across the whole dock.
- Preserve each destination's scroll/edit context. Returning to Match should not recreate the layout or replay its entrance animation.
- Keep current match state and on-air state separate. A tab change is local navigation, not an automatic public presentation switch.
- Put uncommon tools behind a clearly labelled More/Match options control. Keep Undo, clock/readiness and scoring visible.
- All important commands work by click and keyboard. No essential feature depends on long-press, hover or a hidden swipe.
- Training remains a session workspace; do not invent cricket-specific tabs merely to copy its appearance.

## Recommended first demonstration: score → inspect → navigate → return

Show the same shared interaction system through an AFL variant and a cricket variant. Use isolated mock data. Do not connect the demonstration to a real match, saved settings, public display or updater.

| Step | AFL variant | Cricket variant | What the demonstration must establish |
|---|---|---|---|
| 1. Score | Press Goal, then Behind | Press 1, then 4 | Immediate satin-button response; no neighbouring movement or delayed command |
| 2. Observe | New match-log event appears | New delivery appears | Only affected information changes; bounded log does not enlarge the window or steal scroll |
| 3. Inspect | Open a log-event/Match options action | Open batter ellipsis | Menu grows from a clear source, readable at rest, within the work area |
| 4. Cancel | Escape/outside click | Escape/outside click | Smooth return, focus restored, no click-through to scoring |
| 5. Navigate | Match → Output → Settings | Match → Scorecard → Settings | One travelling lens, immediate selection, no repeated full-page motion |
| 6. Return | Return to Match | Return to Match | Scores, target positions, focus and reading position are preserved |
| 7. Interrupt | Open a menu while its previous close is unfinished | Rapidly alternate navigation/menu input | Motion retargets rather than restarting, freezing or leaving a stale scrim |

Public presentation commands in such a demonstration are nonfunctional mock controls. Real output behaviour is neither demonstrated nor redesigned by it.

## Follow-up demonstrations, in order

1. **Integrated sheet and consequence hierarchy:** toss or team choice → child editor → validation → back/cancel. This reveals selection clarity, safe actions and focus handling.
2. **Direct manipulation:** colour circle/slider and training/squad reorder, with typing and keyboard alternatives. This reveals pointer tracking and velocity continuity.
3. **Compact navigation:** repeat the primary journey with narrower dimensions and enlarged text. This reveals clipping, hidden actions and excessive dock proportions.
4. **Recovery and adverse states:** invalid input, failed operation, busy cancellation, reset/unload during entry. This reveals whether the design remains dependable.

Judge Expressive, Standard, Reduced motion, opaque materials and high-contrast states. Motion satisfaction should survive the matte/satin treatments; strong glass is not a prerequisite for good feedback.

## Numbered review points for this update

1. Does the quieter AFL material study keep the Apple character while reducing unnecessary gloss?
2. Is the division of matte information, satin actions and glass floating tools appropriate?
3. Keep the floating dock; review its exact height and spacing in a later interactive prototype.
4. Use the complete score/menu/navigation journey as the first demonstration, then evaluate sheets and sliders.

No additional user answers are required to complete this planning refinement. Exact proportions remain prototype decisions. No application code, public output or system settings are changed by this document.
