// Generated from Services/BoundedExceptionLog.cs; edit translator/adapters.
// Source SHA-256: f81d708038e61813fcd9e40fba7469eae93d4c850bda7ed94c5973cb38fc4bde
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Roche_Scoreboard.Services;

/// <summary>Append-only diagnostics. Existing evidence is never truncated, rotated or deleted.</summary>
internal static class BoundedExceptionLog
{
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int MaximumEntryCharacters = 4096;
    private static readonly object Sync = new();

    internal static bool TryAppend(string path, string source, Exception error)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                // Check before formatting: an old oversized log must cost no further formatting or writes.
                if (file.Length >= MaximumFileBytes)
                    return false;

                byte[] entry = Encoding.UTF8.GetBytes(Format(source, error));
                if (entry.Length > MaximumFileBytes - file.Length)
                    return false;

                file.Position = file.Length;
                file.Write(entry);
                return true;
            }
        }
        catch (Exception failure) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(failure))
        {
            return false; // Ordinary diagnostic I/O is best-effort; resource exhaustion reaches the policy.
        }
    }

    internal static string Format(string source, Exception error)
    {
        var text = new StringBuilder(MaximumEntryCharacters);
        Append(text, "[");
        Append(text, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        Append(text, "] ");
        Append(text, source, 128);
        Append(text, "\n");
        int remainingExceptions = 8;
        AppendException(text, error, ref remainingExceptions);
        // Always leave an explicit boundary, including after an oversized field.
        if (text.Length > MaximumEntryCharacters - 24)
            text.Length = MaximumEntryCharacters - 24;
        Append(text, "\n[end of bounded entry]\n");
        return text.ToString();
    }

    private static void AppendException(StringBuilder text, Exception error, ref int remaining)
    {
        if (remaining-- <= 0 || text.Length >= MaximumEntryCharacters - 24)
            return;

        Append(text, error.GetType().FullName, 192);
        Append(text, " HRESULT=0x");
        Append(text, error.HResult.ToString("X8", CultureInfo.InvariantCulture));
        Append(text, "\n");
        // AggregateException.Message concatenates every inner message before we can cap it.
        try { Append(text, error is AggregateException ? "Aggregate exception" : error.Message, 512); }
        catch (Exception failure) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(failure))
        { Append(text, "[message unavailable]"); }
        Append(text, "\n");
        // Retain the causal exception before stack text can exhaust the entry budget.
        if (error is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                if (remaining <= 0) break;
                Append(text, "Caused by: ");
                AppendException(text, inner, ref remaining);
            }
        }
        else if (error.InnerException is { } cause)
        {
            Append(text, "Caused by: ");
            AppendException(text, cause, ref remaining);
        }
        try { Append(text, error.StackTrace, 1536); }
        catch (Exception failure) when (!UnhandledExceptionPolicy.IsFatalResourceFailure(failure))
        { Append(text, "[stack unavailable]"); }
        Append(text, "\n");
    }

    private static void Append(StringBuilder text, string? value, int fieldLimit = MaximumEntryCharacters)
    {
        if (string.IsNullOrEmpty(value)) return;
        int count = Math.Min(value.Length, Math.Min(fieldLimit, MaximumEntryCharacters - text.Length));
        text.Append(value, 0, count);
        if (count < value.Length && text.Length + 3 <= MaximumEntryCharacters)
            text.Append("...");
    }
}
