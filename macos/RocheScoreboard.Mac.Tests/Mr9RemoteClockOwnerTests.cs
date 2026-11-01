// Reuses the actual public AFL wizard Fixture and production dispatch closure.
// These local envelopes provide queue provenance, not network authentication.
using System.Text.Json;
using Avalonia;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflResetScopeTests
{
    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.ClockSet)]
    [InlineData(RemoteEventTypes.ClockModeSet)]
    [InlineData(RemoteEventTypes.ClockToggle)]
    public async Task Mr9FreshAflHomeClockCommandsRemainAvailableBeforeMatchSetup(string type)
    {
        using var fixture = await Fixture.Create(startMatch: false);
        Assert.False(Read<bool>(fixture.Window, "_aflMatchReady"));
        Assert.Equal(SportMode.AFL, Read<SportMode>(fixture.Window, "_sportMode"));
        fixture.Remote.SetClockMode("CountUp", 20, 0);
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        int changes = fixture.Changes;var elapsed=fixture.Match.ElapsedInQuarter;
        string events=JsonSerializer.Serialize(fixture.Match.Events);
        try
        {
            var command=Mr9ClockCommand(type);dispatcher.Dispatch(command);dispatcher.Dispatch(command);
            Assert.Single(callbacks);Assert.Equal(1,Mr9DrainControlledQueue(callbacks));
            Mr9AssertFreshClock(fixture,type,elapsed,changes,events);
            dispatcher.Dispatch(command);Assert.Empty(callbacks);
            Assert.False(Read<bool>(fixture.Window, "_aflMatchReady"));
        }
        finally { callbacks.Clear();fixture.Match.PauseClock(); }
    }

    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.ClockSet)]
    [InlineData(RemoteEventTypes.ClockModeSet)]
    [InlineData(RemoteEventTypes.ClockToggle)]
    public async Task Mr9QueuedClockCannotActAfterItsActualOwnerCloses(string type)
    {
        using var fixture=await Fixture.Create();
        var callbacks=new Queue<Action>();
        var dispatcher=new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window,callbacks.Enqueue);
        var command=Mr9ClockCommand(type);dispatcher.Dispatch(command);Assert.Single(callbacks);
        fixture.Window.Close();Dispatcher.UIThread.RunJobs();
        string before=Snapshot(fixture.Match);int changes=fixture.Changes;var recovery=fixture.Recovery();
        Assert.Equal(1,Mr9DrainControlledQueue(callbacks));
        Assert.Equal(before,Snapshot(fixture.Match));Assert.Equal(changes,fixture.Changes);AssertRecovery(recovery,fixture.Recovery());
        dispatcher.Dispatch(command);Assert.Empty(callbacks);
    }

    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.ClockSet)]
    [InlineData(RemoteEventTypes.ClockModeSet)]
    [InlineData(RemoteEventTypes.ClockToggle)]
    public async Task Mr9OldAndFreshAflClockCommandsHaveNoAuthorityInActualTrainingMode(string type)
    {
        using var fixture=await Fixture.Create(startMatch:false);
        var callbacks=new Queue<Action>();
        var dispatcher=new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window,callbacks.Enqueue);
        dispatcher.Dispatch(Mr9ClockCommand(type));Assert.Single(callbacks);
        var home=Named<Roche_Scoreboard.Views.SportSelectionScreen>(fixture.Window,"SportSelectionPanel");
        Button? training=null;
        await Until(()=>
        {
            training=home.GetVisualDescendants().OfType<Button>().SingleOrDefault(button=>button.Name=="TrainingLaunchAction");
            if(training is not {IsLoaded:true,IsEffectivelyVisible:true,IsEffectivelyEnabled:true} || training.Bounds.Width<=0 || training.Bounds.Height<=0)return false;
            var point=training.TranslatePoint(new Avalonia.Point(training.Bounds.Width/2,training.Bounds.Height/2),fixture.Window);
            return point is not null && Within(training,fixture.Window.InputHitTest(point.Value));
        },"The current Home Training action must own its enabled pointer target.");
        await Tap(fixture.Window,training!);
        await Until(()=>Read<SportMode>(fixture.Window,"_sportMode")==SportMode.Training && Read<TrainingSession?>(fixture.Window,"_trainingSession") is not null,"Actual Home action must enter Training mode.");
        var session=Read<TrainingSession>(fixture.Window,"_trainingSession");
        string trainingBefore=JsonSerializer.Serialize(session),aflBefore=Snapshot(fixture.Match);int changes=fixture.Changes;
        Assert.Equal(1,Mr9DrainControlledQueue(callbacks));
        dispatcher.Dispatch(Mr9ClockCommand(type));Assert.Equal(1,Mr9DrainControlledQueue(callbacks));
        Assert.Equal(aflBefore,Snapshot(fixture.Match));Assert.Equal(trainingBefore,JsonSerializer.Serialize(session));Assert.Equal(changes,fixture.Changes);
    }

    private static RemoteEvent Mr9ClockCommand(string type) => Mr9Command(type) with
    {
        Payload = JsonSerializer.SerializeToElement(type == RemoteEventTypes.ClockModeSet
            ? (object)new { mode = "Countdown", minutes = 7, seconds = 13 }
            : new { minutes = 1, seconds = 11 })
    };

    private static void Mr9AssertFreshClock(Fixture fixture, string type, TimeSpan previousElapsed,
        int changesBefore, string eventsBefore)
    {
        Assert.Equal(eventsBefore, JsonSerializer.Serialize(fixture.Match.Events));
        switch (type)
        {
            case RemoteEventTypes.ClockSet:
                Assert.False(fixture.Match.ClockRunning);
                Assert.Equal(ClockMode.CountUp, fixture.Match.ClockMode);
                Assert.Equal(TimeSpan.FromSeconds(71), fixture.Match.ElapsedInQuarter);
                Assert.Equal(changesBefore + 1, fixture.Changes);
                break;
            case RemoteEventTypes.ClockModeSet:
                Assert.False(fixture.Match.ClockRunning);
                Assert.Equal(ClockMode.Countdown, fixture.Match.ClockMode);
                Assert.Equal(TimeSpan.FromSeconds(433), fixture.Match.QuarterDuration);
                Assert.Equal(previousElapsed, fixture.Match.ElapsedInQuarter);
                // The real radio Checked handler and target both synchronize
                // model policy. Do not mistake those existing notifications
                // for duplicate envelope execution.
                Assert.True(fixture.Changes > changesBefore);
                break;
            case RemoteEventTypes.ClockToggle:
                Assert.True(fixture.Match.ClockRunning);
                Assert.True(fixture.Match.ElapsedInQuarter >= previousElapsed);
                Assert.Equal(ClockMode.CountUp, fixture.Match.ClockMode);
                Assert.Equal(changesBefore + 1, fixture.Changes);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.ClockSet)]
    [InlineData(RemoteEventTypes.ClockModeSet)]
    [InlineData(RemoteEventTypes.ClockToggle)]
    public async Task Mr9QueuedAflClockCommandsCannotCrossPublicLoadStateOnSameManager(string type)
    {
        using var fixture = await Fixture.Create();
        fixture.Remote.SetClockMode("CountUp", 20, 0);
        await Settle(); // Finish the deliberate fixture clock-mode precondition before queuing an envelope.
        Assert.False(fixture.Match.ClockRunning);
        var originalManager = fixture.Match;
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        var stale = Mr9ClockCommand(type);
        string original = Snapshot(fixture.Match);
        dispatcher.Dispatch(stale); dispatcher.Dispatch(stale);
        Assert.Single(callbacks); Assert.Equal(original, Snapshot(fixture.Match));
        try
        {
            var replacement = new MatchManager();
            replacement.SetTeams("Clock replacement home", "CRH", "Clock replacement away", "CRA");
            replacement.AddGoal(TeamSide.Away); replacement.AddBehind(TeamSide.Home);
            replacement.SetClockMode(ClockMode.CountUp);
            replacement.SetQuarterDuration(TimeSpan.FromMinutes(20));
            replacement.SetElapsed(TimeSpan.FromSeconds(19));
            string loaded = Snapshot(replacement);
            fixture.Match.LoadState(replacement.ToState()); // Actual public same-object retirement.
            Assert.Same(originalManager, fixture.Match); Assert.Equal(loaded, Snapshot(fixture.Match));
            fixture.SaveRecovery(); var recovery = fixture.Recovery();
            int changes = fixture.Changes;
            string events = JsonSerializer.Serialize(fixture.Match.Events);
            var eventReferences = fixture.Match.Events.ToArray();

            Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
            string immediate = Snapshot(fixture.Match);
            string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "clock-owner-" + type + ".json"),
                    JsonSerializer.Serialize(new { type, expected = loaded, immediate, changesBefore = changes, changesAfter = fixture.Changes },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            // FIRST rejection oracle: each old clock command currently has no
            // captured guard and mutates this replacement (elapsed/mode/run).
            Assert.Equal(loaded, Snapshot(fixture.Match));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(changes, fixture.Changes); Assert.Equal(events, JsonSerializer.Serialize(fixture.Match.Events));
            AssertRecovery(recovery, fixture.Recovery());
            for (int i = 0; i < eventReferences.Length; i++) Assert.Same(eventReferences[i], fixture.Match.Events[i]);

            var current = Mr9ClockCommand(type);
            dispatcher.Dispatch(current); dispatcher.Dispatch(current);
            Assert.Single(callbacks); Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
            Mr9AssertFreshClock(fixture, type, TimeSpan.FromSeconds(19), changes, events);
            int acceptedChanges = fixture.Changes;
            // Duplicate ids must stay consumed even across the owner boundary.
            dispatcher.Dispatch(stale); dispatcher.Dispatch(current);
            Assert.Empty(callbacks); Assert.Equal(acceptedChanges, fixture.Changes);
            if (type == RemoteEventTypes.ClockToggle) Assert.True(fixture.Match.ClockRunning);
            else
            {
                string accepted = Snapshot(fixture.Match);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(accepted, Snapshot(fixture.Match));
            }
        }
        finally
        {
            callbacks.Clear(); // A failed oracle must not execute further commands during cleanup.
            fixture.Match.PauseClock();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.ClockSet, "home")]
    [InlineData(RemoteEventTypes.ClockModeSet, "home")]
    [InlineData(RemoteEventTypes.ClockToggle, "home")]
    [InlineData(RemoteEventTypes.ClockSet, "settings")]
    [InlineData(RemoteEventTypes.ClockModeSet, "settings")]
    [InlineData(RemoteEventTypes.ClockToggle, "settings")]
    public async Task Mr9RetainedAflHomeAndSettingsKeepQueuedCurrentClockCommands(string type, string route)
    {
        using var fixture = await Fixture.Create();
        fixture.Remote.SetClockMode("CountUp", 20, 0);
        fixture.Match.SetElapsed(TimeSpan.FromSeconds(19));
        var manager = fixture.Match; Assert.False(manager.ClockRunning);
        var callbacks = new Queue<Action>();
        var dispatcher = new RemoteCommandDispatcher((IRemoteCommandTarget)fixture.Window, callbacks.Enqueue);
        var command = Mr9ClockCommand(type);
        string before = Snapshot(manager);
        dispatcher.Dispatch(command); dispatcher.Dispatch(command); Assert.Single(callbacks);
        try
        {
            Assert.True(fixture.Window.OpenCommandBar()); Assert.True(fixture.Window.ExecuteCommand(route));
            await Settle(100);
            Assert.Same(manager, fixture.Match);
            Assert.Equal(before, Snapshot(manager));
            Assert.True(Read<bool>(fixture.Window, "_aflMatchReady")); // Read current authority, never seed it.
            string events = JsonSerializer.Serialize(manager.Events); int changes = fixture.Changes;
            Assert.Equal(1, Mr9DrainControlledQueue(callbacks));
            Mr9AssertFreshClock(fixture, type, TimeSpan.FromSeconds(19), changes, events);
            int once = fixture.Changes;
            dispatcher.Dispatch(command); Assert.Empty(callbacks); Assert.Equal(once, fixture.Changes);
            // Output-window changes remain the existing local handler's policy.
        }
        finally
        {
            callbacks.Clear(); fixture.Match.PauseClock(); Dispatcher.UIThread.RunJobs();
        }
    }
}
