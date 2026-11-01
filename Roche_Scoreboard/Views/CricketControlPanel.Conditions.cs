using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;

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
        var body = new StackPanel { Tag = rainRevision ? "revised-conditions-editor" : "match-format-editor" };
        var context = WorkflowText($"{innings.BattingTeamName} · {innings.TotalWickets}/{innings.TotalRuns} after {innings.OversDisplay} overs", 18);
        if (rainRevision)
        {
            body.Margin = new Thickness(0,0,12,0); context.Text = "At opening · " + context.Text; context.FontSize = 16;
            var record = new StackPanel(); record.Children.Add(context);
            record.Children.Add(WorkflowText($"Over limit: {match.CurrentOverLimit}" + (chase ? $" · Target: {match.Target?.ToString() ?? "not set"}" : ""),13));
            var recordCard = new Border { Tag = "revised-conditions-context", Child = record, Padding = new Thickness(12,4,12,8), Margin = new Thickness(3,0,3,12), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1) };
            recordCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); recordCard.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush"); body.Children.Add(recordCard);
        }
        else
        {
            body.Margin = new Thickness(0,0,12,0);
            var record = new Border { Child = context, CornerRadius = new CornerRadius(16), Padding = new Thickness(12,4,12,8), Margin = new Thickness(3,0,3,8), BorderThickness = new Thickness(1) };
            record.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); record.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush"); body.Children.Add(record);
        }
        var proposed = WorkflowText("",15); proposed.Tag = "conditions-proposed";
        var formatIcons = new Dictionary<Button, OperatorSymbol>();
        body.Children.Add(WorkflowText(rainRevision
            ? chase ? "Chasing innings · enter the revised total overs and target supplied by the umpires or competition. The app does not calculate DLS."
                : "First innings · enter the revised total overs supplied by the umpires or competition. Set a revised target during the chase, if required."
            : "Change the match format and innings over limit. Completed innings records stay intact.", 14));
        CricketFormat selectedFormat = match.Format;
        if(selectedFormat==CricketFormat.MultiDay)selectedFormat=CricketFormat.MultiInnings;
        var formats = new WrapPanel();
        var oversSection = new StackPanel();
        var overs = WorkflowInput((rainRevision ? match.CurrentOverLimit : match.TotalOvers).ToString()); overs.Tag = "conditions-overs";
        var presets = new WrapPanel();
        var target = WorkflowInput((match.Target ?? 1).ToString()); target.Tag = "conditions-target";
        var error = WorkflowText("", 14); error.Foreground = Brushes.LightSalmon;
        if (!rainRevision)
        {
            body.Children.Add(WorkflowText("MATCH FORMAT", 12)); body.Children.Add(formats);
            foreach (var (format, caption) in new[] { (CricketFormat.LimitedOvers, "Limited overs"), (CricketFormat.MultiInnings, "Multi-innings") })
            {
                var value = format; var button = WorkflowButton(caption, () => { selectedFormat = value; RefreshFormatSelection(); }, "conditions-format-" + format);
                button.Style = (Style)FindResource("CricketScorecardTeamButton"); button.ContentTemplate = null;
                button.MinWidth = 0; button.MinHeight = 90; button.Padding = new Thickness(12,10,24,10);
                var content = new Grid { IsHitTestVisible = false };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); content.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = new OperatorSymbol { Kind = format == CricketFormat.LimitedOvers ? "ball" : "innings-recap", Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.AccentBrush"); content.Children.Add(icon); formatIcons.Add(button,icon);
                var captions = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(captions,1);
                var title = WorkflowText(caption,16); title.Margin = new Thickness(0); title.FontWeight = FontWeights.SemiBold;
                var detail = WorkflowText(format == CricketFormat.LimitedOvers ? "Choose an over limit" : "Manage innings separately",13); detail.Margin = new Thickness(0,4,0,0); detail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
                captions.Children.Add(title); captions.Children.Add(detail); content.Children.Add(captions); button.Content = content;
                System.Windows.Automation.AutomationProperties.SetName(button,caption); System.Windows.Automation.AutomationProperties.SetHelpText(button,detail.Text + ". Draft until Apply match conditions.");
                formats.Children.Add(button);
            }
        }
        oversSection.Children.Add(WorkflowText(rainRevision ? "REVISED TOTAL OVERS" : "TOTAL OVERS PER INNINGS", 12));
        oversSection.Children.Add(overs); oversSection.Children.Add(presets);
        foreach (int amount in new[] { 20, 40, 50 }) { int value = amount; presets.Children.Add(WorkflowButton(amount.ToString(), () => overs.Text = value.ToString(), "conditions-overs-" + amount)); }
        body.Children.Add(oversSection);
        if (!rainRevision)
        {
            System.Windows.Automation.AutomationProperties.SetName(overs,"Total overs per innings");
            System.Windows.Automation.AutomationProperties.SetHelpText(overs,"Enter a whole number from 1 to 1,000. Changing this draft does not change the match until Apply.");
            System.Windows.Documents.Typography.SetNumeralAlignment(overs,FontNumeralAlignment.Tabular);
            foreach (var preset in presets.Children.OfType<Button>())
            {
                preset.Style = (Style)FindResource("CricketLineupRoleButton"); preset.MinHeight = 44; preset.MinWidth = 68;
                System.Windows.Automation.AutomationProperties.SetName(preset,"Use " + preset.Content + " overs per innings");
            }
            body.Children.Add(proposed);
            overs.TextChanged += (_,_) => RefreshFormatSelection();
            void SizeFormatCards()
            {
                int columns = body.ActualWidth >= 460 ? 2 : 1;
                foreach(var button in formats.Children.OfType<Button>())
                {
                    button.Width = Math.Max(1,(body.ActualWidth-6)/columns-6);
                    if(button.Content is Grid content)content.Width = Math.Max(1,button.Width-36);
                }
            }
            body.SizeChanged += (_,_) => SizeFormatCards();
        }
        if (rainRevision)
        {
            System.Windows.Automation.AutomationProperties.SetName(overs,"Revised total overs in this innings");
            System.Windows.Automation.AutomationProperties.SetHelpText(overs,"Total permitted overs, including those already bowled. Use the supplied official value; no DLS calculation is performed.");
            System.Windows.Documents.Typography.SetNumeralAlignment(overs,FontNumeralAlignment.Tabular);
            oversSection.Children.Add(WorkflowText("Total permitted in this innings, including overs already bowled.",13));
            foreach(var preset in presets.Children.OfType<Button>())
            {
                preset.Style = (Style)FindResource("CricketLineupRoleButton"); preset.MinHeight = 44; preset.MinWidth = 68;
                System.Windows.Automation.AutomationProperties.SetName(preset,"Use " + preset.Content + " revised total overs");
            }
            overs.TextChanged += (_,_) => RefreshFormatSelection();
        }
        if (rainRevision && chase)
        {
            var targetFields = new StackPanel();
            targetFields.Children.Add(WorkflowText("REVISED TARGET · TOTAL RUNS TO WIN",13));
            target.FontSize = 26; target.MinHeight = 50; targetFields.Children.Add(target);
            targetFields.Children.Add(WorkflowText("Total runs to win, not runs still required. Use the target supplied by officials.",13));
            var targetCard = new Border { Child = targetFields, CornerRadius = new CornerRadius(20), Padding = new Thickness(12,4,12,8), Margin = new Thickness(3,12,3,4), BorderThickness = new Thickness(1) };
            targetCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush"); targetCard.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.AccentBrush"); body.Children.Add(targetCard);
            System.Windows.Automation.AutomationProperties.SetName(target,"Official revised target, total runs to win");
            System.Windows.Automation.AutomationProperties.SetHelpText(target,"Enter total runs required to win from 1 to 1,000,000, not additional runs. No DLS calculation is performed.");
            System.Windows.Documents.Typography.SetNumeralAlignment(target,FontNumeralAlignment.Tabular);
            target.TextChanged += (_,_) => RefreshFormatSelection();
        }
        else if (rainRevision) body.Children.Add(WorkflowText("No chase target is entered in the first innings.", 13));
        if(rainRevision) body.Children.Add(proposed);
        if (!rainRevision && match.HasRainAdjustment) body.Children.Add(WorkflowText("Applying general match conditions removes the current rain revision.", 14));
        body.Children.Add(error);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(error,System.Windows.Automation.AutomationLiveSetting.Assertive);
        void ShowConditionsError(string message, System.Windows.Controls.TextBox? field = null)
        {
            // Keep a rejected value and its explanation together in the same
            // scroll viewport. General stale/commit errors return to the context.
            var destination = field?.Parent as System.Windows.Controls.Panel ?? body;
            if (error.Parent is System.Windows.Controls.Panel previous) previous.Children.Remove(error);
            int index = field is not null && ReferenceEquals(field.Parent, destination)
                ? destination.Children.IndexOf(field) + 1 : Math.Min(1, destination.Children.Count);
            destination.Children.Insert(index, error);
            error.Text = message;
        }
        void RefreshFormatSelection()
        {
            foreach (var button in formats.Children.OfType<Button>())
            {
                bool active = Equals(button.Tag, "conditions-format-" + selectedFormat);
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,"ControlCentre.SurfaceBrush");
                button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,"ControlCentre.TextBrush");
                OperatorChoiceMotion.SetSelected(button,active);
                formatIcons[button].IsActive = active;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
            }
            oversSection.Visibility = rainRevision || selectedFormat == CricketFormat.LimitedOvers ? Visibility.Visible : Visibility.Collapsed;
            bool valid = int.TryParse(overs.Text,out int draftLimit) && draftLimit is >= 1 and <= 1000;
            bool targetValid = int.TryParse(target.Text,out int draftTarget) && draftTarget is >= 1 and <= 1_000_000;
            if(error.Text == "Enter a total over limit from 1 to 1,000." && (!rainRevision && selectedFormat != CricketFormat.LimitedOvers || valid)) error.Text = "";
            if(error.Text == "Enter the supplied target from 1 to 1,000,000." && targetValid) error.Text = "";
            if(rainRevision)
                proposed.Text = !valid ? "Draft: enter a total over limit from 1 to 1,000"
                    : chase && !targetValid ? "Draft: enter the supplied total target"
                    : chase ? $"Draft: {draftLimit} total overs · target {draftTarget}" : $"Draft: {draftLimit} total overs";
            else proposed.Text = selectedFormat == CricketFormat.MultiInnings ? "Draft: Multi-innings"
                : valid ? $"Draft: {draftLimit} overs per innings" : "Draft: enter an over limit from 1 to 1,000";
            foreach (var preset in presets.Children.OfType<Button>())
            {
                bool active = (rainRevision || selectedFormat == CricketFormat.LimitedOvers) && valid && preset.Tag is string tag && tag == "conditions-overs-" + draftLimit;
                OperatorChoiceMotion.SetSelected(preset,active); System.Windows.Automation.AutomationProperties.SetItemStatus(preset,active ? "Selected" : "");
            }
        }
        RefreshFormatSelection();
        bool RejectConditionsField(System.Windows.Controls.TextBox field,string message)
        {
            ShowConditionsError(message, field);
            int generation = _workflowGeneration; string? rejectedText = field.Text; var rejectedFocus = System.Windows.Input.Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(() =>
            {
                if(generation != _workflowGeneration || !ReferenceEquals(match,_match) || token != match.MatchConditionsToken
                    || (!rainRevision && selectedFormat != CricketFormat.LimitedOvers) || field.Text != rejectedText || error.Text != message
                    || !ReferenceEquals(rejectedFocus,System.Windows.Input.Keyboard.FocusedElement) || !body.IsLoaded || !body.IsVisible
                    || !field.IsVisible || !field.IsEnabled || Window.GetWindow(body) is not { IsActive:true }) return;
                field.BringIntoView(); System.Windows.Input.Keyboard.Focus(field);
            }));
            return false;
        }
        bool ValidateConditionsDraft()
        {
            if (!ReferenceEquals(match,_match)) return false;
            if (!ReferenceEquals(innings,match.CurrentInnings) || token != match.MatchConditionsToken)
            { ShowConditionsError("The match changed while this panel was open. Go back and reopen it before applying changes."); return false; }
            if ((rainRevision || selectedFormat == CricketFormat.LimitedOvers) && (!int.TryParse(overs.Text,out int value) || value is < 1 or > 1000))
                return RejectConditionsField(overs,"Enter a total over limit from 1 to 1,000.");
            if(rainRevision && chase && (!int.TryParse(target.Text,out int supplied) || supplied is < 1 or > 1_000_000))
                return RejectConditionsField(target,"Enter the supplied target from 1 to 1,000,000.");
            int limit = rainRevision || selectedFormat == CricketFormat.LimitedOvers
                ? int.Parse(overs.Text) : Math.Clamp(match.TotalOvers, 1, 1000);
            int? suppliedTarget = rainRevision && chase ? int.Parse(target.Text) : null;
            bool allowed = rainRevision
                ? match.CanApplyRainAdjustment(limit, suppliedTarget, token, out string draftError)
                : match.CanUpdateMatchConditions(selectedFormat, limit, token, out draftError);
            if (!allowed) return RejectConditionsField(overs, draftError);
            error.Text = ""; return true;
        }
        var choices = new List<CricketPrompt.Choice> { new("back", "Back to match options"), new("close", "Close"), new("apply", rainRevision ? "Apply revised conditions" : "Apply match conditions", true) };
        if (rainRevision && match.HasRainAdjustment) choices.Add(new("clear", "Restore original overs and target"));
        while (ReferenceEquals(match, _match))
        {
            string? answer = await ValidatedFormAsync(rainRevision ? chase ? "Revised overs and target" : "Revised overs" : "Format and over limit", "", body, ValidateConditionsDraft, choices.ToArray());
            if (answer == "back") return true;
            if (answer is not ("apply" or "clear")) return false;
            if (!ReferenceEquals(innings, match.CurrentInnings) || token != match.MatchConditionsToken)
            { ShowConditionsError("The match changed while this panel was open. Go back and reopen it before applying changes."); continue; }
            if (answer == "clear")
            {
                string? confirm = await ChooseAsync("Restore original conditions?", "This removes the current rain revision and restores the normal over limit and calculated chase target.", new("back", "Back"), new("close", "Close"), new("yes", "Restore original conditions", true));
                if (confirm == "back") continue;
                if (confirm != "yes") return false;
                if (match.TryClearRainAdjustment(token, out string restoreError)) return false;
                ShowConditionsError(restoreError); continue;
            }
            int limit=Math.Clamp(match.TotalOvers,1,1000);
            if ((rainRevision || selectedFormat==CricketFormat.LimitedOvers) && (!int.TryParse(overs.Text, out limit) || limit is < 1 or > 1000))
            { ShowConditionsError("Enter a total over limit from 1 to 1,000.", overs); continue; }
            int? revisedTarget = null;
            if (rainRevision && chase)
            {
                if (!int.TryParse(target.Text, out int parsedTarget) || parsedTarget is < 1 or > 1_000_000) { ShowConditionsError("Enter the supplied target from 1 to 1,000,000.", target); continue; }
                revisedTarget = parsedTarget;
            }
            string applyError;
            bool applied = rainRevision ? match.TryApplyRainAdjustment(limit, revisedTarget, token, out applyError)
                : match.TryUpdateMatchConditions(selectedFormat, limit, token, out applyError);
            if (applied) return false;
            ShowConditionsError(applyError);
        }
        return false;
    }
}
