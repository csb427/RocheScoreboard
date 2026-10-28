namespace Roche_Scoreboard.Models;

public sealed partial class CricketMatchManager
{
    public bool TryReplaceBatter(bool striker, int replacementIndex, CricketBatterReplacementReason reason, bool carryScores, out string error)
    {
        error = ""; LastOperationError = "";
        var innings = CurrentInnings;
        if (TossPending || IsMatchFinalized || innings is null || innings.IsComplete || !Enum.IsDefined(reason)
            || replacementIndex < 0 || replacementIndex >= innings.BattingOrder.Count)
        { error = LastOperationError = "Choose a replacement in an active innings."; return false; }
        int oldIndex = striker ? innings.StrikerIndex : innings.NonStrikerIndex;
        int otherIndex = striker ? innings.NonStrikerIndex : innings.StrikerIndex;
        if (oldIndex < 0 || replacementIndex == oldIndex || replacementIndex == otherIndex || innings.BattingOrder[replacementIndex].IsOut)
        { error = LastOperationError = "Choose an eligible player who is not already at the crease."; return false; }
        var previous = innings.BattingOrder[oldIndex]; var incoming = innings.BattingOrder[replacementIndex];
        if ((incoming.Runs > 0 || incoming.BallsFaced > 0) && (!incoming.IsRetired || reason == CricketBatterReplacementReason.WrongPlayer))
        { error = LastOperationError = "That player already has a batting record. Select an unbatted replacement, or return a retired not-out batter."; return false; }
        if (reason == CricketBatterReplacementReason.WrongPlayer && carryScores)
        {
            if (incoming.Runs > 0 || incoming.BallsFaced > 0)
            { error = LastOperationError = "Scores cannot be transferred onto a player who already batted. Use the scorecard to reconcile those records."; return false; }
            int Map(int index) => index == oldIndex ? replacementIndex : index == replacementIndex ? oldIndex : index;
            void SwapStatistics(List<CricketPlayer> players)
            {
                if (oldIndex >= players.Count || replacementIndex >= players.Count) return;
                var a = players[oldIndex].Clone(); var b = players[replacementIndex].Clone();
                CopyBatting(b, players[oldIndex]); CopyBatting(a, players[replacementIndex]);
            }
            CricketInningsState Remap(CricketInningsState snapshot)
            {
                var copy = new CricketInnings(); RestoreState(copy, snapshot); SwapStatistics(copy.BattingOrder);
                copy.StrikerIndex = Map(copy.StrikerIndex); copy.NonStrikerIndex = Map(copy.NonStrikerIndex);
                return CaptureState(copy);
            }
            SwapStatistics(innings.BattingOrder);
            innings.StrikerIndex = Map(innings.StrikerIndex); innings.NonStrikerIndex = Map(innings.NonStrikerIndex);
            for (int i = 0; i < innings.Deliveries.Count; i++)
            {
                var delivery = innings.Deliveries[i]; int actor = Map(delivery.StrikerIndex);
                innings.Deliveries[i] = CopyDelivery(delivery, delivery.StateBefore is null ? null : Remap(delivery.StateBefore), Map,
                    actor >= 0 && actor < innings.BattingOrder.Count ? innings.BattingOrder[actor].DisplayName : null);
            }
            for (int i = 0; i < innings.ScorecardChanges.Count; i++)
                innings.ScorecardChanges[i] = innings.ScorecardChanges[i] with { Before = Remap(innings.ScorecardChanges[i].Before) };
        }
        else
        {
            // This is an explicit non-ball operation. Keep the completed balls
            // intact and retain a reversible checkpoint instead of inventing a
            // wicket delivery or throwing away the outgoing batter's runs.
            var before = CaptureState(innings);
            previous.IsRetired = true;
            previous.RetiredReason = reason switch
            {
                CricketBatterReplacementReason.RetiredHurt => "retired hurt",
                CricketBatterReplacementReason.RetiredOut => "retired out",
                CricketBatterReplacementReason.WrongPlayer => "replaced · score retained",
                _ => "retired not out"
            };
            previous.IsOut = reason == CricketBatterReplacementReason.RetiredOut;
            previous.HowOut = previous.IsOut ? DismissalType.RetiredOut : DismissalType.RetiredHurt;
            previous.DismissalBowler = previous.DismissalFielder = null;
            if (previous.IsOut) innings.TotalWickets++;
            incoming.IsRetired = false; incoming.RetiredReason = null;
            if (striker) innings.StrikerIndex = replacementIndex; else innings.NonStrikerIndex = replacementIndex;
            MarkBattingEntry(innings,replacementIndex);
            innings.PartnershipRuns = innings.PartnershipBalls = 0;
            innings.ScorecardChanges.Add(new(innings.Deliveries.Count, before, "Batter replacement"));
            innings.ScorecardAnchor = innings.Deliveries.Count;
        }
        RefreshBatterSelectionState(innings); CheckInningsComplete(innings);
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
}
