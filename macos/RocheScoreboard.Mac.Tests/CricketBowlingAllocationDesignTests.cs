using Avalonia.Automation.Peers;
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
    private static async Task<Border> OpenAllocationReview(CricketControlPanel panel,Window window)
    {
        var tab=Named<TabItem>(panel,"ScorecardTab");await Reveal(window,tab);await StableReachable(window,tab);TapNow(window,tab);await Settle(550);
        var edit=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-edit-totals"));await Reveal(window,edit);
        Record("allocation-entry-hit",new{box=Painted(window,edit),edit.Bounds,edit.IsLoaded,edit.IsEffectivelyVisible,edit.IsEffectivelyEnabled,
            probes=DockEdgePoints(Painted(window,edit)).Select(point=>new{point,owned=Within(edit,window.InputHitTest(point)),type=window.InputHitTest(point)?.GetType().Name,name=(window.InputHitTest(point)as Control)?.Name}),
            ancestors=edit.GetVisualAncestors().OfType<Control>().Select(c=>new{c.Name,c.Tag,type=c.GetType().Name,c.ClipToBounds,box=Painted(window,c)})});
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,"allocation-entry");await StableReachable(window,edit);TapNow(window,edit);await Settle(550);return PromptSheet(panel);
    }
    private static TextBox AllocationField(Border sheet,string tag)=>Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,tag));
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketBowlingAllocation")]
    public async Task AllocationReviewRetainsNumbersAndCancelNeverApplies(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var sheet=await OpenAllocationReview(panel,window);var first=AllocationField(sheet,"scorecard-edit-bowler-runs-0");
        await Reveal(window,first);await StableReachable(window,first);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        var card=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"scorecard-bowling-allocation-0"));
        var grid=Assert.Single(Assert.IsType<StackPanel>(card.Child).Children.OfType<Grid>());
        var cells=grid.Children.OfType<StackPanel>().ToArray();Assert.Equal(3,cells.Length);
        Assert.Equal(width>=1000?1:0,Grid.GetColumn(cells[1]));Assert.Equal(width>=1000?0:1,Grid.GetRow(cells[1]));
        var all=sheet.GetVisualDescendants().OfType<TextBox>().Where(x=>x.Tag is string tag && tag.StartsWith("scorecard-edit-bowler-")).ToArray();
        Assert.Equal(match.CurrentInnings!.BowlingAttack.Count*3,all.Length);
        Assert.Equal(all.Length,all.Select(x=>ControlAutomationPeer.CreatePeerForElement(x)!.GetName()).Distinct(StringComparer.Ordinal).Count());
        foreach(string kind in new[]{"runs","wides","no-balls"})
        {
            var field=AllocationField(sheet,"scorecard-edit-bowler-"+kind+"-0");
            Assert.Contains("bowler 1 · "+match.CurrentInnings.BowlingAttack[0].FullName,ControlAutomationPeer.CreatePeerForElement(field)!.GetName());
            await Reveal(window,field);await StableReachable(window,field);Assert.True(field.Bounds.Height>=42);
        }
        await FigureType(window,first,"7");int caret=first.SelectionStart;
        if(width>=1000)
        {
            window.Width=420;window.Height=320;await Settle(200);Assert.Same(first,AllocationField(sheet,"scorecard-edit-bowler-runs-0"));Assert.Equal("7",first.Text);Assert.Equal(caret,first.SelectionStart);Assert.Equal(1,Grid.GetRow(cells[1]));
            window.Width=width;window.Height=height;await Settle(200);Assert.Same(first,AllocationField(sheet,"scorecard-edit-bowler-runs-0"));Assert.Equal(caret,first.SelectionStart);Assert.Equal(1,Grid.GetColumn(cells[1]));
        }
        await Reveal(window,first);await StableReachable(window,first);PromptCapture(window,$"allocation-first-{width}");Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketBowlingAllocation")]
    public async Task StaleAllocationDraftRemainsInItsCorrectionSheetWithAnExplanation(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        var sheet=await OpenAllocationReview(panel,window);match.RecordDelivery(1,CricketDeliveryType.Runs);string before=State(match);
        var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);await Settle(550);
        Assert.Same(sheet,PromptSheet(panel));Assert.Equal(before,State(match));
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-totals-structure-error") && x.IsVisible && x.Text!.Contains("match changed"));
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"allocation-stale-{width}");
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);
    }
}
