using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class NativeProductionTests
{
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="getenv")]
    private static extern nint NativeEnvironment([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string name);

    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name)
    {
        string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? "/tmp/roche-mac-captures";
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
    [AvaloniaFact]
    public async Task LauncherRetainsEntryMotionChangelogAndSingleSelectionWhenClickedTwice()
    {
        var launcher = new SportSelectionScreen();
        var window = new Window { Width = 1280, Height = 800, Content = launcher };
        var selections = new List<SportMode>();
        launcher.SportSelected += mode=>{selections.Add(mode);launcher.IsVisible=false;};
        try
        {
            window.Show();
            await Settle(1700);
            var card = launcher.FindControl<Border>("AflCard")!;
            Assert.True(card.Opacity > .99);
            var label = launcher.FindControl<TextBlock>("SettingsActionCaption")!;
            Assert.True(label.Bounds.Height > 0, "Launcher settings text must retain a readable native font.");
            Capture(window, "production-launcher");
            var changelog = launcher.FindControl<Button>("ChangelogToggle")!;
            changelog.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle(350);
            Assert.True(launcher.FindControl<Grid>("ChangelogOverlay")!.IsVisible);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Settle(250);
            Assert.False(launcher.FindControl<Grid>("ChangelogOverlay")!.IsVisible);
            var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await Settle(650);
            Assert.Equal(new[] { SportMode.AFL }, selections);
            launcher.IsVisible=true;launcher.PrepareForDisplay();
            Assert.True(launcher.IsHitTestVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task OriginalWizardCompletesAllThreeStepsAndKeepsTheConfiguredClock()
    {
        var wizard = new SetupWizard();
        var window = new Window { Width = 1280, Height = 900, Content = wizard };
        SetupResult? result = null;
        wizard.SetupCompleted += value => result = value;
        async Task Click(string name)
        {
            var target = wizard.FindControl<Control>(name)!;
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await Settle(850);
        }
        try
        {
            window.Show(); await Settle(1400);
            wizard.FindControl<TextBox>("SetupHomeNameBox")!.Text = "Old Xaverians";
            wizard.FindControl<TextBox>("SetupAwayNameBox")!.Text = "Old Caulfield";
            Capture(window, "production-afl-setup-teams");
            await Click("NextButton");
            Capture(window, "production-afl-setup-clock");
            await Click("NextButton");
            Capture(window, "production-afl-setup-messages");
            await Click("NextButton");
            Assert.NotNull(result);
            Assert.Equal("Old Xaverians", result.HomeName);
            Assert.Equal("Old Caulfield", result.AwayName);
            Assert.Equal(20, result.QuarterMinutes);
            wizard.Reset();
            Assert.Equal("Old Xaverians", wizard.FindControl<TextBox>("SetupHomeNameBox")!.Text); // Reset restores the last-used club, as production does.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ProductionConsoleConstructsAllEmbeddedScreensAndKeepsItsOriginalOperatorModeChecks()
    {
        var app = (App)Application.Current!;
        app.IsSelfTestMode = true;
        Roche_Scoreboard.MainWindow? window = null;
        try
        {
            window = new Roche_Scoreboard.MainWindow();
            window.Show(); await Settle(1200);
            Assert.True(window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible);
            var checks = window.RunOperatorModeSelfCheckForQa();
            Assert.All(checks, check => Assert.True(check.Value, check.Key));
            Capture(window, "production-console-launcher");
            Assert.NotNull(window.FindControl<CricketControlPanel>("CricketControlPanelView"));
            Assert.NotNull(window.FindControl<TrainingControlPanel>("TrainingControlPanelView"));
        }
        finally { window?.Close(); app.IsSelfTestMode = false; }
    }

    [AvaloniaFact]
    public async Task ProductionAflFlowKeepsScoringUndoClockAndItsIndependentOutputConnected()
    {
        var app = (App)Application.Current!; app.IsSelfTestMode = true;
        var window = new Roche_Scoreboard.MainWindow();
        async Task Tap(Control target, int wait = 400)
        {
            target.BringIntoView(); await Settle(100);
            Assert.True(target.IsEffectivelyVisible, target.Name + " must be visible before pointer activation");
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await Settle(wait);
        }
        void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show(); await Settle(1600);
            var launcher = window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;
            await Tap(launcher.FindControl<Border>("AflCard")!, 1000);
            var wizard = window.FindControl<SetupWizard>("SetupWizardPanel")!;
            await Tap(wizard.FindControl<Control>("NextButton")!, 300); // Sport is step one of the unified wizard.
            wizard.FindControl<TextBox>("SetupHomeNameBox")!.Text = "Old Xaverians";
            wizard.FindControl<TextBox>("SetupAwayNameBox")!.Text = "Old Caulfield";
            await Tap(wizard.FindControl<Control>("NextButton")!, 700);
            await Tap(wizard.FindControl<Control>("NextButton")!, 700);
            wizard.FindControl<ComboBox>("SetupWeatherLocation")!.Text = "";
            await Tap(wizard.FindControl<Control>("NextButton")!, 900);
            Assert.True(window.FindControl<ScrollViewer>("MainContent")!.IsVisible);
            Click("HomeGoalButton"); Click("AwayBehindButton"); await Settle(300);
            Assert.Equal("1.0.6", window.FindControl<TextBlock>("HomeScoreDisplay")!.Text);
            Assert.Equal("0.1.1", window.FindControl<TextBlock>("AwayScoreDisplay")!.Text);
            Capture(window, "production-afl-console-essentials");
            Click("UndoButton"); await Settle(400);
            Assert.Equal("0.0.0", window.FindControl<TextBlock>("AwayScoreDisplay")!.Text);
            Click("StartPauseButton"); await Settle(1150);
            Click("StartPauseButton"); await Settle(50);
            Assert.DoesNotContain("00:00", window.FindControl<TextBlock>("HeaderQuarterClock")!.Text);
            Click("GuidedModeButton"); await Settle(100);
            var tabs = window.FindControl<TabControl>("MainTabControl")!;
            for (int index = 0; index < 4; index++)
            {
                tabs.SelectedIndex = index; await Settle(150);
                Capture(window, "production-afl-console-tab-" + index);
            }
        }
        finally { window.Close(); await Settle(150); app.IsSelfTestMode = false; }
    }
    [AvaloniaFact]
    public async Task ProductionTrainingControlsRetainTimerEditingAndTextFieldShortcutGuards()
    {
        var session = new TrainingSession();
        var controls = new TrainingControlPanel(); controls.Bind(session);
        var output = new TrainingScorebugControl(); output.Bind(session);
        var window = new Window { Width = 1280, Height = 850, Content = controls };
        var display = new Window { Width = 707, Height = 420, Content = output };
        void Click(string name) => controls.FindControl<Button>(name)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show(); display.Show(); await Settle(200);
            controls.FindControl<TextBox>("NewTitleBox")!.Text = "Warm-up";
            controls.FindControl<TextBox>("NewMinutesBox")!.Text = "2";
            controls.FindControl<TextBox>("NewSecondsBox")!.Text = "30";
            Click("AddTimerButton"); Assert.Single(session.Timers);
            Assert.Equal(TimeSpan.FromSeconds(150), session.Timers[0].Duration);
            Click("AddTenSecButton"); Assert.Equal(TimeSpan.FromSeconds(160), session.ActiveRemaining);
            controls.FindControl<TextBox>("NewTitleBox")!.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(session.IsRunning);
            Click("StartPauseButton"); Assert.True(session.IsRunning);
            Click("StartPauseButton"); Assert.False(session.IsRunning);
            await Settle(150); Capture(window, "production-training-console"); Capture(display, "production-training-output");
        }
        finally { window.Close(); display.Close(); }
    }

#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip = "Build with ROCHE_NATIVE_MEDIA=1 and a native LibVLC runtime to run decoded-video integration.")]
#endif
    public async Task ProductionVideoDecodesIntoTheCompositedOutputAndRetainsItsPlaybackControls()
    {
        var view = new VideoPlayerControl { Loop = false };
        var window = new Window { Width = 480, Height = 270, Content = view };
        try
        {
            window.Show(); await Settle(150);
            if(OperatingSystem.IsMacOS() && Roche_Scoreboard.Services.VlcRuntime.IsAvailable)
                foreach(string name in new[]{"VLC_PLUGIN_PATH","VLC_DATA_PATH"})
                    Assert.Equal(Environment.GetEnvironmentVariable(name),System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeEnvironment(name)));
            view.SetVideoSource(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-pattern.mp4"));
            view.Play();
            var surface = view.FindControl<NativeVideoSurface>("VideoPlayer")!;
            for (int attempt = 0; attempt < 40 && surface.FramesPresented == 0; attempt++) await Settle(100);
            Assert.True(surface.FramesPresented > 0, "VLC must deliver a real decoded frame to the output compositor.\n"+
                string.Join("\n",Roche_Scoreboard.Services.VideoDiagnostics.RecentEntries().Take(16).Select(entry=>$"{entry.Category}: {entry.Message} {entry.ExceptionText}")));
            Capture(window, "production-video-decoded");
            Assert.True(view.HasSource); Assert.True(view.IsPlaying);
            view.Pause(); await Settle(200); Assert.False(view.IsPlaying);
            view.IsMuted = true; Assert.True(view.IsMuted);
            view.Volume = .25; Assert.Equal(.25, view.Volume, 2);
            view.SpeedRatio = 1.5; Assert.Equal(1.5, view.SpeedRatio, 2);
            view.Seek(TimeSpan.FromSeconds(2)); view.Play(); await Settle(500);
            Assert.True(view.Position.TotalSeconds >= 1.8);
            view.Stop(); await Settle(150); Assert.False(view.IsPlaying);
        }
        finally
        {
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {} directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllLines(Path.Combine(directory,"production-video-diagnostics.txt"),Roche_Scoreboard.Services.VideoDiagnostics.RecentEntries().Select(entry=>$"{entry.TimestampUtc:O} {entry.Category}: {entry.Message} {entry.ExceptionText}"));
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NativeTimelinesKeepSequentialSegmentsAndClearBackToLocalValue()
    {
        var transform = new Avalonia.Media.TranslateTransform { X = 12 };
        var board = new Storyboard();
        var first = new DoubleAnimation(0, 100, TimeSpan.FromMilliseconds(100));
        var second = new DoubleAnimation(100, 200, TimeSpan.FromMilliseconds(100)) { BeginTime = TimeSpan.FromMilliseconds(150) };
        foreach (var child in new[] { first, second })
        {
            Storyboard.SetTarget(child, transform);
            Storyboard.SetTargetProperty(child, new PropertyPath(Avalonia.Media.TranslateTransform.XProperty));
            board.Children.Add(child);
        }
        try
        {
            board.Begin();
            Motion.SampleAt(transform,TimeSpan.FromMilliseconds(125));
            Assert.InRange(transform.X, 99.9, 100.1);
            Motion.SampleAt(transform,TimeSpan.FromMilliseconds(300));
            Assert.InRange(transform.X, 199.9, 200.1);
        }
        finally { board.Stop(); }
        Assert.Equal(12, transform.X);
    }
    [AvaloniaTheory]
    [InlineData(GoalAnimationStyle.Broadcast)]
    [InlineData(GoalAnimationStyle.Electric)]
    [InlineData(GoalAnimationStyle.Cinematic)]
    [InlineData(GoalAnimationStyle.Clean)]
    [InlineData(GoalAnimationStyle.Classic)]
    [InlineData(GoalAnimationStyle.CustomVideo)]
    public async Task OriginalGoalRoutinesRevealTheScoreAndCompleteExactlyOnce(GoalAnimationStyle style)
    {
        var view = new ScorebugControl();
        var window = new Window { Width = 707, Height = 420, Content = view };
        try
        {
            window.Show();
            view.SetLayout(ScorebugLayout.Distance);
            view.SetTeams("Old Xaverians", "OX", "Old Caulfield", "OC");
            view.SetTeamColors("#123456", "#9A1821", "#FFFFFF", "#FFFFFF");
            view.SetScores(0, 0, 0, 0);
            view.SetGoalAnimationStyle(style);
            await Settle(60);
            int completions = 0;
            view.PlayScoreAnimation(true, true, 1, 0, 6, () => completions++);
            Assert.True(view.IsScoreAnimating);
            await Settle(650);
            Capture(window, "production-goal-" + style);
            await Settle(6500);
            Assert.False(view.IsScoreAnimating);
            Assert.Equal("6", view.FindControl<TextBlock>("ExpHomeTotalText")!.Text);
            Assert.Equal(1, completions);
            Assert.Contains("score=1.0.6/0.0.0", view.GetCoreStateDiagnosticsForQa());
            view.SetLayout(ScorebugLayout.Expanded);
            Capture(window, "production-classic-" + style);
        }
        finally { view.ClearOverlayQueue(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task SourceOverlayTransitionsTickerAndScoreCorrectionsRemainInterruptible()
    {
        var view = new ScorebugControl();
        var window = new Window { Width = 386, Height = 193, Content = view };
        try
        {
            window.Show(); view.SetLayout(ScorebugLayout.Distance);
            view.SetTeams("Home", "HOM", "Away", "AWA");
            view.SetScores(1, 2, 0, 1);
            int completed = 0;
            view.AnimateScoreCorrection(2, 3, 1, 1, () => completed++);
            await Settle(500);
            Assert.Equal(1, completed);
            Assert.Equal("15", view.FindControl<TextBlock>("ExpHomeTotalText")!.Text);
            view.SetMarqueeMessages(new[] { new MarqueeMessage("WELCOME", "#FFFF00", "#123456") });
            await Settle(650);
            Assert.True(view.IsTickerActive);
            view.ShowTeamDrought("Home", "HOM", "Goal", TimeSpan.FromMinutes(20), "#123456");
            Assert.False(view.IsOverlayActive); // Original ticker has priority over informational cards.
            view.SetMarqueeMessages(Array.Empty<MarqueeMessage>());
            await Settle(700);
            view.ShowTeamDrought("Home", "HOM", "Goal", TimeSpan.FromMinutes(20), "#123456");
            await Settle(1000);
            Assert.True(view.IsOverlayActive);
            Capture(window, "production-team-drought");
            view.ClearOverlayQueue();
            await Settle(50);
            Assert.False(view.IsOverlayActive);
            view.SetMarqueeMessages(Array.Empty<MarqueeMessage>());
            Assert.False(view.IsTickerActive);
        }
        finally { view.ClearOverlayQueue(); window.Close(); }
    }
}

