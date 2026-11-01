// Three local-clear review regressions; baseline must witness old immediate data loss before new UI lookup.
// Root owns all integration. Every initial request verifies queue preservation
// through the OLD actual Clear button BEFORE looking up any proposed new part.
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    private static string ClearFullState(TrainingSession session)=>JsonSerializer.Serialize(new
    {
        session.ActiveIndex,session.AutoAdvance,session.IsRunning,ActiveRemaining=session.ActiveRemaining.Ticks,
        Timers=session.Timers.Select(timer=>new{timer.Id,timer.Title,Duration=timer.Duration.Ticks,Remaining=timer.Remaining.Ticks,timer.HasFinished})
    });
    private static string ClearQueueIdentity(TrainingSession session)=>JsonSerializer.Serialize(new
    {
        session.ActiveIndex,session.AutoAdvance,
        Timers=session.Timers.Select(timer=>new{timer.Id,timer.Title,Duration=timer.Duration.Ticks,timer.HasFinished})
    });
    private static Border ClearOverlay(TrainingControlPanel panel)=>Named<Border>(panel,"TrainingClearReviewOverlay");
    private static Button ClearReviewAction(TrainingControlPanel panel,string tag)=>
        Assert.Single(panel.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Tag,tag));
    private static async Task<Border> OpenStoppedClearReview(Fixture f,TrainingSession current)
    {
        var clear=Named<Button>(f.Panel,"ClearAllButton");
        await TabTo(f.Window,clear);
        string before=ClearFullState(current);
        Key(f.Window,PhysicalKey.Space); // Real original native Clear button.
        // FIRST acceptance oracle. Old source immediately clears the queue,
        // so baseline failure is real data loss, not a missing-overlay lookup.
        Assert.Equal(before,ClearFullState(current));
        await Settle(40);
        var overlay=ClearOverlay(f.Panel);Assert.True(overlay.IsEffectivelyVisible);
        var cancel=ClearReviewAction(f.Panel,"training-clear-cancel");
        Assert.True(cancel.IsDefault);Assert.True(cancel.IsFocused);
        Assert.False(ClearReviewAction(f.Panel,"training-clear-confirm").IsDefault);
        return overlay;
    }

    [AvaloniaFact]
    public async Task LocalClearCancelPreservesPendingDraftAndDoesNotPauseARunningSession()
    {
        await using var f=await Fixture.Create();await f.OpenDraft();
        var initialDraft=ReadDraft(f.Panel);string before=ClearFullState(f.Session);
        int structural=0,exits=f.Exits;f.Session.StructuralChanged+=()=>structural++;
        var clear=Named<Button>(f.Panel,"ClearAllButton");
        await TabTo(f.Window,clear);
        // Navigation can legitimately change a textbox selection. Capture the
        // exact request boundary while retaining all original text assertions.
        var draft=ReadDraft(f.Panel);
        Assert.Equal(initialDraft.RawTitle,draft.RawTitle);Assert.Equal(initialDraft.RawMinutes,draft.RawMinutes);Assert.Equal(initialDraft.RawSeconds,draft.RawSeconds);
        await OpenStoppedClearReview(f,f.Session);
        Assert.Equal(0,structural);Assert.Equal(before,ClearFullState(f.Session));
        Key(f.Window,PhysicalKey.Escape);await Settle(80);
        Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(exits,f.Exits);
        AssertDraft(f.Panel,draft);Assert.True(clear.IsFocused);

        // Same operator transport remains immediate outside the decision.
        var start=Named<Button>(f.Panel,"StartPauseButton");await TabTo(f.Window,start);
        Key(f.Window,PhysicalKey.Space);Assert.True(f.Session.IsRunning);await Settle(100);
        string identity=ClearQueueIdentity(f.Session);var remaining=f.Session.ActiveRemaining;
        await TabTo(f.Window,clear);var runningDraft=ReadDraft(f.Panel);
        Assert.Equal(initialDraft.RawTitle,runningDraft.RawTitle);Assert.Equal(initialDraft.RawMinutes,runningDraft.RawMinutes);Assert.Equal(initialDraft.RawSeconds,runningDraft.RawSeconds);
        Key(f.Window,PhysicalKey.Space);
        Assert.True(f.Session.IsRunning);Assert.Equal(identity,ClearQueueIdentity(f.Session));
        await Settle(60);Assert.True(ClearOverlay(f.Panel).IsEffectivelyVisible);
        Key(f.Window,PhysicalKey.Escape);await Settle(150);
        Assert.True(f.Session.IsRunning);Assert.True(f.Session.ActiveRemaining<remaining);
        Assert.Equal(identity,ClearQueueIdentity(f.Session));Assert.Equal(0,structural);Assert.Equal(exits,f.Exits);
        AssertDraft(f.Panel,runningDraft);Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task LocalClearCommitsOnceUnderReentrantBindAndEmptyClearRemainsANoop()
    {
        await using var f=await Fixture.Create();
        var original=f.Session.Timers.ToArray();string values=Values(original);
        var replacement=new TrainingSession();replacement.AddTimer(new TrainingTimer("Replacement queue",TimeSpan.FromMinutes(2)));
        string replacementBefore=ClearFullState(replacement);
        int structural=0,changed=0;
        f.Session.StructuralChanged+=()=>
        {
            structural++;
            if(structural==1){f.Panel.Bind(replacement);Assert.True(f.OtherScope.Focus());}
        };
        f.Session.Changed+=()=>changed++;
        await OpenStoppedClearReview(f,f.Session);
        var confirm=ClearReviewAction(f.Panel,"training-clear-confirm");
        // Controlled repeated routed activation: exercises a retained actual
        // command delegate, not a claim of physical disabled-input delivery.
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(f.Session.Timers);Assert.False(f.Session.IsRunning);Assert.Equal(-1,f.Session.ActiveIndex);
        Assert.Equal(1,structural);Assert.Equal(1,changed);Assert.Equal(values,Values(original));
        Assert.Equal(replacementBefore,ClearFullState(replacement));Assert.True(f.OtherScope.IsFocused);
        await Settle(150);Assert.Equal(replacementBefore,ClearFullState(replacement));Assert.True(f.OtherScope.IsFocused);
        Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);Assert.Equal(0,f.Exits);

        var empty=new TrainingSession();int emptyChanges=0,emptyStructure=0;
        empty.Changed+=()=>emptyChanges++;empty.StructuralChanged+=()=>emptyStructure++;
        f.Panel.Bind(empty);await Settle();
        var clear=Named<Button>(f.Panel,"ClearAllButton");Assert.False(clear.IsEnabled);
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Also guard the current handler, not only paint.
        Assert.Equal(0,emptyChanges);Assert.Equal(0,emptyStructure);Assert.Empty(empty.Timers);
        Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task OldClearReceiptCannotClearOrCloseANewerBoundReviewAndWindowHideRetiresIt()
    {
        await using var f=await Fixture.Create();string original=ClearFullState(f.Session);
        await OpenStoppedClearReview(f,f.Session);
        var stale=ClearReviewAction(f.Panel,"training-clear-confirm");
        var replacement=new TrainingSession();replacement.AddTimer(new TrainingTimer("Current bound drill",TimeSpan.FromSeconds(95)));
        string current=ClearFullState(replacement);
        f.Panel.Bind(replacement);Assert.True(f.OtherScope.Focus());await Settle(80);
        Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);
        Assert.Equal(original,ClearFullState(f.Session));Assert.Equal(current,ClearFullState(replacement));
        await OpenStoppedClearReview(f,replacement);
        var newCancel=ClearReviewAction(f.Panel,"training-clear-cancel");
        var newConfirm=ClearReviewAction(f.Panel,"training-clear-confirm");Assert.NotSame(stale,newConfirm);
        stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(60);
        Assert.True(ClearOverlay(f.Panel).IsEffectivelyVisible);Assert.True(newCancel.IsFocused);
        Assert.Same(newConfirm,ClearReviewAction(f.Panel,"training-clear-confirm"));
        Assert.Equal(original,ClearFullState(f.Session));Assert.Equal(current,ClearFullState(replacement));

        f.Window.Hide();await Settle(60);
        Assert.False(ClearOverlay(f.Panel).IsVisible); // Local overlay must retire, not merely inherit hidden pixels.
        f.Window.Show();await Settle(150);Assert.True(f.OtherScope.Focus());
        newConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(250);
        Assert.False(ClearOverlay(f.Panel).IsVisible);Assert.True(f.OtherScope.IsFocused);
        Assert.Equal(original,ClearFullState(f.Session));Assert.Equal(current,ClearFullState(replacement));Assert.Equal(0,f.Exits);
    }
}
