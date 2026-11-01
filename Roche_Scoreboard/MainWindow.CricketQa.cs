using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    /// <summary>Exercises the real cricket setup/reset path without saved-game writes.</summary>
    internal async Task<Dictionary<string, bool>> RunCricketLifecycleChecksAsync(string outputDirectory)
    {
        if (System.Windows.Application.Current is not App { IsSelfTestMode: true })
            throw new InvalidOperationException("Cricket checks require isolated self-test mode.");

        Dictionary<string, bool> checks = new();
        Directory.CreateDirectory(outputDirectory);
        foreach (var check in await RunOperatorViewportChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunSetupReflowChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunSettingsVisualChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        ApplySharedOutputDimensions(386, 193, remember: false);
        checks["all-mode-default-output-is-386x193"] = new OperatorWorkflowPreferencesData().OutputWindowWidth == 386
            && new OperatorWorkflowPreferencesData().OutputWindowHeight == 193;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            bool canSelect = (bool)InvokeCricketQa(SportSelectionPanel, "TryBeginSelection")!;
            checks[$"reset-cycle-{cycle}-sport-picker-accepts-click"] = canSelect;
            int selectionGeneration=(int)typeof(SportSelectionScreen).GetField("_selectionGeneration",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(SportSelectionPanel)!;
            InvokeCricketQa(SportSelectionPanel, "CompleteSelection", SportMode.Cricket,selectionGeneration);
            checks[$"reset-cycle-{cycle}-wizard-visible"] = CricketSetupWizardPanel.IsVisible;
            CricketSetupWizardPanel.OversDisplay.Text = "25";
            checks[$"reset-cycle-{cycle}-keyboard-overs-entry"] = CricketSetupWizard.TryParseOvers("25", out int overs)
                && overs == 25 && !CricketSetupWizard.TryParseOvers("0", out _)
                && !CricketSetupWizard.TryParseOvers("101", out _)
                && !CricketSetupWizard.TryParseOvers("2.5", out _);

            InvokeCricketQa(CricketSetupWizardPanel, "FinishWizard");
            checks[$"reset-cycle-{cycle}-default-elevens"] = _cricketMatch?.TeamAPlayers.Count == 11
                && _cricketMatch.TeamBPlayers.Count == 11;
            checks[$"reset-cycle-{cycle}-typed-overs-used-by-match"] = _cricketMatch?.TotalOvers == 25;
            checks[$"reset-cycle-{cycle}-no-loading-screen"] = MatchLoadingPanel.Visibility == Visibility.Collapsed
                && CricketControlPanelView.Visibility == Visibility.Visible;
            if (cycle == 0)
            {
                // Reset while the entry animation is still running. Its old
                // completion must not reopen a selector on the next game.
                OnCricketResetRequested();
                await Task.Delay(450);
                checks["reset-during-entry-cancels-late-selection"] = SportSelectionPanel.IsVisible
                    && SportSelectionPanel.IsHitTestVisible && _cricketMatch is null
                    && CricketControlPanelView.BatterSelectionOverlay.Visibility == Visibility.Collapsed;
                continue;
            }

            await Task.Delay(450);
            checks[$"reset-cycle-{cycle}-starts-at-toss-without-forced-player-dialog"] = _cricketMatch!.TossPending
                && CricketControlPanelView.BatterSelectionOverlay.Visibility == Visibility.Collapsed;
            CaptureCricketQa(CricketControlPanelView, Path.Combine(outputDirectory, "toss-first-control-panel.png"));
            CaptureCricketQa(_cricketScorebug!, Path.Combine(outputDirectory, "toss-first-public-output.png"));
            _cricketMatch.CompleteToss(true, true);
            InvokeCricketQa(CricketControlPanelView, "SelectOpeningBatters_Click", this, new RoutedEventArgs());
            checks[$"reset-cycle-{cycle}-opening-selector-visible"] =
                CricketControlPanelView.BatterSelectionOverlay.Visibility == Visibility.Visible;
            CaptureCricketQa(CricketControlPanelView, Path.Combine(outputDirectory, "batter-selector.png"));
            InvokeCricketQa(CricketControlPanelView, "ConfirmBatterSelection_Click", this, new RoutedEventArgs());
            InvokeCricketQa(CricketControlPanelView, "ConfirmBatterSelection_Click", this, new RoutedEventArgs());
            CaptureCricketQa(CricketControlPanelView, Path.Combine(outputDirectory, "bowler-selector.png"));
            InvokeCricketQa(CricketControlPanelView, "ConfirmBowlerSelection_Click", this, new RoutedEventArgs());
            var match = _cricketMatch!;
            match.RecordDelivery(4, CricketDeliveryType.Four);
            checks[$"reset-cycle-{cycle}-score-applies-to-model-and-panel"] = match.TotalRuns == 4
                && CricketControlPanelView.HeaderScore.Text == "0/4";
            // A live automatic overlay can first complete its 620 ms exit,
            // followed by the boundary hold and exit. Verify convergence after
            // that lifecycle instead of racing a fixed 3.5 second snapshot.
            var scoreDeadline=System.Diagnostics.Stopwatch.StartNew();
            while(scoreDeadline.Elapsed<TimeSpan.FromSeconds(6)
                && (_cricketScorebug!.ScoreRuns.Text!="4" || _cricketScorebug.EventShowing || _cricketScorebug.MomentAwaitingLayout))
                await Task.Delay(50);
            checks[$"reset-cycle-{cycle}-score-animation-settles"] = _cricketScorebug!.ScoreRuns.Text == "4"
                && !_cricketScorebug.EventShowing && !_cricketScorebug.MomentAwaitingLayout;

            if (cycle == 1)
            {
                bool previousScreensaver = _screensaverEnabled;
                OnGlobalScreensaverEnabledChanged(false);
                checks["idle-screensaver-preference-does-not-hide-active-cricket"] = _cricketDisplayWindow!.IsVisible;
                _screensaverEnabled = previousScreensaver;
                _trainingSession = new TrainingSession();
                _trainingScorebug = new TrainingScorebugControl();
                _trainingScorebug.Bind(_trainingSession);
                EnsureTrainingDisplayWindow();
                OnGlobalOutputDimensionsChanged(640, 320);
                await Task.Delay(200);
                checks["global-size-updates-all-three-outputs"] = OutputWindows().All(window =>
                    PixelSizeHelper.GetWindowPixels(window, window.Width, window.Height) == (640, 320));
                PixelSizeHelper.SetWindowPixels(_cricketDisplayWindow!, 512, 256);
                await Task.Delay(200);
                checks["direct-cricket-resize-updates-other-outputs"] = OutputWindows().All(window =>
                    PixelSizeHelper.GetWindowPixels(window, window.Width, window.Height) == (512, 256));
                checks["all-output-clients-are-borderless"] = OutputWindows().All(window =>
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    GetWindowRect(handle, out var frame);
                    GetClientRect(handle, out var client);
                    return window.WindowStyle == WindowStyle.None && !window.AllowsTransparency
                        && frame.Right - frame.Left == client.Right
                        && frame.Bottom - frame.Top == client.Bottom;
                });
                _trainingDisplayWindow!.Close();
                _displayWindow?.Hide();

                for (int i = 0; i < 5; i++)
                {
                    match.RecordDelivery(6, CricketDeliveryType.Six);
                    match.UndoLastDelivery();
                }
                await Task.Delay(1400);
                checks["rapid-score-undo-output-converges"] = _cricketScorebug!.ScoreRuns.Text == match.TotalRuns.ToString();
                checks["rapid-score-undo-output-stays-visible"] = _cricketScorebug.ScoreRuns.Opacity > .99
                    && Math.Abs(_cricketScorebug.ScoreRunsTranslate.Y) < .1;

                InvokeCricketQa(CricketControlPanelView, "SwitchPresentation", "batting");
                match.RecordDelivery(1, CricketDeliveryType.Runs);
                InvokeCricketQa(CricketControlPanelView, "SwitchPresentation", "scorebug");
                checks["summary-switch-keeps-live-match"] = _cricketMatch == match
                    && CricketControlPanelView.HeaderScore.Text == "0/5";
                checks["delivery-log-removes-undone-events"] = CricketControlPanelView.EventLog.Children
                    .OfType<StackPanel>().Sum(group => ((StackPanel)group.Children[1]).Children.Count)
                    == match.CurrentInnings!.Deliveries.Count;

                var bug = _cricketScorebug;
                _cricketDisplayWindow!.Close();
                checks["closed-output-releases-visual-parent"] = bug!.Parent is null;
                EnsureCricketDisplayWindow();
                await Task.Delay(150);
                checks["output-reopens-with-same-scorebug"] = _cricketDisplayWindow!.IsVisible
                    && ReferenceEquals(_cricketScorebug, bug) && bug.Parent is not null;
                checks["reopened-output-keeps-shared-size"] = PixelSizeHelper.GetWindowPixels(
                    _cricketDisplayWindow, _cricketDisplayWindow.Width, _cricketDisplayWindow.Height) == (512, 256);

                for (int i = 0; i < 6; i++)
                {
                    _cricketDisplayWindow.Hide();
                    EnsureCricketDisplayWindow();
                }
                checks["hidden-output-reuses-existing-window"] = _cricketDisplayWindow.IsVisible;
                InvokeCricketQa(GlobalSettingsPanel, "ResetOutputDimensions_Click", this, new RoutedEventArgs());
                await Task.Delay(800);
                checks["reset-size-restores-shared-386x193"] = OutputWindows().All(window =>
                    PixelSizeHelper.GetWindowPixels(window, window.Width, window.Height) == (386, 193));
                CricketControlPanelView.MainTabControl.SelectedItem = CricketControlPanelView.OutputTab;
                CricketControlPanelView.UpdateLayout();
                await Task.Delay(150);
                foreach (var target in new[] { "batting", "bowling", "scorebug", "batting" })
                {
                    var button = (System.Windows.Controls.Button)CricketControlPanelView.FindName("Presentation" + target);
                    button.ApplyTemplate();
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    checks["presentation-outline-" + target] = System.Windows.Automation.AutomationProperties.GetItemStatus(button) == "Selected";
                }
                await Task.Delay(750);
                checks["rapid-presentations-leave-latest-card-visible"] = _cricketSummary!.Visibility == Visibility.Visible
                    && _cricketSummary.HeaderText.Text.Contains("BATTING") && Math.Abs(_cricketSummary.RootTranslate.Y) < .1;
                var dismissed = match.CurrentInnings!.BattingOrder[4];
                dismissed.IsOut = true;
                dismissed.HowOut = DismissalType.Caught;
                dismissed.DismissalFielder = "Fielding Player";
                dismissed.DismissalBowler = "Bowling Player";
                dismissed.Runs = 123;
                dismissed.BallsFaced = 145;
                _cricketSummary.RefreshBattingSummary(match);
                CaptureCricketQa(_cricketSummary!, Path.Combine(outputDirectory, "batting-page-one.png"));
                dismissed.IsOut = false;
                dismissed.HowOut = DismissalType.NotOut;
                dismissed.DismissalFielder = dismissed.DismissalBowler = null;
                dismissed.Runs = dismissed.BallsFaced = 0;
                for (int i = 0; i < 18; i++) InvokeCricketQa(_cricketSummary!, "TickSummary");
                _cricketSummary!.UpdateLayout();
                checks["batting-second-page-has-six-players"] = _cricketSummary.RowsPanel.Children.Count == 6;
                CaptureCricketQa(_cricketSummary!, Path.Combine(outputDirectory, "batting-page-two.png"));
                InvokeCricketQa(CricketControlPanelView, "SwitchPresentation", "bowling");
                await Task.Delay(450);
                CaptureCricketQa(_cricketSummary!, Path.Combine(outputDirectory, "bowling-summary.png"));
                InvokeCricketQa(CricketControlPanelView, "SwitchPresentation", "scorebug");
                await Task.Delay(450);
                var weather = new WeatherSnapshot(24, 23, 12, 27, 0, "☀", "Clear", Array.Empty<HourlyForecast>());
                _cricketScorebug!.UpdateWeatherSnapshot(weather);
                CricketControlPanelView.UpdateWeatherSnapshot(weather);
                checks["shared-weather-reaches-cricket-output-and-controls"] = _cricketScorebug.TopWeatherText.Text.Contains("24")
                    && CricketControlPanelView.CricketWeatherTemperature.Text.Contains("24");
                checks["cricket-output-has-no-ticker"] = _cricketScorebug.FindName("MarqueeText") is null;
                checks["cricket-canvas-fills-output-aspect"] = Math.Abs(
                    _cricketScorebug.CricketDesignRoot.Width / _cricketScorebug.CricketDesignRoot.Height
                    - _cricketScorebug.ActualWidth / _cricketScorebug.ActualHeight) < .001;
                CaptureCricketQa(_cricketScorebug!, Path.Combine(outputDirectory, "cricket-output-386x193.png"));
                CaptureCricketQa(CricketControlPanelView, Path.Combine(outputDirectory, "cricket-control-panel.png"));
                OpenCricketApplicationPreferences();
                checks["cricket-preferences-opens-without-ending-match"] = GlobalSettingsPanel.IsVisible && ReferenceEquals(_cricketMatch, match);
                OnGlobalSettingsBackRequested();
                checks["cricket-preferences-returns-to-live-control-panel"] = CricketControlPanelView.IsVisible && !GlobalSettingsPanel.IsVisible && ReferenceEquals(_cricketMatch, match);

                for (int ball = 0; ball < 18; ball++)
                {
                    if (match.CurrentInnings!.NeedsBowlerSelection)
                        InvokeCricketQa(CricketControlPanelView, "ConfirmBowlerSelection_Click", this, new RoutedEventArgs());
                    match.RecordDelivery(0, CricketDeliveryType.Dot);
                }
                for (int refresh = 0; refresh < 20; refresh++) _cricketScorebug.UpdateFromMatch(match);
            }

            OnCricketResetRequested();
            checks[$"reset-cycle-{cycle}-selection-restored"] = SportSelectionPanel.IsVisible
                && SportSelectionPanel.IsHitTestVisible && _cricketDisplayWindow is null;
            string oldPanelScore = CricketControlPanelView.HeaderScore.Text;
            match.RecordDelivery(2, CricketDeliveryType.Runs);
            checks[$"reset-cycle-{cycle}-old-game-detached"] = CricketControlPanelView.HeaderScore.Text == oldPanelScore;
        }
        foreach (var check in await RunCricketReadabilityChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketWorkflowVisualChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketOverlayVisualChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketTossVisualChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketRefinementChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketMotionChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketOutputHandoffChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketPolishChecksAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in CricketOutputChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in await CricketWorkflowUiChecks.RunAsync(outputDirectory)) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketReviewChecksAsync(outputDirectory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketFreeHitChecksAsync(outputDirectory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketRulesSetupChecksAsync(outputDirectory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketInteractionChecksAsync(outputDirectory)) checks[check.Key]=check.Value;
        foreach(var check in await RunCricketBroadcastExpansionChecksAsync(outputDirectory))checks[check.Key]=check.Value;
        foreach (var check in await CricketEditingChecks.RunDialogChecksAsync()) checks[check.Key] = check.Value;
        foreach (var check in await CricketClubChecks.RunAsync(Path.Combine(outputDirectory, "club-logos"))) checks[check.Key] = check.Value;
        foreach (var check in await CricketPersistenceChecks.RunAsync(Path.Combine(outputDirectory, "persistence"))) checks[check.Key] = check.Value;
        return checks;
    }

    private async Task<Dictionary<string, bool>> RunOperatorViewportChecksAsync(string directory)
    {
        Dictionary<string, bool> checks = new();
        var sport = new SportSelectionScreen();
        var window = new Window { Width = 1200, Height = 800, Content = sport, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Task.Delay(1200);
            var hover = new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0);
            InvokeCricketQa(sport, "Card_MouseEnter", sport.AflCard, hover);
            InvokeCricketQa(sport, "TrainingTile_MouseEnter", sport.TrainingCard, hover);
            await Task.Delay(400);
            checks["launch-card-hover-keeps-fullscreen-card-bounds-stable"] = CricketQaBoundsWithin(sport.AflCard, sport) && CricketQaBoundsWithin(sport.TrainingCard, sport);
            InvokeCricketQa(sport, "Card_MouseLeave", sport.AflCard, hover);
            InvokeCricketQa(sport, "TrainingTile_MouseLeave", sport.TrainingCard, hover);
            checks["resume-shortcut-hidden-before-explicit-deferral"] = sport.ResumeSavedMatchButton.Visibility == Visibility.Collapsed;
            var deferred = new ResumeGameDialog(new GameState());
            InvokeCricketQa(deferred, "KeepSaved_Click", this, new RoutedEventArgs());
            checks["continue-later-records-explicit-deferral"] = deferred.DeferredChosen;
            sport.SetDeferredResumeAvailable(deferred.DeferredChosen);
            checks["resume-shortcut-shown-after-explicit-deferral"] = sport.ResumeSavedMatchButton.Visibility == Visibility.Visible;
            foreach (var size in new[] { (420, 320), (420, 820), (1600, 500), (1000, 800) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                await Task.Delay(180); sport.UpdateLayout();
                checks[$"sport-choices-fit-{size.Item1}x{size.Item2}"] = new[] { sport.AflCard, sport.TrainingCard }
                    .All(card => CricketQaBoundsWithin(card, sport) && card.ActualWidth > 20 && card.ActualHeight > 20);
                CaptureCricketQa(sport, Path.Combine(directory, $"sport-selection-{size.Item1}x{size.Item2}.png"));
            }
            foreach (var page in new (string Name, FrameworkElement View)[]
            {
                ("cricket-console", new CricketControlPanel()), ("training-console", new TrainingControlPanel()),
                ("afl-setup", new SetupWizard()), ("cricket-setup", new CricketSetupWizard())
            })
            {
                window.Content = page.View;
                await Task.Delay(1200); // Inspect the settled setup, including its staged navigation entrance.
                var host = FindQaVisual<OperatorViewport>(page.View)!;
                foreach (var size in new[] { (420, 320), (780, 1100), (1800, 600) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    await Task.Delay(150); page.View.UpdateLayout();
                    var canvas = (FrameworkElement)host.Content;
                    checks[$"{page.Name}-fills-or-scrolls-{size.Item1}x{size.Item2}"] =
                        canvas.ActualWidth >= host.ViewportWidth - 1 && canvas.ActualHeight >= host.ViewportHeight - 1;
                    if (host is WizardViewport)
                    {
                        var indicator = (FrameworkElement)page.View.FindName("StepIndicator");
                        var bounds = indicator.TransformToAncestor(page.View).TransformBounds(new Rect(indicator.RenderSize));
                        var navigation = (FrameworkElement)page.View.FindName("NavBar");
                        checks[$"{page.Name}-step-navigation-fits-{size.Item1}x{size.Item2}"] = bounds.Left >= -1
                            && bounds.Right <= page.View.ActualWidth + 1 && navigation.IsVisible && navigation.Opacity > .99;
                    }
                    host.ScrollToBottom(); host.ScrollToRightEnd();
                    await Task.Delay(30);
                    checks[$"{page.Name}-controls-reachable-{size.Item1}x{size.Item2}"] =
                        (host.ScrollableHeight < 1 || host.VerticalOffset > 0) && (host.ScrollableWidth < 1 || host.HorizontalOffset > 0);
                    host.ScrollToTop(); host.ScrollToLeftEnd();
                    CaptureCricketQa(page.View, Path.Combine(directory, $"{page.Name}-{size.Item1}x{size.Item2}.png"));
                }
            }
        }
        finally { window.Close(); }
        bool oldEnabled = _screensaverEnabled;
        _screensaverEnabled = true;
        ShowOutputScreensaver(manualSelection: false);
        OnGlobalScreensaverEnabledChanged(false);
        checks["idle-screensaver-off-hides-output-windows"] = OutputWindows().All(output => !output.IsVisible);
        ShowOutputScreensaver(manualSelection: false);
        checks["disabled-idle-screensaver-does-not-reopen-afl-output"] = OutputWindows().All(output => !output.IsVisible);
        _screensaverEnabled = oldEnabled;
        return checks;
    }

    private static T? FindQaVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindQaVisual<T>(VisualTreeHelper.GetChild(root, i)) is T child) return child;
        return null;
    }

    private static bool CricketQaBoundsWithin(FrameworkElement element, FrameworkElement root)
    {
        if(!ReferenceEquals(element,root) && !root.IsAncestorOf(element))return false;
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1;
    }

    private static async Task<Dictionary<string, bool>> RunCricketReadabilityChecksAsync(string directory)
    {
        Dictionary<string, bool> checks = new();
        var match = CricketReliabilityChecks.CreateMatch(ready: true);
        match.Format = CricketFormat.MultiDay;
        match.TeamAPrimaryColor = "#FFFFFF";
        var view = new CricketScorebugControl();
        var window = new Window { Width = 386, Height = 193, Content = view, ShowInTaskbar = false };
        ApplyOutputWindowChrome(window);
        window.Show();
        try
        {
            view.UpdateFromMatch(match);
            checks["scorebug-six-empty-balls-visible-before-first-delivery"] = view.CurrentOverBallsPanel.Children.Count == 6;
            checks["cricket-broadcast-overlays-removed"] = view.FindName("EventOverlay") is null && view.FindName("StatsBarBg") is null && view.FindName("PowerplayBarBorder") is null;
            checks["scorebug-duplicate-team-label-removed"] = view.FindName("BattingAbbrText") is null;
            foreach (var delivery in new[] { CricketDeliveryType.Wide, CricketDeliveryType.NoBall })
            {
                match.RecordDelivery(1, delivery);
                view.UpdateFromMatch(match);
            }
            checks["scorebug-extra-deliveries-add-visible-circles"] = view.CurrentOverBallsPanel.Children.Count == 8;
            match.UndoLastDelivery();
            view.UpdateFromMatch(match);
            checks["scorebug-undo-removes-extra-circle"] = view.CurrentOverBallsPanel.Children.Count == 7;
            await Task.Delay(800); // Let scoring/undo transforms settle before testing reserved-width strings.
            view.ScoreRuns.Text = "888";
            view.ScoreWickets.Text = "10";
            view.StrikerRuns.Text = view.NonStrikerRuns.Text = "888";
            view.StrikerBalls.Text = view.NonStrikerBalls.Text = "888";
            view.BowlerFigures.Text = "10-888";
            view.BowlerOvers.Text = "88.5";
            view.OversText.Text = "888.5";
            view.ExtrasValue.Text = "888";
            view.TopTimeText.Text = "12:59 PM";
            view.TopWeatherText.Text = "☀ 38°";
            view.UpdateLayout();
            checks["scorebug-main-panels-fill-output-width"] = view.MainScoreboard.ActualWidth > 950;
            checks["maximum-digit-fixture-is-actually-displayed"] = view.ScoreRuns.Text == "888" && view.BowlerFigures.Text == "10-888";
            foreach (TextBlock number in new[] { view.ScoreRuns, view.ScoreWickets, view.StrikerRuns, view.NonStrikerRuns, view.StrikerBalls, view.NonStrikerBalls, view.BowlerFigures, view.BowlerOvers, view.OversText, view.ExtrasValue, view.TopTimeText })
                checks["reserved-digits-fit-" + number.Name] = CricketTextInkFits(number);
            checks["white-team-score-text-has-smart-contrast"] = ((LinearGradientBrush)view.LeftScoreRail.Background).GradientStops.All(stop =>
                ContrastHelper.ContrastRatio(((SolidColorBrush)view.ScoreRuns.Foreground).Color, stop.Color) >= 4.5);
            CaptureCricketQa(view, Path.Combine(directory, "scorebug-maximum-digits-white-team.png"));
            window.Width = 280; window.Height = 193;
            await Task.Delay(150);
            checks["narrow-output-keeps-reserved-score-digits"] = CricketTextInkFits(view.ScoreRuns) && CricketTextInkFits(view.StrikerRuns);
            CaptureCricketQa(view, Path.Combine(directory, "scorebug-narrow-280x193.png"));
            window.Width = 386; window.Height = 193;
            match.TeamALogoPath = CreateCricketQaLogo(directory, "A", System.Windows.Media.Colors.RoyalBlue);
            match.TeamBLogoPath = CreateCricketQaLogo(directory, "B", System.Windows.Media.Colors.Firebrick);
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            view.UpdateFromMatch(match);
            var newest = (Border)view.CurrentOverBallsPanel.Children[match.CurrentInnings!.CurrentOverBalls.Count - 1];
            checks["new-ball-circle-has-entry-animation"] = ((ScaleTransform)newest.RenderTransform).HasAnimatedProperties;
            checks["dot-ball-uses-centred-solid-dot"] = FindQaVisual<System.Windows.Shapes.Ellipse>(newest) is not null;
            await Task.Delay(500);
            checks["new-ball-circle-settles-without-layout-shift"] = Math.Abs(((ScaleTransform)newest.RenderTransform).ScaleX - 1) < .01 && newest.Opacity > .99;
            checks["both-team-logos-reach-main-scorebug"] = view.BattingLogo.Source is not null && view.BowlingHeaderLogo.Source is not null;
            CaptureCricketQa(view, Path.Combine(directory, "scorebug-team-logos.png"));

            var selectionMatch = CricketReliabilityChecks.CreateMatch();
            selectionMatch.TeamALogoPath = match.TeamALogoPath; selectionMatch.TeamBLogoPath = match.TeamBLogoPath;
            var panel = new CricketControlPanel();
            window.Content = panel; window.Width = 420; window.Height = 320;
            panel.SetMatch(selectionMatch); panel.TriggerInitialSelection();
            await Task.Delay(220); panel.UpdateLayout();
            checks["small-console-stacks-instead-of-horizontal-clipping"] = panel.ConsoleViewport.ScrollableWidth < 1;
            checks["small-batter-selector-confirm-button-fits"] = CricketQaBoundsWithin(panel.ConfirmBatterButton, panel);
            checks["unplayed-batters-do-not-show-unused-zero-figures"] = panel.BatterSelectionList.Items.Cast<object>().All(item => (string)item.GetType().GetProperty("Figures")!.GetValue(item)! == "");
            checks["batter-selector-shows-team-logo"] = panel.BatterSelectionLogo.Source is not null;
            checks["match-centre-control-removed"] = panel.FindName("Presentationcentre") is null;
            CaptureCricketQa(panel, Path.Combine(directory, "batter-selector-small-team-themed.png"));
            InvokeCricketQa(panel, "ConfirmBatterSelection_Click", panel, new RoutedEventArgs());
            InvokeCricketQa(panel, "ConfirmBatterSelection_Click", panel, new RoutedEventArgs());
            await Task.Delay(120); panel.UpdateLayout();
            checks["small-bowler-selector-confirm-button-fits"] = CricketQaBoundsWithin(panel.ConfirmBowlerButton, panel);
            checks["bowler-selector-shows-team-logo"] = panel.BowlerSelectionLogo.Source is not null;
            CaptureCricketQa(panel, Path.Combine(directory, "bowler-selector-small-team-themed.png"));
            panel.ClearMatch();
        }
        finally { view.Dispose(); window.Close(); }
        return checks;
    }

    private static string CreateCricketQaLogo(string directory, string letter, System.Windows.Media.Color colour)
    {
        string path = Path.Combine(directory, $"fixture-logo-{letter}.png");
        var drawing = new DrawingVisual();
        using (DrawingContext context = drawing.RenderOpen())
        {
            var shield = new StreamGeometry();
            using (var shape = shield.Open())
            {
                shape.BeginFigure(new System.Windows.Point(18, 18), true, true);
                shape.LineTo(new System.Windows.Point(174, 18), true, false);
                shape.LineTo(new System.Windows.Point(162, 140), true, false);
                shape.LineTo(new System.Windows.Point(96, 182), true, false);
                shape.LineTo(new System.Windows.Point(30, 140), true, false);
            }
            context.DrawGeometry(new SolidColorBrush(colour), new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 7), shield);
            var text = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Bahnschrift"), 112, System.Windows.Media.Brushes.White, 1);
            context.DrawText(text, new System.Windows.Point((192 - text.Width) / 2, 23));
        }
        var bitmap = new RenderTargetBitmap(192, 192, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path); encoder.Save(stream);
        return path;
    }

    private static bool CricketTextInkFits(TextBlock text)
    {
        var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, System.Windows.Media.Brushes.White, 1);
        if (text.Parent is not FrameworkElement parent) return false;
        double width = parent.ActualWidth;
        if (parent is Grid grid && grid.ColumnDefinitions.Count > 0)
            width = grid.ColumnDefinitions[Grid.GetColumn(text)].ActualWidth;
        var ink = formatted.BuildGeometry(new System.Windows.Point()).Bounds;
        double top = (parent.ActualHeight - formatted.Height) / 2 + ink.Top;
        return formatted.WidthIncludingTrailingWhitespace <= width + .5 && top >= -.5 && top + ink.Height <= parent.ActualHeight + .5;
    }

    private static async Task<Dictionary<string, bool>> RunSettingsVisualChecksAsync(string directory)
    {
        Dictionary<string, bool> checks = new();
        var app = (App)System.Windows.Application.Current;
        var oldTheme = app.ThemeService.CurrentTheme;
        var oldDensity = app.ThemeService.CurrentDensity;
        var screen = new GlobalSettingsScreen();
        screen.ThemeChanged += app.ThemeService.SetTheme;
        screen.DensityChanged += app.ThemeService.SetDensity;
        var window = new Window { Content = screen, Width = 1000, Height = 840, ShowInTaskbar = false };
        window.Show();
        try
        {
            app.ThemeService.SetTheme(AppTheme.Light);
            screen.SetApplicationPreferences(AppTheme.Light, UiDensity.Comfortable, true);
            app.ThemeService.SetDensity(UiDensity.Comfortable);
            await Task.Delay(100);
            screen.UpdateLayout();
            var light = ((SolidColorBrush)screen.ThemeCombo.Foreground).Color;
            double comfortable = screen.QuickHintsToggle.ActualHeight;
            CaptureCricketQa(screen, Path.Combine(directory, "settings-light.png"));
            screen.ThemeCombo.SelectedIndex = 1;
            screen.DensityCombo.SelectedIndex = 1;
            await Task.Delay(150);
            screen.UpdateLayout();
            checks["settings-theme-selection-changes-visible-text"] = light != ((SolidColorBrush)screen.ThemeCombo.Foreground).Color;
            checks["settings-density-selection-changes-control-size"] = screen.QuickHintsToggle.ActualHeight < comfortable;
            checks["settings-theme-and-density-use-selectable-tiles"] = screen.DarkThemeTile.IsChecked == true && screen.CompactTile.IsChecked == true && screen.ThemeCombo.Visibility == Visibility.Collapsed;
            checks["settings-toggle-uses-custom-track"] = screen.QuickHintsToggle.Template.FindName("Track", screen.QuickHintsToggle) is Border;
            var togglePeer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(screen.QuickHintsToggle);
            ((System.Windows.Automation.Provider.IToggleProvider)togglePeer).Toggle();
            checks["settings-custom-toggle-responds-to-activation"] = screen.QuickHintsToggle.IsChecked == false;
            CaptureCricketQa(screen, Path.Combine(directory, "settings-dark-compact.png"));
            screen.GoalAnimationCombo.IsDropDownOpen = true;
            await Task.Delay(300);
            var popup = (System.Windows.Controls.Primitives.Popup)screen.GoalAnimationCombo.Template.FindName("PART_Popup", screen.GoalAnimationCombo);
            if (popup.Child is FrameworkElement popupChild)
            {
                CaptureCricketQa(popupChild, Path.Combine(directory, "settings-dropdown.png"));
                // Material backplates are composited, so measure the rendered fill,
                // rather than treating a gradient/translucent brush as a solid colour.
                var rendered = new RenderTargetBitmap(Math.Max(1,(int)popupChild.ActualWidth),Math.Max(1,(int)popupChild.ActualHeight),96,96,PixelFormats.Pbgra32);
                rendered.Render(popupChild);
                byte[] pixel = new byte[4];
                rendered.CopyPixels(new Int32Rect(Math.Min(8,rendered.PixelWidth-1),rendered.PixelHeight/2,1,1),pixel,4,0);
                var fill = System.Windows.Media.Color.FromRgb(pixel[2],pixel[1],pixel[0]);
                checks["settings-dropdown-items-have-readable-contrast"] = ContrastHelper.ContrastRatio(
                    ((SolidColorBrush)screen.GoalAnimationCombo.Foreground).Color,fill) >= 4.5;
            }
            screen.GoalAnimationCombo.IsDropDownOpen = false;
            window.Width = 620;
            window.Height = 700;
            await Task.Delay(150);
            screen.UpdateLayout();
            checks["settings-narrow-window-stacks-appearance-fields"] = Grid.GetRow(screen.DensityChoicePanel) == 1 && screen.DensityChoicePanel.ActualWidth <= screen.DensityFieldPanel.ActualWidth;
            checks["settings-narrow-window-stacks-output-fields"] = Grid.GetRow(screen.OutputDimensionsFieldPanel) == 1;
            CaptureCricketQa(screen, Path.Combine(directory, "settings-narrow.png"));
        }
        finally
        {
            window.Close();
            app.ThemeService.SetTheme(oldTheme);
            app.ThemeService.SetDensity(oldDensity);
        }
        return checks;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QaRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out QaRect rect);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out QaRect rect);

    private static object? InvokeCricketQa(object target, string method, params object[] args)
    {
        var candidate=target.GetType().GetMethods(BindingFlags.NonPublic|BindingFlags.Instance)
            .Where(item=>item.Name==method && item.GetParameters().Length==args.Length)
            .Where(item=>item.GetParameters().Select((parameter,index)=>args[index] is null ? !parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) is not null : parameter.ParameterType.IsInstanceOfType(args[index])).All(matches=>matches))
            .OrderBy(item=>item.DeclaringType==target.GetType()?0:1).FirstOrDefault()
            ?? throw new MissingMethodException(target.GetType().FullName,method);
        return candidate.Invoke(target,args);
    }

    private static void CaptureCricketQa(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        RenderTargetBitmap bitmap = new(Math.Max(1, (int)element.ActualWidth),
            Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
