using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    private static async Task OpenExitReview(Fixture f)
    {
        await TabTo(f.Window,Named<Button>(f.Panel,"ExitButton"));Key(f.Window,PhysicalKey.Space);await Settle(50);
        Assert.True(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        Assert.True(ExitReviewCommand(f.Panel,"training-exit-cancel").IsFocused);Assert.Equal(0,f.Exits);
    }
    [AvaloniaTheory][InlineData("window")][InlineData("ancestor")]
    public async Task ExitHideRetiresItsOwnReceiptAndOldCommandsCannotCloseTheNewReview(string boundary)
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);await OpenExitReview(f);
        var held=ExitReviewCommand(f.Panel,"training-exit-confirm");var exit=Named<Button>(f.Panel,"ExitButton");
        var host=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),g=>g.Children.Contains(f.Panel)&&g.Children.Contains(f.OtherScope));
        if(boundary=="window")f.Window.Hide();else host.IsVisible=false;await Settle(60);
        Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
        if(boundary=="window")f.Window.Show();else host.IsVisible=true;await Settle(80);Assert.True(f.OtherScope.Focus());
        held.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(f.OtherScope.IsFocused);Assert.Equal(0,f.Exits);
        await OpenExitReview(f);var current=ExitReviewCommand(f.Panel,"training-exit-confirm");Assert.NotSame(held,current);
        held.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Same(current,ExitReviewCommand(f.Panel,"training-exit-confirm"));
        Assert.True(ExitReviewCommand(f.Panel,"training-exit-cancel").IsFocused);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
        Key(f.Window,PhysicalKey.Escape);Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
    }
    [AvaloniaFact]
    public async Task CancellingExitKeepsTheActualRunningTransportAndQueueWhileLiveStatusRemainsTruthful()
    {
        await using var f=await Fixture.Create();await TabTo(f.Window,Named<Button>(f.Panel,"StartPauseButton"));Key(f.Window,PhysicalKey.Space);
        Assert.True(f.Session.IsRunning);string identity=ClearQueueIdentity(f.Session);int changes=0;f.Session.StructuralChanged+=()=>changes++;
        await OpenExitReview(f);var before=f.Session.ActiveRemaining;await Settle(100);
        Assert.True(f.Session.IsRunning);Assert.True(f.Session.ActiveRemaining<before);Assert.Equal(identity,ClearQueueIdentity(f.Session));Assert.Equal(0,changes);
        Assert.StartsWith("Timer running",Named<TextBlock>(f.Panel,"TrainingExitReviewStatus").Text);
        Key(f.Window,PhysicalKey.Enter);Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        Assert.True(f.Session.IsRunning);Assert.Equal(identity,ClearQueueIdentity(f.Session));Assert.Equal(0,changes);Assert.Equal(0,f.Exits);
        await OpenExitReview(f);
        // A public model Pause represents an external transport change, not a
        // click through the modal. Status follows it without losing this receipt.
        f.Session.Pause();await Settle(50);
        Assert.StartsWith("Timer paused",Named<TextBlock>(f.Panel,"TrainingExitReviewStatus").Text);
        Assert.True(ExitReviewCommand(f.Panel,"training-exit-cancel").IsFocused);
        Assert.True(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);Key(f.Window,PhysicalKey.Escape);
        Assert.Equal(0,f.Exits);Assert.Equal(0,changes);Assert.Equal(identity,ClearQueueIdentity(f.Session));
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task ExitStatusDistinguishesFinishedIntervalHoldFromAnExhaustedSession(bool exhausted)
    {
        await using var f=await Fixture.Create(onlyActive:true);
        if(exhausted)f.Session.Skip();
        else
        {
            // Existing public model supports a zero interval (also covered by
            // output lifecycle tests). Tick witnesses its real finished hold;
            // no private flag/index/event handler is assigned or invoked.
            f.Active.Duration=TimeSpan.Zero;f.Active.Remaining=TimeSpan.Zero;f.Session.Start();f.Session.Tick();
            Assert.True(f.Active.HasFinished);Assert.False(f.Session.IsRunning);Assert.False(f.Session.IsExhausted);Assert.Same(f.Active,f.Session.Active);
        }
        Assert.Equal(exhausted,f.Session.IsExhausted);string before=ClearFullState(f.Session);await OpenExitReview(f);
        Assert.StartsWith(exhausted?"Session complete":"Interval complete",Named<TextBlock>(f.Panel,"TrainingExitReviewStatus").Text);
        Key(f.Window,PhysicalKey.Escape);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
    }
    [AvaloniaFact]
    public async Task ExitReviewPreviewLeavesNativeAltOwnerChordsUnconsumed()
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);await OpenExitReview(f);
        foreach(var chord in new[]{(Avalonia.Input.Key.F4,PhysicalKey.F4),(Avalonia.Input.Key.Space,PhysicalKey.Space)})
        {
            var args=new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=chord.Item1,PhysicalKey=chord.Item2,KeyModifiers=KeyModifiers.Alt};
            try{f.Panel.RaiseEvent(args);Assert.False(args.Handled);Assert.True(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);}
            finally{f.Panel.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=chord.Item1,PhysicalKey=chord.Item2,KeyModifiers=KeyModifiers.None});}
        }
        Key(f.Window,PhysicalKey.Escape);Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        // Public routed-key ownership only, not an OS AltF4/system-menu test.
    }
}
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaFact]
    public async Task ActualRootExitClearAndCommandBarRemainMutuallyExclusiveInBothOrders()
    {
        await using var scope=new StorageScope(true,null);var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        string before=TrainingRootClearState(session),afl=JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState());int structural=0,exits=0;
        session.StructuralChanged+=()=>structural++;panel.ExitRequested+=()=>exits++;
        var exit=Named<Button>(panel,"ExitButton");var clear=Named<Button>(panel,"ClearAllButton");var command=Named<Grid>(main,"CommandOverlay");
        var provider=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(exit));
        await TrainingRootClearTabTo(main,exit);TrainingRootClearKey(main,PhysicalKey.Space);await Pump(60);
        var original=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Tag,"training-exit-confirm"));
        Assert.True(panel.HasActiveExitDecision);Assert.False(panel.HasActiveClearDecision);Assert.False(main.CanOpenTrainingExitReview);Assert.False(main.CanOpenTrainingClearReview);
        TrainingRootClearKey(main,PhysicalKey.K,RawInputModifiers.Control);clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(50);
        Assert.False(command.IsVisible);Assert.False(panel.HasActiveClearDecision);Assert.True(panel.HasActiveExitDecision);
        Assert.Same(original,Assert.Single(panel.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Tag,"training-exit-confirm")));
        TrainingRootClearKey(main,PhysicalKey.Escape);await Pump(50);
        await TrainingRootClearOpenReview(main,panel,session);exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(50);
        Assert.True(panel.HasActiveClearDecision);Assert.False(panel.HasActiveExitDecision);Assert.False(command.IsVisible);
        TrainingRootClearKey(main,PhysicalKey.Escape);TrainingRootClearKey(main,PhysicalKey.K,RawInputModifiers.Control);
        var search=Named<TextBox>(main,"CommandSearch");await Until(()=>command.IsEffectivelyVisible&&search.IsFocused,"Actual CtrlK must open after decision Cancel.");
        Assert.False(main.CanOpenTrainingExitReview);Assert.False(exit.IsEffectivelyEnabled);
        var error=Record.Exception(()=>provider.Invoke());Assert.True(error is null||error.GetType().Name=="ElementNotEnabledException",error?.ToString());
        // Controlled queued current handler beyond normal disabled-provider refusal.
        exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump(70);
        Assert.True(command.IsEffectivelyVisible);Assert.True(search.IsFocused);Assert.False(panel.HasActiveExitDecision);Assert.False(panel.HasActiveClearDecision);
        Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(afl,JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState()));Assert.Equal(0,structural);Assert.Equal(0,exits);
        TrainingRootClearKey(main,PhysicalKey.Escape);await TrainingRootClearTabTo(main,exit);TrainingRootClearKey(main,PhysicalKey.Space);await Pump(50);
        Assert.True(panel.HasActiveExitDecision);TrainingRootClearKey(main,PhysicalKey.Escape);Assert.False(panel.HasActiveExitDecision);
        Assert.Equal(before,TrainingRootClearState(session));Assert.Equal(0,structural);Assert.Equal(0,exits);scope.AssertOwnedPaths();
    }
}
