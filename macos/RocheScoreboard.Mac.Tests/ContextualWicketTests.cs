using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class ContextualWicketRulesTests
{
    private static string State(CricketMatchManager match)=>JsonSerializer.Serialize(match.ToState());
    [Theory]
    [InlineData(DismissalType.Bowled)] [InlineData(DismissalType.Caught)] [InlineData(DismissalType.CaughtAndBowled)]
    [InlineData(DismissalType.LBW)] [InlineData(DismissalType.Stumped)] [InlineData(DismissalType.HitWicket)] [InlineData(DismissalType.HitBallTwice)]
    public void OrdinaryRunsCannotDisappearIntoANonRunningDismissal(DismissalType dismissal)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);string before=State(match);int revision=match.DeliveryRevision,events=0;match.MatchChanged+=()=>events++;
        match.RecordDelivery(2,CricketDeliveryType.Wicket,dismissal:dismissal);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);Assert.Contains("runs",match.LastOperationError,StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void IncompatibleCorrectionIsAtomicAndExplicitZeroCorrectionAndUndoWork(bool crossed)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(2,CricketDeliveryType.Runs);var innings=match.CurrentInnings!;var ball=Assert.Single(innings.Deliveries);
        string before=State(match);int revision=match.DeliveryRevision,events=0;match.MatchChanged+=()=>events++;
        var edit=CricketMatchManager.DescribeDelivery(innings,ball) with {Type=CricketDeliveryType.Wicket,Dismissal=DismissalType.Caught,RunOutCrossed=crossed};
        Assert.False(match.TryEditDelivery(ball,edit,out string error));Assert.NotEmpty(error);Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
        Assert.True(match.TryEditDelivery(ball,edit with {Runs=0,RunOutCrossed=false},out error),error);
        Assert.Equal(0,match.TotalRuns);Assert.Equal(1,match.TotalWickets);Assert.True(match.UndoLastDelivery());Assert.Equal(0,match.TotalRuns);Assert.Equal(0,match.TotalWickets);
    }
    [Theory] [InlineData(DismissalType.Stumped,CricketWicketRuns.Wide)] [InlineData(DismissalType.HitWicket,CricketWicketRuns.Wide)] [InlineData(DismissalType.HitBallTwice,CricketWicketRuns.NoBall)]
    public void PenaltyOnlyWicketsKeepExactlyOneExtraAndRejectRunningRuns(DismissalType dismissal,CricketWicketRuns source)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);string before=State(match);match.RecordDelivery(3,CricketDeliveryType.Wicket,dismissal:dismissal,wicketRuns:source);Assert.Equal(before,State(match));
        match.RecordDelivery(1,CricketDeliveryType.Wicket,dismissal:dismissal,wicketRuns:source);Assert.Equal(1,match.TotalRuns);Assert.Equal(1,match.TotalWickets);Assert.Equal(0,match.CurrentInnings!.LegalBallsBowled);
        Assert.True(match.UndoLastDelivery());Assert.Equal(before,State(match));
    }
}
public sealed partial class CricketDeliveryD23RegressionDraftTests
{
    [AvaloniaTheory] [InlineData(700,450)] [InlineData(420,320)]
    [Trait("Evidence","ContextualWicket")]
    public async Task DismissalChangesExposeOnlyApplicableInputsAndClearStaleRuns(int width,int height)
    {
        using var f=await DeliveryFixture.OpenActual("Wicket",width,height);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir){Directory.CreateDirectory(dir);using var image=f.Window.CaptureRenderedFrame();image?.Save(Path.Combine(dir,$"wicket-context-{width}.png"));}
        Assert.False(Named<StackPanel>(f.Entry,"RunOutOptions").IsEffectivelyVisible);
        await ChoiceTile(f,"DismissalTiles","Run out");Assert.True(Named<StackPanel>(f.Entry,"RunOptions").IsEffectivelyVisible);
        Named<TextBox>(f.Entry,"RunsBox").Text="2";
        await ChoiceTile(f,"DismissalTiles","Caught");Assert.Equal("0",Named<TextBox>(f.Entry,"RunsBox").Text);
        Assert.False(Named<StackPanel>(f.Entry,"RunOutOptions").IsEffectivelyVisible);Assert.False(Named<StackPanel>(f.Entry,"RunOptions").IsEffectivelyVisible);Assert.False(Named<WrapPanel>(f.Entry,"DismissedTiles").IsEffectivelyVisible);Assert.True(Named<StackPanel>(f.Entry,"FielderOptions").IsEffectivelyVisible);
        await ChoiceTile(f,"DismissalTiles","Stumped");
        var sources=Named<Control>(f.Entry,"RunOutSourceTiles").GetVisualDescendants().OfType<Button>().Where(x=>x.IsEffectivelyVisible).Select(x=>x.Tag?.ToString()).Order().ToArray();Assert.Equal(new[]{"0","1"},sources);
        var wide=Named<Control>(f.Entry,"RunOutSourceTiles").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Tag,"1"));await Reveal(f.Window,wide);await Tap(f.Window,wide);
        await ChoiceTile(f,"DismissalTiles","Bowled");Assert.Equal(0,Named<ComboBox>(f.Entry,"RunOutChoice").SelectedIndex);Assert.False(Named<StackPanel>(f.Entry,"FielderOptions").IsEffectivelyVisible);
        Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(0,f.ModelChanges);
    }
    [AvaloniaTheory] [InlineData("Stumped", "1")] [InlineData("Hit ball twice", "2")]
    [Trait("Evidence","ContextualWicket")]
    public async Task ApplicablePenaltyOnlyWicketNeedsNoRunSourceChoiceAndUndoRestoresPlay(string dismissal,string source)
    {
        using var f=await DeliveryFixture.OpenActual("Wicket",700,450);
        await ChoiceTile(f,"DismissalTiles",dismissal);
        var tile=Named<Control>(f.Entry,"RunOutSourceTiles").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Tag,source));
        await Reveal(f.Window,tile);await Tap(f.Window,tile);
        Assert.False(Named<StackPanel>(f.Entry,"NoBallOptions").IsEffectivelyVisible);Assert.False(Named<StackPanel>(f.Entry,"RunOptions").IsEffectivelyVisible);
        var apply=Named<Button>(f.Entry,"ApplyButton");await Reveal(f.Window,apply);await Tap(f.Window,apply);await Settle(150);
        Assert.Equal(1,f.Match.TotalRuns);Assert.Equal(1,f.Match.TotalWickets);Assert.Equal(0,f.Match.CurrentInnings!.LegalBallsBowled);Assert.Equal(1,f.EditorClosures);Assert.Equal(1,f.ModelChanges);
        Assert.True(f.Match.UndoLastDelivery());Assert.Equal(f.OpenedState,State(f.Match));
    }

}
