// Generated from Views/CricketControlPanel.Guidance.cs; edit translator/adapters.
// Source SHA-256: 9a39049467186600fd5ce1e05aa1b97a220032614fca063b8d14791b4b5cb47e
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Models;
namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private bool _bowlerWasRequired, _batterWasRequired;
    private bool CloseResolvedSelections(bool battingNeeded, bool bowlingNeeded, CricketMatchManager match, CricketInnings innings)
    {
        int generation = _selectorGeneration;
        bool StillCurrent() => generation == _selectorGeneration && ReferenceEquals(_match, match)
            && ReferenceEquals(match.CurrentInnings, innings);
        if (!StillCurrent()) return false;
        if (_bowlerWasRequired && !bowlingNeeded && BowlerSelectionOverlay.IsVisible == true)
        {
            _bowlerWasRequired = false;
            generation = ++_selectorGeneration;
            BowlerSelectionOverlay.IsVisible = false;
            if (!StillCurrent()) return false;
        }
        if (_batterWasRequired && !battingNeeded && BatterSelectionOverlay.IsVisible == true)
        {
            _batterWasRequired = false;
            generation = ++_selectorGeneration;
            BatterSelectionOverlay.IsVisible = false;
            if (!StillCurrent()) return false;
        }
        return StillCurrent();
    }
    private void ScoringLock_Click(object? sender, PointerEventArgs args)
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
        ScoringLockBanner.IsVisible=!waiting&&!ready?true:false;
        StartTossButton.ContentTemplate=CricketPrompt.ButtonContent;
        StartTossButton.IsVisible=waiting?true:false;
        string key="continue";
        if(_match.IsMatchFinalized) {ScoringReadyHint.Text="MATCH ENDED";ScoringReadyDetail.Text="View the scorecard";key="scorecard";}
        else if(paused) {ScoringReadyHint.Text=_match.BreakKind.ToString().ToUpperInvariant()+(_match.BreakKind==Models.CricketBreakKind.Rain?" DELAY":" BREAK");ScoringReadyDetail.Text="Click to resume play";key="resume";}
        else if(innings.IsComplete) {ScoringReadyHint.Text="INNINGS COMPLETE";ScoringReadyDetail.Text="Choose the next step →";key="innings";}
        else if(!batters&&!bowler) {ScoringReadyHint.Text="SELECT BATTERS & BOWLER";ScoringReadyDetail.Text="Choose the opening pair, then the bowler →";}
        else if(!batters) {ScoringReadyHint.Text="SELECT NEXT BATTER";ScoringReadyDetail.Text="Choose the player to resume scoring →";}
        else if(!bowler) {ScoringReadyHint.Text="SELECT NEXT BOWLER";ScoringReadyDetail.Text=innings.LegalBallsBowled==0 ? "Choose the opening bowler →" : "The over is complete · choose the next bowler →";}
        else {ScoringReadyHint.Text="";ScoringReadyDetail.Text="";}
        CricketPrompt.ApplyActionColour(ScoringLockBanner,key,true);
        ScoringReadyHint.SetBinding(Avalonia.Controls.TextBlock.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){Source=ScoringLockBanner});
        ScoringReadyDetail.SetBinding(Avalonia.Controls.TextBlock.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){Source=ScoringLockBanner});
        EndInningsButton.Content=innings.IsComplete?(_match.AllInnings.Count==1?"Next innings":"Innings options"):"End innings";
        EndInningsButton.ContentTemplate=CricketPrompt.ButtonContent;
        EndInningsButton.FontSize=14;
    }
}
