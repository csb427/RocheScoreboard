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
        _workflowBusy = false; _deliveryPointerGuardUntil = DateTime.UtcNow.AddMilliseconds(220);
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
        if (match.CanContinueCurrentInnings && match.AllInnings.Count > 1) choices.Add(new("continue", "Continue this innings"));
        if (match.AllInnings.Count < 4)
        {
            int aCount = match.AllInnings.Count(i => i.BattingTeamName == match.TeamAName), bCount = match.AllInnings.Count(i => i.BattingTeamName == match.TeamBName);
            if (aCount < 2 && (aCount == 0 || bCount > 0)) choices.Add(new("a", $"Start {match.TeamAName}'s next innings", !finishRecommended));
            if (bCount < 2 && (bCount == 0 || aCount > 0)) choices.Add(new("b", $"Start {match.TeamBName}'s next innings", !finishRecommended));
        }
        if (match.AllInnings.Count > 1) choices.Add(new("finish", "End game",Primary:finishRecommended,Destructive:!finishRecommended));
        string message = automatic ? "Choose what happens next. The score and ball history are preserved." : "End this innings, start another innings, or finish the game.";
        if (!innings.IsComplete && match.Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled < match.CurrentOverLimit * 6)
            message = $"This innings still has {(match.CurrentOverLimit * 6 - innings.LegalBallsBowled) / 6}.{(match.CurrentOverLimit * 6 - innings.LegalBallsBowled) % 6} overs available. Starting another innings will close it early.\n\n" + message;
        string? answer = await ChooseAsync(automatic ? match.CompletionReason : "Innings options", message, choices.ToArray());
        if (!Current()) { if (answer is "a" or "b" or "continue" or "finish") await ExplainChangedInnings(); return false; }
        if (answer == "back") return fromOptions;
        if (answer is "a" or "b")
        {
            if (!innings.IsComplete && await ChooseAsync("Close this innings early?", "The existing deliveries remain in the scorecard. You can review them later.", new("back", "Go back"), new("yes", "Close innings and continue", true)) != "yes") return false;
            if (!Current()) { await ExplainChangedInnings(); return false; }
            if (match.TryStartAdditionalInnings(answer == "a", out string error)) { _completionShown = null; _cpTopBatterIndex = _cpBottomBatterIndex = -1; TriggerInitialSelection(); }
            else await ShowErrorAsync("Unable to start innings", error);
        }
        else if (answer == "continue")
        {
            if (await ChooseAsync("Continue beyond the usual finish?", "The current target and over limit will no longer stop this innings. All-out still ends it.", new("back", "Go back"), new("yes", "Continue scoring", true)) != "yes") return false;
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
        while (ReferenceEquals(match, _match))
        {
            var actions = new List<CricketPrompt.Choice> { new("close", "Close"), new("teams", "Edit teams") };
            if (!match.IsMatchFinalized) actions.Add(new("break", match.BreakKind == CricketBreakKind.None ? "Take a break / rain delay" : "Change the break"));
            if (!match.IsMatchFinalized) actions.Add(new("conditions", "Format and over limit"));
            if (!match.TossPending && !match.IsMatchFinalized && match.Format == CricketFormat.LimitedOvers) actions.Add(new("rain", "Duckworth-Lewis"));
            if (!match.TossPending && !match.IsMatchFinalized) actions.Add(new("innings", "Innings and game options"));
            if (!match.TossPending && !match.IsMatchFinalized) actions.Add(new("before-delivery", "Non-striker run out before delivery"));
            actions.Add(new("outcome", match.IsMatchFinalized ? "Review outcome / reopen match" : "End match / record outcome"));
            string? answer = await ChooseAsync("Match options", "Manage breaks, teams and playing conditions.", actions.ToArray());
            if (!ReferenceEquals(match, _match)) return;
            if (answer == "break")
            {
                var choices = Enum.GetValues<CricketBreakKind>().Where(k => k != CricketBreakKind.None).Select(k => new CricketPrompt.Choice(k.ToString(), k == CricketBreakKind.Rain ? "Rain delay" : k + " break"))
                    .Prepend(new CricketPrompt.Choice("back", "Back to match options")).Prepend(new CricketPrompt.Choice("close", "Close")).ToArray();
                string? kind = await ChooseAsync("Break in play", "Scoring pauses. Click the break panel to resume when play is ready.", choices);
                if (kind == "back") continue;
                if (Enum.TryParse(kind, out CricketBreakKind parsed)) match.SetBreak(parsed);
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
        string? action = replaceDirect ? "replace" : await ChooseAsync(innings.BattingOrder[playerIndex].FullName, "Change the batter without changing historical deliveries.", new("back", "Return"), new("replace", "Replace batter"), new("strike", "Swap strike"));
        if (!Current()) return;
        if (action == "strike") { match.RotateStrike(); return; }
        if (action != "replace") return;
        var candidates = innings.BattingOrder.Select((p, i) => (Player: p, Index: i)).Where(p => p.Index != innings.StrikerIndex && p.Index != innings.NonStrikerIndex && !p.Player.IsOut).ToArray();
        if (candidates.Length == 0) { await ShowErrorAsync("No replacement available", "Every remaining player is already batting or out. Review the scorecard or line-up."); return; }
        string? selected = await ChooseAsync("Select replacement", "The new batter keeps their own innings statistics unless you explicitly transfer a mistaken selection.", candidates.Select(p => new CricketPrompt.Choice(p.Index.ToString(), p.Player.FullName)).Prepend(new CricketPrompt.Choice("back", "Go back")).ToArray());
        if (!Current()) return;
        if (!int.TryParse(selected, out int replacement)) return;
        string? reason = await ChooseAsync("Why is the batter changing?", "This records the departure accurately in the scorecard.", new("back", "Go back"), new("RetiredHurt", "Retired hurt"), new("RetiredNotOut", "Retired not out"), new("RetiredOut", "Retired out"), new("WrongPlayer", "Wrong player was selected"));
        if (!Current()) return;
        if (!Enum.TryParse(reason, out CricketBatterReplacementReason parsedReason)) return;
        bool carry = false;
        if (parsedReason == CricketBatterReplacementReason.WrongPlayer)
        {
            string? transfer = await ChooseAsync("Correct the player selection", "Choose whether the runs and balls recorded for the wrong player belong to the replacement.", new("back", "Go back"), new("carry", "Transfer recorded figures", true), new("reset", "Keep figures with original player"));
            if (!Current()) return;
            if (transfer is null or "back") return; carry = transfer == "carry";
        }
        if (!match.TryReplaceBatter(striker, replacement, parsedReason, carry, out string error)) await ShowErrorAsync("Unable to replace batter", error);
    }

    private async Task<bool> OpenLineupAsync(bool teamA)
    {
        var match = _match; if (match is null) return false;
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
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"Not selected");
            }
        }
        int active = teamA ? 0 : 1, dragging = -1;
        System.Windows.Point? dragOrigin = null;
        var dropPreviews = new List<Border>();
        void ClearDropPreview() { foreach (var preview in dropPreviews) preview.Visibility = Visibility.Collapsed; }
        var body = new StackPanel(); var tabs = new Grid(); tabs.ColumnDefinitions.Add(new()); tabs.ColumnDefinitions.Add(new()); body.Children.Add(tabs);
        var editors = new StackPanel(); body.Children.Add(editors);
        var tabButtons = new Button[2];
        string Primary(int team) => team == 0 ? match.TeamAPrimaryColor : match.TeamBPrimaryColor;
        void RefreshTabs()
        {
            for (int t = 0; t < 2; t++)
            {
                tabButtons[t].Content = (t == 0 ? "HOME · " : "AWAY · ") + names[t].Text;
                tabButtons[t].Background = CricketBroadcastStyle.Gradient(Primary(t), null);
                tabButtons[t].BorderBrush = t == active ? Brushes.LightSkyBlue : Brushes.SlateGray;
                tabButtons[t].BorderThickness = new Thickness(t == active ? 2 : 1); tabButtons[t].Opacity = t == active ? 1 : .72;
                System.Windows.Automation.AutomationProperties.SetItemStatus(tabButtons[t], t == active ? "Selected" : "");
            }
        }
        void Move(int team, int from, int to)
        {
            var lineup = players[team]; if (from == to || from < 0 || to < 0 || from >= lineup.Count || to >= lineup.Count) return;
            var player = lineup[from]; lineup.RemoveAt(from); lineup.Insert(to, player); Render();
            Dispatcher.BeginInvoke(new Action(() => editors.Children.OfType<Grid>().FirstOrDefault(row => Equals(row.Tag, "lineup-row-" + to))?.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, "lineup-drag"))?.Focus()));
        }
        void Render()
        {
            editors.Children.Clear(); dropPreviews.Clear();roleControls.Clear(); RefreshTabs(); int team = active; var lineup = players[team];
            editors.Children.Add(WorkflowText("Team name")); editors.Children.Add(names[team]); editors.Children.Add(WorkflowText("Abbreviation")); editors.Children.Add(abbreviations[team]);
            editors.Children.Add(WorkflowText("Drag the handle to reorder · WK = wicketkeeper, C = captain.", 13));
            var playerHeadings = new Grid(); playerHeadings.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); playerHeadings.ColumnDefinitions.Add(new()); playerHeadings.ColumnDefinitions.Add(new()); playerHeadings.ColumnDefinitions.Add(new() { Width = new GridLength(84) });
            foreach (var (caption, column) in new[] { ("First name", 1), ("Last name", 2), ("Roles", 3) }) { var label = WorkflowText(caption, 12); Grid.SetColumn(label, column); playerHeadings.Children.Add(label); }
            editors.Children.Add(playerHeadings);
            for (int index = 0; index < lineup.Count; index++)
            {
                int i = index; var player = lineup[i];
                var card = new Grid { Tag = "lineup-row-" + i, Background = TeamVisuals.Panel(Primary(team), Primary(team), .12), Margin = new Thickness(0, 2, 0, 2), AllowDrop = true };
                card.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); card.ColumnDefinitions.Add(new()); card.ColumnDefinitions.Add(new()); card.ColumnDefinitions.Add(new() { Width = new GridLength(84) });
                var first = WorkflowInput(player.FirstName); var last = WorkflowInput(player.LastName); Grid.SetColumn(first, 1); Grid.SetColumn(last, 2);
                foreach (var field in new[] { first, last }) { field.FontSize = 15; field.MinHeight = 36; field.Padding = new Thickness(6, 5, 6, 5); field.Margin = new Thickness(2, 3, 2, 3); }
                first.ToolTip = "First name"; last.ToolTip = "Last name";
                first.TextChanged += (_, _) => lineup[i] = lineup[i] with { FirstName = first.Text }; last.TextChanged += (_, _) => lineup[i] = lineup[i] with { LastName = last.Text };
                card.Children.Add(first); card.Children.Add(last);
                var handle = WorkflowButton($"{i + 1} ⋮", () => { }, "lineup-drag"); handle.Width = 30; handle.MinWidth = 0; handle.FontSize = 11; handle.Padding = new Thickness(0); handle.Margin = new Thickness(2); handle.Background = Brushes.Transparent; handle.BorderThickness = new Thickness(0); handle.Cursor = System.Windows.Input.Cursors.SizeAll;
                handle.ToolTip = "Drag to reorder · Alt+↑ / Alt+↓ also moves this player"; System.Windows.Automation.AutomationProperties.SetName(handle, $"Reorder {player.FirstName} {player.LastName}");
                System.Windows.Automation.AutomationProperties.SetHelpText(handle, "Drag to reorder. Use Alt+Up or Alt+Down to move this player with the keyboard.");
                handle.PreviewKeyDown += (_, e) => { var key = e.Key == Key.System ? e.SystemKey : e.Key; if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && key is Key.Up or Key.Down) { Move(team, i, i + (key == Key.Up ? -1 : 1)); e.Handled = true; } };
                card.Children.Add(handle);
                var roles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; Grid.SetColumn(roles, 3); card.Children.Add(roles);
                var keeper = WorkflowButton("WK", () => { lineup[i] = lineup[i] with { IsWicketkeeper = !lineup[i].IsWicketkeeper }; RefreshRoles(); }, "lineup-keeper");
                var captain = WorkflowButton("C", () => { bool set = !lineup[i].IsCaptain; for (int p = 0; p < lineup.Count; p++) lineup[p] = lineup[p] with { IsCaptain = set && p == i }; RefreshRoles(); }, "lineup-captain");
                roleControls.Add((keeper,team,i,true));roleControls.Add((captain,team,i,false));
                foreach (var (role, selected, caption, colour) in new[] { (keeper, player.IsWicketkeeper, "Wicketkeeper", "#75D9B0"), (captain, player.IsCaptain, "Captain", "#F0CB78") })
                {
                    role.MinWidth = 0; role.MinHeight = 32; role.Height = 32; role.Width = 36; role.Padding = new Thickness(0); role.Margin = new Thickness(2); role.FontSize = 13; role.FontWeight = FontWeights.Bold;
                    role.ToolTip = caption + (selected ? " · selected" : ""); role.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colour)!;
                    role.Background = selected ? role.BorderBrush : Brushes.Transparent; role.Foreground = selected ? Brushes.Black : role.BorderBrush;
                    System.Windows.Automation.AutomationProperties.SetName(role, caption + " · " + player.FirstName + " " + player.LastName); System.Windows.Automation.AutomationProperties.SetItemStatus(role, selected ? "Selected" : "Not selected"); roles.Children.Add(role);
                }
                var preview = new Border { Tag = "lineup-insertion-preview", Height = 3, Background = Brushes.DeepSkyBlue, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                Grid.SetColumnSpan(preview, 4); System.Windows.Controls.Panel.SetZIndex(preview, 10); card.Children.Add(preview); dropPreviews.Add(preview);
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
            editors.Children.Add(WorkflowButton("+ Add player", () => { lineup.Add(new() { Index = -1, FirstName = "Player", LastName = (lineup.Count + 1).ToString(), BattingPosition = lineup.Count + 1 }); Render(); }));
        }
        for (int index = 0; index < 2; index++)
        {
            int team = index; var tab = WorkflowButton("", () => { active = team; Render(); }, team == 0 ? "edit-team-home" : "edit-team-away"); tab.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch; tab.MinWidth = 0;
            tabButtons[team] = tab; Grid.SetColumn(tab, team); tabs.Children.Add(tab);
            names[team].TextChanged += (_, _) => RefreshTabs();
        }
        Render();
        while (ReferenceEquals(match, _match))
        {
            string? answer = await FormAsync("Edit teams", "Changes in both tabs stay in this draft until you save. Set the batting order, captain and wicketkeepers for each team.", body, new("back", "Back to match options"), new("close", "Close"), new("save", "Save both teams", true));
            if (answer == "back") return true;
            if (answer != "save") return false;
            var home = players[0].Select((p, i) => p with { BattingPosition = i + 1 }).ToArray();
            var away = players[1].Select((p, i) => p with { BattingPosition = i + 1 }).ToArray();
            if (match.TryUpdateTeams(names[0].Text, abbreviations[0].Text, home, names[1].Text, abbreviations[1].Text, away, out string error)) return false;
            await ShowErrorAsync("Check the teams", error);
        }
        return false;
    }
}
