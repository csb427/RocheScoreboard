# Windows testing feedback verification

Windows source/assets are byte-identical to the tested/cross-built local revision. Publication changes documentation/manifest only.

- **59 distinct affected cases have passing latest results across two runs**, not one59/59 run:57passed/2fixture-failed in the broader59-case run, then both corrected test-only cases passed. The fixture had incorrectly tried to focus a non-interactive consequence TextBlock; the correction scrolls readable copy without making it focusable. Product source did not change between these two runs.0skips/aborts.
- Coverage: actual runtime-built team-form arrivals/first use/reduced motion/rapid navigation; setup replacement/default cancellation/stale completion; integrated new-session decisions; remote command retirement across replacement; AFL/cricket Reset cancel/confirmation/repeat/stale guards; release navigation/compact reachability; cricket actions/dock/scrolling and current-play/output layout.
- Windows Release:0warnings/0errors24.54s. Debug:0warnings/0errors24.01s.
- Linux translated native product build:0errors/190inherited warnings31.17s. Test-only build:0/0 13.29s. Translation audit49XAML/307C#/575declared UI bindings.
- MatchManager, CricketMatchManager, GameStateService, CricketRecoveryWriter and ScorebugControl source bytes remain unchanged from5ea23401. History remains analysis only: no archive/schema/storage/reset-deletion change.

Earlier preview test counts describe earlier source epochs and overlap; do not add them to this revision's count. No new full historical suite, native Windows GUI, MacCI/package, signing or monitor/hardware acceptance is claimed. First-use animation causes were repaired in shared source and observed in Linux renderer phases; this does not certify native Windows smoothness. Exact source file hashes are recorded in SOURCE-SNAPSHOT.json.
