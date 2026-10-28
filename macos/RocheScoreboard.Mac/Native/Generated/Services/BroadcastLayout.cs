// Generated from Services/BroadcastLayout.cs; edit translator/adapters.
// Source SHA-256: bfc944f6d186c8b188a396755a65a5905f9ee4fadca9c8657c2f4081c5363994
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;

namespace Roche_Scoreboard.Services;

/// <summary>Fit the design canvas to the output shape without stretching type or logos.</summary>
internal static class BroadcastLayout
{
    internal static void FitCanvas(Control canvas, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        double aspect = width / height;
        double designWidth = Math.Max(960, 540 * aspect);
        double designHeight = designWidth / aspect;
        if (Math.Abs(canvas.Width - designWidth) > .01) canvas.Width = designWidth;
        if (Math.Abs(canvas.Height - designHeight) > .01) canvas.Height = designHeight;
    }
}
