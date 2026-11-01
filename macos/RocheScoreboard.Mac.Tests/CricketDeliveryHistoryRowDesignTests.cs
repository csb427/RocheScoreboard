using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
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
    private static Border DeliveryHistoryRow(CricketControlPanel panel,CricketDelivery delivery)=>Assert.Single(Named<StackPanel>(panel,"EventLog").GetVisualDescendants().OfType<Border>(),x=>ReferenceEquals(x.Tag,delivery));
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketDeliveryHistoryRow")]
    public async Task DeliveryHistoryHoverKeepsRowsStableAndEditDiscoverable(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);match.RecordDelivery(1,CricketDeliveryType.Runs);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        var row=DeliveryHistoryRow(panel,match.CurrentInnings!.Deliveries[^1]);await Reveal(window,row);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} capture)Directory.CreateDirectory(capture);
        PromptCapture(window,$"delivery-entry-{width}");
        Record("delivery-entry-"+width,new{box=Painted(window,row),probes=DockEdgePoints(Painted(window,row)).Select(point=>new{point,owned=Within(row,window.InputHitTest(point)),type=window.InputHitTest(point)?.GetType().Name,name=(window.InputHitTest(point)as Control)?.Name})});await StableReachable(window,row);
        window.FocusManager!.ClearFocus();window.MouseMove(new Avalonia.Point(1,1));await Settle(250);Assert.True(Ready(window,row));double beforeHeight=row.Bounds.Height;string before=State(match);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        var affordance=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"delivery-edit-affordance"));
        Assert.InRange(row.Bounds.Height,40,48);Assert.Equal(0,affordance.Width);Assert.Equal(0,affordance.Opacity);Assert.False(affordance.IsHitTestVisible);
        var group=Assert.IsAssignableFrom<StackPanel>(row.GetVisualAncestors().OfType<StackPanel>().First(x=>x.Tag is int));
        var overHeader=Assert.IsAssignableFrom<Border>(group.Children[0]);
        Assert.InRange(Math.Abs(overHeader.Bounds.Width-row.Bounds.Width),0,.5);
        Assert.True(Painted(window,overHeader).Bottom+5<=Painted(window,row).Top,"The newest row must clear the over header.");
        PromptCapture(window,$"delivery-rest-{width}");window.MouseMove(Painted(window,row).Center);await Settle(350);PromptCapture(window,$"delivery-hover-{width}");
        Assert.Equal(beforeHeight,row.Bounds.Height);Assert.Equal(before,State(match));
        var edit=Assert.Single(row.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"edit-ball"));Assert.True(edit.Bounds.Height>=28);Assert.True(edit.IsEffectivelyVisible);Assert.Equal(82,affordance.Width);Assert.Equal(1,affordance.Opacity);Assert.True(affordance.IsHitTestVisible);
        for(int i=0;i<3;i++){window.MouseMove(new Avalonia.Point(1,1));await Settle(20);window.MouseMove(Painted(window,row).Center);await Settle(20);}
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await Settle(100);Assert.Equal(beforeHeight,row.Bounds.Height);Assert.Equal(before,State(match));
        window.MouseMove(new Avalonia.Point(1,1));await Settle(120);Assert.Equal(0,affordance.Width);
        await Reveal(window,row);await Settle(100);Assert.Equal(82,affordance.Width);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData("enter")][InlineData("space")][InlineData("invoke")][InlineData("pointer")][InlineData("edit-pointer")]
    [Trait("Evidence","CricketDeliveryHistoryRow")]
    public async Task DeliveryHistoryActivationOpensCurrentEditorAndCancelPreservesPlay(string input)
    {
        using var scope=new Scope(input=="space");var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var row=DeliveryHistoryRow(panel,match.CurrentInnings!.Deliveries[0]);await Reveal(window,row);await StableReachable(window,row);string before=State(match);
        var peer=ControlAutomationPeer.CreatePeerForElement(row);Assert.NotNull(peer);Assert.Contains("0.1",peer.GetName());Assert.Contains("Player 1",peer.GetName());
        if(input=="invoke")Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke();else if(input=="edit-pointer")
        {
            var edit=Assert.Single(row.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"edit-ball"));
            await StableReachable(window,edit);TapNow(window,edit);
        }
        else if(input=="pointer")TapNow(window,row);else
        {
            var key=input=="enter"?PhysicalKey.Enter:PhysicalKey.Space;
            var focusBefore=window.FocusManager?.GetFocusedElement()?.GetType().Name;
            window.KeyPressQwerty(key,RawInputModifiers.None);bool down=Named<ContentControl>(panel,"DeliveryEntryContent").Content is CricketDeliveryDialog;
            var focusDown=window.FocusManager?.GetFocusedElement()?.GetType().Name;
            window.KeyReleaseQwerty(key,RawInputModifiers.None);
            Record("delivery-key-"+input,new{focusBefore,down,focusDown,up=Named<ContentControl>(panel,"DeliveryEntryContent").Content is CricketDeliveryDialog,state=State(match)});
        }
        await Until(()=>Named<ContentControl>(panel,"DeliveryEntryContent").Content is CricketDeliveryDialog,"Actual history activation must open one embedded editor.");
        var editor=Assert.IsType<CricketDeliveryDialog>(Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(before,State(match));
        var cancel=Named<Button>(editor,"CancelButton");await Reveal(window,cancel);await StableReachable(window,cancel);TapNow(window,cancel);await Settle(200);
        Assert.Null(Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(before,State(match));Assert.True(Named<Button>(panel,"UndoDeliveryButton").IsFocused);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketDeliveryHistoryRow")]
    public async Task RetainedDeliveryRowsCannotOpenAgainstClearedOrReplacementMatch(bool replacement)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var row=DeliveryHistoryRow(panel,match.CurrentInnings!.Deliveries[0]);var edit=Assert.Single(row.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"edit-ball"));
        var next=CricketReliabilityChecks.CreateMatch(true);next.RecordDelivery(2,CricketDeliveryType.Runs);string before=State(next);
        if(replacement)panel.SetMatch(next);else panel.ClearMatch();await Settle(100);
        row.RaiseEvent(RocheScoreboard.Mac.Native.NativeActivation.Release(row));edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(150);
        Assert.Null(Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(before,State(next));Assert.False(Named<Border>(panel,"DeliveryEntryOverlay").IsVisible);
    }

}
