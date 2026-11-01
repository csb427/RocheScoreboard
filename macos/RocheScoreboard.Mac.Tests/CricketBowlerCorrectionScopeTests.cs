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
    private static CricketMatchManager MixedBowlerCorrectionMatch()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);
        for(int i=0;i<3;i++)match.RecordDelivery(0,CricketDeliveryType.Dot);
        var innings=match.CurrentInnings!;var middle=innings.Deliveries[1];
        var correction=CricketMatchManager.DescribeDelivery(innings,middle) with{BowlerIndex=1,BowlerForRestOfOver=false};
        Assert.True(match.TryEditDelivery(middle,correction,out string error),error);
        Assert.Equal(new[]{0,1,0},innings.Deliveries.Select(x=>x.BowlerIndex));Assert.Equal(3,innings.LegalBallsBowled);
        return match;
    }
    private static async Task<CricketDeliveryDialog> OpenBowlerCorrection(CricketControlPanel panel,Window window,CricketMatchManager match)
    {
        var row=DeliveryHistoryRow(panel,match.CurrentInnings!.Deliveries[0]);await Reveal(window,row);await StableReachable(window,row);TapNow(window,row);
        await Until(()=>Named<ContentControl>(panel,"DeliveryEntryContent").Content is CricketDeliveryDialog,"Actual history entry must open the current correction.");
        return Assert.IsType<CricketDeliveryDialog>(Named<ContentControl>(panel,"DeliveryEntryContent").Content);
    }
    private static async Task<Border> ReviewBowlerCorrection(CricketControlPanel panel,Window window,CricketDeliveryDialog entry)
    {
        var apply=Named<Button>(entry,"ApplyButton");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);
        await Until(()=>entry.GetVisualDescendants().OfType<Border>().Any(x=>Equals(x.Tag,"cricket-decision-sheet")),"The actual correction owns its nested scope decision.");
        return Assert.Single(entry.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
    }
    private static async Task AnswerBowlerCorrection(CricketControlPanel panel,Window window,Border sheet,string key)
    {
        var action=PromptAction(sheet,key);await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);
        await Until(()=>!panel.GetVisualDescendants().Contains(sheet),"The editor's actual nested decision must retire.");Assert.False(sheet.IsLoaded);await Settle(150);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketBowlerCorrectionScope")]
    public async Task RejectedBowlerCorrectionCannotReuseItsPreviousOverScopeAfterReturningToOriginalBowler()
    {
        using var scope=new Scope(true);var match=MixedBowlerCorrectionMatch();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);int changes=0;match.MatchChanged+=()=>++changes;string before=State(match);int revision=match.DeliveryRevision;
        Named<ComboBox>(entry,"TypeChoice").SelectedIndex=(int)CricketDeliveryType.Bye;Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=1;await Settle(100);
        Assert.Equal("0",Named<TextBox>(entry,"RunsBox").Text);var sheet=await ReviewBowlerCorrection(panel,window,entry);await AnswerBowlerCorrection(panel,window,sheet,"yes");
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,changes);Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.NotEmpty(Named<TextBlock>(entry,"ErrorText").Text!);
        Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=0;await Settle(100);
        var one=Assert.Single(Named<Panel>(entry,"RunsTiles").Children.OfType<Button>(),x=>Equals(x.Tag,"1"));await Reveal(window,one);await StableReachable(window,one);TapNow(window,one);await Settle(100);
        var apply=Named<Button>(entry,"ApplyButton");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);await Settle(250);
        Assert.Null(Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(1,changes);Assert.Equal(revision+1,match.DeliveryRevision);
        Assert.Equal(new[]{0,1,0},match.CurrentInnings!.Deliveries.Select(x=>x.BowlerIndex));Assert.Equal(1,match.TotalRuns);Assert.Equal(1,match.CurrentInnings.Byes);Assert.Equal(3,match.CurrentInnings.LegalBallsBowled);
    }

    [AvaloniaTheory]
    [InlineData(false,1280,850)][InlineData(true,420,320)]
    [Trait("Evidence","CricketBowlerCorrectionScope")]
    public async Task NamedBowlerReviewAppliesOnlyTheScopeChosenForThisAttempt(bool over,int width,int height)
    {
        using var scope=new Scope(true);var match=MixedBowlerCorrectionMatch();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);Named<ComboBox>(entry,"TypeChoice").SelectedIndex=(int)CricketDeliveryType.Runs;Named<TextBox>(entry,"RunsBox").Text="2";Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=1;await Settle(100);
        string before=State(match);int changes=0;match.MatchChanged+=()=>++changes;int revision=match.DeliveryRevision;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"bowler-correction-context"));
        await Until(()=>PromptAction(sheet,"cancel").IsKeyboardFocusWithin,"Contextual review starts with its safe close action.");Assert.False(PromptAction(sheet,"yes").IsDefault);Assert.False(PromptAction(sheet,"no").IsDefault);
        var text=string.Join("\n",sheet.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text));Assert.Contains("Over 1",text);Assert.Contains(match.CurrentInnings!.BowlingAttack[0].FullName,text);Assert.Contains(match.CurrentInnings.BowlingAttack[1].FullName,text);
        Assert.Equal(before,State(match));Assert.Equal(0,changes);PromptCapture(window,"bowler-correction-scope-"+width);
        await AnswerBowlerCorrection(panel,window,sheet,over?"yes":"no");await Until(()=>Named<ContentControl>(panel,"DeliveryEntryContent").Content is null,"Accepted correction must close exactly once.");
        Assert.Equal(1,changes);Assert.Equal(revision+1,match.DeliveryRevision);Assert.Equal(new[]{1,1,over?1:0},match.CurrentInnings.Deliveries.Select(x=>x.BowlerIndex));Assert.Equal(2,match.TotalRuns);Assert.Equal(3,match.CurrentInnings.LegalBallsBowled);
    }

    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketBowlerCorrectionScope")]
    public async Task KeepEditingTheBowlerReviewPreservesTheUnappliedCorrectionAndMatch(int width,int height)
    {
        using var scope=new Scope(true);var match=MixedBowlerCorrectionMatch();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=1;await Settle(100);string before=State(match);int revision=match.DeliveryRevision;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);await Until(()=>PromptAction(sheet,"cancel").IsKeyboardFocusWithin,"Contextual review starts with its safe close action.");
        if(width==1280){KeyNow(window,PhysicalKey.Enter);await Until(()=>!panel.GetVisualDescendants().Contains(sheet),"Enter on the focused safe close keeps editing.");await Settle(150);}else await AnswerBowlerCorrection(panel,window,sheet,"cancel");
        Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(1,Named<ComboBox>(entry,"BowlerChoice").SelectedIndex);Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.True(Named<Button>(entry,"ApplyButton").IsKeyboardFocusWithin,"Returning to the existing correction should restore the enabled Apply action; current focus: "+window.FocusManager?.GetFocusedElement());
        var cancel=Named<Button>(entry,"CancelButton");await Reveal(window,cancel);await StableReachable(window,cancel);TapNow(window,cancel);await Settle(150);Assert.Null(Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(before,State(match));
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketBowlerCorrectionScope")]
    public async Task BowlerReviewCannotAcceptAnUnreviewedReplacementSelection()
    {
        using var scope=new Scope(true);var match=MixedBowlerCorrectionMatch();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=1;await Settle(100);string before=State(match);int revision=match.DeliveryRevision;int changes=0;match.MatchChanged+=()=>++changes;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=2;await Settle(100);await AnswerBowlerCorrection(panel,window,sheet,"yes");
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,changes);Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Contains("selection changed",Named<TextBlock>(entry,"ErrorText").Text);
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketBowlerCorrectionMotion")]
    public async Task BowlerReviewSourceFlightSettlesImmediatelyWhenMotionIsReduced()
    {
        using var scope=new Scope();var match=MixedBowlerCorrectionMatch();var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);Named<ComboBox>(entry,"BowlerChoice").SelectedIndex=1;await Settle(100);string before=State(match);
        var sheet=await ReviewBowlerCorrection(panel,window,entry);var surface=PromptSurface(sheet);var scale=Scale(surface);var shift=Shift(surface);
        await Until(()=>RocheScoreboard.Mac.Native.Motion.IsAnimating(scale)||RocheScoreboard.Mac.Native.Motion.IsAnimating(shift),"The actual pointer-opened review must own a source-flight clock.");
        Assert.True(Math.Abs(scale.ScaleX-1)>.001 || Math.Abs(scale.ScaleY-1)>.001 || Math.Abs(shift.X)>.5 || Math.Abs(shift.Y)>.5,"Observe a real non-final pose, not an expired animation handle.");PromptCapture(window,"bowler-correction-moving");
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
        Assert.False(RocheScoreboard.Mac.Native.Motion.IsAnimating(scale));Assert.False(RocheScoreboard.Mac.Native.Motion.IsAnimating(shift));Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,shift.X);Assert.Equal(0,shift.Y);
        Assert.Equal(before,State(match));await AnswerBowlerCorrection(panel,window,sheet,"cancel");Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);Assert.Equal(before,State(match));
    }
}
