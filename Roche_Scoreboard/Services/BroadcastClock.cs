using System;

namespace Roche_Scoreboard.Services;

internal static class BroadcastClock
{
    internal static string WallTime(DateTime? value = null) => (value ?? DateTime.Now).ToString("h:mm tt");
}
