using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaFact][Trait("Evidence", "QuarterBreakActualPendingDelay")]
    public async Task QuarterPreferenceAppliesToFutureEndsWhileRealStartRetiresCurrentPendingDelay()
    {
        await using var f = await AflFixture.Create(1280, 850);
        await OpenBreakCardSection(f, true);
        var toggle = Named<CheckBox>(f.Window, "SettingsBreakScreenToggle");
        await RevealBreakCardActor(f, toggle);
        var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        if (toggle.IsChecked != true) provider.Toggle();
        Assert.True(BreakCardValue(f));
        int home = f.Match.HomeTotal, away = f.Match.AwayTotal;
        var scores = f.Match.Events.ToArray();

        await Mr4Navigate(f.Window, PhysicalKey.Digit1);
        await Tap(f.Window, Named<Button>(f.Window, "StartPauseButton"));
        Assert.True(f.Match.ClockRunning);
        await Tap(f.Window, Named<Button>(f.Window, "EndQtrButton"));
        Assert.False(f.Match.ClockRunning);
        Assert.Equal(2, f.Match.Quarter);
        var pending = Assert.IsType<DispatcherTimer>(QbXField<DispatcherTimer?>(f.Window, "_breakScreenDelayTimer"));
        Assert.True(pending.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(45), pending.Interval);
        string ended = JsonSerializer.Serialize(f.Match.ToState());

        await Mr4Navigate(f.Window, PhysicalKey.Digit4);
        await OpenBreakCardSection(f, true);
        await RevealBreakCardActor(f, toggle);
        provider.Toggle();
        Assert.False(toggle.IsChecked);
        Assert.False(BreakCardValue(f));
        Assert.False(OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
        Assert.Same(pending, QbXField<DispatcherTimer?>(f.Window, "_breakScreenDelayTimer"));
        Assert.True(pending.IsEnabled); // Existing future-quarter preference semantics.
        Assert.Equal(ended, JsonSerializer.Serialize(f.Match.ToState()));

        await Mr4Navigate(f.Window, PhysicalKey.Digit1);
        await Tap(f.Window, Named<Button>(f.Window, "StartPauseButton"));
        Assert.True(f.Match.ClockRunning);
        Assert.Equal(2, f.Match.Quarter);
        Assert.Null(QbXField<DispatcherTimer?>(f.Window, "_breakScreenDelayTimer"));
        Assert.False(pending.IsEnabled);
        await Settle(650);
        Assert.Null(QbXField<DispatcherTimer?>(f.Window, "_breakScreenDelayTimer"));
        Assert.False(pending.IsEnabled);
        Assert.False(QbXField<bool>(f.Window, "_isBreakRotating"));
        Assert.Equal(home, f.Match.HomeTotal);
        Assert.Equal(away, f.Match.AwayTotal);
        Assert.Equal(scores, f.Match.Events.ToArray());
        // No private Tick call, shortened interval or45-second passage is
        // claimed. This verifies an actual pending owner and real Start cleanup.
        f.Match.PauseClock();
        Capture(f.Window, "quarter-break-pending-owner-retired-by-start");
    }
}
