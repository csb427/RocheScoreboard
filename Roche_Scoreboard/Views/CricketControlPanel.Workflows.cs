using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private bool _workflowBusy;
    internal bool HasActiveDecision => _workflowBusy;
    private string? _completionShown;
    private int _workflowGeneration;
    private void InitializeWorkflowControls() { InitializeScorecard(); IsVisibleChanged += (_, _) => UpdateWorkflowState(); }
    private void ResetWorkflowControls()
    {
        ClosePlayerMenu();
        _workflowGeneration++; _completionShown = null; _workflowBusy = false;
        _deliveryPointerGuardUntil = DateTime.MinValue; _lastOperatorInputWasKeyboard = true;
        _lastPointerDownPosition = _closingPointerPosition = null;
        CricketPrompt.CancelAll(CricketRoot); ResetScorecard();
    }
    internal void PrepareForScenarioReload()
    {
        ResetWorkflowControls(); _selectorGeneration++;
        _deliveryEntry?.Cancel(); _deliveryEntry = null;
        DeliveryEntryOverlay.Visibility = BatterSelectionOverlay.Visibility = BowlerSelectionOverlay.Visibility = TossOverlay.Visibility = Visibility.Collapsed;
        DeliveryEntryContent.Content = null; ConsoleViewport.IsEnabled = true; ConsoleViewport.IsHitTestVisible = true;
        _cpTopBatterIndex = _cpBottomBatterIndex = -1;
        _cpBatLogo = _cpBowlLogo = _cpThemeSignature = null;
        _loggedInnings = null; _loggedDeliveryCount = 0;
    }
    private async Task<string?> ChooseAsync(string title, string message, params CricketPrompt.Choice[] choices)
    {
        if (_workflowBusy) return null;
        _workflowBusy = true; int generation = _workflowGeneration;
        try { var result = await CricketPrompt.ChooseAsync(CricketRoot, title, message, choices); return generation == _workflowGeneration ? result : null; }
        finally { ReleaseWorkflow(generation); }
    }
    private async Task<string?> FormAsync(string title, string message, UIElement body, params CricketPrompt.Choice[] choices)
    {
        if (_workflowBusy) return null;
        _workflowBusy = true; int generation = _workflowGeneration;
        try { var result = await CricketPrompt.ShowAsync(CricketRoot, title, message, body, choices); return generation == _workflowGeneration ? result : null; }
        finally { ReleaseWorkflow(generation); }
    }
    private async Task<string?> ValidatedFormAsync(string title,string message,UIElement body,Func<bool> validate,params CricketPrompt.Choice[] choices)
    {
        if (_workflowBusy) return null;
        _workflowBusy = true; int generation = _workflowGeneration;
        try { var result = await CricketPrompt.ShowValidatedAsync(CricketRoot,title,message,body,validate,choices); return generation == _workflowGeneration ? result : null; }
        finally { ReleaseWorkflow(generation); }
    }
    private void ReleaseWorkflow(int generation)
    {
        if (generation != _workflowGeneration) return;
        _workflowBusy = false; GuardPointerContinuation(220);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        { if (generation == _workflowGeneration) UpdateWorkflowState(); }));
    }
    private async Task<string?> CorrectionFormAsync(string title,string message,UIElement body,FrameworkElement summary,FrameworkElement? source,Func<bool> validate,params CricketPrompt.Choice[] choices)
    {
        if(_workflowBusy)return null;
        _workflowBusy=true;int generation=_workflowGeneration;
        try{var result=await CricketPrompt.ShowCorrectionAsync(CricketRoot,title,message,body,summary,source,validate,choices);return generation==_workflowGeneration?result:null;}
        finally{ReleaseWorkflow(generation);}
    }
    private Task<string?> ShowErrorAsync(string title, string error) => ChooseAsync(title, error, new CricketPrompt.Choice("close", "Return", true));
    private Button WorkflowButton(string text, Action action, string? tag = null)
    {
        var button = new Button { Content = text, ContentTemplate = CricketPrompt.ButtonContent, Tag = tag, Style = (Style)FindResource("CricketActionButton"), MinHeight = 40, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3), FontSize = 14 };
        button.Click += (_, _) => action(); return button;
    }
    private static TextBlock WorkflowText(string text, double size = 15)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = size >= 17 ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 8, 3, 5) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        label.SetResourceReference(TextBlock.FontFamilyProperty, "ControlCentre.Font");
        return label;
    }
    private static TextBox WorkflowInput(string text)
    {
        var input = new TextBox { Text = text, FontSize = 17, MinHeight = 42, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(3) };
        input.SetResourceReference(StyleProperty, "SettingsInput");
        return input;
    }

    private void UpdateWorkflowState()
    {
        if (_match?.CurrentInnings is not { } innings) return;
        bool pause = _match.BreakKind != CricketBreakKind.None || _match.IsMatchFinalized;
        WorkflowStatus.Text = _match.IsMatchFinalized ? _match.ResultStatusLabel : _match.TossPending ? "SETUP" : pause ? _match.BreakKind.ToString().ToUpperInvariant() : "LIVE";
        if (_match.IsMatchFinalized)
        {
            BatterSelectionOverlay.Visibility = BowlerSelectionOverlay.Visibility = TossOverlay.Visibility = Visibility.Collapsed;
            _deliveryEntry?.Cancel();
        }
        EndInningsButton.IsEnabled = !_match.TossPending && !_match.IsMatchFinalized;
        UpdateScoringGate();
        UpdateScorecardStatus();
        if (!_match.CompletionDecisionRequired || !IsVisible || _workflowBusy || _deliveryEntry is not null || BatterSelectionOverlay.Visibility == Visibility.Visible || BowlerSelectionOverlay.Visibility == Visibility.Visible) return;
        string key = $"{innings.InningsNumber}:{_match.CompletionReason}:{_match.DeliveryRevision}";
        if (_completionShown == key) return;
        _completionShown = key; int generation = _workflowGeneration;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (generation != _workflowGeneration || _completionShown != key) return;
            if (_workflowBusy) { _completionShown = null; return; }
            if (_match?.CompletionDecisionRequired == true) await OpenInningsDecisionAsync(true);
        }));
    }
    private async Task<bool> OpenInningsDecisionAsync(bool automatic, bool fromOptions = false)
    {
        var match = _match; var innings = match?.CurrentInnings;
        if (match is null || innings is null || match.TossPending || match.IsMatchFinalized) return false;
        string context = match.MatchConditionsToken;
        bool Current() => ReferenceEquals(match, _match) && ReferenceEquals(innings, match.CurrentInnings)
            && context == match.MatchConditionsToken;
        async Task ExplainChangedInnings()
        {
            if (ReferenceEquals(match, _match))
                await ShowErrorAsync("Innings changed", "Play or match conditions changed while this decision was open. No innings action was applied. Reopen innings options to review the current score.");
        }
        var choices = new List<CricketPrompt.Choice> { new("back", fromOptions ? "Back to match options" : automatic ? "Review match" : "Keep scoring") };
        if (fromOptions) choices.Add(new("close", "Close"));
        bool finishRecommended=match.Format==CricketFormat.LimitedOvers && match.AllInnings.Count>=2 && innings.IsComplete;
        if (match.CanContinueCurrentInnings && match.AllInnings.Count > 1) choices.Add(new("continue", "Continue this innings", Detail:"Target and over limit will no longer stop this innings. All-out still applies."));
        if (match.AllInnings.Count < 4)
        {
            int aCount = match.AllInnings.Count(i => i.BattingTeamName == match.TeamAName), bCount = match.AllInnings.Count(i => i.BattingTeamName == match.TeamBName);
            if (aCount < 2 && (aCount == 0 || bCount > 0)) choices.Add(new("a", $"Start {match.TeamAName}'s next innings", !finishRecommended, Detail:innings.IsComplete ? "Open batting and bowling selection for the next innings." : "Close this innings early, then choose its opening players."));
            if (bCount < 2 && (bCount == 0 || aCount > 0)) choices.Add(new("b", $"Start {match.TeamBName}'s next innings", !finishRecommended, Detail:innings.IsComplete ? "Open batting and bowling selection for the next innings." : "Close this innings early, then choose its opening players."));
        }
        if (match.AllInnings.Count > 1) choices.Add(new("finish", "End game",Primary:finishRecommended,Destructive:!finishRecommended, Detail:"Review match outcome options before finishing."));
        string message = automatic ? "Choose what happens next. The score and ball history are preserved." : "End this innings, start another innings, or finish the game.";
        if (!innings.IsComplete && match.Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled < match.CurrentOverLimit * 6)
            message = $"This innings still has {(match.CurrentOverLimit * 6 - innings.LegalBallsBowled) / 6}.{(match.CurrentOverLimit * 6 - innings.LegalBallsBowled) % 6} overs available. Starting another innings will close it early.\n\n" + message;
        string? answer = await InningsContextDecisionAsync(automatic ? match.CompletionReason : "Innings options", message, match, innings, choices.ToArray());
        if (!Current()) { if (answer is "a" or "b" or "continue" or "finish") await ExplainChangedInnings(); return false; }
        if (answer == "back") return fromOptions;
        if (answer is "a" or "b")
        {
            if (!innings.IsComplete && await InningsContextDecisionAsync("Close this innings early?", "The existing deliveries remain in the scorecard. You can review them later.", match, innings, new("back", "Go back"), new("yes", "Close innings and continue", true, Detail:"Preserve the recorded deliveries and open the next innings.")) != "yes") return false;
            if (!Current()) { await ExplainChangedInnings(); return false; }
            if (match.TryStartAdditionalInnings(answer == "a", out string error)) { _completionShown = null; _cpTopBatterIndex = _cpBottomBatterIndex = -1; TriggerInitialSelection(); }
            else await ShowErrorAsync("Unable to start innings", error);
        }
        else if (answer == "continue")
        {
            if (await InningsContextDecisionAsync("Continue beyond the usual finish?", "The current target and over limit will no longer stop this innings. All-out still ends it.", match, innings, new("back", "Go back"), new("yes", "Continue scoring", true, Detail:"Target and over limit no longer stop this innings. All-out still applies.")) != "yes") return false;
            if (!Current()) { await ExplainChangedInnings(); return false; }
            if (!match.TryContinueCurrentInnings(out string error)) await ShowErrorAsync("Unable to continue", error); else ResumePendingSelection();
        }
        else if (answer == "finish")
            await OpenMatchOutcomeAsync();
        return false;
    }
    private async void MatchOptions_Click(object sender, RoutedEventArgs args)
        => await OpenMatchOptionsAsync();
    private async Task OpenMatchOptionsAsync()
    {
        if (_match is not { } match) return;
        FrameworkElement MatchContext(string tag)
        {
            var record = new StackPanel();
            var teams=WorkflowText(match.TeamAName+" · "+match.TeamBName,19);teams.FontWeight=FontWeights.SemiBold;record.Children.Add(teams);
            if(match.TossPending)record.Children.Add(WorkflowText("Complete the toss before innings actions.",14));
            else if(match.CurrentInnings is { } innings)
            {
                var state=WorkflowText($"Innings {innings.InningsNumber} · {innings.BattingTeamName} batting · {innings.TotalWickets}/{innings.TotalRuns} · at opening",14);
                System.Windows.Documents.Typography.SetNumeralAlignment(state,FontNumeralAlignment.Tabular);record.Children.Add(state);
            }
            string status=match.IsMatchFinalized?match.MatchResult ?? "Match ended":match.BreakKind==CricketBreakKind.None?"No break recorded.":"Break: "+match.BreakKind+(string.IsNullOrWhiteSpace(match.BreakNote)?"":" · "+match.BreakNote);
            if(match.IsMatchFinalized || match.BreakKind!=CricketBreakKind.None)record.Children.Add(WorkflowText(status,13));
            var card=new Border {Tag=tag,Child=record,CornerRadius=new CornerRadius(20),Padding=new Thickness(12,8,12,10),BorderThickness=new Thickness(1)};
            card.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");card.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");return card;
        }
        while (ReferenceEquals(match, _match))
        {
            var actions = new List<CricketPrompt.Choice> { new("close", "Close"), new("teams", "Edit teams") };
            if (!match.IsMatchFinalized) actions.Add(new("break", match.BreakKind == CricketBreakKind.None ? "Take a break / rain delay" : "Change the break", Detail:"Scoring pauses until resumed."));
            if (!match.IsMatchFinalized) actions.Add(new("conditions", "Format and over limit"));
            if (!match.TossPending && !match.IsMatchFinalized && match.Format == CricketFormat.LimitedOvers) actions.Add(new("rain", "Revised overs / target", Detail:"Official revised values only. No DLS calculation."));
            if (!match.TossPending && !match.IsMatchFinalized) actions.Add(new("innings", "Innings and game options"));
            if (match.CanRecordNonDeliveryRunOut) actions.Add(new("before-delivery", "Non-striker run out before delivery", Detail:"No delivery is added."));
            actions.Add(new("outcome", match.IsMatchFinalized ? "Review outcome / reopen match" : "End match / record outcome"));
            string? answer = await ContextDecisionAsync("Match options", "", MatchContext("match-options-context"), actions.ToArray());
            if (!ReferenceEquals(match, _match)) return;
            if (answer == "break")
            {
                var choices = Enum.GetValues<CricketBreakKind>().Where(k => k != CricketBreakKind.None).Select(k => new CricketPrompt.Choice(k.ToString(), k == CricketBreakKind.Rain ? "Rain delay" : k + " break", Detail:k==CricketBreakKind.Rain?"Pause scoring for rain. Revised overs or targets are entered separately.":k==CricketBreakKind.Stumps?"Record stumps without ending the match.":"Pause scoring with this break status. Resume from the break panel."))
                    .Prepend(new CricketPrompt.Choice("back", "Back to match options")).Prepend(new CricketPrompt.Choice("close", "Close")).ToArray();
                string breakReviewToken = match.WorkflowToken;
                string? kind = await ContextDecisionAsync("Break in play", "Scoring pauses. Click the break panel to resume when play is ready.", MatchContext("match-break-context"), choices);
                if (kind == "back") continue;
                if (Enum.TryParse(kind, out CricketBreakKind parsed))
                {
                    if (!ReferenceEquals(match, _match)) return;
                    if (breakReviewToken != match.WorkflowToken)
                    {
                        await ShowErrorAsync("Break was not changed", "The match changed while this review was open. This choice was not applied. Open match options again to review the current break.");
                        return;
                    }
                    match.SetBreak(parsed);
                }
            }
            else if (answer == "teams") { if (await OpenLineupAsync(true)) continue; }
            else if (answer == "innings") { if (await OpenInningsDecisionAsync(false, true)) continue; }
            else if (answer == "conditions") { if (await OpenConditionsEditorAsync(false)) continue; }
            else if (answer == "rain") { if (await OpenConditionsEditorAsync(true)) continue; }
            else if (answer == "before-delivery") await OpenUndeliveredRunOutAsync();
            else if (answer == "outcome") await OpenMatchOutcomeAsync();
            return;
        }
    }
    private async Task OpenBatterActionsAsync(int playerIndex, bool replaceDirect = false)
    {
        var match = _match; var innings = match?.CurrentInnings;
        if (match is null || innings is null || playerIndex < 0 || playerIndex >= innings.BattingOrder.Count) return;
        bool striker = playerIndex == innings.StrikerIndex;
        int revision = match.DeliveryRevision;
        bool Current() => ReferenceEquals(match, _match) && ReferenceEquals(innings, match.CurrentInnings) && revision == match.DeliveryRevision
            && (striker ? innings.StrikerIndex : innings.NonStrikerIndex) == playerIndex;
        var outgoing = innings.BattingOrder[playerIndex];
        string outgoingName = outgoing.FullName, outgoingFigures = $"{outgoing.Runs} ({outgoing.BallsFaced})";
        FrameworkElement source = playerIndex == _cpTopBatterIndex ? StrikerMenuButton : playerIndex == _cpBottomBatterIndex ? NonStrikerMenuButton : MatchOptionsButton;
        FrameworkElement Record(string stage, CricketPlayer? incoming = null)
        {
            var record = new StackPanel();
            record.Children.Add(WorkflowText(stage,12));
            record.Children.Add(WorkflowText(outgoingName,20));
            var figures = WorkflowText($"{outgoingFigures} · runs (balls faced) · {(striker ? "Striker" : "Non-striker")} at opening",13);
            System.Windows.Documents.Typography.SetNumeralAlignment(figures,FontNumeralAlignment.Tabular);record.Children.Add(figures);
            if (incoming is not null)
            {
                var arrival = new Grid { Margin = new Thickness(0,12,0,0) };
                arrival.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });arrival.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = new OperatorSymbol { Kind = "replace", Width = 22, Height = 22, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                icon.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.AccentBrush");arrival.Children.Add(icon);icon.Loaded += (_,_) => icon.Activate();
                var identity = new StackPanel();identity.Children.Add(WorkflowText("SELECTED REPLACEMENT",12));identity.Children.Add(WorkflowText(incoming.FullName,18));
                var existing = WorkflowText($"{incoming.Runs} ({incoming.BallsFaced}) · existing runs (balls faced)",13);
                System.Windows.Documents.Typography.SetNumeralAlignment(existing,FontNumeralAlignment.Tabular);identity.Children.Add(existing);Grid.SetColumn(identity,1);arrival.Children.Add(identity);record.Children.Add(arrival);
            }
            var context = new Border { Tag = "batter-replacement-context", Child = record, CornerRadius = new CornerRadius(20), Padding = new Thickness(12,8,12,12), BorderThickness = new Thickness(1) };
            context.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");context.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");return context;
        }
        async Task<string?> Decide(string title,string message,FrameworkElement context,params CricketPrompt.Choice[] choices)
        {
            if (_workflowBusy) return null;
            _workflowBusy = true; int generation = _workflowGeneration;
            try
            {
                var answer = await CricketPrompt.ChooseWithContextAsync(CricketRoot,title,message,context,source,choices);
                return generation == _workflowGeneration ? answer : null;
            }
            finally { ReleaseWorkflow(generation); }
        }
        string? action = replaceDirect ? "replace" : await Decide(innings.BattingOrder[playerIndex].FullName, "Change the batter without changing historical deliveries.", Record("BATTER UNDER REVIEW"), new("back", "Return"), new("replace", "Replace batter"), new("strike", "Swap strike"));
        if (!Current()) return;
        if (action == "strike") { match.RotateStrike(); return; }
        if (action != "replace") return;
        var candidates = innings.BattingOrder.Select((p, i) => (Player: p, Index: i)).Where(p => p.Index != innings.StrikerIndex && p.Index != innings.NonStrikerIndex && !p.Player.IsOut).ToArray();
        if (candidates.Length == 0) { await ShowErrorAsync("No replacement available", "Every remaining player is already batting or out. Review the scorecard or line-up."); return; }
        string? selected = await Decide("Select replacement", "The new batter keeps their own innings statistics unless you explicitly transfer a mistaken selection.", Record("REPLACING THIS BATTER"), candidates.Select(p => new CricketPrompt.Choice(p.Index.ToString(), p.Player.FullName, Detail: $"{p.Player.Runs} runs · {p.Player.BallsFaced} balls faced" + (p.Player.IsRetired ? " · retired record" : ""))).Prepend(new CricketPrompt.Choice("back", "Cancel replacement")).ToArray());
        if (!Current()) return;
        if (!int.TryParse(selected, out int replacement)) return;
        string? reason = await Decide("Why is the batter changing?", "This records the departure accurately in the scorecard.", Record("REPLACEMENT UNDER REVIEW", innings.BattingOrder[replacement]), new("back", "Cancel replacement"), new("RetiredHurt", "Retired hurt", Detail: "Keeps the outgoing batter’s figures. Does not add a wicket or a delivery."), new("RetiredNotOut", "Retired not out", Detail: "Keeps the outgoing batter’s figures and records the retirement as not out."), new("RetiredOut", "Retired out", Detail: "Keeps the outgoing batter’s figures and adds one team wicket, without a delivery."), new("WrongPlayer", "Wrong player was selected", Detail: "Next, choose whether the recorded figures belong to the replacement or stay with the original player."));
        if (!Current()) return;
        if (!Enum.TryParse(reason, out CricketBatterReplacementReason parsedReason)) return;
        bool carry = false;
        if (parsedReason == CricketBatterReplacementReason.WrongPlayer)
        {
            string? transfer = await Decide("Correct the player selection", "Choose whether the runs and balls recorded for the wrong player belong to the replacement.", Record("FIGURES TO RECONCILE", innings.BattingOrder[replacement]), new("back", "Cancel replacement"), new("carry", "Transfer recorded figures", true, Detail: $"Move {outgoingFigures} from {outgoingName} to {innings.BattingOrder[replacement].FullName}. Delivery attribution is reconciled by the existing correction."), new("reset", "Keep figures with original player", Detail: $"Keep {outgoingFigures} with {outgoingName}. {innings.BattingOrder[replacement].FullName} takes this end with their own figures."));
            if (!Current()) return;
            if (transfer is null or "back") return; carry = transfer == "carry";
        }
        if (!match.TryReplaceBatter(striker, replacement, parsedReason, carry, out string error)) await ShowErrorAsync("Unable to replace batter", error);
    }

    private async Task<bool> OpenLineupAsync(bool teamA)
    {
        var match = _match; if (match is null) return false;
        int lineupGeneration = _workflowGeneration; var lineupOwner = Window.GetWindow(this);
        var originalIdentity = (match.TeamAName,match.TeamAAbbr,match.TeamBName,match.TeamBAbbr);
        var originalRosters = new[] { match.GetLineup(true).ToArray(),match.GetLineup(false).ToArray() };
        bool CurrentRoster() => ReferenceEquals(match,_match) && lineupGeneration == _workflowGeneration
            && originalIdentity == (match.TeamAName,match.TeamAAbbr,match.TeamBName,match.TeamBAbbr)
            && originalRosters[0].SequenceEqual(match.GetLineup(true)) && originalRosters[1].SequenceEqual(match.GetLineup(false));
        var players = new[] { match.GetLineup(true).OrderBy(p => p.BattingPosition).ToList(), match.GetLineup(false).OrderBy(p => p.BattingPosition).ToList() };
        var names = new[] { WorkflowInput(match.TeamAName), WorkflowInput(match.TeamBName) };
        var abbreviations = new[] { WorkflowInput(match.TeamAAbbr), WorkflowInput(match.TeamBAbbr) };
        var roleControls=new List<(Button Control,int Team,int Index,bool Keeper)>();
        void RefreshRoles()
        {
            foreach(var (button,team,index,keeper) in roleControls)
            {
                bool selected=keeper?players[team][index].IsWicketkeeper:players[team][index].IsCaptain;
                var colour=keeper?"#75D9B0":"#F0CB78";var accent=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colour)!;
                button.Background=selected?accent:Brushes.Transparent;button.Foreground=selected?Brushes.Black:accent;
                OperatorChoiceMotion.SetSelected(button, selected);
                button.Content = (keeper ? "WK" : "C") + (selected ? " ✓" : "");
                string caption = keeper ? "Wicketkeeper" : "Captain";
                button.ToolTip = caption + (selected ? " · selected" : " · not selected");
                System.Windows.Automation.AutomationProperties.SetName(button, caption + " · " + players[team][index].FirstName + " " + players[team][index].LastName);
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"Not selected");
            }
        }
        int active = teamA ? 0 : 1, dragging = -1;
        System.Windows.Point? dragOrigin = null;
        var dropPreviews = new List<Border>();
        void ClearDropPreview() { foreach (var preview in dropPreviews) preview.Visibility = Visibility.Collapsed; }
        var body = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; var tabs = new Grid(); tabs.ColumnDefinitions.Add(new()); tabs.ColumnDefinitions.Add(new()); body.Children.Add(tabs);
        var validation = WorkflowText("",14);validation.Tag = "lineup-validation-error";validation.Focusable = true;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(validation,System.Windows.Automation.AutomationLiveSetting.Assertive);
        Border validationCard = new() { Child = validation, CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0,8,0,0), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
        validationCard.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");validationCard.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");body.Children.Add(validationCard);
        var editors = new StackPanel(); body.Children.Add(editors);
        int validationGeneration = 0;
        void ClearValidation() { ++validationGeneration;validation.Text = "";validationCard.Visibility = Visibility.Collapsed; }
        int renderGeneration = 0;
        var tabButtons = new Button[2];
        string Primary(int team) => team == 0 ? match.TeamAPrimaryColor : match.TeamBPrimaryColor;
        void RefreshTabs()
        {
            for (int t = 0; t < 2; t++)
            {
                var label = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                label.Children.Add(new TextBlock { Text = t == 0 ? "HOME" : "AWAY", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Opacity = .75 });
                label.Children.Add(new TextBlock { Text = names[t].Text, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
                tabButtons[t].Content = label; tabButtons[t].ContentTemplate = null;
                OperatorChoiceMotion.SetSelected(tabButtons[t], t == active);
                System.Windows.Automation.AutomationProperties.SetName(tabButtons[t], (t == 0 ? "Home team · " : "Away team · ") + names[t].Text);
                tabButtons[t].Background = CricketBroadcastStyle.Gradient(Primary(t), null);
                tabButtons[t].BorderBrush = t == active ? Brushes.LightSkyBlue : Brushes.SlateGray;
                tabButtons[t].BorderThickness = new Thickness(t == active ? 2 : 1); tabButtons[t].Opacity = t == active ? 1 : .72;
                System.Windows.Automation.AutomationProperties.SetItemStatus(tabButtons[t], t == active ? "Selected" : "");
            }
        }
        void FocusRow(int team, int index, bool firstName, object? expectedFocus)
        {
            int generation = renderGeneration;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (generation != renderGeneration || active != team || lineupGeneration != _workflowGeneration
                    || !ReferenceEquals(match, _match) || !_workflowBusy || !body.IsLoaded || !body.IsVisible
                    || lineupOwner is not { IsActive: true } || !ReferenceEquals(Window.GetWindow(body), lineupOwner)) return;
                if (Keyboard.FocusedElement is FrameworkElement focused && focused.IsVisible
                    && !ReferenceEquals(focused, expectedFocus) && !ReferenceEquals(focused, this)
                    && !ReferenceEquals(focused, body) && focused is not Window) return;
                var row = editors.Children.OfType<Grid>().FirstOrDefault(card => Equals(card.Tag, "lineup-row-" + index));
                FrameworkElement? target = firstName ? row?.Children.OfType<TextBox>().FirstOrDefault(field => Grid.GetColumn(field) == 1)
                    : row?.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, "lineup-drag"));
                if (target is { IsLoaded: true, IsVisible: true, IsEnabled: true } && target.Focus()) target.BringIntoView();
            }));
        }
        void Move(int team, int from, int to)
        {
            var lineup = players[team]; if (from == to || from < 0 || to < 0 || from >= lineup.Count || to >= lineup.Count) return;
            var focus = Keyboard.FocusedElement;
            var player = lineup[from]; lineup.RemoveAt(from); lineup.Insert(to, player); Render();
            FocusRow(team, to, false, focus);
        }
        void Render()
        {
            ++renderGeneration;
            // Keep the original draft inputs when a tab/reorder rebuilds its
            // presentation; their old nested details grid must release them.
            foreach (var field in names.Concat(abbreviations))
                if (field.Parent is System.Windows.Controls.Panel previous) previous.Children.Remove(field);
            editors.Children.Clear(); dropPreviews.Clear();roleControls.Clear(); RefreshTabs(); int team = active; var lineup = players[team];
            var details = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            details.ColumnDefinitions.Add(new()); details.ColumnDefinitions.Add(new() { Width = new GridLength(110) });
            details.RowDefinitions.Add(new() { Height = GridLength.Auto }); details.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var nameLabel = WorkflowText("Team name", 13); var abbreviationLabel = WorkflowText("Abbreviation", 13);
            Grid.SetColumn(abbreviationLabel, 1); details.Children.Add(nameLabel); details.Children.Add(abbreviationLabel);
            Grid.SetRow(names[team], 1); Grid.SetRow(abbreviations[team], 1); Grid.SetColumn(abbreviations[team], 1);
            System.Windows.Automation.AutomationProperties.SetName(names[team], team == 0 ? "Home team name" : "Away team name");
            System.Windows.Automation.AutomationProperties.SetName(abbreviations[team], team == 0 ? "Home abbreviation" : "Away abbreviation");
            details.Children.Add(names[team]); details.Children.Add(abbreviations[team]); editors.Children.Add(details);
            editors.Children.Add(WorkflowText($"Batting order · {lineup.Count} players", 18));
            editors.Children.Add(WorkflowText("Drag the numbered handle, or use Alt+↑ / Alt+↓. Select WK for wicketkeeper or C for captain.", 13));
            var playerHeadings = new Grid(); playerHeadings.ColumnDefinitions.Add(new() { Width = new GridLength(40) }); playerHeadings.ColumnDefinitions.Add(new()); playerHeadings.ColumnDefinitions.Add(new()); playerHeadings.ColumnDefinitions.Add(new() { Width = new GridLength(84) });
            foreach (var (caption, column) in new[] { ("First name", 1), ("Last name", 2), ("Roles", 3) }) { var label = WorkflowText(caption, 12); Grid.SetColumn(label, column); playerHeadings.Children.Add(label); }
            playerHeadings.ColumnDefinitions[3].Width = new GridLength(104);
            var rolesHeading = playerHeadings.Children.OfType<TextBlock>().Single(label => Grid.GetColumn(label) == 3);
            playerHeadings.SizeChanged += (_, _) =>
            {
                bool compact = playerHeadings.ActualWidth < 500;
                playerHeadings.ColumnDefinitions[3].Width = new GridLength(compact ? 0 : 104);
                rolesHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            };
            editors.Children.Add(playerHeadings);
            for (int index = 0; index < lineup.Count; index++)
            {
                int i = index; var player = lineup[i];
                var card = new Grid { Tag = "lineup-row-" + i, Background = TeamVisuals.Panel(Primary(team), Primary(team), .12), Margin = new Thickness(0, 2, 0, 2), AllowDrop = true };
                card.ColumnDefinitions.Add(new() { Width = new GridLength(40) }); card.ColumnDefinitions.Add(new()); card.ColumnDefinitions.Add(new()); card.ColumnDefinitions.Add(new() { Width = new GridLength(104) });
                card.RowDefinitions.Add(new() { Height = GridLength.Auto }); card.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var rowSurface = new Border { CornerRadius = new CornerRadius(14), Background = card.Background, BorderThickness = new Thickness(1), IsHitTestVisible = false };
                rowSurface.SetResourceReference(Border.BorderBrushProperty, "ControlCentre.BorderBrush");
                Grid.SetColumnSpan(rowSurface, 4); Grid.SetRowSpan(rowSurface, 2); card.Children.Add(rowSurface); card.Background = Brushes.Transparent;
                var first = WorkflowInput(player.FirstName); var last = WorkflowInput(player.LastName); Grid.SetColumn(first, 1); Grid.SetColumn(last, 2);
                foreach (var field in new[] { first, last }) { field.FontSize = 16; field.MinHeight = 42; field.Padding = new Thickness(8, 6, 8, 6); field.Margin = new Thickness(2, 3, 2, 3); }
                first.ToolTip = "First name"; last.ToolTip = "Last name";
                first.TextChanged += (_, _) => { lineup[i] = lineup[i] with { FirstName = first.Text }; RefreshRoles(); }; last.TextChanged += (_, _) => { lineup[i] = lineup[i] with { LastName = last.Text }; RefreshRoles(); };
                System.Windows.Automation.AutomationProperties.SetName(first, $"First name · player {i + 1}");
                System.Windows.Automation.AutomationProperties.SetName(last, $"Last name · player {i + 1}");
                card.Children.Add(first); card.Children.Add(last);
                var handle = WorkflowButton($"{i + 1} ⋮", () => { }, "lineup-drag"); handle.Width = 36; handle.MinWidth = 0; handle.MinHeight = 42; handle.FontSize = 13; handle.Padding = new Thickness(0); handle.Margin = new Thickness(2); handle.Background = Brushes.Transparent; handle.BorderThickness = new Thickness(0); handle.Cursor = System.Windows.Input.Cursors.SizeAll;
                handle.ToolTip = "Drag to reorder · Alt+↑ / Alt+↓ also moves this player"; System.Windows.Automation.AutomationProperties.SetName(handle, $"Reorder {player.FirstName} {player.LastName}");
                System.Windows.Automation.AutomationProperties.SetHelpText(handle, "Drag to reorder. Use Alt+Up or Alt+Down to move this player with the keyboard.");
                handle.PreviewKeyDown += (_, e) => { var key = e.Key == Key.System ? e.SystemKey : e.Key; if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && key is Key.Up or Key.Down) { Move(team, i, i + (key == Key.Up ? -1 : 1)); e.Handled = true; } };
                card.Children.Add(handle);
                var roles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Width = 104, HorizontalAlignment = System.Windows.HorizontalAlignment.Left }; Grid.SetColumn(roles, 3); card.Children.Add(roles);
                bool? stacked = null;
                card.SizeChanged += (_, _) =>
                {
                    bool compact = card.ActualWidth < 500; if (stacked == compact) return; stacked = compact;
                    card.ColumnDefinitions[3].Width = new GridLength(compact ? 0 : 104);
                    Grid.SetColumn(roles, compact ? 1 : 3); Grid.SetRow(roles, compact ? 1 : 0); Grid.SetColumnSpan(roles, compact ? 2 : 1);
                };
                var keeper = WorkflowButton("WK", () => { lineup[i] = lineup[i] with { IsWicketkeeper = !lineup[i].IsWicketkeeper }; RefreshRoles(); }, "lineup-keeper");
                var captain = WorkflowButton("C", () => { bool set = !lineup[i].IsCaptain; for (int p = 0; p < lineup.Count; p++) lineup[p] = lineup[p] with { IsCaptain = set && p == i }; RefreshRoles(); }, "lineup-captain");
                roleControls.Add((keeper,team,i,true));roleControls.Add((captain,team,i,false));
                foreach (var (role, selected, caption, colour) in new[] { (keeper, player.IsWicketkeeper, "Wicketkeeper", "#75D9B0"), (captain, player.IsCaptain, "Captain", "#F0CB78") })
                {
                    role.Style = (Style)FindResource("CricketLineupRoleButton");
                    role.MinWidth = 0; role.MinHeight = 42; role.Height = 42; role.Width = 46; role.Padding = new Thickness(0); role.Margin = new Thickness(2); role.FontSize = 13; role.FontWeight = FontWeights.Bold;
                    role.ToolTip = caption + (selected ? " · selected" : ""); role.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colour)!;
                    role.Background = selected ? role.BorderBrush : Brushes.Transparent; role.Foreground = selected ? Brushes.Black : role.BorderBrush;
                    System.Windows.Automation.AutomationProperties.SetName(role, caption + " · " + player.FirstName + " " + player.LastName); System.Windows.Automation.AutomationProperties.SetItemStatus(role, selected ? "Selected" : "Not selected"); roles.Children.Add(role);
                }
                var preview = new Border { Tag = "lineup-insertion-preview", Height = 3, Background = Brushes.DeepSkyBlue, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                Grid.SetColumnSpan(preview, 4); Grid.SetRowSpan(preview, 2); System.Windows.Controls.Panel.SetZIndex(preview, 10); card.Children.Add(preview); dropPreviews.Add(preview);
                handle.PreviewMouseLeftButtonDown += (_, e) => dragOrigin = e.GetPosition(body);
                handle.PreviewMouseLeftButtonUp += (_, _) => dragOrigin = null;
                handle.PreviewMouseMove += (_, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || dragOrigin is not { } start) return;
                    var distance = e.GetPosition(body) - start;
                    if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    dragOrigin = null; dragging = i; card.Opacity = .55;
                    try { DragDrop.DoDragDrop(card, "cricket-lineup-" + team, DragDropEffects.Move); }
                    finally { card.Opacity = 1; ClearDropPreview(); dragging = -1; }
                };
                card.PreviewDragOver += (_, e) =>
                {
                    bool valid = dragging >= 0 && e.Data.GetDataPresent(typeof(string)) && (string?)e.Data.GetData(typeof(string)) == "cricket-lineup-" + team;
                    e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None; ClearDropPreview();
                    if (valid) { preview.VerticalAlignment = e.GetPosition(card).Y < card.ActualHeight / 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom; preview.Visibility = Visibility.Visible; }
                    e.Handled = true;
                };
                card.PreviewDragLeave += (_, e) => { var point = e.GetPosition(card); if (point.X < 0 || point.Y < 0 || point.X > card.ActualWidth || point.Y > card.ActualHeight) preview.Visibility = Visibility.Collapsed; };
                card.PreviewDrop += (_, e) =>
                {
                    if (dragging >= 0 && (string?)e.Data.GetData(typeof(string)) == "cricket-lineup-" + team)
                    { int insertion = i + (e.GetPosition(card).Y >= card.ActualHeight / 2 ? 1 : 0); Move(team, dragging, insertion > dragging ? insertion - 1 : insertion); }
                    ClearDropPreview(); e.Handled = true;
                };
                editors.Children.Add(card);
            }
            RefreshRoles();
            editors.Children.Add(WorkflowButton("+ Add player", () =>
            {
                var focus = Keyboard.FocusedElement;
                lineup.Add(new() { Index = -1, FirstName = "Player", LastName = (lineup.Count + 1).ToString(), BattingPosition = lineup.Count + 1 });
                Render(); FocusRow(team, lineup.Count - 1, true, focus);
            }));
        }
        for (int index = 0; index < 2; index++)
        {
            int team = index; var tab = WorkflowButton("", () => { if (active == team) return; active = team; Render(); }, team == 0 ? "edit-team-home" : "edit-team-away"); tab.Style = (Style)FindResource("CricketScorecardTeamButton"); tab.Height = 66; tab.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch; tab.MinWidth = 0;
            tabButtons[team] = tab; Grid.SetColumn(tab, team); tabs.Children.Add(tab);
            names[team].TextChanged += (_, _) => RefreshTabs();
        }
        Render();
        bool RejectLineup(string message,int team=-1,FrameworkElement? field=null,int player=-1)
        {
            if(team >= 0 && active != team){active=team;Render();}
            if(player >= 0) field = editors.Children.OfType<Grid>().FirstOrDefault(row=>Equals(row.Tag,"lineup-row-"+player))?.Children.OfType<TextBox>().FirstOrDefault(input=>Grid.GetColumn(input)==1);
            var target = field ?? validation;validation.Text = message;validationCard.Visibility = Visibility.Visible;
            int generation = ++validationGeneration, render = renderGeneration;var priorFocus = Keyboard.FocusedElement;
            string? rejectedText = (target as TextBox)?.Text;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(() =>
            {
                if(generation != validationGeneration || render != renderGeneration || lineupGeneration != _workflowGeneration
                    || !ReferenceEquals(match,_match) || validation.Text != message || !ReferenceEquals(priorFocus,Keyboard.FocusedElement)
                    || target is TextBox current && current.Text != rejectedText || !_workflowBusy || !body.IsLoaded || !body.IsVisible
                    || !target.IsLoaded || !target.IsVisible || !target.IsEnabled || lineupOwner is not {IsActive:true}
                    || !ReferenceEquals(Window.GetWindow(body),lineupOwner)) return;
                OperatorViewport.RevealValidationError(target);Keyboard.Focus(target);
            }));
            return false;
        }
        bool ValidateLineupDraft()
        {
            if(!CurrentRoster()) return RejectLineup("The teams changed outside this draft. Close and reopen Edit teams before saving. Your draft has not replaced the newer teams.");
            if(string.IsNullOrWhiteSpace(names[0].Text))return RejectLineup("Enter a home team name.",0,names[0]);
            if(string.IsNullOrWhiteSpace(names[1].Text))return RejectLineup("Enter an away team name.",1,names[1]);
            if(string.Equals((names[0].Text ?? "").Trim(),(names[1].Text ?? "").Trim(),StringComparison.OrdinalIgnoreCase))return RejectLineup("Enter two distinct team names.",active,names[active]);
            for(int team=0;team<2;team++)
            {
                if(string.IsNullOrWhiteSpace(abbreviations[team].Text))return RejectLineup("Enter a team abbreviation.",team,abbreviations[team]);
                int unnamed = players[team].FindIndex(player=>string.IsNullOrWhiteSpace(player.FirstName+player.LastName));
                if(unnamed >= 0)return RejectLineup("Enter a name for player "+(unnamed+1)+".",team,player:unnamed);
            }
            // Reuse the authoritative model validation on detached containers.
            // Never run a live commit while the invalid form is still open.
            var trial = new CricketMatchManager();trial.LoadState(match.CapturePersistenceState());
            var home = players[0].Select((p,i)=>p with {BattingPosition=i+1}).ToArray();
            var away = players[1].Select((p,i)=>p with {BattingPosition=i+1}).ToArray();
            if(!trial.TryUpdateTeams(names[0].Text ?? "",abbreviations[0].Text ?? "",home,names[1].Text ?? "",abbreviations[1].Text ?? "",away,out string error))return RejectLineup(error);
            ClearValidation();return true;
        }
        while (ReferenceEquals(match, _match))
        {
            string? answer = await ValidatedFormAsync("Edit teams", "Changes in both tabs stay in this draft until you save. Set the batting order, captain and wicketkeepers for each team.", body, ValidateLineupDraft, new("back", "Back to match options"), new("close", "Close"), new("save", "Save both teams", true));
            if (answer == "back") return true;
            if (answer != "save") return false;
            if(!ReferenceEquals(match,_match) || lineupGeneration != _workflowGeneration)return false;
            if(!CurrentRoster()){RejectLineup("The teams changed outside this draft. Close and reopen Edit teams before saving. Your draft has not replaced the newer teams.");continue;}
            var home = players[0].Select((p, i) => p with { BattingPosition = i + 1 }).ToArray();
            var away = players[1].Select((p, i) => p with { BattingPosition = i + 1 }).ToArray();
            if (match.TryUpdateTeams(names[0].Text ?? "", abbreviations[0].Text ?? "", home, names[1].Text ?? "", abbreviations[1].Text ?? "", away, out string error)) return false;
            RejectLineup(error);
        }
        return false;
    }
}
