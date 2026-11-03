using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaTheory]
    [InlineData(420,320)] [InlineData(960,420)] [InlineData(1280,850)]
    [Trait("Evidence","WorkflowTrainingLayout")]
    public async Task CurrentTimerAndStartStayReachableWithoutScrollingAndResizeKeepsTheDraft(int width,int height)
    {
        await using var f=await Fixture.Create(width,height);
        f.OtherScope.IsVisible=false;
        var host=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),g=>g.Children.Contains(f.OtherScope));
        host.Height=f.Window.ClientSize.Height;await Settle(180);
        var start=Named<Button>(f.Panel,"StartPauseButton");
        var remaining=Named<TextBlock>(f.Panel,"ActiveRemainingText");
        ActualPoint(f.Window,start);ActualPoint(f.Window,remaining,false);ActualPoint(f.Window,Named<Button>(f.Panel,"ExitButton"));
        Assert.False(Named<Control>(f.Panel,"TrainingSubtitle").IsEffectivelyVisible);
        Assert.False(Named<Control>(f.Panel,"RecentTimersCard").IsEffectivelyVisible);
        Assert.DoesNotContain("Total:",Named<TextBlock>(f.Panel,"SessionTotalText").Text ?? "");
        var cloud=Named<CloudRemoteLauncher>(f.Panel,"TrainingCloudLauncher");
        Assert.Contains(Named<Grid>(f.Panel,"TrainingBuilderColumn"),cloud.GetVisualAncestors());
        Assert.DoesNotContain(Named<Grid>(f.Panel,"TrainingHeaderGrid"),cloud.GetVisualAncestors());
        var identity=f.Session.Timers.Select(t=>(t.Id,t.Title,t.Duration)).ToArray();
        var remainingBefore=f.Session.ActiveRemaining;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        var point=ActualPoint(f.Window,start);f.Window.MouseMove(point);f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);await Settle(30);
        Assert.True(f.Session.IsRunning);f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);await Settle(30);Assert.False(f.Session.IsRunning);
        // Starting legitimately consumes time; preserve identity/durations and
        // reject resets/skips rather than requiring the countdown to stand still.
        Assert.Equal(identity,f.Session.Timers.Select(t=>(t.Id,t.Title,t.Duration)).ToArray());
        Assert.InRange((remainingBefore-f.Session.ActiveRemaining).Ticks,0,elapsed.Elapsed.Ticks+TimeSpan.FromMilliseconds(20).Ticks);
        Assert.Same(f.Active,f.Session.Active);Assert.Equal(0,f.Exits);
        string before=Values(f.Session.Timers);
        var title=Named<TextBox>(f.Panel,"NewTitleBox");title.Text="Keep this timer draft";
        f.Window.Width=width<1000?1280:420;f.Window.Height=width<1000?850:320;host.Height=f.Window.ClientSize.Height;await Settle(180);
        Assert.Equal("Keep this timer draft",title.Text);Assert.Equal(before,Values(f.Session.Timers));
    }
}
