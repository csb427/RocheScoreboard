// Generated from Services/CricketReliabilityChecks.cs; edit translator/adapters.
// Source SHA-256: a78c6334f2a97aaa7267783b8f0a566c968669fa792e054fd61956a7228a00b0
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Isolated cricket regressions; never reads or writes operator data.</summary>
internal static class CricketReliabilityChecks
{
    internal static CricketMatchManager CreateMatch(bool ready = false, int overs = 20)
    {
        CricketSetupResult setup = new();
        setup.FillEmptySquads();
        CricketMatchManager match = new()
        {
            TeamAPlayers = setup.TeamAPlayers,
            TeamBPlayers = setup.TeamBPlayers,
            TotalOvers = overs
        };
        match.StartMatch();
        if (ready) SelectPlayers(match);
        return match;
    }

    private static void SelectPlayers(CricketMatchManager match)
    {
        CricketInnings innings = match.CurrentInnings!;
        if (innings.Striker is null) match.SetStriker(0);
        if (innings.NonStriker is null) match.SetNonStriker(1);
        if (innings.NeedsBatterSelection)
        {
            int next = innings.BattingOrder.FindIndex(player => !player.IsOut
                && !ReferenceEquals(player, innings.Striker) && !ReferenceEquals(player, innings.NonStriker));
            if (next >= 0) match.SelectNewBatter(next);
        }
        if (innings.NeedsBowlerSelection) match.ConfirmBowler(match.SuggestedNextBowler());
    }
}
