using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    internal async Task<Dictionary<string,bool>> RunCricketMotionReviewChecksAsync(string directory)
    {
        var checks=await RunCricketOutputHandoffChecksAsync(directory);
        foreach(var check in await RunCricketReadabilityChecksAsync(directory))checks[check.Key]=check.Value;
        foreach(var check in await RunCricketMotionChecksAsync(directory))checks[check.Key]=check.Value;
        foreach(var check in await RunCricketBroadcastExpansionChecksAsync(directory))checks[check.Key]=check.Value;
        return checks;
    }
    private async Task<Dictionary<string, bool>> RunCricketMotionChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(); match.TossPending = true;
        match.TeamALogoPath = Path.Combine(directory, "fixture-logo-A.png"); match.TeamBLogoPath = Path.Combine(directory, "fixture-logo-B.png");
        var bug = new CricketScorebugControl { EventAnimationsEnabled = false };
        var overlays = new CricketOverlayHost(match) { AutomaticEnabled = false }; overlays.Scene.Children.Add(bug);
        match.MatchChanged += () => bug.UpdateFromMatch(match); bug.UpdateFromMatch(match);
        var window = new Window { Content = overlays, Width = 386, Height = 193, ShowInTaskbar = false }; ApplyOutputWindowChrome(window); window.Show();
        try
        {
            await Task.Delay(120);
            checks["pre-toss-screen-shows-both-real-team-logos-and-team-names"] = bug.PregameScreen.IsVisible && bug.PregameHomeLogo.Source is not null && bug.PregameAwayLogo.Source is not null && bug.PregameHomeName.Text == match.TeamAName.ToUpperInvariant();
            CaptureCricketQa(overlays, Path.Combine(directory, "pre-toss-team-introduction-386x193.png"));
            match.CompleteToss(true, true); await Task.Delay(150);
            checks["toss-handoff-animates-from-team-introduction-without-delaying-state"] = !match.TossPending && bug.MainScoreboard.Visibility == Visibility.Visible && bug.PregameScreen.Visibility == Visibility.Visible;
            CaptureCricketQa(overlays, Path.Combine(directory, "toss-handoff-mid-transition.png"));
            window.Content = null; await Task.Delay(50); window.Content = overlays;
            await Task.Delay(700);
            checks["toss-handoff-settles-with-no-cover-over-the-ready-scorebug"] = bug.PregameScreen.Visibility == Visibility.Collapsed && bug.MainScoreboard.Opacity > .99;
            window.Content = null; await Task.Delay(50); window.Content = overlays; await Task.Delay(120);
            checks["toss-screen-cannot-remain-over-output-after-close-reopen"] = bug.PregameScreen.Visibility == Visibility.Collapsed;
            match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0); await Task.Delay(450);
            checks["broadcast-font-is-consistently-bolder-normal-width-arial"] = new[] { bug.ScoreRuns, bug.ScoreWickets, bug.StrikerName, bug.BowlerName }.All(text => text.FontFamily.Source == "Arial" && text.FontWeight == FontWeights.Bold);
            match.RecordDelivery(1, CricketDeliveryType.NoBall); match.RecordDelivery(1, CricketDeliveryType.NoBall); match.RecordDelivery(0, CricketDeliveryType.Dot);
            checks["bowler-details-can-show-economy-and-only-nonzero-extras"] = (bool)InvokeCricketQa(bug, "ShowBowlerDetails")!;
            await Task.Delay(450);
            checks["bowler-details-omit-zero-wides-and-unrelated-byes"] = bug.BowlerDetailText.Text.Contains("2 NO-BALLS") && bug.BowlerDetailText.Text.Contains("ECONOMY") && !bug.BowlerDetailText.Text.Contains("WIDE") && !bug.BowlerDetailText.Text.Contains("BYES");
            CaptureCricketQa(overlays, Path.Combine(directory, "bowler-detail-strip-only-relevant-extras.png"));
            match.RecordDelivery(1, CricketDeliveryType.Runs); await Task.Delay(50);
            var strikeIndicator = QaDescendants(bug.MainScoreboard).OfType<TextBlock>().Single(text => Equals(text.Tag, "moving-strike-indicator"));
            checks["strike-change-has-an-animation-without-moving-player-rows"] = strikeIndicator.IsVisible && strikeIndicator.RenderTransform.HasAnimatedProperties
                && bug.StrikerIndicator.Opacity < .01 && bug.NonStrikerIndicator.Opacity < .01;
            await WaitCricketQaStateAsync(window,()=>bug.BowlerDetailLane.Visibility==Visibility.Hidden && bug.OverBallsView.Opacity>.99);
            checks["delivery-immediately-returns-the-over-strip-from-bowler-details"] = bug.BowlerDetailLane.Visibility == Visibility.Hidden && bug.OverBallsView.Opacity > .99;
            match.RecordDelivery(0, CricketDeliveryType.Wicket); await Task.Delay(100);
            checks["dismissed-batter-keeps-name-with-strong-grey-state-and-no-out-wording"] = new[] { bug.StrikerName, bug.NonStrikerName }.Any(text => !text.Text.Contains("OUT") && ((SolidColorBrush)text.Foreground).Color.R == 0x88);
            CaptureCricketQa(overlays, Path.Combine(directory, "dismissed-batter-emphasis.png"));
            match.SelectNewBatter(2); await Task.Delay(80);
            checks["new-batter-crossfades-old-and-new-row-without-resizing"] = QaDescendants(bug).OfType<Image>().Any(image => Equals(image.Tag, "actor-change-ghost"));
            CaptureCricketQa(overlays, Path.Combine(directory, "new-batter-handoff.png"));
            await Task.Delay(800);
            checks["batter-handoff-cleans-up-its-outgoing-snapshot"] = !QaDescendants(bug).OfType<Image>().Any(image => Equals(image.Tag, "actor-change-ghost"));
            while (!match.CurrentInnings!.NeedsBowlerSelection && !match.CurrentInnings.IsComplete) match.RecordDelivery(0, CricketDeliveryType.Dot);
            match.ConfirmBowler(1); await Task.Delay(80);
            checks["new-bowler-uses-the-same-player-handoff-transition"] = QaDescendants(bug.BowlerBand).OfType<Image>().Any(image => Equals(image.Tag, "actor-change-ghost"));
            await Task.Delay(950);
            checks["new-bowler-handoff-keeps-six-empty-circles-after-clearing"] = bug.CurrentOverBallsPanel.Children.Count == 6 && bug.CurrentOverBallsPanel.Children.OfType<Border>().All(pip => Equals(pip.Tag, ""));
            double size = bug.ScoreRuns.FontSize; double width = bug.CricketDesignRoot.Width, height = bug.CricketDesignRoot.Height;
            overlays.Request(CricketOverlayKind.SinceScore); await Task.Delay(750);
            checks["overlay-treats-both-team-name-bars-identically"] = bug.BattingHeaderPanel.Opacity < .01 && bug.BowlingHeaderPanel.Opacity < .01;
            checks["overlay-has-no-duplicated-team-header-above-it"] = !QaDescendants(overlays).OfType<Border>().Any(border => Equals(border.Tag, "combined-team-header"));
            var contextBounds = bug.InfoValuePanel.TransformToAncestor(bug.CricketDesignRoot).TransformBounds(new Rect(0, 0, bug.InfoValuePanel.ActualWidth, bug.InfoValuePanel.ActualHeight));
            var bowlerBounds = bug.BowlerBand.TransformToAncestor(bug.CricketDesignRoot).TransformBounds(new Rect(0, 0, bug.BowlerBand.ActualWidth, bug.BowlerBand.ActualHeight));
            var ballBounds = bug.OverBallLane.TransformToAncestor(bug.CricketDesignRoot).TransformBounds(new Rect(0, 0, bug.OverBallLane.ActualWidth, bug.OverBallLane.ActualHeight));
            checks["compact-score-overs-and-live-context-fill-left-column-without-empty-bottom-band"] = Math.Abs(contextBounds.Bottom - height) < 1 && bug.CompactOversPanel.Opacity < .01
                && bug.OversValuePanel.ActualHeight < height / 5 && bug.InfoValuePanel.ActualHeight > 50;
            checks["compact-bowler-keeps-live-ball-strip-instead-of-a-blank-lower-band"] = bug.OverBallLane.Opacity > .99 && bug.CurrentOverBallsPanel.Children.Count >= 6
                && ballBounds.Top - bowlerBounds.Bottom >= 0 && ballBounds.Top - bowlerBounds.Bottom < 12 && height - ballBounds.Bottom < 5;
            var pressure = QaDescendants(overlays).OfType<Viewbox>().Single(view => Equals(view.Tag, "pressure-cluster"));
            var cluster = (FrameworkElement)pressure.Child; var bounds = cluster.TransformToAncestor(overlays).TransformBounds(new Rect(0, 0, cluster.ActualWidth, cluster.ActualHeight));
            checks["pressure-overlay-number-and-label-are-centred-as-one-group"] = Math.Abs(bounds.Left + bounds.Width / 2 - overlays.ActualWidth / 2) < 2;
            for (int i = 0; i < 25; i++) match.RaiseMatchChanged();
            checks["unchanged-match-refresh-does-not-reconstruct-overlay-visual-tree"] = ReferenceEquals(pressure, QaDescendants(overlays).OfType<Viewbox>().Single(view => Equals(view.Tag, "pressure-cluster")));
            checks["overlay-still-preserves-fonts-and-design-canvas-through-refreshes"] = size == bug.ScoreRuns.FontSize && width == bug.CricketDesignRoot.Width && height == bug.CricketDesignRoot.Height;
            var overDrought=CricketOverlayAnalysis.Build(match).Single(fact=>fact.Kind==CricketOverlayKind.SinceScore) with {Title="OVERS SINCE LAST SCORE",Metrics=new[] {new CricketOverlayMetric("OVERS","10.1")}};
            InvokeCricketQa(overlays,"Render",overDrought);
            checks["drought-overlay-adapts-its-units-and-large-value-after-ten-overs"]=QaDescendants(overlays).OfType<TextBlock>().Any(text=>text.Text=="OVERS SINCE\nLAST SCORE")
                && QaDescendants(overlays).OfType<TextBlock>().Any(text=>text.Text=="10.1");
            CaptureCricketQa(overlays, Path.Combine(directory, "centred-pressure-without-duplicate-header.png"));
            overlays.Request(CricketOverlayKind.Projection); await Task.Delay(1400);
            var metrics = QaDescendants(overlays).OfType<Viewbox>().Where(view => Equals(view.Tag, "overlay-metric")).ToArray();
            checks["projected-overlay-shows-every-rate-side-by-side-without-pages"] = metrics.Length == CricketOverlayAnalysis.Build(match).Single(fact => fact.Kind == CricketOverlayKind.Projection).Metrics.Count && metrics.Length >= 2;
            CaptureCricketQa(overlays, Path.Combine(directory, "all-projected-rates-side-by-side.png"));
            overlays.Cancel(); await Task.Delay(800);
            checks["original-team-bars-restore-together-after-overlay"] = bug.BattingHeaderPanel.Opacity > .99 && bug.BowlingHeaderPanel.Opacity > .99 && bug.CompactOversPanel.Opacity < .01;
            bug.EventAnimationsEnabled = true; int before = match.TotalRuns; match.RecordDelivery(4, CricketDeliveryType.Four); await Task.Delay(850);
            checks["four-celebration-holds-readably-before-output-score-roll"] = bug.EventShowing && bug.EventWord.Text == "FOUR" && bug.ScoreRuns.Text == before.ToString();
            checks["four-impact-animates-the-four-letters-without-floating-decoration"] = QaDescendants(bug.EventAccents).OfType<TextBlock>().Select(text => text.Text).SequenceEqual(new[] {"F", "O", "U", "R"})
                && !QaDescendants(bug.EventAccents).OfType<System.Windows.Shapes.Shape>().Any();
            CaptureCricketQa(overlays, Path.Combine(directory, "four-split-typographic-impact.png"));
            await Task.Delay(CricketScorebugControl.MomentHoldMilliseconds + 500); checks["longer-four-celebration-still-catches-up-to-live-total"] = bug.ScoreRuns.Text == match.TotalRuns.ToString() && !bug.EventShowing;
            match.RecordDelivery(4, CricketDeliveryType.Four); await Task.Delay(CricketScorebugControl.MomentHoldMilliseconds + 100);
            match.RecordDelivery(6, CricketDeliveryType.Six); await Task.Delay(180);
            checks["new-boundary-during-exit-receives-a-visible-latest-moment-hold"] = bug.EventShowing && bug.EventWord.Text == "SIX" && bug.EventPresentation.Opacity > .75;
            checks["six-uses-a-distinct-word-zoom-without-floating-balls"] = QaDescendants(bug.EventAccents).OfType<TextBlock>().Select(text => text.Text).SequenceEqual(new[] {"S", "I", "X"})
                && !QaDescendants(bug.EventAccents).OfType<System.Windows.Shapes.Shape>().Any()
                && QaDescendants(bug.EventAccents).OfType<FrameworkElement>().Any(element => Equals(element.Tag, "six-type-scene"));
            CaptureCricketQa(overlays, Path.Combine(directory, "six-camera-zoom-word.png"));
            match.RecordDelivery(0, CricketDeliveryType.Wicket); await Task.Delay(160);
            checks["rapid-new-event-replaces-the-moment-scene-as-well-as-its-word"] = bug.EventWord.Text == "WICKET" && QaDescendants(bug.EventAccents).OfType<TextBlock>().Count() == 6;
            CaptureCricketQa(overlays, Path.Combine(directory, "wicket-appeal-letter-moment.png"));
            match.UndoLastDelivery(); await Task.Delay(3000);
            checks["undo-during-choreographed-moment-cannot-reapply-a-wicket"] = !bug.EventShowing && bug.ScoreWickets.Text == match.TotalWickets.ToString() && bug.ScoreRuns.Text == match.TotalRuns.ToString();
            match.RecordDelivery(0, CricketDeliveryType.Wicket); await Task.Delay(1250);
            var appealLetters = QaDescendants(bug.EventAccents).OfType<TextBlock>().ToArray();
            checks["wicket-appeal-settles-into-one-complete-readable-word"] = bug.EventShowing && appealLetters.Length == 6 && appealLetters.All(letter => CricketQaBoundsWithin(letter, bug.CricketDesignRoot));
            CaptureCricketQa(overlays, Path.Combine(directory, "wicket-appeal-complete-word.png"));
        }
        finally { overlays.Dispose(); bug.Dispose(); window.Close(); }
        var panel = new CricketControlPanel(); var pending = CricketReliabilityChecks.CreateMatch(); pending.TossPending = true;
        pending.TeamALogoPath = match.TeamALogoPath; pending.TeamBLogoPath = match.TeamBLogoPath; panel.SetMatch(pending);
        var control = new Window { Content = panel, Width = 1000, Height = 800, ShowInTaskbar = false }; control.Show();
        try
        {
            await control.Dispatcher.InvokeAsync(()=>panel.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Loaded);
            InvokeCricketQa(panel, "OpenToss_Click", this, new RoutedEventArgs());
            await control.Dispatcher.InvokeAsync(()=>panel.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Loaded);
            panel.TossHomeChoice.IsChecked = true; panel.TossBatChoice.IsChecked = true;
            await Task.Delay(100); panel.UpdateLayout();
            checks["toss-options-use-selectable-cards-without-checkbox-circles"] = panel.TossHomeChoice.Template.FindName("Check",panel.TossHomeChoice) is null && panel.TossHomeChoice.Template.FindName("ChoiceSurface",panel.TossHomeChoice) is OperatorGlassSurface;
            await WaitCricketQaStateAsync(control,()=> (double)panel.TossHomeChoice.GetValue(OperatorChoiceMotion.ProgressProperty)>0);
            checks["toss-choice-animates-the-selected-fill"]=(double)panel.TossHomeChoice.GetValue(OperatorChoiceMotion.ProgressProperty)>0;
            panel.TossAwayChoice.IsChecked=true;await Task.Delay(40);panel.TossHomeChoice.IsChecked=true;await Task.Delay(600);
            checks["rapid-toss-reselection-settles-one-selected-card"]=panel.TossHomeChoice.IsChecked==true && panel.TossAwayChoice.IsChecked!=true
                && (double)panel.TossHomeChoice.GetValue(OperatorChoiceMotion.ProgressProperty)>.95;
            CaptureCricketQa(panel, Path.Combine(directory, "toss-selectable-cards-no-checkboxes.png"));
            InvokeCricketQa(panel, "ConfirmToss_Click", this, new RoutedEventArgs()); await Task.Delay(100);
            checks["missing-players-visually-grey-out-and-disable-scoring-panels"] = !panel.ScoringCommands.IsEnabled && panel.ScoringCommands.Opacity < .5 && !panel.StrikerBar.IsEnabled && !panel.BowlerBar.IsEnabled;
            checks["operator-team-panels-use-calm-colour-surfaces-without-background-logos"] = panel.BattingZoneLogo.Source is null && panel.BowlingZoneLogo.Source is null
                && ((Grid)panel.BattingZone.Child).Background is null && ((Grid)panel.BowlingZone.Child).Background is null;
            CaptureCricketQa(panel, Path.Combine(directory, "unselected-players-disabled-scoring-and-team-colours.png"));
        }
        finally { panel.ClearMatch(); control.Close(); }
        return checks;
    }
}
