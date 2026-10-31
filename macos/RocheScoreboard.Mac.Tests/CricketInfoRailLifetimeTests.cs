using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketInfoRailLifetimeTests
{
    private static async Task Settle(int milliseconds = 650)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Snapshot(CricketMatchManager match) =>
        JsonSerializer.Serialize(match.CapturePersistenceState());

    private static CricketMatchManager FirstInnings(int runs = 2)
    {
        var match = CricketReliabilityChecks.CreateMatch(ready: true);
        match.RecordDelivery(runs, CricketDeliveryType.Runs);
        Assert.Equal(runs, match.TotalRuns);
        Assert.Equal(1, match.CurrentInnings!.LegalBallsBowled);
        return match;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly CricketScorebugControl Scorebug = new()
        {
            EventAnimationsEnabled = false,
            RollingFiguresEnabled = false,
            BowlerDetailsEnabled = false
        };
        private readonly NativeWindow window;
        internal TextBlock Label => Assert.IsType<TextBlock>(Scorebug.FindControl<TextBlock>("InfoLabel"));
        internal TextBlock Value => Assert.IsType<TextBlock>(Scorebug.FindControl<TextBlock>("InfoValue"));

        internal Fixture()
        {
            window = new NativeWindow
            {
                Title = "Cricket Display",
                Width = 386,
                Height = 193,
                Content = Scorebug
            };
            window.Show();
        }

        internal async Task Ready(CricketMatchManager match, string label, string value)
        {
            await Settle(180);
            Assert.True(Scorebug.IsLoaded);
            Scorebug.UpdateFromMatch(match);
            await Settle();
            AssertContext(label, value);
        }

        internal void Refresh(CricketMatchManager match) => Scorebug.UpdateFromMatch(match);

        internal void AssertContext(string label, string value)
        {
            Assert.Equal(label, Label.Text);
            Assert.Equal(value, Value.Text);
            Assert.Equal(1, Label.Opacity, 3);
            Assert.Equal(1, Value.Opacity, 3);
        }

        public void Dispose()
        {
            Scorebug.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RainCancellationCannotRestoreAnObsoleteBreakCaption()
    {
        var match = FirstInnings();
        using var f = new Fixture();
        await f.Ready(match, "RUN RATE", "12.00");

        // Do not yield between updates: the old category fade is still pending.
        match.SetBreak(CricketBreakKind.Rain);
        f.Refresh(match);
        match.SetBreak(CricketBreakKind.None);
        f.Refresh(match);
        string state = Snapshot(match);

        await Settle();

        Assert.Equal(CricketBreakKind.None, match.BreakKind);
        Assert.Equal(state, Snapshot(match));
        f.AssertContext("RUN RATE", "12.00");
    }

    [AvaloniaFact]
    public async Task RapidBreakReversalsKeepTheLatestLiveContext()
    {
        var match = FirstInnings();
        using var f = new Fixture();
        await f.Ready(match, "RUN RATE", "12.00");

        match.SetBreak(CricketBreakKind.Rain);
        f.Refresh(match);
        match.SetBreak(CricketBreakKind.Other);
        f.Refresh(match);
        match.SetBreak(CricketBreakKind.None);
        f.Refresh(match);
        string state = Snapshot(match);

        await Settle();

        Assert.Equal(CricketBreakKind.None, match.BreakKind);
        Assert.Equal(state, Snapshot(match));
        f.AssertContext("RUN RATE", "12.00");
    }

    [AvaloniaFact]
    public async Task ASettledBreakSurvivesAPendingResumeReversedToAnotherBreak()
    {
        var match = FirstInnings();
        match.SetBreak(CricketBreakKind.Rain);
        using var f = new Fixture();
        await f.Ready(match, "BREAK", "RAIN");

        match.SetBreak(CricketBreakKind.None);
        f.Refresh(match);
        match.SetBreak(CricketBreakKind.Other);
        f.Refresh(match);
        string state = Snapshot(match);

        await Settle();

        Assert.Equal(CricketBreakKind.Other, match.BreakKind);
        Assert.Equal(state, Snapshot(match));
        f.AssertContext("BREAK", "PAUSED");
    }

    [AvaloniaFact]
    public async Task ANewInningsRejectsThePreviousInningsPendingCaption()
    {
        var match = CricketReliabilityChecks.CreateMatch(ready: true);
        match.Format = CricketFormat.MultiInnings;
        match.TotalInnings = 4;
        match.EndCurrentInnings();
        Assert.True(match.CanStartNextInnings());
        match.StartNextInnings();
        Assert.Equal(2, match.CurrentInningsNumber);
        var oldInnings = match.CurrentInnings;
        using var f = new Fixture();
        await f.Ready(match, "SCORES", "LEVEL");

        match.SetBreak(CricketBreakKind.Rain);
        f.Refresh(match);
        match.SetBreak(CricketBreakKind.None);
        match.EndCurrentInnings();
        Assert.True(match.CanStartNextInnings());
        match.StartNextInnings();
        f.Refresh(match);
        string state = Snapshot(match);

        await Settle();

        Assert.Equal(3, match.CurrentInningsNumber);
        Assert.NotSame(oldInnings, match.CurrentInnings);
        Assert.Equal(CricketBreakKind.None, match.BreakKind);
        Assert.Equal(state, Snapshot(match));
        f.AssertContext("SCORES", "LEVEL");
    }

    [AvaloniaFact]
    public async Task RebindingTheViewRejectsTheOldMatchPendingCaption()
    {
        var oldMatch = FirstInnings();
        var currentMatch = FirstInnings(3);
        using var f = new Fixture();
        await f.Ready(oldMatch, "RUN RATE", "12.00");

        oldMatch.SetBreak(CricketBreakKind.Rain);
        f.Refresh(oldMatch);
        f.Refresh(currentMatch);
        string oldState = Snapshot(oldMatch);
        string currentState = Snapshot(currentMatch);

        await Settle();

        Assert.Equal(CricketBreakKind.Rain, oldMatch.BreakKind);
        Assert.Equal(CricketBreakKind.None, currentMatch.BreakKind);
        Assert.Equal(2, oldMatch.TotalRuns);
        Assert.Equal(3, currentMatch.TotalRuns);
        Assert.Equal(oldState, Snapshot(oldMatch));
        Assert.Equal(currentState, Snapshot(currentMatch));
        f.AssertContext("RUN RATE", "18.00");
    }
}
