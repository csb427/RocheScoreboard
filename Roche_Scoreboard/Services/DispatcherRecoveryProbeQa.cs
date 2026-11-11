using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Isolated, windowless integration probe of the real dispatcher exception subscription.
/// The synthetic sentinel is intercepted only AFTER the production handler's decision.
/// This proves delivery and Handled assignment, not fatal termination, WER, real resource
/// exhaustion, AppDomain notification, or match persistence. Use a fresh process per case.
/// </summary>
internal sealed class DispatcherRecoveryProbeQa
{
    private const string OrdinaryArgument = "--operator-dispatcher-policy-probe=ordinary";
    private const string FatalArgument = "--operator-dispatcher-policy-probe=fatal";
    private const int SetupFailure = 71, VerificationFailure = 72, IoFailure = 73;
    private const int MaximumReportBytes = 32 * 1024;
    private readonly App _app;
    private readonly Exception _sentinel;
    private readonly ProbeReport _report;
    private bool _subscribed;

    internal static bool IsProbeArgument(string argument) =>
        string.Equals(argument, OrdinaryArgument, StringComparison.Ordinal) ||
        string.Equals(argument, FatalArgument, StringComparison.Ordinal);

    /// <summary>Call immediately after InstallGlobalExceptionHandlers, before production startup.</summary>
    internal static bool TryRun(App app, string[] arguments)
    {
        string? argument = arguments.FirstOrDefault(IsProbeArgument);
        if (argument is null)
            return false;

        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // The startup router must latch isolation. This helper never enables it itself.
        if (!SelfTestStorage.IsEnabled || !app.IsSelfTestMode)
        {
            app.Shutdown(SetupFailure);
            return true;
        }

        new DispatcherRecoveryProbeQa(app, argument == FatalArgument).Start(arguments.Length == 1);
        return true;
    }

    private DispatcherRecoveryProbeQa(App app, bool fatal)
    {
        _app = app;
        string mode = fatal ? "fatal" : "ordinary";
        string sentinel = $"RocheScoreboard.DispatcherRecoveryProbe.{mode}.pid={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}";
        _sentinel = fatal
            ? new COMException(sentinel, unchecked((int)0x8898040D))
            : new InvalidOperationException(sentinel);
        _report = new ProbeReport
        {
            Mode = mode,
            ProcessId = Environment.ProcessId,
            StartedUtc = DateTimeOffset.UtcNow,
            SelfTestStorageRoot = SelfTestStorage.RootPath,
            SelfTestStorageEnabled = SelfTestStorage.IsEnabled,
            ExpectedHandled = !fatal,
            DiagnosticBaseline = App.DiagnosticExceptionCount,
            Sentinel = sentinel,
            ExceptionType = _sentinel.GetType().FullName!,
            ExceptionHResult = _sentinel.HResult,
            ExpectedLogRelativePath = fatal ? "diagnostics/crash.log.fatal" : "diagnostics/crash.log"
        };
    }

    private string ExpectedLogPath => SelfTestStorage.PathFor("diagnostics",
        _report.Mode == "fatal" ? "crash.log.fatal" : "crash.log");

    private string OppositeLogPath => SelfTestStorage.PathFor("diagnostics",
        _report.Mode == "fatal" ? "crash.log" : "crash.log.fatal");

    private void Start(bool hasOnlyProbeArgument)
    {
        try
        {
            if (!hasOnlyProbeArgument || !_app.Dispatcher.CheckAccess() ||
                _app.MainWindow is not null || _app.Windows.Count != 0)
            {
                _report.Errors.Add("Probe requires exactly one fixed argument and no application windows.");
                WriteReportAndShutdown(SetupFailure);
                return;
            }

            // Do not erase evidence or allow a reused PID directory to provide a stale pass.
            if (File.Exists(SelfTestStorage.PathFor("dispatcher-probe", "result.json")))
            {
                _app.Shutdown(IoFailure);
                return;
            }
            if (ReadBoundedLog(ExpectedLogPath).Contains(_report.Sentinel, StringComparison.Ordinal) ||
                ReadBoundedLog(OppositeLogPath).Contains(_report.Sentinel, StringComparison.Ordinal))
            {
                _report.Errors.Add("The sentinel already exists in diagnostic evidence.");
                WriteReportAndShutdown(SetupFailure);
                return;
            }

            // Startup calls us after its actual production subscription. No policy call or
            // fabricated DispatcherUnhandledExceptionEventArgs can satisfy this probe.
            _app.DispatcherUnhandledException += ObserveException;
            _subscribed = true;
            _app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                _report.QueuedActionRan = true;
                throw _sentinel;
            }));
            // BeginInvoke(Action), unlike InvokeAsync/Task.Run, delivers the synchronous
            // exception to WPF's unhandled event. Completion also fails if delivery is absent.
            _app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Complete));
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            _report.Errors.Add("Probe setup failed: " + error.GetType().Name);
            WriteReportAndShutdown(IoFailure);
        }
    }

    private void ObserveException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        if (!ReferenceEquals(args.Exception, _sentinel))
        {
            _report.ObservedEventCount++;
            _report.UnexpectedEventCount++;
            return; // Never change the production decision for any unrelated exception.
        }

        bool originalHandled = args.Handled;
        args.Handled = true; // First action after copying the decision; before any report I/O.
        _report.ObservedEventCount++;
        _report.MatchedEventCount++;
        if (_report.MatchedEventCount == 1)
        {
            _report.OriginalHandled = originalHandled;
            _report.ObserverRanOnDispatcher = _app.Dispatcher.CheckAccess() &&
                ReferenceEquals(args.Dispatcher, _app.Dispatcher);
            _report.MainWindowIsNullAtEvent = _app.MainWindow is null;
            _report.WindowCountAtEvent = _app.Windows.Count;
        }
    }

    private void Complete()
    {
        try
        {
            string expectedLog = ReadBoundedLog(ExpectedLogPath);
            string oppositeLog = ReadBoundedLog(OppositeLogPath);
            _report.LogReadWithinBounds = true;
            _report.ExpectedLogContainsSentinel = expectedLog.Contains(_report.Sentinel, StringComparison.Ordinal);
            _report.OppositeLogContainsSentinel = oppositeLog.Contains(_report.Sentinel, StringComparison.Ordinal);
            _report.ExpectedLogContainsDispatcherSource = expectedLog.Contains(
                "] DispatcherUnhandledException\n", StringComparison.Ordinal);
            string expectedEntry = "] DispatcherUnhandledException\n" + _report.ExceptionType +
                " HRESULT=0x" + _report.ExceptionHResult.ToString("X8", CultureInfo.InvariantCulture) +
                "\n" + _report.Sentinel + "\n";
            _report.ExpectedLogEntryMatches = expectedLog.Contains(expectedEntry, StringComparison.Ordinal);
            _report.ExpectedLogSentinelCount = CountOccurrences(expectedLog, _report.Sentinel + "\n");
            CaptureFinalState();

            Check(_report.QueuedActionRan, "The queued synchronous action did not run.");
            Check(_report.ObservedEventCount == 1 && _report.MatchedEventCount == 1 &&
                _report.UnexpectedEventCount == 0, "Expected exactly one matching dispatcher exception and no unrelated events.");
            Check(_report.OriginalHandled == _report.ExpectedHandled, "The production Handled decision was incorrect or absent.");
            Check(_report.ObserverRanOnDispatcher, "The observer did not execute on the application dispatcher.");
            Check(_report.DiagnosticDelta == 1, "Expected exactly one production diagnostic increment.");
            Check(_report.MainWindowIsNullAtEvent == true && _report.WindowCountAtEvent == 0 &&
                _report.MainWindowIsNull && _report.WindowCount == 0, "Application windows were created during the probe.");
            Check(_report.ExpectedLogEntryMatches && _report.ExpectedLogSentinelCount == 1 &&
                !_report.OppositeLogContainsSentinel, "The sentinel was absent, duplicated, or logged under the wrong recovery classification.");
            WriteReportAndShutdown(_report.Errors.Count == 0 ? 0 : VerificationFailure);
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            _report.Errors.Add("Probe verification failed: " + error.GetType().Name);
            WriteReportAndShutdown(IoFailure);
        }
    }

    private void Check(bool condition, string failure)
    {
        if (!condition)
            _report.Errors.Add(failure);
    }

    private void CaptureFinalState()
    {
        _report.DiagnosticAfter = App.DiagnosticExceptionCount;
        _report.DiagnosticDelta = _report.DiagnosticAfter - _report.DiagnosticBaseline;
        _report.MainWindowIsNull = _app.MainWindow is null;
        _report.WindowCount = _app.Windows.Count;
    }

    private void WriteReportAndShutdown(int exitCode)
    {
        if (_subscribed)
        {
            _app.DispatcherUnhandledException -= ObserveException;
            _subscribed = false;
        }
        try
        {
            CaptureFinalState();
            _report.CompletedUtc = DateTimeOffset.UtcNow;
            _report.ExitCode = exitCode;
            _report.Success = exitCode == 0;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(_report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            if (json.Length > MaximumReportBytes)
                throw new InvalidDataException("Probe report exceeded its byte limit.");
            string path = SelfTestStorage.PathFor("dispatcher-probe", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(json);
        }
        catch (Exception error) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(error))
        {
            // Missing, partial, or stale output and any nonzero exit must fail externally.
            // No retries, alternate output paths, counter resets, or diagnostic-log writes.
            exitCode = IoFailure;
        }
        finally
        {
            _app.Shutdown(exitCode);
        }
    }

    private static string ReadBoundedLog(string path)
    {
        if (!File.Exists(path))
            return string.Empty;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > BoundedExceptionLog.MaximumFileBytes)
            throw new InvalidDataException("Diagnostic log exceeded its byte limit.");
        byte[] bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.Length != bytes.Length)
            throw new InvalidDataException("Diagnostic log changed during bounded reading.");
        return Encoding.UTF8.GetString(bytes);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }
        return count;
    }

    private sealed class ProbeReport
    {
        public int SchemaVersion { get; } = 1;
        public string Mode { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public DateTimeOffset StartedUtc { get; init; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public string SelfTestStorageRoot { get; init; } = string.Empty;
        public bool SelfTestStorageEnabled { get; init; }
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public bool QueuedActionRan { get; set; }
        public int ObservedEventCount { get; set; }
        public int MatchedEventCount { get; set; }
        public int UnexpectedEventCount { get; set; }
        public bool? OriginalHandled { get; set; }
        public bool ExpectedHandled { get; init; }
        public bool ObserverRanOnDispatcher { get; set; }
        public int DiagnosticBaseline { get; init; }
        public int DiagnosticAfter { get; set; }
        public int DiagnosticDelta { get; set; }
        public bool? MainWindowIsNullAtEvent { get; set; }
        public int? WindowCountAtEvent { get; set; }
        public bool MainWindowIsNull { get; set; }
        public int WindowCount { get; set; }
        public string Sentinel { get; init; } = string.Empty;
        public string ExceptionType { get; init; } = string.Empty;
        public int ExceptionHResult { get; init; }
        public string ExpectedLogRelativePath { get; init; } = string.Empty;
        public bool ExpectedLogContainsSentinel { get; set; }
        public bool OppositeLogContainsSentinel { get; set; }
        public bool ExpectedLogContainsDispatcherSource { get; set; }
        public bool ExpectedLogEntryMatches { get; set; }
        public int ExpectedLogSentinelCount { get; set; }
        public bool LogReadWithinBounds { get; set; }
        public List<string> Errors { get; } = new();
    }
}
