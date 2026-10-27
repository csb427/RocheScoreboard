# Cricket presentation order, graphics and milestone review

27 October 2026. This request explicitly authorizes changes to the cricket public output as a separate feature pass. AFL output, scoring rules, global appearance and release version remain outside this pass.

## Appearance order

The presentation used indexed squad order, which is not the order in which the operator actually selected the players. Cricket players now retain innings-scoped batting/bowling entry positions through cloning, undo snapshots and save serialization. New innings reset those positions. The presentation constructs a separate chronological view from deliveries (including non-strikers and illegal deliveries), selected players, recorded entry positions and then the unbatted planned lineup. It does not reorder the indexed model, invalidate delivery actor indices or alter team presets. Legacy saves without entry fields reconstruct appearances from delivery history. Bowling first appearances are indexed in one scan rather than repeatedly searching the log during sorting.

The batting and bowling order gutter is 80 design pixels instead of 48, at the same 38-pixel type size. Double-digit labels retain their complete number and period. Pagination covers 11, 12 and larger squads without an off-screen scroll region on the public output.

## Extras and selectors

The integrated extras shell has an explicit rounded clipping geometry; Border.CornerRadius alone does not clip child content. Choice buttons have spacing on both axes, preventing hover surfaces from touching neighbouring choices. The calculation is a separate rounded panel above the primary Add Delivery action. The content host stretches into its actual allocation, while the middle form body scrolls independently and keeps the calculation/action reachable at short dimensions. All four entry variants are captured at 620 by 620 and 420 by 320.

Batter/bowler selectors reset their actual internal scroll viewer on opening and after loaded layout. They then reveal the selected or suggested item. This retains the existing bowler recommendation order rather than blindly jumping to an unrelated roster entry.

## New cricket overlays and celebrations

Innings boundaries shows FOUR and SIX counts from batting figures, including boundaries off no-balls and excluding byes, leg-byes and wide penalties. The scheduler normally waits eight seconds after a boundary. The global quiet period, group cooldown, correction guard and automatic airtime budget still apply, so this is not a compulsory graphic after every boundary. A boundary item expires from automatic eligibility after 40 seconds and cannot repeat for the same boundary. Manual activation remains available.

Comparison now contains only the first-innings opponent score at the corresponding legal-ball point. Its team surface, team name, heading and large wickets/runs figure refer to that opponent. The chase score remains in the live scoreboard; it is not repeated in the graphic.

A batter crossing 50 or 100 from credited batting runs produces a milestone celebration containing the number, player and balls faced. Penalty/extras alone cannot produce a batting milestone. A boundary celebration precedes the milestone when both happen on the same delivery. A century has a longer hold, larger depth impact and a warm coloured type echo. New milestones can replace a still-active prior milestone; rapid event handling remains bounded rather than creating an unlimited animation queue. Undo, correction, match replacement and innings changes invalidate stale celebrations through the existing output handoff guards. Scoring in the operator model remains immediate; the public figures catch up after the presentation.

## Scoreworm and Manhattan

Both new choices are available beside the existing presentation buttons, refresh from live match changes and join the break rotation. They use the cricket broadcast typeface, dark field, sharp geometry and team gradients, not operator Liquid Glass.

The scoreworm plots cumulative runs against legal-ball progress on common axes. Illegal deliveries stay at the same horizontal progress while still adding runs. Wicket markers remain attached to the corresponding point. The Manhattan plots all runs per over, including extras and partial overs, side by side for the innings pair, with wicket markers. Long innings use 20-over pages with the existing 18-second presentation dwell so that bars remain distinguishable at scoreboard dimensions. Multi-innings matches compare the current innings with the latest opposing innings, explicitly identified in the legend, rather than merging separate batting innings into a misleading total. Empty history produces a clear resting state.

Axes have a small number of large labels with separate number/title rows. Colours are lifted only as needed for contrast; clashing series can use a secondary colour or a dashed/marked second series. No chart library or runtime network dependency was added.

References: [Gully Crix chart documentation](https://www.gullycrix.com/help/results-and-sharing/worm-and-manhattan-charts) describes cumulative worm, per-over Manhattan and wicket markers. [The Maldives Cricket match report](https://www.maldivescricket.org/wp-content/uploads/2017/05/Malaysia-v-Maldives-Match-1.pdf) supplies an official match example of the paired broadcast/statistical graphics. The installed WPF skill was used as reference while retaining the app architecture.

## Verification and limits

Evidence is under `artifacts/cricket-broadcast-expansion/`. The first broad regression run exposed checks tied to previous UI categories, scroll containers and shorter animation timings; these were corrected to target the current controls and wait for actual completion with a timeout. A subsequent run is recorded separately rather than overwriting the earlier failures. Native screenshots are inspected for form shape, numbering, comparisons, chart labels, and milestone hierarchy. The verification summary records final builds and all reports.

Automatic overlay scheduling deliberately respects quiet play and airtime limits; an eight-second post-boundary opportunity does not override a wicket, celebration, player selection, break or the existing cooldown. Charts visualize recorded deliveries; manual scorecard adjustments cannot invent missing over-by-over history. Automated tests and local screenshots do not guarantee legibility at an arbitrary physical viewing distance or a particular frame rate on every laptop. No version bump, commit, push or release is performed.

Final results: Debug and Release builds each completed with zero warnings and errors. The broad Debug cricket regression report passed 1,274 checks; the final Release motion/broadcast report passed 135 checks. The final Release pass also covers the last extras-calculation wording and four additional presentation-button/live-refresh assertions added after the broad run. Counts overlap. The real appearance-preference hash is unchanged, and Graphify was refreshed. Actual output screenshots were reviewed at 386x193, 772x386 and 286x193; extras screens were reviewed at 620x620 and 420x320. Developer-only 50/100 preview buttons allow review without modifying delivery history. All evidence is listed in artifacts/cricket-broadcast-expansion/verification-summary.json.
