using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaFact]
    [Trait("Evidence","CricketPlayerFiguresNavigation")]
    public async Task TheLatestQueuedRequestOwnsPlayerFocusWithinTheSameScorecardView()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,1280,850);string before=State(match);
        var older=f.Panel.BeginPlayerFiguresEdit(false,0);var latest=f.Panel.BeginPlayerFiguresEdit(false,10);
        Assert.False(latest.IsCompleted);await Task.WhenAll(older,latest);await Settle(100);
        var cell=DockFigures(DockPlayerRows(f.Panel,false)[10])[0];Assert.Same(cell,f.Window.FocusManager?.GetFocusedElement());await StableReachable(f.Window,cell);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPlayerFiguresNavigation")]
    public async Task QueuedPlayerFiguresNavigationYieldsToANewerScorecardFocusChoice(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,width,height);string before=State(match);
        // Invoke the actual internal navigation API used by player-menu Edit
        // figures. This is an explicit workflow seam, not a native menu claim.
        var navigation=f.Panel.BeginPlayerFiguresEdit(false,10);Assert.False(navigation.IsCompleted,"Must observe before the real deferred Loaded navigation runs.");
        var newer=DockTeam(f.Panel,true);Assert.True(newer.Focus(NavigationMethod.Tab));Assert.Same(newer,f.Window.FocusManager?.GetFocusedElement());
        await navigation;await Settle(100);Assert.Same(newer,f.Window.FocusManager?.GetFocusedElement());Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPlayerFiguresNavigation")]
    public async Task CurrentPlayerFiguresNavigationStillRevealsAndFocusesTheRequestedPlayersFirstMetric(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,width,height);string before=State(match);
        await f.Panel.BeginPlayerFiguresEdit(true,10);await Settle(100);
        var rows=DockPlayerRows(f.Panel,true);
        Record("requested-bowling-rows-"+width,new{actualBowlingRows=rows.Length,expected=match.CurrentInnings!.BowlingAttack.Count,actualBattingRows=DockPlayerRows(f.Panel,false).Length});
        Assert.Equal(match.CurrentInnings.BowlingAttack.Count,rows.Length);var row=rows[10];var fields=DockFigures(row);Assert.NotEmpty(fields);var field=fields[0];
        Assert.Same(field,f.Window.FocusManager?.GetFocusedElement());await StableReachable(f.Window,field);Assert.Equal(before,State(match));PromptCapture(f.Window,"requested-player-"+width);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketPlayerFiguresNavigation")]
    public async Task LeavingScorecardBeforeQueuedPlayerFocusPreservesTheNewSelectedTab()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var f=await DockOpen(scope,match,1280,850);string before=State(match);
        var navigation=f.Panel.BeginPlayerFiguresEdit(false,10);Assert.False(navigation.IsCompleted);var scroll=DockPageScroll(f.Panel);
        var settings=Named<TabItem>(f.Panel,"CricketSettingsTab");Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(settings)).Select();
        Assert.Same(settings,Named<TabControl>(f.Panel,"MainTabControl").SelectedItem);var offset=scroll.Offset;
        await navigation;await Settle(100);Assert.Same(settings,Named<TabControl>(f.Panel,"MainTabControl").SelectedItem);Assert.Equal(offset,scroll.Offset);Assert.Equal(before,State(match));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketPlayerFiguresNavigation")]
    public async Task ActualPlayerMenuEditFiguresStillHandsOffToItsRequestedPlayer()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);string before=State(match);
        var panel=new Roche_Scoreboard.Views.CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var opened=await Open(window,panel,"top");var action=Assert.Single(opened.Surface.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Tag,"figures"));
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(action)).Invoke();
        await Until(()=>ReferenceEquals(Named<TabControl>(panel,"MainTabControl").SelectedItem,Named<TabItem>(panel,"ScorecardTab")),"Actual player menu must reach Scorecard.");await Settle(200);
        var row=DockPlayerRows(panel,false)[match.CurrentInnings!.StrikerIndex];var cell=DockFigures(row)[0];
        Assert.Same(cell,window.FocusManager?.GetFocusedElement());await StableReachable(window,cell);Assert.Equal(before,State(match));
    }
}
