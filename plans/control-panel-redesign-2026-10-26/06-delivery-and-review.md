# 06 — Later delivery plan and acceptance

Status: DESIGN REVIEW. No implementation, build, launch, dependency installation, commit or release is authorized by this document alone. The user explicitly requested planning first.

Latest scope: bolder iOS Control Centre-inspired OPERATOR redesign. Public output visuals and behaviour are excluded, not an implementation phase. The first understated concepts are superseded by documents 09/10 and the revised boards in 08.

## Proposed delivery sequence

| Phase | Concrete reviewable result | Gate before proceeding |
|---|---|---|
| 0. Direction review | This package, selected concept boards and resolved design questions | User chooses the visual/behaviour direction |
| 1. Isolated component prototype | Button/choice/tab/menu/sheet/scroll/colour/reorder demonstrations with expressiveness and accessibility modes | Feel review on normal Windows hardware; no live match integration |
| 2. Operator material/performance spike | Native WPF sculpted material plus optional bounded operator-backdrop experiment; DPI/popup/fallback evidence | Glass adds value without making operation unreliable; public output runs unchanged for regression checks |
| 3. Shared component contract | Verified templates, one feedback owner, states, focus and automation; shared tokens and surface lifecycle | Automated component states plus keyboard and interruption review |
| 4. Entry/settings/setup | Sports home, recovery, global settings, wizards, club/asset editors | Complete first-use/recovery journeys, responsive and form validation checks |
| 5. Live operator reskin | AFL and cricket scoring geography retained; logs, readiness, decision sheets, settings scope | Domain regression, fixed-target and no-misclick tests |
| 6. Supporting workspaces | Scorecard, output studio, media, training, connections/updater | Full transaction/cancellation/failure workflows |
| 7. Release hardening | Long sessions, lower rendering capability, save/update/restore and packaging checks | User approval of concrete final release candidate |

A native WPF implementation is the default direction. Do not migrate the app to a web wrapper, WinUI, SwiftUI or a new MVVM architecture merely to reproduce the concepts. Shared templates/tokens are likely the first seam, but engineering changes must be scoped and tested after design approval.

## Existing-code constraints to carry forward

- Production AFL UI is in MainWindow, not just the older standalone ControlPanel control.
- Match managers, score history, clock, persistence and updater semantics remain authoritative.
- Shared output dimensions remain global; a mode change must not revive an unrelated display.
- Domain updates are not tied to animation completion. UI feedback must not introduce scoring cooldown changes.
- Existing root source and uncommitted changes are preserved. Later implementation should isolate its own diff and identify the actual current paths rather than blindly replacing legacy templates.
- The current design contract is not overwritten in this planning phase. A later approved implementation can migrate it deliberately.
- Operator resource/API firewall: scope new styles, fonts, materials, implicit templates and motion to operator roots. No inheritance into public display windows or standby/video/calibration/fallback surfaces. Existing presentation commands/settings remain the boundary. Do not change output scheduling or score/clock semantics to support operator effects.

## Accessibility and usability gates

1. Every command can be reached and understood with keyboard and pointer. Focus order matches the visual task order.
2. Every icon-only action has an accessible name. Selection, team identity, readiness and failure do not rely only on colour.
3. Operator text meets the planned contrast threshold in actual composites; critical figures are stronger. Glass fallback and bright logos are included. Public displays are unchanged regression baselines, not new contrast/redesign work.
4. Text scaling, 100/125/150/175/200% DPI and supported window classes preserve readable controls, caret, scroll anchor and action access.
5. Reduced motion removes nonessential travel/elasticity without hiding feedback. Reduced transparency and increased contrast remain independent.
6. A volunteer can choose a sport, configure teams/rules, start/resume play, score, undo, pause/end a period and recover from one mistaken action without finding advanced controls.
7. Destructive actions are distinct and consequence-aware; repeated routine operations do not require unnecessary confirmations.

## Motion and stability gates

| Scenario | Required result |
|---|---|
| Rapid open/close/reopen | Latest surface remains correct; no stale close; no leftover scrim/input lock |
| Fast tab changes | Selection and content agree; indicator retargets; no content overlap |
| Scoring during UI motion | Model updates once per deliberate command; commands do not move under the pointer |
| Keyboard while editing | No accidental match shortcuts; Enter/Escape follow editor semantics |
| Pointer press then resize | No shifted activation target or click-through |
| Focus loss/capture loss | Press state clears; drag cancels/settles safely |
| Reset while editor/async lookup open | Stale completion cannot mutate the new session |
| Large histories | Bounded virtualized scrolling, stable anchors, no whole-list replay |
| Continuous timer ticks | Stable digits and adjacent labels; no refitting per tick |
| Colour preview then cancel | Original colour restored; no delayed secondary-colour overwrite |
| Material fallback | Same geometry/input with opaque surfaces; no missing buttons or blank popup |
| Output active with media | Measured frame/input behaviour; no repeated huge bitmap capture on entry |

A later test run should record application version/commit, hardware/rendering capability, DPI, viewport, motion mode and actual performance traces. A smooth screenshot is not a frame-rate measurement. Budget targets need profiling on representative machines, including a lower-capability configuration; performance is not certified here.

## Domain/failure journeys to retain

### AFL

Score bursts for both teams, behinds inside goal-only runs, undo/correction during overlays, long playing-time droughts excluding breaks, quick quarter end/start, warning takeover with ticker restoration, manual overlay while automation is active, white/black teams, missing logos, repeated presentation visits, output resize and hidden/reopen.

### Cricket

No players entered, toss deferred, selectors skipped, legal/extra delivery replacement rules, free hit, run-out with extras, tenth wicket, next bowler only after a complete legal over, retired/mistaken player replacement, historical delivery correction scope, manual scorecard mismatch, target reached with continuation options, early innings close, revised overs/target, multi-innings asymmetric/follow-on histories, undo during celebration, and restored game after closing.

### Training / system

Queue edit/reorder during running time, completed rows, empty queue, work/rest generation, clear/exit protection, disconnected remote, failed weather/logo/media, updater cancel at each permitted stage, download/verification failure, restart/rollback, unavailable recovery and persistence failure.

## Proposed UX enhancements, separated from the visual reskin

| Proposal | Value | Scope decision |
|---|---|---|
| Unified local notification history | Recover a dismissed noncritical message without alert spam | Optional; do not hide critical banners inside it |
| Training-session recovery | Restore actual queue/session, beyond existing timer recents | New feature needing persistence design/tests |
| Contextual action discovery | A simple Help/shortcut reference for volunteers | Keep optional and nonblocking |
| Explicit on-air versus local-preview state | Prevent accidental media/public takeover | Core interaction clarity |
| Unified adjustments area | Explain corrections/reverts and arithmetic mismatches | Core scorecard clarity; preserve current transaction limits |
| Saved workspace preferences | Restore selected category, density and appropriate scroll state | Useful; avoid restoring stale transactions |

## Numbered design review

1. Do the black surfaces and bolder Control Centre-inspired sculpted controls match the desired character?
2. Do AFL Goal/Behind and cricket run/extras targets remain familiar enough?
3. Is the proposed shared navigation clear without making sports artificially identical?
4. Are global defaults and this-match changes distinct enough?
5. Is the integrated sheet hierarchy preferable for toss, wickets, colours and consequential decisions?
6. Are operator Output controls and on-air status clear while existing public screens remain unchanged?
7. Does the proposed motion intensity sound satisfying, with calm-at-rest live operation?
8. Which two or three interaction sequences should be prototyped first for feel review?

These are design review questions. There is no new runnable app build to test from this pass.
