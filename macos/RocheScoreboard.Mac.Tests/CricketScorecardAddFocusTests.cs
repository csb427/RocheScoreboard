using System.Collections.Specialized;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static Button FowAdd(CricketControlPanel panel)=>Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-fow-add"));
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketScorecardAddFocus")]
    public async Task AddWicketNoteDoesNotOverrideANewerFocusChoice(int width,int height)
    {
        using var scope=new Scope();var match=DockWicketMatch();
        // Explicit older/incomplete-note fixture: one recorded wicket with no
        // note yet. The figures/deliveries themselves are left untouched.
        match.CurrentInnings!.ManualFallOfWickets=[];string before=State(match);var f=await DockOpen(scope,match,width,height);
        var add=FowAdd(f.Panel);await Reveal(f.Window,add);await StableReachable(f.Window,add);
        var rows=Read<StackPanel>(f.Panel,"_scorecardRows");Button? newer=null;bool witnessed=false;
        void Added(object? sender,NotifyCollectionChangedEventArgs args)
        {
            if(args.NewItems is null || !args.NewItems.OfType<StackPanel>().Any(x=>Equals(x.Tag,"scorecard-inline-fow")))return;
            newer=Assert.Single(rows.GetLogicalDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-edit-totals"));
            witnessed=newer.Focus(NavigationMethod.Tab)&&ReferenceEquals(f.Window.FocusManager?.GetFocusedElement(),newer);
        }
        rows.Children.CollectionChanged+=Added;
        try
        {
            TapNow(f.Window,add);Assert.True(witnessed,"Actual newly rendered summary action must take focus before deferred field focus.");await Settle(150);
            Assert.Same(newer,f.Window.FocusManager?.GetFocusedElement());Assert.Equal(before,State(match));
            Assert.Equal(new[]{""},Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
        }
        finally{rows.Children.CollectionChanged-=Added;}
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketScorecardAddFocus")]
    public async Task AnUninterruptedAddStillFocusesAndRevealsTheActualNewNote(int width,int height)
    {
        using var scope=new Scope();var match=DockWicketMatch();match.CurrentInnings!.ManualFallOfWickets=[];string before=State(match);var f=await DockOpen(scope,match,width,height);
        var add=FowAdd(f.Panel);await Reveal(f.Window,add);await StableReachable(f.Window,add);TapNow(f.Window,add);await Settle(150);
        var field=DockWicketField(f.Panel);Assert.Same(field,f.Window.FocusManager?.GetFocusedElement());await StableReachable(f.Window,field);
        Assert.Equal(before,State(match));Assert.Equal(new[]{""},Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);PromptCapture(f.Window,"new-wicket-note-"+width);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketScorecardAddFocus")]
    public async Task AnObsoleteAddAutomationHandleCannotAppendBeyondTheCurrentWicketCount()
    {
        using var scope=new Scope();var match=DockWicketMatch();match.CurrentInnings!.ManualFallOfWickets=[];string before=State(match);var f=await DockOpen(scope,match,1280,850);
        var add=FowAdd(f.Panel);await Reveal(f.Window,add);await StableReachable(f.Window,add);
        var old=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(add));TapNow(f.Window,add);await Settle(150);
        Assert.False(add.IsLoaded);Assert.False(FowAdd(f.Panel).IsEnabled);Assert.Single(Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);
        old.Invoke();await Settle(150);Assert.Single(Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);Assert.Equal(before,State(match));
    }
}
