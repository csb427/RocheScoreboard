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
    [AvaloniaTheory][InlineData(700,450,false)][InlineData(700,450,true)][InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","ScorecardFigureIdentity")]
    public async Task FigureSheetNamesTheSelectedPlayerRoleAndInnings(int width,int height,bool bowling)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,width,height);await DockTeamPointer(f.Panel,f.Window,bowling,"figure-identity-role");
        var figure=DockFigures(DockPlayerRows(f.Panel,bowling)[0])[0];await Reveal(f.Window,figure);await StableReachable(f.Window,figure);TapNow(f.Window,figure);
        await Until(()=>f.Panel.HasActiveDecision,"Actual figure must open its editor.");await Settle(100);
        var sheet=PromptSheet(f.Panel);var field=FigureField(sheet);
        var texts=sheet.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text??"").ToArray();
        Assert.Contains(texts,x=>x.Contains("Player 1")&&x.Contains(bowling?"Bowling":"Batting")&&x.Contains("1st innings"));
        var identity=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-figure-context"));Assert.True(DockWholeTextVisible(f.Window,identity));
        PromptCapture(f.Window,$"figure-identity-{width}-{bowling}");
        await Reveal(f.Window,field);await Key(f.Window,PhysicalKey.Escape);await PromptRetired(f.Panel,sheet);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","ScorecardRetiredFigure")]
    public async Task RetiredFigureCannotOpenAnEditorInAnotherRoleOrReplacementMatch(bool replacement)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,900,700);
        var old=DockFigures(DockPlayerRows(f.Panel,false)[0])[0];await Reveal(f.Window,old);await StableReachable(f.Window,old);
        var peer=ControlAutomationPeer.CreatePeerForElement(old);Assert.NotNull(peer);var invoke=Assert.IsAssignableFrom<IInvokeProvider>(peer);
        var target=match;
        if(replacement){target=CricketReliabilityChecks.CreateMatch(true);target.RecordDelivery(4,Roche_Scoreboard.Models.CricketDeliveryType.Four);f.Panel.SetMatch(target);await Settle(200);}
        else await DockTeamPointer(f.Panel,f.Window,true,"retired-figure-bowling");
        string oldState=State(match),current=State(target);
        var error=Xunit.Record.Exception(()=>invoke.Invoke());
        Assert.True(error is null||error.GetType().Name is "ElementNotEnabledException" or "ElementNotAvailableException",error?.ToString());
        await Settle(150);Assert.False(f.Panel.HasActiveDecision);
        Assert.Equal(current,State(target));Assert.Equal(oldState,State(match));
        if(!replacement)
        {
            await DockTeamPointer(f.Panel,f.Window,false,"retired-figure-return");
            var restored=DockFigures(DockPlayerRows(f.Panel,false)[0])[0];Assert.Same(old,restored);
            await Reveal(f.Window,restored);await StableReachable(f.Window,restored);TapNow(f.Window,restored);
            await Until(()=>f.Panel.HasActiveDecision,"Reattached cached figure must remain usable.");
            var sheet=PromptSheet(f.Panel);var field=FigureField(sheet);await Reveal(f.Window,field);await Key(f.Window,PhysicalKey.Escape);await PromptRetired(f.Panel,sheet);
            Assert.Equal(current,State(target));
        }
    }
}
