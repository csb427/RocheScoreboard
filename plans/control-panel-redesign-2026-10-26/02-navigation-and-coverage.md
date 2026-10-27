# 02 — Navigation and screen coverage

Status: PROPOSED. This is the coverage register for design review, not an implementation checklist marked complete.

Scope: operator interfaces only. Public output windows, presentations, scorebugs, overlays, typography, effects, scheduling, timing, screensavers, video and fallback rendering are excluded and remain unchanged. Operator Output controls may be redesigned using the same existing commands/settings. Documents 09/10 define the stronger Control Centre styling.

## Shared application structure

Sports home → sport setup → live workspace. Application settings and What's New are reachable from sports home and the application menu. A deferred saved match appears as a clearly named recovery entry only after Continue later is chosen.

The live shell has a compact match-state strip and one navigation component. Sport-specific destinations share the same shape, selected state, keyboard treatment and motion:

- AFL: **Match / Output / Media / Settings**.
- Cricket: **Match / Scorecard / Output / Settings**.
- Training: one session workspace, with a session menu and output preferences. Do not invent a separate training wizard or unnecessary tabs.

The selected destination is stable across temporary sheets. Switching tabs does not reset the match, output source, settings fields or scroll position. Ctrl+1…4 can remain consistent with visible order. Essential/full operator disclosure stays accessible and labelled; it does not become a separate confusing screen hierarchy.

### Destination responsibilities

| Destination | Owns | Does not own |
|---|---|---|
| Match | Frequent scoring, live clock/readiness, history, immediate match decisions | Asset libraries or deep appearance preferences |
| Scorecard, cricket | Innings navigation, batting/bowling figures, extras, dismissals, fall of wickets, manual adjustments | A second live-scoring keypad |
| Output | Operator controls/status for existing presentations, automation, overlays, display visibility and AFL messages | Public rendering changes; ambiguous duplicate media controls |
| Media, AFL | Local preview, playback and deliberate public takeover | A second persisted ticker editor |
| Settings, live | This match's teams/rules/controls/output overrides and connection entry | Silent alteration of all future matches |
| Application settings | App appearance/interaction, output and sport defaults, startup, data/update preferences | Live team editing, live clock corrections or finals state |

## Coverage register

Each family below requires wide, narrow, keyboard, high-contrast and reduced-motion designs. Conditional rows are states of a shared surface, not necessarily separate windows.

| ID | Surface family | Planned form / important states |
|---|---|---|
| H01 | Sports home | Three equal sport choices, application actions, deferred recovery entry |
| H02 | Continue saved match | Integrated sheet; continue/later/discard; corrupt/incompatible/unavailable save |
| H03 | What's New | Searchable version sheet, concise New/Fixed groups, no results, close/focus return |
| H04 | Application settings | Searchable category workspace with explicit defaults scope |
| H05 | Startup degradation | Persistent but nonblocking subsystem banner with details/retry where possible |
| W01 | Shared wizard shell | Step navigation, body scrolling, Back/Next/Start, invalid/missing fields, return to sports |
| W02 | Team identities | Symmetric home/away cards, manual entry, abbreviations, colours, logos |
| W03 | Club finder | Sport-scoped search/filter/favourite/list-detail, loading/no-results/offline/failed logo |
| W04 | Preset management | Selected marker, save/rename/update/delete, duplicate name and in-use preset |
| W05 | Colour and eyedropper | Wheel, sliders, hex, recent/default colours, live preview, cancel/restore |
| W06 | Logo editor | Load, crop, pan, zoom, transparent preview, reset, apply/cancel/load failure |
| W07 | AFL clock setup | Count-up/countdown, duration, stoppage option, direct numeric input |
| W08 | AFL optional setup | Finals, weather/location, optional messages, concise final review |
| W09 | Cricket squads | Two teams, ordered roster, C/WK roles, add/remove, empty-squad default preview |
| W10 | Cricket format/rules | Limited/multi-innings, typed overs and presets, independent replacement-ball rules, free hits |
| W11 | Cricket venue/review | Weather/location and readable setup summary; toss remains a match action |
| A01 | AFL live strip | Quarter/time, running/paused/break, team scores, separate on-air source |
| A02 | AFL scoring | Home/away Goal and Behind commands, shortcuts, immediate feedback |
| A03 | AFL clock transport | Start/pause, end quarter, resume/continue, Undo, isolated match options |
| A04 | Live clock/stoppage adjustment | Anchored time editor; current and proposed values; apply/cancel/errors |
| A05 | AFL match log | Bounded history, add/edit score, reading-history state, new-event affordance |
| A06 | AFL historical score editor | Team/type required, optional time/quarter, edit/delete, invalid history warning |
| A07 | Quarter and completion decisions | End/resume previous quarter, break/next-quarter/end-game, rapid reversal |
| A08 | AFL reset | Consequence sheet; safe cancel; clear recovery and return-home feedback |
| C01 | Cricket live strip | Innings, score/overs, readiness/break/finalized, separate on-air source |
| C02 | Cricket batting and bowling | Current pair/bowler, scores/balls/figures, strike state, actor menu |
| C03 | Cricket scoring keypad | Dot/1/2/3 then 4/5/6/W, extras row, Undo; preserve order |
| C04 | Readiness gate | Toss, opening pair, replacement batter, next bowler, break, completion, finalized |
| C05 | Toss | Team and bat/bowl tiles, explicit selection, summary, confirm/cancel |
| C06 | Player selectors | Striker/non-striker/bowler steps, disabled reasons, previous bowler, skip/resume |
| C07 | Player action menu | Swap strike, replace, choose bowler, edit figures; anchored and keyboard accessible |
| C08 | Batter replacement | Retirement reason, wrong player, transfer/retain figures, stale-state validation |
| C09 | Extras | Wide/no-ball/bye/leg-bye, selected values, custom input, allocation and total preview |
| C10 | Wicket | Dismissal type, permitted victim, fielder, run-out/extras, free-hit restrictions |
| C11 | Delivery log | Over grouping/filter, descending event order, over summary, edit affordance |
| C12 | Delivery correction | Actor/type/figures, correction scope confirmation, concurrent state/revision change |
| C13 | Scorecard overview | Innings selection, batting/bowling view, totals and edit guidance |
| C14 | Figure editor | Batter/bowler/team numeric edits, validation, apply/cancel |
| C15 | Dismissal editor | Not out, normal dismissal, retirements, other dismissal, bowler/fielder dependencies |
| C16 | Extras and fall of wickets | Inline section below batters; add/edit/remove entries; clear totals |
| C17 | Reconciliation | Structural block versus arithmetic warning; go back/continue anyway; no silent balancing |
| C18 | Adjustment recovery | Clear changed status, audit/revert limitations, stale draft handling |
| C19 | Match options | Breaks, teams/order/roles, format/limit, revised conditions, innings/game decisions |
| C20 | Break/resume | Lunch/Tea/Rain/Drinks/Stumps/Other; same gate resumes directly |
| C21 | Innings/game completion | Normal close, early close, target reached, all out, continue, next eligible innings, end game |
| C22 | Revised conditions | Supplied overs and target, original values, restore, validation; no automatic DLS calculator |
| C23 | Cricket reset/recovery | Confirm, clean exit, sports home remains responsive, saved-game policy |
| O01 | Shared presentation picker | Sport-appropriate cards, selected source, unavailable reasons, window restore |
| O02 | Overlay controls | Automation toggle, manual families, eligibility/resting states, queued/visible/closing status |
| O03 | Current overlay | One named on-air item, one Cancel action, takeover state, live updates |
| O04 | AFL messages | Eight slots, enable/text/colour/highlight/clear, templates, disable-all/undo |
| O05 | Display configuration | Shared 386×193 default/reset, width/height, proportional test size, visibility |
| O06 | Standby configuration | Built-in/image/video, clock option, file failure and fallback |
| O07 | Output rotation | Order/eligibility/interval, quarter-break behaviour, manual override and resume policy |
| M01 | AFL video/media | Local preview, load/unload, transport, seek/speed, explicit public takeover |
| M02 | Emergency/backup playback operator actions | Clear status, safe return to live scoring, failure fallback |
| S01 | Global appearance/interaction | Material, motion, contrast, density, text scaling, hints |
| S02 | Global output/automation defaults | Dimensions, standby, default animation/preferences, future-match scope |
| S03 | Global startup/application | Recovery, updates, version/notes, data and diagnostics |
| S04 | Live match settings | Current teams, rules, clock/finals, sport-specific values |
| S05 | Live controls/output settings | Shortcuts, current display options, animation, automation and weather |
| S06 | Connections | Disconnected/connecting/live/failed, code/QR/URL copy, disconnect and data warning |
| S07 | Updater | Available/downloading/verifying/ready/cancelling/failed/restarting; staged recovery |
| T01 | Training session | Active timer, total, ready/running/paused/completed/no-timer |
| T02 | Training transport | Start/pause, reset-active, skip, +time; distinct from clear session |
| T03 | Training queue | Add/edit/reorder/remove, active/upcoming/completed, empty state, auto advance |
| T04 | Training builder | Timer presets/recents, work/rest rounds, validation, no trailing rest |
| T05 | Training clear/exit | Consequence-aware confirmation or Undo; session-recovery enhancement is a separate proposal |
| X01 | Notifications | Completion toast, persistent warning/error banner, deduplication, useful action/history |
| X02 | General confirmation | Reusable safe/consequential actions, keyboard focus, Escape and close rules |
| X03 | File operations | Native file chooser with correct parent; themed in-app progress/error wrapper |
| X04 | Developer tools | Debug-only grouped scenarios, isolated preview state, restore original match |
| X05 | Gallery/legacy surfaces | Theme component gallery; verify reachability before removing older standalone controls/windows |

Excluded regression baselines: AFL and cricket public scorebugs/presentations/effects, training display, standby, calibration, video and emergency/fallback displays. These are observed for unintended change; they are not redesign work items. O01–O07/M01–M02 concern operator controls only and do not authorize a new scheduler, presentation geometry or public transition.

## Shared sheet architecture, as a design behaviour

One host presents a sheet within the owning application workspace. It has one title, one close control, one scroll body and a persistent footer. A child step replaces the body and adds a Back control; avoid multiple unrelated windows stacked across the desktop.

Small local changes use an anchored popover. Large team/player/scorecard editors use a side sheet or centred task sheet sized to content. Destructive or consequential decisions use a compact centred sheet. The live timer/model continues underneath; modal input boundaries protect against accidental scoring through the scrim.

The native OS file chooser remains a deliberate system exception because it supplies filesystem navigation and accessibility. It must be parented correctly and return to the same sheet/focus. Native error MessageBoxes can be replaced by the common in-app alert system in a later implementation phase.

## Settings scope and persistence

- **Application defaults:** apply to future sessions; persistent preferences. Sport defaults are named as such.
- **This match:** affect the current match only unless the user explicitly chooses Save as default.
- **Asset/preset:** save reusable team/media information independently of current-match changes.
- **Existing output preview controls:** retain current capability and cancellation semantics; redesign their operator editors only. Do not create new public preview/rendering behaviour.
- **Historical correction:** an explicit transaction, with affected scope and validation shown before commit.

Remember selected destination/category, reasonable window dimensions, density and user preferences. Do not silently persist transient validation errors, half-completed destructive confirmations or stale editor transactions as an approved match change.
