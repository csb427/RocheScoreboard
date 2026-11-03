using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
internal static class PostDialogInput
{
    internal static void Click(Window window,Point point)
    {window.MouseMove(point);window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);}
    internal static void Record(Control panel,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} path)return;
        Directory.CreateDirectory(path);
        var deadline=(DateTime)panel.GetType().GetField("_deliveryPointerGuardUntil",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(panel)!;
        File.WriteAllText(Path.Combine(path,name+".json"),JsonSerializer.Serialize(new{now=DateTime.UtcNow,deadline,guardActive=deadline>DateTime.UtcNow}));
    }
}
public sealed partial class CricketDeliveryD23RegressionDraftTests
{
    [AvaloniaFact]
    [Trait("Evidence","PostDialogFreshInput")]
    public async Task RoutedClosingContinuationsStayBlockedUntilADistinctFreshPressEndsTheGuard()
    {
        using var f=await DeliveryFixture.OpenActual("Wicket",1280,850);
        var score=Named<Control>(f.Panel,"ScoringCommands").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"1"));
        var point=Painted(f.Window,score).Center;
        PointerPressedEventArgs? press=null;
        EventHandler<PointerPressedEventArgs> observe=(_,e)=>press=e;
        f.Window.AddHandler(InputElement.PointerPressedEvent,observe,Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
        try
        {
            var close=TopClose(f.Entry);await Reveal(f.Window,close);await StableReachable(f.Window,close);var closing=Painted(f.Window,close).Center;
            PostDialogInput.Click(f.Window,closing);Assert.False(f.Overlay.IsVisible);Assert.NotNull(press);var original=press;
            // Explicit routed-event policy checks; these do not establish the
            // operating system's physical double-click/touch classification.
            var repeated=new PointerPressedEventArgs(score,original.Pointer,f.Window,point,original.Timestamp+1,original.GetCurrentPoint(f.Window).Properties,KeyModifiers.None,2);
            score.RaiseEvent(repeated);Assert.True(repeated.Handled);Assert.False(score.IsPressed);
            var samePosition=new PointerPressedEventArgs(f.Panel,original.Pointer,f.Window,closing,original.Timestamp+2,original.GetCurrentPoint(f.Window).Properties,KeyModifiers.None,1);
            f.Panel.RaiseEvent(samePosition);Assert.True(samePosition.Handled);Assert.Equal(0,f.Match.TotalRuns);
            PostDialogInput.Click(f.Window,point);PostDialogInput.Click(f.Window,point);
            Assert.Equal(2,f.Match.TotalRuns);Assert.Equal(2,f.Match.CurrentInnings!.Deliveries.Count);
        }
        finally{f.Window.RemoveHandler(InputElement.PointerPressedEvent,observe);}
    }

    [AvaloniaTheory]
    [InlineData("Wicket",false)][InlineData("Wicket",true)][InlineData("Wide",false)][InlineData("Wide",true)]
    [Trait("Evidence","PostDialogFreshInput")]
    public async Task CancelledDeliveryDoesNotDiscardFreshRapidScoreClicks(string source,bool keyboard)
    {
        using var f=await DeliveryFixture.OpenActual(source,1280,850);
        var score=Named<Control>(f.Panel,"ScoringCommands").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"1"));
        var point=Painted(f.Window,score).Center;
        if(keyboard){f.Window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);}
        else
        {
            var close=TopClose(f.Entry);await Reveal(f.Window,close);await StableReachable(f.Window,close);
            PostDialogInput.Click(f.Window,Painted(f.Window,close).Center);
        }
        Assert.False(f.Overlay.IsVisible);Assert.Equal(f.OpenedState,State(f.Match));
        PostDialogInput.Record(f.Panel,"delivery-close-"+source+"-"+keyboard);
        Assert.True(Ready(f.Window,score));
        PostDialogInput.Click(f.Window,point);PostDialogInput.Click(f.Window,point);
        Assert.Equal(2,f.Match.TotalRuns);Assert.Equal(2,f.Match.CurrentInnings!.Deliveries.Count);Assert.Equal(2,f.ModelChanges);
        Assert.Equal(1,f.EditorClosures);
    }
}
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","PostDialogFreshInput")]
    public async Task CancelledFigureDoesNotDiscardTheNextFreshNavigationClick(bool keyboard)
    {
        using var scope=new Scope();var match=Roche_Scoreboard.Services.CricketReliabilityChecks.CreateMatch(true);
        var f=await FigureOpen(scope,match,900,700,false);string before=State(match);
        var output=Named<TabItem>(f.Panel,"OutputTab");var point=Painted(f.Window,output).Center;
        if(keyboard){f.Window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);}
        else
        {
            var close=PromptAction(f.Sheet,"back");await Reveal(f.Window,close);await StableReachable(f.Window,close);
            PostDialogInput.Click(f.Window,Painted(f.Window,close).Center);
        }
        await Until(()=>!f.Panel.HasActiveDecision,"Actual cancellation must finish before the new click.");
        PostDialogInput.Record(f.Panel,"figure-close-"+keyboard);
        PostDialogInput.Click(f.Window,point);
        Assert.Same(output,Named<TabControl>(f.Panel,"MainTabControl").SelectedItem);Assert.Equal(before,State(match));
    }
}
