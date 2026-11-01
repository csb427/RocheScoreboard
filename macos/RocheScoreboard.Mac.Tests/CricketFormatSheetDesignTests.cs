using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Models;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static async Task<Border> FormatSheetOpen(Roche_Scoreboard.Views.CricketControlPanel panel,Window window,string action="conditions")
    {
        var options=Named<Button>(panel,"MatchOptionsButton");await Reveal(window,options);await StableReachable(window,options);TapNow(window,options);await Settle(500);
        var choose=PromptAction(PromptSheet(panel),action);await Reveal(window,choose);await StableReachable(window,choose);TapNow(window,choose);await Settle(550);
        return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,false)]
    [InlineData(1280,850,true)][InlineData(420,320,true)]
    [Trait("Evidence","CricketFormatSheet")]
    public async Task CurrentFormatSheetKeepsConditionsDraftUntilApply(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        var panel=new Roche_Scoreboard.Views.CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string initial=State(match);var sheet=await FormatSheetOpen(panel,window);
        var format=PromptAction(sheet,"conditions-format-LimitedOvers");await Reveal(window,format);await StableReachable(window,format);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"format-open-{width}-{reduced}");
        var overs=Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-overs"));
        var proposed=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"conditions-proposed"));
        if(width==1280 && !reduced) {
            overs.Text="0";var invalidApply=PromptAction(sheet,"apply");await Reveal(window,invalidApply);TapNow(window,invalidApply);
            await Until(()=>overs.IsFocused,"Invalid overs must receive focus inside the same owned sheet.");Assert.Same(sheet,PromptSheet(panel));Assert.True(Ready(window,overs));
            Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Enter a total over limit from 1 to 1,000.");
            Assert.Equal("0",overs.Text);Assert.Equal(initial,State(match));
        }
        overs.Text="37";await Settle(50);Assert.Equal("Draft: 37 overs per innings",proposed.Text);
        foreach(string tag in new[]{"conditions-format-MultiInnings","conditions-format-LimitedOvers"}) {
            var card=PromptAction(sheet,tag);await Reveal(window,card);await StableReachable(window,card);
            KeyNow(window,PhysicalKey.Space);await Settle(450);
            Assert.True(card.Bounds.Height>=88);
            var selected=sheet.GetVisualDescendants().OfType<Button>().Where(x=>x.Tag is string id && id.StartsWith("conditions-format-") && Avalonia.Automation.AutomationProperties.GetItemStatus(x)=="Selected").ToArray();
            Assert.Same(card,Assert.Single(selected));Assert.Equal(1,Assert.Single(card.GetVisualDescendants().OfType<OperatorChoiceSurface>()).Progress);
            Assert.Equal(tag.EndsWith("MultiInnings")?"Draft: Multi-innings":"Draft: 37 overs per innings",proposed.Text);
            Assert.Equal("37",overs.Text);Assert.Equal(initial,State(match));
        }
        var preset=PromptAction(sheet,"conditions-overs-50");await Reveal(window,preset);await StableReachable(window,preset);TapNow(window,preset);await Settle(400);
        Assert.Equal("50",overs.Text);Assert.Equal("Draft: 50 overs per innings",proposed.Text);
        Assert.Equal("Selected",Avalonia.Automation.AutomationProperties.GetItemStatus(preset));Assert.True(preset.Bounds.Height>=44);
        PromptCapture(window,$"format-draft-{width}-{reduced}");
        Assert.Equal(initial,State(match));var close=PromptAction(sheet,"close");await Reveal(window,close);await StableReachable(window,close);TapNow(window,close);await PromptRetired(panel,sheet);
        Assert.Equal(initial,State(match));
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketFormatSheet")]
    public async Task FormatSheetAppliesCurrentDraftAndRejectsPlayWhileOpen(bool stale)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var panel=new Roche_Scoreboard.Views.CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var sheet=await FormatSheetOpen(panel,window);var overs=Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-overs"));overs.Text="50";
        if(stale)match.RecordDelivery(1,CricketDeliveryType.Runs);
        string before=State(match);var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);
        if(stale) {
            await Until(()=>sheet.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="The match changed while this panel was open. Go back and reopen it before applying changes."),"Stale condition token must reject in place.");
            var rejection=PromptSheet(panel);Assert.Same(sheet,rejection);Assert.Contains(rejection.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="The match changed while this panel was open. Go back and reopen it before applying changes.");
            Assert.Equal(before,State(match));var close=PromptAction(rejection,"close");await Reveal(window,close);TapNow(window,close);await PromptRetired(panel,rejection);Assert.Equal(before,State(match));
        }
        else {
            await PromptRetired(panel,sheet);Assert.Equal(50,match.TotalOvers);Assert.Equal(CricketFormat.LimitedOvers,match.Format);
            Assert.Equal(0,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.TotalWickets);Assert.Empty(match.CurrentInnings.Deliveries);
        }
    }

}
