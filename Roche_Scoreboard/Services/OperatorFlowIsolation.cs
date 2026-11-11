using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Process-latched sink policy for the explicit AFL operator integration route.
/// No WPF dependencies: translated production callers retain the same no-op
/// policy unless the Windows QA entry point explicitly activates it.
/// </summary>
internal static class OperatorFlowIsolation
{
    private static int _enabled;
    private static readonly object Gate = new();
    private static readonly AsyncLocal<string?> ExpectedProbe = new();
    private static readonly Dictionary<string, SinkCounts> Counts = new(StringComparer.Ordinal);
    internal static bool IsEnabled => Volatile.Read(ref _enabled) != 0;
    internal static void Enable() => Interlocked.Exchange(ref _enabled, 1);

    internal sealed class SinkCounts
    {
        public int attempts { get; set; }
        public int suppressed { get; set; }
        public int denied { get; set; }
        public int expectedProbes { get; set; }
        public int violations { get; set; }
    }

    internal sealed class DeniedException(string sink) : InvalidOperationException("Isolated AFL flow denied sink: " + sink)
    {
        internal string Sink { get; } = sink;
    }

    internal static bool SuppressAutomatic(string sink)
    {
        if (!IsEnabled) return false;
        if (sink is not ("output-afl" or "weather-automatic"))
            throw new ArgumentOutOfRangeException(nameof(sink));
        lock (Gate)
        {
            var count = Get(sink); ++count.attempts; ++count.suppressed;
        }
        return true;
    }

    internal static void Deny(string sink)
    {
        if (!IsEnabled) return;
        lock (Gate)
        {
            var count = Get(sink); ++count.attempts; ++count.denied;
            if (ExpectedProbe.Value == sink) ++count.expectedProbes;
            else ++count.violations;
        }
        throw new DeniedException(sink);
    }

    internal static IDisposable ExpectDenial(string sink)
    {
        if (!IsEnabled || ExpectedProbe.Value is not null)
            throw new InvalidOperationException("Denial probes require the isolated route and cannot nest.");
        ExpectedProbe.Value = sink;
        return new ProbeScope();
    }

    private sealed class ProbeScope : IDisposable
    {
        public void Dispose() => ExpectedProbe.Value = null;
    }

    private static SinkCounts Get(string sink)
    {
        if (!Counts.TryGetValue(sink, out var count)) Counts[sink] = count = new();
        return count;
    }

    internal static Dictionary<string, SinkCounts> Snapshot()
    {
        lock (Gate) return Counts.ToDictionary(item => item.Key, item => new SinkCounts
        {
            attempts = item.Value.attempts, suppressed = item.Value.suppressed,
            denied = item.Value.denied, expectedProbes = item.Value.expectedProbes,
            violations = item.Value.violations
        }, StringComparer.Ordinal);
    }
}
