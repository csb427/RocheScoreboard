// Generated from Views/OperatorGlassSurface.cs; edit translator/adapters.
// Source SHA-256: 9a217543e24712dc3c91fb8185f3e4a71a10af139920acf7d6fa7432e585c2e9
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Services;
using Border = Avalonia.Controls.Border;
using Brush = Avalonia.Media.IBrush;
using Color = Avalonia.Media.Color;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using Image = Avalonia.Controls.Image;
using Pen = Avalonia.Media.Pen;

namespace Roche_Scoreboard.Views;

public enum GlassMaterial { Regular, Opaque, Clear }

/// <summary>Operator glass with a separately filtered background and unfiltered, native foreground content.</summary>
public sealed class OperatorGlassSurface : NativePaintBorder
{
    private static readonly Brush Lustre=CreateLustre();
    private static readonly Brush[] CoreShades={CreateCoreShade(24),CreateCoreShade(58),CreateCoreShade(100)};
    private static Brush CreateCoreShade(byte alpha)
    {
        var brush=new RadialGradientBrush{Center = new RelativePoint(.5,.5, RelativeUnit.Relative),GradientOrigin = new RelativePoint(.5,.5, RelativeUnit.Relative),RadiusX=new RelativeScalar(.7,RelativeUnit.Relative),RadiusY=new RelativeScalar(.9,RelativeUnit.Relative)};
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha,0,3,8),0));brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha*.8),0,3,8),.75));brush.GradientStops.Add(new GradientStop(Colors.Transparent,1));brush.Freeze();return brush;
    }
    private static Brush CreateLustre()
    {
        var brush=new LinearGradientBrush {StartPoint = new RelativePoint(.15,0, RelativeUnit.Relative),EndPoint = new RelativePoint(.65,1, RelativeUnit.Relative)};
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(40,244,250,255),0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent,.18));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent,.82));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(16,216,234,255),1));
        brush.Freeze();return brush;
    }
    public static readonly AttachedProperty<GlassMaterial> MaterialProperty = (AttachedProperty<GlassMaterial>)NativeProperty.Register(nameof(Material), typeof(GlassMaterial), typeof(OperatorGlassSurface), new FrameworkPropertyMetadata(GlassMaterial.Regular, Changed));
    public GlassMaterial Material { get => (GlassMaterial)GetValue(MaterialProperty); set => SetValue(MaterialProperty, value); }
    public static readonly AttachedProperty<Control> GlassSourceProperty = (AttachedProperty<Control>)NativeProperty.Register(nameof(GlassSource), typeof(Control), typeof(OperatorGlassSurface), new FrameworkPropertyMetadata(null, SourceChanged));
    public Control? GlassSource { get => (Control?)GetValue(GlassSourceProperty); set => SetValue(GlassSourceProperty, value); }
    private readonly MaterialVisual _materialVisual;
    private readonly FocusEdge _focusEdge;
    private Brush _sample = OperatorSceneBackdrop.FallbackBrush();
    private OperatorGlassEffect? _lens;
    private DispatcherOperation? _refresh;
    private Control? _observedLiveSource;
    private bool _observingLiveSource;
    private VisualBrush? _liveBrush;
    private Border? _frostVisual;
    private VisualBrush? _frostBrush;
    private Rect _liveBounds = default(Rect);
    private bool _snapshotReady;
    private bool _down;
    private double _activityTarget;
    private int _activityGeneration;
    private bool HasMaterial => !(Background is ISolidColorBrush { Color.A: 0 } &&
        (BorderThickness.Left == 0 || BorderBrush is ISolidColorBrush { Color.A: 0 }));
    private bool NeedsBackdrop => HasMaterial && Material != GlassMaterial.Opaque
        && OperatorAppearance.GlassEnabled && !NativeSystemPreferences.HighContrast;
    internal Point OpticalSettings=>_lens?.Optics ?? default;
    internal bool IsMaterialAnimating=>_lens?.HasAnimatedProperties==true;
    internal bool UsesShader => false;
    internal bool HasCapturedBackdrop => _snapshotReady;
    internal int LiveSampleBuildCount { get; private set; }
    internal int LiveSampleUpdateCount { get; private set; }
    internal static bool ForceFallback { get; set; }
    private static void Changed(AvaloniaObject d, AvaloniaPropertyChangedEventArgs e)
    {
        var surface = (OperatorGlassSurface)d;
        surface.RefreshAppearance(); surface.ObserveLiveSource(); surface.QueueSample();
    }
    private static void SourceChanged(AvaloniaObject d, AvaloniaPropertyChangedEventArgs e)
    {
        var surface = (OperatorGlassSurface)d;
        surface.ReleaseSample(); surface.ObserveLiveSource(); surface.QueueSample();
    }
    public OperatorGlassSurface()
    {
        _materialVisual = new MaterialVisual(this) { IsHitTestVisible = false }; _focusEdge = new FocusEdge(this) { IsHitTestVisible = false };
        AddVisualChild(_materialVisual); AddVisualChild(_focusEdge);
        UseLayoutRounding = true;
        Loaded += (_, _) => { OperatorAppearance.Changed-=AppearanceChanged;OperatorAppearance.Changed+=AppearanceChanged;ObserveLiveSource();RefreshAppearance(); QueueSample(); };
        Unloaded += (_, _) => { OperatorAppearance.Changed-=AppearanceChanged;ReleaseSample();_down = false; ResetLight(); _materialVisual.Effect = null; _lens = null; };
        SizeChanged += (_, _) => { _snapshotReady = false; QueueSample(); RefreshAppearance(); };
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, e) => { if(e.NewValue is true) { ObserveLiveSource(); QueueSample(); } else { ReleaseSample();_down = false; ResetLight(); } });
        PointerMoved += (_, e) => Illuminate(e.GetPosition(this), IsPointerOver ? .55 : 0);
        PointerEntered += (_, e) => Illuminate(e.GetPosition(this), .55);
        PointerExited += (_, _) => { _down = false; Illuminate(new Point(Bounds.Width * .28, 0), 0); };
        PointerPressed += (_, e) => { _down = true; Illuminate(e.GetPosition(this), 1); };
        PointerReleased += (_, e) => { _down = false; Illuminate(e.GetPosition(this), .55); };
        PointerCaptureLost += (_, _) => { _down = false; Illuminate(new Point(Bounds.Width * .28, 0), IsPointerOver ? .55 : 0); };
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        _materialVisual.Measure(result); _materialVisual.Arrange(new Rect(result));
        _focusEdge.Measure(result); _focusEdge.Arrange(new Rect(result));
        return result;
    }
    protected override void RenderNative(NativeDrawingContext dc)
    {
        if(_materialVisual.Opacity==0 || !OperatorAppearance.GlassEnabled || NativeSystemPreferences.HighContrast || Material==GlassMaterial.Opaque)return;
        for(int i=4;i>=1;i--)dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(5,0,0,0)),null,new Rect(-i,i*.7,Math.Max(0,Bounds.Width+2*i),Math.Max(0,Bounds.Height+2*i)),Radius+i,Radius+i);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(_materialVisual is not null && (e.Property == BackgroundProperty || e.Property == BorderBrushProperty || e.Property == BorderThicknessProperty || e.Property == CornerRadiusProperty))
        { RefreshAppearance(); ObserveLiveSource(); QueueSample(); }
    }
    private double Radius => Math.Min(CornerRadius.TopLeft, Math.Min(Bounds.Width, Bounds.Height) * .5);
    private Color BaseTint
    {
        get
        {
            bool light=this.TryFindResource("ControlCentre.SurfaceBrush") is ISolidColorBrush surface && surface.Color.R>180;
            var colour = Background switch { ISolidColorBrush solid => solid.Color, IGradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color, _ => light ? Color.FromArgb(165,242,246,252) : Color.FromArgb(110,20,22,28) };
            if(colour.A == 0)colour = light ? Color.FromArgb(120,242,246,252) : Color.FromArgb(100,20,22,28);
            if(Material == GlassMaterial.Opaque || !OperatorAppearance.GlassEnabled)
            {
                if(colour.A<90 && colour.R>200 && colour.G>200 && colour.B>200 && this.TryFindResource("ControlCentre.SurfaceBrush") is ISolidColorBrush opaque)colour=opaque.Color;
                return Color.FromArgb(255, colour.R, colour.G, colour.B);
            }
            int strength=(int)OperatorAppearance.GlassIntensity;
            if(colour.A >= 220 && colour.R > 220 && colour.G > 220 && colour.B > 220)return Color.FromArgb((byte)(strength==0?235:strength==1?222:205),colour.R,colour.G,colour.B);
            // Semantic action colours keep enough pigment for their existing text contrast.
            if(colour.A>=220 && Math.Max(colour.R,Math.Max(colour.G,colour.B))-Math.Min(colour.R,Math.Min(colour.G,colour.B))>35)return Color.FromArgb(190,colour.R,colour.G,colour.B);
            int cap=Material==GlassMaterial.Clear?(strength==0?55:strength==1?35:24):(strength==0?166:strength==1?128:88);
            byte opacity=(byte)Math.Min((int)colour.A,cap);
            return Color.FromArgb(opacity, colour.R, colour.G, colour.B);
        }
    }
    private void RefreshAppearance()
    {
        if(_materialVisual is null)return;
        _materialVisual.Opacity = HasMaterial ? 1 : 0;
        if(!NeedsBackdrop)ReleaseSample();
        if(!HasMaterial){ResetLight();_materialVisual.Effect=null;_materialVisual.InvalidateVisual();return;}
        if(!ForceFallback && OperatorAppearance.GlassEnabled && !NativeSystemPreferences.HighContrast && Material != GlassMaterial.Opaque)
            _lens ??= OperatorGlassEffect.Create();
        _materialVisual.Effect = null;
        if(_lens is not null) { _lens.Tint = BaseTint; _lens.Dimensions = new Point(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)); _lens.Radius = Math.Max(0, Radius);_lens.Optics=OperatorAppearance.GlassIntensity switch{OperatorGlassIntensity.Soft=>new Point(3,.24),OperatorGlassIntensity.Balanced=>new Point(7,.40),_=>new Point(14,.68)};_lens.EdgeDepth=OperatorAppearance.GlassIntensity switch{OperatorGlassIntensity.Soft=>.1,OperatorGlassIntensity.Balanced=>.075,_=>.055}; }
        _materialVisual.InvalidateVisual(); _focusEdge.InvalidateVisual();InvalidateVisual();
    }
    private void AppearanceChanged() { RefreshAppearance();if(!OperatorMotion.Enabled || !OperatorAppearance.GlassEnabled)ResetLight();_snapshotReady=false;ObserveLiveSource();QueueSample(); }
    private void Illuminate(Point position, double value)
    {
        if(_lens is null)return;
        if(!OperatorMotion.Enabled){ResetLight();return;}
        _lens.Light = new Point(position.X / Math.Max(1, Bounds.Width), position.Y / Math.Max(1, Bounds.Height));
        double target=_down?.62:value*.55;if(Math.Abs(target-_activityTarget)<.001)return;_activityTarget=target;int generation=++_activityGeneration;
        var lens=_lens;var response=new DoubleAnimation(target,TimeSpan.FromMilliseconds(target==0?260:150)){EasingFunction=OperatorMotion.SmoothOut};
        response.Completed+=(_,_)=>{if(generation!=_activityGeneration || !ReferenceEquals(lens,_lens))return;lens.BeginAnimation(OperatorGlassEffect.ActivityProperty,null);lens.Activity=target;};lens.BeginAnimation(OperatorGlassEffect.ActivityProperty,response);
    }
    private void ResetLight(){++_activityGeneration;_activityTarget=0;if(_lens is null)return;_lens.BeginAnimation(OperatorGlassEffect.ActivityProperty,null);_lens.Activity=0;}
    private void QueueSample()
    {
        if(!NeedsBackdrop || !IsLoaded || !IsVisible || Bounds.Width < 1 || Bounds.Height < 1)return;
        if(_refresh is { Status: DispatcherOperationStatus.Pending })return;
        _refresh = Dispatcher.UIThread.InvokeAsync(new Action(() => { _refresh = null; if(IsLoaded && IsVisible)RefreshSample(); }), DispatcherPriority.Loaded);
    }
    private void ObserveLiveSource(bool observe = true)
    {
        Control? liveSource = GlassSource switch
        {
            OperatorSceneBackdrop scene when !scene.IsAncestorOf(this) => scene,
            Avalonia.Controls.Panel { IsHitTestVisible: false } panel when !panel.IsAncestorOf(this) => panel,
            null => OperatorDesign.GetBackdrop(this),
            _ => _liveBrush?.Visual as Control
        };
        if (!ReferenceEquals(liveSource, _observedLiveSource))
        {
            if (_observingLiveSource && _observedLiveSource is not null) _observedLiveSource.LayoutUpdated -= LiveSourceLayoutUpdated;
            _observedLiveSource = liveSource;
            _observingLiveSource = false;
        }
        bool shouldObserve = observe && NeedsBackdrop && IsLoaded && IsVisible && _observedLiveSource is not null;
        if (_observingLiveSource == shouldObserve || _observedLiveSource is null) return;
        if (shouldObserve) _observedLiveSource.LayoutUpdated += LiveSourceLayoutUpdated;
        else _observedLiveSource.LayoutUpdated -= LiveSourceLayoutUpdated;
        _observingLiveSource = shouldObserve;
    }
    private void LiveSourceLayoutUpdated(object? sender, EventArgs e) => QueueSample();
    private void RefreshSample()
    {
        if(!NeedsBackdrop){ReleaseSample();return;}
        var explicitSource = GlassSource;
        // Dedicated, non-interactive backdrop layers are safe live vector sources.
        // Capturing them would freeze an in-progress wallpaper transition into every tile.
        if(explicitSource is not null && !explicitSource.IsAncestorOf(this) &&
            (explicitSource is OperatorSceneBackdrop || explicitSource is Avalonia.Controls.Panel {IsHitTestVisible:false}))
        {SetLiveSample(explicitSource);return;}
        if(explicitSource is null && OperatorDesign.GetBackdrop(this) is null && NativeWindow.GetWindow(this)?.Owner?.Content is Control ownerContent)explicitSource = ownerContent;
        if(explicitSource is not null && !explicitSource.IsAncestorOf(this) && NativeWindow.GetWindow(explicitSource) is { Content: Control windowContent } sourceWindow && OperatorFeedback.IsOperatorWindow(sourceWindow))explicitSource = windowContent;
        // Never sample a tree containing this material. That would feed the glass
        // and its scrim back into itself on every refresh.
        if(explicitSource is not null && !ReferenceEquals(explicitSource, this) && !explicitSource.IsAncestorOf(this) && NativeWindow.GetWindow(explicitSource) is { } owner && OperatorFeedback.IsOperatorWindow(owner))
        {
            if(TryCapture(explicitSource)) { RefreshAppearance(); return; }
        }
        var scene = OperatorDesign.GetBackdrop(this) ?? (explicitSource is not null ? OperatorDesign.GetBackdrop(explicitSource) : null);
        if(scene is not null && scene.NativeWidth() > 0 && scene.NativeHeight() > 0)
        { SetLiveSample(scene); return; }
        _materialVisual.InvalidateVisual();
    }
    private void ReleaseSample()
    {
        if(_observingLiveSource && _observedLiveSource is not null)
            _observedLiveSource.LayoutUpdated -= LiveSourceLayoutUpdated;
        _observingLiveSource = false; _observedLiveSource = null;
        _refresh?.Abort(); _refresh = null;
        // Break the off-tree brush graph explicitly, including on hide. A cached
        // RenderData may otherwise retain the old graph until another render.
        if(_frostBrush is not null)_frostBrush.Visual = null;
        if(_frostVisual is not null)
        { _frostVisual.Background = null; _frostVisual.Effect = null; NativeCache.Set(_frostVisual, null); }
        if(_liveBrush is not null)_liveBrush.Visual = null;
        _liveBrush = null; _frostVisual = null; _frostBrush = null; _liveBounds = default(Rect);
        _snapshotReady = false; _sample = OperatorSceneBackdrop.FallbackBrush();
        _materialVisual.InvalidateVisual();
    }
    private void SetLiveSample(Control source)
    {
        try
        {
            if(ReferenceEquals(source,this) || source.IsAncestorOf(this)){ReleaseSample();return;}
            Point location=source.PointFromScreen(this.PointToScreen(new Point()));
            var size=new Size(Math.Max(1,Bounds.Width),Math.Max(1,Bounds.Height));
            var bounds = new Rect(location,size);
            bool useFrost = (0>>16)>0 && OperatorAppearance.GlassEnabled && !NativeSystemPreferences.HighContrast;
            double blurRadius = OperatorAppearance.GlassIntensity==OperatorGlassIntensity.Soft?10:6;
            bool sameSource = ReferenceEquals(_liveBrush?.Visual,source);
            bool sameFrost = useFrost == (_frostVisual is not null);
            // LayoutUpdated is dispatcher-wide, not a source-content change. The
            // live brush already follows content changes; do not allocate, arrange
            // or invalidate in response to an unchanged global layout notification.
            if(sameSource && sameFrost && _liveBounds==bounds &&
                (!useFrost || _frostVisual?.Effect is BlurEffect currentBlur && currentBlur.Radius==blurRadius))return;

            if(!sameSource || !sameFrost)
            {
                ReleaseSample();
                _liveBrush=new VisualBrush(source){Stretch=Stretch.Fill,};



                if(useFrost)
                {
                    // Keep one filtered off-tree visual for the lifetime of this
                    // source. Native labels and hit targets remain outside it.
                    _frostVisual=new NativeBorder{Background=_liveBrush,Effect=new BlurEffect{Radius=blurRadius,}};
                    _frostBrush=new VisualBrush(_frostVisual){Stretch=Stretch.Fill,};

                }
                _sample = (Brush?)_frostBrush ?? _liveBrush;
                LiveSampleBuildCount++;
            }
            _liveBrush!.SourceRect = new RelativeRect(bounds, RelativeUnit.Absolute);
            if(_frostVisual is not null && _frostBrush is not null)
            {
                if((_liveBounds.Width<=0 || _liveBounds.Height<=0) || _liveBounds.Size!=size)
                {
                    _frostVisual.Width=size.Width;_frostVisual.Height=size.Height;
                    _frostVisual.Measure(size);_frostVisual.Arrange(new Rect(size));
                    _frostBrush.SourceRect = new RelativeRect(new Rect(size), RelativeUnit.Absolute);
                }
                if(_frostVisual.Effect is BlurEffect frostBlur)frostBlur.Radius=blurRadius;
            }
            _liveBounds=bounds;
            _snapshotReady=false;
            LiveSampleUpdateCount++;
            ObserveLiveSource();
        }
        catch(InvalidOperationException){ReleaseSample();}
        _materialVisual.InvalidateVisual();
    }
    private bool TryCapture(Control source)
    {
        try
        {
            double width = Math.Min(Bounds.Width, 1600), height = Math.Min(Bounds.Height, 1200);
            Point location = source.PointFromScreen(this.PointToScreen(new Point()));
            double scale = .6;
            var drawing = new NativeDrawingVisual();
            using(var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Avalonia.Media.Brushes.Black, null, new Rect(0,0,width,height));
                dc.DrawRectangle(new VisualBrush(source) { SourceRect = new RelativeRect(new Rect(location, new Size(width,height)), RelativeUnit.Absolute), Stretch = Stretch.Fill, }, null, new Rect(0,0,width,height));
            }
            var bitmap = new NativeRenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(width*scale)),Math.Max(1,(int)Math.Ceiling(height*scale)),96*scale,96*scale,PixelFormats.Bgra32);
            bitmap.Render(drawing); bitmap.Freeze();
            var image = new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 14, } };
            image.Measure(new Size(width,height)); image.Arrange(new Rect(0,0,width,height));
            var frost = new NativeRenderTargetBitmap(bitmap.PixelWidth,bitmap.PixelHeight,96*scale,96*scale,PixelFormats.Bgra32); frost.Render(image); frost.Freeze();
            _sample = NativeImageBrush.Create(frost, brush => {brush.Stretch = Stretch.Fill;}); _snapshotReady = true; return true;
        }
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or NotSupportedException) { return false; }
    }
    private sealed class MaterialVisual(OperatorGlassSurface owner) : Control
    {
        public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var dc = new NativeDrawingContext(nativeContext,Bounds.Size);
            if(Bounds.Width < 1 || Bounds.Height < 1)return;
            double radius = Math.Max(0,owner.Radius); var bounds = new Rect(Bounds.Size);
            dc.PushClip(new RectangleGeometry(bounds,radius,radius));
            dc.DrawRectangle(owner._sample,null,bounds);
            var tint=owner.BaseTint;
            if(owner.Material!=GlassMaterial.Opaque && OperatorAppearance.GlassEnabled && !NativeSystemPreferences.HighContrast && tint.A<190 && Math.Max(tint.R,Math.Max(tint.G,tint.B))<180)
                dc.DrawRoundedRectangle(CoreShades[(int)OperatorAppearance.GlassIntensity],null,bounds,radius,radius);
            if(Effect is null)
            {
                var colour = owner.BaseTint;
                if(NativeSystemPreferences.HighContrast)
                {
                    var foreground = owner.GetValue(TextBlock.ForegroundProperty) as ISolidColorBrush;
                    colour = foreground is not null && foreground.Color.R + foreground.Color.G + foreground.Color.B < 384 ? Colors.White : Colors.Black;
                }
                dc.DrawRoundedRectangle(new SolidColorBrush(colour),null,bounds,radius,radius);
                var edge = NativeBrush.Linear(Color.FromArgb(160,240,248,255),Color.FromArgb(35,185,203,225),90);
                dc.DrawRoundedRectangle(null,new Pen(edge,1.2),new Rect(.7,.7,Math.Max(0,Bounds.Width-1.4),Math.Max(0,Bounds.Height-1.4)),radius,radius);
            }
            if(owner.Material!=GlassMaterial.Opaque && OperatorAppearance.GlassEnabled && !NativeSystemPreferences.HighContrast && owner.BaseTint.A<255)
                dc.DrawRoundedRectangle(Lustre,null,bounds,radius,radius);
            dc.Pop();
        }
    }
    private sealed class FocusEdge(OperatorGlassSurface owner) : Control
    {
        public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var dc = new NativeDrawingContext(nativeContext,Bounds.Size);
            if(owner.BorderThickness.Left < 1.8 || Bounds.Width < 3 || Bounds.Height < 3)return;
            dc.DrawRoundedRectangle(null,new Pen(owner.BorderBrush,2),new Rect(1,1,Bounds.Width-2,Bounds.Height-2),Math.Max(0,owner.Radius-1),Math.Max(0,owner.Radius-1));
        }
    }
}
