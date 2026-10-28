using System;
using System.Windows;

namespace Roche_Scoreboard.Services;

/// <summary>Fit the design canvas to the output shape without stretching type or logos.</summary>
internal static class BroadcastLayout
{
    internal static void FitCanvas(FrameworkElement canvas, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        double aspect = width / height;
        double designWidth = Math.Max(960, 540 * aspect);
        double designHeight = designWidth / aspect;
        if (Math.Abs(canvas.Width - designWidth) > .01) canvas.Width = designWidth;
        if (Math.Abs(canvas.Height - designHeight) > .01) canvas.Height = designHeight;
    }
}
