using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private async Task OpenUndeliveredRunOutAsync(int? expectedNonStriker = null)
    {
        var match = _match; var innings = match?.CurrentInnings;
        if (match is null || innings is null) return;
        if (!match.CanRecordNonDeliveryRunOut || expectedNonStriker is int expected && expected != innings.NonStrikerIndex)
        { await ShowErrorAsync("Run out before delivery", "Select both batters and the bowler, resume any break, and reopen this action for the current non-striker."); return; }
        string token = match.WorkflowToken;
        string message = $"Record the umpire's Out decision for {innings.NonStriker!.FullName}. The ball was not delivered.\n\n"
            + "One wicket is added. Runs, ball counts, strike, bowler wickets and any pending free hit stay unchanged. The incoming batter takes the non-striker's end.\n\n"
            + "Undo reverses this action before earlier deliveries can be changed.";
        if (await ChooseAsync("Non-striker run out before delivery", message,
            new("cancel", "Cancel"), new("record-before-delivery", "Record run-out", true)) != "record-before-delivery"
            || !ReferenceEquals(match, _match)) return;
        if (!match.TryRecordNonStrikerRunOutBeforeDelivery(token, out string error)) await ShowErrorAsync("Run-out was not recorded", error);
    }

    private async Task OpenMatchOutcomeAsync()
    {
        var match = _match;
        if (match?.CurrentInnings is null) return;
        string token = match.WorkflowToken;
        if (match.IsMatchFinalized)
        {
            string summary = (match.MatchResult ?? "Match ended") + (string.IsNullOrWhiteSpace(match.ResultNote) ? "" : "\n\n" + match.ResultNote);
            var choices = new List<CricketPrompt.Choice> { new("close", "Close") };
            if (match.ClosureBefore is not null) choices.Add(new("reopen", "Reopen match"));
            else summary += "\n\nThis older save has no reopening checkpoint. Figures remain available in the scorecard.";
            if (await ChooseAsync("Recorded outcome", summary, choices.ToArray()) != "reopen" || !ReferenceEquals(match, _match)) return;
            if (await ChooseAsync("Reopen this match?", "The recorded outcome will be cleared. Corrected figures are retained. The previous break and selection state are restored; a completed innings still needs the next-innings decision.",
                new("cancel", "Keep outcome"), new("confirm-reopen", "Reopen match", true)) != "confirm-reopen" || !ReferenceEquals(match, _match)) return;
            if (!match.TryReopenMatch(token, out string error)) await ShowErrorAsync("Match was not reopened", error);
            else ResumePendingSelection();
            return;
        }
        var body = new StackPanel();
        body.Children.Add(WorkflowText("Scoring will stop; scores and history stay saved for review or reopening. Use the outcome supplied by officials or the competition. No minimum-overs rule, tie-break or DLS result is inferred.", 14));
        body.Children.Add(WorkflowText("Result note (optional)", 14));
        var note = WorkflowInput(""); note.Tag = "outcome-note"; note.MaxLength = 500;
        note.AcceptsReturn = true; note.TextWrapping = TextWrapping.Wrap; note.MinHeight = 70;
        System.Windows.Automation.AutomationProperties.SetName(note, "Result note, optional, up to 500 characters");
        body.Children.Add(note);
        var actions = new List<CricketPrompt.Choice> { new("cancel", "Keep match open") };
        if (!match.TossPending)
            actions.Add(new("score-result", match.MatchResult is { } result ? "Record result: " + result : "End without a recorded result", match.MatchResult is not null));
        actions.Add(new("abandoned", "Match abandoned"));
        actions.Add(new("no-result", "No result — official decision"));
        string? answer = await FormAsync("End match / record outcome",
            "", body, actions.ToArray());
        if (!ReferenceEquals(match, _match) || answer is null or "cancel") return;
        var kind = answer switch { "abandoned" => CricketMatchResultKind.Abandoned, "no-result" => CricketMatchResultKind.NoResult, _ => CricketMatchResultKind.ScoreBased };
        if (!match.TrySetMatchOutcome(kind, note.Text ?? "", token, out string problem)) await ShowErrorAsync("Outcome was not recorded", problem);
    }

    private void RenderNonDeliveryHistory(CricketInnings? innings)
    {
        if (innings is null) return;
        var changes = innings.ScorecardChanges.Where(change => change.NonDeliveryWicket is not null).Reverse().ToArray();
        if (changes.Length == 0) return;
        var history = new StackPanel { Tag = "non-delivery-history", Margin = new Thickness(0, 0, 0, 12) };
        history.Children.Add(WorkflowText("Actions before delivery", 15));
        foreach (var change in changes)
        {
            var wicket = change.NonDeliveryWicket!;
            string name = wicket.BatterIndex >= 0 && wicket.BatterIndex < change.Before.BattingOrder.Count
                ? change.Before.BattingOrder[wicket.BatterIndex].FullName : "Non-striker";
            history.Children.Add(WorkflowText($"{name} · run out before delivery\n{change.Before.TotalWickets + 1}/{change.Before.TotalRuns} at {change.Before.LegalBallsBowled / 6}.{change.Before.LegalBallsBowled % 6} overs · no ball counted", 14));
        }
        history.Children.Add(WorkflowText("Undo reverses the latest action after any following deliveries. The scorecard keeps the adjustment checkpoint.", 13));
        EventLog.Children.Insert(0, history);
    }
}
