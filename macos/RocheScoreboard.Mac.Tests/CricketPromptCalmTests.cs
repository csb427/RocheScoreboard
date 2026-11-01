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
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketPromptCalm")]
    public async Task SwitchingToCalmSettlesTheCurrentOrdinaryFormWithoutChangingItsDraft(bool closing)
    {
        PromptStorage();using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Panel(panel);await Settle(350);
        string before=State(match);int revision=match.DeliveryRevision;
        using var observation=new PromptReduceObservation(window,panel,"form",closing,()=>OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm));
        var opened=await PromptOpen(window,panel,"form",closing?null:observation);
        if(closing){await Settle(650);observation.ArmClosing(opened.Sheet,opened.Surface);EscapeNow(window);observation.GestureReturned();}
        await observation.AwaitOneObservation();var old=Assert.IsType<PromptObservedPose>(observation.Before);var current=Assert.IsType<PromptObservedPose>(observation.After);
        Assert.True(old.Mounted&&old.Loaded&&old.Visible&&old.Busy&&old.OutcomeNoteMounted);Assert.True(old.MotionEnabled);Assert.True(current.MotionEnabled);Assert.False(OperatorAppearance.ReduceMotion);Assert.Equal(OperatorMotionStyle.Calm,OperatorAppearance.MotionStyle);
        if(closing){Assert.False(old.Enabled);Assert.InRange(old.VeilOpacity,.001,.999999);Assert.False(current.Mounted);}else
        {
            Assert.True(Math.Abs(old.ShiftY)>.001||Math.Abs(old.ScaleY-1)>.001);PromptEndpoint(current);
            var note=Assert.Single(opened.Sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"outcome-note"));note.Text="Keep this pending outcome note";
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);await Settle(80);Assert.Equal("Keep this pending outcome note",note.Text);Assert.True(panel.HasActiveDecision);EscapeNow(window);
        }
        await PromptRetired(panel,opened.Sheet);Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        Record("prompt-calm-"+closing,new{before=old,after=current,observation.HandlersRetired});
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketPromptCalm")]
    public async Task SwitchingToCalmDuringAnAcceptedOutcomeCloseCompletesExactlyOnce()
    {
        PromptStorage();using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Panel(panel);await Settle(350);
        var opened=await PromptOpen(window,panel,"form");await Settle(650);var action=PromptAction(opened.Sheet,"no-result");await Reveal(window,action);await StableReachable(window,action);
        int changes=0;match.MatchChanged+=()=>++changes;
        using var observation=new PromptReduceObservation(window,panel,"form",true,()=>OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm));
        observation.ArmClosing(opened.Sheet,opened.Surface);TapNow(window,action);observation.GestureReturned();await observation.AwaitOneObservation();
        var old=Assert.IsType<PromptObservedPose>(observation.Before);var current=Assert.IsType<PromptObservedPose>(observation.After);Assert.False(old.Enabled);Assert.InRange(old.VeilOpacity,.001,.999999);Assert.False(current.Mounted);
        await Until(()=>match.IsMatchFinalized,"The selected original outcome must finish after settled close.");Assert.Equal(CricketMatchResultKind.NoResult,match.ResultKind);Assert.Equal(1,changes);string after=State(match);int revision=match.DeliveryRevision;
        OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);await Settle(650);Assert.Equal(after,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(1,changes);
        Record("prompt-calm-accepted-outcome",new{before=old,after=current});
    }
}
