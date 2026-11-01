using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;
using Roche_Scoreboard.Services;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,false)]
    [InlineData(1280,850,true)][InlineData(420,320,true)]
    [Trait("Evidence","CricketDismissalSheet")]
    public async Task ScorecardDismissalSheetKeepsPlayerDraftLocalUntilApply(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,width,height);string initial=State(match),draft=FigureDraft(f.Panel);
        var first=DockPlayerRows(f.Panel,false)[0];
        var opener=Assert.Single(first.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-dismissal"));
        await Reveal(f.Window,opener);await StableReachable(f.Window,opener);TapNow(f.Window,opener);await Settle(550);
        var sheet=PromptSheet(f.Panel);var body=Assert.Single(sheet.GetVisualDescendants().OfType<StackPanel>(),x=>Equals(x.Tag,"scorecard-dismissal-editor"));
        var selected=Assert.Single(body.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"NotOut"));
        await Reveal(f.Window,selected);await StableReachable(f.Window,selected);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(f.Window,$"dismissal-open-{width}-{reduced}");
        var proposed=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-dismissal-proposed"));
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Player 1");
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="0 runs · 0 balls");
        foreach(string key in new[]{"Caught","RunOut","NotOut"}) {
            var choice=Assert.Single(body.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,key));
            await Reveal(f.Window,choice);await StableReachable(f.Window,choice);TapNow(f.Window,choice);await Settle(500);
            Assert.True(choice.Bounds.Height>=72);
            var active=body.GetVisualDescendants().OfType<Button>().Where(x=>Avalonia.Automation.AutomationProperties.GetItemStatus(x)=="Selected").ToArray();
            Assert.Same(choice,Assert.Single(active));
            Assert.Equal(1,Assert.Single(choice.GetVisualDescendants().OfType<OperatorChoiceSurface>()).Progress);
            Assert.Equal("Player 1 · Proposed: "+(key=="NotOut"?"Not out":key=="RunOut"?"Run out":"Caught"),proposed.Text);
            Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
            if(key=="Caught" && width==1280 && !reduced) {
                var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);
                await Until(()=>sheet.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Choose the bowler to receive this wicket."),"Invalid Apply must show the current bowler requirement in the same open sheet.");
                Assert.Same(sheet,PromptSheet(f.Panel));
                var bowler=Assert.Single(body.GetVisualDescendants().OfType<ComboBox>(),x=>Avalonia.Automation.AutomationProperties.GetName(x)=="Dismissal bowler");
                await Until(()=>bowler.IsFocused,"Rejected actor must receive current-sheet focus.");Assert.True(Ready(f.Window,bowler));
                // Set a visible draft field, not private workflow/model state.
                // Native dropdown keyboard/popup acceptance is separate.
                bowler.SelectedIndex=1;
                await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);
                await Until(()=>sheet.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Choose the fielder for this dismissal."),"Missing fielder must remain an in-place rejection.");
                Assert.Same(sheet,PromptSheet(f.Panel));
                var fielder=Assert.Single(body.GetVisualDescendants().OfType<ComboBox>(),x=>Avalonia.Automation.AutomationProperties.GetName(x)=="Dismissal fielder");
                await Until(()=>fielder.IsFocused,"Rejected fielder must receive current-sheet focus.");Assert.True(Ready(f.Window,fielder));
                Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
            }
        }
        var cancel=PromptAction(sheet,"scorecard-correction-cancel");await Reveal(f.Window,cancel);await StableReachable(f.Window,cancel);PromptCapture(f.Window,$"dismissal-cancel-{width}-{reduced}");TapNow(f.Window,cancel);await PromptRetired(f.Panel,sheet);
        Assert.Equal(initial,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketDismissalSheet")]
    public async Task ScorecardDismissalRunOutCorrectionCommitsThroughExistingApply()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await DockOpen(scope,match,1280,850);
        var first=DockPlayerRows(f.Panel,false)[0];var opener=Assert.Single(first.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-dismissal"));
        await Reveal(f.Window,opener);TapNow(f.Window,opener);await Settle(550);
        var sheet=PromptSheet(f.Panel);var choice=PromptAction(sheet,"RunOut");
        await Reveal(f.Window,choice);await StableReachable(f.Window,choice);TapNow(f.Window,choice);await Settle(450);
        Assert.False(match.CurrentInnings!.BattingOrder[0].IsOut);
        var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);
        await Until(()=>match.CurrentInnings.BattingOrder[0].IsOut,"Original Apply must commit the selected dismissal.");
        await PromptRetired(f.Panel,sheet);
        Assert.Equal(Roche_Scoreboard.Models.DismissalType.RunOut,match.CurrentInnings.BattingOrder[0].HowOut);
        Assert.Equal(1,match.CurrentInnings.TotalWickets);Assert.Equal(0,match.CurrentInnings.TotalRuns);
        Assert.All(match.CurrentInnings.BowlingAttack,x=>Assert.Equal(0,x.Wickets));
    }

}
