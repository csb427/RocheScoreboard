using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaFact]
    [Trait("Evidence","CricketPendingInningsSwitchLateDraft")]
    public async Task ADeferredTextUpdateCannotApplyAnUnreviewedDraftDuringInningsSwitch()
    {
        using var scope=new Scope();var match=PendingSwitchMatch();string before=State(match);var f=await DockOpen(scope,match,1280,850);
        var sheet=await PendingSwitchReview(f.Panel,f.Window);var cached=DockWicketField(f.Panel);
        // Explicit public control-property boundary, representing a late text
        // publication. This does not simulate a native IME or remote command.
        cached.Text="1-0 (0.2) newer text";await Settle(100);
        Assert.Equal(cached.Text,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets[0]);
        var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);
        await Until(()=>!PromptMounted(f.Panel,sheet),"Old review must retire into its draft-change explanation.");await Settle(300);
        var notice=PromptSheet(f.Panel);Assert.Contains(notice.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Pending edit changed");
        Assert.Equal(before,State(match));Assert.Equal(2,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber);
        Assert.Equal("1-0 (0.2) newer text",DockWicketField(f.Panel).Text);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
        KeyNow(f.Window,PhysicalKey.Escape);await PromptRetired(f.Panel,notice);Assert.Equal(before,State(match));
    }
    private static CricketMatchManager PendingSwitchMatch()
    {
        var match=DockWicketMatch();match.EndCurrentInnings();Assert.True(match.CanStartNextInnings());match.StartNextInnings();
        match.SetStriker(0);match.SetNonStriker(1);match.ConfirmBowler(0);
        match.RecordDelivery(0,CricketDeliveryType.Dot);match.RecordDelivery(0,CricketDeliveryType.Wicket,"bowled");match.SelectNewBatter(2);
        Assert.Equal(2,match.CurrentInningsNumber);return match;
    }
    private static async Task<Border> PendingSwitchReview(CricketControlPanel panel,Window window)
    {
        var field=DockWicketField(panel);await FigureType(window,field,"1-0 (0.2) reviewed");Assert.True(Read<bool>(panel,"_scorecardDirty"));
        var first=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-innings-1"));
        await Reveal(window,first);await StableReachable(window,first);TapNow(window,first);await Settle(550);return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPendingInningsSwitch")]
    public async Task ACommittedOldMatchCannotNavigateTheReplacementMatchesScorecard(int width,int height)
    {
        using var scope=new Scope();var original=PendingSwitchMatch();var replacement=PendingSwitchMatch();replacement.SetTeams("Replacement home","RPH","Replacement away","RPA");
        string otherBefore=State(replacement);var f=await DockOpen(scope,original,width,height);var sheet=await PendingSwitchReview(f.Panel,f.Window);
        bool rebound=false;CricketScorecardDraft? expectedDraft=null;
        void Committed()
        {
            if(rebound||original.AllInnings[1].ScorecardChanges.Count==0)return;
            rebound=true;f.Panel.SetMatch(replacement);
            var tab=Named<TabItem>(f.Panel,"ScorecardTab");Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(tab)).Select();
            expectedDraft=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft");Assert.Equal(2,expectedDraft.InningsNumber);
        }
        original.MatchChanged+=Committed;
        try
        {
            var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);
            await Until(()=>rebound,"The accepted old correction must reach the real model publication boundary.");await Settle(150);
            Assert.NotNull(expectedDraft);Assert.Equal(2,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber);
            Assert.Null(Read<object?>(f.Panel,"_scorecardAppliedFeedback"));
            Assert.Equal(otherBefore,State(replacement));Assert.Equal(new[]{"1-0 (0.2) reviewed"},original.AllInnings[1].ManualFallOfWickets);Assert.Single(original.AllInnings[1].ScorecardChanges);
        }
        finally{original.MatchChanged-=Committed;}
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,true)]
    [Trait("Evidence","CricketPendingInningsSwitch")]
    public async Task KeepingThePendingInningsPreservesItsActualRawNoteAndModel(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=PendingSwitchMatch();string before=State(match);var f=await DockOpen(scope,match,width,height);
        var sheet=await PendingSwitchReview(f.Panel,f.Window);PromptCapture(f.Window,"pending-innings-"+width);
        var context=Assert.Single(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"scorecard-innings-switch-context"));
        var labels=context.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text).ToArray();
        Assert.Contains("PENDING · INNINGS 2",labels);Assert.Contains("THEN VIEW · INNINGS 1",labels);
        Assert.Contains(match.AllInnings[1].BattingTeamName,labels);Assert.Contains(match.AllInnings[0].BattingTeamName,labels);
        Assert.Same(PromptAction(sheet,"close"),f.Window.FocusManager?.GetFocusedElement());
        KeyNow(f.Window,reduced?PhysicalKey.Enter:PhysicalKey.Escape);await PromptRetired(f.Panel,sheet);
        Assert.Equal(2,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber);Assert.Equal("1-0 (0.2) reviewed",DockWicketField(f.Panel).Text);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPendingInningsSwitch")]
    public async Task AcceptingTheCurrentPendingEditSavesItAndViewsOnlyTheRequestedInnings(int width,int height)
    {
        using var scope=new Scope();var match=PendingSwitchMatch();var f=await DockOpen(scope,match,width,height);var sheet=await PendingSwitchReview(f.Panel,f.Window);
        var apply=PromptAction(sheet,"apply");await Reveal(f.Window,apply);await StableReachable(f.Window,apply);TapNow(f.Window,apply);await PromptRetired(f.Panel,sheet);await Settle(150);
        Assert.Equal(1,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").InningsNumber);Assert.Equal(2,match.CurrentInningsNumber);
        Assert.Equal(new[]{"1-0 (0.2) reviewed"},match.AllInnings[1].ManualFallOfWickets);Assert.Single(match.AllInnings[1].ScorecardChanges);Assert.Equal(0,match.AllInnings[1].TotalRuns);Assert.Equal(1,match.AllInnings[1].TotalWickets);
    }
}
