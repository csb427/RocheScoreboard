using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task ScorecardWorkspaceMakesEditTargetsAndSelectedRoleClear(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,width,height);string state=State(match),draft=FigureDraft(f.Panel);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        foreach(bool bowling in new[]{false,true,false})
        {
            await DockTeamPointer(f.Panel,f.Window,bowling,$"workspace-team-{width}-{bowling}");
            var selected=DockTeam(f.Panel,bowling);var other=DockTeam(f.Panel,!bowling);
            var check=Assert.Single(selected.GetVisualDescendants().OfType<TextBlock>(),x=>x.Name=="ScorecardTeamSelected");
            var otherCheck=Assert.Single(other.GetVisualDescendants().OfType<TextBlock>(),x=>x.Name=="ScorecardTeamSelected");
            Assert.True(check.IsVisible);Assert.False(otherCheck.IsVisible);
            Assert.Equal(1,Assert.Single(selected.GetVisualDescendants().OfType<OperatorChoiceSurface>()).Progress);
            Assert.Equal(0,Assert.Single(other.GetVisualDescendants().OfType<OperatorChoiceSurface>()).Progress);
            var first=DockPlayerRows(f.Panel,bowling)[0];
            var rowSurface=Assert.Single(first.Children.OfType<Border>(),x=>Equals(x.Tag,"scorecard-player-surface"));
            Assert.Equal(14,rowSurface.CornerRadius.TopLeft);Assert.False(rowSurface.IsHitTestVisible);
            var figure=DockFigures(first)[0];await Reveal(f.Window,figure);await StableReachable(f.Window,figure);
            var well=Assert.Single(figure.GetVisualDescendants().OfType<Border>(),x=>x.Name=="FigureWell");
            Assert.Equal(1,well.BorderThickness.Left);Assert.True(figure.Bounds.Height>=40);
            PromptCapture(f.Window,$"workspace-row-{width}-{bowling}");
            Assert.Equal(state,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
        }
    }
}
