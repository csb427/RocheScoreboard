// Follow-up coverage for the bounded scorecard page. No model/rule changes or
// private handler invocation; real navigation, focus, scrolling and pointer input.
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
    private static ScrollViewer DockPageScroll(CricketControlPanel panel)=>Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),x=>Equals(x.Tag,"scorecard-rows-scroll"));
    private static Control DockSurface(CricketControlPanel panel)=>Assert.Single(Named<TabControl>(panel,"MainTabControl").GetVisualDescendants().OfType<Control>(),x=>x.Name=="PART_OperatorDock");
    private static Button[] DockFigures(Control root)=>root.GetVisualDescendants().OfType<Button>().Where(x=>ToolTip.GetTip(x)is string tip&&tip.StartsWith("Edit ",StringComparison.Ordinal)).ToArray();
    private static Grid[] DockPlayerRows(CricketControlPanel panel,bool bowling)=>panel.GetVisualDescendants().OfType<Grid>()
        .Where(x=>x.Tag is string tag&&tag.StartsWith(bowling?"scorecard-player-bowling-":"scorecard-player-batting-",StringComparison.Ordinal))
        .OrderBy(x=>int.Parse(((string)x.Tag!).Split('-')[^1],System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    private static void DockInvariant(CricketControlPanel panel,Window window,Rect dock)
    {
        Assert.Equal(dock,Painted(window,DockSurface(panel)));
        Assert.True(new Rect(window.ClientSize).Contains(dock),"The main navigation dock must stay in the actual client viewport.");
        Assert.True(DockSurface(panel).IsEffectivelyVisible);
    }
    private static async Task DockVisitFigures(CricketControlPanel panel,Window window,bool bowling,string key)
    {
        await DockTeamPointer(panel,window,bowling,key+"-team");
        var dock=Painted(window,DockSurface(panel));var scroll=DockPageScroll(panel);var host=Named<ContentControl>(panel,"ScorecardHost");
        var figures=DockFigures(panel);Assert.NotEmpty(figures);
        string[] expected=bowling?["Edit Overs","Edit Maidens","Edit Runs conceded","Edit Bowler wickets","Edit Bowler wides","Edit Bowler no-balls"]:["Edit Runs","Edit Balls","Edit 4s","Edit 6s","Edit Wides","Edit No-balls","Edit Byes","Edit Leg-byes"];
        foreach(string label in expected)Assert.Contains(figures,x=>Equals(ToolTip.GetTip(x),label));
        var rows=DockPlayerRows(panel,bowling);Assert.Equal(11,rows.Length);double furthest=0;var visited=new List<object>();
        try
        {
            foreach(var figure in figures)
            {
                await Reveal(window,figure);using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
                var box=Painted(window,figure);var hit=window.InputHitTest(box.Center);furthest=Math.Max(furthest,scroll.Offset.Y);
                visited.Add(new{label=ToolTip.GetTip(figure),box,owned=Within(figure,hit),hit=(hit as Control)?.Name,scroll.Offset});
                Assert.True(Ready(window,figure));Assert.True(Within(figure,hit));
                Assert.True(Painted(window,host).Contains(box),"A revealed figure must fit its allocated scorecard page.");
                Assert.False(box.Intersects(dock),"A revealed figure must stay above the navigation dock.");DockInvariant(panel,window,dock);
            }
            var notes=panel.GetVisualDescendants().OfType<TextBox>().Where(x=>Equals(x.Tag,"scorecard-fow-entry")).ToArray();
            Assert.Single(notes);
            foreach(var target in notes.Cast<Control>().Concat(panel.GetVisualDescendants().OfType<Button>().Where(x=>Equals(x.Tag,"scorecard-recovery")||Equals(x.Tag,"scorecard-fow-apply"))).ToArray())
            {
                await Reveal(window,target);using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
                var box=Painted(window,target);Assert.True(Ready(window,target));Assert.True(Painted(window,host).Contains(box));Assert.False(box.Intersects(dock));DockInvariant(panel,window,dock);
                visited.Add(new{section=target.Tag,box,scroll.Offset});
            }
            Assert.Contains(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-recovery"));
            Assert.True(furthest>0,"The actual page must scroll to reach later players and figures.");
            var innings=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-innings-1"));
            await Reveal(window,innings);await StableReachable(window,innings);DockInvariant(panel,window,dock);
            Assert.True(scroll.Offset.Y<furthest,"Innings navigation must remain reachable by scrolling back to the page top.");
        }
        finally
        {
            Record(key,new{window.ClientSize,bowling,figureCount=figures.Length,furthest,visited,dock,scroll.Offset,scroll.Viewport,scroll.Extent});
            PromptCapture(window,key);
        }
    }
    [AvaloniaTheory]
    [InlineData(420,320,false)][InlineData(420,320,true)][InlineData(760,560,false)][InlineData(900,700,true)]
    public async Task EveryScorecardFigureFamilyRemainsReachableAboveTheAnchoredDock(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(0,CricketDeliveryType.Dot);match.RecordDelivery(0,CricketDeliveryType.Wicket,"bowled");match.SelectNewBatter(2);
        Assert.Single(match.CreateScorecardDraft(1).FallOfWickets);
        var f=await DockOpen(scope,match,width,height);
        string initial=State(match),draft=FigureDraft(f.Panel);int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            await DockVisitFigures(f.Panel,f.Window,false,$"dock-all-{width}-{height}-{reduced}-batting");
            await DockVisitFigures(f.Panel,f.Window,true,$"dock-all-{width}-{height}-{reduced}-bowling");
            Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
        }
        finally{match.MatchChanged-=Changed;}
    }
    private static async Task DockSelectMain(CricketControlPanel panel,Window window,string name)
    {
        var tab=Named<TabItem>(panel,name);await Reveal(window,tab);await StableReachable(window,tab);TapNow(window,tab);await Settle(650);
        Assert.Same(tab,Named<TabControl>(panel,"MainTabControl").SelectedItem);
    }
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task ResizingAndReturningToScorecardRetainsLongNamesSafeTargetsAndCurrentMatch(bool reduced,bool calm)
    {
        using var scope=new Scope(reduced);if(calm)OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
        var match=CricketReliabilityChecks.CreateMatch(true);string a="The exceptionally long northern district community cricket association first eleven",b="The equally long southern regional university cricket club second eleven";match.SetTeams(a,"NTH",b,"STH");
        var f=await DockOpen(scope,match,900,700);string initial=State(match),draft=FigureDraft(f.Panel);int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            foreach(var size in new[]{new Size(900,700),new Size(420,320),new Size(760,560),new Size(420,320)})
            {
                f.Window.Width=size.Width;f.Window.Height=size.Height;await Settle(650);
                Record($"dock-resize-{reduced}-{calm}-{size.Width}-{size.Height}",new{requested=size,actual=f.Window.ClientSize,host=Painted(f.Window,Named<ContentControl>(f.Panel,"ScorecardHost")),dock=Painted(f.Window,DockSurface(f.Panel)),DockPageScroll(f.Panel).Viewport});
                await DockTeamPointer(f.Panel,f.Window,true,$"dock-resize-team-{reduced}-{calm}-{size.Width}-{size.Height}");
                var button=DockTeam(f.Panel,true);Assert.Contains(b,ToolTip.GetTip(button)?.ToString());Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==b);
                var last=DockFigures(DockPlayerRows(f.Panel,true)[^1])[^1];await Reveal(f.Window,last);await StableReachable(f.Window,last);
                Assert.False(Painted(f.Window,last).Intersects(Painted(f.Window,DockSurface(f.Panel))));
            }
            foreach(string name in new[]{"MatchTab","OutputTab","CricketSettingsTab","ScorecardTab"})await DockSelectMain(f.Panel,f.Window,name);
            await DockTeamPointer(f.Panel,f.Window,false,$"dock-return-team-{reduced}-{calm}");
            Assert.Contains(a,ToolTip.GetTip(DockTeam(f.Panel,false))?.ToString());
            Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            var replacement=CricketReliabilityChecks.CreateMatch(true);string other=State(replacement);f.Panel.SetMatch(replacement);await Settle(650);
            await DockSelectMain(f.Panel,f.Window,"ScorecardTab");await DockTeamPointer(f.Panel,f.Window,true,$"dock-rebound-{reduced}-{calm}");
            Assert.Equal(initial,State(match));Assert.Equal(other,State(replacement));Assert.Equal(0,events);
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task CompactFirstAndLastPlayerFiguresKeepApplyCancelAndInvalidRepairReachable(bool reduced,bool bowling)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,420,320);
        string initial=State(match);int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            foreach(bool last in new[]{false,true})
            {
                await DockTeamPointer(f.Panel,f.Window,bowling,$"dock-modal-team-{reduced}-{bowling}-{last}");
                var rows=DockPlayerRows(f.Panel,bowling);var row=last?rows[^1]:rows[0];var figure=DockFigures(row)[0];
                await Reveal(f.Window,figure);await StableReachable(f.Window,figure);TapNow(f.Window,figure);await Settle(650);
                var sheet=PromptSheet(f.Panel);var field=FigureField(sheet);string raw=field.Text??"";await FigureType(f.Window,field,last?"-1":raw);
                await FigureSubmit(f.Window,sheet,field,!last);
                if(last)
                {
                    Assert.Contains(sheet,Named<Grid>(f.Panel,"CricketRoot").Children);Assert.Same(field,FigureField(f.Panel));Assert.Equal("-1",field.Text);
                    var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);
                    var cancel=PromptAction(sheet,"back");await Reveal(f.Window,cancel);await StableReachable(f.Window,cancel);
                    PromptCapture(f.Window,$"dock-modal-{reduced}-{bowling}-invalid");TapNow(f.Window,cancel);
                }
                await PromptRetired(f.Panel,sheet);await Settle(250);
                Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);Assert.False(Read<bool>(f.Panel,"_scorecardDirty"));
            }
            Assert.Empty(match.CurrentInnings!.ScorecardChanges);
        }
        finally{match.MatchChanged-=Changed;}
    }

    [AvaloniaFact]
    public async Task ActualFallOfWicketEditorHasRenderedTextAndOwnsItsPointerAfterKeyboardReveal()
    {
        using var scope=new Scope(true);var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(0,CricketDeliveryType.Dot);match.RecordDelivery(0,CricketDeliveryType.Wicket,"bowled");match.SelectNewBatter(2);
        var f=await DockOpen(scope,match,420,320);var field=Assert.Single(f.Panel.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"scorecard-fow-entry"));
        string initial=State(match),draft=FigureDraft(f.Panel),text=field.Text??"";Assert.False(string.IsNullOrWhiteSpace(text));
        f.Window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);
        try{Assert.True(field.Focus(NavigationMethod.Tab));field.BringIntoView();Avalonia.Threading.Dispatcher.UIThread.RunJobs();}
        finally{f.Window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);}
        await Settle(100);using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);
        var box=Painted(f.Window,field);var hit=f.Window.InputHitTest(box.Center);var visuals=field.GetVisualDescendants().Select(x=>new{type=x.GetType().FullName,name=(x as Control)?.Name}).ToArray();
        Record("dock-fow-template-diagnostic",new{f.Window.ClientSize,text=field.Text,field.IsLoaded,field.IsEffectivelyVisible,field.IsEffectivelyEnabled,field.IsHitTestVisible,field.Focusable,field.IsFocused,box,
            hasTemplate=field.Template is not null,visuals,owned=Within(field,hit),hitType=hit?.GetType().FullName,hitName=(hit as Control)?.Name,
            unchangedState=initial==State(match),unchangedDraft=draft==FigureDraft(f.Panel)});
        PromptCapture(f.Window,"dock-fow-template-diagnostic");
        Assert.NotNull(field.Template);Assert.NotEmpty(visuals);Assert.True(Ready(f.Window,field));Assert.True(Within(field,hit));
        Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));Assert.Equal(text,field.Text);
    }
}
