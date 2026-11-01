using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Label = System.Windows.Controls.Label;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private async Task EditScorecardDismissalAsync(CricketPlayer player)
    {
        var draft = _scorecardDraft; var match = _match;
        if (draft is null || match is null) return;
        var options = new (string Key, string Caption, DismissalType Type)[]
        {
            ("NotOut", "Not out", DismissalType.NotOut), ("Bowled", "Bowled", DismissalType.Bowled), ("Caught", "Caught", DismissalType.Caught),
            ("LBW", "LBW", DismissalType.LBW), ("RunOut", "Run out", DismissalType.RunOut), ("Stumped", "Stumped", DismissalType.Stumped),
            ("HitWicket", "Hit wicket", DismissalType.HitWicket), ("CaughtAndBowled", "Caught & bowled", DismissalType.CaughtAndBowled),
            ("RetiredHurt", "Retired hurt", DismissalType.RetiredHurt), ("RetiredNotOut", "Retired not out", DismissalType.RetiredHurt),
            ("RetiredOut", "Retired out", DismissalType.RetiredOut), ("ObstructingField", "Obstructing the field", DismissalType.ObstructingField),
            ("HitBallTwice", "Hit the ball twice", DismissalType.HitBallTwice), ("Other", "Other dismissal", DismissalType.Other)
        };
        string selected = player.IsRetired ? player.IsOut ? "RetiredOut" : player.RetiredReason == "retired not out" ? "RetiredNotOut" : "RetiredHurt" : player.IsOut ? player.HowOut.ToString() : "NotOut";
        var source = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
        var body = new StackPanel { Tag = "scorecard-dismissal-editor", MinWidth = 0, Margin = new Thickness(0,0,12,0) };
        var context = new StackPanel();
        context.Children.Add(WorkflowText(player.FullName, 19));
        var figures = WorkflowText($"{player.Runs} runs · {player.BallsFaced} balls", 16);
        System.Windows.Documents.Typography.SetNumeralAlignment(figures,FontNumeralAlignment.Tabular);
        context.Children.Add(figures);
        context.Children.Add(WorkflowText("Current record: " + player.DismissalSummary, 13));
        var contextCard = new Border { Child = context, CornerRadius = new CornerRadius(16), Padding = new Thickness(12,4,12,8), Margin = new Thickness(3,0,3,12), BorderThickness = new Thickness(1) };
        contextCard.SetResourceReference(Border.BackgroundProperty, "ControlCentre.SurfaceBrush");
        contextCard.SetResourceReference(Border.BorderBrushProperty, "ControlCentre.BorderBrush");
        body.Children.Add(contextCard); body.Children.Add(WorkflowText("Choose the recorded outcome", 15));
        var choices = new WrapPanel(); body.Children.Add(choices);
        var proposed = WorkflowText("", 15); proposed.Tag = "scorecard-dismissal-proposed";
        System.Windows.Automation.AutomationProperties.SetLiveSetting(proposed, System.Windows.Automation.AutomationLiveSetting.Polite);
        var marks = new Dictionary<Button, TextBlock>();
        void SizeChoices()
        {
            double width = Math.Max(1, body.ActualWidth); int columns = width >= 460 ? 2 : 1;
            foreach (var tile in choices.Children.OfType<Button>())
            {
                tile.Width = Math.Max(1, (width - 6) / columns - 6);
                if (tile.Content is Grid content) content.Width = Math.Max(1,tile.Width-24);
            }
        }
        body.SizeChanged += (_,_) => SizeChoices();
        var bowlerSection = new StackPanel(); var fielderSection = new StackPanel();
        var bowler = new ComboBox { Style = (Style)FindResource("SettingsChoice"), MinHeight = 42, Margin = new Thickness(3) };
        var fielder = new ComboBox { Style = (Style)FindResource("SettingsChoice"), MinHeight = 42, Margin = new Thickness(3) };
        bowler.Items.Add("Select bowler"); fielder.Items.Add("Select fielder");
        foreach (var candidate in draft.Bowlers) { bowler.Items.Add(candidate.FullName); fielder.Items.Add(candidate.FullName); }
        int FindActor(string? name) => draft.Bowlers.FindIndex(p => p.FullName == name || p.DisplayName == name) + 1;
        bowler.SelectedIndex = FindActor(player.DismissalBowler); fielder.SelectedIndex = FindActor(player.DismissalFielder);
        Label Caption(string text, System.Windows.Controls.Control target)
        {
            var label = new Label { Content = text, Target = target, FontSize = 14, FontWeight = FontWeights.SemiBold, Padding = new Thickness(3, 10, 3, 4) };
            label.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
            return label;
        }
        System.Windows.Automation.AutomationProperties.SetName(bowler, "Dismissal bowler");
        System.Windows.Automation.AutomationProperties.SetName(fielder, "Dismissal fielder");
        bowlerSection.Children.Add(Caption("_Bowler", bowler)); bowlerSection.Children.Add(bowler); body.Children.Add(bowlerSection);
        fielderSection.Children.Add(Caption("_Fielder", fielder)); fielderSection.Children.Add(fielder); body.Children.Add(fielderSection);
        var note = WorkflowInput(player.DismissalText ?? ""); var noteSection = new StackPanel(); noteSection.Children.Add(Caption("Dismissal _note", note)); noteSection.Children.Add(note); body.Children.Add(noteSection);
        System.Windows.Automation.AutomationProperties.SetName(note, "Dismissal note");
        var errorText = WorkflowText("", 14); errorText.Foreground = ScorecardAlertBrush(true); body.Children.Add(errorText);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(errorText, System.Windows.Automation.AutomationLiveSetting.Assertive);
        bool RequiresBowler(string key) => key is "Bowled" or "Caught" or "LBW" or "Stumped" or "HitWicket" or "CaughtAndBowled";
        bool CreditsBowler(string key) => RequiresBowler(key) || key == "Other" && bowler.SelectedIndex > 0;
        void RefreshChoices()
        {
            foreach (var tile in choices.Children.OfType<Button>())
            {
                bool active = Equals(tile.Tag, selected);
                tile.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlCentre.SurfaceBrush");
                OperatorChoiceMotion.SetSelected(tile, active);
                marks[tile].Visibility = active ? Visibility.Visible : Visibility.Hidden;
                tile.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
                tile.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, active ? "ControlCentre.AccentBrush" : "ControlCentre.BorderBrush");
                tile.BorderThickness = new Thickness(1);
                System.Windows.Automation.AutomationProperties.SetItemStatus(tile, active ? "Selected" : "");
            }
            proposed.Text = player.DisplayName + " · Proposed: " + options.First(option => option.Key == selected).Caption;
            proposed.ToolTip = player.FullName + " · " + proposed.Text;
            bowlerSection.Visibility = RequiresBowler(selected) || selected == "Other" ? Visibility.Visible : Visibility.Collapsed;
            fielderSection.Visibility = selected is "Caught" or "Stumped" or "RunOut" ? Visibility.Visible : Visibility.Collapsed;
            noteSection.Visibility = selected == "Other" ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var option in options)
        {
            var key = option.Key; var tile = WorkflowButton(option.Caption, () => { selected = key; errorText.Text = ""; RefreshChoices(); }, key);
            tile.Style = (Style)FindResource("CricketLineupRoleButton");
            tile.ContentTemplate = null; tile.MinWidth = 0; tile.MinHeight = 76; tile.Padding = new Thickness(0);
            var content = new Grid { IsHitTestVisible = false };
            content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var captions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var caption = WorkflowText(option.Caption, 15); caption.Margin = new Thickness(0); caption.FontWeight = FontWeights.SemiBold;
            var consequence = WorkflowText(key is "NotOut" or "RetiredHurt" or "RetiredNotOut" ? "No team wicket" : "Team wicket", 12); consequence.Margin = new Thickness(0,4,0,0);
            consequence.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            captions.Children.Add(caption); captions.Children.Add(consequence); content.Children.Add(captions);
            var mark = WorkflowText("✓", 16); mark.Margin = new Thickness(6,0,0,0); mark.VerticalAlignment = VerticalAlignment.Center;
            mark.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.AccentBrush"); Grid.SetColumn(mark,1); content.Children.Add(mark);
            tile.Content = content; marks.Add(tile,mark);
            System.Windows.Automation.AutomationProperties.SetName(tile, option.Caption);
            System.Windows.Automation.AutomationProperties.SetHelpText(tile, consequence.Text + ". This selection is a draft until Apply dismissal.");
            choices.Children.Add(tile);
        }
        RefreshChoices();
        bool Reject(System.Windows.Controls.Control target, string message)
        {
            errorText.Text = message;
            int generation = _workflowGeneration; string rejectedSelection = selected;
            var rejectedFocus = System.Windows.Input.Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (generation != _workflowGeneration || selected != rejectedSelection || errorText.Text != message
                    || !ReferenceEquals(rejectedFocus, System.Windows.Input.Keyboard.FocusedElement) || !ReferenceEquals(draft, _scorecardDraft) || !ReferenceEquals(match, _match)
                    || !body.IsLoaded || !body.IsVisible || !target.IsVisible || !target.IsEnabled
                    || Window.GetWindow(body) is not { IsActive: true }) return;
                target.BringIntoView(); System.Windows.Input.Keyboard.Focus(target);
            }));
            return false;
        }
        bool ValidateDismissal()
        {
            if (!ReferenceEquals(draft, _scorecardDraft) || !ReferenceEquals(match, _match)) return false;
            if (RequiresBowler(selected) && bowler.SelectedIndex <= 0) return Reject(bowler, "Choose the bowler to receive this wicket.");
            if (selected is "Caught" or "Stumped" && fielder.SelectedIndex <= 0) return Reject(fielder, "Choose the fielder for this dismissal.");
            if (selected == "Other" && note.Text.Trim().Length > 120) return Reject(note, "Keep the dismissal note within 120 characters.");
            errorText.Text = ""; return true;
        }
        while (ReferenceEquals(draft, _scorecardDraft) && ReferenceEquals(match, _match))
        {
            if (await CorrectionFormAsync("Edit dismissal", "Review this scorecard correction before applying it.", body, proposed, source, ValidateDismissal, new("back", "Cancel"), new("apply", "Apply dismissal", true)) != "apply") return;
            if (!ReferenceEquals(draft, _scorecardDraft) || !ReferenceEquals(match, _match)) return;
            if (RequiresBowler(selected) && bowler.SelectedIndex <= 0) { errorText.Text = "Choose the bowler to receive this wicket."; continue; }
            if (selected is "Caught" or "Stumped" && fielder.SelectedIndex <= 0) { errorText.Text = "Choose the fielder for this dismissal."; continue; }
            if (selected == "Other" && note.Text.Trim().Length > 120) { errorText.Text = "Keep the dismissal note within 120 characters."; continue; }
            var previousPlayer = player.Clone(); var previousWickets = draft.Bowlers.Select(bowler => bowler.Wickets).ToArray(); int previousTotal = draft.TotalWickets;
            bool oldCredit = player.IsOut && player.HowOut is not (DismissalType.RunOut or DismissalType.RetiredOut or DismissalType.RetiredHurt or DismissalType.NotOut or DismissalType.ObstructingField or DismissalType.HitBallTwice);
            int oldBowler = FindActor(player.DismissalBowler) - 1;
            if (oldCredit && oldBowler >= 0 && draft.Bowlers[oldBowler].Wickets > 0) draft.Bowlers[oldBowler].Wickets--;
            var option = options.First(o => o.Key == selected);
            player.IsOut = selected is not ("NotOut" or "RetiredHurt" or "RetiredNotOut"); player.HowOut = option.Type;
            player.IsRetired = selected is "RetiredHurt" or "RetiredNotOut" or "RetiredOut";
            player.RetiredReason = player.IsRetired ? option.Caption.ToLowerInvariant() : null;
            player.DismissalBowler = CreditsBowler(selected) ? draft.Bowlers[bowler.SelectedIndex - 1].DisplayName : null;
            player.DismissalFielder = fielderSection.Visibility == Visibility.Visible && fielder.SelectedIndex > 0 ? draft.Bowlers[fielder.SelectedIndex - 1].DisplayName : null;
            player.DismissalText = selected == "Other" ? string.IsNullOrWhiteSpace(note.Text) ? "out" : note.Text.Trim() : null;
            if (CreditsBowler(selected)) draft.Bowlers[bowler.SelectedIndex - 1].Wickets++;
            draft.TotalWickets = draft.Batters.Count(p => p.IsOut);
            bool alreadyPending = _scorecardDirty; _scorecardDirty = true;
            if (await CommitScorecardEditAsync(draft)) return;
            if (!ReferenceEquals(draft, _scorecardDraft)) return;
            player.IsOut = previousPlayer.IsOut; player.HowOut = previousPlayer.HowOut; player.IsRetired = previousPlayer.IsRetired; player.RetiredReason = previousPlayer.RetiredReason;
            player.DismissalBowler = previousPlayer.DismissalBowler; player.DismissalFielder = previousPlayer.DismissalFielder; player.DismissalText = previousPlayer.DismissalText;
            for (int i = 0; i < previousWickets.Length; i++) draft.Bowlers[i].Wickets = previousWickets[i]; draft.TotalWickets = previousTotal;
            _scorecardDirty = alreadyPending; RenderScorecard();
        }
    }
}
