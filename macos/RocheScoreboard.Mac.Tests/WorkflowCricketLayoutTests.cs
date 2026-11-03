using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)] [InlineData(960,600)] [InlineData(960,420)]
    [Trait("Evidence","WorkflowCricketLayout")]
    public async Task CricketCurrentPlayAndOutputRegionsUseAvailableSpaceWithoutReparentingDrafts(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);using var host=new CricketOverlayHost(match){MotionEnabled=false};panel.SetOverlayHost(host);var window=scope.Window(panel,width,height);await Settle(500);string before=State(match);
        if(width==1280)
        {
            var four=Named<Border>(panel,"ScoringCommands").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"4"));
            await StableReachable(window,four);Assert.True(Painted(window,Named<Border>(panel,"BattingZone")).Right<=Painted(window,Named<Border>(panel,"BowlingZone")).Left);
            Assert.True(Painted(window,Named<Border>(panel,"BattingZone")).Bottom<=Painted(window,Named<Border>(panel,"ScoringCommands")).Top);
        }
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();await Settle(250);
        var extras=Named<Button>(panel,"CricketExtrasOverlayButton");var overlays=Named<ScrollViewer>(panel,"CricketOverlaysViewport");var presentations=Named<ScrollViewer>(panel,"CricketPresentationViewport");var weather=Named<ScrollViewer>(panel,"CricketWeatherViewport");
        Assert.Contains(overlays,extras.GetVisualAncestors());Assert.DoesNotContain(presentations,extras.GetVisualAncestors());
        Assert.DoesNotContain(Named<Control>(panel,"CricketPresentationGrid"),Named<Button>(panel,"Presentationwindow").GetVisualAncestors());
        bool stacked=width<900 || height<520;Assert.Equal(stacked?1:0,Grid.GetRow(overlays));Assert.Equal(stacked?2:1,Grid.GetRow(weather));
        await Reveal(window,extras);await StableReachable(window,extras);
        var venue=Named<TextBox>(panel,"CricketWeatherLocation");venue.Text="Unapplied review venue";await Reveal(window,venue);await StableReachable(window,venue);Assert.True(venue.Focus());
        var originalParent=venue.Parent;
        foreach(var next in new[]{new Size(420,320),new Size(1280,850)})
        {
            window.Width=next.Width;window.Height=next.Height;await Settle(180);Assert.Same(originalParent,venue.Parent);Assert.Equal("Unapplied review venue",venue.Text);Assert.True(venue.IsKeyboardFocusWithin);
        }
        Assert.Equal(before,State(match));
    }
}
