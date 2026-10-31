using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(RocheScoreboard.Mac.Tests.TestApp))]
// The production application has one dispatcher/font manager for its lifetime.
// Its process-wide preference listeners must not outlive a per-test dispatcher.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace RocheScoreboard.Mac.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", Path.Combine(Path.GetTempPath(), "roche-production-tests-" + Environment.ProcessId));
        // Hosted macOS images enable system Reduce Motion. Keep headless motion
        // fixtures deterministic without modifying that setting or overriding
        // OperatorMotion itself: app-level Reduce Motion must still take effect.
        Console.WriteLine($"Headless system animation baseline: {RocheScoreboard.Mac.Native.NativeSystemPreferences.ClientAreaAnimation}; in-process test preference: True");
        RocheScoreboard.Mac.Native.NativeSystemPreferences.ClientAreaAnimationForTests = true;
        return AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia().WithInterFont();
    }
}

public sealed class WindowTests
{
    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Click(Window window, string name)
    {
        Named<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        string dir = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "roche-mac-captures");
        Directory.CreateDirectory(dir);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(System.IO.Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public void ControlButtonsUpdateTheIndependentOutputAndItCanBeReopened()
    {
        var session = new DemoSession { ReducedMotion = true };
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var control = new ControlWindow(session, new SessionStore(System.IO.Path.Combine(dir, "session.json")));
        try
        {
            control.Show(); Dispatcher.UIThread.RunJobs();
            Click(control, "HomeGoal"); Click(control, "AwayBehind");
            Assert.Equal(6, session.Afl.HomeTotal); Assert.Equal(1, session.Afl.AwayTotal);
            var output = new OutputWindow(session); output.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("6", Named<TextBlock>(output, "OutputHomeScore").Text);
                session.Afl.AddGoal(TeamSide.Away);
                Assert.Equal("7", Named<TextBlock>(output, "OutputAwayScore").Text);
                Click(control, "UndoScore");
                Assert.Equal("1", Named<TextBlock>(output, "OutputAwayScore").Text);
                session.Celebrate(""); session.Announce(""); output.RefreshClock();
                Capture(control, "afl-controls"); Capture(output, "afl-output");
                output.Width = 480; output.Height = 300; Capture(output, "compact-output");
            }
            finally { output.Close(); }
            Click(control, "OpenOutput");
            session.LabelOverride = "CUSTOM";
            Click(control, "StartClock"); Assert.True(session.Afl.ClockRunning);
            Assert.Equal("CUSTOM", session.QuarterLabel);
            Click(control, "PauseClock"); Assert.False(session.Afl.ClockRunning);
            Click(control, "EndQuarter"); Assert.Equal(2, session.Afl.Quarter);
            Click(control, "ContinueQuarter"); Assert.Equal(1, session.Afl.Quarter);
        }
        finally { control.Close(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void AllSportsAndPresentationScreensRender()
    {
        var session = new DemoSession(); var output = new OutputWindow(session);
        output.Show();
        try
        {
            foreach (var sport in Enum.GetValues<DemoSport>())
            {
                session.SwitchSport(sport);
                if (sport == DemoSport.Cricket)
                {
                    session.Cricket.SetNonStriker(1); session.Cricket.SelectNewBatter(0); session.Cricket.ConfirmBowler(0);
                    session.Cricket.RecordDelivery(4, CricketDeliveryType.Four);
                }
                if (sport == DemoSport.Training) session.Training.AddTimer(new TrainingTimer("Warmup", TimeSpan.FromMinutes(5)));
                session.Celebrate("");
                foreach (var screen in Enum.GetValues<Presentation>())
                {
                    session.Screen = screen; output.RefreshPresentation();
                    Capture(output, $"{sport}-{screen}");
                }
            }
        }
        finally { output.Close(); }
    }

    [AvaloniaFact]
    public void CricketAndTrainingControlsDriveTheSharedModels()
    {
        var session = new DemoSession(); session.SwitchSport(DemoSport.Cricket);
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var control = new ControlWindow(session, new SessionStore(System.IO.Path.Combine(dir, "session.json")));
        control.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Click(control, "ConfirmBatters"); Click(control, "ConfirmBowler"); Click(control, "CricketRuns4");
            Assert.Equal(4, session.Cricket.TotalRuns);
            Click(control, "UndoDelivery"); Assert.Equal(0, session.Cricket.TotalRuns);
            Capture(control, "cricket-controls");
            session.SwitchSport(DemoSport.Training); Dispatcher.UIThread.RunJobs();
            Click(control, "AddTimer"); Assert.Single(session.Training.Timers);
            Click(control, "TrainingToggle"); Assert.True(session.Training.IsRunning);
            Click(control, "TrainingToggle"); Assert.False(session.Training.IsRunning);
            Capture(control, "training-controls");
            control.Width = 760; control.Height = 560; Capture(control, "compact-controls");
        }
        finally { control.Close(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void TypingInTeamFieldsDoesNotTriggerScoringShortcuts()
    {
        var session = new DemoSession();
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var control = new ControlWindow(session, new SessionStore(System.IO.Path.Combine(dir, "session.json")));
        control.Show();
        try
        {
            var tabs = control.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var input = control.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
            Assert.True(input.Focus());
            control.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None); control.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
            Assert.Equal(0, session.Afl.HomeTotal);
            Named<Button>(control, "OpenOutput").Focus();
            control.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None); control.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
            Assert.Equal(6, session.Afl.HomeTotal);
        }
        finally { control.Close(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
