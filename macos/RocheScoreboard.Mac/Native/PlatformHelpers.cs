using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RocheScoreboard.Mac.Native;

public static class ColorConverter
{
    public static object ConvertFromString(string value) => Color.Parse(value);
}
public static class ProductionVersion
{
    public static Version CurrentVersion => typeof(RocheScoreboard.Mac.App).Assembly.GetName().Version ?? new Version(4, 1, 3);
}

public readonly record struct NativeDuration(TimeSpan TimeSpan)
{
    public static implicit operator TimeSpan(NativeDuration value) => value.TimeSpan;
}
public static class NativeFreezing
{
    // Avalonia properties are owned by the UI thread. WPF Freeze calls are
    // retained at their source locations; native immutable brushes can be used
    // directly and don't require WPF's Freezable thread-affinity operation.
    public static void Freeze(this object value) { }
}

public static class NativeLayout
{
    public static double NativeWidth(this Avalonia.Controls.Control control) => control.Bounds.Width;
    public static double NativeHeight(this Avalonia.Controls.Control control) => control.Bounds.Height;
    public static double NativeWidth(this Avalonia.Controls.ColumnDefinition column) => column.ActualWidth;
    public static double NativeHeight(this Avalonia.Controls.RowDefinition row) => row.ActualHeight;
}
public static class NativeLayoutTransform
{
    private static LayoutTransformControl Host(Control control)
        => Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(control).OfType<LayoutTransformControl>().FirstOrDefault()
           ?? FindLogicalHost(control)
           ?? throw new InvalidOperationException("The production layout transform wrapper is missing.");
    private static LayoutTransformControl? FindLogicalHost(Control control)
    {
        for (StyledElement? parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent is LayoutTransformControl host) return host;
        return null;
    }
    public static ITransform? Get(Control control) => Host(control).LayoutTransform;
    public static void Set(Control control, ITransform transform) => Host(control).LayoutTransform = transform;
}
public static class NativeLookup
{
    public static object? Find(Control root, string name) =>
        RocheScoreboard.Mac.Production.ProductionNames.RegistryFor(root).TryGetValue(name, out var item) ? item : root.FindControl<Control>(name);
}
public static class NativeObjects
{
    public static T Require<T>(object? value) where T : class
        => value as T ?? throw new InvalidOperationException($"Production object must be {typeof(T).Name}.");
}
public static class NativeBrush
{
    public static T Require<T>(IBrush? brush) where T : class, IBrush => brush as T ?? throw new InvalidOperationException($"Production brush must be {typeof(T).Name}.");
    public static LinearGradientBrush Linear(Color from, Color to, Avalonia.Point start, Avalonia.Point end)
        => new() { StartPoint = new(start, Avalonia.RelativeUnit.Relative), EndPoint = new(end, Avalonia.RelativeUnit.Relative), GradientStops = new() { new(from, 0), new(to, 1) } };
    public static LinearGradientBrush Linear(Color from, Color to, double angle)
    {
        double radians = angle * Math.PI / 180;
        var vector = new Avalonia.Vector(Math.Cos(radians), Math.Sin(radians));
        double scale = .5 / Math.Max(Math.Abs(vector.X), Math.Abs(vector.Y));
        return Linear(from, to, new(.5 - vector.X * scale, .5 - vector.Y * scale), new(.5 + vector.X * scale, .5 + vector.Y * scale));
    }
}
public sealed class NativeFormattedText
{
    private readonly FormattedText _text;
    public NativeFormattedText(string text, System.Globalization.CultureInfo culture, FlowDirection flow, Typeface face,
        double size, IBrush brush, double pixelsPerDip = 1)
        => _text = new(text, culture, flow, face, size, brush);
    public double Width => _text.Width;
    public double WidthIncludingTrailingWhitespace => _text.WidthIncludingTrailingWhitespace;
    public double Height => _text.Height;
}
public static class NativeText
{
    public static void ClearInlines(TextBlock text)
    {
        text.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        text.Inlines.Clear();
    }
}

public sealed class NativeLine : Avalonia.Controls.Shapes.Line
{
    public double X1 { get => StartPoint.X; set => StartPoint = new(value, StartPoint.Y); }
    public double Y1 { get => StartPoint.Y; set => StartPoint = new(StartPoint.X, value); }
    public double X2 { get => EndPoint.X; set => EndPoint = new(value, EndPoint.Y); }
    public double Y2 { get => EndPoint.Y; set => EndPoint = new(EndPoint.X, value); }
    public PenLineCap StrokeStartLineCap { get => StrokeLineCap; set => StrokeLineCap = value; }
    public PenLineCap StrokeEndLineCap { get => StrokeLineCap; set => StrokeLineCap = value; }
}
public sealed class NativePolyline : Avalonia.Controls.Shapes.Polyline
{
    public PenLineJoin StrokeLineJoin { get => StrokeJoin; set => StrokeJoin = value; }
    public PenLineCap StrokeStartLineCap { get => StrokeLineCap; set => StrokeLineCap = value; }
    public PenLineCap StrokeEndLineCap { get => StrokeLineCap; set => StrokeLineCap = value; }
}
public sealed class NativeButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    public object? ToolTip { get => Avalonia.Controls.ToolTip.GetTip(this); set => Avalonia.Controls.ToolTip.SetTip(this, value); }
}
public sealed class NativeBorder : Border
{
    public object? ToolTip { get => Avalonia.Controls.ToolTip.GetTip(this); set => Avalonia.Controls.ToolTip.SetTip(this, value); }
}
public static class NativeTemplates
{
    public static Control? Find(Control root, string name) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>().FirstOrDefault(x => x.Name == name && ReferenceEquals(x.TemplatedParent, root));
    public static void SetResourceReference(this AvaloniaObject target, AvaloniaProperty property, object key)
        => target.Bind(property, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key));
}
public static class VisualTreeHelper
{
    public static int GetChildrenCount(AvaloniaObject target) => target is Visual v ? Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v).Count() : 0;
    public static AvaloniaObject GetChild(AvaloniaObject target, int index) => Avalonia.VisualTree.VisualExtensions.GetVisualChildren((Visual)target).ElementAt(index);
    public static Vector GetOffset(Visual target) => new(target.Bounds.X, target.Bounds.Y);
    public static AvaloniaObject? GetParent(AvaloniaObject target) => target is Avalonia.Visual v ? Avalonia.VisualTree.VisualExtensions.GetVisualParent(v) : (target as StyledElement)?.Parent;
}
public static class NativeFonts
{
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    public static FontFamily Family(string declaration)
    {
        if (Families.TryGetValue(declaration, out var cached)) return cached;
        foreach (string candidate in declaration.Split(','))
        {
            var family = FontManager.Current.SystemFonts.FirstOrDefault(x => string.Equals(x.Name, candidate.Trim(), StringComparison.OrdinalIgnoreCase));
            if (family is not null) return Families[declaration] = family;
        }
        return Families[declaration] = new FontFamily("fonts:Inter#Inter");
    }
}

public sealed class ProductionFontExtension : Avalonia.Markup.Xaml.MarkupExtension
{
    public string Declaration { get; set; } = "";
    public ProductionFontExtension() { }
    public ProductionFontExtension(string declaration) => Declaration = declaration;
    public override object ProvideValue(IServiceProvider serviceProvider) => NativeFonts.Family(Declaration);
}

public static class NativeScrolling
{
    public static void ScrollToLeftEnd(this ScrollViewer viewer) => viewer.Offset = new Vector(0, viewer.Offset.Y);
}
