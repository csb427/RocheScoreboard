using Avalonia.Automation.Peers;
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
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketTossTeamIdentity")]
    public async Task TossEntryOwnsAllPointerProbesAndEscapeReturnsItsFocus(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch();match.TossPending=true;
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(500);string before=State(match);
        var entry=Named<Button>(panel,"StartTossButton");await Reveal(window,entry);await StableReachable(window,entry);TapNow(window,entry);await Settle(550);
        Assert.True(Named<Border>(panel,"TossOverlay").IsEffectivelyVisible);KeyNow(window,PhysicalKey.Escape);await Settle(120);
        Assert.False(Named<Border>(panel,"TossOverlay").IsEffectivelyVisible);Assert.Same(entry,window.FocusManager?.GetFocusedElement());Assert.Equal(before,State(match));
        PromptCapture(window,"toss-entry-after-"+width);
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false,false,false)]
    [InlineData(420,320,false,true,true)]
    [InlineData(420,320,true,false,false)]
    [Trait("Evidence","CricketTossTeamIdentity")]
    public async Task RepeatedTossSelectionsRemainDraftUntilAnExplicitCurrentConfirmation(int width,int height,bool reduced,bool calm,bool confirm)
    {
        using var scope=new Scope(reduced);OperatorAppearance.ConfigureMotionStyle(calm?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
        var match=CricketReliabilityChecks.CreateMatch();match.TossPending=true;
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(500);string before=State(match);
        await OpenTossIdentity(window,panel);
        var home=Named<RadioButton>(panel,"TossHomeChoice");var away=Named<RadioButton>(panel,"TossAwayChoice");
        foreach(var choice in new[]{home,away,home}){await Reveal(window,choice);await StableReachable(window,choice);TapNow(window,choice);Assert.True(choice.IsChecked);}
        Assert.True(home.IsChecked);Assert.False(away.IsChecked);Assert.False(Named<Button>(panel,"ConfirmTossButton").IsEnabled);
        var bat=Named<RadioButton>(panel,"TossBatChoice");var bowl=Named<RadioButton>(panel,"TossBowlChoice");
        await Reveal(window,bat);await StableReachable(window,bat);await StableReachable(window,bowl);
        TapNow(window,bat);TapNow(window,bowl);TapNow(window,bat);
        Assert.True(bat.IsChecked);Assert.False(bowl.IsChecked);Assert.True(Named<Button>(panel,"ConfirmTossButton").IsEnabled);
        Assert.Equal(match.TeamAName+" won the toss and will bat first.",Named<TextBlock>(panel,"TossSelectionSummary").Text);Assert.Equal(before,State(match));
        if(confirm)
        {
            var accept=Named<Button>(panel,"ConfirmTossButton");await Reveal(window,accept);await StableReachable(window,accept);TapNow(window,accept);await Settle(120);
            Assert.False(match.TossPending);Assert.Equal(match.TeamAName,match.TossWinner);Assert.True(match.TeamABatsFirst);Assert.True(match.TossWinnerElectedToBat);
            Assert.Equal(0,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.TotalWickets);Assert.Empty(match.CurrentInnings.Deliveries);
        }
        else
        {
            var close=Named<Button>(panel,"TossCloseButton");await Reveal(window,close);await StableReachable(window,close);
            Assert.Equal(44,close.Bounds.Width);Assert.Equal(44,close.Bounds.Height);Assert.Equal("Close toss without confirming",ControlAutomationPeer.CreatePeerForElement(close)!.GetName());
            TapNow(window,close);await Settle(100);Assert.True(match.TossPending);Assert.Equal(before,State(match));
        }
        Assert.False(Named<Border>(panel,"TossOverlay").IsEffectivelyVisible);
    }
    private static async Task OpenTossIdentity(Window window,CricketControlPanel panel)
    {
        var start=Named<Button>(panel,"StartTossButton");await Reveal(window,start);
        PromptCapture(window,"toss-entry-"+window.Width);
        var box=Painted(window,start);
        Record("toss-entry-hits-"+window.Width,new{box,window.ClientSize,hits=new[]{box.Center,new Avalonia.Point(box.Left+4,box.Center.Y),new Avalonia.Point(box.Right-4,box.Center.Y),new Avalonia.Point(box.Center.X,box.Top+4),new Avalonia.Point(box.Center.X,box.Bottom-4)}.Select(point=>new{point,hit=window.InputHitTest(point)?.GetType().Name,name=(window.InputHitTest(point) as Control)?.Name})});
        // Use the real keyboard entry. The pre-existing entry surface did not
        // own all five pointer probes; that separate geometry gap is retained.
        KeyNow(window,PhysicalKey.Enter);await Settle(550);
        Assert.True(Named<Border>(panel,"TossOverlay").IsEffectivelyVisible);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketTossTeamIdentity")]
    public async Task FullTossTeamNamesRemainReadableAndNamedBeforeAnyChoiceIsConfirmed(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch();match.TossPending=true;
        match.SetTeams("North Eastern District Community Cricket Club","NED","South Coast United Cricket and Sporting Club","SCU");
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(500);string before=State(match);
        await OpenTossIdentity(window,panel);PromptCapture(window,"toss-before-choice-"+width);
        foreach(var pair in new[]{("TossHomeChoice",match.TeamAName),("TossAwayChoice",match.TeamBName)})
        {
            var choice=Named<RadioButton>(panel,pair.Item1);await Reveal(window,choice);await StableReachable(window,choice);
            var title=Assert.Single(choice.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==pair.Item2);
            Assert.DoesNotContain(title.TextLayout.TextLines,line=>line.HasCollapsed);
            Assert.All(title.TextLayout.TextLines,line=>Assert.True(line.Width<=title.Bounds.Width+.01));
            Assert.Equal("Toss winner: "+pair.Item2,ControlAutomationPeer.CreatePeerForElement(choice)!.GetName());
        }
        Assert.False(Named<Button>(panel,"ConfirmTossButton").IsEnabled);Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await Settle(120);Assert.False(Named<Border>(panel,"TossOverlay").IsEffectivelyVisible);Assert.Equal(before,State(match));
    }
}
