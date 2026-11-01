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
    private static CricketMatchManager EndedForReopen(bool priorBreak=false)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        if(priorBreak)match.SetBreak(CricketBreakKind.Drinks,"Drinks break");
        Assert.True(match.TrySetMatchOutcome(CricketMatchResultKind.NoResult,"Official test decision",match.WorkflowToken,out string error),error);
        return match;
    }
    private static async Task<Border> OpenReopenReview(CricketControlPanel panel,Window window)
    {
        var review=await FormatSheetOpen(panel,window,"outcome");var reopen=PromptAction(review,"reopen");
        await Reveal(window,reopen);await StableReachable(window,reopen);TapNow(window,reopen);
        await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(review),"Recorded review must yield to the explicit reopening confirmation.");await Settle(550);
        return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketReopenReview")]
    public async Task RecordedOutcomeReopenConfirmationCanBeDeclinedWithoutLosingRecord(int width,int height)
    {
        using var scope=new Scope();var match=EndedForReopen();
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var sheet=await OpenReopenReview(panel,window);
        Assert.True(PromptAction(sheet,"cancel").IsFocused);
        var context=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"reopen-outcome-context"));
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="No result");
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Official test decision");
        var confirm=PromptAction(sheet,"confirm-reopen");Assert.False(confirm.IsDefault);await Reveal(window,confirm);await StableReachable(window,confirm);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"reopen-open-{width}");Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));Assert.True(match.IsMatchFinalized);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketReopenReview")]
    public async Task ReopenReviewPreservesFiguresAndRestoresBreakOrRejectsNewerMatchState(bool stale)
    {
        using var scope=new Scope();var match=EndedForReopen(!stale);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await OpenReopenReview(panel,window);
        if(stale) {
            Assert.True(match.TryReopenMatch(match.WorkflowToken,out string error),error);match.RecordDelivery(1,CricketDeliveryType.Runs);
        }
        string before=State(match);int revision=match.DeliveryRevision;
        var confirm=PromptAction(sheet,"confirm-reopen");await Reveal(window,confirm);await StableReachable(window,confirm);TapNow(window,confirm);
        if(stale) {
            await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"Old confirmation must yield to existing reopen rejection.");await Settle(550);
            var rejected=PromptSheet(panel);Assert.Contains(rejected.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Match was not reopened");
            Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.False(match.IsMatchFinalized);
            KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,rejected);Assert.Equal(before,State(match));
        }
        else {
            await Until(()=>!match.IsMatchFinalized,"Current explicit confirmation must reopen through existing model.");await PromptRetired(panel,sheet);
            Assert.Equal(4,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.TotalWickets);Assert.Single(match.CurrentInnings.Deliveries);
            Assert.Equal(CricketBreakKind.Drinks,match.BreakKind);Assert.Equal("Drinks break",match.BreakNote);
            Assert.Null(match.ClosureBefore);Assert.Equal("",match.ResultNote);Assert.Equal(CricketMatchResultKind.ScoreBased,match.ResultKind);
            Assert.Equal(revision+1,match.DeliveryRevision);
        }
    }

}
