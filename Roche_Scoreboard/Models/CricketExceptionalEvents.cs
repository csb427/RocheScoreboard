namespace Roche_Scoreboard.Models;

public enum CricketMatchResultKind { ScoreBased, Abandoned, NoResult, Unspecified }
public sealed record CricketMatchClosure(int InningsNumber, bool IsComplete, bool NeedsBatterSelection,
    bool NeedsBowlerSelection, CricketBreakKind BreakKind, string BreakNote)
{
    public bool NaturallyComplete { get; init; }
}
public sealed record CricketNonDeliveryWicket(int BatterIndex, int BowlerIndex, DateTime Timestamp);

public sealed partial class CricketMatchManager
{
    public CricketMatchResultKind ResultKind { get; private set; }
    public string ResultNote { get; private set; } = "";
    public CricketMatchClosure? ClosureBefore { get; private set; }
    public string WorkflowToken => MatchConditionsToken + $":{CurrentInnings?.StrikerIndex}:{CurrentInnings?.NonStrikerIndex}:{CurrentInnings?.CurrentBowlerIndex}:{CurrentInnings?.IsComplete}:{CurrentInnings?.TotalRuns}:{CurrentInnings?.TotalWickets}:{CurrentInnings?.LegalBallsBowled}:{BreakKind}:{BreakNote}:{ResultKind}";
    public bool CanRecordNonDeliveryRunOut => !TossPending && !IsMatchFinalized && BreakKind == CricketBreakKind.None
        && CurrentInnings is { IsComplete: false, NeedsBatterSelection: false, NeedsBowlerSelection: false } innings
        && innings.Striker is { IsOut: false } && innings.NonStriker is { IsOut: false } && innings.CurrentBowler is not null
        && innings.StrikerIndex != innings.NonStrikerIndex;
    public bool CanUndoNonDeliveryRunOut => !IsMatchFinalized && CurrentInnings is { } innings
        && innings.ScorecardChanges.LastOrDefault() is { NonDeliveryWicket: not null } change
        && innings.Deliveries.Count == change.AfterDeliveryCount;
    public bool CanUndoLastScoringAction => CanUndoNonDeliveryRunOut || !IsMatchFinalized && CurrentInnings is { } innings
        && innings.Deliveries.LastOrDefault()?.StateBefore is not null
        && (innings.ScorecardAnchor < 0 || innings.Deliveries.Count > innings.ScorecardAnchor);
    public bool HasRecoverableCricketState => IsMatchFinalized || AllInnings.Any(innings => innings.Deliveries.Count > 0
        || innings.ScorecardChanges.Count > 0 || innings.TotalRuns > 0 || innings.TotalWickets > 0 || innings.LegalBallsBowled > 0
        || innings.StrikerIndex >= 0 || innings.NonStrikerIndex >= 0 || innings.CurrentBowlerIndex >= 0);
    public string ResultStatusLabel => ResultKind switch
    {
        CricketMatchResultKind.Abandoned => "ABANDONED", CricketMatchResultKind.NoResult => "NO RESULT",
        CricketMatchResultKind.Unspecified => "ENDED", _ => "RESULT"
    };

    private bool ValidateWorkflowToken(string expectedToken, out string error)
    {
        error = ""; LastOperationError = "";
        if (expectedToken == WorkflowToken) return true;
        error = LastOperationError = "The match changed while this action was open. Reopen it before applying changes.";
        return false;
    }

    /// <summary>MCC 2026 38.3.2: umpire-confirmed run out when the ball was NOT delivered.</summary>
    public bool TryRecordNonStrikerRunOutBeforeDelivery(string expectedToken, out string error)
    {
        if (!ValidateWorkflowToken(expectedToken, out error)) return false;
        if (!CanRecordNonDeliveryRunOut)
        { error = LastOperationError = "Choose both batters and the bowler in an active, unpaused innings before recording this run out."; return false; }
        var innings = CurrentInnings!;
        var before = CaptureState(innings);
        var fall = CreateScorecardDraft(CurrentInningsNumber).FallOfWickets;
        var batter = innings.NonStriker!;
        MarkBattingEntry(innings, innings.NonStrikerIndex);
        batter.IsOut = true; batter.HowOut = DismissalType.RunOut;
        batter.DismissalFielder = innings.CurrentBowler!.DisplayName; batter.DismissalBowler = null;
        batter.DismissalText = $"run out before delivery ({batter.DismissalFielder})";
        innings.TotalWickets++;
        innings.ManualFallOfWickets = fall;
        fall.Add($"{innings.TotalWickets}-{innings.TotalRuns} ({innings.OversDisplay}, before delivery)");
        innings.PartnershipRuns = innings.PartnershipBalls = 0;
        innings.ScorecardChanges.Add(new(innings.Deliveries.Count, before, "Run out before delivery")
        { NonDeliveryWicket = new(innings.NonStrikerIndex, innings.CurrentBowlerIndex, DateTime.Now) });
        innings.ScorecardAnchor = innings.Deliveries.Count;
        RefreshBatterSelectionState(innings); CheckInningsComplete(innings);
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke();
        if (innings.NeedsBatterSelection) BatterSelectionNeeded?.Invoke();
        return true;
    }

    public bool TryUndoLastScoringAction(string expectedToken, out string error)
    {
        if (!ValidateWorkflowToken(expectedToken, out error)) return false;
        if (CanUndoNonDeliveryRunOut) return TryRevertLatestScorecardCorrection(CurrentInningsNumber, out error);
        if (UndoLastDelivery()) return true;
        error = LastOperationError = LastOperationError.Length > 0 ? LastOperationError : "There is no scoring action available to undo.";
        return false;
    }

    public bool TrySetMatchOutcome(CricketMatchResultKind kind, string note, string expectedToken, out string error)
    {
        if (!ValidateWorkflowToken(expectedToken, out error)) return false;
        if (!Enum.IsDefined(kind) || CurrentInnings is null || IsMatchFinalized
            || TossPending && kind is not CricketMatchResultKind.Abandoned and not CricketMatchResultKind.NoResult)
        { error = LastOperationError = "Choose an outcome in an open match. Reopen an ended match before changing its outcome."; return false; }
        note = (note ?? "").Trim();
        if (note.Length > 500) { error = LastOperationError = "Keep the result note within 500 characters."; return false; }
        var innings = CurrentInnings;
        string? calculated = kind == CricketMatchResultKind.ScoreBased ? MatchResult : null;
        ClosureBefore = new(CurrentInningsNumber, innings.IsComplete, innings.NeedsBatterSelection, innings.NeedsBowlerSelection, BreakKind, BreakNote)
        {
            NaturallyComplete = innings.IsComplete && !innings.IsDeclared && (innings.IsAllOut
                || !innings.IgnoreOverLimit && Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled >= CurrentOverLimit * 6
                || !innings.IgnoreTarget && Target is int target && innings.TotalRuns >= target)
        };
        ResultKind = kind == CricketMatchResultKind.ScoreBased && calculated is null ? CricketMatchResultKind.Unspecified : kind;
        ResultNote = note;
        // Ending play does not retroactively turn an unfinished innings into
        // a naturally completed chase, or invent a winning margin.
        FinalResult = ResultKind switch
        {
            CricketMatchResultKind.Abandoned => "Match abandoned", CricketMatchResultKind.NoResult => "No result",
            CricketMatchResultKind.Unspecified => "Match ended · result not recorded", _ => calculated
        };
        IsMatchFinalized = true; innings.NeedsBatterSelection = innings.NeedsBowlerSelection = false;
        BreakKind = CricketBreakKind.None; BreakNote = "";
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke(); return true;
    }

    public bool TryReopenMatch(string expectedToken, out string error)
    {
        if (!ValidateWorkflowToken(expectedToken, out error)) return false;
        if (!IsMatchFinalized || ClosureBefore is not { } before || before.InningsNumber != CurrentInningsNumber || CurrentInnings is not { } innings)
        { error = LastOperationError = "This saved match has no reopening checkpoint. Its figures remain available in the scorecard."; return false; }
        IsMatchFinalized = false; FinalResult = null;
        ResultKind = CricketMatchResultKind.ScoreBased; ResultNote = ""; ClosureBefore = null;
        innings.IsComplete = before.IsComplete && !before.NaturallyComplete;
        innings.NeedsBatterSelection = before.NeedsBatterSelection; innings.NeedsBowlerSelection = before.NeedsBowlerSelection;
        BreakKind = before.BreakKind; BreakNote = before.BreakNote;
        CheckInningsComplete(innings); RefreshBatterSelectionState(innings); RepairBowlerSelection(innings);
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke(); return true;
    }

    private void RefreshFinalizedResult()
    {
        if (!IsMatchFinalized) return;
        FinalResult = null; // Allow the score-based getter to read the corrected figures.
        FinalResult = ResultKind switch
        {
            CricketMatchResultKind.Abandoned => "Match abandoned", CricketMatchResultKind.NoResult => "No result",
            CricketMatchResultKind.Unspecified => "Match ended · result not recorded",
            _ => MatchResult ?? "Match ended · result needs review"
        };
        if (CurrentInnings is { } innings) innings.NeedsBatterSelection = innings.NeedsBowlerSelection = false;
    }

    private void ResetExceptionalState()
    { ResultKind = CricketMatchResultKind.ScoreBased; ResultNote = ""; ClosureBefore = null; }
}
