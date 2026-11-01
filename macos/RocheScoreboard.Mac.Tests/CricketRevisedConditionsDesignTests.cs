using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static CricketMatchManager RevisionMatch(bool chase)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);
        if(chase) {
            for(int i=0;i<6;i++)match.RecordDelivery(6,CricketDeliveryType.Six);
            Assert.Equal(36,match.CurrentInnings!.TotalRuns);
            Assert.True(match.TryStartAdditionalInnings(false,out string error),error);
            match.SetStriker(0);match.SetNonStriker(1);
            if(match.CurrentInnings!.NeedsBowlerSelection)match.ConfirmBowler(match.SuggestedNextBowler());
            Assert.Equal(37,match.Target);
        }
        return match;
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false,false)][InlineData(1280,850,true,false)]
    [InlineData(420,320,true,false)][InlineData(420,320,true,true)]
    [Trait("Evidence","CricketRevisedConditions")]
    public async Task RevisedConditionsDraftShowsOnlyCurrentInningsInputsAndCancelsSafely(int width,int height,bool chase,bool reduced)
    {
        using var scope=new Scope(reduced);var match=RevisionMatch(chase);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string initial=State(match);var sheet=await FormatSheetOpen(panel,window,"rain");
        var overs=Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-overs"));
        var target=sheet.GetVisualDescendants().OfType<TextBox>().Where(x=>Equals(x.Tag,"conditions-target")).ToArray();
        async Task RevealRevisionField(Control control)
        {
            try { await Reveal(window,control); }
            finally
            {
                if(!Ready(window,control) && Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
                {
                    Directory.CreateDirectory(dir);PromptCapture(window,$"revision-reveal-failed-{width}-{reduced}");
                    object Bounds(Control c){var point=c.TranslatePoint(default,window);return new {c.Name,type=c.GetType().Name,c.Bounds,origin=point,c.IsVisible,c.IsEffectivelyVisible,c.IsEffectivelyEnabled};}
                    var point=control.TranslatePoint(default,window);var centre=point is {} origin?new Avalonia.Point(origin.X+control.Bounds.Width/2,origin.Y+control.Bounds.Height/2):default;
                    File.WriteAllText(Path.Combine(dir,$"revision-reveal-failed-{width}-{reduced}.json"),System.Text.Json.JsonSerializer.Serialize(new {
                        target=Bounds(control),control.IsHitTestVisible,focused=window.FocusManager?.GetFocusedElement()?.GetType().Name,hit=window.InputHitTest(centre)?.GetType().Name,
                        hitAncestors=(window.InputHitTest(centre) as Avalonia.Visual)?.GetVisualAncestors().OfType<Control>().Select(c=>new{c.Name,type=c.GetType().Name,tag=c.Tag?.ToString(),c.Bounds}).ToArray(),
                        ancestors=control.GetVisualAncestors().OfType<Control>().Select(Bounds).ToArray(),
                        scrolls=control.GetVisualAncestors().OfType<ScrollViewer>().Select(s=>new{s.Name,s.Offset,s.Extent,s.Viewport}).ToArray()
                    },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                }
            }
        }
        Assert.Equal(chase?1:0,target.Length);Assert.Equal("20",overs.Text);if(chase)Assert.Equal("37",target[0].Text);
        await RevealRevisionField(chase?target[0]:overs);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"revision-open-{width}-{chase}-{reduced}");
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==(chase?"Revised overs and target":"Revised overs"));
        if(width==1280) {
            var invalid=chase?target[0]:overs;invalid.Text="0";
            var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);
            await Until(()=>invalid.IsFocused,"Invalid official draft field must receive focus in the same sheet.");
            Assert.Same(sheet,PromptSheet(panel));Assert.True(Ready(window,invalid));Assert.Equal("0",invalid.Text);Assert.Equal(initial,State(match));
        }
        overs.Text="10";if(chase)target[0].Text="31";await Settle(80);
        var proposed=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"conditions-proposed"));
        Assert.Equal(chase?"Draft: 10 total overs · target 31":"Draft: 10 total overs",proposed.Text);
        if(chase) {
            Assert.Equal("Official revised target, total runs to win",Avalonia.Automation.AutomationProperties.GetName(target[0]));
            Assert.Contains("not additional runs",Avalonia.Automation.AutomationProperties.GetHelpText(target[0]));
            Assert.True(target[0].Bounds.Height>=50);
        }
        await RevealRevisionField(chase?target[0]:overs);PromptCapture(window,$"revision-draft-{width}-{chase}-{reduced}");
        Assert.Equal(initial,State(match));var close=PromptAction(sheet,"close");await Reveal(window,close);await StableReachable(window,close);TapNow(window,close);await PromptRetired(panel,sheet);
        Assert.Equal(initial,State(match));
    }
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    [Trait("Evidence","CricketRevisedConditions")]
    public async Task SuppliedRevisionAppliesOnceOrRejectsChangedPlay(bool chase,bool stale)
    {
        using var scope=new Scope();var match=RevisionMatch(chase);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var sheet=await FormatSheetOpen(panel,window,"rain");
        Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-overs")).Text="10";
        if(chase)Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"conditions-target")).Text="31";
        if(stale)match.RecordDelivery(1,CricketDeliveryType.Runs);
        string current=State(match);int revision=match.DeliveryRevision;
        var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);
        if(stale) {
            await Until(()=>sheet.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="The match changed while this panel was open. Go back and reopen it before applying changes."),"A revision cannot apply to newer play.");
            Assert.Same(sheet,PromptSheet(panel));Assert.Equal(current,State(match));
            var close=PromptAction(sheet,"close");await Reveal(window,close);TapNow(window,close);await PromptRetired(panel,sheet);Assert.Equal(current,State(match));
        }
        else {
            await PromptRetired(panel,sheet);Assert.Equal(10,match.CurrentOverLimit);Assert.Equal(20,match.TotalOvers);
            Assert.True(match.HasRainAdjustment);Assert.Equal(revision+1,match.DeliveryRevision);
            if(chase)Assert.Equal(31,match.Target);else Assert.Null(match.CurrentInnings!.TargetOverride);
            Assert.Equal(0,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.TotalWickets);Assert.Empty(match.CurrentInnings.Deliveries);
        }
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketRevisedConditions")]
    public async Task RestoreOriginalConditionsRetainsItsExistingConfirmation(bool confirm)
    {
        using var scope=new Scope();var match=RevisionMatch(true);
        Assert.True(match.TryApplyRainAdjustment(10,31,match.MatchConditionsToken,out string error),error);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,420,320);await Settle(500);
        string current=State(match);var sheet=await FormatSheetOpen(panel,window,"rain");
        var clear=PromptAction(sheet,"clear");await Reveal(window,clear);await StableReachable(window,clear);TapNow(window,clear);
        await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"Revision form must hand off to existing restore confirmation.");await Settle(550);
        var decision=PromptSheet(panel);Assert.Contains(decision.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Restore original conditions?");Assert.Equal(current,State(match));
        var action=PromptAction(decision,confirm?"yes":"back");await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);
        if(confirm) {
            await PromptRetired(panel,decision);Assert.False(match.HasRainAdjustment);Assert.Equal(20,match.CurrentOverLimit);Assert.Equal(37,match.Target);
        }
        else {
            await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(decision),"Back must return to the same uncommitted revision form.");await Settle(550);
            Assert.Equal(current,State(match));var returned=PromptSheet(panel);var close=PromptAction(returned,"close");await Reveal(window,close);TapNow(window,close);await PromptRetired(panel,returned);Assert.Equal(current,State(match));
        }
    }

}
