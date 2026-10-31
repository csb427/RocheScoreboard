// Supplementary ancestor visibility and routed window-key ownership regressions.
// Original failing baseline is preserved separately; integrated cases retain
// their business assertions with corrected request-boundary fixture snapshots.
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaFact]
    public async Task TrainingClearAncestorHideRetiresTheReviewAndRejectsAlreadyHiddenRequests()
    {
        await using var f=await Fixture.Create();
        string before=ClearFullState(f.Session);int structural=0;
        f.Session.StructuralChanged+=()=>structural++;
        await OpenStoppedClearReview(f,f.Session);
        var heldConfirm=ClearReviewAction(f.Panel,"training-clear-confirm");
        var clear=Named<Button>(f.Panel,"ClearAllButton");
        var host=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(), grid=>grid.Children.Contains(f.Panel)&&grid.Children.Contains(f.OtherScope));
        Assert.True(f.Window.IsVisible);Assert.True(host.IsVisible);
        Assert.True(f.Panel.IsLoaded);
        host.IsVisible=false;await Settle(60);
        Assert.True(f.Window.IsVisible);Assert.False(host.IsVisible);
        Assert.True(f.Panel.IsLoaded); // Genuine ancestor hide; not unload/Window hide.
        Assert.False(ClearOverlay(f.Panel).IsVisible); // Local ownership must retire.
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);
        // A controlled queued old handler invocation arrives after the only
        // visibility event has already occurred. It must not open a hidden review.
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(ClearOverlay(f.Panel).IsVisible);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);
        host.IsVisible=true;await Settle(100);Assert.True(f.OtherScope.Focus());
        heldConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(80);
        Assert.False(ClearOverlay(f.Panel).IsVisible);Assert.True(f.OtherScope.IsFocused);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(0,f.Exits);
    }

    [AvaloniaFact]
    public async Task TrainingClearPreviewDoesNotConsumeAltOwnerWindowChords()
    {
        await using var f=await Fixture.Create();
        string before=ClearFullState(f.Session);
        await OpenStoppedClearReview(f,f.Session);
        foreach(var chord in new[]{(Avalonia.Input.Key.F4,PhysicalKey.F4),(Avalonia.Input.Key.Space,PhysicalKey.Space)})
        {
            // Controlled public routed-key contract, sent to the real panel:
            // this verifies review preview ownership, NOT an OS system menu or
            // native Alt+F4 desktop-close behavior from a headless executor.
            var args=new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,
                Key=chord.Item1,PhysicalKey=chord.Item2,KeyModifiers=KeyModifiers.Alt };
            try
            {
                f.Panel.RaiseEvent(args);
                Assert.False(args.Handled);
                Assert.True(ClearOverlay(f.Panel).IsEffectivelyVisible);
                Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
            }
            finally
            {
                f.Panel.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyUpEvent,
                    Key=chord.Item1,PhysicalKey=chord.Item2,KeyModifiers=KeyModifiers.None });
            }
        }
        // Ordinary decision Escape remains owned and non-destructive.
        Key(f.Window,PhysicalKey.Escape);await Settle(60);
        Assert.False(ClearOverlay(f.Panel).IsEffectivelyVisible);
        Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
    }
}
