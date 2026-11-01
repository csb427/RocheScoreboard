using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>Actual AFL resume button and native owned OK/Cancel dialog; no message-box hook.</summary>
public sealed class AflContinueQuarterSafetyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) =>
        owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    private static string Snapshot(MatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class Fixture : IDisposable
    {
        internal readonly App App = Assert.IsType<App>(Application.Current);
        internal readonly Roche_Scoreboard.MainWindow Window;
        private readonly bool _previousSelfTest;
        private readonly AppTheme _previousTheme;
        private readonly UiDensity _previousDensity;
        private readonly bool? _previousMotion = OperatorMotion.AnimationOverride;
        private readonly string? _previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string _dataHome = Path.Combine(Path.GetTempPath(), "roche-afl-continue-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> _files = new();

        internal MatchManager Match => Field<MatchManager>(Window, "_match");
        internal Button Continue => Assert.IsAssignableFrom<Button>(Window.FindControl<Button>("ContinueQtrButton"));
        internal int Changes;

        internal Fixture()
        {
            _previousSelfTest = App.IsSelfTestMode;
            _previousTheme = App.ThemeService.CurrentTheme;
            _previousDensity = App.ThemeService.CurrentDensity;
            App.IsSelfTestMode = true;
            OperatorMotion.AnimationOverride = false;
            Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", _dataHome);

            string workflow = (string)typeof(OperatorWorkflowPreferences)
                .GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
            foreach (string path in new[]
            {
                Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json"),
                Path.Combine(SelfTestStorage.DocumentsDirectory, "ux_preferences.json"),
                OperatorAppearance.StoragePath, workflow, GameStateService.RecoveryStatePath
            })
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    _files.TryAdd(path + suffix, File.Exists(path + suffix) ? File.ReadAllBytes(path + suffix) : null);

            // The production call must supply this actual owner. Observe its real
            // native dialog without changing the initialized application's lifetime.
            Window = new Roche_Scoreboard.MainWindow();
            Window.Show();
        }

        internal async Task Ready()
        {
            await Settle(180);
            Window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible = false;
            Window.FindControl<Control>("MainContent")!.IsVisible = true;
            Set(Window, "_aflMatchReady", true);
            Set(Window, "_sportMode", SportMode.AFL);
            Set(Window, "_breakScreenEnabled", false);
            Match.SetTeams("Home club", "HOM", "Away club", "AWY");
            Match.SetElapsed(TimeSpan.FromSeconds(71));
            Match.AddGoal(TeamSide.Home);
            Match.AddBehind(TeamSide.Away);
            Click(Assert.IsAssignableFrom<Button>(Window.FindControl<Button>("EndQtrButton")));
            await Settle(350);
            Assert.Equal(2, Match.Quarter);
            Assert.True(Match.CanContinueQuarter);
            Assert.False(Match.ClockRunning);
            Assert.True(Continue.IsEffectivelyVisible);
            GameStateService.Save(new GameState { Sport = "afl", MatchState = Match.ToState() });
            Assert.True(File.Exists(GameStateService.RecoveryStatePath), "Use an actual isolated recovery fixture, not absent-file equality.");
            Match.MatchChanged += () => Changes++;
        }

        internal void Countdown(bool running, bool autoPaused)
        {
            Set(Window, "_controlPanelCountdownDuration", TimeSpan.FromSeconds(180));
            Set(Window, "_controlPanelCountdownRemaining", TimeSpan.FromSeconds(95));
            Set(Window, "_controlPanelCountdownRunning", running);
            Set(Window, "_controlPanelCountdownAutoPausedClock", autoPaused);
            // Keep legitimate wall-clock rendering from decrementing this fixture while modal.
            Set(Window, "_countdownNextTick", DateTime.UtcNow.AddHours(1));
        }

        internal (bool Running, bool AutoPaused, TimeSpan Duration, TimeSpan Remaining) CountdownState() =>
            (Field<bool>(Window, "_controlPanelCountdownRunning"),
             Field<bool>(Window, "_controlPanelCountdownAutoPausedClock"),
             Field<TimeSpan>(Window, "_controlPanelCountdownDuration"),
             Field<TimeSpan>(Window, "_controlPanelCountdownRemaining"));

        internal Dictionary<string, byte[]?> RecoveryFiles() =>
            new[] { "", ".bak", ".tmp" }.ToDictionary(suffix => GameStateService.RecoveryStatePath + suffix,
                suffix => File.Exists(GameStateService.RecoveryStatePath + suffix)
                    ? File.ReadAllBytes(GameStateService.RecoveryStatePath + suffix) : null);

        public void Dispose()
        {
            try
            {
                foreach (var dialog in Window.OwnedWindows.ToArray()) dialog.Close();
                Window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                App.IsSelfTestMode = _previousSelfTest;
                OperatorMotion.AnimationOverride = _previousMotion;
                App.ThemeService.SetTheme(_previousTheme);
                App.ThemeService.SetDensity(_previousDensity);
                foreach (var (path, bytes) in _files)
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", _previousDataHome);
                if (Directory.Exists(_dataHome)) Directory.Delete(_dataHome, true);
            }
        }
    }

    private static void RespondToRealDialog(Fixture fixture, string action, Action<Window>? whileOpen = null)
    {
        bool opened = false;
        Exception? callbackError = null;
        var response = Dispatcher.UIThread.InvokeAsync(() =>
        {
            Window? dialog = null;
            try
            {
                dialog = Assert.Single(fixture.Window.OwnedWindows);
                opened = true;
                Assert.Same(fixture.Window, dialog.Owner);
                Assert.Equal("Resume Q1", dialog.Title);
                var explanation = Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("resume Quarter 1") == true);
                Assert.Contains("resume Quarter 1", explanation.Text);
                var button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(),
                    candidate => Equals(candidate.Content, action));
                whileOpen?.Invoke(dialog);
                Click(button);
            }
            catch (Exception error)
            {
                callbackError = error;
                if (dialog?.IsVisible == true) dialog.Close(NativeMessageBoxResult.Cancel);
                foreach (var owned in fixture.Window.OwnedWindows.ToArray()) owned.Close(NativeMessageBoxResult.Cancel);
            }
        }, DispatcherPriority.Background);
        try
        {
            Click(fixture.Continue);
        }
        finally
        {
            response.Abort();
        }
        Assert.True(opened, "The actual owned native quarter-resume dialog must open; no adapter result hook is used.");
        Assert.Null(callbackError);
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    private static void AssertFilesEqual(Dictionary<string, byte[]?> expected, Dictionary<string, byte[]?> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(path => path), actual.Keys.OrderBy(path => path));
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CancelKeepsMatchClockCountdownHistoryAndRecoveryUnchanged(bool running, bool autoPaused)
    {
        using var fixture = new Fixture();
        await fixture.Ready();
        fixture.Countdown(running, autoPaused);
        string before = Snapshot(fixture.Match);
        var countdown = fixture.CountdownState();
        var recovery = fixture.RecoveryFiles();
        bool saveQueued = Field<DispatcherTimer?>(fixture.Window, "_deferredGameStateSaveTimer")?.IsEnabled == true;

        RespondToRealDialog(fixture, "Cancel");

        Assert.Equal(before, Snapshot(fixture.Match));
        Assert.Equal(countdown, fixture.CountdownState());
        Assert.Equal(0, fixture.Changes);
        Assert.Equal(saveQueued, Field<DispatcherTimer?>(fixture.Window, "_deferredGameStateSaveTimer")?.IsEnabled == true);
        AssertFilesEqual(recovery, fixture.RecoveryFiles());
        Assert.Equal(2, fixture.Match.Quarter);
        Assert.Equal(1, fixture.Match.CompletedQuarterCount);
        Assert.True(fixture.Match.CanContinueQuarter);
        Assert.False(fixture.Match.ClockRunning);
    }

    [AvaloniaFact]
    public async Task ConfirmResumesOnlyTheCapturedQuarterExactlyOnce()
    {
        using var fixture = new Fixture();
        await fixture.Ready();
        fixture.Countdown(true, true);
        string history = JsonSerializer.Serialize(fixture.Match.Events);
        var recovery = fixture.RecoveryFiles();

        RespondToRealDialog(fixture, "OK");
        Click(fixture.Continue); // A repeated public action after continuation is ineligible.

        Assert.Equal(1, fixture.Match.Quarter);
        Assert.Equal(TimeSpan.FromSeconds(71), fixture.Match.ElapsedInQuarter);
        Assert.False(fixture.Match.ClockRunning);
        Assert.False(fixture.Match.CanContinueQuarter);
        Assert.Equal(0, fixture.Match.CompletedQuarterCount);
        Assert.Null(fixture.Match.GetQuarterSnapshot(1));
        Assert.Equal(history, JsonSerializer.Serialize(fixture.Match.Events));
        Assert.Equal(1, fixture.Changes);
        Assert.False(fixture.CountdownState().Running);
        Assert.False(fixture.CountdownState().AutoPaused);
        Assert.Equal(TimeSpan.FromSeconds(95), fixture.CountdownState().Remaining);
        AssertFilesEqual(recovery, fixture.RecoveryFiles()); // Self-test mode deliberately suppresses physical recovery saves.
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedStalePromptCannotResumeAReplacementMatchOrLaterQuarter(bool laterQuarter)
    {
        using var fixture = new Fixture();
        await fixture.Ready();
        var replacement = new MatchManager();
        replacement.SetTeams("Replacement home", "NEW", "Replacement away", "OTH");
        replacement.SetElapsed(TimeSpan.FromSeconds(43));
        replacement.AddGoal(TeamSide.Away);
        Assert.True(replacement.EndQuarter());
        if (laterQuarter)
        {
            replacement.SetElapsed(TimeSpan.FromSeconds(29));
            replacement.AddBehind(TeamSide.Home);
            Assert.True(replacement.EndQuarter());
        }
        Assert.True(replacement.CanContinueQuarter);
        string? currentContext = null;
        (bool Running, bool AutoPaused, TimeSpan Duration, TimeSpan Remaining) countdown = default;
        var recovery = fixture.RecoveryFiles();

        RespondToRealDialog(fixture, "OK", _ =>
        {
            // MainWindow retains a readonly MatchManager; loading a replacement is its real model API.
            fixture.Match.LoadState(replacement.ToState());
            fixture.Countdown(false, true);
            currentContext = Snapshot(fixture.Match);
            countdown = fixture.CountdownState();
            Assert.Equal(laterQuarter ? 3 : 2, fixture.Match.Quarter);
            Assert.Equal(1, fixture.Changes); // Only the independently loaded context changed so far.
        });

        Assert.NotNull(currentContext);
        Assert.Equal(currentContext, Snapshot(fixture.Match));
        Assert.Equal(countdown, fixture.CountdownState());
        Assert.Equal(1, fixture.Changes);
        Assert.Equal(laterQuarter ? 3 : 2, fixture.Match.Quarter);
        Assert.True(fixture.Match.CanContinueQuarter);
        AssertFilesEqual(recovery, fixture.RecoveryFiles());
    }

    [AvaloniaFact]
    public async Task RepeatedContinueWhilePromptIsOpenKeepsOneReceiptAndOneAdvance()
    {
        using var fixture = new Fixture();
        await fixture.Ready();
        string before = Snapshot(fixture.Match);
        RespondToRealDialog(fixture, "OK", dialog =>
        {
            Click(fixture.Continue);
            Assert.Same(dialog, Assert.Single(fixture.Window.OwnedWindows));
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Equal(0, fixture.Changes);
        });
        Assert.Equal(1, fixture.Changes);
        Assert.Equal(1, fixture.Match.Quarter);
        Assert.False(fixture.Match.CanContinueQuarter);
        Assert.Empty(fixture.Window.OwnedWindows);
    }
}
