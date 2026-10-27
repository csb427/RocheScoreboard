using System.Windows;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private bool _scorecardCommitBusy;
    private async Task<bool> CommitScorecardEditAsync(CricketScorecardDraft draft)
    {
        var match = _match; int generation = _workflowGeneration;
        if (match is null || !ReferenceEquals(draft, _scorecardDraft) || _scorecardCommitBusy) return false;
        _scorecardCommitBusy = true; _scorecardDirty = true;
        try
        {
            var errors = match.ValidateScorecardStructure(draft);
            if (errors.Count > 0) { await ShowErrorAsync("Unable to apply this figure", string.Join("\n\n", errors)); return false; }
            var current = match.CreateScorecardDraft(draft.InningsNumber);
            if (SameScorecardFigures(draft, current)) { _scorecardDirty = false; SetAppliedScorecardFeedback(match, draft.InningsNumber, "Figure is already up to date. Click another figure to edit."); RefreshScorecard(true); return true; }
            var warnings = match.GetScorecardArithmeticWarnings(draft);
            bool allowDifference = false;
            if (warnings.Count > 0)
            {
                string? answer = await ChooseAsync("Figures do not add up", string.Join("\n\n", warnings) + "\n\nGo back to correct the entry, or continue with exactly these figures. No other scores will be changed to balance them.",
                    new("go-back", "Go back", true), new("continue-anyway", "Continue anyway"), new("close", "Close"));
                if (answer != "continue-anyway") return false;
                allowDifference = true;
            }
            if (generation != _workflowGeneration || !ReferenceEquals(match, _match) || !ReferenceEquals(draft, _scorecardDraft)) return false;
            if (!match.TryApplyScorecard(draft, allowDifference, out string error)) { await ShowErrorAsync("Figure was not applied", error); return false; }
            _scorecardDirty = false;
            SetAppliedScorecardFeedback(match, draft.InningsNumber, "Change applied. Click another figure to edit.");
            RefreshScorecard(true); return true;
        }
        finally { if (generation == _workflowGeneration) _scorecardCommitBusy = false; }
    }
    private void SetAppliedScorecardFeedback(CricketMatchManager match, int inningsNumber, string message)
        => _scorecardAppliedFeedback = (inningsNumber, match.DeliveryRevision, match.AllInnings.Sum(innings => innings.Deliveries.Count), message);

    private static bool SameScorecardFigures(CricketScorecardDraft first, CricketScorecardDraft second)
    {
        bool Batting(CricketPlayer a, CricketPlayer b) => a.Runs == b.Runs && a.BallsFaced == b.BallsFaced && a.Fours == b.Fours && a.Sixes == b.Sixes && a.IsOut == b.IsOut && a.HowOut == b.HowOut
            && a.IsRetired == b.IsRetired && a.RetiredReason == b.RetiredReason && a.DismissalText == b.DismissalText && a.DismissalBowler == b.DismissalBowler && a.DismissalFielder == b.DismissalFielder;
        bool Bowling(CricketPlayer a, CricketPlayer b) => a.BallsBowled == b.BallsBowled && a.RunsConceded == b.RunsConceded && a.Wickets == b.Wickets && a.Maidens == b.Maidens && a.Wides == b.Wides && a.NoBalls == b.NoBalls;
        return first.TotalRuns == second.TotalRuns && first.TotalWickets == second.TotalWickets && first.LegalBallsBowled == second.LegalBallsBowled && first.Wides == second.Wides && first.NoBalls == second.NoBalls && first.Byes == second.Byes && first.LegByes == second.LegByes
            && first.Batters.Zip(second.Batters).All(pair => Batting(pair.First, pair.Second)) && first.Bowlers.Zip(second.Bowlers).All(pair => Bowling(pair.First, pair.Second)) && first.FallOfWickets.SequenceEqual(second.FallOfWickets);
    }

    private async Task OpenScorecardRecoveryAsync()
    {
        var match = _match; var draft = _scorecardDraft;
        if (match is null || draft is null) return;
        var innings = match.AllInnings[draft.InningsNumber - 1];
        bool canRevert = innings.ScorecardChanges.Count > 0 && innings.Deliveries.Count == innings.ScorecardChanges[^1].AfterDeliveryCount;
        string message = string.IsNullOrWhiteSpace(innings.ManualScorecardWarning) ? "Manual figures are saved. Original deliveries remain protected by their adjustment checkpoint." : innings.ManualScorecardWarning;
        if (!canRevert) message += "\n\nUndo deliveries added after the latest adjustment before reverting it.";
        var choices = new List<CricketPrompt.Choice> { new("close", "Close") };
        if (_scorecardDirty) choices.Add(new("discard-pending", "Discard pending row and reload current figures"));
        if (canRevert) choices.Add(new("revert", "Revert the latest adjustment"));
        string? answer = await ChooseAsync("Manual scorecard adjustments", message, choices.ToArray());
        if (!ReferenceEquals(match, _match)) return;
        if (answer == "discard-pending") { _scorecardDirty = false; RefreshScorecard(true); return; }
        if (answer != "revert") return;
        if (await ChooseAsync("Revert this adjustment?", "This restores the figures immediately before the latest adjustment. Original deliveries stay intact.", new("close", "Close"), new("yes", "Revert adjustment", true)) != "yes") return;
        if (!ReferenceEquals(match, _match)) return;
        if (!match.TryRevertLatestScorecardCorrection(draft.InningsNumber, out string error)) { await ShowErrorAsync("Unable to revert", error); return; }
        _scorecardDirty = false; RefreshScorecard(true);
    }
}
