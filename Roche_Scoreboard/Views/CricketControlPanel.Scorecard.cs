using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private CricketScorecardDraft? _scorecardDraft;
    private bool _scorecardDirty;
    private int _scorecardInnings;
    private bool _scorecardBowling;
    private readonly StackPanel _scorecardRows = new();
    private readonly WrapPanel _scorecardInningsButtons = new(), _scorecardTeamButtons = new();
    private readonly System.Windows.Controls.TextBlock _scorecardContext = new()
    {
        Tag = "scorecard-context", FontSize = 12, FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 0, 0, 6)
    };
    private readonly System.Windows.Controls.TextBlock _scorecardStatus = WorkflowText("Select an innings to view its scorecard.", 13);
    private Grid? _scorecardLayout;
    private bool? _scorecardCompact;
    private bool? _scorecardSummaryNarrow;
    private bool _scorecardFowEditing;
    private (int Innings, int Revision, int MatchBalls, string Message)? _scorecardAppliedFeedback;
    private readonly Dictionary<(bool Bowling, int Index), Grid> _scorecardPlayerRows = new();
    private ScrollViewer? _scorecardScroll;
    private int _scorecardRenderVersion;
    internal int ScorecardRenderCount => _scorecardRenderVersion;
    private readonly Dictionary<bool,(UIElement[] Children,Dictionary<(bool Bowling,int Index),Grid> Rows)> _scorecardViewCache=new();
    private CricketScorecardDraft? _scorecardCachedDraft;
    private int _scorecardCachedWidthClass;
    private string _scorecardMetadata="";
    private void InitializeScorecard()
    {
        var layout = new Grid { Margin = new Thickness(12) }; _scorecardLayout = layout;
        // The selected-tab host supplies the available height. One existing
        // page scroller keeps headings and every figure reachable at short sizes.
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _scorecardContext.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_scorecardContext, System.Windows.Automation.AutomationLiveSetting.Polite);
        layout.Children.Add(_scorecardContext);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_scorecardStatus, System.Windows.Automation.AutomationLiveSetting.Polite);
        _scorecardInningsButtons.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; _scorecardInningsButtons.Margin = new Thickness(0, 0, 0, 3);
        content.Children.Add(_scorecardInningsButtons);
        content.Children.Add(_scorecardStatus);
        var teamCaption = WorkflowText("BATTING / BOWLING", 11); teamCaption.TextAlignment = TextAlignment.Center;
        content.Children.Add(teamCaption);
        var teamNavigation = new OperatorGlassSurface
        {
            Material = GlassMaterial.Regular, GlassSource = CricketRoot, CornerRadius = new CornerRadius(26),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(76, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(130, 30, 34, 43)), Padding = new Thickness(4),
            Margin = new Thickness(0, 4, 0, 8), Child = _scorecardTeamButtons
        };
        content.Children.Add(teamNavigation); content.Children.Add(_scorecardRows);
        var scroll = new ScrollViewer { Tag = "scorecard-rows-scroll", Content = content, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _scorecardScroll = scroll;
        _scorecardTeamButtons.SizeChanged += (_, _) => SizeScorecardTeamTabs();
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll); ScorecardHost.Content = layout;
        SizeChanged += (_, _) => { BoundScorecardViewport(); bool compact = ActualWidth < 820, summaryNarrow = ActualWidth < 760; if (_scorecardCompact != compact || _scorecardSummaryNarrow != summaryNarrow) { _scorecardCompact = compact; _scorecardSummaryNarrow = summaryNarrow; if (_scorecardDraft is not null) RenderScorecard(); } };
        PersistentHeader.SizeChanged += (_, _) => BoundScorecardViewport();
        BoundScorecardViewport();
    }
    private void BoundScorecardViewport()
    {
        if (_scorecardLayout is null || ActualHeight <= 0) return;
        _scorecardStatus.MinHeight = ActualWidth < 620 ? 36 : 20;
        // Keep normal text and controls inside the tab's actual allocation.
        // Innings and instructions precede the table in the same page scroller;
        // the match strip and navigation dock remain outside and anchored.
    }
    private void ResetScorecard() { _scorecardContext.Text = ""; _scorecardContext.ToolTip = null; System.Windows.Automation.AutomationProperties.SetName(_scorecardContext, ""); _scorecardViewCache.Clear();_scorecardCachedDraft=null;_scorecardMetadata=""; _scorecardDraft = null; _scorecardDirty = false; _scorecardCommitBusy = false; _scorecardFowEditing = false; _scorecardAppliedFeedback = null; _scorecardInnings = 0; _scorecardPlayerRows.Clear(); _scorecardRows.Children.Clear(); _scorecardInningsButtons.Children.Clear(); _scorecardTeamButtons.Children.Clear(); }
    private void SizeScorecardTeamTabs()
    {
        double width = Math.Max(100, (_scorecardTeamButtons.ActualWidth - 12) / 2);
        foreach (var tab in _scorecardTeamButtons.Children.OfType<Button>()) tab.Width = width;
    }
    private void RefreshScorecard(bool force = false)
    {
        if (_match is not { } match || match.AllInnings.Count == 0) return;
        _scorecardInnings = Math.Clamp(_scorecardInnings == 0 ? match.CurrentInningsNumber : _scorecardInnings, 1, match.AllInnings.Count);
        string metadata=string.Join("|",match.TeamAName,match.TeamBName,match.TeamAPrimaryColor,match.TeamBPrimaryColor,
            string.Join(";",match.AllInnings[_scorecardInnings-1].BattingOrder.Select(player=>player.FullName)),
            string.Join(";",match.AllInnings[_scorecardInnings-1].BowlingAttack.Select(player=>player.FullName)));
        if(!force && _scorecardDraft is { } existing && existing.InningsNumber==_scorecardInnings &&
            existing.Revision==match.DeliveryRevision && existing.DeliveryCount==match.AllInnings[_scorecardInnings-1].Deliveries.Count &&
            existing.MatchDeliveryCount==match.AllInnings.Sum(i=>i.Deliveries.Count) && _scorecardRows.Children.Count>0 && metadata==_scorecardMetadata)
        { UpdateScorecardStatus(); return; }
        if (_scorecardDraft is null || force || !_scorecardDirty) _scorecardDraft = match.CreateScorecardDraft(_scorecardInnings);
        _scorecardMetadata=metadata;
        _scorecardInningsButtons.Children.Clear();
        foreach (var innings in match.AllInnings)
        {
            int number = innings.InningsNumber;
            string ordinal = number switch { 1 => "1ST", 2 => "2ND", 3 => "3RD", _ => "4TH" };
            var button = WorkflowButton(ordinal + " INNINGS", async () =>
            {
                if (_scorecardDirty && number != _scorecardInnings)
                {
                    if (await ChooseAsync("Apply the pending row first?", "The current innings has an unfinished inline edit. Apply it before changing innings.", new("close", "Keep editing"), new("apply", "Apply pending edit", true)) != "apply" || _scorecardDraft is not { } pending || !await CommitScorecardEditAsync(pending)) return;
                }
                _scorecardInnings = number; _scorecardDirty = false; RefreshScorecard(true);
            });
            button.Tag = "scorecard-innings-" + number; button.ToolTip = innings.BattingTeamName + " batting"; button.MinWidth = 100; button.MinHeight = 40; button.FontSize = 13; button.FontWeight = FontWeights.Bold; button.Padding = new Thickness(10, 5, 10, 5);
            button.BorderBrush = number == _scorecardInnings ? Brushes.LightSkyBlue : Brushes.SlateGray;
            button.BorderThickness = new Thickness(1);
            button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, number == _scorecardInnings ? "ControlCentre.SelectionBrush" : "ControlCentre.SurfaceBrush");
            _scorecardInningsButtons.Children.Add(button);
        }
        var current = match.AllInnings[_scorecardInnings - 1]; _scorecardTeamButtons.Children.Clear();
        var batting = WorkflowButton(current.BattingTeamName, () => SelectScorecardTeam(false), "scorecard-team-batting");
        var bowling = WorkflowButton(current.BowlingTeamName, () => SelectScorecardTeam(true), "scorecard-team-bowling");
        batting.ToolTip = current.BattingTeamName + " · Batting"; bowling.ToolTip = current.BowlingTeamName + " · Bowling";
        batting.Background = CricketBroadcastStyle.Gradient(current.BattingTeamName == match.TeamAName ? match.TeamAPrimaryColor : match.TeamBPrimaryColor, null);
        bowling.Background = CricketBroadcastStyle.Gradient(current.BowlingTeamName == match.TeamAName ? match.TeamAPrimaryColor : match.TeamBPrimaryColor, null);
        foreach (var (tab, name, role) in new[] { (batting, current.BattingTeamName, "BATTING"), (bowling, current.BowlingTeamName, "BOWLING") })
        {
            var label = new StackPanel(); label.Children.Add(new System.Windows.Controls.TextBlock { Text = role, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Opacity = .75, TextAlignment = TextAlignment.Center });
            label.Children.Add(new System.Windows.Controls.TextBlock { Text = name, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            var navigation = new Grid(); navigation.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); navigation.ColumnDefinitions.Add(new());
            navigation.Children.Add(new OperatorSymbol { Kind = role == "BATTING" ? "Cricket" : "Teams", Width = 22, Height = 22, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(label, 1); navigation.Children.Add(label);
            tab.Content = navigation; tab.ContentTemplate = null; tab.Height = 66; tab.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        }
        _scorecardTeamButtons.Children.Add(batting); _scorecardTeamButtons.Children.Add(bowling);
        SizeScorecardTeamTabs();
        RenderScorecard();
    }
    private void SelectScorecardTeam(bool bowling)
    {
        if(_scorecardBowling==bowling)return;
        _scorecardBowling=bowling; RenderScorecardView(true);
        SharedSurfaceMotion.Reveal(_scorecardRows);
    }
    private void UpdateScorecardStatus()
    {
        if (_scorecardDraft is null || _match is null) return;
        var selectedInnings = _match.AllInnings[_scorecardDraft.InningsNumber - 1];
        bool stale = _scorecardDraft.Revision != _match.DeliveryRevision
            || _scorecardDraft.DeliveryCount != selectedInnings.Deliveries.Count
            || _scorecardDraft.MatchDeliveryCount != _match.AllInnings.Sum(innings => innings.Deliveries.Count);
        if (_scorecardAppliedFeedback is { } feedback && (feedback.Innings != _scorecardDraft.InningsNumber || feedback.Revision != _match.DeliveryRevision || feedback.MatchBalls != _match.AllInnings.Sum(innings => innings.Deliveries.Count))) _scorecardAppliedFeedback = null;
        if (stale && !_scorecardDirty && !_workflowBusy && !_scorecardFowEditing && MainTabControl.SelectedItem == ScorecardTab) { RefreshScorecard(true); return; }
        string previousStatus = _scorecardStatus.Text;
        _scorecardStatus.Text = _scorecardDirty ? stale ? "The match changed. Reopen this edit to use current figures." : "Complete the inline row and press Apply."
            : !string.IsNullOrWhiteSpace(selectedInnings.ManualScorecardWarning) ? "Manual figures have reconciliation differences · review adjustments below."
            : _scorecardAppliedFeedback?.Message ?? "Hover and click a figure to edit. Apply updates the match immediately.";
        _scorecardStatus.ToolTip = selectedInnings.ManualScorecardWarning;
        _scorecardStatus.FontWeight = FontWeights.SemiBold;
        if (stale && _scorecardDirty) _scorecardStatus.Foreground = ScorecardAlertBrush(true);
        else if (_scorecardDirty || !string.IsNullOrWhiteSpace(selectedInnings.ManualScorecardWarning)) _scorecardStatus.Foreground = ScorecardAlertBrush(false);
        else _scorecardStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ControlCentre.AccentBrush");
        if (previousStatus != _scorecardStatus.Text && System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged))
        {
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(_scorecardStatus) ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(_scorecardStatus);
            peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }
    private Button Figure(string label, Func<int> read, Action<int> write, bool overs = false, bool compact = false)
    {
        string value = overs ? $"{read() / 6}.{read() % 6}" : read().ToString(CultureInfo.InvariantCulture);
        var button = WorkflowButton(compact ? value : label + "  " + value, async () =>
        {
            var draft = _scorecardDraft; var match = _match; int generation = _workflowGeneration;
            if (draft is null || match is null) return;
            bool Current() => generation == _workflowGeneration && ReferenceEquals(match, _match) && ReferenceEquals(draft, _scorecardDraft);
            var editor = CreateScorecardFigureEditor(label, value, overs, out var field);
            var error = WorkflowText("", 14); error.Tag = "scorecard-figure-error"; error.Visibility = Visibility.Collapsed;
            error.Foreground = ScorecardAlertBrush(true); editor.Children.Add(error);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Assertive);
            int parsed = 0;
            bool Validate()
            {
                if (!Current()) return false;
                string[] parts = field.Text.Trim().Split('.');
                bool valid = !overs ? int.TryParse(field.Text, out parsed) : parts.Length <= 2 && int.TryParse(parts[0], out int complete) && complete >= 0 && complete <= 166666 && (parts.Length == 1 || int.TryParse(parts[1], out int balls) && balls is >= 0 and <= 5);
                if (valid && overs) parsed = int.Parse(parts[0], CultureInfo.InvariantCulture) * 6 + (parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
                if (valid && parsed is >= 0 and <= 1_000_000) { error.Text = ""; error.Visibility = Visibility.Collapsed; return true; }
                error.Text = "Enter a valid non-negative figure. Overs use a ball digit from 0 to 5.";
                error.Visibility = Visibility.Visible;
                field.Focus();
                if (Current()) OperatorViewport.RevealValidationError(field);
                return false;
            }
            while (Current())
            {
                if (await ValidatedFormAsync("Edit " + label.ToLowerInvariant(), overs ? "Use overs and legal balls, for example 5.3." : "Update this figure. The scorecard checks the totals before applying it.", editor, Validate, new("back", "Cancel"), new("apply", "Apply", true)) != "apply") return;
                if (!Current()) return;
                int previous = read(); bool alreadyPending = _scorecardDirty;
                write(parsed); _scorecardDirty = true;
                if (await CommitScorecardEditAsync(draft)) return;
                if (!ReferenceEquals(draft, _scorecardDraft)) return;
                write(previous); _scorecardDirty = alreadyPending; RenderScorecard();
            }
        });
        button.MinWidth = compact ? 54 : 76; if (compact) { button.Width = 54; button.FontSize = 18; button.Padding = new Thickness(2, 6, 2, 6); }
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.ToolTip = "Edit " + label;
        button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(button, label + ", " + value + ". Edit figure.");
        return button;
    }
    private void RenderScorecard()=>RenderScorecardView(false);
    private void RenderScorecardView(bool reuseViews)
    {
        var previousField = System.Windows.Input.Keyboard.FocusedElement as TextBox;
        string? focusedUid = previousField is { Tag: "scorecard-fow-entry" } ? previousField.Uid : null;
        int selectionStart = previousField?.SelectionStart ?? 0, selectionLength = previousField?.SelectionLength ?? 0;
        double scrollOffset = _scorecardScroll?.VerticalOffset ?? 0;
        int renderVersion = ++_scorecardRenderVersion;
        _scorecardRows.Children.Clear(); _scorecardPlayerRows.Clear(); if (_scorecardDraft is not { } draft || _match is null) return;
        var viewedInnings = _match.AllInnings[draft.InningsNumber - 1];
        string ordinal = draft.InningsNumber switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => draft.InningsNumber + "th" };
        // This is the viewed scorecard, which may differ from the live strip.
        // Put the role before the ellipsized team name so it stays readable.
        string context = $"Scorecard · {ordinal} innings · {(_scorecardBowling ? "Bowling" : "Batting")} · {(_scorecardBowling ? viewedInnings.BowlingTeamName : viewedInnings.BattingTeamName)}";
        _scorecardContext.Text = context; _scorecardContext.ToolTip = context;
        System.Windows.Automation.AutomationProperties.SetName(_scorecardContext, context);
        int widthClass=ActualWidth<760?0:ActualWidth<820?1:2;
        bool clean=!_scorecardDirty&&!_scorecardFowEditing;
        if(!reuseViews || !clean || !ReferenceEquals(_scorecardCachedDraft,draft) || widthClass!=_scorecardCachedWidthClass)_scorecardViewCache.Clear();
        _scorecardCachedDraft=draft;_scorecardCachedWidthClass=widthClass;
        foreach (var tab in _scorecardTeamButtons.Children.OfType<Button>())
        {
            bool selected = Equals(tab.Tag, _scorecardBowling ? "scorecard-team-bowling" : "scorecard-team-batting");
            tab.BorderBrush = selected ? Brushes.White : Brushes.Transparent;
            tab.BorderThickness = new Thickness(2); tab.Opacity = selected ? 1 : .72;
            string colour=Equals(tab.Tag,"scorecard-team-batting") ? (_match.AllInnings[draft.InningsNumber-1].BattingTeamName==_match.TeamAName?_match.TeamAPrimaryColor:_match.TeamBPrimaryColor)
                : (_match.AllInnings[draft.InningsNumber-1].BowlingTeamName==_match.TeamAName?_match.TeamAPrimaryColor:_match.TeamBPrimaryColor);
            var tint=TeamVisuals.Panel(colour,colour,.28);
            tab.Background=selected?tint:new SolidColorBrush(Color.FromRgb(32,34,40));
            if (tab.Content is Grid navigation)
                foreach (var symbol in navigation.Children.OfType<OperatorSymbol>()) symbol.IsActive = selected;
            System.Windows.Automation.AutomationProperties.SetItemStatus(tab, selected ? "Selected" : "");
        }
        if(clean && _scorecardViewCache.TryGetValue(_scorecardBowling,out var cached))
        {
            foreach(var child in cached.Children)_scorecardRows.Children.Add(child);
            foreach(var entry in cached.Rows)_scorecardPlayerRows[entry.Key]=entry.Value;
            UpdateScorecardStatus();return;
        }
        AddScorecardSummary(draft);
        bool compact = ActualWidth < 820;
        var tableTitle = WorkflowText(_scorecardBowling ? "Bowling figures" : "Batting figures", 19);
        tableTitle.Margin = new Thickness(8, 12, 8, 8); _scorecardRows.Children.Add(tableTitle);
        var headings = _scorecardBowling ? new[] { "O", "M", "R", "W", "WD", "NB", "ECON" } : new[] { "R", "B", "4s", "6s" };
        var header = new Grid { Margin = new Thickness(4, 0, 4, 0) };
        if (compact)
        {
            for (int i = 0; i < 4; i++) header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
            foreach (var _ in headings) header.ColumnDefinitions.Add(new() { Width = new GridLength(60) });
            if (!_scorecardBowling) header.ColumnDefinitions.Add(new() { Width = new GridLength(200) });
            header.Children.Add(WorkflowText("PLAYER", 12));
            for (int i = 0; i < headings.Length; i++) { var heading = WorkflowText(headings[i], 12); heading.TextAlignment = TextAlignment.Center; Grid.SetColumn(heading, i + 1); header.Children.Add(heading); }
            if (!_scorecardBowling) { var caption = WorkflowText("DISMISSAL", 12); Grid.SetColumn(caption, header.ColumnDefinitions.Count - 1); header.Children.Add(caption); }
            _scorecardRows.Children.Add(header);
        }
        var players = _scorecardBowling ? draft.Bowlers : draft.Batters;
        foreach (var player in players.OrderBy(p => p.BattingPosition))
        {
            var row = new Grid { Margin = new Thickness(4, compact ? 5 : 2, 4, compact ? 5 : 2) };
            row.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "ControlCentre.SurfaceBrush");
            int playerIndex = players.IndexOf(player); row.Tag = $"scorecard-player-{(_scorecardBowling ? "bowling" : "batting")}-{playerIndex}";
            _scorecardPlayerRows[(_scorecardBowling, playerIndex)] = row;
            row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (compact) row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            foreach (var column in header.ColumnDefinitions) row.ColumnDefinitions.Add(new() { Width = column.Width, MinWidth = column.MinWidth });
            var name = WorkflowText(player.FullName, 16); name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(8, 5, 6, 5); name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (compact) { Grid.SetColumnSpan(name, 4); name.Margin = new Thickness(12, 12, 12, 3); name.ToolTip = player.FullName; }
            row.Children.Add(name);
            var values = new List<FrameworkElement>();
            if (_scorecardBowling)
            {
                values.Add(Figure("Overs", () => player.BallsBowled, v => player.BallsBowled = v, true, true)); values.Add(Figure("Maidens", () => player.Maidens, v => player.Maidens = v, compact: true)); values.Add(Figure("Runs conceded", () => player.RunsConceded, v => player.RunsConceded = v, compact: true)); values.Add(Figure("Bowler wickets", () => player.Wickets, v => player.Wickets = v, compact: true)); values.Add(Figure("Bowler wides", () => player.Wides, v => player.Wides = v, compact: true)); values.Add(Figure("Bowler no-balls", () => player.NoBalls, v => player.NoBalls = v, compact: true));
                var econ = WorkflowText($"{player.Economy:0.00}", 15); econ.TextAlignment = TextAlignment.Center; econ.VerticalAlignment = VerticalAlignment.Center; econ.ToolTip = "Economy · calculated from balls and runs"; values.Add(econ);
            }
            else
            {
                values.Add(Figure("Runs", () => player.Runs, v => player.Runs = v, compact: true)); values.Add(Figure("Balls", () => player.BallsFaced, v => player.BallsFaced = v, compact: true)); values.Add(Figure("4s", () => player.Fours, v => player.Fours = v, compact: true)); values.Add(Figure("6s", () => player.Sixes, v => player.Sixes = v, compact: true));
                var dismissal = WorkflowButton(player.DismissalSummary, async () => await EditScorecardDismissalAsync(player), "scorecard-dismissal"); dismissal.Background = Brushes.Transparent; dismissal.BorderThickness = new Thickness(0); dismissal.MinHeight = compact ? 30 : 40; dismissal.FontSize = 13; dismissal.Margin = new Thickness(4, 0, 4, 2);
                dismissal.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
                Grid.SetRow(dismissal, compact ? 2 : 0); Grid.SetColumn(dismissal, compact ? 0 : row.ColumnDefinitions.Count - 1); if (compact) Grid.SetColumnSpan(dismissal, row.ColumnDefinitions.Count); row.Children.Add(dismissal);
            }
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (compact)
                {
                    ConfigureScorecardMetric(value, headings[i]);
                    Grid.SetColumn(value, i < 4 ? i : i - 4); Grid.SetRow(value, i < 4 ? 1 : 2);
                    if (_scorecardBowling && i == 6) Grid.SetColumnSpan(value, 2);
                }
                else
                {
                    if (value is Button button) { button.Width = double.NaN; button.MinWidth = 0; }
                    Grid.SetColumn(value, i + 1); Grid.SetRow(value, 0);
                }
                row.Children.Add(value);
            }
            _scorecardRows.Children.Add(row);
        }
        if (!_scorecardBowling)
        {
            var extrasRegion = new StackPanel { Tag = "scorecard-extras", Margin = new Thickness(4, 14, 4, 6) }; _scorecardRows.Children.Add(extrasRegion);
            extrasRegion.Children.Add(WorkflowText($"EXTRAS  ·  {draft.Wides + draft.NoBalls + draft.Byes + draft.LegByes}", 19));
            var extras = new System.Windows.Controls.Primitives.UniformGrid { Columns = ActualWidth < 760 ? 2 : 4 }; extrasRegion.Children.Add(extras);
            extras.Children.Add(ScorecardSummaryCard(Figure("Wides", () => draft.Wides, v => draft.Wides = v), "Wides", draft.Wides.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("No-balls", () => draft.NoBalls, v => draft.NoBalls = v), "No-balls", draft.NoBalls.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("Byes", () => draft.Byes, v => draft.Byes = v), "Byes", draft.Byes.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("Leg-byes", () => draft.LegByes, v => draft.LegByes = v), "Leg-byes", draft.LegByes.ToString(), 23));
        }
        AddInlineFallOfWickets(draft);
        UpdateScorecardStatus();
        if(clean)_scorecardViewCache[_scorecardBowling]=(_scorecardRows.Children.Cast<UIElement>().ToArray(),new(_scorecardPlayerRows));
        if (!string.IsNullOrWhiteSpace(focusedUid))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (renderVersion != _scorecardRenderVersion || !ReferenceEquals(draft, _scorecardDraft) || MainTabControl.SelectedItem != ScorecardTab || Window.GetWindow(this) is not { IsActive: true }) return;
                var current = System.Windows.Input.Keyboard.FocusedElement;
                if (current is FrameworkElement { IsVisible: true } && !ReferenceEquals(current, previousField) && current is not Window && !ReferenceEquals(current, this) && !ReferenceEquals(current, ScorecardHost)) return;
                var region = _scorecardRows.Children.OfType<StackPanel>().FirstOrDefault(panel => Equals(panel.Tag, "scorecard-inline-fow"));
                var field = region?.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<TextBox>()).FirstOrDefault(entry => entry.Uid == focusedUid);
                if (field is null) return;
                _scorecardScroll?.ScrollToVerticalOffset(scrollOffset); field.Focus();
                field.Select(Math.Min(selectionStart, field.Text.Length), Math.Min(selectionLength, Math.Max(0, field.Text.Length - selectionStart)));
                field.BringIntoView();
            }));
    }
}
