using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static int[] VisibleDeliveryOvers(CricketControlPanel panel)=>Named<StackPanel>(panel,"EventLog").Children.OfType<StackPanel>()
        .Where(g=>g.Tag is int&&g.IsVisible).Select(g=>(int)g.Tag!).ToArray();
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","CricketDeliveryFilterPolicy")]
    public async Task OverFilterHasPurposeAndPreservesSpecificOverAcrossDeliveryAndUndo(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        for(int i=0;i<6;i++)match.RecordDelivery(0,CricketDeliveryType.Dot);
        match.ConfirmBowler(1);for(int i=0;i<2;i++)match.RecordDelivery(0,CricketDeliveryType.Dot);
        Assert.Equal(8,match.CurrentInnings!.Deliveries.Count);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(450);
        var filter=Named<ComboBox>(panel,"DeliveryOverChoice");Assert.Equal("Filter delivery log by over",ControlAutomationPeer.CreatePeerForElement(filter)!.GetName());
        await Reveal(window,filter);string before=State(match);
        await Key(window,PhysicalKey.ArrowDown);await Key(window,PhysicalKey.ArrowDown);
        Assert.Equal("Over 1",filter.SelectedItem!.ToString());Assert.Equal(new[]{0},VisibleDeliveryOvers(panel));Assert.Equal(before,State(match));
        await Key(window,PhysicalKey.F4);Assert.True(filter.IsDropDownOpen);await Key(window,PhysicalKey.Escape);Assert.False(filter.IsDropDownOpen);Assert.Equal("Over 1",filter.SelectedItem.ToString());
        match.RecordDelivery(0,CricketDeliveryType.Dot);await Settle();Assert.Equal(9,match.CurrentInnings.Deliveries.Count);Assert.Equal("Over 1",filter.SelectedItem.ToString());Assert.Equal(new[]{0},VisibleDeliveryOvers(panel));
        Assert.True(match.UndoLastDelivery());await Settle();Assert.Equal(8,match.CurrentInnings.Deliveries.Count);Assert.Equal("Over 1",filter.SelectedItem!.ToString());Assert.Equal(new[]{0},VisibleDeliveryOvers(panel));
        string restored=State(match);await Reveal(window,filter);await Key(window,PhysicalKey.ArrowUp);await Key(window,PhysicalKey.ArrowUp);
        Assert.Equal("All overs · latest first",filter.SelectedItem!.ToString());Assert.Equal(new[]{1,0},VisibleDeliveryOvers(panel));Assert.Equal(restored,State(match));
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,"over-filter-"+width);
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","CricketDeliveryFilterPolicy")]
    public async Task ActualNewDeliveryBadgeSettlesOnLiveReduceOrRemovalWithoutMutatingScore(bool remove)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(0,CricketDeliveryType.Dot);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(450);
        window.MouseMove(new Point(1,1));window.MouseDown(new Point(1,1),MouseButton.Left);window.MouseUp(new Point(1,1),MouseButton.Left);
        match.RecordDelivery(1,CricketDeliveryType.Runs);Dispatcher.UIThread.RunJobs();
        var delivery=match.CurrentInnings!.Deliveries[^1];var row=DeliveryHistoryRow(panel,delivery);
        var badge=Assert.Single(row.GetVisualDescendants().OfType<Border>(),b=>b.Width==28&&b.Height==24);
        Assert.True(Motion.IsAnimating(badge),"Actual newly appended badge clock must be active; no synthetic/private animation invocation.");
        string before=State(match);int revision=match.DeliveryRevision,changes=0;match.MatchChanged+=()=>changes++;
        if(remove)panel.ClearMatch();else OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
        Assert.False(Motion.IsAnimating(badge));Assert.Equal(1,badge.Opacity);await Settle(200);
        Assert.False(Motion.IsAnimating(badge));Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,changes);
        if(remove)Assert.False(row.IsLoaded);else Assert.True(row.IsLoaded);
    }
}
