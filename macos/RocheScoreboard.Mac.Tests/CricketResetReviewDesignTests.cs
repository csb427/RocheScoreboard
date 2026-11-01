using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static Button CricketResetAction(CricketControlPanel panel)=>Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Reset match"));
    private static async Task<Border> OpenCricketResetReview(CricketControlPanel panel,Window window,bool keyboard=false)
    {
        var reset=CricketResetAction(panel);await Reveal(window,reset);
        Record("reset-entry-"+window.ClientSize.Width+"-"+keyboard,new{box=Painted(window,reset),probes=DockEdgePoints(Painted(window,reset)).Select(point=>new{point,owned=Within(reset,window.InputHitTest(point)),type=window.InputHitTest(point)?.GetType().Name,name=(window.InputHitTest(point)as Control)?.Name})});
        if(keyboard)KeyNow(window,PhysicalKey.Enter);else{await StableReachable(window,reset);TapNow(window,reset);}
        await Settle(550);return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketResetReview")]
    public async Task CricketResetReviewCanKeepTheCurrentMatchWithoutRaisingReset(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        int requests=0;panel.ResetRequested+=()=>requests++;string before=State(match);var sheet=await OpenCricketResetReview(panel,window);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-reset-context"));
        Assert.True(PromptAction(sheet,"cancel").IsFocused);Assert.False(PromptAction(sheet,"reset").IsDefault);Assert.False(PromptAction(sheet,"keep").IsDefault);
        PromptCapture(window,$"reset-review-{width}");
        if(width==1280)KeyNow(window,PhysicalKey.Enter);else KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(0,requests);Assert.Equal(before,State(match));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketResetReview")]
    public async Task OldResetConfirmationCannotDiscardPlayRecordedWhileReviewWasOpen()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        int requests=0;panel.ResetRequested+=()=>requests++;var sheet=await OpenCricketResetReview(panel,window,true);match.RecordDelivery(1,CricketDeliveryType.Runs);string before=State(match);
        var reset=PromptAction(sheet,"reset");await Reveal(window,reset);await StableReachable(window,reset);TapNow(window,reset);await Settle(550);
        Assert.Equal(0,requests);Assert.Equal(before,State(match));
        var current=PromptSheet(panel);Assert.Contains(current.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Match was not reset");KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,current);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketResetReview")]
    public async Task CurrentResetReviewEmitsOneRequestAndRetainedConfirmationCannotRepeatIt()
    {
        using var scope=new Scope(true);var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        int requests=0;panel.ResetRequested+=()=>requests++;string before=State(match);var sheet=await OpenCricketResetReview(panel,window);
        var reset=PromptAction(sheet,"reset");await Reveal(window,reset);await StableReachable(window,reset);TapNow(window,reset);await PromptRetired(panel,sheet);
        Assert.Equal(1,requests);Assert.Equal(before,State(match));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(150);Assert.Equal(1,requests);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketResetReview")]
    public async Task ReplacingTheMatchInvalidatesItsOldResetReview()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        int requests=0;panel.ResetRequested+=()=>requests++;var sheet=await OpenCricketResetReview(panel,window);var reset=PromptAction(sheet,"reset");
        var replacement=CricketReliabilityChecks.CreateMatch(true);replacement.RecordDelivery(2,CricketDeliveryType.Runs);string before=State(replacement);panel.SetMatch(replacement);await Settle(250);
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(150);Assert.Equal(0,requests);Assert.Equal(before,State(replacement));Assert.False(panel.HasActiveDecision);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketResetReview")]
    public async Task AResetButtonRetainedAfterClearCannotOpenAnEmptyResetScope()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var reset=CricketResetAction(panel);int requests=0;panel.ResetRequested+=()=>requests++;panel.ClearMatch();
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(200);Assert.Equal(0,requests);Assert.False(panel.HasActiveDecision);
        Assert.DoesNotContain(Named<Grid>(panel,"CricketRoot").Children.OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketResetReview")]
    public async Task HidingTheOwnerWhenResetSheetRetiresCannotDispatchTheOldRequest()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        int requests=0;panel.ResetRequested+=()=>requests++;string before=State(match);var sheet=await OpenCricketResetReview(panel,window);var reset=PromptAction(sheet,"reset");
        bool hidden=false;sheet.DetachedFromVisualTree+=(_,_)=>{hidden=true;window.Hide();};
        await Reveal(window,reset);await StableReachable(window,reset);TapNow(window,reset);await Settle(300);
        Assert.True(hidden,"Actual sheet retirement must hide the real owner before the reset continuation dispatches.");Assert.False(window.IsVisible);
        Assert.Equal(0,requests);Assert.Equal(before,State(match));Assert.False(panel.HasActiveDecision);
    }

}
