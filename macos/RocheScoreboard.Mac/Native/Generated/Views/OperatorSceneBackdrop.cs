// Generated from Views/OperatorSceneBackdrop.cs; edit translator/adapters.
// Source SHA-256: b3a35270ccb54cbf14a998493c0e4c95ba698b644ec5a6f4857724edd3b0b349
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;
using Brush = Avalonia.Media.IBrush;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace Roche_Scoreboard.Views;

/// <summary>Original vector wallpaper with finite material changes; never a perpetual background animation.</summary>
public sealed class OperatorSceneBackdrop : Control
{
    private static readonly AttachedProperty<Brush> ContextProperty=(AttachedProperty<Brush>)NativeProperty.Register("Context",typeof(Brush),typeof(OperatorSceneBackdrop),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender,ContextChanged));
    private static readonly AttachedProperty<double> BlendProperty=(AttachedProperty<double>)NativeProperty.Register("Blend",typeof(double),typeof(OperatorSceneBackdrop),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Geometry Ribbon=Path("M-100,820 C-60,430 280,560 520,400 C720,260 650,60 1090,-160 L1200,-40 C860,100 1040,300 650,530 C360,760 130,610 80,890 Z");
    private static readonly Geometry UpperRibbon=Path("M-80,-110 C200,-15 365,0 600,125 C800,235 820,375 1110,490 L1110,560 C785,440 740,315 570,220 C340,75 140,100 -80,0 Z");
    private DrawingGroup? _scene;
    private readonly Dictionary<OperatorBackdropStyle,DrawingGroup> _sources=new();
    private Dictionary<OperatorBackdropStyle,double> _fromWeights=new();
    private Size _size;
    private bool _light,_contrast;
    private OperatorBackdropStyle _style;
    private int _generation;
    internal bool IsTransitioning=>Motion.IsAnimating(this);
    internal int CachedSceneCount=>_sources.Count;
    public OperatorSceneBackdrop()
    {
        IsHitTestVisible=false;Focusable=false;this.SetResourceReference(ContextProperty,"ControlCentre.CanvasBrush");
        Loaded+=(_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;Refresh(false);};
        Unloaded+=(_,_)=>{OperatorAppearance.Changed-=AppearanceChanged;Settle();};
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_,e)=>{if(e.NewValue is false)Settle();});
        SizeChanged+=(_,_)=>Refresh(false);
    }
    private static Geometry Path(string data){var geometry=Geometry.Parse(data);geometry.Freeze();return geometry;}
    private static void ContextChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((OperatorSceneBackdrop)target).Refresh(false);
    private bool IsLight=>this.TryFindResource("ControlCentre.CanvasBrush") is SolidColorBrush brush && brush.Color.R>128;
    private void AppearanceChanged()=>Refresh(OperatorMotion.Enabled);
    private void Settle(){++_generation;this.BeginAnimation(BlendProperty,null);SetValue(BlendProperty,1d);_fromWeights.Clear();InvalidateVisual();}
    private void Refresh(bool animate)
    {
        if(!OperatorMotion.Enabled)Settle();
        if(Bounds.Size.Width<1 || Bounds.Size.Height<1)return;
        bool light=IsLight,contrast=NativeSystemPreferences.HighContrast;var style=OperatorAppearance.Backdrop;
        if(_scene is not null && _size==Bounds.Size && _style==style && _light==light && _contrast==contrast)return;
        bool sameFrame=_scene is not null && _size==Bounds.Size && _light==light && _contrast==contrast;
        var from=sameFrame?VisibleWeights():null;
        if(!sameFrame)_sources.Clear();
        _size=Bounds.Size;_style=style;_light=light;_contrast=contrast;
        if(!_sources.TryGetValue(style,out var drawing)){drawing=new DrawingGroup();using(var context=drawing.Open())DrawScene(context,new Rect(Bounds.Size),light,style);drawing.Freeze();_sources[style]=drawing;}_scene=drawing;
        Settle();
        if(!animate || !IsLoaded || !IsVisible || from is null || contrast)return;
        _fromWeights=from;SetValue(BlendProperty,0d);int generation=++_generation;
        var blend=new DoubleAnimation(1,TimeSpan.FromMilliseconds(360)){EasingFunction=OperatorMotion.SmoothOut};
        blend.Completed+=(_,_)=>{if(generation==_generation)Settle();};this.BeginAnimation(BlendProperty,blend);
    }
    private Dictionary<OperatorBackdropStyle,double> VisibleWeights()
    {
        var result=new Dictionary<OperatorBackdropStyle,double>();double blend=Math.Clamp((double)GetValue(BlendProperty),0,1);
        if(_fromWeights.Count==0){result[_style]=1;return result;}
        foreach(var (style,weight) in _fromWeights)result[style]=weight*(1-blend);
        result[_style]=result.GetValueOrDefault(_style)+blend;return result;
    }
    private void DrawLayers(NativeDrawingContext drawing)
    {
        double blend=Math.Clamp((double)GetValue(BlendProperty),0,1);
        if(_fromWeights.Count==0){if(_scene is not null)drawing.DrawDrawing(_scene);return;}
        // At most three frozen scenes, even after arbitrarily many interrupted switches.
        // Normalized source-over gives a constant-opacity linear blend without a black flash.
        double accumulated=0;
        for(int i=0;i<3;i++)
        {
            var style=(OperatorBackdropStyle)i;double weight=_fromWeights.GetValueOrDefault(style)*(1-blend)+(style==_style?blend:0);
            if(weight<.000001 || !_sources.TryGetValue(style,out var source))continue;
            accumulated+=weight;drawing.PushOpacity(weight/accumulated);drawing.DrawDrawing(source);drawing.Pop();
        }
    }
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var drawing = new NativeDrawingContext(nativeContext,Bounds.Size);
        if(_scene is null)DrawScene(drawing,new Rect(Bounds.Size),IsLight);else DrawLayers(drawing);
    }
    internal static void DrawScene(NativeDrawingContext drawing,Rect bounds,bool light)=>DrawScene(drawing,bounds,light,OperatorAppearance.Backdrop);
    private static void DrawScene(NativeDrawingContext drawing,Rect bounds,bool light,OperatorBackdropStyle style)
    {
        if(NativeSystemPreferences.HighContrast){drawing.DrawRectangle(NativeSystemColors.WindowBrush,null,bounds);return;}
        drawing.DrawRectangle(new SolidColorBrush(light?Color.FromRgb(235,239,244):Color.FromRgb(3,5,9)),null,bounds);
        if(bounds.Width<1 || bounds.Height<1)return;
        void Pool(double x,double y,double width,double height,Color colour)
        {var brush=NativeBrush.Radial(colour,Colors.Transparent);drawing.DrawEllipse(brush,null,new Point(bounds.Width*x,bounds.Height*y),bounds.Width*width,bounds.Height*height);}
        bool colour=style==OperatorBackdropStyle.Colour;
        Pool(.12,.08,.66,.80,light?Color.FromArgb(120,151,183,216):colour?Color.FromArgb(150,30,67,100):Color.FromArgb(130,58,63,72));
        Pool(.93,.35,.60,.84,light?Color.FromArgb(90,156,193,190):colour?Color.FromArgb(145,20,82,74):Color.FromArgb(100,45,49,56));
        if(style==OperatorBackdropStyle.Neutral)return;
        drawing.PushClip(new RectangleGeometry(bounds));drawing.PushTransform(new ScaleTransform(bounds.Width/1000,bounds.Height/700));
        var ribbon=new LinearGradientBrush{StartPoint = new RelativePoint(0,0, RelativeUnit.Relative),EndPoint = new RelativePoint(1,1, RelativeUnit.Relative)};
        ribbon.GradientStops.Add(new GradientStop(light?Color.FromRgb(194,207,223):Color.FromRgb(18,25,38),0));
        ribbon.GradientStops.Add(new GradientStop(light?Color.FromRgb(232,242,245):colour?Color.FromRgb(35,139,143):Color.FromRgb(72,79,91),.36));
        ribbon.GradientStops.Add(new GradientStop(light?Colors.White:colour?Color.FromRgb(107,174,203):Color.FromRgb(166,178,188),.50));
        ribbon.GradientStops.Add(new GradientStop(light?Color.FromRgb(190,215,230):colour?Color.FromRgb(43,74,134):Color.FromRgb(49,57,71),.62));
        ribbon.GradientStops.Add(new GradientStop(light?Color.FromRgb(222,231,238):Color.FromRgb(8,14,25),1));
        drawing.DrawGeometry(ribbon,null,Ribbon);
        var upper=NativeBrush.Linear(light?Color.FromArgb(90,255,255,255):colour?Color.FromArgb(65,144,104,86):Color.FromArgb(55,117,126,141),Colors.Transparent,70);
        drawing.DrawGeometry(upper,null,UpperRibbon);drawing.Pop();drawing.Pop();
    }
    internal static Brush FallbackBrush()
    {var group=new DrawingGroup();using(var drawing=group.Open())DrawScene(drawing,new Rect(0,0,1000,700),false);group.Freeze();return new DrawingBrush(group){Stretch=Stretch.Fill};}
}
