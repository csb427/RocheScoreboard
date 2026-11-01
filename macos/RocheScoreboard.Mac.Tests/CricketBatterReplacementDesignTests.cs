using Avalonia.Controls;
using Avalonia.Headless;
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
    private static async Task<Border> OpenReplacementReview(CricketControlPanel panel,Window window)
    {
        var menu=await Open(window,panel,"top");
        Assert.Equal("replace-batter",menu.First.Tag);
        menu.Root.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);menu.Root.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);
        await Until(()=>Named<Grid>(panel,"CricketRoot").Children.OfType<Border>().Any(x=>Equals(x.Tag,"cricket-decision-sheet")),"Actual batter menu must open replacement review.");
        await Settle(550);return PromptSheet(panel);
    }
    private static async Task<Border> ReplacementNext(CricketControlPanel panel,Window window,Border sheet,string key)
    {
        var action=PromptAction(sheet,key);await Reveal(window,action);await StableReachable(window,action);TapNow(window,action);
        await Until(()=>!Named<Grid>(panel,"CricketRoot").Children.Contains(sheet),"Current replacement step must retire before the next step.");
        await Settle(550);return PromptSheet(panel);
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,true)]
    [InlineData(1280,850,true)][InlineData(420,320,false)]
    [Trait("Evidence","CricketBatterReplacement")]
    public async Task BatterReplacementReviewKeepsDraftAndCanCancelAtTransfer(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(550);
        string before=State(match);var sheet=await OpenReplacementReview(panel,window);
        Assert.True(PromptAction(sheet,"back").IsFocused);
        Assert.Contains(sheet.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"batter-replacement-context"));
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        PromptCapture(window,$"replacement-candidates-{width}");
        sheet=await ReplacementNext(panel,window,sheet,"2");Assert.Equal(before,State(match));
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==match.CurrentInnings!.BattingOrder[2].FullName);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("adds one team wicket")==true);
        PromptCapture(window,$"replacement-pair-{width}-{reduced}");
        var reason=PromptAction(sheet,"WrongPlayer");await Reveal(window,reason);await StableReachable(window,reason);PromptCapture(window,$"replacement-reason-{width}");
        sheet=await ReplacementNext(panel,window,sheet,"WrongPlayer");Assert.Equal(before,State(match));
        var carry=PromptAction(sheet,"carry");Assert.False(carry.IsDefault);Assert.True(PromptAction(sheet,"back").IsFocused);
        Assert.Contains(carry.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("4 (1)")==true);
        PromptCapture(window,$"replacement-transfer-context-{width}-{reduced}");await Reveal(window,carry);await StableReachable(window,carry);PromptCapture(window,$"replacement-transfer-{width}");
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData("RetiredHurt", "")][InlineData("RetiredOut", "")]
    [InlineData("WrongPlayer", "carry")][InlineData("WrongPlayer", "reset")]
    [Trait("Evidence","CricketBatterReplacement")]
    public async Task ReplacementReviewPreservesExistingDepartureAndFiguresSemantics(string reason,string transfer)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var innings=match.CurrentInnings!;int oldIndex=innings.StrikerIndex;int revision=match.DeliveryRevision;
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await OpenReplacementReview(panel,window);sheet=await ReplacementNext(panel,window,sheet,"2");
        if(transfer.Length>0)sheet=await ReplacementNext(panel,window,sheet,reason);
        var confirm=PromptAction(sheet,transfer.Length>0?transfer:reason);Assert.False(confirm.IsDefault);
        await Reveal(window,confirm);await StableReachable(window,confirm);TapNow(window,confirm);await PromptRetired(panel,sheet);
        Assert.Equal(2,innings.StrikerIndex);Assert.Equal(4,innings.TotalRuns);Assert.Equal(1,innings.LegalBallsBowled);Assert.Single(innings.Deliveries);Assert.Equal(revision+1,match.DeliveryRevision);
        Assert.Equal(reason=="RetiredOut"?1:0,innings.TotalWickets);
        Assert.Equal(transfer=="carry"?4:0,innings.BattingOrder[2].Runs);Assert.Equal(transfer=="carry"?0:4,innings.BattingOrder[oldIndex].Runs);
        Assert.Equal(transfer!="carry",innings.BattingOrder[oldIndex].IsRetired);
        if(transfer=="carry")Assert.Equal(2,innings.Deliveries[0].StrikerIndex);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketBatterReplacement")]
    public async Task OldReplacementTransferCannotApplyAfterPlayChangesTheReviewedPair()
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,1280,850);await Settle(550);
        var sheet=await OpenReplacementReview(panel,window);sheet=await ReplacementNext(panel,window,sheet,"2");sheet=await ReplacementNext(panel,window,sheet,"WrongPlayer");
        match.RecordDelivery(1,CricketDeliveryType.Runs);string before=State(match);int revision=match.DeliveryRevision;
        var carry=PromptAction(sheet,"carry");await Reveal(window,carry);await StableReachable(window,carry);TapNow(window,carry);await PromptRetired(panel,sheet);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(5,match.CurrentInnings!.TotalRuns);Assert.Equal(0,match.CurrentInnings.BattingOrder[2].Runs);
    }

}
