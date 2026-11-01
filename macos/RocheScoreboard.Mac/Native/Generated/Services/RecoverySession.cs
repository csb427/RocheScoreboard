// Generated from Services/RecoverySession.cs; edit translator/adapters.
// Source SHA-256: f864b63ff663255457c55652fe5538dd35ce779b746c58eda226a9f9b7eb378f
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>A description of the existing recovery slot, never a second match history.</summary>
internal sealed record RecoverySession(string Title, string Detail)
{
    internal static RecoverySession? Describe(GameState? state)
    {
        if (state is null) return null;
        string saved = state.SavedAtUtc is { } time
            ? "Saved " + time.ToLocalTime().ToString("g") : "Saved time unavailable (older save)";
        if (state.CricketMatchState is { } cricket)
        {
            // Check the same structure required by the restore path. Reject malformed
            // payloads rather than advertising a file that cannot be resumed.
            if (cricket.AllInnings is not { Count: > 0 } || cricket.TeamAPlayers is null || cricket.TeamBPlayers is null || cricket.Messages is null ||
                cricket.TeamAPlayers.Exists(p => p is null) || cricket.TeamBPlayers.Exists(p => p is null) ||
                cricket.AllInnings.Exists(i => i is null || i.Deliveries is null || i.BattingOrder is null || i.BowlingAttack is null || i.BattingOrder.Exists(p => p is null) || i.BowlingAttack.Exists(p => p is null))) return null;
            var innings = cricket.AllInnings[Math.Clamp(cricket.CurrentInningsNumber, 1, cricket.AllInnings.Count) - 1];
            return new($"{cricket.TeamAName} v {cricket.TeamBName}",
                $"Cricket · innings {Math.Clamp(cricket.CurrentInningsNumber, 1, cricket.AllInnings.Count)} · {innings.TotalRuns}/{innings.TotalWickets} · {saved}");
        }
        if (string.Equals(state.Sport, "cricket", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrWhiteSpace(state.Sport) && !string.Equals(state.Sport, "afl", StringComparison.OrdinalIgnoreCase)) return null;
        if (state.MatchState is not { } afl || state.TickerSlots is null || state.Messages is null || state.TickerSlots.Exists(s => s is null) || (afl.Events?.Exists(e => e is null) ?? false)) return null;
        return new($"{afl.HomeName ?? "HOME"} v {afl.AwayName ?? "AWAY"}",
            $"AFL · quarter {Math.Clamp(afl.Quarter, 1, 4)} · {(long)afl.HomeGoals * 6 + afl.HomeBehinds}–{(long)afl.AwayGoals * 6 + afl.AwayBehinds} · {saved}");
    }
}
