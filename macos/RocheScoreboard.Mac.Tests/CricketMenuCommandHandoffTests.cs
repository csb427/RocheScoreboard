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
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","CricketMenuCommandHandoff")]
    public async Task CommandBarRetiresTheRealPlayerMenuAndReturnsToItsCurrentLauncher(int width,int height)
    {
        using var scope=new Scope();var seed=CricketReliabilityChecks.CreateMatch(true);
        GameStateService.Save(new GameState{Sport="cricket",CricketMatchState=seed.CapturePersistenceState()});
        var window=scope.Main();await Settle(600);
        var home=Named<SportSelectionScreen>(window,"SportSelectionPanel");home.PrepareForDisplay();
        var resume=Assert.Single(home.GetVisualDescendants().OfType<Button>(),b=>b.Name=="HomeResumeButton");await StableReachable(window,resume);await Tap(window,resume);
        var prompt=Assert.Single(window.GetVisualDescendants().OfType<ResumeMatchPrompt>());var accept=Named<Button>(prompt,"ResumeButton");await StableReachable(window,accept);await Tap(window,accept);
        var panel=Named<CricketControlPanel>(window,"CricketControlPanelView");
        await Until(()=>panel.IsEffectivelyVisible&&!Named<Control>(window,"RecoveryOverlay").IsVisible,"Actual recovery must enter Cricket.");
        window.Width=width;window.Height=height;await Settle(600);
        var match=Read<CricketMatchManager>(window,"_cricketMatch");string state=State(match);int changes=0;match.MatchChanged+=()=>changes++;
        Assert.False(Read<Window?>(window,"_cricketDisplayWindow")?.IsVisible==true);
        for(int cycle=0;cycle<2;cycle++)
        {
            var anchor=Anchor(panel,"bowler");var menu=await Open(window,panel,"bowler");
            // Public command entry: exercises real ownership/disable/focus, not
            // a private menu callback or fabricated accepted prompt result.
            if(cycle==0)Assert.True(window.OpenCommandBar());
            else
            {
                menu.Root.KeyPressQwerty(PhysicalKey.K,RawInputModifiers.Control);
                menu.Root.KeyReleaseQwerty(PhysicalKey.K,RawInputModifiers.Control);
                await Settle(100);
                Assert.True(Named<Control>(window,"CommandOverlay").IsVisible,"Ctrl+K from the actual popup input root must open the command bar.");
            }
            Assert.False(menu.Popup.IsOpen,"A floating player menu must retire before the command bar owns input.");
            Retired(panel,menu.Popup);Assert.True(Named<Control>(window,"CommandOverlay").IsVisible);
            Assert.Same(Named<Control>(window,"CommandSearch"),window.FocusManager?.GetFocusedElement());
            KeyNow(window,PhysicalKey.Escape);await Settle(200);
            Assert.False(Named<Control>(window,"CommandOverlay").IsVisible);Assert.Same(anchor,window.FocusManager?.GetFocusedElement());
            Assert.Equal(state,State(match));Assert.Equal(0,changes);Assert.False(Read<Window?>(window,"_cricketDisplayWindow")?.IsVisible==true);
        }
    }
}
