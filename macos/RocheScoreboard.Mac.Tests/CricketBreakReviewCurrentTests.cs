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
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","CricketBreakReviewCurrent")]
    public async Task AStaleBreakReviewCannotOverwriteANewerBreakOrItsNote(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        var sheet=await OpenOptionsDesk(panel,window);sheet=await ReplacementNext(panel,window,sheet,"break");
        // Public model publication represents another accepted owner action;
        // no private fields, timer callbacks, or prompt results are injected.
        match.SetBreak(CricketBreakKind.Drinks,"Official drinks interval");await Settle(100);
        string changed=State(match);int publications=0;match.MatchChanged+=()=>publications++;
        var rain=PromptAction(sheet,"Rain");await Reveal(window,rain);await StableReachable(window,rain);TapNow(window,rain);await Settle(450);
        Assert.Equal(changed,State(match));Assert.Equal(0,publications);
        var notice=PromptSheet(panel);Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Break was not changed");
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)Directory.CreateDirectory(dir);
        PromptCapture(window,"stale-break-review-"+width);
        var close=PromptAction(notice,"close");await Reveal(window,close);await StableReachable(window,close);TapNow(window,close);await PromptRetired(panel,notice);
        Assert.Equal(changed,State(match));Assert.Equal(0,publications);
        // An explicit fresh review still applies immediately and exactly once.
        sheet=await OpenOptionsDesk(panel,window);sheet=await ReplacementNext(panel,window,sheet,"break");
        rain=PromptAction(sheet,"Rain");await Reveal(window,rain);await StableReachable(window,rain);TapNow(window,rain);await PromptRetired(panel,sheet);
        Assert.Equal(CricketBreakKind.Rain,match.BreakKind);Assert.Equal(1,publications);
        Assert.Equal(4,match.TotalRuns);Assert.Single(match.CurrentInnings!.Deliveries);
    }
}
