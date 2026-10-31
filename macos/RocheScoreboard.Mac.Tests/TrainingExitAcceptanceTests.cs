// OUTSIDE ONLY: two supplementary post-application cases; UNCOMPILED/UNRUN.
// Root's already-integrated two genuine baselines remain untouched.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    private static Button ExitReviewCommand(TrainingControlPanel panel,string tag)=>
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Tag,tag));

    [AvaloniaFact]
    public async Task ExitReceiptCannotAcceptOrCloseANewerReviewAndCurrentAcceptRunsOnce()
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);
        var exit=Named<Button>(f.Panel,"ExitButton");await TabTo(f.Window,exit);Key(f.Window,PhysicalKey.Space);
        Assert.Equal(0,f.Exits);Assert.Equal(before,ClearFullState(f.Session));await Settle(60);
        var oldConfirm=ExitReviewCommand(f.Panel,"training-exit-confirm");
        var replacement=new TrainingSession();replacement.AddTimer(new TrainingTimer("Reviewed replacement",TimeSpan.FromMinutes(2)));
        string replacementBefore=ClearFullState(replacement);
        f.Panel.Bind(replacement);Assert.True(f.OtherScope.Focus());await Settle(60);
        Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        await TabTo(f.Window,exit);Key(f.Window,PhysicalKey.Space);await Settle(60);
        var currentCancel=ExitReviewCommand(f.Panel,"training-exit-cancel");
        var currentConfirm=ExitReviewCommand(f.Panel,"training-exit-confirm");Assert.NotSame(oldConfirm,currentConfirm);
        oldConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0,f.Exits);Assert.True(currentCancel.IsFocused);
        Assert.True(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsEffectivelyVisible);
        Assert.Same(currentConfirm,ExitReviewCommand(f.Panel,"training-exit-confirm"));
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(replacementBefore,ClearFullState(replacement));
        currentConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        currentConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1,f.Exits);Assert.False(replacement.IsRunning);
        Assert.Equal(replacementBefore,ClearFullState(replacement));Assert.Equal(before,ClearFullState(f.Session));
        Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
        // Empty queue has no discarded timer/correction. Existing builders are
        // retained across Bind, so an extra question has no data-loss purpose.
        var empty=new TrainingSession();f.Panel.Bind(empty);
        var title=Named<TextBox>(f.Panel,"NewTitleBox");title.Text="Unsubmitted retained builder";
        await TabTo(f.Window,exit);Key(f.Window,PhysicalKey.Space);await Settle(60);
        Assert.Equal(2,f.Exits);Assert.Empty(empty.Timers);
        Assert.Equal("Unsubmitted retained builder",title.Text);
        Assert.False(Named<Border>(f.Panel,"TrainingExitReviewOverlay").IsVisible);
    }
}

public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaFact]
    public async Task AcceptedExitStoresOldBoundsBeforePauseAndCannotClobberChangedObserverReplacement()
    {
        await using var scope=new StorageScope(true,null);
        var(panel,old)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;
        var oldOutput=Read<NativeWindow>(main,"_trainingDisplayWindow");
        oldOutput.Position=new Avalonia.PixelPoint(117,131);oldOutput.Width=420;oldOutput.Height=210;
        await Pump(80);var oldBounds=WindowLayoutStorage.Capture(oldOutput);
        target.TrainingStartPause();Assert.True(old.IsRunning);
        string oldMetadata=ExitQueueMetadata(old);int stoppedNotifications=0;
        TrainingSession? replacement=null;NativeWindow? replacementOutput=null;
        WindowLayoutStorage.WindowLayout? replacementBounds=null;
        TextBox? replacementFocus=null;
        void OnChanged()
        {
            if(old.IsRunning)return;
            stoppedNotifications++;
            if(stoppedNotifications!=1)return;
            Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
            var stored=WindowLayoutStorage.Get(WindowLayoutStorage.TrainingDisplayKey);Assert.NotNull(stored);
            Assert.Equal(oldBounds.Left,stored.Left);Assert.Equal(oldBounds.Top,stored.Top);
            Assert.Equal(oldBounds.Width,stored.Width);Assert.Equal(oldBounds.Height,stored.Height);
            var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");Assert.True(home.IsEffectivelyVisible);
            var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="TrainingLaunchAction");
            // A synchronous observer invokes the actual current public action
            // delegate. This is explicitly a controlled reentry contract, not
            // a physical click during Home's decorative entry-disabled period.
            launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            replacement=Read<TrainingSession>(main,"_trainingSession");Assert.NotSame(old,replacement);
            replacement.AddTimer(new TrainingTimer("Reentrant retained owner",TimeSpan.FromMinutes(4)));
            replacementOutput=Read<NativeWindow>(main,"_trainingDisplayWindow");Assert.NotSame(oldOutput,replacementOutput);
            replacementOutput.Position=new Avalonia.PixelPoint(271,143);replacementOutput.Width=430;replacementOutput.Height=215;
            main.UpdateLayout();replacementOutput.UpdateLayout();
            replacementBounds=WindowLayoutStorage.Capture(replacementOutput);
            WindowLayoutStorage.Set(WindowLayoutStorage.TrainingDisplayKey,replacementBounds);
            replacementFocus=Named<TextBox>(panel,"NewTitleBox");Assert.True(replacementFocus.Focus());
        }
        old.Changed+=OnChanged;
        try
        {
            target.TrainingExit();
            Assert.Equal(1,stoppedNotifications);Assert.False(old.IsRunning);
            Assert.NotNull(replacement);Assert.NotNull(replacementOutput);Assert.NotNull(replacementBounds);
            Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));
            Assert.Same(replacementOutput,Read<NativeWindow>(main,"_trainingDisplayWindow"));
            Assert.True(panel.IsEffectivelyVisible);Assert.True(replacementOutput.IsVisible);Assert.True(replacementFocus!.IsFocused);
            Assert.False(oldOutput.IsVisible);Assert.Null(oldOutput.Content);
            Assert.Equal(oldMetadata,ExitQueueMetadata(old));Assert.Single(replacement.Timers);
            var saved=WindowLayoutStorage.Get(WindowLayoutStorage.TrainingDisplayKey);Assert.NotNull(saved);
            Assert.Equal(replacementBounds.Left,saved.Left);Assert.Equal(replacementBounds.Top,saved.Top);
            Assert.Equal(replacementBounds.Width,saved.Width);Assert.Equal(replacementBounds.Height,saved.Height);
            var banked=old.ActiveRemaining;await Pump(140);
            Assert.Equal(banked,old.ActiveRemaining);Assert.Same(replacement,Read<TrainingSession>(main,"_trainingSession"));
            Assert.True(replacementFocus.IsFocused);scope.AssertOwnedPaths();
        }
        finally{old.Changed-=OnChanged;old.Pause();replacement?.Pause();}
    }
}
