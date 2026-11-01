// Public scorecard navigation and real pointer probes. Source reads only assert
// unchanged state; no private layout/render/team-selection handlers are invoked.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
    private static Button DockTeam(CricketControlPanel panel,bool bowling) => Assert.Single(
        panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,bowling?"scorecard-team-bowling":"scorecard-team-batting"));
    private static Point[] DockEdgePoints(Rect box)=>[box.Center,new(box.Left+4,box.Center.Y),new(box.Right-4,box.Center.Y),new(box.Center.X,box.Top+4),new(box.Center.X,box.Bottom-4)];
    private static async Task<(CricketControlPanel Panel,Window Window)> DockOpen(Scope scope,CricketMatchManager match,int width,int height)
    {
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(650);
        var tab=Named<TabItem>(panel,"ScorecardTab");await Reveal(window,tab);await StableReachable(window,tab);TapNow(window,tab);await Settle(650);
        Assert.Same(tab,Named<TabControl>(panel,"MainTabControl").SelectedItem);return(panel,window);
    }
    private static async Task DockTeamPointer(CricketControlPanel panel,Window window,bool bowling,string capture)
    {
        var button=DockTeam(panel,bowling);await Reveal(window,button);await Settle(100);
        using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
        var box=Painted(window,button);var host=Named<ContentControl>(panel,"ScorecardHost");var hostBox=Painted(window,host);
        var dock=Assert.Single(Named<TabControl>(panel,"MainTabControl").GetVisualDescendants().OfType<Control>(),x=>x.Name=="PART_OperatorDock");
        var dockBox=Painted(window,dock);var points=DockEdgePoints(box);
        var probes=points.Select(point=>new{point,owned=Within(button,window.InputHitTest(point)),hitType=window.InputHitTest(point)?.GetType().Name,hitName=(window.InputHitTest(point)as Control)?.Name}).ToArray();
        // Preserve every real probe and pixels before a failing assertion.
        Record(capture,new{window.ClientSize,bowling,box,hostBox,dockBox,probes,
            scrolls=button.GetVisualAncestors().OfType<ScrollViewer>().Select(x=>new{x.Name,x.Tag,x.Offset,x.Extent,x.Viewport,painted=Painted(window,x)}).ToArray()});
        PromptCapture(window,capture);
        var lower=points[^1];window.MouseMove(lower);window.MouseDown(lower,MouseButton.Left);window.MouseUp(lower,MouseButton.Left);await Settle(650);
        bool selected=ReferenceEquals(Named<TabControl>(panel,"MainTabControl").SelectedItem,Named<TabItem>(panel,"ScorecardTab"));
        bool rows=panel.GetVisualDescendants().OfType<Grid>().Any(x=>Equals(x.Tag,bowling?"scorecard-player-bowling-0":"scorecard-player-batting-0"));
        Record(capture+"-after-pointer",new{selected,rows,window.ClientSize});
        Assert.All(probes,probe=>Assert.True(probe.owned,"Team card interior must own every pointer edge; "+probe.point+" hit "+probe.hitName+":"+probe.hitType));
        Assert.True(hostBox.Contains(box),"The full revealed team hit target must fit the actual selected-content allocation.");
        Assert.False(box.Intersects(dockBox),"Scorecard team target must not overlap the anchored navigation dock.");
        Assert.True(selected,"A team selection must not navigate to another main tab.");Assert.True(rows,"The actual lower-edge pointer click must select the intended team rows.");
    }
    private static async Task DockTeamCase(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,width,height);
        string initial=State(match),draft=FigureDraft(f.Panel);int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            await DockTeamPointer(f.Panel,f.Window,true,$"dock-team-{width}-{height}-{reduced}-bowling");
            await DockTeamPointer(f.Panel,f.Window,false,$"dock-team-{width}-{height}-{reduced}-batting");
            Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task CompactScorecardTeamCardsOwnTheirFullPointerTargetsWithoutDockOverlap(bool reduced)=>await DockTeamCase(420,320,reduced);
    [AvaloniaTheory]
    [InlineData(760,560,false)][InlineData(760,560,true)][InlineData(900,700,false)][InlineData(900,700,true)]
    public async Task StandardScorecardTeamCardsKeepTheirFullPointerTargets(int width,int height,bool reduced)=>await DockTeamCase(width,height,reduced);
}
