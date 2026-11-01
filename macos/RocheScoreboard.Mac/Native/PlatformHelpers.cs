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
    private sealed class Marker { }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,Marker> Frozen=new();
    internal static bool IsFrozen(object value)=>Frozen.TryGetValue(value,out _);

    // Avalonia properties are owned by the UI thread. WPF Freeze calls are
    // retained at their source locations; native immutable brushes can be used
    // directly and don't require WPF's Freezable thread-affinity operation.
    public static void Freeze(this object value) { Frozen.GetOrCreateValue(value); }
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
    public static object? Find(Control root,string name)
    {
        if(RocheScoreboard.Mac.Production.ProductionNames.RegistryFor(root).TryGetValue(name,out var item))return item;
        for(StyledElement? current=root;current is not null;current=current.Parent)
            if(NameScope.GetNameScope(current) is {} scope)return scope.Find(name);
        // Runtime-built menu/sheet controls may have no compiled name scope.
        // Missing optional animation parts must behave like WPF FindName: null.
        return root.Name==name?root:Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>().FirstOrDefault(child=>child.Name==name);
    }
}
public static class NativeObjects
{
    public static T Require<T>(object? value) where T : class
        => value as T ?? throw new InvalidOperationException($"Production object must be {typeof(T).Name}.");
}
public static class NativeBrush
{
    public static LinearGradientBrush Linear()=>new();
    public static RadialGradientBrush Radial(Color first,Color last)=>new(){GradientStops=new(){new(first,0),new(last,1)}};
    public static LinearGradientBrush Linear(GradientStops stops,double angle){var result=Linear(Colors.Transparent,Colors.Transparent,angle);result.GradientStops=stops;return result;}
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
    {
        _text = new(text, culture, flow, face, size, brush);
        if (text.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || "./-+:()%".Contains(character)))
            _text.SetFontFeatures(new FontFeatureCollection { FontFeature.Parse("tnum"), FontFeature.Parse("lnum"), FontFeature.Parse("-kern") });
    }
    internal FormattedText Text => _text;
    public double MaxTextWidth{get=>_text.MaxTextWidth;set=>_text.MaxTextWidth=value;}
    public int MaxLineCount{get=>_text.MaxLineCount;set=>_text.MaxLineCount=value;}
    public TextTrimming Trimming{get=>_text.Trimming;set=>_text.Trimming=value;}
    public double Width => _text.Width;
    public double WidthIncludingTrailingWhitespace => _text.WidthIncludingTrailingWhitespace;
    public double Height => _text.Height;
    public Geometry BuildGeometry(Point origin)=>_text.BuildGeometry(origin);
    public Geometry BuildHighlightGeometry(Point origin,int start,int count)=>_text.BuildHighlightGeometry(origin,start,count);
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
    public static object? FindObject(Control? root,string name)
    {
        if(root is null)return null;
        var found=Find(root,name);
        if(found is not null)return found;
        foreach(var child in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>())
            foreach(var value in NativeObjectGraph.Walk(child))
                if(RocheScoreboard.Mac.Production.ProductionNames.GetId(value)==name)return value;
        return NativeLookup.Find(root,name);
    }

    public static Control? Find(Control root, string name) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>().FirstOrDefault(x => x.Name == name && ReferenceEquals(x.TemplatedParent, root));
    public static void SetResourceReference(this AvaloniaObject target, AvaloniaProperty property, object key)
        => target.Bind(property, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key));
}
public static class VisualTreeHelper
{
    public readonly record struct Dpi(double PixelsPerDip){public double DpiScaleX=>PixelsPerDip;public double DpiScaleY=>PixelsPerDip;}
    public static Dpi GetDpi(Visual target)=>new(TopLevel.GetTopLevel(target)?.RenderScaling??1);
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
public static class NativeCompatibility
{
    public static bool IntersectsWith(this Rect rect,Rect other)=>rect.Intersects(other);
    public static bool IsAncestorOf(this Visual target,AvaloniaObject child)=>child is Visual visual&&Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(visual).Contains(target);
    public static Rect TransformBounds(this MatrixTransform transform,Rect rect)=>rect.TransformToAABB(transform.Matrix);
    public static Point PointFromScreen(this Visual source,PixelPoint point)=>source.PointToClient(point);
    public static Point NativeTranslatePoint(this Visual source,Point point,Visual target)=>source.TranslatePoint(point,target)??default;
    public static Point PointFromScreen(this Visual source,Point point)=>source.PointToClient(new PixelPoint((int)Math.Round(point.X),(int)Math.Round(point.Y)));
    public static MatrixTransform TransformToAncestor(this Visual source,Visual target)=>new(source.TransformToVisual(target)??Matrix.Identity);
    public static object? FindName(this Avalonia.Controls.Templates.IControlTemplate template,string name,Control root)=>NativeTemplates.Find(root,name);

    public static object? FindName(this Control root,string name)=>NativeLookup.Find(root,name);
    public static object? TryFindResource(this Control root,object key)=>NativeResources.FindOrNull(root,key);
    public static IDisposable SetBinding(this AvaloniaObject target,AvaloniaProperty property,Avalonia.Data.IBinding binding)=>target.Bind(property,binding);
    public static bool IsAncestorOf(this Visual target,Visual child)=>Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(child).Contains(target);
    public static void ScrollToVerticalOffset(this ScrollViewer target,double offset)=>target.Offset=new Vector(target.Offset.X,offset);
    public static void BezierTo(this StreamGeometryContext context,Point a,Point b,Point c,bool stroke,bool smooth)=>context.CubicBezierTo(a,b,c);
    public static void LineTo(this StreamGeometryContext context,Point p,bool stroke,bool smooth)=>context.LineTo(p);
    public static void BeginFigure(this StreamGeometryContext context,Point p,bool filled,bool closed)=>context.BeginFigure(p,filled);
    public static void Post(this Avalonia.Threading.Dispatcher dispatcher,Avalonia.Threading.DispatcherPriority priority,Action action)=>dispatcher.Post(action,priority);
    public static Avalonia.Threading.DispatcherOperation InvokeAsync(this Avalonia.Threading.Dispatcher dispatcher,Avalonia.Threading.DispatcherPriority priority,Action action)=>dispatcher.InvokeAsync(action,priority);
}
public static class LogicalTreeHelper
{
    public static IEnumerable<object> GetChildren(AvaloniaObject owner)=>owner is Avalonia.LogicalTree.ILogical logical?Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(logical):[];
    public static AvaloniaObject? GetParent(AvaloniaObject owner)=>owner is StyledElement styled?styled.Parent:null;
}
public static class NativeFocusManager
{
    public static object? GetFocusScope(Control target)=>TopLevel.GetTopLevel(target);
    public static void SetFocusedElement(object? scope,Control? target)=>target?.Focus();
    public static Avalonia.Input.IInputElement? GetFocusedElement(object? scope)=>(scope as TopLevel)?.FocusManager?.GetFocusedElement();
}
public static class NativePanel
{
    public static void SetZIndex(Control target,int value)=>target.ZIndex=value;
}
public sealed class NativeControlTheme: Avalonia.Styling.ControlTheme
{
    public NativeControlTheme(Type type,Avalonia.Styling.ControlTheme? parent=null):base(type){BasedOn=parent;}
}
public static class NativeNavigation
{
    public static bool FocusFirst(Control root)
    {
        var first=Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>().FirstOrDefault(c=>c.Focusable&&c.IsEffectivelyEnabled&&c.IsEffectivelyVisible);
        return first?.Focus()==true;
    }
    public static void Select(this TextBox box,int start,int length){box.SelectionStart=start;box.SelectionEnd=start+length;}
    public static void AddHandler<T>(this Avalonia.Interactivity.Interactive control,Avalonia.Interactivity.RoutedEvent<T> routed,EventHandler<T> handler,bool handled)where T:Avalonia.Interactivity.RoutedEventArgs
        =>control.AddHandler(routed,handler,Avalonia.Interactivity.RoutingStrategies.Tunnel|Avalonia.Interactivity.RoutingStrategies.Bubble,handled);
}

public sealed class NativeBrushConverter
{
    public object ConvertFromString(string value)=>new SolidColorBrush(Color.Parse(value));
}
