// Generated from Views/CricketControlPanel.Conditions.cs; edit translator/adapters.
// Source SHA-256: 3c56ff4738a2c56f26b3b2af8641132058eda3cef21dc48cacb94fa47c8f3eba
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Button = Avalonia.Controls.Button;
using TextBlock = Avalonia.Controls.TextBlock;
using Brushes = Avalonia.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    /// <returns>True when the operator chooses the header Back arrow.</returns>
    private async Task<bool> OpenConditionsEditorAsync(bool rainRevision)
    {
        var match = _match; var innings = match?.CurrentInnings;
        if (match is null || innings is null) return false;
        string token = match.MatchConditionsToken;
        bool chase = innings.InningsNumber > 1;
        var body = new StackPanel();
        var context = WorkflowText($"{innings.BattingTeamName} · {innings.TotalWickets}/{innings.TotalRuns} after {innings.OversDisplay} overs", 18); body.Children.Add(context);
        body.Children.Add(WorkflowText(rainRevision
            ? chase ? "Chasing innings · enter the revised total overs and target supplied by the umpires or competition. The app does not calculate DLS."
                : "First innings · enter the revised total overs supplied by the umpires or competition. Set a revised target during the chase, if required."
            : "Change the match format and innings over limit. Completed innings records stay intact.", 14));
        CricketFormat selectedFormat = match.Format;
        if(selectedFormat==CricketFormat.MultiDay)selectedFormat=CricketFormat.MultiInnings;
        var formats = new WrapPanel();
        var oversSection = new StackPanel();
        if (!rainRevision)
        {
            body.Children.Add(WorkflowText("MATCH FORMAT", 12)); body.Children.Add(formats);
            foreach (var (format, caption) in new[] { (CricketFormat.LimitedOvers, "Limited overs"), (CricketFormat.MultiInnings, "Multi-innings") })
            {
                var value = format; var button = WorkflowButton(caption, () => { selectedFormat = value; RefreshFormatSelection(); }, "conditions-format-" + format); button.MinWidth = 145; formats.Children.Add(button);
            }
        }
        oversSection.Children.Add(WorkflowText(rainRevision ? "REVISED TOTAL OVERS" : "TOTAL OVERS PER INNINGS", 12));
        var overs = WorkflowInput((rainRevision ? match.CurrentOverLimit : match.TotalOvers).ToString()); overs.Tag = "conditions-overs"; oversSection.Children.Add(overs);
        var presets = new WrapPanel(); oversSection.Children.Add(presets);
        foreach (int amount in new[] { 20, 40, 50 }) { int value = amount; presets.Children.Add(WorkflowButton(amount.ToString(), () => overs.Text = value.ToString(), "conditions-overs-" + amount)); }
        body.Children.Add(oversSection);
        var target = WorkflowInput((match.Target ?? 1).ToString()); target.Tag = "conditions-target";
        if (rainRevision && chase)
        {
            body.Children.Add(WorkflowText("REVISED TARGET · TOTAL RUNS TO WIN", 12)); body.Children.Add(target);
            body.Children.Add(WorkflowText($"Current target: {match.Target?.ToString() ?? "not set"} · this changes the target, not the runs already scored.", 13));
        }
        else if (rainRevision) body.Children.Add(WorkflowText("No chase target is entered in the first innings.", 13));
        if (!rainRevision && match.HasRainAdjustment) body.Children.Add(WorkflowText("Applying general match conditions removes the current rain revision.", 14));
        var error = WorkflowText("", 14); error.Foreground = Brushes.LightSalmon; body.Children.Add(error);
        void RefreshFormatSelection()
        {
            foreach (var button in formats.Children.OfType<Button>())
            {
                bool active = Equals(button.Tag, "conditions-format-" + selectedFormat);
                button.BorderBrush = active ? Brushes.LightSkyBlue : Brushes.SlateGray; button.BorderThickness = new Thickness(active ? 2 : 1);
                button.Opacity = active ? 1 : .7; Avalonia.Automation.AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
            }
            oversSection.IsVisible = rainRevision || selectedFormat == CricketFormat.LimitedOvers ? true : false;
        }
        RefreshFormatSelection();
        var choices = new List<CricketPrompt.Choice> { new("back", "Back to match options"), new("close", "Close"), new("apply", rainRevision ? "Apply revised conditions" : "Apply match conditions", true) };
        if (rainRevision && match.HasRainAdjustment) choices.Add(new("clear", "Restore original overs and target"));
        while (ReferenceEquals(match, _match))
        {
            string? answer = await FormAsync(rainRevision ? "Duckworth-Lewis" : "Format and over limit", "", body, choices.ToArray());
            if (answer == "back") return true;
            if (answer is not ("apply" or "clear")) return false;
            if (!ReferenceEquals(innings, match.CurrentInnings) || token != match.MatchConditionsToken)
            { error.Text = "The match changed while this panel was open. Go back and reopen it before applying changes."; continue; }
            if (answer == "clear")
            {
                string? confirm = await ChooseAsync("Restore original conditions?", "This removes the current rain revision and restores the normal over limit and calculated chase target.", new("back", "Back"), new("close", "Close"), new("yes", "Restore original conditions", true));
                if (confirm == "back") continue;
                if (confirm != "yes") return false;
                if (match.TryClearRainAdjustment(token, out string restoreError)) return false;
                error.Text = restoreError; continue;
            }
            int limit=Math.Clamp(match.TotalOvers,1,1000);
            if ((rainRevision || selectedFormat==CricketFormat.LimitedOvers) && (!int.TryParse((overs.Text ?? string.Empty), out limit) || limit is < 1 or > 1000))
            { error.Text = "Enter a total over limit from 1 to 1,000."; continue; }
            int? revisedTarget = null;
            if (rainRevision && chase)
            {
                if (!int.TryParse((target.Text ?? string.Empty), out int parsedTarget) || parsedTarget is < 1 or > 1_000_000) { error.Text = "Enter the supplied target from 1 to 1,000,000."; continue; }
                revisedTarget = parsedTarget;
            }
            string applyError;
            bool applied = rainRevision ? match.TryApplyRainAdjustment(limit, revisedTarget, token, out applyError)
                : match.TryUpdateMatchConditions(selectedFormat, limit, token, out applyError);
            if (applied) return false;
            error.Text = applyError;
        }
        return false;
    }
}
