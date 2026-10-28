// Generated from Services/BroadcastClock.cs; edit translator/adapters.
// Source SHA-256: 3a6f23186febb4a6857331894297dc814b7b59682ff94d4006e6fcc7f27b309a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

namespace Roche_Scoreboard.Services;

internal static class BroadcastClock
{
    internal static string WallTime(DateTime? value = null) => (value ?? DateTime.Now).ToString("h:mm tt");
}
