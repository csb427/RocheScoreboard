# 04 — Screen and behaviour specifications

Status: PROPOSED. All existing sport rules and operational edge cases remain requirements. This document plans a new interface around them.

Operator-only boundary: public output windows, rendering, typography, presentation layouts, overlays, animations, timing, scheduling, standby and media playback remain unchanged. The operator controls for those existing functions are in scope. Apply the revised sculpted Control Centre components and motion from 09/10 across every operator family below.

## 1. Sports home and recovery

The sport choice is the primary content. Use three clearly named, equally weighted choices with restrained sport accents and consistent icons. At narrower sizes use horizontal rows rather than cropping tall artwork. Settings and What's New form a secondary application toolbar with reserved space; they must not overlap or become inaccessible.

On discovering a recoverable match, show one integrated recovery sheet with team identities, sport and phase. Continue is primary; Continue later is secondary; Discard is separated and consequential. Do not show a duplicate Continue button behind the prompt. Only after Continue later is chosen should the deferred recovery row become visible on sports home. A corrupt or incompatible save needs a specific explanation and safe recovery/data action, not an endless Continue loop.

No decorative loading screen is introduced. If necessary work takes time, show local progress with a meaningful label and cancellation where safe.

What's New uses the shared sheet system, a version navigator/search and concise New/Fixed sections. Opening it does not block the application indefinitely; close/reopen and focus restoration are specified by the common surface state machine.

## 2. Setup shell

Use a consistent page title, sport context, step navigator, naturally sized body and persistent Back/Next/Start actions. Completed steps have a fixed check. The current step has one selected treatment. Clicking a completed step must preserve current values and respect validation rules.

Do not universal-scale the page down to fit. Equal team cards reflow together. At narrow widths the body scrolls; navigation is reachable and does not cover its final fields. Errors identify the exact field and stay visible until corrected.

### Teams and clubs

Promote Find a club before manual entry. The library must visibly name its destination: Home or Away. A team-card shortcut preselects that destination; a global Find a club entry has an explicit destination selector. Selecting Use for Home/Away updates only that team and returns to the same step.

Library layout: search and sport-relevant filters, results list, selected-team preview, favourite and Use Team. AFL and cricket catalogs remain separate. Loading, no result, offline, failed crest and manual-creation alternatives are designed states. Favourite adds a preset without requiring a second search workflow.

Manual team cards include name, abbreviation, primary/secondary colours and logo. Advanced presentation assets can be disclosed. Show true alpha over a checkerboard in the asset editor; do not represent a solid white rectangle as a transparent crest. Use the existing catalog/logo processing where possible; changes to extraction/removal algorithms require their own engineering validation.

Preset selection uses the successful fixed animated check from the current direction. Saving/updating and deleting are different operations. Duplicate names and an asset still in use receive clear feedback. Selecting another preset does not shift neighbouring tiles.

### AFL setup

Teams → Clock → Options and review. Count-up/countdown are visible choices, with one direct duration editor and a clearly related stoppage option. Finals, weather and optional prepared messages are grouped as optional choices. Review shows the values that matter, not every advanced default.

### Cricket setup

Teams → Players → Format → Review/venue. The player editor has first/last-name fields, a drag handle, C and WK roles and a separate remove action. A single captain per team and multiple deliberate wicketkeepers remain supported. Empty squads clearly preview the automatic eleven placeholders; invalid partial squads receive local validation.

Format choices expose limited overs and multi-innings with direct numeric entry and 20/40/45/50/100 presets. Replace wides, replace no-balls and free hits remain independent labelled options. They must not be hidden inside an unrelated appearance setting. Toss happens after entering the match workspace, not as a duplicate wizard step.

## 3. AFL live Match

Preserve the familiar layout: timer/clock context above paired Home and Away scoring cards, frequent transport below, bounded match history on the right.

- Team identity and current score stay clear. Goal is visually primary within each team; Behind is secondary but equally easy to target.
- Preserve A/S and K/L where configured, with quiet shortcut hints. Editing a field or invoking a focused control must not also fire a match shortcut.
- Running/paused/break and on-air source are separate states. Use a compact persistent strip without moving clock digits or dynamic label widths.
- Stoppage adjustment opens one anchored editor. Show Current and New time and one Apply action. Do not add another start/stop button inside it.
- Start/Pause, End quarter and Undo remain easy to find. Reset belongs in Match options with a consequence sheet, away from repeated scoring commands.
- Essential mode shows necessary match operation. More controls opens secondary functions without rearranging Goal/Behind targets under the pointer.

### History

The log owns its scroll area and fills the allocated right column. Rows retain a stable footprint. Hover/focus reveals an edit affordance inside reserved space. New events do not pull a user away from older history; show New events to return to live.

Add/Edit Score uses the shared editor: team and score type required, quarter/time optional with clear current-value defaults. Validate historical ordering and dependent totals. Delete is separated from Save. An unsuccessful correction keeps entered values and explains the issue.

### Quarter lifecycle

Ending a quarter, immediately resuming it, starting the next quarter and resetting during a transition are explicit state tests. Model clocks and scores update independently of existing output folding. A decision sheet cannot apply to a new match after reset. Existing public quarter transitions are excluded from this redesign and remain unchanged.

## 4. Cricket live Match

Preserve current geography and the exact keypad order:

`Dot | 1 | 2 | 3`

`4 | 5 | 6 | Wicket`

`Wide | No-ball | Bye | Leg-bye`

Batters and bowler remain above the keypad. The delivery log remains to the right at desktop widths. Match options, Undo and End innings remain below frequent scoring; Reset moves into the consequential-action path.

Batting/bowling bands use subtle team identity, neutral content and clear figures. Avoid repeated score text under the player and again on the right. Runs and balls/figures align in stable columns. Strike is identified by an icon and state, not colour alone. Ellipsis actions open from their anchor.

### Readiness and pause

The scoring region has a fixed-size action gate for toss, opening players, replacement batter, next bowler, break and innings/game completion. The gate names the missing action and opens it directly. It does not make the unavailable keypad appear active. A break gate reads, for example, Rain delay · Resume play, and pressing it resumes through the permitted domain action.

Recording the toss retains the existing opening-player selection workflow. Skip for now returns to an obvious readiness gate, not a dead end. Existing pre-toss public rendering remains unchanged.

### Toss and selectors

The toss sheet has two clearly selected team tiles and two Bat/Bowl choices, a readable sentence and one Confirm action. Repeated selection retargets its marker without moving the tile.

Player selection separates available, already active, dismissed, previous bowler and otherwise unavailable players with text reasons. Opening striker → non-striker → bowler is a connected flow; optional skip returns to the same context. A previously used bowler can be promoted in the list without violating bowler eligibility rules.

### Extras

Wide/no-ball entries use visible choices for small option sets. Additional-run presets are obvious; Custom reveals a text field only when requested. No-ball allocation distinguishes bat, byes and leg-byes. The calculation preview at the bottom explains the addition to team/batter/extras totals. Wides/no-balls do not become mutually combined contradictory delivery types. Bye/leg-bye zero is not presented as a useful scoring choice.

### Wicket

A neutral sheet avoids implying that one team's colour is the action's owner. Show dismissal tiles, permitted batter(s), fielder when relevant, and conditional runs/extras. Non-striker selection is visibly unavailable for striker-only dismissals. Run-out and free-hit restrictions remain enforced by the domain layer; the design must not simplify away legitimate cases.

Do not reintroduce the confusing unfinished-run crossing control. Make completed-run information clear and retain a deliberate manual strike correction path. Final legal-ball/strike behaviour needs domain tests, not assumptions inferred from the mockup.

### Replacement and correction

Replace batter asks for retired hurt/not out/out or mistaken selection. A wrong-player correction offers explicit transfer/retain figures. Replacements are not just label changes. The editor must reject stale match/innings/player revisions.

Delivery correction presents relevant fields with selection tiles where option sets are short and dropdowns for long player/fielder sets. If a strike or bowler change affects later deliveries, show an explicit scope decision before applying. Apply once; do not silently rewrite later balls based on a hidden switch.

The delivery log groups overs with bowler, over runs/wickets and cumulative score in the over header. Latest deliveries appear consistently first. Event rows emphasize the delivery number and result; omit repeated irrelevant batter captions. Invalid extra deliveries can repeat the legal-ball number without a meaningless plus suffix.

## 5. Cricket scorecard

Use a strong innings navigator above a separate Batting/Bowling segmented control. They must not look like interchangeable navigation levels. The main table occupies the workspace; team identity is concentrated in its header.

Batting: name/dismissal, R, B, 4s, 6s; extras below batters; team total and fall of wickets clearly grouped. Bowling: O, M, R, W, WD, NB and calculated economy where space permits. Narrow layouts use named row details rather than tiny horizontal tables.

Editable values look like reading content at rest. Hover or keyboard focus exposes editing; click/Enter opens the corresponding row/value editor. A brief instruction explains this at the top. No permanent text-field outline around every cell.

Each editor has Apply/Cancel and validates the proposed transaction. Structural errors block. An arithmetic mismatch shows the relevant totals and offers Go back or an explicit Continue anyway where domain policy permits. An explicit Recalculate total action can be proposed, but no balancing is silent. If coordinated multi-row adjustment is needed, open a labelled Adjust scorecard task instead of repeatedly warning after every keystroke. Avoid an unexplained global Save button when individual Apply operations are the actual commit boundary.

Dismissal editing reuses real wicket choices and conditional bowler/fielder requirements. Fall of wickets is an editable list in the scorecard, not a disconnected window. Adjustment history/revert limitations remain discoverable under Adjustments, with clear indication when later deliveries prevent a simple revert.

## 6. Match options, completion and revised conditions

One Match options sheet groups:

- Play status: lunch, tea, rain, drinks, stumps, other break.
- Teams and players: one Edit teams action, team-themed Home/Away tabs, order and C/WK roles.
- Conditions: format/over limit and supplied revised target/overs.
- Innings/game: eligible next innings, continue, end game, reset.

Child tasks have Back and Close in distinct positions. Dismissal/cancellation returns to the original context. Do not display Resume play as a redundant enabled action when already playing.

Completion hierarchy follows context. After a first innings, starting the next eligible innings is primary. In a normal limited-overs chase that reaches target, End game is primary and unusual continuation is secondary. Multi-innings and follow-on/asymmetric innings histories need explicit eligible team choices. An early limited-overs close shows the remaining allowance; unlimited play does not receive that misleading warning.

Revised conditions state that the target/overs are supplied by umpires or the competition. In a first innings, show relevant revised overs; during a chase also show target. Display original and new values, validation and restore. Do not label this as an internally calculated DLS result.

## 7. Output studio

This is the OPERATOR workspace only. Restyle buttons, navigation, settings editors and status using existing APIs and eligibility/queue/cancellation semantics. Do not introduce a new public overlay lifecycle or change presentation rendering, transitions, scheduling or timing.

Output starts with actual on-air source and window availability. Presentation cards use the same component family across sports but only show supported destinations. A selected card reflects the real routed presentation, including automatic rotation and restoration.

Automation is an independent toggle/configuration, not a mutually exclusive Automatic/Manual navigation mode. Manual controls remain available while automation is enabled. Manual triggers can expose their eligibility/resting state and the reason a fact is unavailable. One current-overlay tray shows visible/queued/closing/celebration-takeover state and one Cancel overlay action.

Group overlays by purpose: match context, pressure/runs, team, weather and alerts. Keep rare timing controls in a secondary sheet. Do not put all algorithm tuning in front of a volunteer.

AFL messages live in one clearly named section: eight slots with enable, text, colour, highlight and local clear. Typing preserves spaces/caret. Disable all is reversible; Clear all is consequential and offers appropriate confirmation/Undo. Do not imply a second ticker editor in Media. Cricket ticker is not added merely for visual symmetry.

Surface existing shared dimensions, reset, standby media and clock preferences through redesigned operator editors, retaining their current values, effects and persistence scope. The default/reset remains 386×193. This redesign does not alter 2× rendering, internal output geometry, screensaver rendering or fallback implementation.

## 8. Media

Make existing Local preview and On scoreboard controls distinct. Restyle transport, seek, speed, unload, failure indicators and Return to live. Preserve current public playback, takeover, emergency fallback and return behaviour; do not add or redesign public media rendering in this task.

## 9. Settings

Application categories: Appearance; Interaction; Output defaults; Automation defaults; Startup and recovery; Connections; Application and updates. Sport-specific defaults are labelled within the appropriate category.

Live categories: Match; Teams and players; Controls; Display; Automation; Connections. The header says This match. A Save as default action is explicit. A search result shows its category and scope, and navigation preserves scroll/focus.

Use visible choices for small sets, custom dropdowns for larger sets, switches for binary options and direct fields for numbers. Appearance choices must actually change a complete operator theme rather than recolouring a few controls; explicitly scoped resources must not affect public windows. Defaults and limitations must be explained where they matter, not hidden in tooltips.

## 10. Training

A large active timer and Start/Pause are primary. Reset active, Skip and +time stay nearby but distinct. The queue has bounded scrolling, active/upcoming/completed states and a direct Add timer action. Builder/interval presets open in a consistent secondary panel instead of dominating the whole session.

Drag uses a handle and insertion preview; Alt+Up/Down or equivalent accessible move commands remain. Inline editing preserves focus and temporarily suspends drag for that row. Clear queue and Exit session need consequence-aware protection or Undo. Persisted session recovery is a useful optional enhancement, not an existing capability to assume or silently add during a visual reskin.

## 11. Alerts, notifications and updates

| Situation | Surface and actions |
|---|---|
| Invalid field | Inline error at field; keep input and focus |
| Recoverable club/weather/image failure | Local state with Retry / choose another source |
| Output/video/remote subsystem degraded | Persistent named banner with useful recovery action; match operation continues when safe |
| Completed preset/save/copy | Short local check or toast; never claim success before completion |
| Arithmetic score mismatch | Decision sheet with actual difference and safe return |
| Reset/discard/disconnect affecting others | Specific consequence sheet, safe default, isolated consequential action |
| Unsaved editor close | Discard changes / Keep editing, only if a draft actually exists |
| Update available | Shared sheet with version/notes, Download and restart, Later, release details |
| Download/verification/staging | Honest named stages, real progress where known, stage-safe Cancel |
| Update failure | Persistent explanation, Retry/details/release alternative; no loss of match or settings |
| Recovery write failure | Visible unsaved state and recovery action; never a false Saved indicator |

Notifications do not expand the live scoring layout or appear under the mouse where the next score command belongs. A proposed notification history can hold noncritical completion messages; important failures remain visible until addressed. Do not turn every routine click into a toast.

## 12. Asset and utility editors

Colour, logo crop, club details, cloud joining and diagnostic details follow the same sheet/header/footer language. Screen eyedropper and native file dialogs are intentional platform-level exceptions with correct owner/focus return. Updater/recovery surfaces that appear before a live workspace need the same app shell/host contract, rather than an unparented floating window.

Developer previews remain debug-only, grouped into Match situations, Presentations, Effects, Timing and Diagnostics. Preview state is isolated and restoration explicit. It must be impossible to mistake a simulated game for the active live match.
