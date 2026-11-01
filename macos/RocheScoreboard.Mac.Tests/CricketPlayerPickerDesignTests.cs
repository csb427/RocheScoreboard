using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(false,700,450)] [InlineData(true,700,450)]
    [InlineData(false,420,320)] [InlineData(true,420,320)]
    public async Task PlayerPickerShowsCurrentDraftAndCommitsOnlyOnConfirm(bool bowling,int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(false);
        if(bowling){match.SetStriker(0);match.SetNonStriker(1);}
        var innings=match.CurrentInnings!;
        string longName="Alexandria Charlotte Very Long Player Name";
        var roster=bowling?innings.BowlingAttack:innings.BattingOrder;
        roster[1].FirstName=longName;roster[1].LastName="Review";
        string original=State(match);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(200);
        var source=Named<Button>(panel,bowling?"SelectOpeningBowler":"SelectOpeningBatters");
        await Reveal(window,source);await StableReachable(window,source);TapNow(window,source);
        string role=bowling?"Bowler":"Batter";var card=Named<OperatorGlassSurface>(panel,role+"SelectionCard");
        await Settle(90);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"picker-opening-{role}-{width}");
        await Settle(550);
        var list=Named<ListBox>(panel,role+"SelectionList");
        Assert.Equal("Available "+role.ToLowerInvariant()+"s",ControlAutomationPeer.CreatePeerForElement(list)?.GetName());
        list.ScrollIntoView(list.Items[1]!);await Settle(70);
        var row=Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
        await StableReachable(window,row);TapNow(window,row);
        await Settle(100);
        Assert.Equal(1,list.SelectedIndex);Assert.Equal(original,State(match));
        var selected=Named<TextBlock>(panel,role+"SelectedName");Assert.Equal(longName+" Review",selected.Text);
        Assert.True(new Rect(window.Bounds.Size).Contains(Painted(window,selected)));
        var fill=Assert.Single(row.GetVisualDescendants().OfType<OperatorChoiceSurface>());
        Assert.InRange(fill.Progress,.001,.999); // genuine selection surface, not only the tick
        var layers=fill.GetVisualParent()!.GetVisualChildren().ToArray();
        var contentLayer=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>x.Name=="PlayerChoiceContentLayer");
        Assert.True(Array.IndexOf(layers,fill)<Array.IndexOf(layers,contentLayer),"Selected fill must be behind the actual player text.");
        var surface=Assert.Single(row.GetVisualDescendants().OfType<Control>(),x=>x.Name=="PART_Surface");
        Assert.True(Painted(window,list).Contains(Painted(window,surface)));
        PromptCapture(window,$"picker-selected-{role}-{width}");
        var skip=Assert.Single(Named<Border>(panel,role+"SelectionOverlay").GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"SKIP FOR NOW"));
        var skipText=Assert.Single(skip.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="SKIP FOR NOW");
        Assert.True(Painted(window,skip).Contains(Painted(window,skipText)),"Full Skip label must stay in its button.");
        var confirm=Named<Button>(panel,"Confirm"+role+"Button");await StableReachable(window,confirm);TapNow(window,confirm);
        await Settle(650);
        if(bowling){Assert.Equal(1,innings.CurrentBowlerIndex);Assert.False(innings.NeedsBowlerSelection);}
        else {Assert.Equal(1,innings.StrikerIndex);Assert.True(innings.NeedsBatterSelection);}
        // Repeated Enter/Escape safety is covered by existing real-route selector cases.
        KeyNow(window,PhysicalKey.Escape);await Settle(100);
        Assert.Equal(0,innings.TotalRuns);Assert.Equal(0,innings.LegalBallsBowled);
        Assert.False(Named<Border>(panel,"BowlerSelectionOverlay").IsVisible);
        Assert.False(Named<Border>(panel,"BatterSelectionOverlay").IsVisible);
    }
}
