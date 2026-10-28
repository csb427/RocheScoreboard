using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketRefinementChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var match=CricketReliabilityChecks.CreateMatch(true,50);
        match.TossWinner=match.TeamAName;
        match.TeamALogoPath=Path.Combine(directory,"fixture-logo-A.png");
        match.TeamBLogoPath=Path.Combine(directory,"fixture-logo-B.png");
        var bug=new CricketScorebugControl { EventAnimationsEnabled=false };
        var overlays=new CricketOverlayHost(match) { AutomaticEnabled=false }; overlays.Scene.Children.Add(bug);
        match.MatchChanged+=()=>bug.UpdateFromMatch(match); bug.UpdateFromMatch(match);
        var output=new Window { Content=overlays,Width=386,Height=193,WindowStyle=WindowStyle.None,ShowInTaskbar=false };
        ApplyOutputWindowChrome(output); output.Show();
        try
        {
            await Task.Delay(120);
            double designWidth=bug.CricketDesignRoot.Width,designHeight=bug.CricketDesignRoot.Height;
            double[] sizes=[bug.ScoreRuns.FontSize,bug.ScoreWickets.FontSize,bug.StrikerName.FontSize,bug.StrikerRuns.FontSize,bug.BowlerFigures.FontSize,bug.OversText.FontSize];
            int sizeChanges=0; SizeChangedEventHandler changed=(_,_)=>sizeChanges++; bug.SizeChanged+=changed;
            overlays.Request(CricketOverlayKind.Extras); await Task.Delay(200);
            var bowlerDuring=bug.BowlerBand.TransformToAncestor(bug.CricketDesignRoot).TransformBounds(new Rect(0,0,bug.BowlerBand.ActualWidth,bug.BowlerBand.ActualHeight));
            var ballsDuring=bug.OverBallLane.TransformToAncestor(bug.CricketDesignRoot).TransformBounds(new Rect(0,0,bug.OverBallLane.ActualWidth,bug.OverBallLane.ActualHeight));
            checks["overlay-transition-keeps-bowler-clear-of-the-fading-ball-strip"]=bowlerDuring.Bottom<=ballsDuring.Top+1;
            checks["overlay-animation-does-not-resize-the-scorebug-each-frame"]=sizeChanges==0 && bug.CricketDesignRoot.Width==designWidth && bug.CricketDesignRoot.Height==designHeight;
            await Task.Delay(500);
            checks["overlay-preserves-every-primary-score-and-player-font-size"]=sizes.SequenceEqual(new[] {bug.ScoreRuns.FontSize,bug.ScoreWickets.FontSize,bug.StrikerName.FontSize,bug.StrikerRuns.FontSize,bug.BowlerFigures.FontSize,bug.OversText.FontSize});
            checks["extras-overlay-uses-the-bowling-team"]=overlays.ThemedTeam==match.BowlingTeamName;
            var extraLabels=QaDescendants(overlays).OfType<Viewbox>().Where(view=>Equals(view.Tag,"metric-label-row")).ToArray();
            var extraValues=QaDescendants(overlays).OfType<Viewbox>().Where(view=>Equals(view.Tag,"metric-value-row")).ToArray();
            double Midline(FrameworkElement element)=>element.TranslatePoint(new System.Windows.Point(0,element.ActualHeight/2),overlays).Y;
            checks["extras-labels-and-values-share-two-fixed-baselines"]=extraLabels.Length==4 && extraValues.Length==4
                && extraLabels.Select(Midline).Max()-extraLabels.Select(Midline).Min()<.5 && extraValues.Select(Midline).Max()-extraValues.Select(Midline).Min()<.5;
            checks["overlay-keeps-large-stacked-score-not-a-smaller-slash-score"]=bug.ScoreSlash.Text=="FOR" && Grid.GetRowSpan(bug.ScoreStack)==2;
            checks["overlay-bottom-bowler-and-score-remain-within-output"]=CricketQaBoundsWithin(bug.BowlerBand,bug.CricketDesignRoot) && CricketQaBoundsWithin(bug.OversText,bug.CricketDesignRoot);
            CaptureCricketQa(overlays,Path.Combine(directory,"refined-extras-with-unchanged-score-size.png"));
            overlays.Cancel(); await WaitCricketQaStateAsync(output,()=>Math.Abs(((TranslateTransform)bug.LeftScoreRail.RenderTransform).Y)<.01 && bug.OverBallLane.Opacity>.99 && !overlays.IsShowing);
            checks["overlay-and-scorebug-return-together-with-no-empty-top-band"]=Math.Abs(((TranslateTransform)bug.LeftScoreRail.RenderTransform).Y)<.01 && bug.OverBallLane.Opacity>.99 && !overlays.IsShowing;
            bug.SizeChanged-=changed;
            var normalBounds=bug.ScoreStack.TransformToAncestor(overlays).TransformBounds(new Rect(0,0,bug.ScoreStack.ActualWidth,bug.ScoreStack.ActualHeight));
            CaptureCricketQa(overlays,Path.Combine(directory,"scale-reference-386x193.png"));
            output.Width=772; output.Height=386; await Task.Delay(180);
            var doubleBounds=bug.ScoreStack.TransformToAncestor(overlays).TransformBounds(new Rect(0,0,bug.ScoreStack.ActualWidth,bug.ScoreStack.ActualHeight));
            checks["exact-double-preview-preserves-design-canvas-and-fonts"]=Math.Abs(bug.CricketDesignRoot.Width-designWidth)<.01 && Math.Abs(bug.CricketDesignRoot.Height-designHeight)<.01 && bug.ScoreRuns.FontSize==sizes[0];
            checks["exact-double-preview-scales-panel-geometry-by-two"]=Math.Abs(doubleBounds.Width-normalBounds.Width*2)<.5 && Math.Abs(doubleBounds.Height-normalBounds.Height*2)<.5;
            CaptureCricketQa(overlays,Path.Combine(directory,"scale-reference-772x386.png"));
            output.Width=386; output.Height=193; await Task.Delay(120);
            bug.ScoreRollVisual.Roll(bug.ScoreRuns,11,17); await Task.Delay(240);
            checks["digit-roll-uses-native-text-advance-without-tracking-expansion"]=Math.Abs(bug.ScoreRollVisual.RestingAdvance-bug.ScoreRollVisual.AnimatedAdvance)<.6 && bug.ScoreRollVisual.MovingDigitCount==1;
            CaptureCricketQa(overlays,Path.Combine(directory,"stable-native-digit-spacing-mid-roll.png"));
            await Task.Delay(600); bug.UpdateFromMatch(match);
            bug.ScoreRollVisual.Roll(bug.ScoreRuns,100,99); await Task.Delay(240);
            checks["decreasing-digit-count-rolls-down-without-expanding-target-tracking"]=bug.ScoreRollVisual.IsDecrementing && bug.ScoreRollVisual.MovingDigitCount==3
                && Math.Abs(bug.ScoreRollVisual.RestingAdvance-bug.ScoreRollVisual.AnimatedAdvance)<.6;
            CaptureCricketQa(overlays,Path.Combine(directory,"decreasing-digit-count-100-to-99.png"));
            await Task.Delay(600); bug.UpdateFromMatch(match);
            overlays.Request(CricketOverlayKind.Projection); await Task.Delay(700);
            var projectionFirst=QaDescendants(overlays).OfType<TextBlock>().Where(t=>t.IsVisible).Select(t=>t.Text).ToArray();
            CaptureCricketQa(overlays,Path.Combine(directory,"projection-large-rates-page-one.png"));
            await Task.Delay(4600);
            var projectionSecond=QaDescendants(overlays).OfType<TextBlock>().Where(t=>t.IsVisible).Select(t=>t.Text).ToArray();
            checks["projection-shows-all-rate-ranges-together-without-rotation"]=projectionFirst.Contains("CRR 0.00") && projectionFirst.Contains("2 RPO") && projectionSecond.Contains("CRR 0.00") && projectionSecond.Contains("2 RPO");
            CaptureCricketQa(overlays,Path.Combine(directory,"projection-large-rates-page-two.png"));
            overlays.Cancel(); await Task.Delay(800);
            var summary=new CricketSummaryControl(); overlays.Scene.Children.Add(summary); summary.ShowBattingSummary(match); bug.Visibility=Visibility.Hidden;
            overlays.Request(CricketOverlayKind.LastOver); await Task.Delay(800);
            checks["summary-overlays-use-three-readable-rows-not-cropped-first-player"]=summary.RowsPanel.Rows==3 && CricketQaBoundsWithin(summary.RowsPanel,summary.RootGrid);
            CaptureCricketQa(overlays,Path.Combine(directory,"summary-with-overlay-readable-four-row-page.png"));
            overlays.Cancel(); await Task.Delay(800);
            int summaryRuns=match.TotalRuns,summaryWickets=match.TotalWickets,summaryDeliveries=match.CurrentInnings!.Deliveries.Count;
            summary.ShowMatchSummary(match); await Task.Delay(450);
            checks["match-summary-is-a-distinct-three-band-score-batting-and-bowling-layout"]=Equals(summary.RowsPanel.Tag,"match-summary") && summary.RowsPanel.Rows==3
                && summary.RowsPanel.Children.Count==3 && QaDescendants(summary).OfType<TextBlock>().Any(text=>Equals(text.Tag,"match-summary-score") && text.Text==$"{match.TotalWickets}/{match.TotalRuns}");
            checks["viewing-match-summary-does-not-change-the-match"]=match.TotalRuns==summaryRuns && match.TotalWickets==summaryWickets && match.CurrentInnings.Deliveries.Count==summaryDeliveries;
            checks["match-summary-uses-one-overarching-crest-without-repeating-it-in-each-band"]=summary.RowsPanel.Background is ImageBrush
                && !QaDescendants(summary.RowsPanel).OfType<System.Windows.Controls.Panel>().Any(panel=>panel.Background is ImageBrush);
            CaptureCricketQa(overlays,Path.Combine(directory,"purposeful-match-summary-score-and-performers.png"));
            summary.ShowBowlingSummary(match); await Task.Delay(450);
            checks["leaving-match-summary-restores-scorecard-column-headings"]=summary.RowsPanel.Tag is null && summary.RootGrid.RowDefinitions[1].Height.Value==44 && summary.SummaryColumns.Children.Count==5;
            summary.HideSummary(); overlays.Scene.Children.Remove(summary); bug.Visibility=Visibility.Visible;
            bug.EventAnimationsEnabled=true;
            overlays.Request(CricketOverlayKind.Extras); await Task.Delay(750);
            match.RecordDelivery(4,CricketDeliveryType.Four);
            checks["boundary-first-requests-a-smooth-overlay-exit-before-the-celebration"]=match.TotalRuns==4 && bug.MomentAwaitingLayout && !bug.EventShowing && overlays.IsShowing && bug.ScoreRuns.Text=="0";
            await Task.Delay(200);
            checks["celebration-never-appears-on-top-of-a-half-restored-overlay-layout"]=!bug.EventShowing && bug.MomentAwaitingLayout;
            CaptureCricketQa(overlays,Path.Combine(directory,"boundary-waits-for-overlay-and-layout-handoff.png"));
            await Task.Delay(650);
            checks["celebration-starts-only-after-overlay-and-both-team-bars-finish-restoring"]=bug.EventShowing && !bug.MomentAwaitingLayout && !overlays.IsShowing && bug.BattingHeaderPanel.Opacity>.99 && bug.BowlingHeaderPanel.Opacity>.99;
            checks["four-updates-authoritative-score-immediately-but-presentation-precedes-roll"]=match.TotalRuns==4 && bug.EventShowing && bug.ScoreRuns.Text=="0";
            await Task.Delay(250); CaptureCricketQa(overlays,Path.Combine(directory,"four-presentation-before-figure-update.png"));
            await Task.Delay(CricketScorebugControl.MomentHoldMilliseconds + 500);
            checks["four-presentation-finishes-before-new-figure-is-rolled"]=!bug.EventShowing && bug.ScoreRuns.Text=="4";
            await Task.Delay(800);
            overlays.Request(CricketOverlayKind.Extras); await Task.Delay(750);
            match.RecordDelivery(6,CricketDeliveryType.Six); match.RecordDelivery(4,CricketDeliveryType.Four);
            checks["rapid-boundaries-coalesce-to-one-pending-moment-during-overlay-restoration"]=match.TotalRuns==14 && bug.MomentAwaitingLayout && !bug.EventShowing;
            await WaitCricketQaStateAsync(output,()=>bug.EventShowing && bug.EventWord.Text=="FOUR" && !overlays.IsShowing);
            checks["pending-boundary-handoff-plays-the-latest-word-without-an-old-score-queue"]=bug.EventShowing && bug.EventWord.Text=="FOUR" && !overlays.IsShowing;
            await Task.Delay(CricketScorebugControl.MomentHoldMilliseconds + 600);
            checks["rapid-boundaries-coalesce-without-unbounded-score-lag"]=match.TotalRuns==14 && bug.ScoreRuns.Text=="14" && !bug.EventShowing;
            overlays.Request(CricketOverlayKind.Extras); await Task.Delay(750);
            match.RecordDelivery(6,CricketDeliveryType.Six); match.UndoLastDelivery(); await Task.Delay(1300);
            checks["undo-cancels-inflight-boundary-with-no-stale-score-callback"]=match.TotalRuns==14 && bug.ScoreRuns.Text=="14" && !bug.EventShowing && !bug.MomentAwaitingLayout;
            match.RecordDelivery(0,CricketDeliveryType.Wicket);
            checks["wicket-has-a-distinct-bowling-team-presentation"]=bug.EventShowing && bug.EventWord.Text=="WICKET";
            await Task.Delay(250); CaptureCricketQa(overlays,Path.Combine(directory,"wicket-presentation.png"));
            match.EndCurrentInnings(); match.StartNextInnings(); await Task.Delay(1300);
            checks["new-innings-cancels-prior-team-presentation-and-resets-output"]=!bug.EventShowing && bug.ScoreRuns.Text=="0" && bug.ScoreWickets.Text=="0";
            bug.EventAnimationsEnabled=false;
            var allOut=CricketReliabilityChecks.CreateMatch(true,20);
            for(int wicket=0;wicket<10;wicket++)
            {
                var innings=allOut.CurrentInnings!;
                if(innings.NeedsBatterSelection) allOut.SelectNewBatter(innings.BattingOrder.FindIndex(player=>!player.IsOut && player!=innings.Striker && player!=innings.NonStriker));
                if(innings.NeedsBowlerSelection) allOut.ConfirmBowler(allOut.SuggestedNextBowler());
                allOut.RecordDelivery(0,CricketDeliveryType.Wicket);
            }
            bug.UpdateFromMatch(allOut);
            checks["tenth-wicket-keeps-dismissed-output-batter-grey-even-without-a-replacement-prompt"]=allOut.CurrentInnings!.IsComplete && !allOut.CurrentInnings.NeedsBatterSelection
                && new[] {bug.StrikerName,bug.NonStrikerName}.Any(text=>text.Foreground is SolidColorBrush brush && brush.Color.R==0x88);
        }
        finally { overlays.Dispose(); bug.Dispose(); output.Close(); }

        var entryMatch=CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel(); panel.SetMatch(entryMatch);
        var control=new Window { Content=panel,Width=1000,Height=800,ShowInTaskbar=false }; control.Show();
        try
        {
            await Task.Delay(100);
            checks["operator-batter-and-bowler-figures-are-not-duplicated-under-names"]=panel.StrikerStats.Text.StartsWith("SR ") && !panel.StrikerStats.Text.Contains('(') && panel.BowlerStats.Text.StartsWith("Economy ") && panel.StrikerBallsFaced.Text=="(0)";
            checks["bowling-management-is-no-longer-exposed-in-settings"]=panel.FindName("BowlerList") is null;
            int windows=System.Windows.Application.Current.Windows.Count;
            InvokeCricketQa(panel,"NoBall_Click",this,new RoutedEventArgs());
            var entry=(CricketDeliveryDialog)panel.DeliveryEntryContent.Content;
            checks["extras-is-a-real-embedded-control-not-another-window"]=System.Windows.Application.Current.Windows.Count==windows && !panel.ConsoleViewport.IsHitTestVisible && panel.DeliveryEntryOverlay.IsVisible;
            await Task.Delay(60);
            checks["opening-extras-preserves-active-player-card-appearance"] = panel.ConsoleViewport.IsEnabled && panel.StrikerBar.IsEnabled && panel.NonStrikerBar.IsEnabled && panel.BowlerBar.IsEnabled
                && panel.StrikerBar.Opacity==1 && panel.NonStrikerBar.Opacity==1 && panel.BowlerBar.Opacity==1;
            InvokeCricketQa(panel,"Run1_Click",this,new RoutedEventArgs());
            InvokeCricketQa(panel,"SwapStrike_Click",this,new RoutedEventArgs());
            checks["extras-editor-blocks-background-score-and-strike-commands"] = entryMatch.TotalRuns==0 && entryMatch.CurrentInnings!.StrikerIndex==0;
            var backgroundButton=QaDescendants(panel.ScoringCommands).OfType<Button>().First();
            backgroundButton.Focus(); await Task.Delay(25);
            var backgroundKey=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(control)!,0,System.Windows.Input.Key.Space) { RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            backgroundButton.RaiseEvent(backgroundKey);
            checks["extras-editor-traps-keyboard-focus-and-background-key-presses"] = entry.IsKeyboardFocusWithin && backgroundKey.Handled && entryMatch.TotalRuns==0;
            checks["custom-runs-textbox-is-hidden-until-requested"]=entry.RunsBox.Visibility==Visibility.Collapsed;
            var customPeer=new System.Windows.Automation.Peers.ButtonAutomationPeer(entry.CustomRunsButton);
            ((System.Windows.Automation.Provider.IInvokeProvider)customPeer).Invoke(); await Task.Delay(80);
            checks["custom-runs-button-reveals-and-focuses-entry"]=entry.RunsBox.IsVisible && entry.RunsBox.IsKeyboardFocused;
            entry.RunsBox.Text="2"; entry.NoBallChoice.SelectedIndex=2;
            CaptureCricketQa(panel,Path.Combine(directory,"embedded-extra-custom-entry.png"));
            var applyPeer=new System.Windows.Automation.Peers.ButtonAutomationPeer(entry.ApplyButton);
            ((System.Windows.Automation.Provider.IInvokeProvider)applyPeer).Invoke(); await Task.Delay(100);
            checks["embedded-extras-real-apply-restores-controls-and-credits-once"]=entryMatch.TotalRuns==3 && entryMatch.CurrentInnings!.NoBalls==1 && entryMatch.CurrentInnings.LegByes==2 && panel.ConsoleViewport.IsEnabled && !panel.DeliveryEntryOverlay.IsVisible;
            checks["applying-extras-does-not-leave-player-cards-grey"] = panel.ConsoleViewport.IsHitTestVisible && panel.StrikerBar.Opacity==1 && panel.NonStrikerBar.Opacity==1 && panel.BowlerBar.Opacity==1
                && panel.StrikerName.Foreground==System.Windows.Media.Brushes.White && panel.NonStrikerName.Foreground==System.Windows.Media.Brushes.White;
            InvokeCricketQa(entry,"Apply_Click",entry.ApplyButton,new RoutedEventArgs());
            checks["completed-extra-form-cannot-submit-the-delivery-twice"]=entryMatch.TotalRuns==3;
            InvokeCricketQa(panel,"Wide_Click",this,new RoutedEventArgs());
            var small=(CricketDeliveryDialog)panel.DeliveryEntryContent.Content;
            control.Width=420; control.Height=320; await Task.Delay(150);
            checks["embedded-extras-apply-remains-reachable-at-420x320"]=CricketQaBoundsWithin(small.ApplyButton,panel);
            checks["embedded-extras-retains-a-scrollable-run-selection-area-at-420x320"]=FindQaVisual<ScrollViewer>(small)!.ActualHeight>=60;
            CaptureCricketQa(panel,Path.Combine(directory,"embedded-extra-420x320.png"));
            InvokeCricketQa(small,"Cancel_Click",this,new RoutedEventArgs());
            checks["embedded-extras-cancel-restores-interaction-without-a-score"]=panel.ConsoleViewport.IsEnabled && panel.ConsoleViewport.IsHitTestVisible && entryMatch.TotalRuns==3;
            control.Width=1000; control.Height=800; await Task.Delay(150);
            InvokeCricketQa(panel,"NoBall_Click",this,new RoutedEventArgs());
            var stale=(CricketDeliveryDialog)panel.DeliveryEntryContent.Content;
            entryMatch.RecordDelivery(1,CricketDeliveryType.Runs);
            InvokeCricketQa(stale,"Apply_Click",stale.ApplyButton,new RoutedEventArgs());
            checks["embedded-extra-rejects-stale-player-and-score-context"]=stale.ErrorText.Text.Contains("match changed") && entryMatch.TotalRuns==4;
            InvokeCricketQa(stale,"Cancel_Click",this,new RoutedEventArgs());
            entryMatch.RecordDelivery(1,CricketDeliveryType.Runs);
            var logCard=QaDescendants(panel.EventLog).OfType<Border>().First(b=>b.Focusable);
            logCard.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent });
            await Task.Delay(220);
            checks["delivery-log-hover-reveals-a-real-edit-ball-button"]=QaDescendants(logCard).OfType<Button>().Any(b=>Equals(b.Tag,"edit-ball") && b.IsVisible && b.ActualHeight>20);
            var logRows=QaDescendants(panel.EventLog).OfType<Border>().Where(row=>row.Tag is CricketDelivery).ToArray();
            string BallLabel(Border row)=>QaDescendants(row).OfType<TextBlock>().Single(text=>Equals(text.Tag,"ball-number")).Text;
            var noBallRow=logRows.Single(row=>((CricketDelivery)row.Tag).Type==CricketDeliveryType.NoBall);
            var firstCountedRow=logRows.Single(row=>((CricketDelivery)row.Tag).BallInOver==1);
            checks["delivery-log-repeats-extra-ball-number-without-plus-suffix"] = BallLabel(noBallRow)=="0.1" && BallLabel(firstCountedRow)=="0.1" && logRows.All(row=>!BallLabel(row).Contains('+'));
            checks["delivery-log-shows-concise-no-ball-extra-breakdown"] = QaDescendants(noBallRow).OfType<TextBlock>().Any(text=>text.Text=="No ball + 2 leg byes");
            CaptureCricketQa(panel,Path.Combine(directory,"delivery-log-hover-edit-action.png"));
            panel.ClearMatch();
        }
        finally { panel.ClearMatch(); control.Close(); }

        var preferences=new GlobalSettingsScreen(); int width=0,height=0;
        preferences.OutputDimensionsChanged+=(w,h)=> { width=w; height=h; };
        InvokeCricketQa(preferences,"DoubleOutputDimensions_Click",this,new RoutedEventArgs());
        checks["output-two-times-preview-requests-exact-772x386"]=width==772 && height==386;
        InvokeCricketQa(preferences,"ResetOutputDimensions_Click",this,new RoutedEventArgs());
        checks["output-preview-can-reset-to-canonical-386x193"]=width==386 && height==193;
        return checks;
    }
}
