using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalExtrasAndBoundaryButtonsKeepDistinctFactsAndScorebugEligibility(int width,int height)
    {
        using var scope=new Scope(); var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(4,CricketDeliveryType.Four); match.RecordDelivery(2,CricketDeliveryType.Wide);
        using var host=new CricketOverlayHost(match) { MotionEnabled=false };
        var panel=new CricketControlPanel(); panel.SetMatch(match); panel.SetOverlayHost(host); host.AutomaticEnabled=false;
        var window=scope.Window(panel,width,height); var output=scope.Window(host,386,193);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory) Directory.CreateDirectory(directory);
        await Settle(180);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();
        await Settle(180); string state=State(match);
        var extras=Named<Button>(panel,"CricketExtrasOverlayButton"); var boundaries=Named<Button>(panel,"CricketBoundariesOverlayButton");
        Assert.NotSame(extras,boundaries);
        foreach(var entry in new[]{(button:extras,kind:CricketOverlayKind.Extras,caption:"EXTRAS",metric:"WIDES",value:"2",excluded:"FOURS"),
            (button:boundaries,kind:CricketOverlayKind.Boundaries,caption:"INNINGS BOUNDARIES",metric:"FOURS",value:"1",excluded:"WIDES")})
        {
            try { await Reveal(window,entry.button); await StableReachable(window,entry.button); }
            catch
            {
                PromptCapture(window,$"overlay-button-unreachable-{entry.kind}-{width}");
                var box=Painted(window,entry.button);
                Record($"overlay-button-unreachable-{entry.kind}-{width}",new { box,window.ClientSize,
                    probes=new[]{box.Center,new Avalonia.Point(box.Left+4,box.Center.Y),new Avalonia.Point(box.Right-4,box.Center.Y),new Avalonia.Point(box.Center.X,box.Top+4),new Avalonia.Point(box.Center.X,box.Bottom-4)}
                        .Select(p=>new {p,owned=Within(entry.button,window.InputHitTest(p)),hit=(window.InputHitTest(p) as Control)?.Name,type=window.InputHitTest(p)?.GetType().Name}),
                    scrolls=entry.button.GetVisualAncestors().OfType<ScrollViewer>().Select(x=>new {x.Name,x.Offset,x.Viewport,x.Extent,box=Painted(window,x)}) });
                throw;
            }
            Assert.Equal(entry.caption,ControlAutomationPeer.CreatePeerForElement(entry.button)?.GetName());
            Assert.True(Painted(window,entry.button).Height>=47.5);
            PromptCapture(window,$"original-overlay-button-{entry.kind}-{width}");
            if(entry.kind==CricketOverlayKind.Extras) TapNow(window,entry.button);
            else KeyNow(window,PhysicalKey.Enter);
            await Until(()=>host.Status=="ON AIR · "+entry.caption,"Original input must select its own on-air graphic.");
            Assert.Contains(host.Facts.Single(f=>f.Kind==entry.kind).Metrics,m=>m.Label==entry.metric&&m.Value==entry.value);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==entry.metric);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==entry.excluded);
            PromptCapture(output,$"original-overlay-content-{entry.kind}-{width}");
            Assert.Equal(state,State(match)); host.Cancel(); await Until(()=>!host.IsShowing,"Cancel must retire the previous graphic.");
        }
        host.SetPresentation("worm"); await Settle(40);
        Assert.False(extras.IsEnabled); Assert.False(boundaries.IsEnabled); Assert.False(host.IsShowing);
        Assert.False(host.Request(CricketOverlayKind.Extras)); Assert.False(host.Request(CricketOverlayKind.Boundaries));
        host.SetPresentation("scorebug"); await Settle(40);
        Assert.True(extras.IsEnabled); Assert.True(boundaries.IsEnabled); Assert.Equal(state,State(match));
    }
}
