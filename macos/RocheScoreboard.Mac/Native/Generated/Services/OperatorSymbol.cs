// Generated from Services/OperatorSymbol.cs; edit translator/adapters.
// Source SHA-256: 43b033c0eb10243d57941495624d1e6e1d24aed6cf2824af9bb3b28128c74923
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Brush = Avalonia.Media.IBrush;
using Brushes = Avalonia.Media.Brushes;
using Size = Avalonia.Size;
using Pen = Avalonia.Media.Pen;
using Point = Avalonia.Point;
using ButtonBase = Avalonia.Controls.Button;

namespace Roche_Scoreboard.Services;

/// <summary>Original layered symbols. State changes animate the meaning, never their hit target.</summary>
public sealed class OperatorSymbol : Control
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Geometry> Paths=new();
    private static Geometry Shape(string path)=>Paths.GetOrAdd(path,static value=>{var geometry=Geometry.Parse(value);geometry.Freeze();return geometry;});
    public static readonly AttachedProperty<object> KindProperty = (AttachedProperty<object>)NativeProperty.Register(nameof(Kind),typeof(object),typeof(OperatorSymbol),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public object? Kind { get=>(object?)GetValue(KindProperty); set=>SetValue(KindProperty,value); }
    public static readonly AttachedProperty<Brush> ForegroundProperty = (AttachedProperty<Brush>)NativeProperty.Register(nameof(Foreground),typeof(Brush),typeof(OperatorSymbol),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get=>(Brush)GetValue(ForegroundProperty); set=>SetValue(ForegroundProperty,value); }
    public static readonly AttachedProperty<bool> IsActiveProperty = (AttachedProperty<bool>)NativeProperty.Register(nameof(IsActive),typeof(bool),typeof(OperatorSymbol),new FrameworkPropertyMetadata(false,ActiveChanged));
    public bool IsActive { get=>(bool)GetValue(IsActiveProperty); set=>SetValue(IsActiveProperty,value); }
    private static readonly AttachedProperty<double> FillProgressProperty=(AttachedProperty<double>)NativeProperty.Register("FillProgress",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly AttachedProperty<double> PulseProperty=(AttachedProperty<double>)NativeProperty.Register("Pulse",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly AttachedProperty<double> TurnProperty=(AttachedProperty<double>)NativeProperty.Register("Turn",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private ButtonBase? _button; private TabItem? _tab;
    internal double FillProgress=>(double)GetValue(FillProgressProperty);
    internal bool IsAnimating=>Motion.IsAnimating(this);
    internal object Diagnostics=>new {Kind,IsActive,FillProgress,IsAnimating,Pulse=(double)GetValue(PulseProperty),Turn=(double)GetValue(TurnProperty)};
    public OperatorSymbol()
    {
        IsHitTestVisible=false;Focusable=false;
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_,args)=>{if(args.NewValue is false)Settle();});
    }
    private static void ActiveChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)
    {
        var symbol=(OperatorSymbol)target;symbol.AnimateFill();
        if(args.NewValue is true)symbol.Activate();
        else {symbol.BeginAnimation(PulseProperty,null);symbol.BeginAnimation(TurnProperty,null);}
    }
    private void AnimateFill()
    {
        double target=IsActive?1:0;
        if(!IsLoaded || !OperatorMotion.Enabled){this.BeginAnimation(FillProgressProperty,null);SetValue(FillProgressProperty,target);return;}
        var animation=new DoubleAnimation(target,TimeSpan.FromMilliseconds(320)){EasingFunction=OperatorMotion.SmoothOut};
        animation.Completed+=(_,_)=>{if(IsActive==(target==1)){this.BeginAnimation(FillProgressProperty,null);SetValue(FillProgressProperty,target);}};
        this.BeginAnimation(FillProgressProperty,animation);
    }
    public void Activate()
    {
        if(!IsLoaded || !IsVisible || !OperatorMotion.Enabled)return;
        var pulse=new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)),OperatorMotion.SmoothOut));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(580)),OperatorMotion.SmoothOut));
        pulse.Completed+=(_,_)=>this.BeginAnimation(PulseProperty,null);this.BeginAnimation(PulseProperty,pulse);
        // Gears land on an equivalent tooth; rays land on the next ray. No reverse spin.
        var turn=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(580)){EasingFunction=OperatorMotion.SmoothOut};
        turn.Completed+=(_,_)=>this.BeginAnimation(TurnProperty,null);this.BeginAnimation(TurnProperty,turn);
    }
    private void Attach()
    {
        Detach();OperatorAppearance.Changed+=AppearanceChanged;AvaloniaObject? parent=VisualTreeHelper.GetParent(this);
        while(parent is not null && parent is not ButtonBase && parent is not TabItem)parent=VisualTreeHelper.GetParent(parent);
        if(parent is TabItem tab){_tab=tab;tab.AddHandler(NativeSelection.SelectedEvent,new EventHandler<RoutedEventArgs>(TabChanged));tab.AddHandler(NativeSelection.UnselectedEvent,new EventHandler<RoutedEventArgs>(TabChanged));SetCurrentValue(IsActiveProperty,tab.IsSelected);}
        else if(parent is ButtonBase button){_button=button;button.Click+=Clicked;if(button is ToggleButton toggle){toggle.Checked+=ToggleChanged;toggle.Unchecked+=ToggleChanged;SetCurrentValue(IsActiveProperty,toggle.IsChecked==true);}}
        SetValue(FillProgressProperty,IsActive?1d:0d);
    }
    private void TabChanged(object sender,RoutedEventArgs e){if(ReferenceEquals(e.Source,_tab))SetCurrentValue(IsActiveProperty,_tab!.IsSelected);}
    private void ToggleChanged(object sender,RoutedEventArgs e)=>SetCurrentValue(IsActiveProperty,((ToggleButton)sender).IsChecked==true);
    private void Clicked(object sender,RoutedEventArgs e){if(_button is not ToggleButton)Activate();}
    private void Detach()
    {
        OperatorAppearance.Changed-=AppearanceChanged;
        if(_button is not null){_button.Click-=Clicked;if(_button is ToggleButton toggle){toggle.Checked-=ToggleChanged;toggle.Unchecked-=ToggleChanged;}_button=null;}
        if(_tab is not null){_tab.RemoveHandler(NativeSelection.SelectedEvent,new EventHandler<RoutedEventArgs>(TabChanged));_tab.RemoveHandler(NativeSelection.UnselectedEvent,new EventHandler<RoutedEventArgs>(TabChanged));_tab=null;}
        Settle();
    }
    private void Settle(){foreach(var property in new[]{FillProgressProperty,PulseProperty,TurnProperty})this.BeginAnimation(property,null);SetValue(FillProgressProperty,IsActive?1d:0d);}
    private void AppearanceChanged(){if(!OperatorMotion.Enabled)Settle();}
    protected override Size MeasureOverride(Size size)=>new(Math.Min(24,Math.Max(0,size.Width)),Math.Min(24,Math.Max(0,size.Height)));
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var d = new NativeDrawingContext(nativeContext,Bounds.Size);
        if(Bounds.Width<=0 || Bounds.Height<=0)return;
        // A single square optical frame includes stroke caps and rotating gear teeth.
        // Stretching separately on each axis distorted icons in constrained settings rows.
        double scale=Math.Min(Bounds.Width,Bounds.Height)/28;
        d.PushTransform(new TranslateTransform((Bounds.Width-24*scale)/2,(Bounds.Height-24*scale)/2));
        d.PushTransform(new ScaleTransform(scale,scale));
        string kind=Kind?.ToString()?.ToLowerInvariant()??"";
        double fill=Math.Clamp(Math.Max(FillProgress,(double)GetValue(PulseProperty)),0,1),turn=(double)GetValue(TurnProperty);
        var pen=new Pen(Foreground,1.7){LineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        void Path(string geometry,Brush? brush=null)
        {
            var shape=Shape(geometry);
            if(brush is not null){d.PushOpacity(fill);d.DrawGeometry(brush,null,shape);d.Pop();}
            d.DrawGeometry(null,pen,shape);
        }
        void Tint(Rect bounds,double radius=2){d.PushOpacity(fill*.4);d.DrawRoundedRectangle(Foreground,null,bounds,radius,radius);d.Pop();}
        if(kind.Contains("close") || kind.Contains("cancel"))
        {d.DrawEllipse(null,pen,new Point(12,12),9,9);Path("M9,9 L15,15 M15,9 L9,15");}
        else if(kind.Contains("search"))
        {d.DrawEllipse(null,pen,new Point(10,10),6,6);d.DrawLine(pen,new Point(14.5,14.5),new Point(21,21));}
        else if(kind.Contains("favourite") || kind.Contains("favorite") || kind.Contains("star"))
        {Path("M12,2 L15,8.5 22,9.5 17,14.5 18,22 12,18.5 6,22 7,14.5 2,9.5 9,8.5 Z",Foreground);}
        else if(kind.Contains("undo"))
        {d.PushTransform(new RotateTransform(-18*(double)GetValue(PulseProperty),12,12));Path("M8,5 L3,10 8,15 M3,10 L15,10 C23,10 23,20 15,20");d.Pop();}
        else if(kind=="power")
        {d.PushOpacity(.5+.5*fill);Path("M7,5 A8,8 0 1 0 17,5 M12,2 L12,12");d.Pop();}
        else if(kind=="plus" || kind=="add")
        {Path("M5,12 L19,12 M12,5 L12,19");}
        else if(kind=="afl")
        {
            d.PushTransform(new RotateTransform(30,12,12));
            d.DrawEllipse(Foreground,null,new Point(12,12),6.5,10);
            var seam=new Pen(Avalonia.Media.Brushes.Black,1.2){LineCap=PenLineCap.Round};
            d.DrawLine(seam,new Point(12,6),new Point(12,18));
            for(int i=0;i<4;i++)d.DrawLine(seam,new Point(9.5,7.5+i*3),new Point(14.5,7.5+i*3));d.Pop();
        }
        else if(kind=="cricket")
        {d.PushTransform(new RotateTransform(25,12,12));d.DrawRoundedRectangle(Foreground,null,new Rect(5,9,7,13),2,2);d.DrawRoundedRectangle(Foreground,null,new Rect(7,1,3,10),1,1);d.Pop();d.DrawEllipse(Foreground,null,new Point(19,18),3.3,3.3);}
        else if(kind=="training")
        {d.DrawRoundedRectangle(Foreground,null,new Rect(2,19,20,3),1.5,1.5);Path("M9,3 Q12,1 15,3 L19,18 5,18 Z",Foreground);d.DrawLine(new Pen(Avalonia.Media.Brushes.Black,1.5),new Point(8,10),new Point(16,10));}
        else if(kind.Contains("scorecard"))
        {Tint(new Rect(3,3,18,18),3);Path("M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M3,9 L21,9 M10,9 L10,21 M3,15 L21,15");}
        else if(kind=="bat")
        {Path("M5,18 L12,8 17,12 10,22 Z M13,8 L17,2 20,4 16,10",Foreground);}
        else if(kind=="ball")
        {d.DrawEllipse(null,pen,new Point(12,12),9,9);Path("M6,5 C15,9 9,16 18,19 M8,4 C17,8 11,15 20,17");}
        else if(kind=="eyedropper")
        {Path("M4,17 L15,6 19,10 8,21 3,22 Z M12,4 L20,12 M16,3 L21,8");}
        else if(kind.Contains("more"))
        {for(int i=0;i<3;i++)d.DrawEllipse(Foreground,null,new Point(5+i*7,12),1.6+fill*.4,1.6+fill*.4);}
        else if(kind.Contains("swap") || kind.Contains("replace"))
        {d.PushTransform(new RotateTransform(turn*360,12,12));Path("M3,8 L20,8 15,3 M21,16 L4,16 9,21");d.Pop();}
        else if(kind.Contains("edit"))
        {Tint(new Rect(3,18,18,3),1);Path("M4,15 L15,4 20,9 9,20 3,21 Z M13,6 L18,11");}
        else if(kind.Contains("copy"))
        {Tint(new Rect(8,8,13,13),2);Path("M8,8 L21,8 21,21 8,21 Z M4,16 L3,16 3,3 16,3 16,4");}
        else if(kind.Contains("check") || kind.Contains("fixed"))
        {Tint(new Rect(2,2,20,20),10);Path("M6,12 L10,16 18,7");}
        else if(kind.Contains("new"))
        {Path("M12,2 L14.5,9.5 22,12 14.5,14.5 12,22 9.5,14.5 2,12 9.5,9.5 Z",fill>.01?Foreground:null);}
        else if(kind.Contains("system") || kind=="all")
        {for(int i=0;i<4;i++){var rect=new Rect(3+i%2*11,3+i/2*11,7,7);d.DrawRoundedRectangle(fill*4>i?Foreground:null,pen,rect,2,2);}}
        else if(kind.Contains("timer") || kind.Contains("clock") || kind.Contains("session"))
        {d.DrawEllipse(null,pen,new Point(12,13),8,8);Path("M9,2 L15,2 M12,2 L12,5");d.PushTransform(new RotateTransform(turn*360,12,13));Path("M12,7 L12,13 16,13");d.Pop();}
        else if(kind=="sliders")
        {
            Path("M3,6 L21,6 M3,12 L21,12 M3,18 L21,18");
            d.DrawEllipse(Brushes.Black,pen,new Point(8,6),2.4,2.4);
            d.DrawEllipse(Brushes.Black,pen,new Point(16,12),2.4,2.4);
            d.DrawEllipse(Brushes.Black,pen,new Point(10,18),2.4,2.4);
        }
        else if(kind.Contains("setting") || kind.Contains("control"))
        {
            d.PushTransform(new RotateTransform(turn*360,12,13));
            Path("M9,3 L15,3 15.6,5.6 18,6.5 20.2,5.4 23,10.3 21,12 21,14 23,15.7 20.2,20.6 18,19.5 15.6,20.4 15,23 9,23 8.4,20.4 6,19.5 3.8,20.6 1,15.7 3,14 3,12 1,10.3 3.8,5.4 6,6.5 8.4,5.6 Z");
            d.DrawEllipse(null,pen,new Point(12,13),3.5,3.5);d.Pop();
        }
        else if(kind.Contains("output") || kind.Contains("display"))
        {Tint(new Rect(4.5,5.5,15,Math.Max(.01,10*fill)),1);Path("M3,4 L21,4 21,17 3,17 Z M8,21 L16,21 M12,17 L12,21");}
        else if(kind.Contains("match") || kind.Contains("scor") && !kind.Contains("scorecard"))
        {for(int i=0;i<4;i++){double height=i switch {0=>7,1=>14,2=>10,_=>18};d.PushOpacity(.48+.52*Math.Clamp(fill*4-i,0,1));d.DrawRoundedRectangle(Foreground,null,new Rect(2+i*5.5,21-height,3,height),1.5,1.5);d.Pop();}}
        else if(kind.Contains("weather") || kind.Contains("appearance") || kind.Contains("light"))
        {d.DrawEllipse(fill>.01?Foreground:null,pen,new Point(12,12),4,4);d.PushTransform(new RotateTransform(turn*45,12,12));for(int i=0;i<8;i++){double a=i*Math.PI/4;d.DrawLine(pen,new Point(12+Math.Cos(a)*7,12+Math.Sin(a)*7),new Point(12+Math.Cos(a)*10,12+Math.Sin(a)*10));}d.Pop();}
        else if(kind.Contains("dark"))
        {Path("M15,3 C7,1 2,7 4,15 C6,23 18,23 22,15 C13,19 8,10 15,3 Z",fill>.01?Foreground:null);}
        else if(kind.Contains("density") || kind.Contains("compact") || kind.Contains("comfortable"))
        {for(int i=0;i<3;i++){double y=4+i*(kind.Contains("comfortable")?5+fill*2:7-fill*2);d.DrawRoundedRectangle(Foreground,null,new Rect(4,y,16,2.2),1.1,1.1);}}
        else if(kind.Contains("automation") || kind.Contains("bolt"))
        {Path("M14,2 L5,13 11,13 10,22 20,10 13,10 Z",fill>.01?Foreground:null);}
        else if(kind.Contains("player"))
        {Tint(new Rect(3,13,18,8),4);Path("M3,21 C3,12 11,12 11,21 M13,21 C13,12 21,12 21,21 M7,3 C2,3 2,10 7,10 C12,10 12,3 7,3 M17,3 C12,3 12,10 17,10 C22,10 22,3 17,3");}
        else if(kind.Contains("team"))
        {Path("M3,4 L10,2 17,4 17,11 C17,16 13,19 10,21 C6,19 3,16 3,11 Z",fill>.01?Foreground:null);Path("M19,5 L22,6 22,12 C22,17 19,20 16,22 M7,8 L13,8 M10,5 L10,12");}
        else if(kind.Contains("update") || kind.Contains("download"))
        {Path("M4,17 L4,21 20,21 20,17 M12,3 L12,15 M7,10 L12,15 17,10");d.PushOpacity(fill);d.DrawLine(new Pen(Foreground,3),new Point(7,19),new Point(7+10*fill,19));d.Pop();}
        else if(kind.Contains("forward")){Path("M9,5 L16,12 9,19");d.PushOpacity(fill);d.DrawLine(pen,new Point(4,12),new Point(16,12));d.Pop();}
        else if(kind.Contains("back")){Path("M15,4 L7,12 15,20");}
        else if(kind.Contains("media")){Tint(new Rect(3,5,18,14),3);Path("M3,4 L21,4 21,20 3,20 Z M10,8 L16,12 10,16 Z");}
        else if(kind.Contains("remote")){Tint(new Rect(8,4,8,13));Path("M7,2 L17,2 17,22 7,22 Z M10,18 L14,18");}
        else
        {Path("M5,3 L19,3 19,21 5,21 Z");for(int i=0;i<3;i++){double length=6*(.7+.3*Math.Clamp(fill*3-i,0,1));d.DrawLine(pen,new Point(9,8+i*4),new Point(9+length,8+i*4));}}
        d.Pop();d.Pop();
    }
}
