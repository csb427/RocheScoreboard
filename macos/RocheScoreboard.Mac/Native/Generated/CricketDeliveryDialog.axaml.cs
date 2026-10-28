using RocheScoreboard.Mac.Production;
// Generated from Views/CricketDeliveryDialog.xaml.cs; edit translator/adapters.
// Source SHA-256: 9247270c1a9c69694ad91e4beb215facaa28b3b5baa0acbc8e91052970b04238
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using UserControl = Avalonia.Controls.UserControl;
using MessageBox = RocheScoreboard.Mac.Native.NativeMessageBox;

namespace Roche_Scoreboard.Views;

public partial class CricketDeliveryDialog : UserControl
{
    private readonly CricketMatchManager _match;
    private readonly CricketInnings _innings;
    private readonly CricketDelivery? _delivery;
    private readonly List<int> _dismissedIndices = new();
    private readonly List<int> _batterIndices = new();
    private bool _applying;
    private string EntryColour => TypeChoice?.SelectedIndex == (int)CricketDeliveryType.Wicket ? "#48617A" : _match.BattingTeamPrimaryColor;
    private readonly DismissalType[] _dismissals = [DismissalType.Bowled, DismissalType.Caught, DismissalType.LBW,
        DismissalType.RunOut, DismissalType.Stumped, DismissalType.CaughtAndBowled, DismissalType.HitWicket, DismissalType.Other,
        DismissalType.ObstructingField,DismissalType.HitBallTwice];
    private bool EntryIsFreeHit => _delivery?.WasFreeHit ?? _match.FreeHitPending;
    private bool _initializing = true;
    private int _originalStriker;
    private readonly int _openedCount, _openedRevision, _openedStriker, _openedNonStriker, _openedBowler;
    private readonly string _openedConditionsToken;
    private bool EntryContextIsCurrent => ReferenceEquals(_match.CurrentInnings, _innings)
        && _openedCount == _innings.Deliveries.Count && _openedRevision == _match.DeliveryRevision
        && _openedStriker == _innings.StrikerIndex && _openedNonStriker == _innings.NonStrikerIndex
        && _openedBowler == _innings.CurrentBowlerIndex && _openedConditionsToken == _match.MatchConditionsToken;
    private bool SelectedWicketIsAllowed => CricketMatchManager.IsDismissalAllowedForDelivery(
        _dismissals[Math.Max(0, DismissalChoice.SelectedIndex)],
        (CricketWicketRuns)Math.Max(0, RunOutChoice.SelectedIndex), EntryIsFreeHit);
    private Action<bool>? _embeddedCompletion;
    private bool _completed;
    public event Action<bool>? Completed;
    internal Control Embed(Action<bool> completed)
    {
        _embeddedCompletion = completed;
        var body = (Control)this;
        body.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Avalonia.Media.Brushes.White);
        body.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, NativeFonts.Family("Bahnschrift"));
        body.KeyDown += (_, e) =>
        {
            if (EntryLayout.Children.OfType<Border>().Any(child => Equals(child.Tag, "cricket-decision-sheet"))) return;
            if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; Complete(false); }
            else if (e.Key == Avalonia.Input.Key.Enter && e.Source is Avalonia.Controls.TextBox) { e.Handled = true; Apply_Click(ApplyButton, new RoutedEventArgs()); }
        };
        return body;
    }
    private void Complete(bool applied)
    {
        if (_completed) return; _completed = true;
        CricketPrompt.CancelAll(EntryLayout);
        if (_embeddedCompletion != null) _embeddedCompletion(applied); else Completed?.Invoke(applied);
    }
    internal void Cancel() => Complete(false);
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Complete(false);
    private void CustomRuns_Click(object? sender, RoutedEventArgs args)
    {
        RunsBox.IsVisible = true; RunsBox.Focus(); RunsBox.SelectAll();
    }

    public CricketDeliveryDialog(CricketMatchManager match, CricketDelivery? delivery = null,
        CricketDeliveryType initialType = CricketDeliveryType.Wicket)
    {
        _match = match; _innings = match.CurrentInnings ?? throw new ArgumentException("An innings is required."); _delivery = delivery;
        _openedCount = _innings.Deliveries.Count; _openedRevision = match.DeliveryRevision; _openedStriker = _innings.StrikerIndex; _openedBowler = _innings.CurrentBowlerIndex;
        _openedNonStriker = _innings.NonStrikerIndex; _openedConditionsToken = match.MatchConditionsToken;
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        InteractionMotion.Enable(this);
        MaxHeight = Math.Max(300, NativeDesktop.WorkArea.Height - 32);
        MaxWidth = Math.Max(340, NativeDesktop.WorkArea.Width - 32);

        TeamHeader.Background = TeamVisuals.Panel(match.BattingTeamPrimaryColor, match.BattingTeamPrimaryColor, .35);
        TeamHeader.BorderBrush = ApplyButton.BorderBrush = TeamVisuals.Accent(match.BattingTeamPrimaryColor);
        ApplyButton.Background = TeamVisuals.Panel(match.BattingTeamPrimaryColor, match.BattingTeamPrimaryColor, .4);
        TeamLogo.Source = TeamVisuals.LoadLogo(match.BattingTeamName == match.TeamAName ? match.TeamALogoPath : match.TeamBLogoPath);
        foreach (string label in new[] { "Dot ball", "Runs off the bat", "Four (boundary)", "Six (boundary)", "Wide", "No-ball", "Byes", "Leg-byes", "Wicket" }) TypeChoice.Items.Add(label);
        foreach (string label in new[] { "Off the bat", "Byes", "Leg-byes" }) NoBallChoice.Items.Add(label);
        foreach (string label in new[] { "Off the bat", "Wide", "No-ball", "Byes", "Leg-byes" }) RunOutChoice.Items.Add(label);
        foreach (string label in new[] { "Bowled", "Caught", "LBW", "Run out", "Stumped", "Caught & bowled", "Hit wicket", "Other dismissal", "Obstructing field", "Hit ball twice" }) DismissalChoice.Items.Add(label);
        foreach (var player in _innings.BowlingAttack) BowlerChoice.Items.Add(player.FullName);
        FielderChoice.Items.Add("Not recorded");
        foreach (var player in _innings.BowlingAttack) FielderChoice.Items.Add(player.FullName);
        foreach (string label in new[] { "No — this ball only", "Yes — remaining balls in this over", "Yes — all following balls in this innings" }) ScopeChoice.Items.Add(label);
        var input = delivery is null ? new CricketDeliveryEdit
        {
            Type = initialType,
            Runs = initialType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall or CricketDeliveryType.Bye or CricketDeliveryType.LegBye ? 1 : 0,
            StrikerIndex = _innings.StrikerIndex,
            BowlerIndex = _innings.CurrentBowlerIndex,
            DismissedBatterIndex = _innings.StrikerIndex
        } : CricketMatchManager.DescribeDelivery(_innings, delivery);
        TypeChoice.SelectedIndex = (int)input.Type; NoBallChoice.SelectedIndex = (int)input.NoBallRuns;
        RunOutChoice.SelectedIndex = (int)input.WicketRuns;
        RunOutCrossed.IsChecked = input.RunOutCrossed;
        RunOutNotCrossed.IsChecked = !input.RunOutCrossed;
        _originalStriker = input.StrikerIndex;
        int otherBatter = delivery?.NonStrikerIndex ?? _innings.NonStrikerIndex;
        if (otherBatter < 0) otherBatter = delivery?.StateBefore?.NonStrikerIndex ?? -1;
        foreach (int actor in new[] { input.StrikerIndex, otherBatter }.Where(index => index >= 0 && index < _innings.BattingOrder.Count).Distinct())
        { _batterIndices.Add(actor); BatterChoice.Items.Add(_innings.BattingOrder[actor].FullName); }
        BatterChoice.SelectedIndex = _batterIndices.IndexOf(input.StrikerIndex); BowlerChoice.SelectedIndex = input.BowlerIndex;
        DismissalChoice.SelectedIndex = Math.Max(0, Array.IndexOf(_dismissals, input.Dismissal));
        if(delivery is null && initialType==CricketDeliveryType.Wicket && EntryIsFreeHit) DismissalChoice.SelectedIndex=Array.IndexOf(_dismissals,DismissalType.RunOut);
        FielderChoice.SelectedIndex = input.Fielder is null ? 0 : Math.Max(0, _innings.BowlingAttack.FindIndex(p => p.DisplayName == input.Fielder || p.FullName == input.Fielder) + 1);
        int other = delivery?.NonStrikerIndex ?? _innings.NonStrikerIndex;
        if (other < 0) other = delivery?.StateBefore?.NonStrikerIndex ?? _innings.NonStrikerIndex;
        foreach (int actor in new[] { input.StrikerIndex, other }.Where(i => i >= 0).Distinct())
        { _dismissedIndices.Add(actor); DismissedChoice.Items.Add(_innings.BattingOrder[actor].FullName); }
        DismissedChoice.SelectedIndex = Math.Max(0, _dismissedIndices.IndexOf(input.DismissedBatterIndex));
        RunsBox.Text = (input.Runs - (input.Type is CricketDeliveryType.NoBall or CricketDeliveryType.Wide || input.Type == CricketDeliveryType.Wicket && input.WicketRuns is CricketWicketRuns.Wide or CricketWicketRuns.NoBall ? 1 : 0)).ToString();
        ScopeChoice.SelectedIndex = 0;
        bool editing = delivery is not null;
        Heading.Text = editing ? "EDIT DELIVERY" : initialType == CricketDeliveryType.Wicket ? "RECORD WICKET" : "RECORD EXTRAS";
        ApplyButton.Content = editing ? "APPLY CORRECTION" : initialType == CricketDeliveryType.Wicket ? "CONFIRM WICKET" : "ADD DELIVERY";
        ContextText.Text = editing ? $"{match.BattingTeamName} · Over {delivery!.OverNumber + 1}, delivery {_innings.Deliveries.IndexOf(delivery) + 1}"
            : $"{match.BattingTeamName} · {_innings.Striker?.FullName} facing {_innings.CurrentBowler?.FullName}";
        ActorOptions.IsVisible = editing ? true : false; CorrectionOptions.IsVisible = false;
        TypeLabel.IsVisible = TypeChoice.IsVisible = editing ? true : false;
        if (!editing && initialType != CricketDeliveryType.Wicket) Heading.Text = initialType switch { CricketDeliveryType.NoBall => "NO-BALL", CricketDeliveryType.Wide => "WIDE", CricketDeliveryType.Bye => "BYES", _ => "LEG-BYES" };
        SizeChanged += (_, _) => ReflowEntry();
        BuildChoiceTiles(DismissalTiles, DismissalChoice); BuildChoiceTiles(DismissedTiles, DismissedChoice); BuildChoiceTiles(FielderTiles, FielderChoice);
        BuildChoiceTiles(BatterTiles, BatterChoice); BuildChoiceTiles(BowlerTiles, BowlerChoice);
        _initializing = false; RunsBox.IsVisible = !int.TryParse((RunsBox.Text ?? string.Empty), out int preset) || preset > 6 ? true : false; UpdateFields();
    }

    private void ReflowEntry()
    {
        bool compact = Bounds.Height < 500, shortEntry = Bounds.Height < 330;
        EntryLayout.Margin = new Thickness(compact ? 8 : 16);
        TeamHeader.Height = shortEntry ? 44 : compact ? 64 : 96;
        TeamHeader.Padding = new Thickness(compact ? 10 : 16, compact ? 5 : 12, compact ? 10 : 16, compact ? 5 : 12);
        Heading.FontSize = compact ? 20 : 25;
        ContextText.IsVisible = shortEntry ? false : true;
        ContextText.FontSize = compact ? 12 : 14;
        PreviewText.FontSize = compact ? 15 : 18; PreviewPanel.Padding = new Thickness(compact ? 8 : 12);
        ApplyButton.Height = CancelButton.Height = compact ? 38 : 48;
    }
    private void Source_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Avalonia.Controls.Button { Tag: string value } && int.TryParse(value, out int source))
            NoBallChoice.SelectedIndex = source;
    }
    private void RunOutSource_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Avalonia.Controls.Button { Tag: string value } && int.TryParse(value, out int source)) RunOutChoice.SelectedIndex = source;
    }
    private void RunsTile_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Avalonia.Controls.Button { Tag: string value })
        { RunsBox.Text = value; RunsBox.IsVisible = false; }
    }
    private void Details_Changed(object? sender, SelectionChangedEventArgs args) { if (!_initializing) UpdateFields(); }
    private void Runs_Changed(object? sender, TextChangedEventArgs args) { if (!_initializing) UpdateFields(); }
    private void Batter_Changed(object? sender, SelectionChangedEventArgs args)
    {
        if (_initializing || _delivery is null) return;
        ScopeChoice.SelectedIndex = SelectedBatterIndex == _originalStriker ? 0 : -1;
        if (_dismissedIndices.Count > 0 && BatterChoice.SelectedIndex >= 0)
        {
            int previous = _dismissedIndices[0]; int current = SelectedBatterIndex;
            if (_dismissedIndices.Count > 1 && _dismissedIndices[1] == current)
            { _dismissedIndices[1] = previous; DismissedChoice.Items[1] = _innings.BattingOrder[previous].FullName; }
            _dismissedIndices[0] = current; DismissedChoice.Items[0] = _innings.BattingOrder[current].FullName;
        }
        UpdateFields();
    }
    private void UpdateFields()
    {
        if (TypeChoice.SelectedIndex < 0) return;
        ErrorText.Text = ""; ErrorText.IsVisible = false;
        foreach (var tile in new[] { BatRunsTile, ByesTile, LegByesTile })
        {
            bool selected = tile.Tag?.ToString() == NoBallChoice.SelectedIndex.ToString();
            tile.Background = TeamVisuals.Panel(EntryColour, EntryColour, selected ? .55 : .15);
            tile.BorderBrush = selected ? TeamVisuals.Accent(EntryColour) : Avalonia.Media.Brushes.SlateGray;
            Avalonia.Automation.AutomationProperties.SetItemStatus(tile, selected ? "Selected" : "Not selected");
        }
        foreach (var tile in RunsTiles.Children.OfType<Avalonia.Controls.Button>())
        {
            if (ReferenceEquals(tile,CustomRunsButton)) continue;
            bool selected = tile.Tag?.ToString() == (RunsBox.Text ?? string.Empty);
            tile.BorderThickness = new Thickness(2);
            tile.BorderBrush = selected ? Avalonia.Media.Brushes.LightSkyBlue : Avalonia.Media.Brushes.SlateGray;
            tile.Background = selected ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(26,83,135)) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(20,31,44));
            tile.Foreground = Avalonia.Media.Brushes.White;
            tile.FontSize=18;
            tile.Content = selected ? "✓  " + tile.Tag : tile.Tag;
            Avalonia.Automation.AutomationProperties.SetItemStatus(tile,selected?"Selected":"Not selected");
            bool noZero = (CricketDeliveryType)TypeChoice.SelectedIndex is CricketDeliveryType.Bye or CricketDeliveryType.LegBye;
            tile.IsVisible = noZero && Equals(tile.Tag,"0") ? false : true;
        }
        var type = (CricketDeliveryType)TypeChoice.SelectedIndex;
        bool neutral = type == CricketDeliveryType.Wicket;
        TeamHeader.Background = TeamVisuals.Panel(neutral ? "#48617A" : EntryColour, neutral ? "#48617A" : EntryColour, .35);
        TeamHeader.BorderBrush = ApplyButton.BorderBrush = neutral ? Avalonia.Media.Brushes.SlateGray : TeamVisuals.Accent(EntryColour);
        TeamLogo.IsVisible = neutral ? false : true;
        ApplyButton.Background = TeamVisuals.Panel(neutral ? "#48617A" : EntryColour, neutral ? "#48617A" : EntryColour, .4);
        var dismissal = DismissalChoice.SelectedIndex >= 0 ? _dismissals[DismissalChoice.SelectedIndex] : DismissalType.Bowled;
        bool runOut = type == CricketDeliveryType.Wicket && dismissal == DismissalType.RunOut;
        bool runningDismissal = type == CricketDeliveryType.Wicket && dismissal is DismissalType.RunOut or DismissalType.ObstructingField;
        var scoringType = type == CricketDeliveryType.Wicket ? CricketDelivery.WicketScoringType((CricketWicketRuns)Math.Max(0, RunOutChoice.SelectedIndex)) : type;
        RunOutOptions.IsVisible = type == CricketDeliveryType.Wicket ? true : false;
        RunOutCrossingOptions.IsVisible = runningDismissal ? true : false;
        foreach (var tile in RunOutSourceTiles.Children.OfType<Avalonia.Controls.Button>())
        {
            bool selected = tile.Tag?.ToString() == RunOutChoice.SelectedIndex.ToString();
            tile.BorderThickness = new Thickness(selected ? 2 : 1);
            tile.Background = TeamVisuals.Panel(EntryColour, EntryColour, selected ? .55 : .15);
            tile.BorderBrush = selected ? TeamVisuals.Accent(EntryColour) : Avalonia.Media.Brushes.SlateGray;
            Avalonia.Automation.AutomationProperties.SetItemStatus(tile, selected ? "Selected" : "Not selected");
        }
        NoBallOptions.IsVisible = scoringType == CricketDeliveryType.NoBall ? true : false;
        WicketOptions.IsVisible = type == CricketDeliveryType.Wicket ? true : false;
        RunOptions.IsVisible = type is CricketDeliveryType.Dot or CricketDeliveryType.Four or CricketDeliveryType.Six ? false : true;
        if (type == CricketDeliveryType.Wicket)
        {
            RunOptions.IsVisible = runningDismissal ? true : false;
            RunsLabel.Text = scoringType is CricketDeliveryType.NoBall or CricketDeliveryType.Wide ? "COMPLETED RUNS (PENALTY IS AUTOMATIC)" : "COMPLETED RUNS BEFORE THE DISMISSAL";
        }
        else RunsLabel.Text = type is CricketDeliveryType.NoBall or CricketDeliveryType.Wide ? "ADDITIONAL RUNS (PENALTY IS AUTOMATIC)" : "RUNS";
        DismissedChoice.IsEnabled = dismissal is DismissalType.RunOut or DismissalType.Other or DismissalType.ObstructingField;
        if (!DismissedChoice.IsEnabled && _dismissedIndices.Contains(SelectedBatterIndex))
            DismissedChoice.SelectedIndex = _dismissedIndices.IndexOf(SelectedBatterIndex);
        RefreshChoiceTiles();
        foreach(var tile in DismissalTiles.Children.OfType<Avalonia.Controls.Button>())
        {
            int choice=((ValueTuple<Avalonia.Controls.ComboBox,int>)tile.Tag).Item2;
            bool allowed=CricketMatchManager.IsDismissalAllowedForDelivery(_dismissals[choice], (CricketWicketRuns)Math.Max(0,RunOutChoice.SelectedIndex), EntryIsFreeHit);
            tile.IsEnabled=allowed;Avalonia.Controls.ToolTip.SetTip(tile,allowed?null:"Choose a different delivery outcome. This dismissal is not available for the selected outcome or free hit.");
        }
        FreeHitEntryHint.IsVisible=EntryIsFreeHit && type==CricketDeliveryType.Wicket ? true : false;
        WicketRuleHint.IsVisible=type==CricketDeliveryType.Wicket && !SelectedWicketIsAllowed ? true : false;
        ApplyButton.IsEnabled=!_applying && !_completed && (type!=CricketDeliveryType.Wicket || SelectedWicketIsAllowed);
        foreach (var tile in DismissedTiles.Children.OfType<Avalonia.Controls.Button>())
        {
            int index = ((ValueTuple<Avalonia.Controls.ComboBox, int>)tile.Tag).Item2;
            bool eligible = DismissedChoice.IsEnabled || _dismissedIndices[index] == SelectedBatterIndex;
            tile.IsEnabled = eligible; tile.Opacity = eligible ? 1 : .18;
            Avalonia.Controls.ToolTip.SetTip(tile, eligible ? null : "Only the striker can be dismissed this way.");
        }
        DismissedBatterHint.IsVisible = DismissedChoice.IsEnabled ? false : true;
        FielderOptions.IsVisible = dismissal is DismissalType.Caught or DismissalType.RunOut or DismissalType.Stumped ? true : false;
        bool penalty = scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall;
        RunExplanation.Text = penalty ? "Enter only additional runs. The one-run penalty is added once. A no-ball cannot also be a wide."
            : type == CricketDeliveryType.Wicket ? "Only completed runs count on a run out. Choose which batter was dismissed." : "Enter the runs completed. Byes and leg-byes have no automatic penalty.";
        int.TryParse((RunsBox.Text ?? string.Empty), out int additional);
        int total = type switch
        {
            CricketDeliveryType.Dot => 0,
            CricketDeliveryType.Four => 4,
            CricketDeliveryType.Six => 6,
            CricketDeliveryType.Wicket when !runningDismissal => penalty ? 1 : 0,
            _ => additional + (penalty ? 1 : 0)
        };
        string extraSource=NoBallChoice.SelectedIndex==1?(additional==1?"bye":"byes"):NoBallChoice.SelectedIndex==2?(additional==1?"leg-bye":"leg-byes"):(additional==1?"run off bat":"runs off bat");
        string composition = scoringType == CricketDeliveryType.NoBall ? additional==0?"1 no-ball = 1 run":$"1 no-ball + {additional} {extraSource} = {total} runs"
            : scoringType == CricketDeliveryType.Wide ? additional==0?"1 wide = 1 run":$"1 wide + {additional} completed = {total} wides"
            : $"{additional} {(scoringType == CricketDeliveryType.Bye ? "byes" : scoringType == CricketDeliveryType.LegBye ? "leg-byes" : "runs off bat")}";
        PreviewText.Text = type == CricketDeliveryType.Wicket ? runningDismissal ? $"{(runOut ? "RUN OUT" : "OBSTRUCTING FIELD")} · {composition}" : CricketMatchManager.DismissalLabel(dismissal, FielderChoice.SelectedIndex > 0 ? _innings.BowlingAttack[FielderChoice.SelectedIndex - 1].DisplayName : null, _innings.CurrentBowler?.DisplayName)
                + (penalty ? $" · +1 {(scoringType == CricketDeliveryType.Wide ? "wide" : "no-ball")}" : "")
            : type is CricketDeliveryType.NoBall or CricketDeliveryType.Wide ? composition
            : $"{TypeChoice.SelectedItem} · +{total} to the total";
        if (runningDismissal)
        {
            int start = SelectedBatterIndex, other = _batterIndices.FirstOrDefault(index => index != start, -1);
            bool swap = (additional % 2 == 1) ^ (RunOutCrossed.IsChecked == true);
            int legalBefore = _delivery?.StateBefore?.LegalBallsBowled ?? _innings.LegalBallsBowled;
            bool countsInOver = scoringType switch
            {
                CricketDeliveryType.Wide => !_match.ReplaceWideBalls,
                CricketDeliveryType.NoBall => !_match.ReplaceNoBalls,
                _ => true
            };
            if (_delivery is not null && scoringType == _delivery.ScoringType) countsInOver = _delivery.IsCountedBall;
            if (countsInOver && (legalBefore + 1) % 6 == 0) swap = !swap;
            int next = swap ? other : start;
            int victim = DismissedChoice.SelectedIndex >= 0 ? _dismissedIndices[DismissedChoice.SelectedIndex] : start;
            string nextName = next == victim ? "new batter" : next >= 0 && next < _innings.BattingOrder.Count ? _innings.BattingOrder[next].DisplayName : "select batter";
            PreviewText.Text += $"\nNext on strike: {nextName}";
        }
    }
    private async void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (_completed || _applying) return;
        _applying = true; ApplyButton.IsEnabled = false;
        try
        {
            ErrorText.Text = ""; ErrorText.IsVisible = true;
            if (!EntryContextIsCurrent)
            { ErrorText.Text = "The match changed while this panel was open. Cancel and reopen it to use the current players and score."; return; }
            if (!ReferenceEquals(_match.CurrentInnings, _innings)) { ErrorText.Text = "The innings changed. Close this window and reopen the delivery."; return; }
            var type = (CricketDeliveryType)TypeChoice.SelectedIndex;
            var dismissal = _dismissals[Math.Max(0, DismissalChoice.SelectedIndex)];
            var wicketRuns = type == CricketDeliveryType.Wicket ? (CricketWicketRuns)Math.Max(0, RunOutChoice.SelectedIndex) : CricketWicketRuns.Bat;
            if (type == CricketDeliveryType.Wicket && !SelectedWicketIsAllowed)
            { ErrorText.Text = "Choose a dismissal allowed for this delivery outcome and free-hit status."; return; }
            var scoringType = type == CricketDeliveryType.Wicket ? CricketDelivery.WicketScoringType(wicketRuns) : type;
            bool requiresRuns = RunOptions.IsVisible == true;
            if (requiresRuns && (!int.TryParse((RunsBox.Text ?? string.Empty), out int value) || value < 0 || value > 998))
            { ErrorText.Text = "Enter a whole number from 0 to 998."; return; }
            if (_delivery is not null && SelectedBatterIndex != _originalStriker)
            {
                string? scope = await CricketPrompt.ChooseAsync(EntryLayout, "Update strike for later balls?", "Choose how far this correction should carry. Score figures are recalculated from the delivery history.", new("ball", "This delivery only"), new("over", "This and the rest of the over", true), new("innings", "This and all following deliveries"), new("cancel", "Cancel correction"));
                if (_completed || scope is null or "cancel") return;
                ScopeChoice.SelectedIndex = scope == "innings" ? 2 : scope == "over" ? 1 : 0;
            }
            else ScopeChoice.SelectedIndex = 0;
            if (_delivery is not null && BowlerChoice.SelectedIndex != _delivery.BowlerIndex)
            {
                string? propagate = await CricketPrompt.ChooseAsync(EntryLayout, "Apply bowler to this over?", "Should the selected bowler replace the bowler on the remaining deliveries in this over?", new("yes", "Yes — rest of this over", true), new("no", "This delivery only"), new("cancel", "Cancel correction"));
                if (_completed || propagate is null or "cancel") return;
                RestOfOverBowler.IsChecked = propagate == "yes";
            }
            // Prompts yield to input/remote commands. Recheck after the last
            // await so a confirmation never overwrites a newer match state.
            if (!EntryContextIsCurrent)
            { ErrorText.Text = "The match changed during confirmation. Cancel and reopen this delivery before applying it."; return; }
            int.TryParse((RunsBox.Text ?? string.Empty), out int entered);
            int runs = type switch
            {
                CricketDeliveryType.Dot => 0,
                CricketDeliveryType.Four => 4,
                CricketDeliveryType.Six => 6,
                CricketDeliveryType.NoBall or CricketDeliveryType.Wide => entered + 1,
                CricketDeliveryType.Wicket when dismissal is not DismissalType.RunOut and not DismissalType.ObstructingField
                    => scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall ? 1 : 0,
                CricketDeliveryType.Wicket when scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall => entered + 1,
                _ => entered
            };
            if (type is CricketDeliveryType.Bye or CricketDeliveryType.LegBye && runs == 0) { ErrorText.Text = "Choose at least one bye/leg-bye, or use a dot ball."; return; }
            string? fielder = FielderOptions.IsVisible == true && FielderChoice.SelectedIndex > 0 ? _innings.BowlingAttack[FielderChoice.SelectedIndex - 1].DisplayName : null;
            int victim = DismissedChoice.SelectedIndex >= 0 ? _dismissedIndices[DismissedChoice.SelectedIndex] : SelectedBatterIndex;
            var edit = new CricketDeliveryEdit
            {
                Type = type,
                Runs = runs,
                StrikerIndex = SelectedBatterIndex,
                BowlerIndex = BowlerChoice.SelectedIndex,
                NoBallRuns = (CricketNoBallRuns)Math.Max(0, NoBallChoice.SelectedIndex),
                WicketRuns = wicketRuns,
                RunOutCrossed = RunOutCrossed.IsChecked == true && type == CricketDeliveryType.Wicket && dismissal is DismissalType.RunOut or DismissalType.ObstructingField,
                CompletedRunsOnly = _delivery?.CompletedRunsOnly == true && type == _delivery.Type && runs == _delivery.Runs
                    && (CricketNoBallRuns)Math.Max(0, NoBallChoice.SelectedIndex) == _delivery.NoBallRuns,
                Dismissal = dismissal,
                Fielder = fielder,
                DismissedBatterIndex = victim,
                StrikeScope = (CricketCorrectionScope)Math.Max(0, ScopeChoice.SelectedIndex),
                BowlerForRestOfOver = RestOfOverBowler.IsChecked == true
            };
            if (_delivery is not null)
            { if (!_match.TryEditDelivery(_delivery, edit, out string error)) { ErrorText.Text = error; return; } }
            else
            {
                int before = _innings.Deliveries.Count;
                _match.RecordDelivery(runs, type, type == CricketDeliveryType.Wicket ? CricketMatchManager.DismissalLabel(dismissal, fielder, _innings.CurrentBowler?.DisplayName) : null,
                    edit.NoBallRuns, dismissal, fielder, victim, wicketRuns: wicketRuns, runOutCrossed: edit.RunOutCrossed);
                if (_innings.Deliveries.Count == before) { ErrorText.Text = _match.LastOperationError is {Length:>0} problem ? problem : "Scoring is paused. Select both batters and a bowler before adding this delivery."; return; }
            }
            ApplyButton.IsEnabled = false; Complete(true);
        }
        finally
        {
            _applying = false;
            if (!_completed)
                ApplyButton.IsEnabled = (CricketDeliveryType)TypeChoice.SelectedIndex != CricketDeliveryType.Wicket
                    || SelectedWicketIsAllowed;
        }
    }
    private int SelectedBatterIndex => BatterChoice.SelectedIndex >= 0 && BatterChoice.SelectedIndex < _batterIndices.Count ? _batterIndices[BatterChoice.SelectedIndex] : -1;
    private void RunOutCrossing_Changed(object? sender, RoutedEventArgs args) { if (!_initializing) UpdateFields(); }
    private void BuildChoiceTiles(WrapPanel panel, Avalonia.Controls.ComboBox choice)
    {
        panel.Children.Clear();
        for (int index = 0; index < choice.Items.Count; index++)
        {
            int selected = index;
            var tile = new Avalonia.Controls.Button { Tag = (choice, index), Content = choice.Items[index]?.ToString(), ContentTemplate = CricketPrompt.ButtonContent, Theme = NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "CricketActionButton"), MinHeight = 42, MinWidth = 128, MaxWidth = 240, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 7, 12, 7), FontSize = 14 };
            tile.Click += (_, _) => { choice.SelectedIndex = selected; UpdateFields(); }; panel.Children.Add(tile);
        }
    }
    private void RefreshChoiceTiles()
    {
        foreach (var panel in new[] { DismissalTiles, DismissedTiles, FielderTiles, BatterTiles, BowlerTiles })
            foreach (var tile in panel.Children.OfType<Avalonia.Controls.Button>())
                if (tile.Tag is ValueTuple<Avalonia.Controls.ComboBox, int> item)
                {
                    tile.Content = item.Item1.Items[item.Item2]?.ToString();
                    bool selected = item.Item1.SelectedIndex == item.Item2;
                    tile.BorderBrush = selected ? TeamVisuals.Accent(EntryColour) : Avalonia.Media.Brushes.SlateGray;
                    tile.BorderThickness = new Thickness(selected ? 2 : 1);
                    tile.Background = TeamVisuals.Panel(EntryColour, EntryColour, selected ? .5 : .12);
                    Avalonia.Automation.AutomationProperties.SetItemStatus(tile, selected ? "Selected" : "Not selected");
                }
    }
}

public partial class CricketDeliveryDialog
{
    private void Native_Batter_Changed(object? sender, SelectionChangedEventArgs e) { Batter_Changed(sender!, e); }
    private void Native_Details_Changed(object? sender, SelectionChangedEventArgs e) { Details_Changed(sender!, e); }
    private void Native_RunOutCrossing_Changed_Checked(object? sender, RoutedEventArgs e) { if (sender is Avalonia.Controls.Primitives.ToggleButton toggle && toggle.IsChecked != true) return; RunOutCrossing_Changed(sender!, e); }
    private void Native_Runs_Changed(object? sender, TextChangedEventArgs e) { Runs_Changed(sender!, e); }
}
