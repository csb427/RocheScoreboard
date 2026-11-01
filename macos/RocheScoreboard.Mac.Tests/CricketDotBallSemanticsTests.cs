using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task OriginalDotBallHasSemanticNameAndScoresOncePerAcceptedInput(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);
        var match=CricketReliabilityChecks.CreateMatch(ready:true);
        var panel=new CricketControlPanel(); panel.SetMatch(match);
        var window=scope.Window(panel,width,height); await Settle(650);
        // Mounted production panel with an already-ready match: this is not a
        // Home/setup acceptance claim or a synthetic replacement for scoring.
        var commands=Named<Border>(panel,"ScoringCommands");
        var dot=Assert.Single(commands.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Content,"•"));
        await Reveal(window,dot); await StableReachable(window,dot);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory) Directory.CreateDirectory(directory);
        PromptCapture(window,$"dot-ball-{width}-{reduced}");
        Assert.Equal("Dot ball",ControlAutomationPeer.CreatePeerForElement(dot)?.GetName());
        Assert.Equal(0,match.TotalRuns); Assert.Equal(0,match.TotalWickets);
        int before=match.CurrentInnings!.LegalBallsBowled;
        void Authority(int accepted)
        {
            Assert.Equal(before+accepted,match.CurrentInnings.LegalBallsBowled);
            Assert.Equal(accepted,match.CurrentInnings.Deliveries.Count);
            Assert.Equal(0,match.TotalRuns); Assert.Equal(0,match.TotalWickets);
        }
        TapNow(window,dot); Authority(1); // No wait for the visual response.
        await Reveal(window,dot); await StableReachable(window,dot);
        KeyNow(window,PhysicalKey.Enter); Authority(2);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(dot)).Invoke();
        await Until(()=>match.CurrentInnings.LegalBallsBowled==before+3,"Native Invoke must accept one more dot ball.");
        Authority(3); await Settle(650); Authority(3);
        Assert.Equal("Dot ball",ControlAutomationPeer.CreatePeerForElement(dot)?.GetName());
    }
}
