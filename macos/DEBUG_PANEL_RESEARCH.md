# Debug panel research (after the Mac port)

This is a proposed testing catalogue, not an implemented panel or a complete
enumeration of the Windows app. It is derived from the current model, service and
view branches, rather than assumed scoreboard features. The Mac port comes first.

## Proposed behavior

Make the panel an explicit **Demo testing** tool with saved scenario fixtures,
an explanation of each trigger and expected result, and **Restore previous demo**.
Load scenarios into an isolated session using the real engines; do not bypass
validations or mutate production/remote sessions. Pair every threshold with
just-before / exactly-at / just-after cases. Provide deterministic random seeds
for probability calculations and replayable event timelines for scoring stories.

A timer speed control alone is insufficient: playing time, wall time, presentation
holds and delayed callbacks are different clocks. Use an injected clock at the
controller/presentation boundary, following the existing `OverlayScheduler` clock
delegate; a later, separately reviewed engine change would be needed to inject
time into the `Stopwatch`-based match/training engines. Until then, set elapsed time
through the models' supported methods. Never sleep for ten minutes in a test.

## Scenario catalogue

| Group and source | Scenarios to expose |
| --- | --- |
| AFL clock: `Models/MatchManager.cs` | Count up/down; pause/resume; UI delay; elapsed zero/negative/24-hour clamp; countdown just before/at/after expiry; duration changed while running; expiry fires once; start at full time; repeated Start/Pause |
| AFL quarters: `Models/MatchManager.cs` | End Q1/Q2/Q3/Q4; repeated End Quarter during break; continue before next start; continue after next start; post-siren goal/behind including Q4; saved break restored; score added just as clock expires |
| AFL timeline: `Models/MatchManager.cs`, `Models/ScoreEvent.cs` | Empty undo; duplicate timestamps; insert earlier score; edit team/type/quarter/time; remove first/middle/last; correction across quarter boundary; stale scalar totals vs authoritative event history; legacy totals-only recovery; bad enum and future-quarter input; snapshot reconciliation |
| AFL runs: `Services/AflGoalRunDetector.cs` | Three/four/five straight goals; behinds interleaved; run crosses quarters; four of five vs four straight; dominance at 75%; opposition reply; dominance adds one vs two extra goals; both teams have candidate stories; timestamps produce distinct story identity |
| AFL droughts: `Services/AflDroughtCalculator.cs` | No score since bounce; score vs goal drought; behind resets score drought only; 9:59/10:00/10:01 and 19:59/20:00/20:01; home/away/match scopes; quarter break excluded from playing time; short vs long recorded quarters; future event clamp; a new drought after an earlier milestone |
| Overlay timing: `Services/OverlayScheduler.cs` | Early buffer 75 s; late reduced period 90 s remaining; suppression 30 s remaining; scoring cooldown 22 s; goal-animation cooldown 12 s; event cooldown 35 s; post-overlay buffer 35 s; minimum spacing 75 s; quiet threshold two minutes/spacing 95 s; two busy events within two minutes; 45 s extra busy delay; short/long quarter adaptive spacing |
| Overlay priority/fairness: `Services/OverlayScheduler.cs` | Empty/ineligible catalogue; relevancy changes; stale priority tie; conditions vs match-story families; late-quarter minimum priority 80; goal interrupts an information overlay; overlay queued during pause; reset at quarter boundary; repeat suppression and fairness after long inactivity |
| AFL presentation: `Views/ScorebugControl.xaml.cs` and related views | Goal during opening/closing animation; behind during goal; undo/correction while overlay visible; goal/announcement contention; long/unicode/blank names; goals/behinds expand/collapse and inactivity; centre-label override across breaks; compact aspect threshold 1.30 with 0.05 hysteresis; rapid resize; missing/corrupt/translucent logos; stacked/wide layouts; reduced motion |
| AFL statistics: `Models/MatchStats.cs`, `Models/WinProbabilityEngine.cs` | Nil-all; tied game; one event; alternating leads; largest-lead correction; time-in-front over breaks; accuracy zero shots; mismatched elapsed quarter lengths; pregame/final-time probability; extreme margins; late comeback; deterministic seeded replay; probability cancellation/background calculation |
| Cricket setup: `Models/CricketMatchManager.cs` | Two/eleven-player rosters; alternate batting order; limited overs/multi-day/multi-innings; missing pair/bowler; same player selected twice; dismissed batter selected; no consecutive-over bowler; changing active players; distinct per-innings player objects |
| Cricket deliveries: `Models/CricketMatchManager.cs` | Dot and 1–6 runs; odd/even strike rotation; wide with penalty-only/additional runs; no-ball penalty/off-bat runs/boundaries/balls-faced; byes/leg-byes; sixth legal ball vs illegal ball; maiden with byes; extras in maiden; wicket and run-out bowler credit; wicket on sixth ball; wicket then bowler selection |
| Cricket lifecycle: `Models/CricketMatchManager.cs` | All out with a small roster; overs exhausted; declaration/end innings; repeated end/next; chase reached by run/boundary/extra; tie; multi-day lead/trail and innings result; undo at all-out/over end/target completion; restore pending batter/bowler prompts; previous scorecards survive a new innings |
| Cricket milestones/analytics: `Models/CricketAnalytics.cs` and `Models/CricketMatchManager.cs` | Cross 50/100/150/200 by 1/4/6/no-ball bat runs; no milestone for penalty-only extras; 18-ball boundary drought; recent vs required rate at ±1.5; wickets in hand at three; phase boundaries; zero legal balls; powerplay boundaries for 10/20/40/50-over formats; undo a milestone |
| Training: `Models/TrainingSession.cs`, `Views/TrainingScorebugControl.xaml.cs` | Empty queue; zero/one-second timer; 10-second warning boundary; pause at expiry; resume expired; immediate auto-start during 7.5-second finish presentation; final 10-second completion notice; auto-advance off; skip/reset/remove during finish; remove active/previous/upcoming; reorder active and surrounding timers; edit running duration shorter/longer; extend paused/running; app close/recovery during transition |
| Messages and media: `Views/ScorebugControl.xaml.cs`, `Views/VideoPlayerControl.xaml.cs` | Empty/one/many ticker messages; long/unicode text; rotation at five-second boundary; 4.2-second announcement interrupted/replaced; missing/unsupported/corrupt media; rapid source switching; loop/end/error; media decode while clock runs. Media scenarios require the media port first |
| Weather: `Services/WeatherService.cs` | Geocoding miss; timeout; cancellation after location change; empty forecast; rain threshold/forecast crossing midnight; units and icon mapping; refresh during dispose; offline/cached data. Weather scenarios require the weather port first |
| Storage/windowing: `Services/AtomicFile.cs` plus Mac controller/store | Truncated primary/valid backup; both corrupt; unwritable storage; disk-full error; version mismatch; malformed imported session; cancel save/load/reset; output closed/reopened; controller close with output open; sleep/wake; two app instances; monitor unplug/reconnect; Retina scale; minimum window; file picker cancellation; keyboard focus prevents scoring while typing |

## Suggested order

1. Finish port parity and Mac acceptance checks. Avoid tests for UI features that
   have not been ported.
2. Implement deterministic AFL scenario fixtures and session restore, since the
   story/quarter/clock triggers are the largest manual-testing burden.
3. Add overlay clock controls once the overlay scheduler is used by the port.
4. Add cricket and training fixture groups, then media/weather/network faults as
   those integrations arrive.
5. Turn validated regressions into automated tests alongside the debug fixtures,
   without tests that merely repeat the scenario-loading implementation.

The initial Mac regression suite already covers several engine and window cases;
it does not claim to cover this entire catalogue or Windows-only presentation code.
