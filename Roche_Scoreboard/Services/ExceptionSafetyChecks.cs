using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Exercises production policy and file writes without raising a fatal dispatcher event.</summary>
internal static class ExceptionSafetyChecks
{
    internal static Task<Dictionary<string, bool>> RunAsync()
    {
        if (!SelfTestStorage.IsEnabled)
            throw new InvalidOperationException("Exception safety checks require isolated self-test storage.");

        var checks = new Dictionary<string, bool>();
        CheckPolicy(checks);
        CheckLogging(checks);
        CheckRecoveryWriter(checks);
        return Task.FromResult(checks);
    }

    private static void CheckPolicy(Dictionary<string, bool> checks)
    {
        var oom = new OutOfMemoryException("synthetic, never thrown");
        var handles = new COMException("synthetic render failure", unchecked((int)0x8898040D));
        var memoryHr = new COMException("synthetic memory failure", unchecked((int)0x8007000E));
        var recoverable = new InvalidOperationException("UCEERR_OUTOFHANDLES is only text here");
        checks["fatal-policy-recognizes-oom-and-resource-hresults"] =
            new Exception[] { oom, handles, memoryHr }.All(UnhandledExceptionPolicy.IsFatalResourceFailure);
        checks["fatal-policy-inspects-wrapped-and-nonfirst-aggregate-cause"] =
            UnhandledExceptionPolicy.IsFatalResourceFailure(new InvalidOperationException("wrapper", handles)) &&
            UnhandledExceptionPolicy.IsFatalResourceFailure(new AggregateException(recoverable,
                new InvalidOperationException("second branch", oom)));
        checks["fatal-policy-does-not-match-text-or-unrelated-com-errors"] =
            !UnhandledExceptionPolicy.IsFatalResourceFailure(recoverable) &&
            !UnhandledExceptionPolicy.IsFatalResourceFailure(new COMException("other", unchecked((int)0x80004005))) &&
            !UnhandledExceptionPolicy.IsFatalResourceFailure(new AggregateException(recoverable, new IOException("io")));

        var calls = new List<string>();
        bool exactRecord = false;
        var policy = new UnhandledExceptionPolicy((source, error, fatal) =>
        {
            calls.Add("log");
            exactRecord = source == "dispatcher" && ReferenceEquals(error, handles) && fatal;
        }, () => calls.Add("save"));
        bool firstHandled = policy.Handle("dispatcher", handles);
        bool anyRepeatHandled = false;
        for (int i = 0; i < 1000; i++)
            anyRepeatHandled |= policy.Handle("dispatcher-repeat", handles);
        bool domainHandled = policy.Handle("domain", handles, terminating: true);
        bool laterRecoveryHandled = policy.Handle("later", recoverable);
        checks["fatal-policy-leaves-dispatcher-unhandled-and-saves-once"] =
            !firstHandled && exactRecord && calls.SequenceEqual(new[] { "log", "save" });
        checks["fatal-policy-suppresses-render-storm-and-domain-duplicate"] =
            !anyRepeatHandled && !domainHandled && !laterRecoveryHandled && calls.Count == 2;

        int logs = 0, saves = 0;
        bool nestedHandled = true;
        UnhandledExceptionPolicy? reentrant = null;
        reentrant = new UnhandledExceptionPolicy((_, error, _) =>
        {
            logs++;
            nestedHandled = reentrant!.Handle("nested-domain", error, terminating: true);
        }, () => saves++);
        bool outerHandled = reentrant.Handle("first", oom);
        checks["fatal-policy-reentrant-notification-does-not-log-or-save-again"] =
            !outerHandled && !nestedHandled && logs == 1 && saves == 1;

        logs = saves = 0;
        UnhandledExceptionPolicy? interrupted = null;
        interrupted = new UnhandledExceptionPolicy((_, _, _) =>
        {
            logs++;
            nestedHandled = interrupted!.Handle("nested-fatal", oom);
        }, () => saves++);
        bool interruptedHandled = interrupted.Handle("recoverable", recoverable);
        checks["fatal-policy-reentry-during-ordinary-recovery-is-terminal"] =
            !interruptedHandled && !nestedHandled && logs == 1 && saves == 1 &&
            !interrupted.Handle("later", recoverable);

        logs = saves = 0;
        var failingCallbacks = new UnhandledExceptionPolicy((_, _, _) =>
        {
            logs++;
            throw new IOException("synthetic logger failure");
        }, () => { saves++; throw new IOException("synthetic save failure"); });
        bool failingHandled = failingCallbacks.Handle("first", oom);
        failingCallbacks.Handle("repeat", oom);
        checks["fatal-policy-failed-log-still-attempts-save-without-retry"] =
            !failingHandled && logs == 1 && saves == 1;

        foreach (bool failInLog in new[] { true, false })
        {
            bool allLatched = true;
            foreach (Exception secondary in new Exception[]
            {
                oom, handles, memoryHr,
                new InvalidOperationException("wrapped resource failure", oom),
                new AggregateException(recoverable, handles)
            })
            {
                var attempts = new List<string>();
                var failing = new UnhandledExceptionPolicy((_, _, fatal) =>
                {
                    attempts.Add(fatal ? "unexpected-fatal-input" : "log");
                    if (failInLog) throw secondary;
                }, () =>
                {
                    attempts.Add("save");
                    if (!failInLog) throw secondary;
                });
                bool handled = failing.Handle("ordinary-input", recoverable);
                bool repeatHandled = failing.Handle("repeat", recoverable);
                allLatched &= !handled && !repeatHandled &&
                    attempts.SequenceEqual(new[] { "log", "save" });
            }
            checks[failInLog ? "fatal-policy-secondary-log-failure-latches-with-one-save"
                : "fatal-policy-secondary-save-failure-latches-without-retry"] = allLatched;
        }

        logs = saves = 0;
        var ordinaryFailures = new UnhandledExceptionPolicy((_, _, _) =>
        { logs++; throw new IOException("ordinary diagnostic failure"); }, () =>
        { saves++; throw new IOException("ordinary recovery failure"); });
        bool ordinaryFailuresHandled = ordinaryFailures.Handle("one", recoverable);
        bool nextOrdinaryHandled = ordinaryFailures.Handle("two", recoverable);
        checks["fatal-policy-ordinary-callback-io-failures-do-not-latch-terminal"] =
            ordinaryFailuresHandled && nextOrdinaryHandled && logs == 2 && saves == 2;

        logs = saves = 0;
        var logOnly = new UnhandledExceptionPolicy((_, _, _) => { logs++; throw handles; }, () => saves++);
        bool logOnlyHandled = logOnly.Handle("domain", recoverable, saveState: false);
        bool logOnlyRepeated = logOnly.Handle("repeat", recoverable);
        checks["fatal-policy-secondary-failure-preserves-no-save-request"] =
            !logOnlyHandled && !logOnlyRepeated && logs == 1 && saves == 0;

        logs = saves = 0;
        bool nonfatalRecords = true;
        var ordinary = new UnhandledExceptionPolicy((_, _, fatal) => { logs++; nonfatalRecords &= !fatal; }, () => saves++);
        bool ordinaryHandled = ordinary.Handle("one", recoverable) && ordinary.Handle("two", new IOException("ordinary"));
        bool domainNonfatalHandled = ordinary.Handle("domain", recoverable, saveState: false);
        checks["fatal-policy-preserves-ordinary-recovery-and-domain-save-rules"] =
            ordinaryHandled && domainNonfatalHandled && nonfatalRecords && logs == 3 && saves == 2;

        logs = saves = 0;
        bool terminalRecorded = false;
        var terminal = new UnhandledExceptionPolicy((_, _, fatal) => { logs++; terminalRecorded = fatal; }, () => saves++);
        bool terminalHandled = terminal.Handle("domain", recoverable, terminating: true);
        terminal.Handle("repeat", recoverable, terminating: true);
        checks["fatal-policy-terminating-domain-failure-is-always-once-only"] =
            !terminalHandled && terminalRecorded && logs == 1 && saves == 1;

        int concurrentLogs = 0, concurrentSaves = 0, continued = 0;
        var concurrent = new UnhandledExceptionPolicy((_, _, _) => Interlocked.Increment(ref concurrentLogs),
            () => Interlocked.Increment(ref concurrentSaves));
        Parallel.For(0, 32, _ =>
        {
            if (concurrent.Handle("concurrent", handles)) Interlocked.Increment(ref continued);
        });
        checks["fatal-policy-concurrent-notifications-have-one-owner"] =
            concurrentLogs == 1 && concurrentSaves == 1 && continued == 0;

        Exception deep = recoverable;
        for (int i = 0; i < 40; i++) deep = new Exception("wrapper", deep);
        checks["fatal-policy-bounds-exception-tree-inspection"] =
            UnhandledExceptionPolicy.IsFatalResourceFailure(deep) &&
            UnhandledExceptionPolicy.IsFatalResourceFailure(new AggregateException(Enumerable.Repeat(recoverable, 300)));
    }

    private static void CheckLogging(Dictionary<string, bool> checks)
    {
        // This run owns a fresh directory. Never target existing app/operator diagnostics.
        string directory = SelfTestStorage.PathFor("exception-safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "bounded.log");
        string preservedPath = Path.Combine(directory, "oversized-evidence.log");
        var large = new LargeDetailsException();
        string formatted = BoundedExceptionLog.Format("bounded-check", large);
        checks["exception-log-bounds-fields-without-calling-tostring"] =
            formatted.Length <= BoundedExceptionLog.MaximumEntryCharacters &&
            formatted.Contains("bounded-check", StringComparison.Ordinal) &&
            formatted.Contains("FIRST EVIDENCE", StringComparison.Ordinal) &&
            formatted.Contains("HRESULT=0x8898040D", StringComparison.Ordinal) &&
            formatted.Contains("[end of bounded entry]", StringComparison.Ordinal);

        Exception withStack;
        try { throw new InvalidOperationException("known-stack-evidence"); }
        catch (Exception error) { withStack = error; }
        string stackRecord = BoundedExceptionLog.Format("stack-check", withStack);
        checks["exception-log-retains-first-cause-and-stack-evidence"] =
            stackRecord.Contains(nameof(CheckLogging), StringComparison.Ordinal) &&
            stackRecord.Contains("known-stack-evidence", StringComparison.Ordinal) &&
            BoundedExceptionLog.Format("wrapped", new Exception("outer", withStack))
                .Contains("known-stack-evidence", StringComparison.Ordinal);

        var unavailableDetails = new ThrowingDetailsException();
        string unavailableRecord = BoundedExceptionLog.Format("fallback", unavailableDetails);
        checks["exception-log-survives-failing-message-and-stack-getters"] =
            unavailableRecord.Contains("HRESULT=0x8007000E", StringComparison.Ordinal) &&
            unavailableRecord.Contains("[message unavailable]", StringComparison.Ordinal) &&
            unavailableRecord.Contains("[stack unavailable]", StringComparison.Ordinal);
        string aggregateRecord = BoundedExceptionLog.Format("aggregate", new AggregateException(unavailableDetails));
        checks["exception-log-does-not-expand-aggregate-message"] =
            aggregateRecord.Contains("Aggregate exception", StringComparison.Ordinal) &&
            aggregateRecord.Contains("HRESULT=0x8007000E", StringComparison.Ordinal);

        bool firstWritten = BoundedExceptionLog.TryAppend(path, "FIRST-RECORD", withStack);
        string first = File.ReadAllText(path);
        int written = 0;
        while (written < 1024 && BoundedExceptionLog.TryAppend(path, "REPEATED-RECORD", large)) written++;
        long saturatedLength = new FileInfo(path).Length;
        byte[] saturatedHash = Hash(path);
        bool appendedAfterLimit = BoundedExceptionLog.TryAppend(path, "REPEATED-RECORD", large);
        string contents = File.ReadAllText(path);
        checks["exception-log-enforces-utf8-byte-cap-under-repetition"] =
            firstWritten && written > 0 && written < 1024 && !appendedAfterLimit &&
            saturatedLength <= BoundedExceptionLog.MaximumFileBytes &&
            saturatedLength == new FileInfo(path).Length && saturatedHash.SequenceEqual(Hash(path)) &&
            Encoding.UTF8.GetByteCount(contents) == saturatedLength;
        checks["exception-log-preserves-first-evidence-and-complete-entry-boundaries"] =
            contents.StartsWith(first, StringComparison.Ordinal) &&
            contents.Split("[end of bounded entry]", StringSplitOptions.None).Length - 1 == written + 1;

        using (var preserved = new FileStream(preservedPath, FileMode.CreateNew, FileAccess.Write))
        {
            preserved.Write(Encoding.UTF8.GetBytes("PRESERVED ORIGINAL EVIDENCE"));
            preserved.SetLength(BoundedExceptionLog.MaximumFileBytes + 17L);
        }
        byte[] originalHash = Hash(preservedPath);
        int detailsReadBeforeFullLog = large.DetailsReadCount;
        bool oversizedWritten = BoundedExceptionLog.TryAppend(preservedPath, "must-not-write", large);
        checks["exception-log-never-truncates-or-appends-to-existing-oversized-evidence"] =
            !oversizedWritten && new FileInfo(preservedPath).Length == BoundedExceptionLog.MaximumFileBytes + 17L &&
            originalHash.SequenceEqual(Hash(preservedPath)) && large.DetailsReadCount == detailsReadBeforeFullLog;

        string fatalPath = preservedPath + ".fatal";
        bool fatalWritten = BoundedExceptionLog.TryAppend(fatalPath, "FIRST-FATAL", large);
        checks["exception-log-separate-fatal-budget-records-despite-full-ordinary-log"] =
            fatalWritten && File.ReadAllText(fatalPath).Contains("FIRST-FATAL", StringComparison.Ordinal) &&
            new FileInfo(fatalPath).Length <= BoundedExceptionLog.MaximumFileBytes &&
            originalHash.SequenceEqual(Hash(preservedPath));

        string lockedPath = Path.Combine(directory, "locked.log");
        using (var locked = new FileStream(lockedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            checks["exception-log-io-failure-is-best-effort-and-nonthrowing"] =
                !BoundedExceptionLog.TryAppend(lockedPath, "locked", large) && locked.Length == 0;
        }

        foreach (bool failInMessage in new[] { true, false })
        {
            bool allLatched = true;
            foreach (Exception failure in new Exception[]
            {
                new OutOfMemoryException("synthetic getter failure"),
                new COMException("synthetic getter failure", unchecked((int)0x8898040D))
            })
            {
                string getterPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".log");
                var incoming = new FatalDetailsException(failure, failInMessage);
                int logAttempts = 0, saveAttempts = 0;
                bool ordinaryInput = false;
                var policy = new UnhandledExceptionPolicy((source, error, fatal) =>
                {
                    logAttempts++;
                    ordinaryInput = !fatal && ReferenceEquals(error, incoming);
                    BoundedExceptionLog.TryAppend(getterPath, source, error);
                }, () => saveAttempts++);
                bool handled = policy.Handle("getter-failure", incoming);
                bool repeatHandled = policy.Handle("repeat", new IOException("ordinary"));
                allLatched &= ordinaryInput && !handled && !repeatHandled && logAttempts == 1 && saveAttempts == 1 &&
                    incoming.MessageReads == 1 && incoming.StackReads == (failInMessage ? 0 : 1) &&
                    File.Exists(getterPath) && new FileInfo(getterPath).Length == 0;
            }
            checks[failInMessage ? "exception-log-fatal-message-getter-reaches-policy-without-retry"
                : "exception-log-fatal-stack-getter-reaches-policy-without-retry"] = allLatched;
        }
    }

    private static void CheckRecoveryWriter(Dictionary<string, bool> checks)
    {
        string directory = SelfTestStorage.PathFor("exception-safety-writer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        static GameState Snapshot() => new() { MatchState = new MatchManager().ToState() };
        var incoming = new InvalidOperationException("ordinary dispatcher failure");

        foreach (bool failInReport in new[] { false, true })
        {
            string path = Path.Combine(directory, failInReport ? "report-failure.json" : "write-failure.json");
            Exception fatal = failInReport ? new OutOfMemoryException("synthetic report failure")
                : new COMException("synthetic commit failure", unchecked((int)0x8898040D));
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var reported = new ManualResetEventSlim();
            int preparations = 0, reports = 0;
            var writer = new CricketRecoveryWriter(path, onError: _ =>
            {
                Interlocked.Increment(ref reports);
                reported.Set();
                if (failInReport) throw fatal;
            }, beforeCommit: () =>
            {
                Interlocked.Increment(ref preparations);
                started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException("Fixture gate was not released.");
                if (failInReport) throw new IOException("synthetic ordinary write failure");
                throw fatal;
            }, checkpointDirectory: Path.Combine(directory, "history"));
            bool reached;
            try
            {
                writer.Queue(Snapshot());
                reached = started.Wait(TimeSpan.FromSeconds(4));
                if (reached) writer.Queue(Snapshot()); // A terminal worker must discard this already-pending write.
            }
            finally { release.Set(); }
            int logs = 0, saves = 0;
            var policy = new UnhandledExceptionPolicy((_, _, _) => logs++, () =>
            {
                saves++;
                writer.Flush(TimeSpan.FromSeconds(4));
            });
            bool handled = policy.Handle("ordinary-input", incoming);
            bool repeatHandled = policy.Handle("repeat", incoming);
            bool reportFinished = reported.Wait(TimeSpan.FromSeconds(4));
            bool queueRejected = ThrowsSame(() => writer.Queue(Snapshot()), fatal);
            bool clearRejected = ThrowsSame(() => writer.Clear(), fatal);
            bool flushRejected = ThrowsSame(() => writer.Flush(TimeSpan.Zero), fatal);
            checks[failInReport ? "recovery-writer-fatal-error-callback-latches-and-reaches-policy"
                : "recovery-writer-fatal-commit-latches-and-reaches-policy"] =
                reached && reportFinished && !handled && !repeatHandled && logs == 1 && saves == 1 &&
                queueRejected && clearRejected && flushRejected && ReferenceEquals(writer.LastError, fatal) &&
                preparations == 1 && reports == 1 && writer.CompletedWrites == 0 && !File.Exists(path);
        }

        string ordinaryPath = Path.Combine(directory, "ordinary-io.json");
        int ordinaryPreparations = 0, ordinaryReports = 0;
        var ordinary = new CricketRecoveryWriter(ordinaryPath, onError: _ => ordinaryReports++, beforeCommit: () =>
        {
            if (Interlocked.Increment(ref ordinaryPreparations) == 1) throw new IOException("synthetic first write failure");
        }, checkpointDirectory: Path.Combine(directory, "ordinary-history"));
        ordinary.Queue(Snapshot());
        bool firstFlushed = ordinary.Flush(TimeSpan.FromSeconds(4));
        bool ordinaryError = ordinary.LastError is IOException;
        ordinary.Queue(Snapshot());
        bool secondFlushed = ordinary.Flush(TimeSpan.FromSeconds(4));
        checks["recovery-writer-ordinary-io-failure-allows-later-successful-save"] =
            !firstFlushed && ordinaryError && secondFlushed && ordinary.LastError is null &&
            ordinaryPreparations == 2 && ordinaryReports == 1 && ordinary.CompletedWrites == 1 &&
            File.Exists(ordinaryPath) && GameStateService.LoadFrom(ordinaryPath)?.Sport == "afl";

        CheckTerminalClearWait(checks, directory);
    }

    private static void CheckTerminalClearWait(Dictionary<string, bool> checks, string directory)
    {
        string path = Path.Combine(directory, "clear-after-terminal-wait.json");
        byte[] original = Encoding.UTF8.GetBytes("PRESERVED LAST RECOVERY BEFORE TERMINAL FAILURE");
        File.WriteAllBytes(path, original);
        using var heldFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var reportEntered = new ManualResetEventSlim();
        using var releaseReport = new ManualResetEventSlim();
        var fatal = new OutOfMemoryException("synthetic clear reporter failure");
        int reports = 0;
        var writer = new CricketRecoveryWriter(path, onError: _ =>
        {
            Interlocked.Increment(ref reports);
            reportEntered.Set();
            if (!releaseReport.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("Fixture clear gate was not released.");
            throw fatal;
        });
        Exception? firstFailure = null, waitingFailure = null;
        var firstClear = new Thread(() =>
        {
            try { writer.Clear(); }
            catch (Exception error) { firstFailure = error; }
        }) { IsBackground = true };
        var waitingClear = new Thread(() =>
        {
            try { writer.Clear(); }
            catch (Exception error) { waitingFailure = error; }
        }) { IsBackground = true };
        bool entered = false, waiting = false, secondStarted = false;
        bool firstFinished, secondFinished;
        firstClear.Start();
        try
        {
            // Windows refuses the locked fixture deletion. Its error callback
            // holds the real commit gate while the second Clear passes its
            // first terminal check and blocks on that monitor.
            entered = reportEntered.Wait(TimeSpan.FromSeconds(4));
            if (entered)
            {
                waitingClear.Start();
                secondStarted = true;
                waiting = SpinWait.SpinUntil(() =>
                    (waitingClear.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(4));
            }
        }
        finally
        {
            // Deletion would now succeed if Clear forgot to recheck terminal
            // state after acquiring the gate. The first callback latches fatal
            // before releasing that gate to the waiting second Clear.
            heldFile.Dispose();
            releaseReport.Set();
            firstFinished = firstClear.Join(TimeSpan.FromSeconds(4));
            secondFinished = !secondStarted || waitingClear.Join(TimeSpan.FromSeconds(4));
        }
        checks["recovery-writer-clear-rechecks-terminal-after-wait-and-preserves-recovery"] =
            entered && waiting && firstFinished && secondFinished && reports == 1 &&
            ReferenceEquals(firstFailure, fatal) && ReferenceEquals(waitingFailure, fatal) &&
            ReferenceEquals(writer.LastError, fatal) && writer.CompletedWrites == 0 &&
            File.Exists(path) && original.SequenceEqual(File.ReadAllBytes(path));
    }

    private static bool ThrowsSame(Action action, Exception expected)
    {
        try { action(); return false; }
        catch (Exception actual) { return ReferenceEquals(actual, expected); }
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private sealed class LargeDetailsException : Exception
    {
        internal int DetailsReadCount { get; private set; }
        internal LargeDetailsException() : base("FIRST EVIDENCE " + new string('\u754c', 10000))
        {
            HResult = unchecked((int)0x8898040D);
        }
        public override string Message { get { DetailsReadCount++; return base.Message; } }
        public override string StackTrace { get { DetailsReadCount++; return new string('\u754c', 10000); } }
        public override string ToString() => throw new InvalidOperationException("Do not use Exception.ToString in bounded logging.");
    }

    private sealed class ThrowingDetailsException : Exception
    {
        internal ThrowingDetailsException() => HResult = unchecked((int)0x8007000E);
        public override string Message => throw new InvalidOperationException("message unavailable");
        public override string StackTrace => throw new InvalidOperationException("stack unavailable");
    }

    private sealed class FatalDetailsException(Exception failure, bool failInMessage) : Exception
    {
        internal int MessageReads { get; private set; }
        internal int StackReads { get; private set; }
        public override string Message
        {
            get { MessageReads++; if (failInMessage) throw failure; return "ordinary incoming exception"; }
        }
        public override string StackTrace
        {
            get { StackReads++; throw failure; }
        }
    }
}
