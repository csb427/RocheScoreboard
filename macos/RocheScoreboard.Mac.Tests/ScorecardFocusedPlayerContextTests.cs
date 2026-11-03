using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(420,320,false)] [InlineData(420,320,true)]
    [InlineData(1280,850,false)] [InlineData(1280,850,true)]
    [Trait("Evidence","ScorecardFocusedPlayerContext")]
    public async Task FocusedLatePlayerFigureKeepsItsIdentityVisibleAndClearsWhenChangingRole(int width,int height,bool bowling)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,width,height);string before=State(match),draft=FigureDraft(f.Panel);
        await DockTeamPointer(f.Panel,f.Window,bowling,"focused-player-team");
        var figure=DockFigures(DockPlayerRows(f.Panel,bowling)[^1])[0];
        await Reveal(f.Window,figure);await StableReachable(f.Window,figure);
        string name=(bowling?match.CurrentInnings!.BowlingAttack:match.CurrentInnings!.BattingOrder)[^1].FullName;
        Assert.True(figure.IsFocused);
        Assert.Contains(f.Panel.GetVisualDescendants().OfType<TextBlock>(),x=>DockWholeTextVisible(f.Window,x)&&x.Text is not null&&x.Text.Contains(name));
        Assert.Contains(name,AutomationProperties.GetName(figure));
        var context=Assert.Single(f.Panel.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-focus-context"));
        Assert.True(DockWholeTextVisible(f.Window,context));Assert.Contains(bowling?"Overs":"Runs",context.Text);
        PromptCapture(f.Window,$"focused-player-{width}-{bowling}");
        await DockTeamPointer(f.Panel,f.Window,!bowling,"focused-player-switch");
        Assert.False(context.IsEffectivelyVisible);
        Assert.Equal(before,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
    }
}
