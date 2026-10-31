// OUTSIDE SUPPLEMENT DRAFT ONLY: two cases, uncompiled/unrun. Root owns integration.
// Uses existing partial fixture/MR6 helpers. Public CountUp Start and original
// Toggle provider, no private handler/timer invocation, visibility forcing or model writes.
using System;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","MR6RealRunningStoppageToggle")]
    public async Task OriginalStoppageOffPausesOnlyTheLiveToolAndOnDoesNotRestartIt(bool reduced)
    {
        await using var f=await AflFixture.Create(1280,850);
        await OpenClockSection(f,reduced);
        Mr6Mode(f,true);await Settle(400);
        Assert.Equal(ClockMode.CountUp,f.Match.ClockMode);
        var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
        await Mr6Reveal(f,toggle);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        if(toggle.IsChecked!=true)provider.Toggle();
        Assert.True(toggle.IsChecked);Assert.True(Mr6Read<bool>(f,"_showControlPanelCountdown"));
        Assert.True(OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Assert.False(f.Match.ClockRunning);Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        Assert.True(f.Match.ElapsedInQuarter.TotalSeconds<2,"Must start a genuinely new quarter through the original Start route.");
        var mainDuration=f.Match.QuarterDuration;
        var toolDuration=Mr6Read<TimeSpan>(f,"_controlPanelCountdownDuration");
        var toolBeforeStart=Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining");
        Assert.True(toolBeforeStart>TimeSpan.FromSeconds(5));
        string nonClock=Mr4NonClockState(f.Match);int scoreEvents=0;
        f.Match.ScoreEventAdded+=_=>scoreEvents++;

        await Mr4Navigate(f.Window,PhysicalKey.Digit1);
        await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
        Assert.True(f.Match.ClockRunning,"Real Start must accept immediately.");
        Assert.True(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        // Witness BOTH actual stopwatches. A running flag alone would miss a
        // disconnected rendering/tick route and would not substantiate this test.
        await Until(()=>f.Match.ElapsedInQuarter.TotalMilliseconds>=1100
            &&Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining")<toolBeforeStart,
            "Real CountUp Start must advance the match and decrement the stoppage tool through normal rendering.");
        Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(0,scoreEvents);
        Assert.Equal(mainDuration,f.Match.QuarterDuration);Assert.Equal(toolDuration,Mr6Read<TimeSpan>(f,"_controlPanelCountdownDuration"));

        await Mr4Navigate(f.Window,PhysicalKey.Digit4);
        await OpenClockSection(f,reduced);await Mr6Reveal(f,toggle);
        Assert.True(f.Match.ClockRunning);Assert.True(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        Assert.Equal(ToggleState.On,provider.ToggleState);
        int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
        // Sample AFTER all asynchronous navigation/reveal work. No await or
        // forced frame occurs between this sample and native Toggle dispatch,
        // so a legitimate earlier running tick is not misclassified as reset.
        var remainingAtOff=Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining");
        var elapsedAtOff=f.Match.ElapsedInQuarter;
        provider.Toggle();
        Assert.False(toggle.IsChecked);Assert.Equal(ToggleState.Off,provider.ToggleState);Assert.Equal(1,changes);
        Assert.False(Mr6Read<bool>(f,"_showControlPanelCountdown"));
        Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        Assert.Equal(DateTime.MinValue,Mr6Read<DateTime>(f,"_countdownNextTick"));
        Assert.False(OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Assert.True(f.Match.ClockRunning,"Disabling a display/tool preference must not pause the scoreboard CountUp clock.");
        Assert.True(f.Match.ElapsedInQuarter>=elapsedAtOff);
        Assert.Equal(remainingAtOff,Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining"));
        Assert.Equal(toolDuration,Mr6Read<TimeSpan>(f,"_controlPanelCountdownDuration"));
        Assert.Equal(mainDuration,f.Match.QuarterDuration);Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(0,scoreEvents);

        // Re-enable immediately, while the match remains running. The preference
        // supplies availability; it is not a synthetic Resume/Start action.
        provider.Toggle();
        Assert.True(toggle.IsChecked);Assert.Equal(ToggleState.On,provider.ToggleState);Assert.Equal(2,changes);
        Assert.True(Mr6Read<bool>(f,"_showControlPanelCountdown"));
        Assert.True(OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        Assert.Equal(DateTime.MinValue,Mr6Read<DateTime>(f,"_countdownNextTick"));
        Assert.True(f.Match.ClockRunning);Assert.Equal(remainingAtOff,Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining"));
        var elapsedAfterOn=f.Match.ElapsedInQuarter;
        await Settle(1350); // Beyond the production1s tool gate, with ordinary renderer pumping.
        Assert.True(f.Match.ClockRunning);Assert.True(f.Match.ElapsedInQuarter>elapsedAfterOn+TimeSpan.FromMilliseconds(500));
        Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
        Assert.Equal(remainingAtOff,Mr6Read<TimeSpan>(f,"_controlPanelCountdownRemaining"));
        Assert.Equal(toolDuration,Mr6Read<TimeSpan>(f,"_controlPanelCountdownDuration"));
        Assert.Equal(mainDuration,f.Match.QuarterDuration);Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(0,scoreEvents);
        Assert.Equal(2,changes);Assert.True(OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Capture(f.Window,$"mr6-real-running-toggle-reduce-{reduced}");
    }
}
