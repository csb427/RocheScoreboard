using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    [Trait("Evidence","ScoreBarResetRefresh")]
    public async Task BoundBarCannotPublishResetTotalsWithAnOldScoreCueStillVisible(bool home)
    {
        await using var storage=await AflFixture.Create(1280,850);
        var match=DeltaOwnerMatch(false);var bar=new PresentationScoreBar { Height=64 };
        var window=new Window { Width=707,Height=100,Content=bar };
        try
        {
            DeltaOwnerPopulate(bar,match);window.Show();await Settle(100);
            var score=home?match.AddGoal(TeamSide.Home):match.AddBehind(TeamSide.Away);
            DeltaOwnerPopulate(bar,match);bar.PlayScoreAnimation(score);await Settle(80);
            Assert.True(DeltaOwnerTimerRunning(bar,home));
            Assert.Equal(1,Named<Border>(bar,DeltaOwnerSide(home)+"DeltaOverlay").Opacity);
            match.ResetForNewGame();string state=DeltaOwnerState(match);
            // Deliberately no Populate. The existing public bar refreshes
            // scores every500ms even without a caller rebinding its assets.
            await Until(()=>Named<TextBlock>(bar,"HomeTotalText").Text=="0"
                &&Named<TextBlock>(bar,"AwayTotalText").Text=="0","The real bar timer must publish the reset totals.");
            DeltaOwnerRecord("reset-without-populate-"+home,new { home,pose=DeltaOwnerPose(bar,home),before=state,after=DeltaOwnerState(match) });
            Capture(window,"scorebar-reset-totals-"+home);
            DeltaOwnerGone(bar,true);DeltaOwnerGone(bar,false);Assert.Equal(state,DeltaOwnerState(match));
            var fresh=match.AddBehind(home?TeamSide.Away:TeamSide.Home);bar.PlayScoreAnimation(fresh);
            Assert.True(DeltaOwnerTimerRunning(bar,!home));Assert.Equal("+1",Named<TextBlock>(bar,DeltaOwnerSide(!home)+"DeltaText").Text);
            string accepted=DeltaOwnerState(match);await Settle(1800);
            DeltaOwnerGone(bar,true);DeltaOwnerGone(bar,false);Assert.Equal(accepted,DeltaOwnerState(match));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    [Trait("Evidence","ScoreBarResetRefresh")]
    public async Task FreshCueBeforeTheNextResetRefreshRetiresOnlyTheOldLifetime()
    {
        await using var storage=await AflFixture.Create(1280,850);
        var match=DeltaOwnerMatch(false);var bar=new PresentationScoreBar { Height=64 };
        var window=new Window { Width=707,Height=100,Content=bar };
        try
        {
            DeltaOwnerPopulate(bar,match);window.Show();await Settle(100);
            var old=match.AddGoal(TeamSide.Home);DeltaOwnerPopulate(bar,match);bar.PlayScoreAnimation(old);await Settle(80);
            Assert.True(DeltaOwnerTimerRunning(bar,true));
            match.ResetForNewGame();var fresh=match.AddGoal(TeamSide.Away);bar.PlayScoreAnimation(fresh);
            DeltaOwnerGone(bar,true);Assert.True(DeltaOwnerTimerRunning(bar,false));
            string state=DeltaOwnerState(match);
            await Until(()=>Named<TextBlock>(bar,"HomeTotalText").Text=="0"
                &&Named<TextBlock>(bar,"AwayTotalText").Text=="6","The ordinary refresh must publish current totals.");
            Assert.True(DeltaOwnerTimerRunning(bar,false));Assert.Equal("+6",Named<TextBlock>(bar,"AwayDeltaText").Text);
            Assert.Equal(1,Named<Border>(bar,"AwayDeltaOverlay").Opacity);Assert.Equal(state,DeltaOwnerState(match));
            await Settle(1900);DeltaOwnerGone(bar,true);DeltaOwnerGone(bar,false);Assert.Equal(state,DeltaOwnerState(match));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    [Trait("Evidence","ScoreBarResetBreakRefresh")]
    public async Task NaturalBreakFadeCannotCarryItsPhaseAcrossAResetBeforeTheBarRefreshes()
    {
        await using var storage=await AflFixture.Create(1280,850);
        var match=BreakFadeMatch(1);var bar=new PresentationScoreBar { Height=64 };
        var window=new Window { Width=707,Height=100,Content=bar };
        try
        {
            BreakFadePopulate(bar,match,1);window.Show();await Settle(100);
            var text=Named<TextBlock>(bar,"BreakCenterText");
            for(int i=0;i<230&&!(Motion.IsAnimating(text)&&text.Opacity>.02&&text.Opacity<.99);i++)await Settle(100);
            Assert.True(Motion.IsAnimating(text));Assert.InRange(text.Opacity,.020001,.989999);
            Capture(window,"scorebar-reset-break-active");
            Assert.True(Motion.IsAnimating(text),"The reset must still interrupt the real natural fade after capturing its frame.");
            match.ResetForNewGame();match.AddBehind(TeamSide.Home);string state=DeltaOwnerState(match);
            await Until(()=>Named<TextBlock>(bar,"HomeTotalText").Text=="1","Witness a real post-reset timer refresh,not a guessed delay.");
            Assert.False(Motion.IsAnimating(text));Assert.Equal(1,text.Opacity);
            Assert.Equal(QuarterLabelService.GetBreakLabel(1),text.Text);
            Assert.True(HiddenBarTimer(bar,"_breakCentreTimer"));Assert.Equal(state,DeltaOwnerState(match));
            Capture(window,"scorebar-reset-break-settled");
        }
        finally { window.Close(); }
    }
}
