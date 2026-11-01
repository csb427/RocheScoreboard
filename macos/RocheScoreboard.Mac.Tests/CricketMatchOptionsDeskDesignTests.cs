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
    private static async Task<Border> OpenOptionsDesk(CricketControlPanel panel,Window window)
    {
        var button=Named<Button>(panel,"MatchOptionsButton");await Reveal(window,button);await StableReachable(window,button);TapNow(window,button);await Settle(550);return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketMatchOptionsDesk")]
    public async Task MatchOptionsAndBreakReviewCanReturnWithoutChangingTheMatch(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);string before=State(match);
        var sheet=await OpenOptionsDesk(panel,window);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"match-options-context"));
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="MATCH AT OPENING");
        Assert.Contains(PromptAction(sheet,"rain").GetVisualDescendants().OfType<TextBlock>(),x=>x.Text!.Contains("No DLS calculation"));
        PromptCapture(window,$"options-desk-{width}");
        sheet=await ReplacementNext(panel,window,sheet,"break");
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"match-break-context"));
        var rain=PromptAction(sheet,"Rain");await Reveal(window,rain);await StableReachable(window,rain);PromptCapture(window,$"options-break-{width}");
        sheet=await ReplacementNext(panel,window,sheet,"back");Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketMatchOptionsDesk")]
    public async Task ChoosingRainRecordsOnlyTheExistingBreakTransition()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await OpenOptionsDesk(panel,window);sheet=await ReplacementNext(panel,window,sheet,"break");
        int changes=0;match.MatchChanged+=()=>changes++;
        var rain=PromptAction(sheet,"Rain");await Reveal(window,rain);await StableReachable(window,rain);TapNow(window,rain);await PromptRetired(panel,sheet);
        Assert.Equal(CricketBreakKind.Rain,match.BreakKind);Assert.Equal(1,changes);Assert.Equal(4,match.TotalRuns);Assert.Equal(0,match.CurrentInnings!.TotalWickets);Assert.Single(match.CurrentInnings.Deliveries);Assert.Equal(1,match.CurrentInnings.LegalBallsBowled);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketMatchOptionsDesk")]
    public async Task EndedMatchDeskShowsRecordedStateAndPreservesActionAvailability()
    {
        using var scope=new Scope();var match=EndedForReopen();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        string before=State(match);var sheet=await OpenOptionsDesk(panel,window);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="No result");
        foreach(string absent in new[]{"break","conditions","rain","innings","before-delivery"})Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,absent));
        foreach(string present in new[]{"teams","outcome"}){var action=PromptAction(sheet,present);await Reveal(window,action);await StableReachable(window,action);}
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));
    }

}
