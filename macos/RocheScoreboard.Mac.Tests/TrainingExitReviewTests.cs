// Two next-feature baselines; original OLD control/lifetime oracles precede future-part lookup.
// Reuses existing actual Training and MainWindow fixtures. No private exit handler
// is invoked. Root chooses the final reviewed Exit-part names/tags on integration.
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaFact]
    public async Task LocalExitFirstPreservesThePendingSessionAndEnterDefaultsToCancel()
    {
        await using var f=await Fixture.Create();await f.OpenDraft();
        var initialDraft=ReadDraft(f.Panel);string before=ClearFullState(f.Session);
        var original=f.Session.Timers.ToArray();int structural=0;
        f.Session.StructuralChanged+=()=>structural++;
        var exit=Named<Button>(f.Panel,"ExitButton");await TabTo(f.Window,exit);
        var draft=ReadDraft(f.Panel);
        Assert.Equal(initialDraft.RawTitle,draft.RawTitle);Assert.Equal(initialDraft.RawMinutes,draft.RawMinutes);Assert.Equal(initialDraft.RawSeconds,draft.RawSeconds);
        Assert.True(exit.IsFocused);Key(f.Window,PhysicalKey.Space);
        // FIRST acceptance oracle through the actual OLD Exit control. Old
        // source emits ExitRequested immediately; no future-part lookup yet.
        Assert.Equal(0,f.Exits);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);
        await Settle(60);
        var review=Assert.Single(f.Panel.GetVisualDescendants().OfType<Border>(),
            part=>Equals(part.Tag,"training-exit-review"));
        Assert.True(review.IsEffectivelyVisible);
        var cancel=Assert.Single(review.GetVisualDescendants().OfType<Button>(),
            part=>Equals(part.Tag,"training-exit-cancel"));
        var confirm=Assert.Single(review.GetVisualDescendants().OfType<Button>(),
            part=>Equals(part.Tag,"training-exit-confirm"));
        Assert.True(cancel.IsDefault);Assert.True(cancel.IsFocused);Assert.False(confirm.IsDefault);
        // Controlled retained/repeated activation is a command-delegate check,
        // not a claim of physical input on a review-obscured Exit control.
        exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(f.Panel.GetVisualDescendants().OfType<Border>(),
            part=>Equals(part.Tag,"training-exit-review") && part.IsEffectivelyVisible);
        Assert.Equal(0,f.Exits);Assert.Equal(before,ClearFullState(f.Session));
        Key(f.Window,PhysicalKey.Enter);await Settle(80);
        Assert.False(review.IsEffectivelyVisible);Assert.Equal(0,f.Exits);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);
        Assert.Equal(original,f.Session.Timers);AssertDraft(f.Panel,draft);Assert.True(exit.IsFocused);
    }
}

public sealed partial class BreakRotationModeIsolationTests
{
    private static string ExitQueueMetadata(TrainingSession session)=>JsonSerializer.Serialize(new
    {
        session.ActiveIndex,session.AutoAdvance,
        Timers=session.Timers.Select(timer=>new{timer.Id,timer.Title,Duration=timer.Duration.Ticks,timer.HasFinished})
    });

    [AvaloniaFact]
    public async Task PhoneConfirmedExitImmediatelyTearsDownTheActualOwnerAndStopsItsOldStopwatch()
    {
        await using var scope=new StorageScope(true,null);
        var(panel,session)=await TrainingRootClearOpenTraining(scope);var main=scope.Main!;
        var target=(IRemoteCommandTarget)main;
        session.Clear();
        var active=new TrainingTimer("Running old exit drill",TimeSpan.FromMinutes(3));
        var next=new TrainingTimer("Preserved old queue",TimeSpan.FromMinutes(2));
        session.AddTimer(active);session.AddTimer(next);session.AutoAdvance=false;
        var output=Read<Window?>(main,"_trainingDisplayWindow");Assert.NotNull(output);Assert.True(output.IsVisible);
        Assert.True(panel.IsEffectivelyVisible);
        target.TrainingStartPause();Assert.True(session.IsRunning);await Pump(100);
        string metadata=ExitQueueMetadata(session);var remainingBefore=session.ActiveRemaining;
        int structural=0;session.StructuralChanged+=()=>structural++;
        try
        {
            // This public target represents the existing phone-confirmed exit.
            // It must NOT be converted into the new local desktop review route.
            target.TrainingExit();
            Assert.Null(Read<TrainingSession?>(main,"_trainingSession"));
            Assert.Null(Read<Window?>(main,"_trainingDisplayWindow"));
            Assert.False(output.IsVisible);
            Assert.False(panel.IsEffectivelyVisible);
            Assert.True(Named<SportSelectionScreen>(main,"SportSelectionPanel").IsEffectivelyVisible);
            // FIRST changed-lifetime oracle: old source nulls root authority and
            // stops UI ticking but leaves this exact captured Stopwatch running.
            Assert.False(session.IsRunning);
            Assert.Equal(metadata,ExitQueueMetadata(session));Assert.Equal(0,structural);
            Assert.Equal(new[]{active,next},session.Timers);
            Assert.InRange(session.ActiveRemaining.Ticks,1,remainingBefore.Ticks);
            Assert.Equal(active.Remaining,session.ActiveRemaining);
            var banked=session.ActiveRemaining;await Pump(180);
            Assert.False(session.IsRunning);Assert.Equal(banked,session.ActiveRemaining);
            Assert.Equal(metadata,ExitQueueMetadata(session));Assert.Equal(0,structural);scope.AssertOwnedPaths();
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Border>(),
                part=>Equals(part.Tag,"training-exit-review") && part.IsEffectivelyVisible);
        }
        finally
        {
            // Cleanup AFTER acceptance or failure; never repairs the oracle.
            session.Pause();
        }
    }
}
