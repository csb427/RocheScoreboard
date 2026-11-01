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
    private static TextBox LineupField(Border sheet,string caption)=>Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>ControlAutomationPeer.CreatePeerForElement(x)?.GetName()==caption);
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketLineupValidation")]
    public async Task InvalidLineupDraftStaysInTheSameSheetWithBothTeamEdits(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var sheet=await FormatSheetOpen(panel,window,"teams");var name=LineupField(sheet,"Home team name");
        var away=PromptAction(sheet,"edit-team-away");await Reveal(window,away);await StableReachable(window,away);TapNow(window,away);await Settle(100);
        await FigureType(window,LineupField(sheet,"Away abbreviation"),"AWD");
        var home=PromptAction(sheet,"edit-team-home");await Reveal(window,home);await StableReachable(window,home);TapNow(window,home);await Settle(100);
        await FigureType(window,name,match.TeamBName);
        var save=PromptAction(sheet,"save");await Reveal(window,save);await StableReachable(window,save);TapNow(window,save);await Settle(550);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"lineup-invalid-{width}");
        Assert.Same(sheet,PromptSheet(panel));Assert.Equal(before,State(match));Assert.Same(name,LineupField(sheet,"Home team name"));
        Record("lineup-invalid-focus-"+width,new{name.IsFocused,ready=Ready(window,name),box=Painted(window,name),focus=(window.FocusManager?.GetFocusedElement()as Control)?.Name});
        Assert.True(name.IsFocused,"Rejected team-name field must receive focus.");Assert.True(Ready(window,name),"Rejected team-name field must be painted inside the compact viewport.");
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"lineup-validation-error") && x.Text=="Enter two distinct team names.");
        await FigureType(window,name,"Home repaired");await Reveal(window,away);await StableReachable(window,away);TapNow(window,away);await Settle(100);
        Assert.Equal("AWD",LineupField(sheet,"Away abbreviation").Text);Assert.Equal(before,State(match));
        if(width==1280){save=PromptAction(sheet,"save");await Reveal(window,save);await StableReachable(window,save);TapNow(window,save);await PromptRetired(panel,sheet);Assert.Equal("Home repaired",match.TeamAName);Assert.Equal("AWD",match.TeamBAbbr);Assert.Equal(0,match.TotalRuns);}
        else{KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));}
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketLineupValidation")]
    public async Task OldLineupDraftCannotOverwriteANewerRosterIdentity()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await FormatSheetOpen(panel,window,"teams");
        Assert.True(match.TryUpdateTeam(true,"External home","EXT",match.GetLineup(true),out string error),error);string newer=State(match);int revision=match.DeliveryRevision;
        var save=PromptAction(sheet,"save");await Reveal(window,save);await StableReachable(window,save);TapNow(window,save);await Settle(550);
        Assert.Equal("External home",match.TeamAName);Assert.Equal(newer,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Same(sheet,PromptSheet(panel));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketLineupValidation")]
    public async Task LineupSaveAfterNewPlayPreservesTheNewDeliveryAndCurrentFigures()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await FormatSheetOpen(panel,window,"teams");await FigureType(window,LineupField(sheet,"Home team name"),"Home renamed after play");
        match.RecordDelivery(1,CricketDeliveryType.Runs);int striker=match.CurrentInnings!.StrikerIndex;
        var save=PromptAction(sheet,"save");await Reveal(window,save);await StableReachable(window,save);TapNow(window,save);await PromptRetired(panel,sheet);
        Assert.Equal("Home renamed after play",match.TeamAName);Assert.Equal(1,match.TotalRuns);Assert.Equal(1,match.CurrentInnings!.LegalBallsBowled);Assert.Single(match.CurrentInnings.Deliveries);Assert.Equal(striker,match.CurrentInnings.StrikerIndex);
    }

}
