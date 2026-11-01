using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850,true)] [InlineData(420,320,false)]
    public async Task LineupWorkspaceKeepsBothTeamDraftsAndRolesUntilExplicitSave(int width,int height,bool save)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);string initial=State(match);
        var panel=new Roche_Scoreboard.Views.CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(650);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        var options=Named<Button>(panel,"MatchOptionsButton");await Reveal(window,options);await StableReachable(window,options);TapNow(window,options);await Settle(650);
        var teams=PromptAction(PromptSheet(panel),"teams");await Reveal(window,teams);await StableReachable(window,teams);TapNow(window,teams);await Settle(650);
        var sheet=PromptSheet(panel);
        async Task Reach(Control target)
        {
            await Reveal(window,target);
            try { await StableReachable(window,target); }
            catch
            {
                var box=Painted(window,target);
                var points=new[]{box.Center,new Avalonia.Point(box.Left+4,box.Center.Y),new Avalonia.Point(box.Right-4,box.Center.Y),new Avalonia.Point(box.Center.X,box.Top+4),new Avalonia.Point(box.Center.X,box.Bottom-4)};
                Record($"lineup-hit-failure-{width}",new{target.Tag,target.Name,box,points=points.Select(point=>new{point,hit=(window.InputHitTest(point)as Control)?.Name,type=window.InputHitTest(point)?.GetType().Name,owned=Within(target,window.InputHitTest(point))}),ancestors=target.GetVisualAncestors().OfType<Control>().Select(x=>new{x.Name,x.Tag,type=x.GetType().Name,x.ClipToBounds,box=Painted(window,x)})});
                PromptCapture(window,$"lineup-hit-failure-{width}");throw;
            }
        }
        TextBox Field(string name)=>Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>ControlAutomationPeer.CreatePeerForElement(x)?.GetName()==name);
        Grid Row(int index)=>Assert.Single(sheet.GetVisualDescendants().OfType<Grid>(),x=>Equals(x.Tag,"lineup-row-"+index));
        Button Tagged(Control root,string tag)=>Assert.Single(root.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,tag));
        await FigureType(window,Field("Home team name"),"Home draft review");
        var first=Row(0);var firstName=Assert.Single(first.Children.OfType<TextBox>(),x=>Grid.GetColumn(x)==1);await FigureType(window,firstName,"Home player");
        if(save)
        {
            var homeField=Field("Home team name");int caret=firstName.SelectionStart;
            window.Width=420;window.Height=320;await Settle(150);
            Assert.Same(first,Row(0));Assert.Same(homeField,Field("Home team name"));Assert.Equal("Home player",firstName.Text);Assert.Equal(caret,firstName.SelectionStart);
            Assert.Equal(1,Grid.GetRow(Assert.IsType<Avalonia.Controls.Primitives.UniformGrid>(Tagged(first,"lineup-keeper").GetVisualParent())));
            window.Width=width;window.Height=height;await Settle(150);
            Assert.Same(first,Row(0));Assert.Equal(caret,firstName.SelectionStart);Assert.Equal("Home draft review",homeField.Text);
        }
        var insertion=Assert.Single(first.Children.OfType<Border>(),x=>Equals(x.Tag,"lineup-insertion-preview"));
        Assert.Equal(2,Grid.GetRowSpan(insertion)); // drop marker must follow the complete two-row card
        var keeper=Tagged(first,"lineup-keeper");await Reach(keeper);TapNow(window,keeper);await Settle(100);
        Assert.Equal("WK ✓",keeper.Content);Assert.Contains("Home player",ControlAutomationPeer.CreatePeerForElement(keeper)?.GetName());
        Assert.True(keeper.Bounds.Width>=42&&keeper.Bounds.Height>=42);
        var roleGroup=Assert.IsType<Avalonia.Controls.Primitives.UniformGrid>(keeper.GetVisualParent());
        Assert.Equal(width<500?1:0,Grid.GetRow(roleGroup));
        var captain=Tagged(Row(1),"lineup-captain");
        if(!Equals(captain.Content,"C ✓")){await Reach(captain);TapNow(window,captain);}
        Assert.Equal("C ✓",captain.Content);Assert.Equal("C",Tagged(first,"lineup-captain").Content);
        PromptCapture(window,$"lineup-role-{width}");Assert.Equal(initial,State(match));
        if(save)
        {
            var handle=Tagged(first,"lineup-drag");await Reach(handle);
            KeyNow(window,PhysicalKey.ArrowDown,RawInputModifiers.Alt);await Settle(120);
            var moved=Row(1);Assert.Contains(moved.Children.OfType<TextBox>(),x=>x.Text=="Home player");
            Assert.Equal("WK ✓",Tagged(moved,"lineup-keeper").Content);
            Assert.Same(Tagged(moved,"lineup-drag"),window.FocusManager?.GetFocusedElement());
        }
        var away=Tagged(sheet,"edit-team-away");await Reach(away);TapNow(window,away);await Settle(100);
        await FigureType(window,Field("Away team name"),"Away draft review");Assert.Equal(initial,State(match));
        var home=Tagged(sheet,"edit-team-home");await Reach(home);TapNow(window,home);await Settle(100);
        Assert.Equal("Home draft review",Field("Home team name").Text);
        Assert.True(Assert.Single(home.GetVisualDescendants().OfType<TextBlock>(),x=>x.Name=="ScorecardTeamSelected").IsVisible);
        if(!save)
        {
            var add=Assert.Single(sheet.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"+ Add player"));
            await Reach(add);TapNow(window,add);await Settle(150);
            var added=Row(11);var addedName=Assert.Single(added.Children.OfType<TextBox>(),x=>Grid.GetColumn(x)==1);
            Assert.Equal("Player",addedName.Text);Assert.Same(addedName,window.FocusManager?.GetFocusedElement());
            Assert.Equal(initial,State(match));
        }
        var finish=PromptAction(sheet,save?"save":"close");await Reach(finish);PromptCapture(window,$"lineup-finish-{width}");TapNow(window,finish);await PromptRetired(panel,sheet);await Settle(250);
        if(save)
        {
            Assert.Equal("Home draft review",match.TeamAName);Assert.Equal("Away draft review",match.TeamBName);
            var player=Assert.Single(match.GetLineup(true),p=>p.FirstName=="Home player");Assert.True(player.IsWicketkeeper);Assert.False(player.IsCaptain);Assert.Equal(2,player.BattingPosition);
            Assert.Single(match.GetLineup(true),p=>p.IsCaptain);
        }
        else Assert.Equal(initial,State(match));
        Assert.Equal(0,match.TotalRuns);Assert.Equal(0,match.CurrentInnings!.LegalBallsBowled);
    }
}
