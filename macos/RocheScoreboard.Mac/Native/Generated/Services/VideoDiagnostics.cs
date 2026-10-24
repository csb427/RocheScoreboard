// Generated from Services/VideoDiagnostics.cs; edit translator/adapters.
// Source SHA-256: 04ba4cde9356bddf4a81f92a3b43c704374dfab0ad65da25e275c274ad0df03a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Severity of a <see cref="VideoDiagnosticEntry"/>. Kept deliberately
/// small so the operator-facing telemetry HUD can colour-code entries
/// without translating arbitrary strings.
/// </summary>
internal enum VideoLogSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Single structured log record produced by the video pipeline. The
/// ring buffer and file appender both consume the same record type so
/// the operator HUD and the on-disk log stay consistent.
/// </summary>
internal sealed record VideoDiagnosticEntry(
    DateTime TimestampUtc,
    VideoLogSeverity Severity,
    string Category,
    string Message,
    string? ExceptionText);

/// <summary>
/// Thread-safe diagnostic sink for the entire video pipeline. Used by
/// <see cref="VlcRuntime"/>, <c>VideoPlayerControl</c>, and the takeover
/// model in <c>MainWindow.RestoreMembers.cs</c> so that every state
/// transition (init, source resolution, hardware-decode attempt,
/// software fallback, playback start/stop, takeover entry/exit,
/// watchdog trip, emergency fallback) is captured with the same
/// structure. This makes black-screen and stall reports diagnosable
/// after the fact instead of relying on operator memory.
/// </summary>
internal static class VideoDiagnostics
{
    private const int RingCapacity = 256;

    private static readonly object _gate = new();
    private static readonly LinkedList<VideoDiagnosticEntry> _ring = new();
    private static readonly ConcurrentQueue<string> _fileQueue = new();
    private static string? _logFilePath;
    private static bool _fileWriterStarted;

    /// <summary>
    /// Raised whenever a new entry is appended. The operator telemetry
    /// HUD subscribes to this on the UI thread to update its status
    /// line; subscribers MUST marshal back to the dispatcher because
    /// log calls can originate from any thread (libvlc callbacks, timers).
    /// </summary>
    public static event EventHandler<VideoDiagnosticEntry>? EntryAdded;

    /// <summary>The most recently appended entry, or <c>null</c> when the buffer is empty.</summary>
    public static VideoDiagnosticEntry? Last
    {
        get
        {
            lock (_gate) return _ring.Last?.Value;
        }
    }

    /// <summary>Snapshot of the in-memory ring buffer, newest first.</summary>
    public static IReadOnlyList<VideoDiagnosticEntry> RecentEntries()
    {
        lock (_gate)
        {
            return _ring.Reverse().ToList();
        }
    }

    public static void Info(string category, string message)
        => Append(VideoLogSeverity.Info, category, message, null);

    public static void Warn(string category, string message, Exception? ex = null)
        => Append(VideoLogSeverity.Warning, category, message, ex);

    public static void Error(string category, string message, Exception? ex = null)
        => Append(VideoLogSeverity.Error, category, message, ex);

    private static void Append(VideoLogSeverity severity, string category, string message, Exception? ex)
    {
        VideoDiagnosticEntry entry = new(
            DateTime.UtcNow,
            severity,
            category,
            message,
            ex?.ToString());

        lock (_gate)
        {
            _ring.AddLast(entry);
            while (_ring.Count > RingCapacity)
                _ring.RemoveFirst();
        }

        TryAppendToFile(entry);

        try { EntryAdded?.Invoke(null, entry); }
        catch { /* a diagnostic listener must never break playback */ }
    }

    /// <summary>
    /// Returns the path the diagnostics file appender writes to. Resolved
    /// lazily so unit-test hosts that don't have a profile directory don't
    /// crash on assembly load.
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (_logFilePath is not null) return _logFilePath;
            try
            {
                string root = NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(root, "Roche_Scoreboard");
                Directory.CreateDirectory(dir);
                _logFilePath = Path.Combine(dir, "video.log");
            }
            catch
            {
                // If LocalAppData is unavailable, fall back to the temp folder.
                _logFilePath = Path.Combine(Path.GetTempPath(), "Roche_Scoreboard_video.log");
            }
            return _logFilePath;
        }
    }

    private static void TryAppendToFile(VideoDiagnosticEntry entry)
    {
        try
        {
            StringBuilder sb = new();
            sb.Append(entry.TimestampUtc.ToString("O"));
            sb.Append(" [").Append(entry.Severity).Append("] ");
            sb.Append(entry.Category).Append(": ").Append(entry.Message);
            if (!string.IsNullOrEmpty(entry.ExceptionText))
                sb.Append(" | ").Append(entry.ExceptionText.Replace(Environment.NewLine, " ⏎ "));

            _fileQueue.Enqueue(sb.ToString());
            EnsureFileWriter();
        }
        catch
        {
            // Logging must never throw — swallow.
        }
    }

    private static void EnsureFileWriter()
    {
        if (_fileWriterStarted) return;
        lock (_gate)
        {
            if (_fileWriterStarted) return;
            _fileWriterStarted = true;
        }

        // Background flush — single thread, fire-and-forget, keeps log
        // writes off the UI/libvlc threads even on slow disks.
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            while (true)
            {
                try
                {
                    if (_fileQueue.TryDequeue(out string? line))
                    {
                        File.AppendAllText(LogFilePath, line + Environment.NewLine);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(150);
                    }
                }
                catch
                {
                    System.Threading.Thread.Sleep(500);
                }
            }
        });
    }
}
