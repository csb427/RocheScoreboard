// Generated from Services/OperatorInsetConverter.cs; edit translator/adapters.
// Source SHA-256: 9a857850602419abf2862f7ea8ffe1f0dad53615982a99a83a6636c718e66d16
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Globalization;
using Avalonia.Data;

namespace Roche_Scoreboard.Services;

/// <summary>Preserve text space in the existing compact command controls without changing their targets.</summary>
public sealed class OperatorInsetConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Count < 4 || values[2] is not Thickness padding) return new Thickness(0);
        double width = values[0] is double w ? w : 0, height = values[1] is double h ? h : 0;
        double font = values[3] is double f ? f : 15;
        // Only explicit constraints are safe here. Feeding Bounds.Height back into
        // an Auto-sized presenter's margin creates a shrinking layout loop.
        double horizontal = double.IsFinite(width) && width > 0 ? Math.Max(0, (width - font * 2.4) / 2 - 1) : double.PositiveInfinity;
        double vertical = double.IsFinite(height) && height > 0 ? Math.Max(0, (height - font * 1.45) / 2 - 1) : double.PositiveInfinity;
        return new Thickness(Math.Min(padding.Left, horizontal), Math.Min(padding.Top, vertical), Math.Min(padding.Right, horizontal), Math.Min(padding.Bottom, vertical));
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
