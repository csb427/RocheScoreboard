using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketOutcomeReview")]
    public async Task ActualOutcomeReviewKeepsDraftNoteAndCurrentScoreUntilChosen(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var opened=await PromptOpen(window,panel,"form");await Settle(550);var sheet=opened.Sheet;
        var note=Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"outcome-note"));
        await Reveal(window,note);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"outcome-open-{width}");
        var record=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"outcome-opening-record"));
        Assert.Contains(record.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="0/4");
        Assert.Contains(record.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==match.CurrentInnings!.BattingTeamName);
        foreach(string tag in new[]{"score-result","abandoned","no-result"}) {
            var action=PromptAction(sheet,tag);await Reveal(window,action);await StableReachable(window,action);
            Assert.True(action.GetVisualDescendants().OfType<TextBlock>().Count(x=>!string.IsNullOrWhiteSpace(x.Text))>=2,"Outcome must expose its actual label and consequence.");
        }
        PromptCapture(window,$"outcome-actions-{width}");
        note.Text="Official decision pending";Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));Assert.False(match.IsMatchFinalized);
    }
    [AvaloniaTheory]
    [InlineData("abandoned",CricketMatchResultKind.Abandoned)]
    [InlineData("no-result",CricketMatchResultKind.NoResult)]
    [InlineData("score-result",CricketMatchResultKind.Unspecified)]
    [Trait("Evidence","CricketOutcomeReview")]
    public async Task OutcomeReviewStillRecordsOnlyTheChosenKindAndNote(string tag,CricketMatchResultKind expected)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var opened=await PromptOpen(window,panel,"form");await Settle(550);var sheet=opened.Sheet;
        Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"outcome-note")).Text="Official test decision";
        int events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try {
            var action=PromptAction(sheet,tag);await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);
            await Until(()=>match.IsMatchFinalized,"Existing outcome action must still reach the authoritative model.");await PromptRetired(panel,sheet);
            Assert.Equal(expected,match.ResultKind);Assert.Equal("Official test decision",match.ResultNote);Assert.Equal(1,events);Assert.NotNull(match.ClosureBefore);
            Assert.Equal(4,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.TotalWickets);Assert.Single(match.CurrentInnings.Deliveries);
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketOutcomeReview")]
    public async Task OutcomeReviewRejectsItsOldWorkflowTokenAfterNewPlay()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var opened=await PromptOpen(window,panel,"form");await Settle(550);var sheet=opened.Sheet;
        match.RecordDelivery(1,CricketDeliveryType.Runs);string current=State(match);int revision=match.DeliveryRevision;
        var action=PromptAction(sheet,"no-result");await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);
        await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"Old outcome form must hand off to its existing rejection notice.");await Settle(550);
        var rejected=PromptSheet(panel);Assert.Contains(rejected.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Outcome was not recorded");
        Assert.False(match.IsMatchFinalized);Assert.Null(match.ClosureBefore);Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(current,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,rejected);Assert.Equal(current,State(match));
    }

}
