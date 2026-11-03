using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
internal static class WorkflowReviewCapture
{
    internal static void Save(Window window,string name)
    {
        string path=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? throw new InvalidOperationException("Explicit capture directory required.");
        Directory.CreateDirectory(path);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(path,name+".png"));
    }
}
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","WorkflowReview")]
    public async Task CaptureCurrentCricketScoringAndOutputWorkflows(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,Roche_Scoreboard.Models.CricketDeliveryType.Four);match.RecordDelivery(1,Roche_Scoreboard.Models.CricketDeliveryType.Runs);
        var panel=new CricketControlPanel();panel.SetMatch(match);using var host=new CricketOverlayHost(match){MotionEnabled=false};panel.SetOverlayHost(host);
        var window=scope.Window(panel,width,height);await Settle(600);string state=State(match);
        WorkflowReviewCapture.Save(window,$"01-cricket-scoring-{width}");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();await Settle(350);
        Named<ScrollViewer>(panel,"CricketOutputPageViewport").ScrollToHome();Named<ScrollViewer>(panel,"CricketPresentationViewport").ScrollToHome();await Settle(100);
        WorkflowReviewCapture.Save(window,$"02-cricket-output-{width}");
        var extra=Named<Button>(panel,"CricketExtrasOverlayButton");await Reveal(window,extra);await StableReachable(window,extra);
        WorkflowReviewCapture.Save(window,$"03-cricket-overlays-{width}");Assert.Equal(state,State(match));
    }
}
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","WorkflowReview")]
    public async Task CaptureCurrentAflOperatorWorkflow(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        await Tap(f.Window,Named<TabItem>(f.Window,"MatchControlTab"));await Settle(350);
        WorkflowReviewCapture.Save(f.Window,$"04-afl-tools-{width}");
        await Tap(f.Window,Named<Button>(f.Window,"GuidedModeButton"));await Settle(250);
        WorkflowReviewCapture.Save(f.Window,$"05-afl-essentials-{width}");Assert.Equal(6,f.Match.HomeTotal);Assert.Equal(1,f.Match.AwayTotal);
    }
}
public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","WorkflowReview")]
    public async Task CaptureCurrentTrainingOperatorWorkflow(int width,int height)
    {
        await using var f=await Fixture.Create(width,height);f.OtherScope.IsVisible=false;
        var parent=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),grid=>grid.Children.Contains(f.Panel)&&grid.Children.Contains(f.OtherScope));parent.Height=f.Window.ClientSize.Height;
        await Settle(350);WorkflowReviewCapture.Save(f.Window,$"06-training-{width}");Assert.Equal(4,f.Session.Timers.Count);
    }
}
