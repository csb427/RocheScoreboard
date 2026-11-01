using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                bool totalBalances = draft.CalculatedTotal == draft.TotalRuns;
                string? answer = await ChooseAsync(totalBalances ? "Review related scorecard figures" : "Team total does not balance",
                    (totalBalances ? $"Batters ({draft.BattingRuns}) + extras ({draft.ExtrasRuns}) = team total ({draft.TotalRuns}). This total balances.\n\n" : "")
                    + string.Join("\n\n", warnings) + "\n\nGo back to correct the entry, or continue with exactly these figures. No other scores will be changed to balance them.",
                    new("go-back", "Go back", true), new("continue-anyway", "Continue anyway"), new("close", "Close"));
                if (answer != "continue-anyway") return false;
                allowDifference = true;
            }
            if (generation != _workflowGeneration || !ReferenceEquals(match, _match) || !ReferenceEquals(draft, _scorecardDraft)) return false;
            if (!match.TryApplyScorecard(draft, allowDifference, out string error)) { await ShowErrorAsync("Figure was not applied", error); return false; }
            // Model publication can replace this panel's match. Keep the
            // accepted correction, but never publish its UI into a new owner.
            if(generation!=_workflowGeneration || !ReferenceEquals(match,_match) || !ReferenceEquals(draft,_scorecardDraft))return false;
            _scorecardDirty = false;
            SetAppliedScorecardFeedback(match, draft.InningsNumber, "Change applied. Click another figure to edit.");
            RefreshScorecard(true); return generation==_workflowGeneration && ReferenceEquals(match,_match);
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
        if (match is null || draft is null || draft.InningsNumber < 1 || draft.InningsNumber > match.AllInnings.Count || !IsLoaded || !IsVisible) return;
        var owner=Window.GetWindow(this);if(owner is not {IsVisible:true})return;
        var innings = match.AllInnings[draft.InningsNumber - 1];
        var checkpoint=innings.ScorecardChanges.LastOrDefault();
        string token=match.WorkflowToken, pending=System.Text.Json.JsonSerializer.Serialize(draft);
        int generation=_workflowGeneration;
        bool Current()=>IsLoaded && IsVisible && owner.IsVisible && ReferenceEquals(owner,Window.GetWindow(this))
            && generation==_workflowGeneration && ReferenceEquals(match,_match) && token==match.WorkflowToken
            && ReferenceEquals(draft,_scorecardDraft) && pending==System.Text.Json.JsonSerializer.Serialize(draft)
            && draft.InningsNumber<=match.AllInnings.Count && ReferenceEquals(innings,match.AllInnings[draft.InningsNumber-1])
            && ReferenceEquals(checkpoint,innings.ScorecardChanges.LastOrDefault());
        async Task<bool> RequireCurrentAsync()
        {
            if(Current())return true;
            if(IsLoaded && IsVisible && owner.IsVisible && ReferenceEquals(match,_match))
                await ShowErrorAsync("Adjustment was not changed","The match, pending edits or adjustment checkpoint changed while this review was open. Reopen manual adjustments to review the current figures. Nothing was discarded or reverted.");
            return false;
        }
        bool canRevert = checkpoint is not null && innings.Deliveries.Count == checkpoint.AfterDeliveryCount;
        string latest = checkpoint?.Reason ?? "No applied adjustment checkpoint";
        string applied=$"Applied at opening: {innings.TotalRuns} runs · {innings.TotalWickets} wickets · {innings.OversDisplay} overs";
        string previous=checkpoint is null?"Only unapplied draft edits can be discarded here.":$"Before latest adjustment: {checkpoint.Before.TotalRuns} runs · {checkpoint.Before.TotalWickets} wickets";
        FrameworkElement RecordContext(bool reverting)
        {
            var body=new StackPanel();body.Children.Add(WorkflowText(reverting?"CHECKPOINT UNDER REVERT REVIEW":"ADJUSTMENTS AT OPENING",12));
            var heading=WorkflowText($"Innings {draft.InningsNumber} · {innings.BattingTeamName}",19);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
            body.Children.Add(WorkflowText(applied,15));body.Children.Add(WorkflowText(previous,15));
            body.Children.Add(WorkflowText(latest,13));
            if(_scorecardDirty)body.Children.Add(WorkflowText("Unapplied draft edits are also discarded if you revert.",14));
            var card=new Border{Tag=reverting?"scorecard-revert-context":"scorecard-adjustments-context",Child=body,CornerRadius=new CornerRadius(20),Padding=new Thickness(12,8,12,12),BorderThickness=new Thickness(1)};
            card.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");card.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");return card;
        }
        string message = string.IsNullOrWhiteSpace(innings.ManualScorecardWarning)
            ? checkpoint is null ? "No applied adjustment is available to revert. Pending edits have not changed the recorded figures." : "Original deliveries remain protected by the adjustment checkpoint."
            : innings.ManualScorecardWarning;
        if (checkpoint is not null && !canRevert) message += "\n\nA checkpoint can only be reverted after undoing deliveries recorded since that adjustment.";
        var choices = new List<CricketPrompt.Choice> { new("close", "Keep current figures"),new("keep","Keep current figures",true,Detail:"Return to the scorecard without discarding or reverting anything.") };
        if (_scorecardDirty) choices.Add(new("discard-pending", "Discard pending edits and reload",false,true,Detail:"Reload the applied figures. Recorded scores and adjustment history stay unchanged."));
        if (canRevert) choices.Add(new("revert", "Review reverting the latest adjustment",Detail:"Review the saved figures before this adjustment. A separate confirmation is required."));
        string? answer = await ContextDecisionAsync("Manual scorecard adjustments", message,RecordContext(false), choices.ToArray());
        if(answer is not ("discard-pending" or "revert"))return;
        if(!await RequireCurrentAsync())return;
        if (answer == "discard-pending") { _scorecardDirty = false; RefreshScorecard(true); return; }
        if (await ContextDecisionAsync("Revert this adjustment?", "Restore the figures shown before the latest adjustment. Original deliveries stay intact. Any unapplied draft edits are discarded.",RecordContext(true),
            new("close","Keep current figures"),new("keep","Keep current figures",true,Detail:"Return without restoring or discarding anything."),
            new("yes","Revert latest adjustment",false,true,Detail:"Restore this checkpoint and remove only the latest adjustment from the applied history.")) != "yes") return;
        if(!await RequireCurrentAsync())return;
        if (!match.TryRevertLatestScorecardCorrection(draft.InningsNumber, out string error)) { await ShowErrorAsync("Unable to revert", error); return; }
        // The accepted revert publishes model events. Its previous owner may
        // have been replaced while those subscribers were running.
        if(generation!=_workflowGeneration || !ReferenceEquals(match,_match) || !IsLoaded || !IsVisible
            || !owner.IsVisible || !ReferenceEquals(owner,Window.GetWindow(this)))return;
        _scorecardDirty = false; RefreshScorecard(true);
    }
}
