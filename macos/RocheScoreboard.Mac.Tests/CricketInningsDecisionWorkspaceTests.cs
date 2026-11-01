using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Roche_Scoreboard.Views;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task InningsDecisionKeepsScoreAndEarlyCloseConfirmation(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(4,CricketDeliveryType.Four);match.RecordDelivery(1,CricketDeliveryType.Wide);
        Assert.Equal(5,match.TotalRuns);Assert.Equal("0.1",match.OversDisplay);string initial=State(match);
        var panel=new Roche_Scoreboard.Views.CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(650);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        var options=Named<Button>(panel,"MatchOptionsButton");await Reveal(window,options);await StableReachable(window,options);TapNow(window,options);await Settle(650);
        var innings=PromptAction(PromptSheet(panel),"innings");await Reveal(window,innings);await StableReachable(window,innings);TapNow(window,innings);await Settle(650);
        var sheet=PromptSheet(panel);Assert.Equal(initial,State(match));
        var context=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"innings-decision-context"));
        var displayed=Assert.Single(context.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"innings-decision-score"));
        Assert.Equal("0/5",displayed.Text);Assert.Equal(Named<TextBlock>(panel,"HeaderScore").Text,displayed.Text);
        Assert.Equal("5 runs, 0 wickets",Avalonia.Automation.AutomationProperties.GetName(displayed));
        Assert.Equal("5 runs, 0 wickets",ToolTip.GetTip(displayed));
        Record($"innings-score-semantics-{width}-{reduced}",new{declaredName=Avalonia.Automation.AutomationProperties.GetName(displayed),nativePeerName=ControlAutomationPeer.CreatePeerForElement(displayed)?.GetName(),displayed.Text});
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==match.BattingTeamName);
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="0.1 overs · In progress");
        var surface=Assert.Single(sheet.GetVisualDescendants().OfType<OperatorGlassSurface>(),x=>Equals(x.Tag,"innings-decision-surface"));
        if(reduced){Assert.Equal(1,Assert.Single(Assert.IsType<TransformGroup>(surface.RenderTransform).Children.OfType<ScaleTransform>()).ScaleX);Assert.Equal(1,surface.Opacity);}
        await StableReachable(window,PromptAction(sheet,"b")); // initial primary target must not need a scroll/reveal
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="19.5 overs available");
        PromptCapture(window,$"innings-open-{width}-{reduced}");
        var next=PromptAction(sheet,"b");
        Assert.Contains(next.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Close this innings early, then choose its opening players.");
        Assert.False(next.IsDefault);
        await Reveal(window,next);await StableReachable(window,next);
        if(reduced)
        {
            KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);
        }
        else
        {
            TapNow(window,next);await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"The previous sheet must detach before the owned early-close confirmation.");await Settle(500);
            var confirmation=PromptSheet(panel);Assert.Contains(confirmation.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Close this innings early?");
            Assert.Equal(initial,State(match));var cancel=PromptAction(confirmation,"back");await Reveal(window,cancel);await StableReachable(window,cancel);PromptCapture(window,$"innings-confirm-{width}");TapNow(window,cancel);await PromptRetired(panel,confirmation);
        }
        await Settle(200);Assert.Equal(initial,State(match));Assert.Equal(1,match.CurrentInningsNumber);
    }
}
