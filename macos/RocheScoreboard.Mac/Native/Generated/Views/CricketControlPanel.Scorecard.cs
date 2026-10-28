// Generated from Views/CricketControlPanel.Scorecard.cs; edit translator/adapters.
// Source SHA-256: b96f92462662f1a6f34a0a348222563c2d1570965a433fd159cf6317dfc54d0b
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Globalization;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = Avalonia.Controls.Button;
using TextBox = Avalonia.Controls.TextBox;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private CricketScorecardDraft? _scorecardDraft;
    private bool _scorecardDirty;
    private int _scorecardInnings;
    private bool _scorecardBowling;
    private readonly StackPanel _scorecardRows = new();
    private readonly WrapPanel _scorecardInningsButtons = new(), _scorecardTeamButtons = new();
    private readonly Avalonia.Controls.TextBlock _scorecardStatus = WorkflowText("Select an innings to view its scorecard.", 13);
    private Grid? _scorecardLayout;
    private bool? _scorecardCompact;
    private bool? _scorecardSummaryNarrow;
    private bool _scorecardFowEditing;
    private (int Innings, int Revision, int MatchBalls, string Message)? _scorecardAppliedFeedback;
    private readonly Dictionary<(bool Bowling, int Index), Grid> _scorecardPlayerRows = new();
    private ScrollViewer? _scorecardScroll;
    private int _scorecardRenderVersion;
    internal int ScorecardRenderCount => _scorecardRenderVersion;
    private readonly Dictionary<bool,(Control[] Children,Dictionary<(bool Bowling,int Index),Grid> Rows)> _scorecardViewCache=new();
    private CricketScorecardDraft? _scorecardCachedDraft;
    private int _scorecardCachedWidthClass;
    private string _scorecardMetadata="";
    private void InitializeScorecard()
    {
        var layout = new Grid { Margin = new Thickness(12) }; _scorecardLayout = layout;
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var top = new StackPanel(); layout.Children.Add(top);
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_scorecardStatus, Avalonia.Automation.AutomationLiveSetting.Polite);
        _scorecardInningsButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; _scorecardInningsButtons.Margin = new Thickness(0, 0, 0, 3);
        top.Children.Add(_scorecardInningsButtons);
        top.Children.Add(_scorecardStatus);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
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
        SizeChanged += (_, _) => { BoundScorecardViewport(); bool compact = Bounds.Width < 820, summaryNarrow = Bounds.Width < 760; if (_scorecardCompact != compact || _scorecardSummaryNarrow != summaryNarrow) { _scorecardCompact = compact; _scorecardSummaryNarrow = summaryNarrow; if (_scorecardDraft is not null) RenderScorecard(); } };
        PersistentHeader.SizeChanged += (_, _) => BoundScorecardViewport();
        BoundScorecardViewport();
    }
    private void BoundScorecardViewport()
    {
        if (_scorecardLayout is null || Bounds.Height <= 0) return;
        _scorecardStatus.MinHeight = Bounds.Width < 620 ? 36 : 20;
        // Natural-height operator pages are measured without a vertical limit.
        // Keep innings navigation and editing instructions visible above the table.
        _scorecardLayout.Height = Math.Max(130, Bounds.Height - PersistentHeader.NativeHeight() - 76);
    }
    private void ResetScorecard() { _scorecardViewCache.Clear();_scorecardCachedDraft=null;_scorecardMetadata=""; _scorecardDraft = null; _scorecardDirty = false; _scorecardCommitBusy = false; _scorecardFowEditing = false; _scorecardAppliedFeedback = null; _scorecardInnings = 0; _scorecardPlayerRows.Clear(); _scorecardRows.Children.Clear(); _scorecardInningsButtons.Children.Clear(); _scorecardTeamButtons.Children.Clear(); }
    private void SizeScorecardTeamTabs()
    {
        double width = Math.Max(100, (_scorecardTeamButtons.NativeWidth() - 12) / 2);
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
            button.Tag = "scorecard-innings-" + number; Avalonia.Controls.ToolTip.SetTip(button, innings.BattingTeamName + " batting"); button.MinWidth = 100; button.MinHeight = 40; button.FontSize = 13; button.FontWeight = FontWeight.Bold; button.Padding = new Thickness(10, 5, 10, 5);
            button.BorderBrush = number == _scorecardInnings ? Brushes.LightSkyBlue : Brushes.SlateGray;
            button.BorderThickness = new Thickness(1);
            button.SetResourceReference(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, number == _scorecardInnings ? "ControlCentre.SelectionBrush" : "ControlCentre.SurfaceBrush");
            _scorecardInningsButtons.Children.Add(button);
        }
        var current = match.AllInnings[_scorecardInnings - 1]; _scorecardTeamButtons.Children.Clear();
        var batting = WorkflowButton(current.BattingTeamName, () => SelectScorecardTeam(false), "scorecard-team-batting");
        var bowling = WorkflowButton(current.BowlingTeamName, () => SelectScorecardTeam(true), "scorecard-team-bowling");
        Avalonia.Controls.ToolTip.SetTip(batting, current.BattingTeamName + " · Batting"); Avalonia.Controls.ToolTip.SetTip(bowling, current.BowlingTeamName + " · Bowling");
        batting.Background = CricketBroadcastStyle.Gradient(current.BattingTeamName == match.TeamAName ? match.TeamAPrimaryColor : match.TeamBPrimaryColor, null);
        bowling.Background = CricketBroadcastStyle.Gradient(current.BowlingTeamName == match.TeamAName ? match.TeamAPrimaryColor : match.TeamBPrimaryColor, null);
        foreach (var (tab, name, role) in new[] { (batting, current.BattingTeamName, "BATTING"), (bowling, current.BowlingTeamName, "BOWLING") })
        {
            var label = new StackPanel(); label.Children.Add(new NativeTextBlock { Text = role, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Opacity = .75, TextAlignment = TextAlignment.Center });
            label.Children.Add(new NativeTextBlock { Text = name, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            var navigation = new Grid(); navigation.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); navigation.ColumnDefinitions.Add(new());
            navigation.Children.Add(new OperatorSymbol { Kind = role == "BATTING" ? "Cricket" : "Teams", Width = 22, Height = 22, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(label, 1); navigation.Children.Add(label);
            tab.Content = navigation; tab.ContentTemplate = null; tab.Height = 66; tab.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
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
        string previousStatus = (_scorecardStatus.Text ?? string.Empty);
        _scorecardStatus.Text = _scorecardDirty ? stale ? "The match changed. Reopen this edit to use current figures." : "Complete the inline row and press Apply."
            : !string.IsNullOrWhiteSpace(selectedInnings.ManualScorecardWarning) ? "Manual figures have reconciliation differences · review adjustments below."
            : _scorecardAppliedFeedback?.Message ?? "Hover and click a figure to edit. Apply updates the match immediately.";
        Avalonia.Controls.ToolTip.SetTip(_scorecardStatus, selectedInnings.ManualScorecardWarning);
        _scorecardStatus.FontWeight = FontWeight.SemiBold;
        if (stale && _scorecardDirty) _scorecardStatus.Foreground = ScorecardAlertBrush(true);
        else if (_scorecardDirty || !string.IsNullOrWhiteSpace(selectedInnings.ManualScorecardWarning)) _scorecardStatus.Foreground = ScorecardAlertBrush(false);
        else _scorecardStatus.SetResourceReference(Avalonia.Controls.TextBlock.ForegroundProperty, "ControlCentre.AccentBrush");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_scorecardStatus,Avalonia.Automation.AutomationLiveSetting.Polite);
    }
    private Button Figure(string label, Func<int> read, Action<int> write, bool overs = false, bool compact = false)
    {
        string value = overs ? $"{read() / 6}.{read() % 6}" : read().ToString(CultureInfo.InvariantCulture);
        var button = WorkflowButton(compact ? value : label + "  " + value, async () =>
        {
            var draft = _scorecardDraft; if (draft is null) return;
            var editor = CreateScorecardFigureEditor(label, value, overs, out var field);
            while (ReferenceEquals(draft, _scorecardDraft))
            {
                if (await FormAsync("Edit " + label.ToLowerInvariant(), overs ? "Use overs and legal balls, for example 5.3." : "Update this figure. The scorecard checks the totals before applying it.", editor, new("back", "Cancel"), new("apply", "Apply", true)) != "apply") return;
                if (!ReferenceEquals(draft, _scorecardDraft)) return;
                string[] parts = (field.Text ?? string.Empty).Trim().Split('.');
                int parsed = 0; bool valid = !overs ? int.TryParse((field.Text ?? string.Empty), out parsed) : parts.Length <= 2 && int.TryParse(parts[0], out int complete) && complete >= 0 && complete <= 166666 && (parts.Length == 1 || int.TryParse(parts[1], out int balls) && balls is >= 0 and <= 5);
                if (valid && overs) parsed = int.Parse(parts[0], CultureInfo.InvariantCulture) * 6 + (parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
                if (!valid || parsed < 0 || parsed > 1_000_000) { await ShowErrorAsync("Check the figure", "Enter a valid non-negative figure. Overs use a ball digit from 0 to 5."); continue; }
                int previous = read(); bool alreadyPending = _scorecardDirty;
                write(parsed); _scorecardDirty = true;
                if (await CommitScorecardEditAsync(draft)) return;
                if (!ReferenceEquals(draft, _scorecardDraft)) return;
                write(previous); _scorecardDirty = alreadyPending; RenderScorecard();
            }
        });
        button.MinWidth = compact ? 54 : 76; if (compact) { button.Width = 54; button.FontSize = 18; button.Padding = new Thickness(2, 6, 2, 6); }
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); Avalonia.Controls.ToolTip.SetTip(button, "Edit " + label);
        button.SetResourceReference(Avalonia.Controls.TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        Avalonia.Automation.AutomationProperties.SetName(button, label + ", " + value + ". Edit figure.");
        return button;
    }
    private void RenderScorecard()=>RenderScorecardView(false);
    private void RenderScorecardView(bool reuseViews)
    {
        var previousField = NativeKeyboard.FocusedElement as TextBox;
        string? focusedUid = previousField is { Tag: "scorecard-fow-entry" } ? (previousField.Name ?? string.Empty) : null;
        int selectionStart = previousField?.SelectionStart ?? 0, selectionLength = (previousField is null?0:previousField.SelectionEnd-previousField.SelectionStart);
        double scrollOffset = _scorecardScroll?.Offset.Y ?? 0;
        int renderVersion = ++_scorecardRenderVersion;
        _scorecardRows.Children.Clear(); _scorecardPlayerRows.Clear(); if (_scorecardDraft is not { } draft || _match is null) return;
        int widthClass=Bounds.Width<760?0:Bounds.Width<820?1:2;
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
            Avalonia.Automation.AutomationProperties.SetItemStatus(tab, selected ? "Selected" : "");
        }
        if(clean && _scorecardViewCache.TryGetValue(_scorecardBowling,out var cached))
        {
            foreach(var child in cached.Children)_scorecardRows.Children.Add(child);
            foreach(var entry in cached.Rows)_scorecardPlayerRows[entry.Key]=entry.Value;
            UpdateScorecardStatus();return;
        }
        AddScorecardSummary(draft);
        bool compact = Bounds.Width < 820;
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
            row.SetResourceReference(Avalonia.Controls.Panel.BackgroundProperty, "ControlCentre.SurfaceBrush");
            int playerIndex = players.IndexOf(player); row.Tag = $"scorecard-player-{(_scorecardBowling ? "bowling" : "batting")}-{playerIndex}";
            _scorecardPlayerRows[(_scorecardBowling, playerIndex)] = row;
            row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            if (compact) row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            foreach (var column in header.ColumnDefinitions) row.ColumnDefinitions.Add(new() { Width = column.Width, MinWidth = column.MinWidth });
            var name = WorkflowText(player.FullName, 16); name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(8, 5, 6, 5); name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (compact) { Grid.SetColumnSpan(name, 4); name.Margin = new Thickness(12, 12, 12, 3); Avalonia.Controls.ToolTip.SetTip(name, player.FullName); }
            row.Children.Add(name);
            var values = new List<Control>();
            if (_scorecardBowling)
            {
                values.Add(Figure("Overs", () => player.BallsBowled, v => player.BallsBowled = v, true, true)); values.Add(Figure("Maidens", () => player.Maidens, v => player.Maidens = v, compact: true)); values.Add(Figure("Runs conceded", () => player.RunsConceded, v => player.RunsConceded = v, compact: true)); values.Add(Figure("Bowler wickets", () => player.Wickets, v => player.Wickets = v, compact: true)); values.Add(Figure("Bowler wides", () => player.Wides, v => player.Wides = v, compact: true)); values.Add(Figure("Bowler no-balls", () => player.NoBalls, v => player.NoBalls = v, compact: true));
                var econ = WorkflowText($"{player.Economy:0.00}", 15); econ.TextAlignment = TextAlignment.Center; econ.VerticalAlignment = VerticalAlignment.Center; Avalonia.Controls.ToolTip.SetTip(econ, "Economy · calculated from balls and runs"); values.Add(econ);
            }
            else
            {
                values.Add(Figure("Runs", () => player.Runs, v => player.Runs = v, compact: true)); values.Add(Figure("Balls", () => player.BallsFaced, v => player.BallsFaced = v, compact: true)); values.Add(Figure("4s", () => player.Fours, v => player.Fours = v, compact: true)); values.Add(Figure("6s", () => player.Sixes, v => player.Sixes = v, compact: true));
                var dismissal = WorkflowButton(player.DismissalSummary, async () => await EditScorecardDismissalAsync(player), "scorecard-dismissal"); dismissal.Background = Brushes.Transparent; dismissal.BorderThickness = new Thickness(0); dismissal.MinHeight = compact ? 30 : 40; dismissal.FontSize = 13; dismissal.Margin = new Thickness(4, 0, 4, 2);
                dismissal.SetResourceReference(Avalonia.Controls.TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
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
            var extras = new Avalonia.Controls.Primitives.UniformGrid { Columns = Bounds.Width < 760 ? 2 : 4 }; extrasRegion.Children.Add(extras);
            extras.Children.Add(ScorecardSummaryCard(Figure("Wides", () => draft.Wides, v => draft.Wides = v), "Wides", draft.Wides.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("No-balls", () => draft.NoBalls, v => draft.NoBalls = v), "No-balls", draft.NoBalls.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("Byes", () => draft.Byes, v => draft.Byes = v), "Byes", draft.Byes.ToString(), 23));
            extras.Children.Add(ScorecardSummaryCard(Figure("Leg-byes", () => draft.LegByes, v => draft.LegByes = v), "Leg-byes", draft.LegByes.ToString(), 23));
        }
        AddInlineFallOfWickets(draft);
        UpdateScorecardStatus();
        if(clean)_scorecardViewCache[_scorecardBowling]=(_scorecardRows.Children.Cast<Control>().ToArray(),new(_scorecardPlayerRows));
        if (!string.IsNullOrWhiteSpace(focusedUid))
            Dispatcher.UIThread.Post(Avalonia.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (renderVersion != _scorecardRenderVersion || !ReferenceEquals(draft, _scorecardDraft) || MainTabControl.SelectedItem != ScorecardTab || NativeWindow.GetWindow(this) is not { IsActive: true }) return;
                var current = NativeKeyboard.FocusedElement;
                if (current is Control { IsVisible: true } && !ReferenceEquals(current, previousField) && current is not Window && !ReferenceEquals(current, this) && !ReferenceEquals(current, ScorecardHost)) return;
                var region = _scorecardRows.Children.OfType<StackPanel>().FirstOrDefault(panel => Equals(panel.Tag, "scorecard-inline-fow"));
                var field = region?.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<TextBox>()).FirstOrDefault(entry => (entry.Name ?? string.Empty) == focusedUid);
                if (field is null) return;
                _scorecardScroll?.ScrollToVerticalOffset(scrollOffset); field.Focus();
                field.Select(Math.Min(selectionStart, (field.Text ?? string.Empty).Length), Math.Min(selectionLength, Math.Max(0, (field.Text ?? string.Empty).Length - selectionStart)));
                field.BringIntoView();
            }));
    }
}
