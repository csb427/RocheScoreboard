using System.IO;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Visibility = System.Windows.Visibility;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketTossVisualChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        int windowCount=System.Windows.Application.Current.Windows.Count;
        ShowRecoveryPrompt(new GameState { Sport="cricket",CricketMatchState=CricketReliabilityChecks.CreateMatch().ToState() });
        await Task.Delay(150);
        var prompt=RecoveryOverlay.Children.OfType<ResumeMatchPrompt>().Single();
        checks["recovery-is-integrated-with-no-separate-window"]=System.Windows.Application.Current.Windows.Count==windowCount && RecoveryOverlay.IsVisible && _recoveryPromptActive;
        checks["recovery-blocks-background-interaction"]=!SportSelectionPanel.IsEnabled;
        CaptureCricketQa(this,Path.Combine(directory,"integrated-recovery.png"));
        double originalWidth=Width,originalHeight=Height;
        Width=420; Height=320; await Task.Delay(150);
        checks["integrated-recovery-actions-fit-short-control-window"]=CricketQaBoundsWithin(prompt.LaterButton,this)
            && CricketQaBoundsWithin(prompt.ResumeButton,this) && CricketQaBoundsWithin(prompt.DiscardButton,this);
        checks["integrated-recovery-both-team-names-fit-short-window-without-clipping"]=CricketQaBoundsWithin(prompt.HomeNameText,FindQaVisual<ScrollViewer>(prompt)!)
            && CricketQaBoundsWithin(prompt.AwayNameText,FindQaVisual<ScrollViewer>(prompt)!);
        CaptureCricketQa(this,Path.Combine(directory,"integrated-recovery-420x320.png"));
        Width=originalWidth; Height=originalHeight; await Task.Delay(150);
        InvokeCricketQa(prompt,"KeepSaved_Click",this,new RoutedEventArgs());
        await WaitCricketQaStateAsync(this,()=>!_recoveryPromptActive && RecoveryOverlay.Visibility==Visibility.Collapsed);
        checks["recovery-continue-later-restores-background-interaction"]=!_recoveryPromptActive && SportSelectionPanel.IsEnabled && RecoveryOverlay.Visibility==Visibility.Collapsed;
        checks["recovery-dismissal-guards-against-trailing-double-clicks"]=_navigationPointerGuardUntilUtc>DateTime.UtcNow;

        var match=CricketReliabilityChecks.CreateMatch(); match.TossPending=true;
        match.TeamALogoPath=Path.Combine(directory,"fixture-logo-A.png"); match.TeamBLogoPath=Path.Combine(directory,"fixture-logo-B.png");
        var bug=new CricketScorebugControl { EventAnimationsEnabled=false }; bug.UpdateFromMatch(match);
        var overlays=new CricketOverlayHost(match) { AutomaticEnabled=false }; overlays.Scene.Children.Add(bug);
        var display=new Window { Content=overlays,Width=386,Height=193,WindowStyle=WindowStyle.None,ShowInTaskbar=false };
        var panel=new CricketControlPanel(); panel.SetMatch(match); panel.CricketEventAnimations.IsChecked=false; panel.SetScorebug(bug); panel.SetOverlayHost(overlays); panel.CricketAutomaticOverlays.IsChecked=false;
        var control=new Window { Content=panel,Width=1000,Height=800,ShowInTaskbar=false };
        display.Show(); control.Show();
        try
        {
            foreach(bool home in new[] {false,true}) foreach(bool bat in new[] {false,true})
            {
                var alternative=CricketReliabilityChecks.CreateMatch(); alternative.TossPending=true;
                var choicePanel=new CricketControlPanel(); choicePanel.SetMatch(alternative); control.Content=choicePanel;
                InvokeCricketQa(choicePanel,"OpenToss_Click",this,new RoutedEventArgs());
                (home ? choicePanel.TossHomeChoice : choicePanel.TossAwayChoice).IsChecked=true;
                (bat ? choicePanel.TossBatChoice : choicePanel.TossBowlChoice).IsChecked=true;
                checks[$"toss-ui-{home}-{bat}-enables-confirm-for-selected-combination"]=choicePanel.ConfirmTossButton.IsEnabled;
                var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(choicePanel.ConfirmTossButton);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer).Invoke(); await Task.Delay(70);
                checks[$"toss-ui-{home}-{bat}-real-button-applies-correct-result"]=!alternative.TossPending && alternative.TeamABatsFirst==(home==bat);
                choicePanel.ClearMatch();
            }
            control.Content=panel;
            await Task.Delay(150);
            checks["toss-public-screen-has-no-player-names-or-score-panels"]=bug.PregameScreen.Visibility==Visibility.Visible && bug.MainScoreboard.Visibility==Visibility.Hidden;
            checks["toss-control-panel-starts-with-one-toss-action"]=panel.StartTossButton.IsVisible && panel.ScoringCommands.Visibility==Visibility.Collapsed && panel.BatterSelectionOverlay.Visibility==Visibility.Collapsed;
            InvokeCricketQa(panel,"OpenToss_Click",this,new RoutedEventArgs());
            InvokeCricketQa(panel,"ConfirmToss_Click",this,new RoutedEventArgs());
            checks["toss-requires-both-winner-and-decision"]=match.TossPending && panel.TossError.Text.Length>0;
            panel.TossAwayChoice.IsChecked=true; panel.TossBowlChoice.IsChecked=true;
            checks["toss-choice-clears-validation-and-enables-real-confirm-button"]=panel.TossError.Text.Length==0 && panel.ConfirmTossButton.IsEnabled;
            CaptureCricketQa(panel,Path.Combine(directory,"integrated-cricket-toss.png"));
            InvokeCricketQa(panel,"ConfirmToss_Click",this,new RoutedEventArgs());
            checks["toss-confirm-immediately-opens-opening-batter-selection"]=!match.TossPending && match.TeamABatsFirst && panel.BatterSelectionOverlay.Visibility==Visibility.Visible;
            InvokeCricketQa(panel,"ConfirmBatterSelection_Click",this,new RoutedEventArgs());
            checks["toss-batter-selection-chains-striker-to-nonstriker"]=match.CurrentInnings!.Striker is not null && panel.BatterSelectionOverlay.Visibility==Visibility.Visible;
            InvokeCricketQa(panel,"SkipPlayerSelection_Click",this,new RoutedEventArgs());
            checks["toss-skip-keeps-completed-selection-and-return-actions"]=match.CurrentInnings.NonStriker is null && match.CurrentInnings.Striker is not null && panel.SelectOpeningBatters.IsVisible && panel.SelectOpeningBowler.IsVisible;
            InvokeCricketQa(panel,"SelectOpeningBowler_Click",this,new RoutedEventArgs());
            InvokeCricketQa(panel,"ConfirmBowlerSelection_Click",this,new RoutedEventArgs());
            await WaitCricketQaStateAsync(control,()=>panel.BatterSelectionOverlay.Visibility==Visibility.Visible);
            checks["toss-bowler-first-handoff-returns-to-missing-batter"]=match.CurrentInnings.CurrentBowler is not null && panel.BatterSelectionOverlay.Visibility==Visibility.Visible;
            InvokeCricketQa(panel,"ConfirmBatterSelection_Click",this,new RoutedEventArgs());
            match.RecordDelivery(4,CricketDeliveryType.Four); match.RecordDelivery(6,CricketDeliveryType.Six);
            await Task.Delay(900);
            checks["toss-completed-player-setup-can-score-and-update-output"]=match.TotalRuns==10 && bug.ScoreRuns.Text=="10";
            checks["cricket-wickets-and-total-share-exact-font-and-size"]=bug.ScoreRuns.FontFamily.Source==bug.ScoreWickets.FontFamily.Source && Math.Abs(bug.ScoreRuns.FontSize-bug.ScoreWickets.FontSize)<.01;
            overlays.Request(CricketOverlayKind.LastOver); await Task.Delay(800);
            checks["overlay-keeps-stacked-score-and-live-balls-while-hiding-both-team-bars"]=bug.ScoreSlash.Text=="FOR" && bug.OverBallLane.Opacity>.99
                && bug.BattingHeaderPanel.Opacity<.01 && bug.BowlingHeaderPanel.Opacity<.01 && !overlays.UsesBothTeamBackgrounds;
            CaptureCricketQa(overlays,Path.Combine(directory,"compact-score-under-team-overlay.png"));
            match.RecordDelivery(1,CricketDeliveryType.Runs); await Task.Delay(900);
            checks["scoring-still-works-with-compact-live-ball-lane"]=match.TotalRuns==11 && bug.ScoreRuns.Text=="11";
            overlays.Cancel(); await Task.Delay(900);
            checks["overlay-exit-restores-stacked-score-and-current-over-balls"]=bug.ScoreSlash.Text=="FOR" && bug.OverBallLane.Opacity>.99 && bug.CurrentOverBallsPanel.Children.Count>=6;
            CaptureCricketQa(overlays,Path.Combine(directory,"restored-score-after-overlay.png"));
            // Keep the live refresh source consistent with the reel's target.
            match.CurrentInnings!.TotalRuns=17;bug.UpdateFromMatch(match);
            bug.ScoreRollVisual.Roll(bug.ScoreRuns,11,17);
            checks["digit-reel-only-moves-units-for-eleven-to-seventeen"]=bug.ScoreRollVisual.MovingDigitCount==1;
            await Task.Delay(300); CaptureCricketQa(overlays,Path.Combine(directory,"digit-reel-unchanged-tens.png"));
            await WaitCricketQaStateAsync(display,()=>bug.ScoreRuns.Text=="17" && bug.ScoreRuns.Opacity>.99);
            checks["digit-reel-settles-with-authoritative-target-visible"]=bug.ScoreRuns.Text=="17" && bug.ScoreRuns.Opacity>.99;
            bug.ScoreRollVisual.RollText(bug.ScoreRuns,"2-11","2-17");
            checks["digit-reel-keeps-bowler-wickets-and-separator-still"]=bug.ScoreRollVisual.MovingDigitCount==1;
            bug.ScoreRollVisual.Stop(); bug.UpdateFromMatch(match);
        }
        finally { panel.ClearMatch(); overlays.Dispose(); bug.Dispose(); control.Close(); display.Close(); }
        return checks;
    }
}

