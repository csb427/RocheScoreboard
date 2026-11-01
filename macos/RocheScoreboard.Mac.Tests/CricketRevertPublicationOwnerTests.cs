using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketRevertPublicationOwner")]
    public async Task AcceptedOldRevertCannotDiscardAReplacementMatchesNewNote(int width,int height)
    {
        using var scope=new Scope(true);var original=RecoveryMatch();var replacement=DockWicketMatch();replacement.SetTeams("Replacement home","RPH","Replacement away","RPA");string otherBefore=State(replacement);
        var f=await DockOpen(scope,original,width,height);var sheet=await OpenAdjustmentReview(f.Panel,f.Window);sheet=await ReplacementNext(f.Panel,f.Window,sheet,"revert");
        bool rebound=false;TextBox? published=null;
        void Reverted()
        {
            if(rebound||original.CurrentInnings!.ScorecardChanges.Count!=0)return;
            rebound=true;f.Panel.SetMatch(replacement);
            var tab=Named<TabItem>(f.Panel,"ScorecardTab");Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(tab)).Select();
            published=DockWicketField(f.Panel);
            // Explicit synchronous public text publication into the NEW panel
            // during old-model event handling; no private draft/dirty writes.
            published.Text="1-0 (0.2) replacement pending";Assert.Equal("1-0 (0.2) replacement pending",published.Text);
        }
        original.MatchChanged+=Reverted;
        try
        {
            var yes=PromptAction(sheet,"yes");await Reveal(f.Window,yes);await StableReachable(f.Window,yes);TapNow(f.Window,yes);
            await Until(()=>rebound,"The actual accepted model revert must reach publication.");await Settle(150);
            Assert.NotNull(published);Assert.Same(published,DockWicketField(f.Panel));
            Assert.Equal("1-0 (0.2) replacement pending",DockWicketField(f.Panel).Text);
            Assert.Equal(new[]{"1-0 (0.2) replacement pending"},Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
            Assert.Equal(otherBefore,State(replacement));Assert.Equal(4,original.TotalRuns);Assert.Equal(0,original.CurrentInnings!.Wides);Assert.Empty(original.CurrentInnings.ScorecardChanges);Assert.Single(original.CurrentInnings.Deliveries);
        }
        finally{original.MatchChanged-=Reverted;}
    }
}
