using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    // A separate draft keeps all related entries atomic, including Cancel and
    // failed validation. No delivery or bowler allocation is silently rewritten.
    private async Task EditScorecardTotalsAsync()
    {
        var match = _match; var original = _scorecardDraft;
        if (match is null || original is null || _scorecardCommitBusy) return;
        if (_scorecardDirty)
        {
            await ShowErrorAsync("Finish the pending edit", "Apply or discard the pending scorecard row before editing totals together.");
            return;
        }
        var draft = match.CreateScorecardDraft(original.InningsNumber);
        var body = new StackPanel();
        var summaryBody=new StackPanel();
        var totals=new Grid();totals.ColumnDefinitions.Add(new());totals.ColumnDefinitions.Add(new(){Width=new GridLength(30)});totals.ColumnDefinitions.Add(new());
        TextBlock Figure(StackPanel column,string caption,string value)
        {
            column.Children.Add(WorkflowText(caption,12));var number=WorkflowText(value,24);number.FontWeight=FontWeights.Bold;
            System.Windows.Documents.Typography.SetNumeralAlignment(number,FontNumeralAlignment.Tabular);column.Children.Add(number);return number;
        }
        var currentColumn=new StackPanel();Figure(currentColumn,"Before edit",draft.TotalRuns.ToString(CultureInfo.InvariantCulture));totals.Children.Add(currentColumn);
        var arrow=new OperatorSymbol{Kind="forward",Width=18,Height=18,VerticalAlignment=VerticalAlignment.Center};arrow.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.SecondaryTextBrush");Grid.SetColumn(arrow,1);totals.Children.Add(arrow);
        var draftColumn=new StackPanel();var draftNumber=Figure(draftColumn,"Your draft",draft.TotalRuns.ToString(CultureInfo.InvariantCulture));draftNumber.Tag="scorecard-draft-total";Grid.SetColumn(draftColumn,2);totals.Children.Add(draftColumn);summaryBody.Children.Add(totals);
        var equation=WorkflowText("",13);equation.Tag="scorecard-totals-equation";equation.Margin=new Thickness(0,6,0,0);summaryBody.Children.Add(equation);
        var summary=new Border{Tag="scorecard-correction-summary",Child=summaryBody,CornerRadius=new CornerRadius(18),Padding=new Thickness(12)};summary.SetResourceReference(BackgroundProperty,"ControlCentre.RaisedBrush");
        var auto = new CheckBox { Content = "Calculate team total from these entries", IsChecked = true, Margin = new Thickness(0, 10, 0, 10), Tag = "scorecard-totals-auto" };
        auto.SetResourceReference(StyleProperty, "SettingsToggle"); body.Children.Add(auto);
        var fields = new List<(TextBox Field, Action<int> Write, Expander? Section)>();
        TextBox Add(string caption, int value, Action<int> write, string tag,StackPanel? target=null,Expander? section=null)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(110) });
            var label = WorkflowText(caption, 14); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(label);
            var input = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Tag = tag, MinHeight = 42, MaxLength = 7 };
            input.SetResourceReference(StyleProperty, "SettingsInput"); Grid.SetColumn(input, 1); row.Children.Add(input); (target??body).Children.Add(row);
            System.Windows.Automation.AutomationProperties.SetName(input, caption);
            fields.Add((input, write,section)); return input;
        }
        var total = Add("Team total", draft.TotalRuns, value => draft.TotalRuns = value, "scorecard-edit-team-total");
        var batters=CorrectionSection("Batters","bat","batters");body.Children.Add(batters.Group);
        for (int i = 0; i < draft.Batters.Count; i++)
        {
            var player = draft.Batters[i]; Add(player.FullName, player.Runs, value => player.Runs = value, "scorecard-edit-batter-" + i,batters.Body,batters.Group);
        }
        var extras=CorrectionSection("Extras","plus","extras");body.Children.Add(extras.Group);
        extras.Body.Children.Add(WorkflowText("Enter runs, including each wide/no-ball penalty. Separate penalty awards are not supported here; do not disguise them as extras.",12));
        Add("Wides", draft.Wides, value => draft.Wides = value, "scorecard-edit-wides",extras.Body,extras.Group);
        Add("No-balls", draft.NoBalls, value => draft.NoBalls = value, "scorecard-edit-no-balls",extras.Body,extras.Group);
        Add("Byes", draft.Byes, value => draft.Byes = value, "scorecard-edit-byes",extras.Body,extras.Group);
        Add("Leg-byes", draft.LegByes, value => draft.LegByes = value, "scorecard-edit-leg-byes",extras.Body,extras.Group);
        var bowling=CorrectionSection("Bowling allocation","ball","bowling");body.Children.Add(bowling.Group);
        bowling.Body.Children.Add(WorkflowText("Update allocations explicitly when they changed. No bowler's figures are guessed.",12));
        for (int i = 0; i < draft.Bowlers.Count; i++)
        {
            var bowler = draft.Bowlers[i]; bowling.Body.Children.Add(WorkflowText(bowler.FullName, 14));
            Add("Runs conceded", bowler.RunsConceded, value => bowler.RunsConceded = value, "scorecard-edit-bowler-runs-" + i,bowling.Body,bowling.Group);
            Add("Wides", bowler.Wides, value => bowler.Wides = value, "scorecard-edit-bowler-wides-" + i,bowling.Body,bowling.Group);
            Add("No-balls", bowler.NoBalls, value => bowler.NoBalls = value, "scorecard-edit-bowler-no-balls-" + i,bowling.Body,bowling.Group);
        }
        bool reading = false;int repairGeneration=0;
        bool Read(bool revealInvalid=false)
        {
            if (reading) return false;
            reading = true;int repair=++repairGeneration;
            try
            {
                var values = new List<int>();
                foreach (var entry in fields)
                {
                    if (!int.TryParse(entry.Field.Text, out int value) || value is < 0 or > 1_000_000)
                    {
                        draftNumber.Text="—";equation.Text="Enter whole numbers from 0 to 1,000,000. No figures have been applied.";
                        if(revealInvalid)
                        {
                            if(entry.Section is not null)DisclosureMotion.RevealImmediately(entry.Section);
                            entry.Field.Focus();entry.Field.SelectAll();OperatorViewport.RevealValidationError(entry.Field);
                            // The owning viewport needs one layout turn after a
                            // long group unfolds. A newer edit/closed sheet or
                            // replacement match invalidates this focus work.
                            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
                            {
                                if(repair!=repairGeneration || !ReferenceEquals(match,_match) || !ReferenceEquals(original,_scorecardDraft)
                                    || !entry.Field.IsLoaded || !entry.Field.IsVisible || !entry.Field.IsEnabled)return;
                                OperatorViewport.RevealValidationError(entry.Field);
                            }));
                        }
                        return false;
                    }
                    values.Add(value);
                }
                // Parse the entire form before writing even the local draft.
                for (int i = 0; i < fields.Count; i++) fields[i].Write(values[i]);
                if (auto.IsChecked == true && draft.CalculatedTotal <= 1_000_000)
                { draft.TotalRuns = (int)draft.CalculatedTotal; total.Text = draft.TotalRuns.ToString(CultureInfo.InvariantCulture); }
                equation.Text = $"Batters {draft.BattingRuns} + extras {draft.ExtrasRuns} = {draft.CalculatedTotal}. Team total: {draft.TotalRuns}. "
                    + (draft.CalculatedTotal == draft.TotalRuns ? "Total balances." : "Total does not balance.");
                draftNumber.Text=draft.TotalRuns.ToString(CultureInfo.InvariantCulture);
                batters.Detail.Text=$"{draft.Batters.Count} batters · {draft.BattingRuns} runs";
                extras.Detail.Text=$"{draft.ExtrasRuns} runs · wides, no-balls, byes and leg-byes";
                bowling.Detail.Text=$"{draft.Bowlers.Count} bowlers · explicit allocation";
                total.IsReadOnly = auto.IsChecked == true; return true;
            }
            finally { reading = false; }
        }
        foreach (var entry in fields) entry.Field.TextChanged += (_, _) => Read();
        auto.Checked += (_, _) => Read(); auto.Unchecked += (_, _) => Read(); Read();
        while (ReferenceEquals(match, _match) && ReferenceEquals(original, _scorecardDraft))
        {
            var source=_scorecardRows.Children.OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"scorecard-edit-totals"));
            if (await CorrectionFormAsync("Edit runs & extras together", "Changes apply together. Wides and no-balls are runs, including the one-run penalty—not a count of deliveries. Separate penalty awards are not supported by this editor; do not disguise them as wides or no-balls. Bowling allocation is never guessed.",
                body,summary,source,()=>Read(true), new("back", "Cancel"), new("apply", "Apply all figures", true)) != "apply") return;
            if (!ReferenceEquals(match, _match) || !ReferenceEquals(original, _scorecardDraft)) return;
            if (!Read(true)) continue;
            if (match.ValidateScorecardStructure(draft) is { Count: > 0 } errors)
            { await ShowErrorAsync("Figures were not applied", string.Join("\n", errors)); continue; }
            _scorecardDraft = draft;
            if (await CommitScorecardEditAsync(draft)) return;
            if (!ReferenceEquals(draft, _scorecardDraft)) return;
            _scorecardDraft = original; _scorecardDirty = false;
        }
    }
}
