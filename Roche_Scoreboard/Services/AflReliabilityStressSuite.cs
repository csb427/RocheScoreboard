using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard.Services;

internal static class AflReliabilityStressSuite
{
    internal sealed record Check(string Name, bool Pass, string Detail);
    internal sealed record Report(
        int Seeds,
        int OperationsPerSeed,
        int TotalOperations,
        int Assertions,
        int Failures,
        IReadOnlyList<Check> Checks,
        IReadOnlyList<string> FailureSamples);

    public static Report Run(int seeds = 240, int operationsPerSeed = 500)
    {
        List<Check> checks = [];
        List<string> failures = [];
        int assertions = 0;

        void Verify(string name, bool pass, string detail)
        {
            assertions++;
            checks.Add(new Check(name, pass, detail));
            if (!pass && failures.Count < 50) failures.Add($"{name}: {detail}");
        }

        RunDeterministicChecks(Verify);
        RunOverlayChecks(Verify);

        for (int seed = 0; seed < seeds; seed++)
        {
            Random random = new(seed * 104729 + 17);
            MatchManager match = NewMatch();

            for (int step = 0; step < operationsPerSeed; step++)
            {
                ApplyRandomOperation(match, random);

                if (step % 37 == 0)
                    match = RoundTrip(match);

                string? problem = Validate(match);
                assertions++;
                if (problem is not null)
                {
                    if (failures.Count < 50)
                        failures.Add($"seed={seed}; step={step}; {problem}");
                    break;
                }
            }
        }

        int failedChecks = checks.Count(check => !check.Pass);
        int randomizedFailures = failures.Count - failedChecks;
        return new Report(
            seeds,
            operationsPerSeed,
            seeds * operationsPerSeed,
            assertions,
            Math.Max(0, failedChecks + randomizedFailures),
            checks,
            failures);
    }

    private static void RunDeterministicChecks(Action<string, bool, string> verify)
    {
        MatchManager postSiren = NewMatch();
        postSiren.SetElapsed(TimeSpan.FromMinutes(21));
        postSiren.EndQuarter();
        ScoreEvent afterSiren = postSiren.AddGoal(TeamSide.Home);
        verify("post-siren-score-quarter", afterSiren.Quarter == 1,
            $"eventQ={afterSiren.Quarter}; managerQ={postSiren.Quarter}");
        verify("post-siren-score-snapshot", postSiren.GetQuarterSnapshot(1)?.HomeTotal == 6,
            $"snapshot={postSiren.GetQuarterSnapshot(1)?.HomeTotal}");
        int completedBeforeDuplicateEnd = postSiren.CompletedQuarterCount;
        bool duplicateEndAccepted = postSiren.EndQuarter();
        verify("duplicate-end-quarter-is-idempotent",
            !duplicateEndAccepted && postSiren.CompletedQuarterCount == completedBeforeDuplicateEnd && postSiren.Quarter == 2,
            $"accepted={duplicateEndAccepted}; completed={postSiren.CompletedQuarterCount}; quarter={postSiren.Quarter}");
        postSiren.StartClock();
        postSiren.PauseClock();
        ScoreEvent nextQuarter = postSiren.AddBehind(TeamSide.Away);
        verify("next-quarter-score-after-clock-start", nextQuarter.Quarter == 2,
            $"eventQ={nextQuarter.Quarter}");

        MatchManager correction = NewMatch();
        correction.AddGoal(TeamSide.Home);
        correction.EndQuarter();
        correction.StartClock();
        correction.PauseClock();
        correction.RemoveEvent(0);
        QuarterSnapshot? correctedQ1 = correction.GetQuarterSnapshot(1);
        verify("completed-quarter-survives-last-event-delete",
            correctedQ1 is { HomeTotal: 0, AwayTotal: 0 },
            correctedQ1 is null ? "snapshot missing" : $"{correctedQ1.HomeTotal}-{correctedQ1.AwayTotal}");

        MatchManager manualLog = NewMatch();
        manualLog.EndQuarter();
        manualLog.StartClock();
        manualLog.PauseClock();
        manualLog.AddRecordedScore(TeamSide.Home, ScoreType.Goal, 2, TimeSpan.FromMinutes(5));
        manualLog.AddRecordedScore(TeamSide.Away, ScoreType.Behind, 1, TimeSpan.FromMinutes(12));
        verify("manual-log-insert-is-chronological",
            manualLog.Events.Count == 2 && manualLog.Events[0].Quarter == 1 && manualLog.Events[1].Quarter == 2 &&
            manualLog.HomeTotal == 6 && manualLog.AwayTotal == 1,
            $"order={string.Join(',', manualLog.Events.Select(score => $"Q{score.Quarter}@{score.GameTime.TotalMinutes:0}"))}; score={manualLog.HomeTotal}-{manualLog.AwayTotal}");
        manualLog.ModifyEvent(1, TeamSide.Home, ScoreType.Goal, TimeSpan.FromMinutes(2), 1);
        verify("edited-log-event-resorts-timeline",
            manualLog.Events[0].GameTime == TimeSpan.FromMinutes(2) && manualLog.Events[1].GameTime == TimeSpan.FromMinutes(12),
            $"times={string.Join(',', manualLog.Events.Select(score => score.GameTime.TotalMinutes.ToString("0")))}");
        manualLog.AddBehind(TeamSide.Away);
        verify("live-score-after-log-insert-remains-chronological",
            manualLog.Events.Zip(manualLog.Events.Skip(1), (left, right) =>
                left.Quarter < right.Quarter ||
                (left.Quarter == right.Quarter && left.GameTime <= right.GameTime)).All(ordered => ordered)
            && manualLog.ValidateScoreSequence().Count == 0,
            $"order={string.Join(',', manualLog.Events.Select(score => $"Q{score.Quarter}@{score.GameTime.TotalMinutes:0}"))}");

        MatchManager negativeCorrection = NewMatch();
        negativeCorrection.AddGoal(TeamSide.Home);
        bool negativeEditAccepted = negativeCorrection.ModifyEvent(
            0, TeamSide.Away, ScoreType.Behind, TimeSpan.FromMinutes(-3), 1);
        verify("negative-log-time-is-clamped",
            negativeEditAccepted && negativeCorrection.Events[0].GameTime == TimeSpan.Zero
            && negativeCorrection.AwayTotal == 1 && negativeCorrection.ValidateScoreSequence().Count == 0,
            $"accepted={negativeEditAccepted}; time={negativeCorrection.Events[0].GameTime}; score={negativeCorrection.AwayTotal}");

        MatchManager futureQuarterProtection = NewMatch();
        ScoreEvent futureInsert = futureQuarterProtection.AddRecordedScore(
            TeamSide.Home, ScoreType.Goal, 4, TimeSpan.FromMinutes(3));
        futureQuarterProtection.ModifyEvent(0, TeamSide.Away, ScoreType.Behind,
            TimeSpan.FromMinutes(4), 3);
        verify("future-quarter-log-input-is-contained",
            futureInsert.Quarter == 1 && futureQuarterProtection.Events[0].Quarter == 1
            && futureQuarterProtection.AwayTotal == 1,
            $"insertQ={futureInsert.Quarter}; editedQ={futureQuarterProtection.Events[0].Quarter}; score={futureQuarterProtection.AwayTotal}");

        MatchManager extremeClockProtection = NewMatch();
        extremeClockProtection.SetElapsed(TimeSpan.MaxValue);
        extremeClockProtection.AdjustElapsed(TimeSpan.MaxValue);
        extremeClockProtection.SetQuarterDuration(TimeSpan.MaxValue);
        extremeClockProtection.AddRecordedScore(TeamSide.Home, ScoreType.Behind, 1, TimeSpan.MaxValue);
        verify("extreme-clock-input-is-bounded",
            extremeClockProtection.ElapsedInQuarter == TimeSpan.FromHours(24)
            && extremeClockProtection.QuarterDuration == TimeSpan.FromHours(24)
            && extremeClockProtection.Events[0].GameTime == TimeSpan.FromHours(24),
            $"elapsed={extremeClockProtection.ElapsedInQuarter}; duration={extremeClockProtection.QuarterDuration}; event={extremeClockProtection.Events[0].GameTime}");

        DateTime identicalTimestamp = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        MatchManager stableTimeline = new();
        stableTimeline.LoadState(new SerializableState
        {
            Quarter = 1,
            Events =
            [
                new ScoreEvent { Quarter = 1, GameTime = TimeSpan.FromMinutes(2), Timestamp = identicalTimestamp, Team = TeamSide.Home, Type = ScoreType.Goal },
                new ScoreEvent { Quarter = 1, GameTime = TimeSpan.FromMinutes(2), Timestamp = identicalTimestamp, Team = TeamSide.Away, Type = ScoreType.Behind }
            ]
        });
        verify("equal-time-events-keep-input-order",
            stableTimeline.Events.Count == 2 && stableTimeline.Events[0].Team == TeamSide.Home
            && stableTimeline.Events[1].Team == TeamSide.Away,
            $"order={string.Join(',', stableTimeline.Events.Select(score => score.Team))}");

        MatchManager corruptedRecovery = new();
        corruptedRecovery.LoadState(new SerializableState
        {
            HomeName = "RECOVERED HOME",
            AwayName = "RECOVERED AWAY",
            HomeGoals = 99,
            AwayBehinds = 99,
            Quarter = 2,
            CompletedQuarterCount = 1,
            Events =
            [
                new ScoreEvent { Quarter = 7, GameTime = TimeSpan.FromMinutes(-2), Team = TeamSide.Home, Type = ScoreType.Goal },
                new ScoreEvent { Quarter = 1, GameTime = TimeSpan.FromMinutes(8), Team = TeamSide.Away, Type = ScoreType.Behind },
                new ScoreEvent { Quarter = 1, GameTime = TimeSpan.FromMinutes(9), Team = (TeamSide)99, Type = ScoreType.Goal }
            ]
        });
        verify("corrupt-recovery-reconciles-event-history",
            corruptedRecovery.Events.Count == 2
            && corruptedRecovery.Events.All(score => score.Quarter is >= 1 and <= 4 && score.GameTime >= TimeSpan.Zero)
            && corruptedRecovery.HomeTotal == 6 && corruptedRecovery.AwayTotal == 1
            && corruptedRecovery.ValidateScoreSequence().Count == 0,
            $"events={corruptedRecovery.Events.Count}; score={corruptedRecovery.HomeTotal}-{corruptedRecovery.AwayTotal}; warnings={corruptedRecovery.ValidateScoreSequence().Count}");

        MatchManager scoreless = NewMatch();
        scoreless.SetElapsed(TimeSpan.FromMinutes(23));
        scoreless.EndQuarter();
        MatchManager restoredScoreless = RoundTrip(scoreless);
        verify("scoreless-quarter-round-trip",
            restoredScoreless.CompletedQuarterCount == 1 && restoredScoreless.GetQuarterSnapshot(1) is not null,
            $"completed={restoredScoreless.CompletedQuarterCount}; snapshot={restoredScoreless.GetQuarterSnapshot(1) is not null}");
        verify("continue-quarter-round-trip", restoredScoreless.CanContinueQuarter,
            $"canContinue={restoredScoreless.CanContinueQuarter}");

        MatchManager fullTime = NewMatch();
        for (int quarter = 1; quarter <= 4; quarter++)
        {
            fullTime.SetElapsed(TimeSpan.FromMinutes(20 + quarter));
            fullTime.EndQuarter();
            if (quarter < 4)
            {
                fullTime.StartClock();
                fullTime.PauseClock();
            }
        }
        MatchManager restoredFullTime = RoundTrip(fullTime);
        verify("scoreless-full-time-round-trip",
            restoredFullTime.IsFullTime && restoredFullTime.GetQuarterSnapshot(4) is not null,
            $"completed={restoredFullTime.CompletedQuarterCount}; q4={restoredFullTime.GetQuarterSnapshot(4) is not null}");

        MatchManager variableQuarter = NewMatch();
        variableQuarter.SetElapsed(TimeSpan.FromMinutes(5));
        variableQuarter.AddGoal(TeamSide.Home);
        variableQuarter.SetElapsed(TimeSpan.FromMinutes(25));
        variableQuarter.EndQuarter();
        variableQuarter.StartClock();
        variableQuarter.PauseClock();
        variableQuarter.SetElapsed(TimeSpan.FromMinutes(5));
        MatchStats stats = MatchStats.Calculate(variableQuarter, 2, variableQuarter.ElapsedInQuarter);
        verify("time-in-front-uses-recorded-quarter-length", Math.Abs(stats.HomeTimePctInFront - 83.3) <= 0.1,
            $"home={stats.HomeTimePctInFront}; draw={stats.DrawPct}");

        MatchManager countdown = NewMatch();
        countdown.SetClockMode(ClockMode.Countdown);
        countdown.SetQuarterDuration(TimeSpan.FromMinutes(1));
        countdown.SetElapsed(TimeSpan.FromMinutes(2));
        countdown.StartClock();
        countdown.Tick();
        verify("countdown-overrun-clamps-and-stops",
            !countdown.ClockRunning && countdown.DisplayClock == TimeSpan.Zero,
            $"running={countdown.ClockRunning}; display={countdown.DisplayClock}");

        string persistenceDirectory = Path.Combine(Path.GetTempPath(), "RocheScoreboardStress", Guid.NewGuid().ToString("N"));
        string persistencePath = Path.Combine(persistenceDirectory, "state.json");
        try
        {
            AtomicFile.WriteAllText(persistencePath, "first-valid-state");
            AtomicFile.WriteAllText(persistencePath, "second-valid-state");
            string current = AtomicFile.TryReadAllText(persistencePath) ?? string.Empty;
            File.Delete(persistencePath);
            string recovered = AtomicFile.TryReadAllText(persistencePath) ?? string.Empty;
            verify("atomic-persistence-current-and-backup",
                current == "second-valid-state" && recovered == "first-valid-state",
                $"current={current}; recovered={recovered}");

            AtomicFile.WriteAllText(persistencePath, "{\"Value\":41}");
            AtomicFile.WriteAllText(persistencePath, "{\"Value\":42}");
            File.WriteAllText(persistencePath, "{truncated");
            PersistenceProbe? recoveredJson = AtomicFile.TryReadJson<PersistenceProbe>(persistencePath);
            verify("malformed-json-recovers-valid-backup", recoveredJson?.Value == 41,
                $"recovered={recoveredJson?.Value}");
        }
        finally
        {
            try { Directory.Delete(persistenceDirectory, recursive: true); } catch { }
        }

        foreach ((string name, bool pass) in AutoUpdateService.RunSafetyChecks())
            verify($"updater-{name}", pass, pass ? "passed" : "failed");

        verify("stoppage-clock-live-adjustment",
            MainWindow.CalculateAdjustedStoppageTime(TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(10)) == TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(10)
            && MainWindow.CalculateAdjustedStoppageTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-10)) == TimeSpan.Zero,
            "live value changes in place and clamps at zero");
        bool validStoppage = MainWindow.TryParseStoppageTime("12", "34", out TimeSpan parsedStoppage);
        bool rejectsInvalidStoppage = !MainWindow.TryParseStoppageTime("12", "60", out _)
                                       && !MainWindow.TryParseStoppageTime("0", "0", out _)
                                       && !MainWindow.TryParseStoppageTime("abc", "10", out _);
        verify("stoppage-clock-editor-validation",
            validStoppage && parsedStoppage == TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(34)
            && rejectsInvalidStoppage,
            $"valid={validStoppage}; parsed={parsedStoppage}; invalidRejected={rejectsInvalidStoppage}");


        MatchManager highScore = NewMatch();
        for (int i = 0; i < 99; i++) highScore.AddGoal(TeamSide.Home);
        for (int i = 0; i < 99; i++) highScore.AddBehind(TeamSide.Home);
        for (int i = 0; i < 83; i++) highScore.AddGoal(TeamSide.Away);
        for (int i = 0; i < 2; i++) highScore.AddBehind(TeamSide.Away);
        MatchManager restoredHighScore = RoundTrip(highScore);
        verify("rapid-high-score-round-trip",
            restoredHighScore.HomeGoals == 99 && restoredHighScore.HomeBehinds == 99
            && restoredHighScore.HomeTotal == 693 && restoredHighScore.AwayTotal == 500,
            $"home={restoredHighScore.HomeGoals}.{restoredHighScore.HomeBehinds}.{restoredHighScore.HomeTotal}; away={restoredHighScore.AwayTotal}");

        while (restoredHighScore.Events.Count > 0) restoredHighScore.UndoLastScore();
        verify("rapid-high-score-full-undo", restoredHighScore.HomeTotal == 0
            && restoredHighScore.AwayTotal == 0 && restoredHighScore.Events.Count == 0,
            $"score={restoredHighScore.HomeTotal}-{restoredHighScore.AwayTotal}; events={restoredHighScore.Events.Count}");

        MatchManager immediateInput = NewMatch();
        MainWindow.ApplyScoreToModel(immediateInput, TeamSide.Home, ScoreType.Goal);
        MainWindow.ApplyScoreToModel(immediateInput, TeamSide.Away, ScoreType.Behind);
        MainWindow.ApplyScoreToModel(immediateInput, TeamSide.Home, ScoreType.Behind);
        verify("score-input-applies-before-visual-queue",
            immediateInput.HomeGoals == 1 && immediateInput.HomeBehinds == 1
            && immediateInput.AwayBehinds == 1 && immediateInput.Events.Count == 3,
            $"home={immediateInput.HomeGoals}.{immediateInput.HomeBehinds}; away={immediateInput.AwayGoals}.{immediateInput.AwayBehinds}; events={immediateInput.Events.Count}");

        MatchManager drawnFullTime = NewMatch();
        drawnFullTime.AddGoal(TeamSide.Home);
        drawnFullTime.AddGoal(TeamSide.Away);
        for (int quarter = 1; quarter <= 4; quarter++)
        {
            drawnFullTime.EndQuarter();
            if (quarter < 4) { drawnFullTime.StartClock(); drawnFullTime.PauseClock(); }
        }
        MatchManager restoredDraw = RoundTrip(drawnFullTime);
        verify("drawn-full-time-state", restoredDraw.IsFullTime && restoredDraw.Margin == 0,
            $"fullTime={restoredDraw.IsFullTime}; margin={restoredDraw.Margin}");

        MatchManager replyToRun = NewMatch();
        for (int i = 0; i < 4; i++) replyToRun.AddGoal(TeamSide.Away);
        replyToRun.AddGoal(TeamSide.Home);
        AflGoalRunDetector.Result? manualStory = AflGoalRunDetector.DetectBest(replyToRun.Events);
        AflGoalRunDetector.Result? automaticStory = AflGoalRunDetector.Detect(replyToRun.Events, TeamSide.Home);
        verify("goal-run-auto-belongs-to-latest-scorer",
            manualStory?.Team == TeamSide.Away && automaticStory is null,
            $"manual={manualStory?.Team}; auto={automaticStory?.Team}");

        DateTime runBase = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        List<ScoreEvent> separatedRuns = [];
        for (int i = 0; i < 4; i++)
            separatedRuns.Add(new ScoreEvent { Team = TeamSide.Home, Type = ScoreType.Goal, Timestamp = runBase.AddSeconds(i) });
        AflGoalRunDetector.Result? firstRun = AflGoalRunDetector.Detect(separatedRuns, TeamSide.Home);
        // A single reply still leaves a valid 8-of-9 dominance story, so use a
        // meaningful opposition response to close the first story before the
        // same team launches a genuinely separate four-goal streak.
        for (int i = 4; i < 8; i++)
            separatedRuns.Add(new ScoreEvent { Team = TeamSide.Away, Type = ScoreType.Goal, Timestamp = runBase.AddSeconds(i) });
        for (int i = 8; i < 12; i++)
            separatedRuns.Add(new ScoreEvent { Team = TeamSide.Home, Type = ScoreType.Goal, Timestamp = runBase.AddSeconds(i) });
        AflGoalRunDetector.Result? distinctRun = AflGoalRunDetector.Detect(separatedRuns, TeamSide.Home);
        verify("goal-run-distinct-streak-has-new-anchor",
            firstRun is not null && distinctRun is not null && distinctRun.IsConsecutive
            && distinctRun.StartTimestamp > firstRun.EndTimestamp,
            $"firstEnd={firstRun?.EndTimestamp:O}; nextStart={distinctRun?.StartTimestamp:O}");

        MatchManager repeatedDrought = NewMatch();
        repeatedDrought.SetElapsed(TimeSpan.FromMinutes(10));
        AflDroughtCandidate firstDrought = AflDroughtCalculator.Calculate(repeatedDrought).Match;
        repeatedDrought.AddBehind(TeamSide.Home);
        repeatedDrought.SetElapsed(TimeSpan.FromMinutes(20));
        AflDroughtCandidate secondDrought = AflDroughtCalculator.Calculate(repeatedDrought).Match;
        verify("new-drought-has-new-milestone-identity",
            firstDrought.TenMinuteMilestone == 10 && secondDrought.TenMinuteMilestone == 10
            && firstDrought.Key != secondDrought.Key,
            $"first={firstDrought.Key}; second={secondDrought.Key}");

        MatchManager crossQuarterDrought = NewMatch();
        crossQuarterDrought.SetElapsed(TimeSpan.FromMinutes(15));
        crossQuarterDrought.AddGoal(TeamSide.Home);
        crossQuarterDrought.SetElapsed(TimeSpan.FromMinutes(20));
        crossQuarterDrought.EndQuarter();
        crossQuarterDrought.StartClock();
        crossQuarterDrought.PauseClock();
        crossQuarterDrought.SetElapsed(TimeSpan.FromMinutes(5));
        AflDroughtCandidate crossQuarterHome = AflDroughtCalculator.Calculate(crossQuarterDrought).Home;
        verify("drought-carries-playing-time-across-quarters",
            crossQuarterHome.Kind == AflDroughtEventKind.Score
            && crossQuarterHome.Elapsed == TimeSpan.FromMinutes(10),
            $"kind={crossQuarterHome.Kind}; elapsed={crossQuarterHome.Elapsed}");

        int examinedGoalSequences = 0;
        string? goalSequenceProblem = null;
        for (int length = 1; length <= 12 && goalSequenceProblem is null; length++)
        {
            int combinations = 1 << length;
            for (int mask = 0; mask < combinations && goalSequenceProblem is null; mask++)
            {
                List<ScoreEvent> goals = [];
                for (int index = 0; index < length; index++)
                {
                    goals.Add(new ScoreEvent
                    {
                        Team = (mask & (1 << index)) == 0 ? TeamSide.Home : TeamSide.Away,
                        Type = ScoreType.Goal,
                        Timestamp = runBase.AddSeconds(index)
                    });
                }

                examinedGoalSequences++;
                foreach (TeamSide team in Enum.GetValues<TeamSide>())
                {
                    AflGoalRunDetector.Result? result = AflGoalRunDetector.Detect(
                        goals, team, minimumFeaturedGoals: 1, requireLatestGoal: false);
                    if (result is null) continue;
                    double share = (double)result.TeamGoals / result.WindowGoals;
                    if (result.Team != team || result.TeamGoals < 1 || result.WindowGoals < result.TeamGoals
                        || (!result.IsConsecutive && share < 0.75))
                    {
                        goalSequenceProblem = $"length={length}; mask={mask}; team={team}; result={result}; share={share:0.###}";
                        break;
                    }
                }
            }
        }
        verify("goal-run-exhaustive-combinations", goalSequenceProblem is null,
            goalSequenceProblem ?? $"validated={examinedGoalSequences} sequences");

        double zeroWidthColumn = StatsScreenControl.CalculateValueColumnWidth(0);
        double transientWidthColumn = StatsScreenControl.CalculateValueColumnWidth(80);
        verify("stats-transient-layout-widths",
            double.IsFinite(zeroWidthColumn) && zeroWidthColumn >= 0
            && double.IsFinite(transientWidthColumn) && transientWidthColumn > 0 && transientWidthColumn <= 80,
            $"zero={zeroWidthColumn:0.##}; eighty={transientWidthColumn:0.##}");

        double compactLogHeight = MainWindow.CalculateMatchLogPanelMaxHeight(320);
        double standardLogHeight = MainWindow.CalculateMatchLogPanelMaxHeight(1050);
        verify("match-log-is-viewport-bounded",
            compactLogHeight == 180 && standardLogHeight == 740,
            $"compact={compactLogHeight:0.##}; standard={standardLogHeight:0.##}");

        int[] fivePanelDelays = Enumerable.Range(0, 5).Select(i => MainWindow.GetFoldDelay(i, 5)).ToArray();
        int[] threePanelDelays = Enumerable.Range(0, 3).Select(i => MainWindow.GetFoldDelay(i, 3)).ToArray();
        verify("quarter-fold-cadence-is-monotonic",
            fivePanelDelays[0] == 0
            && threePanelDelays[0] == 0
            && fivePanelDelays.Zip(fivePanelDelays.Skip(1), (left, right) => right > left).All(increases => increases)
            && threePanelDelays.Zip(threePanelDelays.Skip(1), (left, right) => right > left).All(increases => increases)
            && fivePanelDelays[^1] <= 250
            && threePanelDelays[^1] <= 250,
            $"five={string.Join(',', fivePanelDelays)}; three={string.Join(',', threePanelDelays)}");
    }

    private sealed class PersistenceProbe
    {
        public int Value { get; set; }
    }

    private static void RunOverlayChecks(Action<string, bool, string> verify)
    {
        DateTime now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        OverlayScheduler scheduler = new(() => now);
        scheduler.RegisterOverlays(
        [
            new OverlayDefinition
            {
                Kind = OverlayKind.StatsBar,
                DisplayDuration = TimeSpan.FromSeconds(8),
                MinInterval = TimeSpan.FromSeconds(60),
                Priority = 60,
                IsRelevant = () => true,
                Family = OverlayFamily.MatchStory
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Weather,
                DisplayDuration = TimeSpan.FromSeconds(8),
                MinInterval = TimeSpan.FromSeconds(60),
                Priority = 90,
                IsRelevant = () => true,
                Family = OverlayFamily.Conditions
            },
            new OverlayDefinition
            {
                Kind = OverlayKind.Forecast,
                DisplayDuration = TimeSpan.FromSeconds(8),
                MinInterval = TimeSpan.FromSeconds(60),
                Priority = 100,
                IsRelevant = () => throw new InvalidOperationException("transient provider failure"),
                Family = OverlayFamily.Conditions
            }
        ]);
        verify("overlay-registration-has-unique-kinds",
            scheduler.Definitions.Select(definition => definition.Kind).Distinct().Count() == scheduler.Definitions.Count,
            $"definitions={scheduler.Definitions.Count}");

        scheduler.NotifyQuarterStart();
        now += TimeSpan.FromSeconds(60);
        verify("overlay-early-buffer", scheduler.TryGetNextOverlay(
            TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(20), true, false) is null, "must suppress first 75 seconds");

        now += TimeSpan.FromSeconds(20);
        OverlayDefinition? first = scheduler.TryGetNextOverlay(
            TimeSpan.FromSeconds(80), TimeSpan.FromMinutes(20), true, false);
        verify("overlay-active-window", first is not null, $"selected={first?.Kind}");
        if (first is not null)
        {
            scheduler.NotifyOverlayShown(first.Kind);
            scheduler.NotifyOverlayHidden();
        }

        now += TimeSpan.FromSeconds(80);
        scheduler.NotifyScoreEvent();
        now += TimeSpan.FromSeconds(21);
        verify("overlay-score-cooldown", scheduler.TryGetNextOverlay(
            TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(20), true, false) is null, "score cooldown should still be active");
        now += TimeSpan.FromSeconds(2);
        verify("overlay-break-screen-hard-stop", scheduler.TryGetNextOverlay(
            TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(20), true, true) is null, "break screen must suppress overlays");
        verify("overlay-clock-stopped-hard-stop", scheduler.TryGetNextOverlay(
            TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(20), false, false) is null, "stopped clock must suppress overlays");
        verify("overlay-quarter-ended-hard-stop", scheduler.TryGetNextOverlay(
            TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), true, false) is null, "ended quarter must suppress overlays");

        scheduler.NotifyOverlayShown(OverlayKind.Weather);
        scheduler.NotifyQuarterStart();
        now += TimeSpan.FromSeconds(80);
        OverlayDefinition? nextQuarter = scheduler.TryGetNextOverlay(
            TimeSpan.FromSeconds(80), TimeSpan.FromMinutes(20), true, false);
        verify("overlay-quarter-reset-clears-active-and-freshness",
            nextQuarter is not null,
            $"selected={nextQuarter?.Kind}");

        if (nextQuarter is not null)
        {
            scheduler.NotifyOverlayShown(nextQuarter.Kind);
            scheduler.NotifyOverlayHidden();
        }
        now -= TimeSpan.FromMinutes(5);
        OverlayDefinition? afterClockCorrection = scheduler.TryGetNextOverlay(
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), true, false);
        verify("overlay-clock-rewind-does-not-freeze-scheduler",
            afterClockCorrection is not null,
            $"selected={afterClockCorrection?.Kind}");
    }

    private static MatchManager NewMatch()
    {
        MatchManager match = new();
        match.SetTeams("HOME", "HOM", "AWAY", "AWY");
        return match;
    }

    private static MatchManager RoundTrip(MatchManager source)
    {
        string json = JsonSerializer.Serialize(source.ToState());
        SerializableState state = JsonSerializer.Deserialize<SerializableState>(json)
            ?? throw new InvalidOperationException("AFL state round-trip returned null.");
        MatchManager restored = new();
        restored.LoadState(state);
        return restored;
    }

    private static void ApplyRandomOperation(MatchManager match, Random random)
    {
        int operation = random.Next(100);
        TeamSide side = random.Next(2) == 0 ? TeamSide.Home : TeamSide.Away;

        if (operation < 21) match.AddGoal(side);
        else if (operation < 43) match.AddBehind(side);
        else if (operation < 51) match.UndoLastScore();
        else if (operation < 57 && match.Events.Count > 0) match.RemoveEvent(random.Next(match.Events.Count));
        else if (operation < 64 && match.Events.Count > 0)
        {
            match.ModifyEvent(
                random.Next(match.Events.Count),
                side,
                random.Next(2) == 0 ? ScoreType.Goal : ScoreType.Behind,
                TimeSpan.FromSeconds(random.Next(0, 35 * 60)));
        }
        else if (operation < 67)
        {
            match.AddRecordedScore(
                side,
                random.Next(2) == 0 ? ScoreType.Goal : ScoreType.Behind,
                random.Next(1, 5),
                TimeSpan.FromSeconds(random.Next(-120, 35 * 60)));
        }
        else if (operation < 72) match.StartClock();
        else if (operation < 77) match.PauseClock();
        else if (operation < 83) match.SetElapsed(TimeSpan.FromSeconds(random.Next(0, 35 * 60)));
        else if (operation < 88) match.AdjustElapsed(TimeSpan.FromSeconds(random.Next(-180, 181)));
        else if (operation < 93) match.EndQuarter();
        else if (operation < 96) match.ContinueQuarter();
        else if (operation < 99)
        {
            match.SetClockMode(random.Next(2) == 0 ? ClockMode.CountUp : ClockMode.Countdown);
            match.SetQuarterDuration(TimeSpan.FromMinutes(random.Next(10, 31)));
            match.Tick();
        }
        else
        {
            _ = MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter);
            _ = AflGoalRunDetector.DetectBest(match.Events, minimumFeaturedGoals: 1);
            _ = AflDroughtCalculator.Calculate(match);
        }
    }

    private static string? Validate(MatchManager match)
    {
        int hg = 0, hb = 0, ag = 0, ab = 0;
        foreach (ScoreEvent score in match.Events)
        {
            if (score.Quarter is < 1 or > 4) return $"event quarter out of range: {score.Quarter}";
            if (score.GameTime < TimeSpan.Zero) return $"negative event time: {score.GameTime}";

            if (score.Team == TeamSide.Home)
            {
                if (score.Type == ScoreType.Goal) hg++;
                else hb++;
            }
            else
            {
                if (score.Type == ScoreType.Goal) ag++;
                else ab++;
            }

            if (score.HomeGoals != hg || score.HomeBehinds != hb || score.AwayGoals != ag || score.AwayBehinds != ab)
                return "event cumulative snapshot mismatch";
        }

        if (match.HomeGoals != hg || match.HomeBehinds != hb || match.AwayGoals != ag || match.AwayBehinds != ab)
            return $"manager/event score mismatch manager={match.HomeGoals}.{match.HomeBehinds}-{match.AwayGoals}.{match.AwayBehinds} events={hg}.{hb}-{ag}.{ab}";

        if (match.ValidateScoreSequence().Count != 0)
            return $"score validation emitted {match.ValidateScoreSequence().Count} warning(s)";

        int cumulativeHg = 0, cumulativeHb = 0, cumulativeAg = 0, cumulativeAb = 0;
        for (int quarter = 1; quarter <= 4; quarter++)
        {
            foreach (ScoreEvent score in match.Events.Where(score => score.Quarter == quarter))
            {
                if (score.Team == TeamSide.Home)
                {
                    if (score.Type == ScoreType.Goal) cumulativeHg++;
                    else cumulativeHb++;
                }
                else
                {
                    if (score.Type == ScoreType.Goal) cumulativeAg++;
                    else cumulativeAb++;
                }
            }

            QuarterSnapshot? snapshot = match.GetQuarterSnapshot(quarter);
            if (quarter <= match.CompletedQuarterCount)
            {
                if (snapshot is null) return $"completed Q{quarter} snapshot missing";
                if (snapshot.HomeGoals != cumulativeHg || snapshot.HomeBehinds != cumulativeHb
                    || snapshot.AwayGoals != cumulativeAg || snapshot.AwayBehinds != cumulativeAb)
                    return $"Q{quarter} snapshot mismatch";
            }
            else if (snapshot is not null)
            {
                return $"uncompleted Q{quarter} has a snapshot";
            }
        }

        MatchStats stats = MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter);
        double pct = stats.HomeTimePctInFront + stats.AwayTimePctInFront + stats.DrawPct;
        if (pct != 0 && Math.Abs(pct - 100) > 0.2) return $"time-in-front percentages sum to {pct}";
        return null;
    }
}
