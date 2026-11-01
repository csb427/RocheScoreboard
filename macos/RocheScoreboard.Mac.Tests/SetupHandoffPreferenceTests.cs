using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class AflSetupFinalsSemanticsTests
{
    private static async Task FinishAtHandoffBoundary(Fixture f,bool reduced)
    {
        // Real application preference rather than the fixture's animation
        // override, and actual pointer release on the original final command.
        OperatorMotion.AnimationOverride=null;
        OperatorAppearance.Configure(true,reduced);
        Assert.Equal(!reduced,OperatorMotion.Enabled);
        var finish=Named<Button>(f.Wizard,"NextButton"); await Reveal(f.Window,finish);
        var point=Painted(f.Window,finish).Center;
        f.Window.MouseMove(point); f.Window.MouseDown(point,MouseButton.Left); f.Window.MouseUp(point,MouseButton.Left);
        Assert.NotNull(f.Wizard.Result); Assert.True(Read<bool>(f.Window,"_aflMatchReady"));
        Assert.True(Named<Control>(f.Window,"MainContent").IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","SetupHandoffLiveReduce")]
    public async Task LiveReduceFinishesTheActualSetupHandoffImmediatelyWithoutReplacingTheMatch(int width,int height)
    {
        await Fixture.Run(width,height,false,false,async f=>
        {
            int completions=0,witnesses=0; f.Wizard.SetupCompleted+=_=>completions++;
            var content=Named<Control>(f.Window,"MainContent");
            bool beforeIncoming=false,beforeOutgoing=false,afterIncoming=true,afterOutgoing=true;
            object? match=null; string? state=null; Exception? observerError=null;
            void ObserveHandoff(object? sender,AvaloniaPropertyChangedEventArgs args)
            {
                if(args.Property!=Control.OpacityProperty || witnesses!=0
                    || Read<object?>(f.Window,"_aflSetupTransition") is null || !Motion.IsAnimating(content)) return;
                witnesses++;
                try
                {
                    // A public property notification from the actual incoming
                    // animation supplies the timing witness. The settings
                    // event is synchronous, so record both sides immediately;
                    // final pointer dispatch can outlast the whole transition.
                    beforeIncoming=Motion.IsAnimating(content);beforeOutgoing=Motion.IsAnimating(f.Wizard);
                    match=f.Match;state=MatchState(f);
                    OperatorAppearance.Configure(true,true);
                    afterIncoming=Motion.IsAnimating(content);afterOutgoing=Motion.IsAnimating(f.Wizard);
                }
                catch(Exception error){observerError=error;}
            }
            content.PropertyChanged+=ObserveHandoff;
            try { await FinishAtHandoffBoundary(f,false); }
            finally { content.PropertyChanged-=ObserveHandoff; }
            Assert.Null(observerError);Assert.Equal(1,witnesses);
            Assert.True(beforeIncoming);Assert.True(beforeOutgoing);
            Assert.False(afterIncoming);Assert.False(afterOutgoing);
            Assert.False(Motion.IsAnimating(content));Assert.False(Motion.IsAnimating(f.Wizard));
            Assert.Equal(1,content.Opacity);Assert.Equal(1,f.Wizard.Opacity);
            Assert.False(f.Wizard.IsVisible);Assert.True(f.Wizard.IsHitTestVisible);
            Assert.Null(Read<object?>(f.Window,"_aflSetupTransitionFallback"));
            Assert.Same(match,f.Match);Assert.Equal(state,MatchState(f));Assert.Equal(1,completions);
            Capture(f.Window,$"setup-handoff-reduced-{width}");
            await Settle(720); Assert.Same(match,f.Match); Assert.Equal(state,MatchState(f)); Assert.Equal(1,completions);
            Assert.False(f.Wizard.IsVisible); Assert.Equal(1,content.Opacity);
        });
    }

    [AvaloniaFact]
    [Trait("Evidence","SetupHandoffRetirement")]
    public async Task AlreadyReducedSetupCommitsOnceWithoutStartingAHandoffClock()
    {
        await Fixture.Run(420,320,true,false,async f=>
        {
            int completions=0; f.Wizard.SetupCompleted+=_=>completions++;
            await FinishAtHandoffBoundary(f,true);
            var content=Named<Control>(f.Window,"MainContent");
            Assert.False(Motion.IsAnimating(content)); Assert.False(Motion.IsAnimating(f.Wizard));
            Assert.False(f.Wizard.IsVisible); Assert.Equal(1,content.Opacity); Assert.Equal(1,completions);
            Assert.Null(Read<object?>(f.Window,"_aflSetupTransitionFallback"));
        });
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","SetupHandoffRetirement")]
    public async Task CompletedSetupCanReturnHomeOrCloseWithoutLateFallbackReappearance(bool close)
    {
        await Fixture.Run(1280,850,false,false,async f=>
        {
            await FinishAtHandoffBoundary(f,false);
            var content=Named<Control>(f.Window,"MainContent");
            // This observes completed setup navigation, not a claimed
            // native close during the active250ms compositor transition.
            Assert.Null(Read<object?>(f.Window,"_aflSetupTransition"));
            if(close) f.Window.Close();
            else { Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("home")); }
            Assert.False(Motion.IsAnimating(content)); Assert.False(Motion.IsAnimating(f.Wizard));
            Assert.Null(Read<object?>(f.Window,"_aflSetupTransitionFallback"));
            OperatorAppearance.Configure(true,true); await Settle(720);
            Assert.False(Motion.IsAnimating(content)); Assert.False(Motion.IsAnimating(f.Wizard));
            Assert.Equal(1,content.Opacity); Assert.Equal(1,f.Wizard.Opacity);
            if(close) Assert.False(f.Window.IsVisible);
            else { Assert.True(f.Home.IsEffectivelyVisible); Assert.False(content.IsVisible); }
        });
    }
}
