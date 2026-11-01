// Generated from Views/CricketControlPanel.ExceptionalEvents.cs; edit translator/adapters.
// Source SHA-256: bf78946b86da1420b15f2cbedb9089f57e8543969cd44b4a94dcf83287ace250
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = Avalonia.Controls.Button;
using TextBlock = Avalonia.Controls.TextBlock;

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
        var victim = innings.NonStriker!;
        var identity = new StackPanel(); identity.Children.Add(WorkflowText("NON-STRIKER AT OPENING",12));
        identity.Children.Add(WorkflowText(victim.FullName,19));
        identity.Children.Add(WorkflowText("The ball has not been delivered.",13));
        var contextRow = new Grid(); contextRow.ColumnDefinitions.Add(new ColumnDefinition()); contextRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); contextRow.Children.Add(identity);
        var figures = new StackPanel { MinWidth = 100, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var number = WorkflowText($"{victim.Runs} ({victim.BallsFaced})",22); number.TextAlignment = TextAlignment.Right;
        number.FontFeatures=new FontFeatureCollection { FontFeature.Parse("tnum"), FontFeature.Parse("lnum"), FontFeature.Parse("-kern") };
        Avalonia.Controls.ToolTip.SetTip(number, $"{victim.FullName}: {victim.Runs} runs, {victim.BallsFaced} balls faced");
        figures.Children.Add(number); var figureLabel = WorkflowText("runs (balls faced)",12); figureLabel.TextAlignment = TextAlignment.Right; figures.Children.Add(figureLabel);
        Grid.SetColumn(figures,1); contextRow.Children.Add(figures);
        var context = new NativeBorder { Tag = "undelivered-runout-context", Child = contextRow, CornerRadius = new CornerRadius(20), Padding = new Thickness(12,6,12,10), BorderThickness = new Thickness(1) };
        context.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); context.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
        if (await ContextDecisionAsync("Non-striker run out before delivery", message, context,
            new("cancel", "Cancel"), new("record-before-delivery", "Record run-out", true,
                Detail: $"For {victim.DisplayName}: adds one wicket. Runs, ball counts, strike, bowler wickets and any pending free hit stay unchanged. The incoming batter takes the non-striker's end. Undo reverses this action.")) != "record-before-delivery"
            || !ReferenceEquals(match, _match)) return;
        if (!match.TryRecordNonStrikerRunOutBeforeDelivery(token, out string error)) await ShowErrorAsync("Run-out was not recorded", error);
    }

    private async Task<string?> ContextDecisionAsync(string title,string message,Control context,params CricketPrompt.Choice[] choices)
    {
        if (_workflowBusy) return null;
        _workflowBusy = true; int generation = _workflowGeneration;
        var source = NativeKeyboard.FocusedElement as Control;
        if (source is not { IsLoaded:true, IsVisible:true } || !CricketRoot.IsAncestorOf(source)) source = MatchOptionsButton;
        try
        {
            var answer = await CricketPrompt.ChooseWithContextAsync(CricketRoot,title,message,context,source,choices);
            return generation == _workflowGeneration ? answer : null;
        }
        finally { ReleaseWorkflow(generation); }
    }

    private async Task OpenMatchOutcomeAsync()
    {
        var match = _match;
        if (match?.CurrentInnings is null) return;
        string token = match.WorkflowToken;
        if (match.IsMatchFinalized)
        {
            string recordedOutcome = match.MatchResult ?? "Match ended"; string recordedNote = match.ResultNote ?? "";
            string summary = recordedOutcome + (string.IsNullOrWhiteSpace(recordedNote) ? "" : "\n\n" + recordedNote);
            var choices = new List<CricketPrompt.Choice> { new("close", "Close") };
            if (match.ClosureBefore is not null) choices.Add(new("reopen", "Reopen match"));
            else summary += "\n\nThis older save has no reopening checkpoint. Figures remain available in the scorecard.";
            if (await ChooseAsync("Recorded outcome", summary, choices.ToArray()) != "reopen" || !ReferenceEquals(match, _match)) return;
            var recorded = new StackPanel(); recorded.Children.Add(WorkflowText("RECORDED OUTCOME UNDER REVIEW",12));
            recorded.Children.Add(WorkflowText(recordedOutcome,22));
            if(!string.IsNullOrWhiteSpace(recordedNote)) recorded.Children.Add(WorkflowText(recordedNote,14));
            var context = new NativeBorder { Tag = "reopen-outcome-context", Child = recorded, CornerRadius = new CornerRadius(20), Padding = new Thickness(12,6,12,12), BorderThickness = new Thickness(1) };
            context.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); context.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
            if (await ContextDecisionAsync("Reopen this match?", "The recorded outcome will be cleared. Corrected figures are retained. The previous break and selection state are restored; a completed innings still needs the next-innings decision.", context,
                new("cancel", "Keep outcome"), new("confirm-reopen", "Reopen match", true,
                    Detail: "Clears this recorded outcome and keeps corrected figures. Restores the previous break and selection state; a completed innings still needs the next-innings decision.")) != "confirm-reopen" || !ReferenceEquals(match, _match)) return;
            if (!match.TryReopenMatch(token, out string error)) await ShowErrorAsync("Match was not reopened", error);
            else ResumePendingSelection();
            return;
        }
        var body = new StackPanel { Tag = "match-outcome-review", Margin = new Thickness(0,0,12,0) };
        var innings = match.CurrentInnings;
        var record = new Grid { IsHitTestVisible = false };
        record.ColumnDefinitions.Add(new ColumnDefinition()); record.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        var identity = new StackPanel();
        identity.Children.Add(WorkflowText("AT OPENING",12));
        identity.Children.Add(WorkflowText(innings.BattingTeamName,18));
        identity.Children.Add(WorkflowText($"Innings {innings.InningsNumber} · {innings.OversDisplay} overs",13)); record.Children.Add(identity);
        var score = WorkflowText($"{innings.TotalWickets}/{innings.TotalRuns}",28); score.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; score.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        score.FontFeatures=new FontFeatureCollection { FontFeature.Parse("tnum"), FontFeature.Parse("lnum"), FontFeature.Parse("-kern") };
        Avalonia.Controls.ToolTip.SetTip(score, $"{innings.TotalRuns} runs, {innings.TotalWickets} wickets");
        Avalonia.Automation.AutomationProperties.SetName(score,$"{innings.TotalRuns} runs, {innings.TotalWickets} wickets");
        Grid.SetColumn(score,1); record.Children.Add(score);
        var recordCard = new NativeBorder { Child = record, Tag = "outcome-opening-record", Padding = new Thickness(12,4,12,8), Margin = new Thickness(3,0,3,12), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1) };
        recordCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); recordCard.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush"); body.Children.Add(recordCard);
        body.Children.Add(WorkflowText("Scoring will stop; scores and history stay saved for review or reopening. Use the outcome supplied by officials or the competition. No minimum-overs rule, tie-break or DLS result is inferred.", 14));
        var note = WorkflowInput(""); note.Tag = "outcome-note"; note.MaxLength = 500;
        note.AcceptsReturn = true; note.TextWrapping = TextWrapping.Wrap; note.MinHeight = 70;
        Avalonia.Automation.AutomationProperties.SetName(note, "Result note, optional, up to 500 characters");
        var noteFields = new StackPanel();
        var noteLabel = new Avalonia.Controls.Label { Content = "Result _note · optional", Target = note, FontSize = 14, Padding = new Thickness(3,4,3,6) };
        noteLabel.SetResourceReference(Avalonia.Controls.TextBlock.ForegroundProperty,"ControlCentre.TextBrush"); noteFields.Children.Add(noteLabel); noteFields.Children.Add(note);
        var noteCard = new NativeBorder { Child = noteFields, Padding = new Thickness(10,6,10,10), Margin = new Thickness(3,8,3,8), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        noteCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); noteCard.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush"); body.Children.Add(noteCard);
        var actions = new List<CricketPrompt.Choice> { new("cancel", "Keep match open") };
        if (!match.TossPending)
            actions.Add(new("score-result", match.MatchResult is { } result ? "Record result: " + result : "End without a recorded result", match.MatchResult is not null,
                Detail: match.MatchResult is not null ? "Record the available score-based result and stop scoring. A reopening checkpoint is kept." : "Stop scoring without assigning a result. Figures stay available for review or reopening."));
        actions.Add(new("abandoned", "Match abandoned", Detail: "Record abandonment as the outcome and stop scoring. Figures and history are kept."));
        actions.Add(new("no-result", "No result — official decision", Detail: "Record the officials' No result decision. No minimum-overs or DLS outcome is calculated."));
        string? answer = await FormAsync("End match / record outcome",
            "", body, actions.ToArray());
        if (!ReferenceEquals(match, _match) || answer is null or "cancel") return;
        var kind = answer switch { "abandoned" => CricketMatchResultKind.Abandoned, "no-result" => CricketMatchResultKind.NoResult, _ => CricketMatchResultKind.ScoreBased };
        if (!match.TrySetMatchOutcome(kind, (note.Text ?? string.Empty) ?? "", token, out string problem)) await ShowErrorAsync("Outcome was not recorded", problem);
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
