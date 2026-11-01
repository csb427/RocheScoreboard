using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class UpdatePromptLifetimeTests
{
    private static void AssertDecisionFrame(Fixture f)
    {
        foreach(string name in new[]{"DialogHeading","CloseButton","ViewReleaseButton","SkipButton","UpdateButton"})
        {
            var control=f.Named<Control>(name);var point=control.TranslatePoint(default,f.Dialog);Assert.NotNull(point);
            Assert.True(new Rect(f.Dialog.ClientSize).Contains(new Rect(point.Value,control.Bounds.Size)),
                name+" must remain within the same visible client frame, without revealing another control first.");
        }
    }
    [AvaloniaTheory]
    [InlineData(420,320)] [InlineData(680,400)]
    [Trait("Evidence","UpdateDialogCompact")]
    public async Task ShortOwnedUpdateDialogKeepsCurrentStatusAndSafeActionsReachable(int width,int height)
    {
        using var f=new Fixture(width,height);await Task.Delay(100);Dispatcher.UIThread.RunJobs();
        Record(f,"compact-open-"+width,Snapshot(f));
        Assert.InRange(f.Dialog.Bounds.Width,1,width);Assert.InRange(f.Dialog.Bounds.Height,1,height);
        Assert.Same(f.Owner,f.Dialog.Owner);Assert.Same(f.Named<Button>("SkipButton"),f.Dialog.FocusManager!.GetFocusedElement());
        Assert.Equal(0,f.Op.Calls);AssertDecisionFrame(f);
        await Begin(f);
        f.Op.Report("Downloading the selected scoreboard package — a deliberately long current status that must wrap and remain accessible.");
        await Until(()=>f.Named<TextBlock>("ProgressText").Text!.StartsWith("Downloading the selected"),"Current status must render through the actual public progress callback.");
        var progress=f.Named<TextBlock>("ProgressText");progress.BringIntoView();await Task.Delay(50);Dispatcher.UIThread.RunJobs();
        var origin=progress.TranslatePoint(default,f.Dialog);Assert.NotNull(origin);
        Assert.True(new Rect(f.Dialog.ClientSize).Contains(new Rect(origin.Value,progress.Bounds.Size)),"Wrapped current status must fit after ordinary reveal.");
        Assert.Contains("Downloading the selected",AutomationProperties.GetHelpText(f.Named<ProgressBar>("UpdateProgressBar")));
        Record(f,"compact-busy-"+width,Snapshot(f));AssertDecisionFrame(f);
        await Pointer(f,"SkipButton");Assert.True(f.Op.Token.IsCancellationRequested);Assert.False(f.Dialog.UpdateApplied);Assert.True(f.Dialog.IsVisible);
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Acknowledged cancellation remains recoverable.");
        Assert.Equal("Download cancelled",f.Named<TextBlock>("ProgressText").Text);Assert.Equal(1,f.Op.Calls);
        await Pointer(f,"SkipButton");await Until(()=>!f.Dialog.IsVisible,"Real safe dismiss must close without another operation.");
        Assert.Equal(1,f.Closed);Assert.Equal(1,f.Op.Calls);
    }
}
