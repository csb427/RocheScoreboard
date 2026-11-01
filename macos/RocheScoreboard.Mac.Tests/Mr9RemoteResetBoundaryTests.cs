// Actual MainWindow / pointer Reset regressions with a controlled enqueue seam.
// Default public-constructor marshalling/deduplication are positive controls;
// only boundary scheduling is held. The queued callback is production code.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflResetScopeTests
{
    private static RemoteEvent Mr9Command(string type) => new()
    { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), EventType = type };

    private static MatchManager Mr9ExpectedCommand(MatchManager current, string type)
    {
        var expected = new MatchManager();
        expected.LoadState(current.ToState());
        switch (type)
        {
            case RemoteEventTypes.HomeGoal: expected.AddGoal(TeamSide.Home); break;
            case RemoteEventTypes.HomeBehind: expected.AddBehind(TeamSide.Home); break;
            case RemoteEventTypes.AwayGoal: expected.AddGoal(TeamSide.Away); break;
            case RemoteEventTypes.AwayBehind: expected.AddBehind(TeamSide.Away); break;
            case RemoteEventTypes.Undo: Assert.True(expected.UndoLastScore()); break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
        return expected;
    }

    private static void Mr9AssertExpectedScoringState(MatchManager expected, MatchManager actual, string type)
    {
        var state = expected.ToState();
        Assert.Equal(state.Events!.Count, actual.Events.Count);
        if (type != RemoteEventTypes.Undo)
        {
            // Expected and actual legitimate additions occur at different wall
            // times. Normalize ONLY the newly added event's wall timestamp;
            // retain exact earlier timestamps, game times and all score data.
            var score = state.Events[^1];
            state.Events[^1] = new ScoreEvent
            {
                Timestamp = actual.Events[^1].Timestamp,
                Quarter = score.Quarter, GameTime = score.GameTime,
                Team = score.Team, Type = score.Type,
                HomeGoals = score.HomeGoals, HomeBehinds = score.HomeBehinds,
                AwayGoals = score.AwayGoals, AwayBehinds = score.AwayBehinds
            };
        }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(state), Snapshot(actual));
    }

    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.HomeGoal)]
    [InlineData(RemoteEventTypes.HomeBehind)]
    [InlineData(RemoteEventTypes.AwayGoal)]
    [InlineData(RemoteEventTypes.AwayBehind)]
    [InlineData(RemoteEventTypes.Undo)]
    public async Task Mr9FiveScoringCommandsCannotCrossLoadStateOnTheSameManager(string type)
    {
        using var fixture = await Fixture.Create();
        var originalManager = fixture.Match;
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        var stale = Mr9Command(type);
        string original = Snapshot(fixture.Match);
        dispatcher.Dispatch(stale); dispatcher.Dispatch(stale);
        Assert.Single(callbacks); Assert.Equal(original, Snapshot(fixture.Match));
        int scoreAdded = 0;
        void OnScore(ScoreEvent _) => scoreAdded++;
        fixture.Match.ScoreEventAdded += OnScore;
        try
        {
            var loaded = new MatchManager();
            loaded.SetTeams("Loaded home", "LDH", "Loaded away", "LDA");
            loaded.AddGoal(TeamSide.Away); loaded.AddGoal(TeamSide.Away);
            loaded.AddBehind(TeamSide.Home); loaded.SetElapsed(TimeSpan.FromSeconds(19));
            string expectedLoaded = Snapshot(loaded);
            Assert.NotEqual(original, expectedLoaded);
            fixture.Match.LoadState(loaded.ToState()); // Actual public same-manager replacement boundary.
            Assert.Same(originalManager, fixture.Match);
            Assert.Equal(expectedLoaded, Snapshot(fixture.Match));
            Assert.DoesNotContain("LifecycleVersion", expectedLoaded, StringComparison.Ordinal);
            fixture.SaveRecovery();
            var recovery = fixture.Recovery();
            int changes = fixture.Changes;
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedLoaded, Snapshot(fixture.Match));
            Assert.Equal(3, fixture.Match.Events.Count);
            Assert.Equal(changes, fixture.Changes); Assert.Equal(0, scoreAdded);
            AssertRecovery(recovery, fixture.Recovery());

            // A fresh acceptance in this loaded lifetime still acts once; a
            // blanket score/Undo disable must not satisfy the rejection check.
            var expectedCurrent = Mr9ExpectedCommand(fixture.Match, type);
            var current = Mr9Command(type);
            dispatcher.Dispatch(current); dispatcher.Dispatch(current);
            Assert.Single(callbacks);
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks)); Dispatcher.UIThread.RunJobs();
            Mr9AssertExpectedScoringState(expectedCurrent, fixture.Match, type);
            Assert.Equal(changes + 1, fixture.Changes);
            Assert.Equal(type == RemoteEventTypes.Undo ? 0 : 1, scoreAdded);
            dispatcher.Dispatch(stale); dispatcher.Dispatch(current);
            Assert.Empty(callbacks); Mr9AssertExpectedScoringState(expectedCurrent, fixture.Match, type);
        }
        finally
        {
            try { Mr9DrainControlledQueue(callbacks); Dispatcher.UIThread.RunJobs(); }
            finally { fixture.Match.ScoreEventAdded -= OnScore; }
        }
    }

    [AvaloniaFact]
    public async Task Mr9QueuedGoalCannotMutateTheActualClosedOwner()
    {
        using var fixture = await Fixture.Create();
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        var command = Mr9Command(RemoteEventTypes.HomeGoal);
        bool closed = false;
        fixture.Window.Closed += (_, _) => closed = true;
        int scoreAdded = 0;
        void OnScore(ScoreEvent _) => scoreAdded++;
        fixture.Match.ScoreEventAdded += OnScore;
        try
        {
            string before = Snapshot(fixture.Match);
            dispatcher.Dispatch(command); Assert.Single(callbacks);
            Assert.Equal(before, Snapshot(fixture.Match));
            fixture.Window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.True(closed); Assert.False(fixture.Window.IsVisible);
            string afterClose = Snapshot(fixture.Match);
            int changes = fixture.Changes, events = fixture.Match.Events.Count;
            var recovery = fixture.Recovery();
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(afterClose, Snapshot(fixture.Match));
            Assert.Equal(events, fixture.Match.Events.Count); Assert.Equal(changes, fixture.Changes);
            Assert.Equal(0, scoreAdded); AssertRecovery(recovery, fixture.Recovery());
            dispatcher.Dispatch(command); Assert.Empty(callbacks);
        }
        finally
        {
            try { Mr9DrainControlledQueue(callbacks); Dispatcher.UIThread.RunJobs(); }
            finally { fixture.Match.ScoreEventAdded -= OnScore; }
        }
    }

    [AvaloniaTheory]
    [InlineData("home")]
    [InlineData("settings")]
    [InlineData("logical-minimized")]
    public async Task Mr9RetainedSessionPresentationChangesKeepQueuedGoalEligible(string presentation)
    {
        using var fixture = await Fixture.Create();
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        string before = Snapshot(fixture.Match);
        int changes = fixture.Changes;
        var expected = Mr9ExpectedCommand(fixture.Match, RemoteEventTypes.HomeGoal);
        var command = Mr9Command(RemoteEventTypes.HomeGoal);
        dispatcher.Dispatch(command); Assert.Single(callbacks);
        try
        {
            if (presentation == "logical-minimized")
            {
                // Avalonia logical state only: no native OS minimize/input claim.
                fixture.Window.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Minimized, fixture.Window.WindowState);
            }
            else
            {
                Assert.True(fixture.Window.OpenCommandBar());
                Assert.True(fixture.Window.ExecuteCommand(presentation)); // Production typed command API.
                await Settle();
                Assert.False(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
                Assert.True(Named<Control>(fixture.Window,
                    presentation == "home" ? "SportSelectionPanel" : "GlobalSettingsPanel").IsEffectivelyVisible);
            }
            Assert.Single(callbacks); Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Equal(changes, fixture.Changes);
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks)); Dispatcher.UIThread.RunJobs();
            Mr9AssertExpectedScoringState(expected, fixture.Match, RemoteEventTypes.HomeGoal);
            Assert.Equal(changes + 1, fixture.Changes);
            dispatcher.Dispatch(command); Assert.Empty(callbacks);
            Mr9AssertExpectedScoringState(expected, fixture.Match, RemoteEventTypes.HomeGoal);
        }
        finally
        {
            Mr9DrainControlledQueue(callbacks); Dispatcher.UIThread.RunJobs();
            if (presentation == "logical-minimized") fixture.Window.WindowState = WindowState.Normal;
        }
    }

    [AvaloniaFact]
    public async Task Mr9ReceiveThreadDefaultDispatcherScoresOnceOnTheActualUiThread()
    {
        using var fixture = await Fixture.Create();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, Dispatcher.UIThread);
        int uiThread = Environment.CurrentManagedThreadId, receiveThread = 0;
        var scoreThreads = new List<int>();
        var changeThreads = new List<int>();
        void OnScore(ScoreEvent _) => scoreThreads.Add(Environment.CurrentManagedThreadId);
        void OnChange() => changeThreads.Add(Environment.CurrentManagedThreadId);
        fixture.Match.ScoreEventAdded += OnScore; fixture.Match.MatchChanged += OnChange;
        string before = Snapshot(fixture.Match);
        int changes = fixture.Changes;
        var expected = Mr9ExpectedCommand(fixture.Match, RemoteEventTypes.HomeGoal);
        var command = Mr9Command(RemoteEventTypes.HomeGoal);
        Exception? receiveError = null;
        // Joining the bounded receive thread withholds UI pumping until its
        // actual public Dispatch calls return. No recording target is involved.
        var receiver = new System.Threading.Thread(() =>
        {
            try
            {
                receiveThread = Environment.CurrentManagedThreadId;
                dispatcher.Dispatch(command); dispatcher.Dispatch(command);
            }
            catch (Exception error) { receiveError = error; }
        }) { IsBackground = true };
        try
        {
            receiver.Start();
            Assert.True(receiver.Join(TimeSpan.FromSeconds(5)), "Receive-thread dispatch must return without requiring a UI pump.");
            Assert.Null(receiveError); Assert.NotEqual(uiThread, receiveThread);
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Empty(scoreThreads); Assert.Empty(changeThreads);
            Dispatcher.UIThread.RunJobs();
            Mr9AssertExpectedScoringState(expected, fixture.Match, RemoteEventTypes.HomeGoal);
            Assert.Equal(changes + 1, fixture.Changes);
            Assert.Equal(uiThread, Assert.Single(scoreThreads));
            Assert.Equal(uiThread, Assert.Single(changeThreads));
        }
        finally
        {
            // Finish any accepted UI closure before fixture file restoration.
            Dispatcher.UIThread.RunJobs();
            receiver.Join(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            fixture.Match.ScoreEventAdded -= OnScore; fixture.Match.MatchChanged -= OnChange;
        }
    }

    [AvaloniaFact]
    public async Task Mr9CurrentLifetimeQueuedUndoKeepsLatestEventSemanticsAfterAnotherGoal()
    {
        using var fixture = await Fixture.Create();
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        var originalEvents = fixture.Match.Events.ToArray();
        string before = Snapshot(fixture.Match);
        int changes = fixture.Changes, scoresAdded = 0;
        void OnScore(ScoreEvent _) => scoresAdded++;
        fixture.Match.ScoreEventAdded += OnScore;
        var undo = Mr9Command(RemoteEventTypes.Undo);
        try
        {
            dispatcher.Dispatch(undo); dispatcher.Dispatch(undo);
            Assert.Single(callbacks); Assert.Equal(before, Snapshot(fixture.Match));
            // A different legitimate command executes first in the SAME model
            // lifetime. Undo intentionally addresses the latest event at apply.
            var publicDispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, Dispatcher.UIThread);
            publicDispatcher.Dispatch(Mr9Command(RemoteEventTypes.HomeGoal)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalEvents.Length + 1, fixture.Match.Events.Count);
            Assert.Equal(changes + 1, fixture.Changes); Assert.Equal(1, scoresAdded);
            Assert.Equal(TeamSide.Home, fixture.Match.Events[^1].Team);
            Assert.Equal(ScoreType.Goal, fixture.Match.Events[^1].Type);
            Assert.Single(callbacks);
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Equal(changes + 2, fixture.Changes); Assert.Equal(1, scoresAdded);
            Assert.Equal(originalEvents.Length, fixture.Match.Events.Count);
            for (int index = 0; index < originalEvents.Length; index++) Assert.Same(originalEvents[index], fixture.Match.Events[index]);
            dispatcher.Dispatch(undo); Assert.Empty(callbacks);
            Assert.Equal(before, Snapshot(fixture.Match));
        }
        finally
        {
            try { Mr9DrainControlledQueue(callbacks); Dispatcher.UIThread.RunJobs(); }
            finally { fixture.Match.ScoreEventAdded -= OnScore; }
        }
    }

    private static async Task Mr9CompleteConfirmedNewAflSetup(Fixture fixture, Action? assertHeld = null)
    {
        // Production typed command API, not a physical keyboard claim. Home
        // keeps the current session available until setup is actually confirmed.
        Assert.True(fixture.Window.OpenCommandBar());
        Assert.True(fixture.Window.ExecuteCommand("home"));
        await Settle();
        assertHeld?.Invoke();
        var home = Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel");
        Button? launch = null;
        void CaptureHomeReadiness(string stage)
        {
            string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            try
            {
                Directory.CreateDirectory(directory);
                string stem = "mr9-home-" + stage + "-" + Guid.NewGuid().ToString("N");
                var current = home.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name == "AflLaunchAction");
                var center = current?.TranslatePoint(new Avalonia.Point(current.Bounds.Width / 2, current.Bounds.Height / 2), fixture.Window);
                var hit = center is null ? null : fixture.Window.InputHitTest(center.Value);
                var scrolls = current?.GetVisualAncestors().OfType<ScrollViewer>()
                    .Select(scroll => new { scroll.Name, offset = scroll.Offset.ToString(), extent = scroll.Extent.ToString(), viewport = scroll.Viewport.ToString(), bounds = scroll.Bounds.ToString() }).ToArray();
                File.WriteAllText(Path.Combine(directory, stem + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    stage, window = new { fixture.Window.Width, fixture.Window.Height, bounds = fixture.Window.Bounds.ToString() },
                    home = new { home.IsLoaded, home.IsEffectivelyVisible, home.IsEffectivelyEnabled, home.IsHitTestVisible, bounds = home.Bounds.ToString() },
                    action = current is null ? null : new { current.IsLoaded, current.IsEffectivelyVisible, current.IsEffectivelyEnabled, current.IsHitTestVisible, bounds = current.Bounds.ToString(), center = center?.ToString() },
                    actualHit = hit?.GetType().Name, hitName = (hit as Control)?.Name,
                    scrolls, remembered = WindowLayoutStorage.Get(WindowLayoutStorage.ControlPanelKey)
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                using var frame = fixture.Window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, stem + ".png"));
            }
            catch (Exception diagnostic) { Console.Error.WriteLine("MR9 Home diagnostic failed: " + diagnostic); }
        }
        CaptureHomeReadiness("before-readiness");
        try
        {
        await Until(() =>
        {
            // Resolve the attached dynamic action; Home can retire its old
            // namescope button when it refreshes the current-session card.
            var current = home.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(button => button.Name == "AflLaunchAction");
            if (current is not { IsLoaded: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                || current.Bounds.Width <= 0 || current.Bounds.Height <= 0) return false;
            launch = current;
            return true;
        }, "Replacement must attach its enabled Home Start Game action.");
        // A returned current-session card legitimately places Start below a
        // compact viewport. Reach it through real sequential Tab navigation;
        // never replace a physical hit proof with synthetic Click or resizing.
        for (int attempt = 0; attempt < 40 && !launch!.IsFocused; attempt++)
        {
            Key(fixture.Window, PhysicalKey.Tab);
            await Settle(25);
        }
        Assert.True(launch!.IsFocused, "Sequential Tab must reach Home Start Game and reveal its real target.");
        await Until(() =>
        {
            var center = launch.TranslatePoint(new Avalonia.Point(launch.Bounds.Width / 2, launch.Bounds.Height / 2), fixture.Window);
            return center is not null && Within(launch, fixture.Window.InputHitTest(center.Value));
        }, "The keyboard-revealed Home Start action must own its actual pointer target.");
        CaptureHomeReadiness("keyboard-revealed");
        }
        catch { CaptureHomeReadiness("readiness-failure"); throw; }
        await Tap(fixture.Window, launch!);
        var wizard = Named<SetupWizard>(fixture.Window, "SetupWizardPanel");
        await Until(() => Named<Control>(wizard, "SportStepContainer").IsEffectivelyVisible,
            "Home Start Game must enter the real sport step.");
        var afl = Named<RadioButton>(wizard, "AflSportChoice");
        afl.IsChecked = true; Click(afl);
        Click(Named<Button>(wizard, "NextButton")); await Settle();
        Named<TextBox>(wizard, "SetupHomeNameBox").Text = "MR9 replacement home";
        Named<TextBox>(wizard, "SetupHomeAbbrBox").Text = "MRH";
        Named<TextBox>(wizard, "SetupAwayNameBox").Text = "MR9 replacement away";
        Named<TextBox>(wizard, "SetupAwayAbbrBox").Text = "MRA";
        Click(Named<Button>(wizard, "NextButton")); await Settle();
        Named<RadioButton>(wizard, "SetupCountdown").IsChecked = true;
        Click(Named<Button>(wizard, "NextButton")); await Settle();
        Named<ComboBox>(wizard, "SetupWeatherLocation").Text = ""; // No weather/network fixture.
        assertHeld?.Invoke();

        var finish = Named<Button>(wizard, "NextButton");
        await RevealByKeyboard(fixture.Window, finish);
        bool answered = false;
        Exception? responseError = null;
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        var responder = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        responder.Tick += (_, _) =>
        {
            try
            {
                if (waiting.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("The actual owned new-session confirmation did not appear within 15 seconds.");
                var dialogs = fixture.Window.OwnedWindows.Where(window => window.Title == "New session").ToArray();
                if (answered || dialogs.Length == 0) return;
                var dialog = Assert.Single(dialogs);
                Assert.Same(fixture.Window, dialog.Owner);
                Assert.True(dialog.IsVisible);
                var yes = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Yes"));
                var no = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "No"));
                Assert.True(no.IsDefault); Assert.False(yes.IsDefault);
                Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("Completing setup will replace", StringComparison.Ordinal) == true);
                assertHeld?.Invoke();
                answered = true;
                // Public button activation on the actual owned modal. Do not
                // set DialogResult or invoke Authorize/Finalize privately.
                Click(yes);
            }
            catch (Exception error)
            {
                responseError = error;
                responder.Stop();
                foreach (var dialog in fixture.Window.OwnedWindows.Where(window => window.Title == "New session").ToArray()) dialog.Close();
            }
        };
        responder.Start();
        try { await Tap(fixture.Window, finish); }
        finally { responder.Stop(); }
        Assert.Null(responseError);
        Assert.True(answered, "The actual replacement confirmation must be explicitly answered Yes.");
        await Until(() => Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible && !wizard.IsEffectivelyVisible
            && fixture.Match.HomeName == "MR9 replacement home" && fixture.Match.AwayName == "MR9 replacement away",
            "Confirmed replacement must publish the new AFL teams and mounted console before releasing any held command.");
        Assert.False(home.IsEffectivelyVisible);
        Assert.DoesNotContain(fixture.Window.OwnedWindows, window => window.Title == "New session");
        assertHeld?.Invoke();
    }

    [AvaloniaFact]
    public async Task Mr9ConfirmedNewAflSetupStartsFreshAfterAnExistingScoredQuarter()
    {
        using var fixture = await Fixture.Create();
        // Public model preconditions make quarter/history/elapsed resets
        // observable; all replacement navigation uses the actual public UI.
        Assert.True(fixture.Match.EndQuarter());
        fixture.Match.SetElapsed(TimeSpan.FromSeconds(37));
        Assert.True(fixture.Match.Quarter > 1);
        Assert.True(fixture.Match.ToState().CompletedQuarterCount > 0);
        Assert.NotEmpty(fixture.Match.Events);
        Assert.Equal(6, fixture.Match.HomeTotal); Assert.Equal(1, fixture.Match.AwayTotal);
        Assert.False(string.IsNullOrWhiteSpace(Named<TextBlock>(fixture.Window, "LastEventText").Text));
        Assert.False(string.IsNullOrWhiteSpace(Named<TextBlock>(fixture.Window, "MatchLogCountBadge").Text));
        await Mr9CompleteConfirmedNewAflSetup(fixture);

        Assert.Equal(0, fixture.Match.HomeGoals); Assert.Equal(0, fixture.Match.HomeBehinds);
        Assert.Equal(0, fixture.Match.AwayGoals); Assert.Equal(0, fixture.Match.AwayBehinds);
        Assert.Equal(1, fixture.Match.Quarter); Assert.False(fixture.Match.ClockRunning);
        Assert.Equal(TimeSpan.Zero, fixture.Match.ElapsedInQuarter);
        Assert.Empty(fixture.Match.Events);
        var fresh = fixture.Match.ToState();
        Assert.Equal(0, fresh.CompletedQuarterCount); Assert.Equal(0, fresh.EndedQuarterNumber);
        Assert.Equal(0L, fresh.EndedQuarterElapsedTicks);
        Assert.All(fresh.QuarterSnapshots!, snapshot => Assert.Null(snapshot));
        Assert.All(fresh.QuarterPlayingTimeTicks!, ticks => Assert.Equal(0L, ticks));
        Assert.Empty(Named<StackPanel>(fixture.Window, "EventList").Children);
        Assert.Equal(string.Empty, Named<TextBlock>(fixture.Window, "LastEventText").Text);
        Assert.Equal(string.Empty, Named<TextBlock>(fixture.Window, "MatchLogCountBadge").Text);
    }

    [AvaloniaFact]
    public async Task Mr9OldQueuedGoalCannotEnterConfirmedReplacementButTwoFreshDistinctGoalsCan()
    {
        using var fixture = await Fixture.Create();
        var target = (IRemoteCommandTarget)fixture.Window;
        var publicDispatcher = new RemoteCommandDispatcher(target, Dispatcher.UIThread);
        Guid oldSession = Guid.NewGuid(); // Envelope provenance, not network-session authentication.
        Mr9ProveEligiblePublicRemoteRoute(fixture, publicDispatcher, oldSession);
        var callbacks = new Queue<Action>();
        var boundaryDispatcher = new RemoteCommandDispatcher(target, callbacks.Enqueue);
        var oldCommand = Mr9HomeGoal(oldSession);
        string original = Snapshot(fixture.Match);
        int originalChanges = fixture.Changes;
        boundaryDispatcher.Dispatch(oldCommand); boundaryDispatcher.Dispatch(oldCommand);
        Assert.Single(callbacks);
        Assert.Equal(original, Snapshot(fixture.Match));
        Assert.Equal(originalChanges, fixture.Changes);
        try
        {
            await Mr9CompleteConfirmedNewAflSetup(fixture, () => Assert.Single(callbacks));
            // Initialization has its own independent regression above. Capture
            // the actually published new-session state here, so retained old
            // scores cannot conceal a second stale-command ownership defect.
            string replacement = Snapshot(fixture.Match);
            var recovery = fixture.Recovery();
            int changes = fixture.Changes, events = fixture.Match.Events.Count, goals = fixture.Match.HomeGoals;
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(callbacks);
            Assert.Equal(replacement, Snapshot(fixture.Match));
            Assert.Equal(changes, fixture.Changes);
            AssertRecovery(recovery, fixture.Recovery());

            // New acceptance AFTER confirmed replacement remains eligible.
            // Two distinct IDs must both score; replaying one must not score a
            // third time. This uses default production dispatcher marshalling.
            var first = Mr9HomeGoal(Guid.NewGuid());
            var second = Mr9HomeGoal(first.SessionId);
            publicDispatcher.Dispatch(first); publicDispatcher.Dispatch(second); publicDispatcher.Dispatch(first);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(goals + 2, fixture.Match.HomeGoals);
            Assert.Equal(events + 2, fixture.Match.Events.Count);
            Assert.Equal(changes + 2, fixture.Changes);
            Assert.All(fixture.Match.Events.Skip(events), score =>
            { Assert.Equal(TeamSide.Home, score.Team); Assert.Equal(ScoreType.Goal, score.Type); });
            Assert.Equal("MR9 replacement home", fixture.Match.HomeName);
            Assert.True(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
            Assert.False(fixture.Match.ClockRunning);
        }
        finally
        {
            // Never let a failed assertion carry a real callback past fixture
            // disposal and file restoration.
            Mr9DrainControlledQueue(callbacks);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static RemoteEvent Mr9HomeGoal(Guid session) => new()
    { Id = Guid.NewGuid(), SessionId = session, EventType = RemoteEventTypes.HomeGoal };

    private static int Mr9DrainControlledQueue(Queue<Action> callbacks)
    {
        Dispatcher.UIThread.VerifyAccess();
        int count = 0;
        while (callbacks.Count > 0) { callbacks.Dequeue()(); count++; }
        return count;
    }

    private static void Mr9ProveEligiblePublicRemoteRoute(Fixture fixture, RemoteCommandDispatcher dispatcher, Guid session)
    {
        Assert.True(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
        Assert.False(Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel").IsEffectivelyVisible);
        Assert.False(fixture.Match.ClockRunning);
        int goals = fixture.Match.HomeGoals, events = fixture.Match.Events.Count, changes = fixture.Changes;
        var first = Mr9HomeGoal(session);
        dispatcher.Dispatch(first);
        dispatcher.Dispatch(first); // Same Id must be deduplicated, not two goals.
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(goals + 1, fixture.Match.HomeGoals);
        Assert.Equal(events + 1, fixture.Match.Events.Count);
        Assert.Equal(changes + 1, fixture.Changes);
        Assert.Equal(TeamSide.Home, fixture.Match.Events[^1].Team);
        Assert.Equal(ScoreType.Goal, fixture.Match.Events[^1].Type);

        // Two different legitimate commands in the same dispatcher drain must
        // each score; a blanket debounce/disable cannot satisfy this control.
        var second = Mr9HomeGoal(session);
        var third = Mr9HomeGoal(session);
        dispatcher.Dispatch(second); dispatcher.Dispatch(third); dispatcher.Dispatch(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(goals + 3, fixture.Match.HomeGoals);
        Assert.Equal(events + 3, fixture.Match.Events.Count);
        Assert.Equal(changes + 3, fixture.Changes);
        Assert.False(fixture.Match.ClockRunning);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mr9QueuedHomeGoalBeforeResetDecisionCannotCrossConfirmButSurvivesSafeCancel(bool confirm)
    {
        using var fixture = await Fixture.Create();
        // The public constructor still marshals through the actual UI dispatcher.
        // No recording proxy/private application handler is used in either route.
        var publicDispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, Dispatcher.UIThread);
        Guid originalSession = Guid.NewGuid(); // Queue provenance tag, not a real/network cloud-session claim.
        Mr9ProveEligiblePublicRemoteRoute(fixture, publicDispatcher, originalSession);
        var callbacks = new Queue<Action>();
        var boundaryDispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        fixture.SaveRecovery();

        // Current public Home/wizard Fixture and physical target probes remain
        // authoritative. Pointer processing may pump UI; it cannot drain this
        // controlled queue until the test explicitly invokes its real closures.
        await RevealByKeyboard(fixture.Window, fixture.Opener);
        AssertPhysicalTarget(fixture.Window, fixture.Opener);
        string before = Snapshot(fixture.Match);
        var recoveryBefore = fixture.Recovery();
        var oldEvents = fixture.Match.Events.ToArray();
        int oldHomeGoals = fixture.Match.HomeGoals;
        int oldHomeBehinds = fixture.Match.HomeBehinds, oldAwayGoals = fixture.Match.AwayGoals, oldAwayBehinds = fixture.Match.AwayBehinds;
        int oldQuarter = fixture.Match.Quarter;
        TimeSpan oldElapsed = fixture.Match.ElapsedInQuarter;
        int changesBeforeBoundary = fixture.Changes;
        var scoreNotifications = new List<ScoreEvent>();
        void ScoreAdded(ScoreEvent score) => scoreNotifications.Add(score);
        fixture.Match.ScoreEventAdded += ScoreAdded;
        var pending = Mr9HomeGoal(originalSession);
        try
        {
            // This command is accepted while the original match is still live,
            // BEFORE either opening or deciding Reset. This avoids inventing a
            // policy for new commands submitted while confirmation is modal.
            Assert.Empty(callbacks);
            boundaryDispatcher.Dispatch(pending);
            boundaryDispatcher.Dispatch(pending); // Duplicate acceptance must queue one closure.
            Assert.Single(callbacks);
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Empty(scoreNotifications);

            await Tap(fixture.Window, fixture.Opener);
            Assert.Single(callbacks);
            Assert.True(fixture.Overlay.IsEffectivelyVisible);
            Assert.True(fixture.Overlay.IsHitTestVisible);
            Assert.True(fixture.Confirm.IsEnabled);
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Equal(changesBeforeBoundary, fixture.Changes);
            Assert.Empty(scoreNotifications);
            Button decision = confirm ? fixture.Confirm : fixture.Cancel;
            AssertPhysicalTarget(fixture.Window, decision);
            Assert.Single(callbacks);
            await Tap(fixture.Window, decision);
            Assert.Single(callbacks);
            Assert.Empty(scoreNotifications); // No held production callback ran during real pointer processing.
            Assert.False(fixture.Overlay.IsVisible);
            Assert.False(fixture.Overlay.IsHitTestVisible);

            if (confirm)
            {
                // Establish the actual public reset receipt BEFORE releasing the
                // queued callback: no final-state-only timing assumption.
                Assert.Equal(changesBeforeBoundary + 1, fixture.Changes);
                Assert.Equal(0, fixture.Match.HomeTotal); Assert.Equal(0, fixture.Match.AwayTotal);
                Assert.Empty(fixture.Match.Events); Assert.False(fixture.Match.ClockRunning);
                Assert.Equal(TimeSpan.Zero, fixture.Match.ElapsedInQuarter);
                Assert.True(Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel").IsEffectivelyVisible);
                Assert.False(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
                Assert.Empty(Named<StackPanel>(fixture.Window, "EventList").Children);
                Assert.False(GameStateService.HasSavedState());
                string resetState = Snapshot(fixture.Match);
                var resetRecovery = fixture.Recovery();
                string? lastEvent = Named<TextBlock>(fixture.Window, "LastEventText").Text;

                Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
                Assert.Empty(callbacks);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(resetState, Snapshot(fixture.Match));
                Assert.Equal(changesBeforeBoundary + 1, fixture.Changes);
                Assert.Empty(scoreNotifications);
                Assert.Empty(fixture.Match.Events);
                Assert.Empty(Named<StackPanel>(fixture.Window, "EventList").Children);
                Assert.Equal(lastEvent, Named<TextBlock>(fixture.Window, "LastEventText").Text);
                AssertRecovery(resetRecovery, fixture.Recovery());
                Assert.False(GameStateService.HasSavedState());
                Assert.True(Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel").IsEffectivelyVisible);
                Assert.False(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
                return;
            }

            // Cancel did not retire the original match; an input accepted while
            // it was live must not be discarded by a blanket Reset-open epoch.
            Assert.Equal(before, Snapshot(fixture.Match));
            Assert.Equal(changesBeforeBoundary, fixture.Changes);
            AssertRecovery(recoveryBefore, fixture.Recovery());
            Assert.True(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
            Assert.Empty(callbacks);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(changesBeforeBoundary + 1, fixture.Changes);
            Assert.Equal(oldHomeGoals + 1, fixture.Match.HomeGoals);
            Assert.Equal(oldHomeBehinds, fixture.Match.HomeBehinds);
            Assert.Equal(oldAwayGoals, fixture.Match.AwayGoals); Assert.Equal(oldAwayBehinds, fixture.Match.AwayBehinds);
            Assert.Equal(oldQuarter, fixture.Match.Quarter); Assert.Equal(oldElapsed, fixture.Match.ElapsedInQuarter);
            Assert.False(fixture.Match.ClockRunning);
            Assert.Equal(oldEvents.Length + 1, fixture.Match.Events.Count);
            for (int index = 0; index < oldEvents.Length; index++) Assert.Same(oldEvents[index], fixture.Match.Events[index]);
            Assert.Same(fixture.Match.Events[^1], Assert.Single(scoreNotifications));
            Assert.Equal(TeamSide.Home, fixture.Match.Events[^1].Team); Assert.Equal(ScoreType.Goal, fixture.Match.Events[^1].Type);
            Assert.True(Named<Control>(fixture.Window, "MainContent").IsEffectivelyVisible);
            Assert.False(Named<SportSelectionScreen>(fixture.Window, "SportSelectionPanel").IsEffectivelyVisible);
            int once = fixture.Changes;
            boundaryDispatcher.Dispatch(pending);
            Assert.Empty(callbacks);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(once, fixture.Changes); // Replayed Id still cannot score twice after Cancel.
            Assert.Equal(oldHomeGoals + 1, fixture.Match.HomeGoals);
        }
        finally
        {
            // Release actual production closures on the UI thread even after an
            // assertion failure, before Fixture.Dispose restores isolated files.
            try { Mr9DrainControlledQueue(callbacks); }
            finally
            {
                fixture.Match.ScoreEventAdded -= ScoreAdded;
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
