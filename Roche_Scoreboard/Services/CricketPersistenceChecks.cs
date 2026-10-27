using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Exercises the actual worker against isolated fixture paths, not operator saves.</summary>
internal static class CricketPersistenceChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(true, 50);
        match.RecordDelivery(4, CricketDeliveryType.Four);
        var detached = match.CapturePersistenceState();
        string capturedHistory = JsonSerializer.Serialize(detached.AllInnings[0].Deliveries);
        match.RecordDelivery(6, CricketDeliveryType.Six);
        match.CurrentInnings!.CurrentOverBalls.Add("fixture-only");
        checks["recovery-snapshot-detaches-live-innings-players-and-over-lists"] = detached.AllInnings[0].TotalRuns == 4
            && detached.AllInnings[0].BattingOrder[0].Runs == 4 && detached.AllInnings[0].CurrentOverBalls.Count == 1
            && detached.AllInnings[0].Deliveries.Count == 1;
        match.CurrentInnings.CurrentOverBalls.RemoveAt(match.CurrentInnings.CurrentOverBalls.Count - 1);
        match.UndoLastDelivery();
        checks["recovery-snapshot-shared-delivery-history-is-stable-after-undo"] = JsonSerializer.Serialize(detached.AllInnings[0].Deliveries) == capturedHistory;

        GameState Capture() => new() { Sport = "cricket", CricketMatchState = match.CapturePersistenceState() };
        string firstPath = Path.Combine(directory, "worker-coalescing.json");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int preparations = 0;
        var writer = new CricketRecoveryWriter(firstPath, beforeCommit: () =>
        {
            if (Interlocked.Increment(ref preparations) == 1)
            { started.Set(); if (!release.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException("Fixture gate was not released."); }
        });
        try
        {
            var watch = Stopwatch.StartNew(); writer.Queue(Capture()); watch.Stop();
            checks["recovery-queue-does-not-run-serialization-and-disk-write-on-caller"] = watch.ElapsedMilliseconds < 250;
            bool ready = await Task.Run(() => started.Wait(TimeSpan.FromSeconds(3)));
            checks["recovery-background-worker-reaches-injected-disk-boundary"] = ready;
            match.RecordDelivery(1, CricketDeliveryType.Runs); writer.Queue(Capture());
            match.RecordDelivery(2, CricketDeliveryType.Runs); writer.Queue(Capture());
            match.RecordDelivery(3, CricketDeliveryType.Runs); writer.Queue(Capture());
            release.Set();
            checks["recovery-final-flush-finishes-pending-snapshot"] = await Task.Run(() => writer.Flush(TimeSpan.FromSeconds(4)));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(firstPath));
            checks["recovery-burst-keeps-latest-score-with-bounded-write-count"] = json.RootElement.GetProperty("CricketMatchState").GetProperty("AllInnings")[0].GetProperty("TotalRuns").GetInt32() == match.TotalRuns
                && writer.CompletedWrites == 2;
            checks["recovery-retains-established-format-and-atomic-backup"] = json.RootElement.GetProperty("Sport").GetString() == "cricket"
                && json.RootElement.TryGetProperty("SavedAtUtc", out _) && File.Exists(firstPath + ".bak");
        }
        finally { release.Set(); await Task.Run(() => writer.Flush(TimeSpan.FromSeconds(4))); }

        string resetPath = Path.Combine(directory, "worker-reset.json");
        using var resetStarted = new ManualResetEventSlim(); using var resetRelease = new ManualResetEventSlim();
        var resetWriter = new CricketRecoveryWriter(resetPath, beforeCommit: () =>
        { resetStarted.Set(); if (!resetRelease.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException("Fixture reset gate was not released."); });
        try
        {
            resetWriter.Queue(Capture()); await Task.Run(() => resetStarted.Wait(TimeSpan.FromSeconds(3)));
            var bounded = Stopwatch.StartNew();
            bool timedOut = await Task.Run(() => !resetWriter.Flush(TimeSpan.FromMilliseconds(25)));
            bounded.Stop();
            checks["recovery-shutdown-flush-has-a-bounded-timeout"] = timedOut && bounded.ElapsedMilliseconds < 1000;
            resetWriter.Clear(); resetRelease.Set();
            await Task.Run(() => resetWriter.Flush(TimeSpan.FromSeconds(4)));
            checks["recovery-reset-invalidates-a-prepared-stale-snapshot"] = !File.Exists(resetPath) && !File.Exists(resetPath + ".bak");
            var next = CricketReliabilityChecks.CreateMatch(true); next.RecordDelivery(2, CricketDeliveryType.Runs);
            resetWriter.Queue(new GameState { Sport = "cricket", CricketMatchState = next.CapturePersistenceState() });
            await Task.Run(() => resetWriter.Flush(TimeSpan.FromSeconds(4)));
            using var nextJson = JsonDocument.Parse(await File.ReadAllTextAsync(resetPath));
            checks["recovery-next-game-after-reset-can-save-without-old-game-resurrection"] = nextJson.RootElement.GetProperty("CricketMatchState").GetProperty("AllInnings")[0].GetProperty("TotalRuns").GetInt32() == 2;
        }
        finally { resetRelease.Set(); await Task.Run(() => resetWriter.Flush(TimeSpan.FromSeconds(4))); }

        string impossible = Path.Combine(directory, "directory-not-a-recovery-file"); Directory.CreateDirectory(impossible);
        var failureWriter = new CricketRecoveryWriter(impossible); failureWriter.Queue(Capture());
        bool flushed = await Task.Run(() => failureWriter.Flush(TimeSpan.FromSeconds(4)));
        checks["recovery-io-failure-is-contained-and-reported-not-unobserved"] = !flushed && failureWriter.LastError is IOException;
        return checks;
    }
}
