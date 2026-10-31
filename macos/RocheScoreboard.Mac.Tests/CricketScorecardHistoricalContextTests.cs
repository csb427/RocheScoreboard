using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static bool DockWholeTextVisible(Window window,TextBlock text)
    {
        if(!text.IsEffectivelyVisible||text.Bounds.Width<=0||text.Bounds.Height<=0)return false;
        var box=Painted(window,text);if(!new Rect(window.ClientSize).Contains(box))return false;
        return text.GetVisualAncestors().OfType<Control>().Where(x=>x.ClipToBounds).All(x=>Painted(window,x).Contains(box));
    }
    [AvaloniaTheory]
    [InlineData(420,320,false,false)][InlineData(420,320,true,true)][InlineData(760,560,false,true)][InlineData(900,700,true,false)]
    public async Task HistoricalLatePlayerKeepsSelectedInningsActualTeamAndRoleVisible(int width,int height,bool reduced,bool bowling)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        match.EndCurrentInnings();Assert.True(match.CanStartNextInnings());match.StartNextInnings();match.SetStriker(0);match.SetNonStriker(1);match.ConfirmBowler(0);Assert.Equal(2,match.CurrentInningsNumber);
        string initial=State(match);int revision=match.DeliveryRevision;
        var f=await DockOpen(scope,match,width,height);var innings=Assert.Single(f.Panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-innings-1"));
        await Reveal(f.Window,innings);await StableReachable(f.Window,innings);TapNow(f.Window,innings);await Settle(650);Assert.Equal(1,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber);
        await DockTeamPointer(f.Panel,f.Window,bowling,$"dock-history-team-{width}-{height}-{reduced}-{bowling}");
        var last=DockFigures(DockPlayerRows(f.Panel,bowling)[^1])[^1];await Reveal(f.Window,last);await StableReachable(f.Window,last);
        string team=bowling?match.AllInnings[0].BowlingTeamName:match.AllInnings[0].BattingTeamName;
        string expected=$"Scorecard · 1st innings · {(bowling?"Bowling":"Batting")} · {team}";
        var visible=f.Panel.GetVisualDescendants().OfType<TextBlock>().Where(x=>DockWholeTextVisible(f.Window,x)).Select(x=>new{x.Text,x.Tag,box=Painted(f.Window,x)}).ToArray();
        Record($"dock-history-context-{width}-{height}-{reduced}-{bowling}",new{f.Window.ClientSize,currentLiveInnings=match.CurrentInningsNumber,viewedInnings=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber,team,bowling,expected,liveHeader=Named<TextBlock>(f.Panel,"InningsLabel").Text,visible,offset=DockPageScroll(f.Panel).Offset,last=Painted(f.Window,last)});PromptCapture(f.Window,$"dock-history-context-{width}-{height}-{reduced}-{bowling}");
        Assert.Contains(visible,x=>x.Text==expected);
        var caption=Assert.Single(f.Panel.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-context"));Assert.Equal(expected,caption.Text);Assert.True(DockWholeTextVisible(f.Window,caption));Assert.True(caption.Bounds.Height<30);
        Assert.False(Painted(f.Window,caption).Intersects(Painted(f.Window,DockSurface(f.Panel))));Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(2,match.CurrentInningsNumber);
    }
}
