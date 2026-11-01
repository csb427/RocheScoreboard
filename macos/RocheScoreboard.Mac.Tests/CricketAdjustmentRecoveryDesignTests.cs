using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static void AddRecoveryWide(CricketMatchManager match)
    {
        var draft=match.CreateScorecardDraft(1);draft.TotalRuns++;draft.Wides++;draft.Bowlers[0].Wides++;draft.Bowlers[0].RunsConceded++;
        Assert.True(match.TryApplyScorecard(draft,out string error),error);
    }
    private static CricketMatchManager RecoveryMatch()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);AddRecoveryWide(match);return match;
    }
    private static async Task<Border> OpenAdjustmentReview(CricketControlPanel panel,Window window)
    {
        var action=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-recovery"));await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);await Settle(550);return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketAdjustmentRecovery")]
    public async Task AdjustmentReviewCanCloseWithoutChangingAppliedOrPendingFigures(int width,int height)
    {
        using var scope=new Scope(width==420);var match=RecoveryMatch();var f=await DockOpen(scope,match,width,height);string before=State(match),draft=FigureDraft(f.Panel);
        var sheet=await OpenAdjustmentReview(f.Panel,f.Window);if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);PromptCapture(f.Window,$"adjustment-review-{width}");
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"scorecard-adjustments-context"));Assert.True(PromptAction(sheet,"close").IsFocused);
        KeyNow(f.Window,PhysicalKey.Escape);await PromptRetired(f.Panel,sheet);Assert.Equal(before,State(match));Assert.Equal(draft,FigureDraft(f.Panel));
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketAdjustmentRecovery")]
    public async Task OldRevertConfirmationCannotRevertANewerManualCorrection(bool atConfirmation)
    {
        using var scope=new Scope(true);var match=RecoveryMatch();var f=await DockOpen(scope,match,1280,850);var sheet=await OpenAdjustmentReview(f.Panel,f.Window);
        if(atConfirmation)sheet=await ReplacementNext(f.Panel,f.Window,sheet,"revert");AddRecoveryWide(match);string before=State(match);Assert.Equal(2,match.CurrentInnings!.ScorecardChanges.Count);
        var yes=PromptAction(sheet,atConfirmation?"yes":"revert");await Reveal(f.Window,yes);await StableReachable(f.Window,yes);TapNow(f.Window,yes);await Settle(550);
        Assert.Equal(before,State(match));Assert.Equal(2,match.CurrentInnings.ScorecardChanges.Count);
        var notice=PromptSheet(f.Panel);Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Adjustment was not changed");KeyNow(f.Window,PhysicalKey.Escape);await PromptRetired(f.Panel,notice);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketAdjustmentRecovery")]
    public async Task ExplicitCurrentRevertRestoresOnlyTheReviewedCheckpointOnce(int width,int height)
    {
        using var scope=new Scope(true);var match=RecoveryMatch();var f=await DockOpen(scope,match,width,height);var sheet=await OpenAdjustmentReview(f.Panel,f.Window);
        sheet=await ReplacementNext(f.Panel,f.Window,sheet,"revert");Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"scorecard-revert-context"));
        Assert.True(PromptAction(sheet,"close").IsFocused);var yes=PromptAction(sheet,"yes");Assert.False(yes.IsDefault);
        int changes=0,revision=match.DeliveryRevision;match.MatchChanged+=()=>changes++;
        await Reveal(f.Window,yes);await StableReachable(f.Window,yes);TapNow(f.Window,yes);await PromptRetired(f.Panel,sheet);
        Assert.Equal(4,match.TotalRuns);Assert.Equal(0,match.CurrentInnings!.Wides);Assert.Empty(match.CurrentInnings.ScorecardChanges);Assert.Single(match.CurrentInnings.Deliveries);Assert.Equal(1,match.CurrentInnings.LegalBallsBowled);
        Assert.Equal(4,match.CurrentInnings.BowlingAttack[0].RunsConceded);Assert.Equal(revision+1,match.DeliveryRevision);Assert.Equal(1,changes);string after=State(match);
        yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(150);Assert.Equal(after,State(match));Assert.Equal(1,changes);
    }

}
