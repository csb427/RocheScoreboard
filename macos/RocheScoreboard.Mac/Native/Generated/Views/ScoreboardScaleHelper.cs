// Generated from Views/ScoreboardScaleHelper.cs; edit translator/adapters.
// Source SHA-256: 3b74c2a3072d356a29540aa2b71e5c3c29628344d893ab8eedb1e0acc33fd484
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

namespace Roche_Scoreboard.Views;

internal static class ScoreboardScaleHelper
{
    internal const double DesignWidth = 386.0;
    internal const double DesignHeight = 193.0;

    internal static double GetScale(double actualWidth, double actualHeight)
    {
        if (actualWidth <= 0 || actualHeight <= 0)
            return 1.0;

        return Math.Min(actualWidth / DesignWidth, actualHeight / DesignHeight);
    }

    internal static double Scale(double designValue, double scale)
    {
        if (designValue <= 0)
            return 0;

        if (scale <= 0)
            return designValue;

        return designValue * scale;
    }

    internal static Thickness Scale(Thickness designThickness, double scale)
        => new(
            Scale(designThickness.Left, scale),
            Scale(designThickness.Top, scale),
            Scale(designThickness.Right, scale),
            Scale(designThickness.Bottom, scale));
}
