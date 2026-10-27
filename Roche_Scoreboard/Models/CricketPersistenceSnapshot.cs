namespace Roche_Scoreboard.Models;

public sealed partial class CricketMatchManager
{
    /// <summary>
    /// Capture on the owning UI thread. Mutable innings, players and collection
    /// containers are detached before a background recovery writer sees them.
    /// Deliveries are init-only records; their StateBefore snapshots are never
    /// mutated by the engine (undo clones them, correction replaces deliveries).
    /// Reusing that immutable history avoids cloning 22 players for every old ball.
    /// </summary>
    internal CricketSerializableState CapturePersistenceState()
    {
        var snapshot = ToState();
        snapshot.AllInnings = AllInnings.Select(innings => new CricketInnings
        {
            InningsNumber = innings.InningsNumber,
            BattingTeamName = innings.BattingTeamName,
            BowlingTeamName = innings.BowlingTeamName,
            BattingOrder = innings.BattingOrder.Select(player => player.Clone()).ToList(),
            BowlingAttack = innings.BowlingAttack.Select(player => player.Clone()).ToList(),
            TotalRuns = innings.TotalRuns,
            TotalWickets = innings.TotalWickets,
            TotalExtras = innings.TotalExtras,
            Wides = innings.Wides,
            NoBalls = innings.NoBalls,
            Byes = innings.Byes,
            LegByes = innings.LegByes,
            LegalBallsBowled = innings.LegalBallsBowled,
            CurrentOverBalls = [.. innings.CurrentOverBalls],
            StrikerIndex = innings.StrikerIndex,
            NonStrikerIndex = innings.NonStrikerIndex,
            CurrentBowlerIndex = innings.CurrentBowlerIndex,
            PreviousBowlerIndex = innings.PreviousBowlerIndex,
            PartnershipRuns = innings.PartnershipRuns,
            PartnershipBalls = innings.PartnershipBalls,
            IsDeclared = innings.IsDeclared,
            IsComplete = innings.IsComplete,
            OverLimitOverride = innings.OverLimitOverride,
            TargetOverride = innings.TargetOverride,
            AdjustmentNote = innings.AdjustmentNote,
            ConditionsDeliveryCount = innings.ConditionsDeliveryCount,
            IgnoreTarget=innings.IgnoreTarget,
            IgnoreOverLimit=innings.IgnoreOverLimit,
            ScorecardAnchor=innings.ScorecardAnchor,
            ScorecardChanges=[.. innings.ScorecardChanges],
            ManualFallOfWickets=innings.ManualFallOfWickets is null ? null : [.. innings.ManualFallOfWickets],
            NeedsBatterSelection = innings.NeedsBatterSelection,
            NeedsBowlerSelection = innings.NeedsBowlerSelection,
            FreeHitPending = innings.FreeHitPending,
            ManualScorecardWarning = innings.ManualScorecardWarning,
            Deliveries = [.. innings.Deliveries]
        }).ToList();
        return snapshot;
    }
}
