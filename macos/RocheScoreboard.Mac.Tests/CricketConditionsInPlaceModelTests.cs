using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static CricketMatchManager ConditionsAfterSevenLegalBalls()
    {
        var match=RevisionMatch(false);
        for(int i=0;i<7;i++)
        {
            if(match.CurrentInnings!.NeedsBowlerSelection)match.ConfirmBowler(match.SuggestedNextBowler());
            match.RecordDelivery(1,CricketDeliveryType.Runs);
        }
        Assert.Equal(7,match.CurrentInnings!.LegalBallsBowled);Assert.Equal(7,match.CurrentInnings.TotalRuns);return match;
    }
    [AvaloniaTheory]
    [InlineData(false,1280,850)] [InlineData(true,1280,850)]
    [InlineData(false,420,320)] [InlineData(true,420,320)]
    [Trait("Evidence","CricketConditionsInPlaceModel")]
    public async Task ModelRejectedOverLimitStaysInTheSameConditionsSheetWithoutChangingMatch(bool rain,int width,int height)
    {
        using var scope=new Scope(true);var match=ConditionsAfterSevenLegalBalls();var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(250);var sheet=await FormatSheetOpen(panel,window,rain?"rain":"conditions");
        var heading=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==(rain?"Revised overs":"Format and over limit"));Assert.Null(ToolTip.GetTip(heading));
        var overs=Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-overs"));overs.Text="1";
        string unchanged=State(match),token=match.MatchConditionsToken;int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);
            await Until(()=>panel.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text?.Contains("legal balls already bowled")==true),"Actual existing model rejection must be shown.");
            Assert.Equal(unchanged,State(match));Assert.Equal(token,match.MatchConditionsToken);Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Assert.Same(sheet,PromptSheet(panel));Assert.Equal("1",overs.Text);
            await Until(()=>overs.IsFocused&&Ready(window,overs),"Rejected current field must be focused/revealed without a replacement sheet.");
            var reason=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("legal balls already bowled")==true);
            Assert.Same(overs.GetVisualParent(),reason.GetVisualParent());
            await Until(()=> {
                var viewport=reason.GetVisualAncestors().OfType<ScrollViewer>().First();
                var point=reason.TranslatePoint(default,window);var top=viewport.TranslatePoint(default,window);
                return point is {} origin && top is {} start && new Rect(start,viewport.Bounds.Size).Contains(new Rect(origin,reason.Bounds.Size));
            },"Actual rejection explanation must be visible beside the focused field, not below an unseen part of the form.");
            PromptCapture(window,$"conditions-model-rejection-{rain}-{width}");
            if(width==420)
            {
                var close=PromptAction(sheet,"close");await Reveal(window,close);await StableReachable(window,close);TapNow(window,close);await PromptRetired(panel,sheet);
                Assert.Equal(unchanged,State(match));Assert.Equal(0,events);
            }
            else
            {
                overs.Text="10";apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);await PromptRetired(panel,sheet);
                Assert.Equal(10,match.CurrentOverLimit);Assert.Equal(rain?20:10,match.TotalOvers);Assert.Equal(rain,match.HasRainAdjustment);
                Assert.Equal(revision+1,match.DeliveryRevision);Assert.Equal(1,events);Assert.Equal(7,match.CurrentInnings!.LegalBallsBowled);Assert.Equal(7,match.CurrentInnings.TotalRuns);
            }
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","CricketConditionsPreflightModel")]
    public void ConditionsPreflightIsPureAndFinalCommitStillRejectsStaleReceipts(bool rain)
    {
        var match=ConditionsAfterSevenLegalBalls();string token=match.MatchConditionsToken,before=State(match);int revision=match.DeliveryRevision,events=0;
        void Changed()=>events++;match.MatchChanged+=Changed;
        bool Preview(int limit,string receipt,out string error)=>rain?match.CanApplyRainAdjustment(limit,null,receipt,out error):match.CanUpdateMatchConditions(CricketFormat.LimitedOvers,limit,receipt,out error);
        bool Commit(int limit,string receipt,out string error)=>rain?match.TryApplyRainAdjustment(limit,null,receipt,out error):match.TryUpdateMatchConditions(CricketFormat.LimitedOvers,limit,receipt,out error);
        try
        {
            Assert.False(Preview(1,token,out string previewError));Assert.False(Commit(1,token,out string commitError));Assert.Equal(previewError,commitError);
            Assert.Equal(before,State(match));Assert.Equal(token,match.MatchConditionsToken);Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Assert.True(Preview(10,token,out previewError),previewError);Assert.Equal("",previewError);Assert.Equal(before,State(match));Assert.Equal(0,events);
            Assert.True(Commit(10,token,out commitError),commitError);Assert.Equal(1,events);Assert.Equal(revision+1,match.DeliveryRevision);
            string committed=State(match),current=match.MatchConditionsToken;
            Assert.False(Preview(12,token,out previewError));Assert.False(Commit(12,token,out commitError));Assert.Equal(previewError,commitError);
            Assert.Equal(committed,State(match));Assert.Equal(current,match.MatchConditionsToken);Assert.Equal(1,events);
        }
        finally{match.MatchChanged-=Changed;}
    }

}
