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
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,false)]
    [InlineData(1280,850,true)][InlineData(420,320,true)]
    [Trait("Evidence","CricketUndeliveredRunOut")]
    public async Task BeforeDeliveryRunOutReviewCancelsWithoutChangingPlay(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);Assert.True(match.CanRecordNonDeliveryRunOut);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var sheet=await FormatSheetOpen(panel,window,"before-delivery");
        Assert.True(PromptAction(sheet,"cancel").IsFocused,"Contextual consequential decision must initially focus Cancel.");
        var record=PromptAction(sheet,"record-before-delivery");await Reveal(window,record);await StableReachable(window,record);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        var context=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"undelivered-runout-context"));
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==match.CurrentInnings!.NonStriker!.FullName);
        Assert.Contains(context.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="0 (0)");
        Assert.Contains(record.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("bowler wickets and any pending free hit stay unchanged")==true);
        Assert.False(record.IsDefault);
        PromptCapture(window,$"runout-open-{width}-{reduced}");Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketUndeliveredRunOut")]
    public async Task BeforeDeliveryRunOutStillUsesCurrentTokenAndReversibleModelAction(bool stale)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var innings=match.CurrentInnings!;int victimIndex=innings.NonStrikerIndex;
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(500);
        var sheet=await FormatSheetOpen(panel,window,"before-delivery");
        if(stale)match.RecordDelivery(1,CricketDeliveryType.Runs);
        string before=State(match);int revision=match.DeliveryRevision;int balls=innings.LegalBallsBowled;int striker=innings.StrikerIndex;int deliveries=innings.Deliveries.Count;int bowlerWickets=innings.CurrentBowler!.Wickets;bool freeHit=match.FreeHitPending;
        var record=PromptAction(sheet,"record-before-delivery");await Reveal(window,record);await StableReachable(window,record);TapNow(window,record);
        if(stale) {
            await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"Old decision must yield to its existing stale rejection notice.");await Settle(550);
            var rejected=PromptSheet(panel);Assert.Contains(rejected.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Run-out was not recorded");
            Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
            KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,rejected);Assert.Equal(before,State(match));
        }
        else {
            await Until(()=>innings.BattingOrder[victimIndex].IsOut,"Existing guarded model action must dismiss the captured non-striker.");await PromptRetired(panel,sheet);
            Assert.Equal(1,innings.TotalWickets);Assert.Equal(4,innings.TotalRuns);Assert.Equal(balls,innings.LegalBallsBowled);Assert.Equal(deliveries,innings.Deliveries.Count);
            Assert.Equal(striker,innings.StrikerIndex);Assert.Equal(bowlerWickets,innings.CurrentBowler!.Wickets);Assert.Equal(freeHit,match.FreeHitPending);
            Assert.Equal(DismissalType.RunOut,innings.BattingOrder[victimIndex].HowOut);Assert.True(match.CanUndoNonDeliveryRunOut);
            Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken,out string error),error);
            Assert.False(match.CurrentInnings!.BattingOrder[victimIndex].IsOut);Assert.Equal(0,match.CurrentInnings.TotalWickets);
            Assert.Equal(4,match.CurrentInnings.TotalRuns);Assert.Equal(balls,match.CurrentInnings.LegalBallsBowled);Assert.Equal(deliveries,match.CurrentInnings.Deliveries.Count);
        }
    }

}
