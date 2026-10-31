using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    private static async Task<CloudRemoteLauncher> OpenTrainingCloudDisclosure(Fixture f)
    {
        var launcher=Named<CloudRemoteLauncher>(f.Panel,"TrainingCloudLauncher");launcher.ApplyState(true,true,"ABC123","https://remote.example.invalid/join/ABC123");launcher.ApplyConnection(true,true);
        await TabTo(f.Window,Named<Button>(launcher,"CollapsedButton"));Key(f.Window,PhysicalKey.Space);await Settle(120);
        Assert.True(launcher.IsExpanded);return launcher;
    }
    [AvaloniaTheory][InlineData("clear")][InlineData("exit")]
    public async Task ANewOwnedTrainingDecisionImmediatelyRetiresCloudDisclosureAndItsPendingFocus(string kind)
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);var launcher=await OpenTrainingCloudDisclosure(f);
        int copies=0;launcher.CopyJoinCodeRequested+=(_,e)=>{copies++;Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(e).Succeeded=true;};
        var heldCopy=Named<Button>(launcher,"CopyCodeButton");
        var action=Named<Button>(f.Panel,kind=="clear"?"ClearAllButton":"ExitButton");
        // Controlled queued original action while a popup owns focus. This is
        // explicitly not a physical click through a popup or disabled overlay.
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(50);
        var review=Named<Border>(f.Panel,kind=="clear"?"TrainingClearReviewOverlay":"TrainingExitReviewOverlay");Assert.True(review.IsVisible);
        Assert.False(launcher.IsExpanded); // First missing-handoff oracle, no proposed API lookup.
        Assert.False(launcher.IsDetailsAnimating);
        var cancel=kind=="clear"?ClearReviewAction(f.Panel,"training-clear-cancel"):ExitReviewCommand(f.Panel,"training-exit-cancel");
        Assert.True(cancel.IsFocused);heldCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(650);
        Assert.Equal(0,copies);Assert.True(cancel.IsFocused);Assert.True(review.IsVisible);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
        Key(f.Window,PhysicalKey.Escape);Assert.False(review.IsVisible);Assert.Equal(0,f.Exits);Assert.Equal(before,ClearFullState(f.Session));
        await TabTo(f.Window,Named<Button>(launcher,"CollapsedButton"));Key(f.Window,PhysicalKey.Space);await Settle(80);
        Assert.True(launcher.IsExpanded);Key(f.Window,PhysicalKey.Escape);await Settle(80);Assert.False(launcher.IsExpanded);
        Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);Assert.Equal(0,f.Exits);Assert.Equal(before,ClearFullState(f.Session));
    }
    [AvaloniaTheory]
    [InlineData("clear","rebind")][InlineData("exit","rebind")]
    [InlineData("clear","hide")][InlineData("exit","hide")]
    [InlineData("clear","same-rebind")][InlineData("exit","same-rebind")]
    public async Task PopupClosureReentryCannotOpenAReviewForTheOldSessionOrHiddenOwner(string kind,string boundary)
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);var launcher=await OpenTrainingCloudDisclosure(f);
        var popup=Named<Avalonia.Controls.Primitives.Popup>(launcher,"DetailsPopup");
        var replacement=new Roche_Scoreboard.Models.TrainingSession();replacement.AddTimer(new Roche_Scoreboard.Models.TrainingTimer("Closure replacement",TimeSpan.FromMinutes(2)));
        string replacementBefore=ClearFullState(replacement);int closes=0;
        EventHandler<EventArgs> closed=(_,_)=>
        {
            closes++;
            if(boundary=="hide")f.Window.Hide();
            else{f.Panel.Bind(boundary=="same-rebind"?f.Session:replacement);Assert.True(f.OtherScope.Focus());}
        };
        popup.Closed+=closed;
        try
        {
            // Real popup Closed event injects a synchronous public rebind/hide;
            // no private UI handler or future receipt flag is assigned.
            Named<Button>(f.Panel,kind=="clear"?"ClearAllButton":"ExitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(100);
            Assert.Equal(1,closes);Assert.False(launcher.IsExpanded);Assert.False(f.Panel.HasActiveClearDecision);Assert.False(f.Panel.HasActiveExitDecision);
            Assert.False(Named<Border>(f.Panel,"TrainingClearReviewOverlay").IsVisible);Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
            Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(replacementBefore,ClearFullState(replacement));Assert.Equal(0,f.Exits);
            if(boundary=="hide")Assert.False(f.Window.IsVisible);else Assert.True(f.OtherScope.IsFocused);
        }
        finally{popup.Closed-=closed;}
    }
    [AvaloniaTheory][InlineData(PhysicalKey.Space)][InlineData(PhysicalKey.Enter)]
    public async Task RealTrainingCloudCopyKeysBelongToTheDisclosureAndDoNotOperateTheTimer(PhysicalKey key)
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);var launcher=await OpenTrainingCloudDisclosure(f);
        int copies=0,ends=0;launcher.CopyJoinCodeRequested+=(_,e)=>{var request=Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(e);Assert.Equal("ABC123",request.Value);request.Succeeded=true;copies++;};
        launcher.EndRequested+=(_,_)=>ends++;
        var copy=Named<Button>(launcher,"CopyCodeButton");await TabTo(f.Window,copy);Key(f.Window,key);await Settle(70);
        Assert.Equal(1,copies);Assert.Equal(0,ends);Assert.Equal(0,f.Exits);Assert.False(f.Session.IsRunning);Assert.Equal(before,ClearFullState(f.Session));
        Assert.True(launcher.IsExpanded);Assert.False(f.Panel.HasActiveExitDecision);Assert.False(f.Panel.HasActiveClearDecision);
        Key(f.Window,PhysicalKey.Escape);await Settle(80);
        Assert.False(launcher.IsExpanded);Assert.True(Named<Button>(launcher,"CollapsedButton").IsFocused);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);Assert.False(f.Panel.HasActiveExitDecision);
    }
}
