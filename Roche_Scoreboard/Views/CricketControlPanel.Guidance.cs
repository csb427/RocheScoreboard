using System.Windows;
using System.Windows.Input;
namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private bool _bowlerWasRequired, _batterWasRequired;
    private void CloseResolvedSelections(bool battingNeeded, bool bowlingNeeded)
    {
        if (_bowlerWasRequired && !bowlingNeeded && BowlerSelectionOverlay.Visibility == Visibility.Visible)
        { _bowlerWasRequired = false; _selectorGeneration++; BowlerSelectionOverlay.Visibility = Visibility.Collapsed; }
        if (_batterWasRequired && !battingNeeded && BatterSelectionOverlay.Visibility == Visibility.Visible)
        { _batterWasRequired = false; _selectorGeneration++; BatterSelectionOverlay.Visibility = Visibility.Collapsed; }
    }
    private void ScoringLock_Click(object sender, MouseButtonEventArgs args)
    {
        args.Handled = true;
        NavigateScoringGate();
    }
    private void ScoringGate_Click(object sender,RoutedEventArgs args)=>NavigateScoringGate();
    private async void NavigateScoringGate()
    {
        if (_match is null) return;
        ClosePlayerMenu();
        if(_match.IsMatchFinalized) {MainTabControl.SelectedItem=ScorecardTab;return;}
        if (_match.BreakKind != Models.CricketBreakKind.None) {_match.SetBreak(Models.CricketBreakKind.None);ResumePendingSelection();return;}
        if(_match.CurrentInnings?.IsComplete==true) {await OpenInningsDecisionAsync(false);return;}
        ResumePendingSelection();
    }
    private void UpdateScoringGate()
    {
        if(_match?.CurrentInnings is not {} innings) return;
        bool waiting=_match.TossPending && !_match.IsMatchFinalized,paused=_match.BreakKind!=Models.CricketBreakKind.None;
        bool batters=innings.Striker is{IsOut:false} && innings.NonStriker is{IsOut:false} && innings.StrikerIndex!=innings.NonStrikerIndex && !innings.NeedsBatterSelection;
        bool bowler=innings.CurrentBowler is not null && !innings.NeedsBowlerSelection;
        bool ready=!waiting&&!paused&&!innings.IsComplete&&!_match.IsMatchFinalized&&batters&&bowler;
        ScoringCommands.IsEnabled=ExtraCommands.IsEnabled=ready;
        ScoringCommands.Opacity=ExtraCommands.Opacity=ready?1:.12;
        ScoringLockBanner.Visibility=!waiting&&!ready?Visibility.Visible:Visibility.Collapsed;
        StartTossButton.ContentTemplate=CricketPrompt.ButtonContent;
        StartTossButton.Visibility=waiting?Visibility.Visible:Visibility.Collapsed;
        string key="continue";
        if(_match.IsMatchFinalized) {ScoringReadyHint.Text="MATCH ENDED";ScoringReadyDetail.Text="View the scorecard";key="scorecard";}
        else if(paused) {ScoringReadyHint.Text=_match.BreakKind.ToString().ToUpperInvariant()+(_match.BreakKind==Models.CricketBreakKind.Rain?" DELAY":" BREAK");ScoringReadyDetail.Text="Click to resume play";key="resume";}
        else if(innings.IsComplete) {ScoringReadyHint.Text="INNINGS COMPLETE";ScoringReadyDetail.Text="Choose the next step →";key="innings";}
        else if(!batters&&!bowler) {ScoringReadyHint.Text="SELECT BATTERS & BOWLER";ScoringReadyDetail.Text="Choose the opening pair, then the bowler →";}
        else if(!batters) {ScoringReadyHint.Text="SELECT NEXT BATTER";ScoringReadyDetail.Text="Choose the player to resume scoring →";}
        else if(!bowler) {ScoringReadyHint.Text="SELECT NEXT BOWLER";ScoringReadyDetail.Text=innings.LegalBallsBowled==0 ? "Choose the opening bowler →" : "The over is complete · choose the next bowler →";}
        else {ScoringReadyHint.Text="";ScoringReadyDetail.Text="";}
        CricketPrompt.ApplyActionColour(ScoringLockBanner,key,true);
        ScoringReadyHint.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=ScoringLockBanner});
        ScoringReadyDetail.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=ScoringLockBanner});
        EndInningsButton.Content=innings.IsComplete?(_match.AllInnings.Count==1?"Next innings":"Innings options"):"End innings";
        EndInningsButton.ContentTemplate=CricketPrompt.ButtonContent;
        EndInningsButton.FontSize=14;
    }
}
