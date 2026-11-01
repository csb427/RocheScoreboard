using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaFact]
    public async Task HeldEscapeCancelsClearWithoutDiscardingTheUnderlyingUnsavedCorrection()
    {
        await using var f=await Fixture.Create();await f.OpenDraft();
        await OpenStoppedClearReview(f,f.Session);var fields=CorrectionFields(f.Panel);
        var draft=ReadDraft(f.Panel);string before=ClearFullState(f.Session);
        try
        {
            f.Window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);
            Assert.False(ClearOverlay(f.Panel).IsVisible);Assert.Equal(0,f.Exits);
            Assert.All(fields,field=>Assert.True(field.IsLoaded));AssertDraft(f.Panel,draft);
            // No key release: a genuine OS-style repeated key after the modal
            // has gone must not become a second destructive navigation intent.
            f.Window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);await Settle(60);
            Assert.All(fields,field=>Assert.True(field.IsLoaded,"Holding review Cancel must not discard the underlying correction."));
            AssertDraft(f.Panel,draft);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
        }
        finally{f.Window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);}
        // A deliberately released/new Escape still cancels that correction.
        Key(f.Window,PhysicalKey.Escape);await Settle(60);
        Assert.All(fields,field=>Assert.False(field.IsLoaded));Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
    }

    [AvaloniaFact]
    public async Task HeldClearConfirmEnterCannotAppendATimerAfterFocusReturnsToTheBuilder()
    {
        await using var f=await Fixture.Create();
        Assert.Equal("WARM-UP",Named<TextBox>(f.Panel,"NewTitleBox").Text);
        Assert.Equal("5",Named<TextBox>(f.Panel,"NewMinutesBox").Text);Assert.Equal("0",Named<TextBox>(f.Panel,"NewSecondsBox").Text);
        await OpenStoppedClearReview(f,f.Session);Key(f.Window,PhysicalKey.Tab);
        Assert.True(ClearReviewAction(f.Panel,"training-clear-confirm").IsFocused);
        int changes=0;f.Session.StructuralChanged+=()=>changes++;
        try
        {
            f.Window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);
            Assert.Empty(f.Session.Timers);Assert.Equal(1,changes);Assert.False(ClearOverlay(f.Panel).IsVisible);
            Assert.True(Named<TextBox>(f.Panel,"NewTitleBox").IsFocused);
            f.Window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);await Settle(60);
            Assert.Empty(f.Session.Timers);Assert.Equal(1,changes);Assert.Equal(0,f.Exits);
        }
        finally{f.Window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);}
        // Fresh complete presses remain intentional, immediate Add commands;
        // there is no time-based debounce or blanket suppression of repetitions.
        Key(f.Window,PhysicalKey.Enter);Assert.Single(f.Session.Timers);Assert.Equal(2,changes);
        Key(f.Window,PhysicalKey.Enter);Assert.Equal(2,f.Session.Timers.Count);Assert.Equal(3,changes);
        Assert.All(f.Session.Timers,timer=>{Assert.Equal("WARM-UP",timer.Title);Assert.Equal(TimeSpan.FromMinutes(5),timer.Duration);});
    }
}
