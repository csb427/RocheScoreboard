// Generated from Services/UnhandledExceptionPolicy.cs; edit translator/adapters.
// Source SHA-256: 89c7177e64819418b1cddf8a3622d5ebfdb21bd3b941db63cab04ea4e25e6c12
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>Decides whether the dispatcher can continue. No dependency on WPF or process exit.</summary>
internal sealed class UnhandledExceptionPolicy
{
    private readonly Action<string, Exception, bool> _log;
    private readonly Action _save;
    private int _state; // 0 = idle, 1 = handling, 2 = terminal (never reset)

    internal UnhandledExceptionPolicy(Action<string, Exception, bool> log, Action save)
    {
        _log = log;
        _save = save;
    }

    /// <returns>True only when it is safe to retain the existing recoverable-error behavior.</returns>
    internal bool Handle(string source, Exception error, bool terminating = false, bool saveState = true)
    {
        // A second notification during recovery must not recursively log or save.
        // The outer call owns the one best-effort attempt and must not resume rendering.
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            Interlocked.CompareExchange(ref _state, 2, 1);
            return false;
        }

        try
        {
            bool fatal = terminating || IsFatalResourceFailure(error) || Volatile.Read(ref _state) == 2;
            if (fatal)
                Volatile.Write(ref _state, 2); // latch before either callback can re-enter

            try { _log(source, error, fatal); }
            catch (Exception secondary)
            {
                // Recovery itself can exhaust resources. Latch directly rather
                // than recursively logging the secondary failure or retrying.
                if (IsFatalResourceFailure(secondary)) Volatile.Write(ref _state, 2);
            }

            if (saveState)
            {
                try { _save(); }
                catch (Exception secondary)
                {
                    if (IsFatalResourceFailure(secondary)) Volatile.Write(ref _state, 2);
                }
            }

            return Volatile.Read(ref _state) != 2;
        }
        finally
        {
            Interlocked.CompareExchange(ref _state, 0, 1);
        }
    }

    internal static bool IsFatalResourceFailure(Exception error)
    {
        int remaining = 256;
        return IsFatalResourceFailure(error, 0, ref remaining);
    }

    private static bool IsFatalResourceFailure(Exception error, int depth, ref int remaining)
    {
        // Do not allocate a flattened aggregate or recurse indefinitely under memory pressure.
        if (error is OutOfMemoryException || error.HResult is
            unchecked((int)0x8007000E) or // E_OUTOFMEMORY
            unchecked((int)0x8898040D))   // WPF UCEERR_OUTOFHANDLES
            return true;

        if (depth >= 32 || remaining-- <= 0)
            return true; // an excessively nested unhandled failure is not safe to resume

        if (error is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
                if (IsFatalResourceFailure(inner, depth + 1, ref remaining))
                    return true;
            return false;
        }

        return error.InnerException is { } cause && IsFatalResourceFailure(cause, depth + 1, ref remaining);
    }
}
