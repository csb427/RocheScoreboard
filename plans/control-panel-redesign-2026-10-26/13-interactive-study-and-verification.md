# 13 — Interactive operator study and verification

26 October 2026. This is an isolated browser design reference with mock data, presented in the conversation. It is not the desktop application, a published release or a complete cricket/AFL rules engine. No application source or public output rendering is connected to it.

## What can be explored

- AFL Goal/Behind press, immediate figure update, bounded history, Pause/Resume and Undo.
- Cricket run controls, a simple extras example, current-player/context menus and figure updates.
- Floating navigation, selection-lens travel, returning to Match with current study values.
- Timer-adjustment sheet, local validation, Escape/cancellation and focus return.
- Scorecard value edit and an integrated arithmetic-mismatch decision example.
- Operator Output controls as local mock selections; no presentation or overlay is rendered.
- Comfortable/Compact, reduced motion, stronger contrast, and host review controls for width, curvature and history proportion.

Sport switching changes study contexts. This is not a replacement for real setup, saved-match recovery, delivery replay, dismissal legality, full scorecard reconciliation or media/output routing. The media surface selects a local filename only and performs no public playback. The quarter clock is a stable illustrative fixture, not a live match clock.

## Browser verification

Final first-study run: **21 interaction/lifecycle assertions passed**, including one aggregate width-matrix assertion covering **48 cases**: two sports × four workspaces × six review widths (1100, 960, 760, 640, 420 and 320 pixels).

The assertions covered:

1. Immediate AFL Goal commit in the study.
2. Undo restoration.
3. Fixed scoring target bounds under repeated entries.
4. Unchanged outer height as history grows.
5. Visible disabled scoring while paused.
6. Context-menu opening.
7. Escape dismissal and focus restoration.
8. Invalid timer value remaining in context with feedback.
9. Valid timer adjustment.
10. Navigation lens travelling from its old position and settling at the selected destination.
11. Reduced-motion preference.
12. Timer edit surviving local navigation.
13. Cricket total and current-player figure updates.
14. A recorded run value appearing in the over strip.
15. A no-ball example preserving legal-ball count while adding a circle.
16. Undo restoring that strip/count.
17. Contextual strike change.
18. Integrated scorecard mismatch decision.
19. Rapid repeated navigation settling at Match without a stranded overlay.
20. No horizontal escape by the app or visible controls across the 48 width cases.
21. No uncaught interaction/resize errors during that run.

Wide AFL and compact cricket appearances were also visually inspected. The preview wrapper was made same-origin for automated DOM inspection only; the conversation reference retains the normal sandbox. A background browser can suspend animation timelines, so lifecycle/motion checks were rerun with the study page active.

Found and corrected within the study: log-driven height growth, premature selection-lens reset, a centred dock shifting when a scrollbar appeared, and resize-notification feedback. Earlier failed assertions were rerun after corrections; an initially incorrect expected striker in the test was also corrected. No production-app fixes are implied.

## Limits of this evidence

Geometry containment does not prove every string is readable or every possible name/number fits. The study does not establish native WPF frame pacing, memory/performance on a match-day laptop, full assistive-technology compatibility, short-height window behaviour, mixed-DPI scaling, persistent save/recovery or all sporting rules. Those need the later native prototype and regression phases. No full-app build or app launch was performed.

## Numbered review sequence

1. Press AFL Goal/Behind several times; judge button response and whether surrounding information remains calm.
2. Open Match options or a cricket player menu; dismiss with Escape and switch tabs repeatedly.
3. Switch to cricket, score a delivery and inspect the player rows, keypad and over-strip hierarchy.
4. Narrow the review width; judge labelled navigation, stacked history, spacing and action access.
5. Compare reduced motion/compact preferences; report anything that looks generic, cramped, unstable or insufficiently Apple-like.

The next design checkpoint is integrated sheets and compact-height behaviour, not deployment. Public output windows and all their presentation/animation behaviour remain outside this redesign.
