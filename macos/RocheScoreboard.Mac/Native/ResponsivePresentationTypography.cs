using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Roche_Scoreboard.Views;

internal static class ResponsivePresentationTypography
{
    private sealed record Design(double Size);
    private static readonly ConditionalWeakTable<TextBlock, Design> Sizes = new();
    internal static void Apply(Visual root, double scale)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        ApplyRecursive(root, scale, true);
    }
    private static void ApplyRecursive(Visual current, double scale, bool isRoot = false)
    {
        if (!isRoot && current is PresentationScoreBar or Canvas) return;
        if (current is TextBlock text)
            text.FontSize = Math.Max(3.5, Sizes.GetValue(text, t => new Design(t.FontSize)).Size * scale);
        foreach (var child in current.GetVisualChildren()) ApplyRecursive(child, scale);
    }
}
