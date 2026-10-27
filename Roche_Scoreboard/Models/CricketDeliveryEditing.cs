namespace Roche_Scoreboard.Models;

public enum CricketCorrectionScope { ThisBall, RestOfOver, RestOfInnings }

public sealed record CricketDeliveryEdit
{
    public CricketDeliveryType Type { get; init; }
    /// <summary>Total added, including the one-run penalty on wides and no-balls.</summary>
    public int Runs { get; init; }
    public int StrikerIndex { get; init; }
    public int BowlerIndex { get; init; }
    public CricketNoBallRuns NoBallRuns { get; init; }
    public CricketWicketRuns WicketRuns { get; init; }
    public bool CompletedRunsOnly { get; init; }
    public bool RunOutCrossed { get; init; }
    public DismissalType Dismissal { get; init; } = DismissalType.Bowled;
    public string? Fielder { get; init; }
    public int DismissedBatterIndex { get; init; } = -1;
    public CricketCorrectionScope StrikeScope { get; init; }
    public bool BowlerForRestOfOver { get; init; }
}

public sealed partial class CricketMatchManager
{
    private bool _replaying;
    public int DeliveryRevision { get; private set; }

    private static bool BowlerGetsWicket(DismissalType type) => type is DismissalType.Bowled
        or DismissalType.Caught or DismissalType.CaughtAndBowled or DismissalType.LBW
        or DismissalType.Stumped or DismissalType.HitWicket;

    private static int BowlerRuns(CricketDelivery delivery) => delivery.ScoringType switch
    {
        CricketDeliveryType.Bye or CricketDeliveryType.LegBye => 0,
        CricketDeliveryType.NoBall when delivery.NoBallRuns != CricketNoBallRuns.Bat => 1,
        _ => delivery.Runs
    };

    public static CricketDeliveryEdit DescribeDelivery(CricketInnings innings, CricketDelivery delivery) => new()
    {
        Type = delivery.Type,
        Runs = delivery.Runs,
        NoBallRuns = delivery.NoBallRuns,
        WicketRuns = delivery.WicketRuns,
        CompletedRunsOnly = delivery.CompletedRunsOnly,
        RunOutCrossed = delivery.RunOutCrossed,
        StrikerIndex = Actor(delivery.StrikerIndex, delivery.StateBefore?.StrikerIndex,
            innings.BattingOrder.FindIndex(p => p.DisplayName == delivery.BatsmanName)),
        BowlerIndex = Actor(delivery.BowlerIndex, delivery.StateBefore?.CurrentBowlerIndex,
            innings.BowlingAttack.FindIndex(p => p.DisplayName == delivery.BowlerName)),
        DismissedBatterIndex = delivery.DismissedBatterIndex >= 0 ? delivery.DismissedBatterIndex
            : delivery.StateBefore?.StrikerIndex ?? -1,
        Dismissal = delivery.Dismissal != DismissalType.NotOut ? delivery.Dismissal
            : delivery.DismissalText?.StartsWith("run out", StringComparison.OrdinalIgnoreCase) == true
                ? DismissalType.RunOut : DismissalType.Bowled,
        Fielder = delivery.Fielder
    };

    private static int Actor(int explicitIndex, int? snapshotIndex, int namedIndex) => explicitIndex >= 0
        ? explicitIndex : snapshotIndex is >= 0 ? snapshotIndex.Value : namedIndex;

    /// <summary>Replay on isolated player objects first. No live score, save, event or selection changes until every ball validates.</summary>
    public bool TryEditDelivery(CricketDelivery expected, CricketDeliveryEdit edit, out string error)
    {
        error = "";
        var original = CurrentInnings;
        int index = original?.Deliveries.IndexOf(expected) ?? -1;
        if (original is null || index < 0) { error = "This ball has changed or was undone. Reopen it from the delivery log."; return false; }
        if (edit is null) { error = "The correction is unavailable. Reopen this delivery."; return false; }
        if(original.ScorecardAnchor>=0 && index<original.ScorecardAnchor)
        { error="This ball predates a saved scorecard or player adjustment. Revert the latest scorecard edit before changing earlier balls; the original history has been retained."; return false; }
        if (!Enum.IsDefined(edit.Type) || !Enum.IsDefined(edit.NoBallRuns) || !Enum.IsDefined(edit.WicketRuns) || !Enum.IsDefined(edit.StrikeScope)
            || edit.Runs < 0 || edit.Runs > 999 || edit.StrikerIndex < 0 || edit.StrikerIndex >= original.BattingOrder.Count
            || edit.BowlerIndex < 0 || edit.BowlerIndex >= original.BowlingAttack.Count)
        { error = "Choose a valid batter, bowler, score type and a run value from 0 to 999."; return false; }

        var source = original.Deliveries.ToArray();
        var before = DescribeDelivery(original, expected);
        var scratch = new CricketMatchManager
        {
            Format = Format,
            TotalOvers = TotalOvers,
            ReplaceWideBalls = ReplaceWideBalls,
            ReplaceNoBalls = ReplaceNoBalls,
            FreeHitsEnabled = FreeHitsEnabled,
            TotalInnings = TotalInnings,
            CurrentInningsNumber = CurrentInningsNumber,
            _replaying = true
        };
        scratch.SetTeams(TeamAName, TeamAAbbr, TeamBName, TeamBAbbr);
        scratch.AllInnings.AddRange(AllInnings.Take(CurrentInningsNumber - 1));
        var rebuilt = new CricketInnings
        {
            InningsNumber = original.InningsNumber,
            BattingTeamName = original.BattingTeamName,
            BowlingTeamName = original.BowlingTeamName,
            OverLimitOverride = original.OverLimitOverride,
            TargetOverride = original.TargetOverride,
            AdjustmentNote = original.AdjustmentNote,
            ConditionsDeliveryCount = original.ConditionsDeliveryCount,
            BattingOrder = original.BattingOrder.Select(p => { var copy = p.Clone(); copy.ResetBatting(); return copy; }).ToList(),
            BowlingAttack = original.BowlingAttack.Select(p => { var copy = p.Clone(); copy.ResetBowling(); return copy; }).ToList(),
            StrikerIndex = -1,
            NonStrikerIndex = -1,
            CurrentBowlerIndex = -1
        };
        if(original.ScorecardAnchor>=0)
        {
            // Replay only the ledger after the explicit scorecard baseline.
            var baseline=original.ScorecardAnchor<source.Length ? source[original.ScorecardAnchor].StateBefore : CaptureState(original);
            if(baseline is null) { error="The scorecard baseline is unavailable. No changes were applied."; return false; }
            RestoreState(rebuilt,baseline);
            rebuilt.Deliveries.AddRange(source.Take(original.ScorecardAnchor));
            rebuilt.ScorecardChanges=[.. original.ScorecardChanges];
        }
        rebuilt.IgnoreTarget = original.IgnoreTarget; rebuilt.IgnoreOverLimit = original.IgnoreOverLimit;
        scratch.AllInnings.Add(rebuilt);
        bool propagating = false;
        int Map(int actor) => actor == before.StrikerIndex ? edit.StrikerIndex
            : actor == edit.StrikerIndex ? before.StrikerIndex : actor;
        try
        {
            for (int i = Math.Max(0,original.ScorecardAnchor); i < source.Length; i++)
            {
                // Revised conditions apply prospectively. Earlier balls were
                // actually played under the previous target/limit, and cannot
                // be truncated retrospectively by a newly entered rain target.
                rebuilt.IgnoreTarget = i < original.ConditionsDeliveryCount || original.IgnoreTarget;
                rebuilt.IgnoreOverLimit = i < original.ConditionsDeliveryCount || original.IgnoreOverLimit;
                var old = source[i];
                var input = i == index ? edit : DescribeDelivery(original, old);
                if (rebuilt.IsComplete) { error = $"This correction would end the innings before delivery {i + 1}. Correct the later balls first; no balls were removed."; return false; }
                int nonStriker = Actor(old.NonStrikerIndex, old.StateBefore?.NonStrikerIndex, -1);
                if (nonStriker < 0) { error = "This older save has no non-striker history for this ball. It cannot be safely replayed."; return false; }
                bool following = i > index && edit.StrikeScope != CricketCorrectionScope.ThisBall
                    && (edit.StrikeScope == CricketCorrectionScope.RestOfInnings || old.OverNumber == expected.OverNumber);
                if (following)
                {
                    int nextStriker = Map(input.StrikerIndex), nextNonStriker = Map(nonStriker);
                    // Recalculate strike through the same pair; honour later substitutions/selections.
                    if (propagating && ((rebuilt.StrikerIndex == nextStriker && rebuilt.NonStrikerIndex == nextNonStriker)
                        || (rebuilt.StrikerIndex == nextNonStriker && rebuilt.NonStrikerIndex == nextStriker)))
                    { nextStriker = rebuilt.StrikerIndex; nextNonStriker = rebuilt.NonStrikerIndex; }
                    input = input with
                    {
                        StrikerIndex = nextStriker,
                        DismissedBatterIndex = input.DismissedBatterIndex == input.StrikerIndex ? nextStriker : nextNonStriker
                    };
                    nonStriker = nextNonStriker;
                }
                if (i == index && input.StrikerIndex == nonStriker) nonStriker = before.StrikerIndex;
                if (i > index && edit.BowlerForRestOfOver && old.OverNumber == expected.OverNumber)
                    input = input with { BowlerIndex = edit.BowlerIndex };
                if (input.StrikerIndex == nonStriker || input.StrikerIndex < 0 || nonStriker < 0
                    || input.StrikerIndex >= rebuilt.BattingOrder.Count || nonStriker >= rebuilt.BattingOrder.Count
                    || input.BowlerIndex < 0 || input.BowlerIndex >= rebuilt.BowlingAttack.Count || rebuilt.BattingOrder[input.StrikerIndex].IsOut
                    || rebuilt.BattingOrder[nonStriker].IsOut)
                { error = $"Delivery {i + 1} would use a dismissed batter or the same batter at both ends. Adjust the strike scope or wicket first."; return false; }
                bool pairChanged = rebuilt.StrikerIndex >= 0 && (!new[] { rebuilt.StrikerIndex, rebuilt.NonStrikerIndex }.Contains(input.StrikerIndex)
                    || !new[] { rebuilt.StrikerIndex, rebuilt.NonStrikerIndex }.Contains(nonStriker));
                if (pairChanged) rebuilt.PartnershipRuns = rebuilt.PartnershipBalls = 0;
                if (rebuilt.NeedsBowlerSelection) rebuilt.CurrentOverBalls.Clear();
                if (rebuilt.CurrentBowlerIndex != input.BowlerIndex) rebuilt.PreviousBowlerIndex = rebuilt.CurrentBowlerIndex;
                rebuilt.StrikerIndex = input.StrikerIndex; rebuilt.NonStrikerIndex = nonStriker;
                rebuilt.CurrentBowlerIndex = input.BowlerIndex;
                rebuilt.NeedsBatterSelection = rebuilt.NeedsBowlerSelection = false;
                int count = rebuilt.Deliveries.Count;
                int dismissed = input.DismissedBatterIndex >= 0 ? input.DismissedBatterIndex : input.StrikerIndex;
                scratch.RecordDelivery(input.Runs, input.Type,
                    input.Type == CricketDeliveryType.Wicket ? DismissalLabel(input.Dismissal, input.Fielder, rebuilt.CurrentBowler!.DisplayName) : null,
                    input.NoBallRuns, input.Dismissal, input.Fielder, dismissed, old.Timestamp, input.WicketRuns, input.CompletedRunsOnly, input.RunOutCrossed,
                    (input.Type == CricketDeliveryType.Wicket ? CricketDelivery.WicketScoringType(input.WicketRuns) : input.Type) == old.ScoringType ? old.CountsInOver : null);
                if (rebuilt.Deliveries.Count != count + 1) { error = $"Delivery {i + 1} is not valid. No changes were applied."; return false; }
                if (i >= index) propagating = true;
            }
            // Preserve the operator's selections made after the most recent ball, unless strike propagation was requested.
            rebuilt.IgnoreTarget = original.IgnoreTarget; rebuilt.IgnoreOverLimit = original.IgnoreOverLimit;
            scratch.CheckInningsComplete(rebuilt);
            if (!rebuilt.IsComplete)
            {
                bool validEnd = original.StrikerIndex >= 0 && original.NonStrikerIndex >= 0
                    && original.StrikerIndex < rebuilt.BattingOrder.Count && original.NonStrikerIndex < rebuilt.BattingOrder.Count
                    && !rebuilt.BattingOrder[original.StrikerIndex].IsOut && !rebuilt.BattingOrder[original.NonStrikerIndex].IsOut;
                bool affectsCurrent = edit.StrikeScope == CricketCorrectionScope.RestOfInnings
                    || (edit.StrikeScope == CricketCorrectionScope.RestOfOver && source[^1].OverNumber == expected.OverNumber);
                if (!affectsCurrent && validEnd)
                { rebuilt.StrikerIndex = original.StrikerIndex; rebuilt.NonStrikerIndex = original.NonStrikerIndex; }
                RefreshBatterSelectionState(rebuilt);
                if (rebuilt.BallsInCurrentOver == 0 && !original.NeedsBowlerSelection && original.BallsInCurrentOver == 0)
                {
                    rebuilt.PreviousBowlerIndex = rebuilt.CurrentBowlerIndex;
                    rebuilt.CurrentBowlerIndex = original.CurrentBowlerIndex;
                    rebuilt.NeedsBowlerSelection = false; rebuilt.CurrentOverBalls.Clear();
                }
            }
            bool manuallyClosed = original.IsComplete && !original.IsAllOut
                && (Format != CricketFormat.LimitedOvers || original.IgnoreOverLimit || original.LegalBallsBowled < CurrentOverLimit * 6)
                && (Target is not int target || original.TotalRuns < target);
            if (original.IsDeclared || manuallyClosed) { rebuilt.IsDeclared = original.IsDeclared; rebuilt.IsComplete = true; rebuilt.NeedsBatterSelection = rebuilt.NeedsBowlerSelection = false; }
        }
        catch (ArgumentException) { error = "The correction contains invalid player information. No changes were applied."; return false; }
        RestoreState(original, CaptureState(rebuilt));
        original.Deliveries.Clear(); original.Deliveries.AddRange(rebuilt.Deliveries);
        DeliveryRevision++;
        MatchChanged?.Invoke();
        return true;
    }

    public static string DismissalLabel(DismissalType type, string? fielder, string? bowler) => type switch
    {
        DismissalType.Bowled => $"b {bowler}",
        DismissalType.Caught => $"c {fielder ?? "?"} b {bowler}",
        DismissalType.CaughtAndBowled => $"c&b {bowler}",
        DismissalType.LBW => $"lbw b {bowler}",
        DismissalType.Stumped => $"st {fielder ?? "?"} b {bowler}",
        DismissalType.RunOut => $"run out ({fielder ?? "?"})",
        DismissalType.HitWicket => $"hit wicket b {bowler}",
        DismissalType.ObstructingField => "obstructing the field",
        DismissalType.HitBallTwice => "hit the ball twice",
        _ => "out"
    };
}
