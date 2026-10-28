namespace Roche_Scoreboard.Models;

public sealed class CricketScorecardDraft
{
    public int InningsNumber { get; set; }
    public int Revision { get; set; }
    public int DeliveryCount { get; set; }
    public int MatchDeliveryCount { get; set; }
    public int TotalRuns { get; set; }
    public int TotalWickets { get; set; }
    public int Wides { get; set; }
    public int NoBalls { get; set; }
    public int Byes { get; set; }
    public int LegByes { get; set; }
    public int LegalBallsBowled { get; set; }
    public List<CricketPlayer> Batters { get; set; } = [];
    public List<CricketPlayer> Bowlers { get; set; } = [];
    public List<string> FallOfWickets { get; set; } = [];
}
public sealed record CricketScorecardChange(int AfterDeliveryCount, CricketInningsState Before, string Reason = "Scorecard correction")
{
    public CricketNonDeliveryWicket? NonDeliveryWicket { get; init; }
}

public sealed partial class CricketMatchManager
{
    public CricketScorecardDraft CreateScorecardDraft(int inningsNumber)
    {
        if (inningsNumber < 1 || inningsNumber > AllInnings.Count) throw new ArgumentOutOfRangeException(nameof(inningsNumber));
        var innings = AllInnings[inningsNumber - 1];
        return new CricketScorecardDraft
        {
            InningsNumber = inningsNumber,
            Revision = DeliveryRevision,
            DeliveryCount = innings.Deliveries.Count,
            MatchDeliveryCount = AllInnings.Sum(i => i.Deliveries.Count),
            TotalRuns = innings.TotalRuns,
            TotalWickets = innings.TotalWickets,
            Wides = innings.Wides,
            NoBalls = innings.NoBalls,
            Byes = innings.Byes,
            LegByes = innings.LegByes,
            LegalBallsBowled = innings.LegalBallsBowled,
            Batters = innings.BattingOrder.Select(p => p.Clone()).ToList(),
            Bowlers = innings.BowlingAttack.Select(p => p.Clone()).ToList(),
            FallOfWickets = innings.ManualFallOfWickets is { } manual ? [.. manual]
                : innings.Deliveries.Where(d => d.Type == CricketDeliveryType.Wicket).Select(d => $"{d.WicketsAfter}-{d.TotalAfter} ({d.OverNumber}.{Math.Max(1, d.BallInOver)})").ToList()
        };
    }
    public IReadOnlyList<string> ValidateScorecard(CricketScorecardDraft draft)
        => [.. ValidateScorecardStructure(draft), .. GetScorecardArithmeticWarnings(draft)];

    /// <summary>These errors can never be bypassed by accepting a reconciliation difference.</summary>
    public IReadOnlyList<string> ValidateScorecardStructure(CricketScorecardDraft draft)
    {
        var errors = new List<string>();
        if (draft is null) return ["The scorecard is unavailable. Reopen it."];
        if (draft.InningsNumber < 1 || draft.InningsNumber > AllInnings.Count) return ["That innings is no longer available."];
        var innings = AllInnings[draft.InningsNumber - 1];
        if (draft.Revision != DeliveryRevision || draft.DeliveryCount != innings.Deliveries.Count || draft.MatchDeliveryCount != AllInnings.Sum(i => i.Deliveries.Count)) errors.Add("The match changed while this editor was open. Reopen the figure before applying changes.");
        if (draft.Batters is null || draft.Bowlers is null || draft.Batters.Count != innings.BattingOrder.Count || draft.Bowlers.Count != innings.BowlingAttack.Count)
            return [.. errors, "The line-up changed. Reopen the scorecard before applying changes."];
        bool Valid(int value) => value is >= 0 and <= 1_000_000;
        if (new[] { draft.TotalRuns, draft.TotalWickets, draft.Wides, draft.NoBalls, draft.Byes, draft.LegByes, draft.LegalBallsBowled }.Any(v => !Valid(v))
            || draft.Batters.Any(p => p is null || !Valid(p.Runs) || !Valid(p.BallsFaced) || !Valid(p.Fours) || !Valid(p.Sixes))
            || draft.Bowlers.Any(p => p is null || !Valid(p.RunsConceded) || !Valid(p.BallsBowled) || !Valid(p.Maidens) || !Valid(p.Wickets) || !Valid(p.Wides) || !Valid(p.NoBalls)))
            return [.. errors, "Use non-negative whole numbers no greater than 1,000,000."];
        bool SameActor(CricketPlayer edited, CricketPlayer original) => edited.RosterIndex == original.RosterIndex && edited.FirstName == original.FirstName && edited.LastName == original.LastName;
        if (draft.Batters.Where((p, i) => !SameActor(p, innings.BattingOrder[i])).Any() || draft.Bowlers.Where((p, i) => !SameActor(p, innings.BowlingAttack[i])).Any()) errors.Add("Player identities cannot be changed through figure adjustments. Use Edit teams.");
        bool KnownFielder(string? name) => string.IsNullOrWhiteSpace(name) || draft.Bowlers.Any(p => p.DisplayName == name || p.FullName == name);
        if (draft.Batters.Any(p => !Enum.IsDefined(p.HowOut) || !KnownFielder(p.DismissalBowler) || !KnownFielder(p.DismissalFielder))) errors.Add("Choose valid dismissal types and fielding players from this innings.");
        if (draft.TotalWickets != draft.Batters.Count(p => p.IsOut)) errors.Add("Team wickets must equal the number of batters marked out. Edit the dismissal to adjust wickets.");
        if (draft.TotalWickets >= draft.Batters.Count) errors.Add("At least one batter must remain not out.");
        if (draft.Bowlers.Sum(p => (long)p.Wickets) > draft.TotalWickets) errors.Add("Bowler wickets cannot exceed team wickets; run-outs are not credited to a bowler.");
        if (draft.Bowlers.Any(p => p.Maidens > p.BallsBowled / 6)) errors.Add("A bowler cannot have more maidens than completed overs.");
        if (draft.FallOfWickets is null || draft.FallOfWickets.Count > draft.TotalWickets || draft.FallOfWickets.Any(line => string.IsNullOrWhiteSpace(line) || line.Length > 120)) errors.Add("Fall-of-wicket notes must be non-empty, at most 120 characters each, and no more numerous than wickets.");
        return errors;
    }

    /// <summary>Arithmetic differences can be saved only after an explicit operator acknowledgement.</summary>
    public IReadOnlyList<string> GetScorecardArithmeticWarnings(CricketScorecardDraft draft)
    {
        if (draft is null || draft.InningsNumber < 1 || draft.InningsNumber > AllInnings.Count || draft.Batters is null || draft.Bowlers is null || draft.Batters.Any(p => p is null) || draft.Bowlers.Any(p => p is null)) return [];
        var warnings = new List<string>(); var innings = AllInnings[draft.InningsNumber - 1];
        long extras = (long)draft.Wides + draft.NoBalls + draft.Byes + draft.LegByes, batting = draft.Batters.Sum(p => (long)p.Runs);
        if (batting + extras != draft.TotalRuns) warnings.Add($"Batters ({batting}) + extras ({extras}) do not equal the team total ({draft.TotalRuns}).");
        if (draft.Batters.Any(p => (long)p.Fours * 4 + (long)p.Sixes * 6 > p.Runs)) warnings.Add("A batter's boundary runs exceed their entered runs.");
        if (draft.Bowlers.Sum(p => (long)p.RunsConceded) != draft.TotalRuns - (long)draft.Byes - draft.LegByes) warnings.Add("Bowler runs do not equal the team total excluding byes and leg-byes.");
        if (draft.Bowlers.Sum(p => (long)p.BallsBowled) != draft.LegalBallsBowled) warnings.Add("Bowler balls do not add up to the innings ball count.");
        if (draft.Bowlers.Sum(p => (long)p.Wides) != draft.Wides || draft.Bowlers.Sum(p => (long)p.NoBalls) != draft.NoBalls) warnings.Add("Bowler wides or no-balls do not equal the innings extras.");
        long faced = draft.Batters.Sum(p => (long)p.BallsFaced);
        long countedWides = innings.Deliveries.Count(d => d.ScoringType == CricketDeliveryType.Wide && d.IsCountedBall);
        long uncountedNoBalls = innings.Deliveries.Count(d => d.ScoringType == CricketDeliveryType.NoBall && !d.IsCountedBall);
        long minimumFaced = Math.Max(0, (long)draft.LegalBallsBowled - countedWides);
        if (faced < minimumFaced || faced > minimumFaced + Math.Max(draft.NoBalls, uncountedNoBalls)) warnings.Add("Batters' balls faced do not reconcile with counted deliveries and no-balls.");
        return warnings;
    }

    public bool TryApplyScorecard(CricketScorecardDraft draft, out string error)
        => TryApplyScorecard(draft, false, out error);
    public bool TryApplyScorecard(CricketScorecardDraft draft, bool allowArithmeticMismatch, out string error)
    {
        var errors = ValidateScorecardStructure(draft);
        var warnings = GetScorecardArithmeticWarnings(draft);
        if (!allowArithmeticMismatch) errors = [.. errors, .. warnings];
        error = string.Join("\n", errors); LastOperationError = error;
        if (errors.Count > 0) return false;
        var innings = AllInnings[draft.InningsNumber - 1];
        var before = CaptureState(innings);
        innings.ManualScorecardWarning = warnings.Count > 0 ? "Manual figures accepted with reconciliation differences:\n" + string.Join("\n", warnings) : "";
        bool chaseWasComplete = CurrentInnings is { IsComplete: true, IsDeclared: false, IgnoreTarget: false } chase
            && Target is int previousTarget && chase.TotalRuns >= previousTarget;
        bool wasNatural = innings.IsAllOut || !innings.IgnoreOverLimit && Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled >= (innings.OverLimitOverride ?? TotalOvers) * 6
            || ReferenceEquals(innings, CurrentInnings) && !innings.IgnoreTarget && Target is int oldTarget && innings.TotalRuns >= oldTarget;
        for (int i = 0; i < draft.Batters.Count; i++) CopyBatting(draft.Batters[i], innings.BattingOrder[i]);
        for (int i = 0; i < draft.Bowlers.Count; i++) CopyBowling(draft.Bowlers[i], innings.BowlingAttack[i]);
        innings.TotalRuns = draft.TotalRuns; innings.TotalWickets = draft.TotalWickets;
        innings.Wides = draft.Wides; innings.NoBalls = draft.NoBalls; innings.Byes = draft.Byes; innings.LegByes = draft.LegByes;
        innings.TotalExtras = draft.Wides + draft.NoBalls + draft.Byes + draft.LegByes;
        innings.LegalBallsBowled = draft.LegalBallsBowled;
        // An explicitly cleared list must remain empty rather than resurrecting
        // historical wickets from the unchanged ball ledger on the next edit.
        innings.ManualFallOfWickets = [.. draft.FallOfWickets];
        innings.ScorecardChanges.Add(new(innings.Deliveries.Count, before, warnings.Count > 0 ? "Manual figures accepted with reconciliation differences" : "Scorecard correction"));
        innings.ScorecardAnchor = innings.Deliveries.Count;
        if (before.LegalBallsBowled != innings.LegalBallsBowled)
        {
            innings.CurrentOverBalls.Clear();
            for (int i = 0; i < innings.BallsInCurrentOver; i++) innings.CurrentOverBalls.Add("");
        }
        if (ReferenceEquals(innings, CurrentInnings))
        {
            if (wasNatural) innings.IsComplete = false;
            RefreshBatterSelectionState(innings);
            CheckInningsComplete(innings);
            if (!innings.IsComplete) innings.NeedsBowlerSelection = innings.CurrentBowler is null || innings.LegalBallsBowled > 0 && innings.BallsInCurrentOver == 0;
        }
        else if (CurrentInnings is { } current)
        {
            // Correcting the first innings also changes the live chase target.
            if (chaseWasComplete) current.IsComplete = false;
            CheckInningsComplete(current);
            RefreshBatterSelectionState(current);
        }
        RefreshFinalizedResult();
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
    public bool TryRevertLatestScorecardCorrection(int inningsNumber, out string error)
    {
        error = "";
        if (inningsNumber < 1 || inningsNumber > AllInnings.Count) { error = "That innings is unavailable."; return false; }
        var innings = AllInnings[inningsNumber - 1];
        if (innings.ScorecardChanges.Count == 0) { error = "There is no manual scorecard edit to revert."; return false; }
        var change = innings.ScorecardChanges[^1];
        if (change.NonDeliveryWicket is not null && !ReferenceEquals(innings, CurrentInnings))
        { error = "A later innings has started. Correct the earlier figures in the scorecard; this action cannot reopen an earlier innings."; return false; }
        if (innings.Deliveries.Count != change.AfterDeliveryCount) { error = "Undo balls added after this scorecard change first. No history was removed."; return false; }
        bool chaseWasComplete = CurrentInnings is { IsComplete: true, IsDeclared: false, IgnoreTarget: false } chase
            && Target is int previousTarget && chase.TotalRuns >= previousTarget;
        bool ignoreTarget = innings.IgnoreTarget, ignoreLimit = innings.IgnoreOverLimit;
        RestoreState(innings, change.Before); innings.ScorecardChanges.RemoveAt(innings.ScorecardChanges.Count - 1);
        innings.IgnoreTarget = ignoreTarget; innings.IgnoreOverLimit = ignoreLimit;
        if (ReferenceEquals(innings, CurrentInnings)) CheckInningsComplete(innings);
        if (!ReferenceEquals(innings, CurrentInnings) && CurrentInnings is { } current)
        {
            if (chaseWasComplete) current.IsComplete = false;
            CheckInningsComplete(current);
            RefreshBatterSelectionState(current);
        }
        RefreshFinalizedResult();
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
    private static void CopyBatting(CricketPlayer source, CricketPlayer target)
    {
        target.BattingEntryOrder=source.BattingEntryOrder;
        target.Runs = source.Runs; target.BallsFaced = source.BallsFaced; target.Fours = source.Fours; target.Sixes = source.Sixes;
        target.IsOut = source.IsOut; target.HowOut = source.IsOut && source.HowOut == DismissalType.NotOut ? DismissalType.Other : source.HowOut;
        target.DismissalText = source.DismissalText; target.DismissalBowler = source.DismissalBowler; target.DismissalFielder = source.DismissalFielder;
        target.IsRetired = source.IsRetired; target.RetiredReason = source.RetiredReason;
    }
    private static void CopyBowling(CricketPlayer source, CricketPlayer target)
    {
        target.BowlingEntryOrder=source.BowlingEntryOrder;
        target.BallsBowled = source.BallsBowled; target.RunsConceded = source.RunsConceded; target.Maidens = source.Maidens; target.Wickets = source.Wickets;
        target.Wides = source.Wides; target.NoBalls = source.NoBalls; target.LastOverBowledAt = source.LastOverBowledAt;
    }

    private static CricketInningsState CopyStateWithIdentities(CricketInningsState state, List<CricketPlayer>? batting, List<CricketPlayer>? bowling)
    {
        var temporary = new CricketInnings(); RestoreState(temporary, state);
        if (batting is not null) SyncIdentities(temporary.BattingOrder, batting);
        if (bowling is not null) SyncIdentities(temporary.BowlingAttack, bowling);
        return CaptureState(temporary);
    }
    private static CricketDelivery CopyDelivery(CricketDelivery d, CricketInningsState? state, Func<int, int>? battingMap = null, string? batsmanName = null, string? bowlerName = null)
    {
        int Map(int index) => battingMap is null || index < 0 ? index : battingMap(index);
        return new CricketDelivery
        {
            Timestamp = d.Timestamp,
            Innings = d.Innings,
            OverNumber = d.OverNumber,
            BallInOver = d.BallInOver,
            Type = d.Type,
            Runs = d.Runs,
            StrikerIndex = Map(d.StrikerIndex),
            NonStrikerIndex = Map(d.NonStrikerIndex),
            BowlerIndex = d.BowlerIndex,
            DismissedBatterIndex = Map(d.DismissedBatterIndex),
            RunOutCrossed = d.RunOutCrossed,
            CountsInOver = d.CountsInOver,
            WasFreeHit = d.WasFreeHit,
            NoBallRuns = d.NoBallRuns,
            WicketRuns = d.WicketRuns,
            CompletedRunsOnly = d.CompletedRunsOnly,
            Dismissal = d.Dismissal,
            Fielder = d.Fielder,
            BatsmanName = batsmanName ?? d.BatsmanName,
            BowlerName = bowlerName ?? d.BowlerName,
            DismissalText = d.DismissalText,
            IsExtra = d.IsExtra,
            TotalAfter = d.TotalAfter,
            WicketsAfter = d.WicketsAfter,
            StateBefore = state
        };
    }
}
