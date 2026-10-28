// Generated from Services/CricketOverTracker.cs; edit translator/adapters.
// Source SHA-256: 1e2668ffc423a9512556f9f13615612732c9734eaae9f5a093d16fbce7cd369c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
namespace Roche_Scoreboard.Services;

/// <summary>One position per delivery plus remaining counted balls, honoring the recorded replacement rules.</summary>
internal static class CricketOverTracker
{
    internal static string[] CreateSlots(Models.CricketInnings innings)
    {
        var values = innings.CurrentOverBalls;
        int uncounted = values.All(string.IsNullOrEmpty) ? 0
            : innings.Deliveries.Skip(Math.Max(0, innings.Deliveries.Count - values.Count)).Count(delivery => !delivery.IsCountedBall);
        var slots = new string[Math.Max(6 + uncounted, values.Count)];
        Array.Fill(slots, "");
        for (int i = 0; i < values.Count; i++) slots[i] = values[i];
        return slots;
    }
    internal static string[] CreateSlots(IReadOnlyList<string> deliveries)
    {
        int illegal = deliveries.Count(ball => ball.Contains("wd", StringComparison.OrdinalIgnoreCase)
            || ball.Contains("nb", StringComparison.OrdinalIgnoreCase));
        var slots = new string[Math.Max(6 + illegal, deliveries.Count)];
        Array.Fill(slots, "");
        for (int i = 0; i < deliveries.Count; i++) slots[i] = deliveries[i];
        return slots;
    }
}
