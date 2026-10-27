namespace Roche_Scoreboard.Models;

public sealed partial class CricketMatchManager
{
    private readonly Guid _conditionsIdentity = Guid.NewGuid();
    private long _conditionsVersion;
    public string MatchConditionsToken => $"{_conditionsIdentity:N}:{_conditionsVersion}:{DeliveryRevision}:{CurrentInningsNumber}:{Format}:{TotalOvers}:{CurrentOverLimit}:{CurrentInnings?.TargetOverride}:{TossPending}:{IsMatchFinalized}";
    public int CurrentOverLimit => CurrentInnings?.OverLimitOverride ?? TotalOvers;
    public bool HasRainAdjustment => !string.IsNullOrEmpty(CurrentInnings?.AdjustmentNote);

    public bool TryUpdateMatchConditions(CricketFormat format, int overs, string expectedToken, out string error)
    {
        if (!ValidateConditionsEdit(expectedToken, out error)) return false;
        if (!Enum.IsDefined(format) || overs is < 1 or > 1000)
        { error = "Select a match format and an over limit from 1 to 1,000."; return false; }
        if (format == CricketFormat.LimitedOvers && CurrentInnings!.LegalBallsBowled > overs * 6)
        { error = "The over limit cannot be lower than the legal balls already bowled."; return false; }
        bool reopen = NaturallyCompleted(CurrentInnings!);
        foreach (var innings in AllInnings.Where(innings => !ReferenceEquals(innings, CurrentInnings)))
            innings.OverLimitOverride ??= TotalOvers;
        Format = format; TotalOvers = overs;
        TotalInnings = format == CricketFormat.LimitedOvers && AllInnings.Count <= 2 ? 2 : 4;
        CurrentInnings!.OverLimitOverride = null; CurrentInnings.TargetOverride = null; CurrentInnings.AdjustmentNote = "";
        CurrentInnings.IgnoreOverLimit = CurrentInnings.IgnoreTarget = false;
        ReevaluateConditions(reopen);
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke(); return true;
    }

    public bool TryApplyRainAdjustment(int revisedOvers, int? revisedTarget, string expectedToken, out string error)
    {
        if (!ValidateConditionsEdit(expectedToken, out error)) return false;
        if (Format != CricketFormat.LimitedOvers || TossPending)
        { error = "Record the toss in a limited-overs match before entering a rain adjustment."; return false; }
        if (revisedOvers is < 1 or > 1000 || CurrentInnings!.LegalBallsBowled > revisedOvers * 6)
        { error = "Enter 1 to 1,000 overs, without reducing below the legal balls already bowled."; return false; }
        bool chase = CurrentInningsNumber > 1;
        if (chase ? revisedTarget is null or < 1 or > 1_000_000 : revisedTarget is not null)
        { error = chase ? "Enter the externally supplied target, from 1 to 1,000,000." : "A first-innings adjustment changes overs only; the chase target is set in its innings."; return false; }
        bool reopen = NaturallyCompleted(CurrentInnings!);
        CurrentInnings!.OverLimitOverride = revisedOvers; CurrentInnings.TargetOverride = revisedTarget;
        CurrentInnings.AdjustmentNote = "Externally supplied rain adjustment";
        CurrentInnings.IgnoreOverLimit = CurrentInnings.IgnoreTarget = false;
        ReevaluateConditions(reopen);
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke(); return true;
    }

    public bool TryClearRainAdjustment(string expectedToken, out string error)
    {
        if (!ValidateConditionsEdit(expectedToken, out error)) return false;
        if (Format == CricketFormat.LimitedOvers && CurrentInnings!.LegalBallsBowled > TotalOvers * 6)
        { error = "The original limit is below the balls already bowled. Change the match limit first."; return false; }
        bool reopen = NaturallyCompleted(CurrentInnings!);
        CurrentInnings!.OverLimitOverride = null; CurrentInnings.TargetOverride = null; CurrentInnings.AdjustmentNote = "";
        CurrentInnings.IgnoreOverLimit = CurrentInnings.IgnoreTarget = false;
        ReevaluateConditions(reopen);
        DeliveryRevision++; _conditionsVersion++; MatchChanged?.Invoke(); return true;
    }

    private bool ValidateConditionsEdit(string expectedToken, out string error)
    {
        error = "";
        if (expectedToken != MatchConditionsToken) { error = "The match changed while this panel was open. Reopen it before applying changes."; return false; }
        if (CurrentInnings is null || IsMatchFinalized) { error = "Change conditions in an open match, before finalizing its result."; return false; }
        return true;
    }
    private bool NaturallyCompleted(CricketInnings innings) => innings.IsComplete && !innings.IsDeclared && !innings.IsAllOut
        && (!innings.IgnoreOverLimit && Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled >= CurrentOverLimit * 6
            || !innings.IgnoreTarget && Target is int target && innings.TotalRuns >= target);
    private void ReevaluateConditions(bool reopen)
    {
        var innings = CurrentInnings!;
        innings.ConditionsDeliveryCount = innings.Deliveries.Count;
        if (reopen)
        {
            innings.IsComplete = false;
            innings.NeedsBowlerSelection = innings.CurrentBowler is null || innings.LegalBallsBowled > 0
                && innings.BallsInCurrentOver == 0 && (innings.Deliveries.LastOrDefault()?.OverNumber ?? -1) < innings.CompletedOvers;
        }
        CheckInningsComplete(innings);
        RefreshBatterSelectionState(innings);
        RepairBowlerSelection(innings);
    }
}
