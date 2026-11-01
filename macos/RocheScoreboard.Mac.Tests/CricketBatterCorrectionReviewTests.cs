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
    private static CricketMatchManager BatterReviewMatch()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);
        for(int i=0;i<8;i++)
        {
            if(match.CurrentInnings!.NeedsBowlerSelection)match.SetCurrentBowler(1);
            match.RecordDelivery(0,CricketDeliveryType.Dot);
        }
        Assert.Equal(8,match.CurrentInnings!.Deliveries.Count);
        Assert.Equal(new[]{0,0,0,0,0,0,1,1},match.CurrentInnings.Deliveries.Select(x=>x.StrikerIndex));
        return match;
    }

    private static async Task SelectBatterForReview(Window window,CricketDeliveryDialog entry)
    {
        var choice=Named<ComboBox>(entry,"BatterChoice");
        var actor=Assert.Single(Named<WrapPanel>(entry,"BatterTiles").Children.OfType<Button>(),
            button=>button.Tag is ValueTuple<ComboBox,int> pair&&ReferenceEquals(pair.Item1,choice)&&pair.Item2==1);
        await Reveal(window,actor);await StableReachable(window,actor);TapNow(window,actor);await Settle(80);
        Assert.Equal(1,choice.SelectedIndex);
        // Public fields establish a pending score change, not model state.
        Named<ComboBox>(entry,"TypeChoice").SelectedIndex=(int)CricketDeliveryType.Runs;
        Named<TextBox>(entry,"RunsBox").Text="2";await Settle(80);
    }

    [AvaloniaTheory]
    [InlineData("batter",1280,850)] [InlineData("runs",420,320)]
    [Trait("Evidence","CricketBatterReviewAuthority")]
    public async Task AFieldPublicationDuringBatterReviewCannotApplyAnUnreviewedDraft(string field,int width,int height)
    {
        using var scope=new Scope(true);var match=BatterReviewMatch();var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);await SelectBatterForReview(window,entry);
        string before=State(match);int revision=match.DeliveryRevision,changes=0;match.MatchChanged+=()=>changes++;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);PromptCapture(window,$"batter-review-before-{field}-{width}");
        // Explicit late public field-publication seam, not an assertion that
        // a physical pointer can click through the owned modal backdrop.
        if(field=="batter")Named<ComboBox>(entry,"BatterChoice").SelectedIndex=0;
        else Named<TextBox>(entry,"RunsBox").Text="3";
        await AnswerBowlerCorrection(panel,window,sheet,"ball");
        await Until(()=>Named<Button>(entry,"ApplyButton").IsEnabled
            ||Named<ContentControl>(panel,"DeliveryEntryContent").Content is null,
            "The review must settle to the editable draft or its observed completion before checking state.");
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,changes);
        Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);
        Assert.Equal(field=="batter"?0:1,Named<ComboBox>(entry,"BatterChoice").SelectedIndex);
        Assert.Equal(field=="runs"?"3":"2",Named<TextBox>(entry,"RunsBox").Text);
        Assert.Contains("changed",Named<TextBlock>(entry,"ErrorText").Text,StringComparison.OrdinalIgnoreCase);
        Assert.True(Named<TextBlock>(entry,"ErrorText").IsEffectivelyVisible);
        Assert.True(new Avalonia.Rect(window.ClientSize).Contains(Painted(window,Named<TextBlock>(entry,"ErrorText"))));
        Assert.True(Named<Button>(entry,"ApplyButton").IsKeyboardFocusWithin);
        PromptCapture(window,$"batter-review-rejected-{field}-{width}");
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,true)]
    [Trait("Evidence","CricketBatterReviewSafeReturn")]
    public async Task EnterOrEscapeAtBatterReviewKeepsTheUnappliedDraft(int width,int height,bool escape)
    {
        using var scope=new Scope(true);var match=BatterReviewMatch();var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);await SelectBatterForReview(window,entry);
        string before=State(match);int revision=match.DeliveryRevision;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);await Settle(180);
        KeyNow(window,escape?PhysicalKey.Escape:PhysicalKey.Enter);await Settle(200);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);
        Assert.Equal(1,Named<ComboBox>(entry,"BatterChoice").SelectedIndex);Assert.Equal("2",Named<TextBox>(entry,"RunsBox").Text);
        Assert.DoesNotContain(sheet,entry.GetVisualDescendants());
        Assert.True(Named<Button>(entry,"ApplyButton").IsKeyboardFocusWithin);
    }

    [AvaloniaTheory]
    [InlineData("ball",1280,850)] [InlineData("over",420,320)] [InlineData("innings",1280,850)]
    [InlineData("innings",420,320)]
    [Trait("Evidence","CricketBatterReviewScopes")]
    public async Task NamedBatterReviewAppliesExactlyItsChosenExistingScope(string key,int width,int height)
    {
        using var scope=new Scope(true);var match=BatterReviewMatch();var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);await SelectBatterForReview(window,entry);
        string before=State(match);int revision=match.DeliveryRevision,changes=0;match.MatchChanged+=()=>changes++;
        var sheet=await ReviewBowlerCorrection(panel,window,entry);
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"batter-correction-context"));
        string text=string.Join("\n",sheet.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text));
        Assert.Contains(match.CurrentInnings!.BattingOrder[0].FullName,text);Assert.Contains(match.CurrentInnings.BattingOrder[1].FullName,text);
        Assert.Contains("Over 1",text);Assert.Contains(Named<TextBlock>(entry,"PreviewText").Text!,text);
        await Until(()=>PromptAction(sheet,"cancel").IsKeyboardFocusWithin,"Batter review must start at Keep editing.");
        foreach(string choice in new[]{"ball","over","innings"})Assert.False(PromptAction(sheet,choice).IsDefault);
        Assert.Equal(before,State(match));Assert.Equal(0,changes);PromptCapture(window,$"batter-review-{key}-{width}");
        await AnswerBowlerCorrection(panel,window,sheet,key);
        await Until(()=>Named<ContentControl>(panel,"DeliveryEntryContent").Content is null,"Current accepted review must finish once.");
        Assert.Equal(1,changes);Assert.Equal(revision+1,match.DeliveryRevision);Assert.Equal(2,match.TotalRuns);Assert.Equal(8,match.CurrentInnings.LegalBallsBowled);
        int[] expected=key=="ball"?[1,0,0,0,0,0,1,1]:key=="over"?[1,1,1,1,1,1,1,1]:[1,1,1,1,1,1,0,0];
        Assert.Equal(expected,match.CurrentInnings.Deliveries.Select(x=>x.StrikerIndex));
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketBatterReviewMotion")]
    public async Task BatterReviewSourceFlightSettlesWithoutChangingThePendingCorrection()
    {
        using var scope=new Scope();var match=BatterReviewMatch();var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,1280,850);await Settle(350);
        var entry=await OpenBowlerCorrection(panel,window,match);await SelectBatterForReview(window,entry);
        string before=State(match);var sheet=await ReviewBowlerCorrection(panel,window,entry);
        var surface=PromptSurface(sheet);var scale=Scale(surface);var shift=Shift(surface);
        await Until(()=>RocheScoreboard.Mac.Native.Motion.IsAnimating(scale)||RocheScoreboard.Mac.Native.Motion.IsAnimating(shift),
            "Actual pointer-opened batter review must own its source flight.");
        Assert.True(Math.Abs(scale.ScaleX-1)>.001||Math.Abs(scale.ScaleY-1)>.001||Math.Abs(shift.X)>.5||Math.Abs(shift.Y)>.5);
        PromptCapture(window,"batter-review-moving");
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
        Assert.False(RocheScoreboard.Mac.Native.Motion.IsAnimating(scale));Assert.False(RocheScoreboard.Mac.Native.Motion.IsAnimating(shift));
        Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,shift.X);Assert.Equal(0,shift.Y);
        Assert.Equal(before,State(match));await AnswerBowlerCorrection(panel,window,sheet,"cancel");
        Assert.Same(entry,Named<ContentControl>(panel,"DeliveryEntryContent").Content);
        Assert.Equal(1,Named<ComboBox>(entry,"BatterChoice").SelectedIndex);Assert.Equal("2",Named<TextBox>(entry,"RunsBox").Text);
        Assert.Equal(before,State(match));
    }
}
