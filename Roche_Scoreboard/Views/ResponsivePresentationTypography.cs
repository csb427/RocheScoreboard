using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Roche_Scoreboard.Views;

/// <summary>Scales presentation text from the 386 × 193 design canvas without value-driven font changes.</summary>
internal static class ResponsivePresentationTypography
{
    private static readonly DependencyProperty DesignFontSizeProperty = DependencyProperty.RegisterAttached(
        "DesignFontSize", typeof(double), typeof(ResponsivePresentationTypography), new PropertyMetadata(double.NaN));

    internal static void Apply(DependencyObject root, double scale)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        ApplyRecursive(root, scale, isRoot: true);
    }

    private static void ApplyRecursive(DependencyObject current, double scale, bool isRoot = false)
    {
        if (!isRoot && current is PresentationScoreBar) return;

        if (current is TextBlock text)
        {
            double design = (double)text.GetValue(DesignFontSizeProperty);
            if (double.IsNaN(design))
            {
                design = text.FontSize;
                text.SetValue(DesignFontSizeProperty, design);
            }
            text.FontSize = Math.Max(3.5, design * scale);
        }

        int count = VisualTreeHelper.GetChildrenCount(current);
        for (int i = 0; i < count; i++) ApplyRecursive(VisualTreeHelper.GetChild(current, i), scale);
    }
}
