using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Roche_Scoreboard.Services;

/// <summary>Original layered symbols. State changes animate the meaning, never their hit target.</summary>
public sealed class OperatorSymbol : FrameworkElement
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Geometry> Paths=new();
    private static Geometry Shape(string path)=>Paths.GetOrAdd(path,static value=>{var geometry=Geometry.Parse(value);geometry.Freeze();return geometry;});
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind),typeof(object),typeof(OperatorSymbol),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public object? Kind { get=>(object?)GetValue(KindProperty); set=>SetValue(KindProperty,value); }
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground),typeof(Brush),typeof(OperatorSymbol),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get=>(Brush)GetValue(ForegroundProperty); set=>SetValue(ForegroundProperty,value); }
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(nameof(IsActive),typeof(bool),typeof(OperatorSymbol),new FrameworkPropertyMetadata(false,ActiveChanged));
    public bool IsActive { get=>(bool)GetValue(IsActiveProperty); set=>SetValue(IsActiveProperty,value); }
    private static readonly DependencyProperty FillProgressProperty=DependencyProperty.Register("FillProgress",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty PulseProperty=DependencyProperty.Register("Pulse",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty TurnProperty=DependencyProperty.Register("Turn",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private ButtonBase? _button; private TabItem? _tab;
    private int _fillGeneration,_activationGeneration;
    private int _audioGeneration;
    public static readonly DependencyProperty AudioLevelProperty=DependencyProperty.Register(nameof(AudioLevel),typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(1d,AudioChanged));
    public double AudioLevel {get=>(double)GetValue(AudioLevelProperty);set=>SetValue(AudioLevelProperty,Math.Clamp(value,0,1));}
    public static readonly DependencyProperty AudioMutedProperty=DependencyProperty.Register(nameof(AudioMuted),typeof(bool),typeof(OperatorSymbol),new FrameworkPropertyMetadata(false,AudioChanged));
    public bool AudioMuted {get=>(bool)GetValue(AudioMutedProperty);set=>SetValue(AudioMutedProperty,value);}
    private static readonly DependencyProperty AudioProgressProperty=DependencyProperty.Register("AudioProgress",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty MuteProgressProperty=DependencyProperty.Register("MuteProgress",typeof(double),typeof(OperatorSymbol),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    internal double RenderedAudioLevel=>(double)GetValue(AudioProgressProperty);
    internal double RenderedMute=>(double)GetValue(MuteProgressProperty);
    private static void AudioChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)=>((OperatorSymbol)target).AnimateAudio();
    private void AnimateAudio()
    {
        int generation=++_audioGeneration;
        void Part(DependencyProperty property,double value)
        {
            if(!IsLoaded || !IsVisible || !OperatorMotion.Enabled){BeginAnimation(property,null);SetValue(property,value);return;}
            var animation=new DoubleAnimation(value,OperatorMotion.Page){EasingFunction=OperatorMotion.SmoothOut};
            animation.Completed+=(_,_)=>{if(generation==_audioGeneration){BeginAnimation(property,null);SetValue(property,value);}};
            BeginAnimation(property,animation);
        }
        Part(AudioProgressProperty,AudioMuted?0:AudioLevel);Part(MuteProgressProperty,AudioMuted?1:0);
    }
    internal double FillProgress=>(double)GetValue(FillProgressProperty);
    internal bool IsAnimating=>HasAnimatedProperties;
    internal object Diagnostics=>new {Kind,IsActive,FillProgress,IsAnimating,Pulse=(double)GetValue(PulseProperty),Turn=(double)GetValue(TurnProperty)};
    public OperatorSymbol()
    {
        IsHitTestVisible=false;Focusable=false;
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();
        IsVisibleChanged+=(_,args)=>{if(args.NewValue is false)Settle();};
    }
    private static void ActiveChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        var symbol=(OperatorSymbol)target;symbol.AnimateFill();
        if(args.NewValue is true)symbol.Activate();
        else {++symbol._activationGeneration;symbol.BeginAnimation(PulseProperty,null);symbol.BeginAnimation(TurnProperty,null);}
    }
    private void AnimateFill()
    {
        int generation=++_fillGeneration;
        double target=IsActive?1:0;
        if(!IsLoaded || !IsVisible || !OperatorMotion.Enabled){BeginAnimation(FillProgressProperty,null);SetValue(FillProgressProperty,target);return;}
        var animation=new DoubleAnimation(target,OperatorMotion.SymbolFill){EasingFunction=OperatorMotion.SmoothOut};
        animation.Completed+=(_,_)=>{if(generation==_fillGeneration && IsActive==(target==1)){BeginAnimation(FillProgressProperty,null);SetValue(FillProgressProperty,target);}};
        BeginAnimation(FillProgressProperty,animation);
    }
    public void Activate()
    {
        if(!IsLoaded || !IsVisible || !OperatorMotion.Enabled)return;
        // These symbols animate only their semantic selected-state parts: no
        // unrelated pulse/turn timelines or implied live progress.
        if(string.Equals(Kind?.ToString(),"hide-team-names",StringComparison.OrdinalIgnoreCase)
            || string.Equals(Kind?.ToString(),"quarter-break",StringComparison.OrdinalIgnoreCase))return;
        int generation=++_activationGeneration;
        var pulse=new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(OperatorMotion.SymbolAccent),OperatorMotion.SmoothOut));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromTimeSpan(OperatorMotion.SymbolArrival),OperatorMotion.SmoothOut));
        pulse.Completed+=(_,_)=>{if(generation==_activationGeneration)BeginAnimation(PulseProperty,null);};BeginAnimation(PulseProperty,pulse);
        // Finals uses an upright star and a separate finite halo, never a turn.
        if(string.Equals(Kind?.ToString(),"finals",StringComparison.OrdinalIgnoreCase))return;
        // Gears land on an equivalent tooth; rays land on the next ray. No reverse spin.
        double current=(double)GetValue(TurnProperty);current-=Math.Floor(current);
        var turn=new DoubleAnimation(current,1,OperatorMotion.SymbolArrival){EasingFunction=OperatorMotion.SmoothOut};
        turn.Completed+=(_,_)=>{if(generation==_activationGeneration)BeginAnimation(TurnProperty,null);};BeginAnimation(TurnProperty,turn);
    }
    private void Attach()
    {
        Detach();OperatorAppearance.Changed+=AppearanceChanged;DependencyObject? parent=VisualTreeHelper.GetParent(this);
        while(parent is not null && parent is not ButtonBase && parent is not TabItem)parent=VisualTreeHelper.GetParent(parent);
        if(parent is TabItem tab){_tab=tab;tab.AddHandler(Selector.SelectedEvent,new RoutedEventHandler(TabChanged));tab.AddHandler(Selector.UnselectedEvent,new RoutedEventHandler(TabChanged));SetCurrentValue(IsActiveProperty,tab.IsSelected);}
        else if(parent is ButtonBase button){_button=button;button.Click+=Clicked;if(button is ToggleButton toggle){toggle.Checked+=ToggleChanged;toggle.Unchecked+=ToggleChanged;SetCurrentValue(IsActiveProperty,toggle.IsChecked==true);}}
        SetValue(FillProgressProperty,IsActive?1d:0d);
        // A restored selection is already true, not a fresh activation gesture.
        if(string.Equals(Kind?.ToString(),"finals",StringComparison.OrdinalIgnoreCase)
            || string.Equals(Kind?.ToString(),"hide-team-names",StringComparison.OrdinalIgnoreCase)
            || string.Equals(Kind?.ToString(),"quarter-break",StringComparison.OrdinalIgnoreCase))Settle();
    }
    private void TabChanged(object sender,RoutedEventArgs e){if(ReferenceEquals(e.OriginalSource,_tab))SetCurrentValue(IsActiveProperty,_tab!.IsSelected);}
    private void ToggleChanged(object sender,RoutedEventArgs e)=>SetCurrentValue(IsActiveProperty,((ToggleButton)sender).IsChecked==true);
    private void Clicked(object sender,RoutedEventArgs e){if(_button is not ToggleButton)Activate();}
    private void Detach()
    {
        OperatorAppearance.Changed-=AppearanceChanged;
        if(_button is not null){_button.Click-=Clicked;if(_button is ToggleButton toggle){toggle.Checked-=ToggleChanged;toggle.Unchecked-=ToggleChanged;}_button=null;}
        if(_tab is not null){_tab.RemoveHandler(Selector.SelectedEvent,new RoutedEventHandler(TabChanged));_tab.RemoveHandler(Selector.UnselectedEvent,new RoutedEventHandler(TabChanged));_tab=null;}
        Settle();
    }
    private void Settle(){++_activationGeneration;++_fillGeneration;++_audioGeneration;foreach(var property in new[]{FillProgressProperty,PulseProperty,TurnProperty,AudioProgressProperty,MuteProgressProperty})BeginAnimation(property,null);SetValue(FillProgressProperty,IsActive?1d:0d);SetValue(AudioProgressProperty,AudioMuted?0d:AudioLevel);SetValue(MuteProgressProperty,AudioMuted?1d:0d);}
    internal static double MoreDotOffset(int index,double progress)
        =>-2.6*Math.Sin(Math.PI*Math.Clamp((progress-index*.13)/.55,0,1));
    private void AppearanceChanged(){if(!OperatorMotion.Enabled)Settle();}
    protected override Size MeasureOverride(Size size)=>new(Math.Min(24,Math.Max(0,size.Width)),Math.Min(24,Math.Max(0,size.Height)));
    protected override void OnRender(DrawingContext d)
    {
        if(ActualWidth<=0 || ActualHeight<=0)return;
        // A single square optical frame includes stroke caps and rotating gear teeth.
        // Stretching separately on each axis distorted icons in constrained settings rows.
        double scale=Math.Min(ActualWidth,ActualHeight)/28;
        d.PushTransform(new TranslateTransform((ActualWidth-24*scale)/2,(ActualHeight-24*scale)/2));
        d.PushTransform(new ScaleTransform(scale,scale));
        string kind=Kind?.ToString()?.ToLowerInvariant()??"";
        double fill=Math.Clamp(Math.Max(FillProgress,(double)GetValue(PulseProperty)),0,1),turn=(double)GetValue(TurnProperty);
        var pen=new Pen(Foreground,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
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
        {double p=(double)GetValue(PulseProperty);d.DrawEllipse(null,pen,new Point(10,10),6+p*.3,6+p*.3);d.DrawLine(pen,new Point(14.5,14.5),new Point(21+p,21+p));}
        else if(kind=="finals")
        {
            var star=Shape("M12,2 L15,8.5 22,9.5 17,14.5 18,22 12,18.5 6,22 7,14.5 2,9.5 9,8.5 Z");
            Brush ink=SystemParameters.HighContrast?(IsActive?SystemColors.HighlightTextBrush:SystemColors.WindowTextBrush):Foreground;
            var outline=new Pen(ink,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            double selected=Math.Clamp(FillProgress,0,1);
            d.PushOpacity(.68+.32*selected);d.DrawGeometry(null,outline,star);d.Pop();
            if(selected>0)
            {
                // Fill is a state channel; the halo never fills the star early.
                d.PushClip(new RectangleGeometry(new Rect(0,24*(1-selected),24,24*selected)));
                d.DrawGeometry(ink,null,star);d.Pop();
            }
            double pulse=Math.Clamp((double)GetValue(PulseProperty),0,1);
            if(pulse>0)
            {
                double travel=OperatorMotion.TravelScale;
                d.PushOpacity(pulse*.28);
                d.DrawEllipse(null,new Pen(ink,1),new Point(12,12),10.4+1.2*pulse*travel,10.4+1.2*pulse*travel);d.Pop();
                d.PushOpacity(pulse*.72);
                var ray=new Pen(ink,1.2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
                for(int i=0;i<4;i++)
                {
                    double angle=i*Math.PI*.5;
                    double first=10.5,last=10.7+1.2*pulse*travel;
                    d.DrawLine(ray,new Point(12+Math.Cos(angle)*first,12+Math.Sin(angle)*first),new Point(12+Math.Cos(angle)*last,12+Math.Sin(angle)*last));
                }
                d.Pop();
            }
        }
        else if(kind=="quarter-break")
        {
            // The screen/frame stays upright; selected summary rows arrive
            // independently inside it. This is a preference, not live output
            // progress, a countdown, or an instruction to start a break.
            d.DrawRoundedRectangle(null,pen,new Rect(2,3,20,14),2,2);
            d.DrawLine(pen,new Point(12,17),new Point(12,21));
            d.DrawLine(pen,new Point(8,21),new Point(16,21));
            double selected=Math.Clamp(FillProgress,0,1);
            d.PushClip(new RectangleGeometry(new Rect(4,5,16,10)));
            for(int row=0;row<3;row++)
            {
                double progress=Math.Clamp(selected*1.5-row*.25,0,1);
                if(progress<=0)continue;
                double y=6.5+row*3.5,offset=4*(1-progress)*OperatorMotion.TravelScale;
                d.PushOpacity(progress);
                d.DrawLine(pen,new Point(5+offset,y),new Point(18,y));
                d.Pop();
            }
            d.Pop();
        }
        else if(kind=="hide-team-names")
        {
            // Read actual selection progress, never the generic click pulse.
            // The two score wells/marks stay fixed while only nameplates retract.
            double shown=1-Math.Clamp(FillProgress,0,1);
            for(int row=0;row<2;row++)
            {
                double y=4+row*10;
                d.DrawRoundedRectangle(null,pen,new Rect(16,y,6,7),1.4,1.4);
                d.DrawLine(pen,new Point(18,y+3.5),new Point(20,y+3.5));
                if(shown>0)
                {
                    d.PushOpacity(shown);
                    d.PushClip(new RectangleGeometry(new Rect(1,y-1,14*shown,9)));
                    d.DrawRoundedRectangle(null,pen,new Rect(2,y,12,7),2,2);
                    d.DrawLine(pen,new Point(4,y+3.5),new Point(12,y+3.5));
                    d.Pop();d.Pop();
                }
            }
        }
        else if(kind.Contains("favourite") || kind.Contains("favorite") || kind.Contains("star"))
        {Path("M12,2 L15,8.5 22,9.5 17,14.5 18,22 12,18.5 6,22 7,14.5 2,9.5 9,8.5 Z",Foreground);}
        else if(kind=="release-all")
        {
            double p=(double)GetValue(PulseProperty)*OperatorMotion.TravelScale;
            for(int i=0;i<4;i++)
            {
                double x=3+i%2*11+(i%2==0?1:-1)*p,y=3+i/2*11+(i<2?1:-1)*p;
                d.DrawRoundedRectangle(fill*4>i?Foreground:null,pen,new Rect(x,y,7,7),2,2);
            }
        }
        else if(kind=="release-new")
        {
            double radius=8+2*(double)GetValue(PulseProperty)*OperatorMotion.TravelScale;
            d.DrawEllipse(Foreground,null,new Point(12,12),1.3,1.3);
            for(int i=0;i<4;i++){double angle=i*Math.PI*.5;d.DrawLine(pen,new Point(12+Math.Cos(angle)*3.6,12+Math.Sin(angle)*3.6),new Point(12+Math.Cos(angle)*radius,12+Math.Sin(angle)*radius));}
        }
        else if(kind=="release-fixed")
        {
            d.DrawEllipse(null,pen,new Point(12,12),9,9);
            d.PushOpacity(.35);Path("M6,12 L10,16 18,7");d.Pop();
            double first=Math.Clamp(fill*2,0,1),second=Math.Clamp(fill*2-1,0,1);
            d.DrawLine(pen,new Point(6,12),new Point(6+4*first,12+4*first));
            if(second>0)d.DrawLine(pen,new Point(10,16),new Point(10+8*second,16-9*second));
        }
        else if(kind=="launch")
        {
            // A stationary threshold and independently advancing arrow describe
            // entering setup, rather than displaying statistics or playing media.
            Path("M5,21 L5,3 14,3 14,7 M14,17 L14,21 5,21");
            double advance=(double)GetValue(PulseProperty)*1.5*OperatorMotion.TravelScale;
            d.DrawLine(pen,new Point(9,12),new Point(21+advance,12));
            d.DrawLine(pen,new Point(17+advance,8),new Point(21+advance,12));
            d.DrawLine(pen,new Point(21+advance,12),new Point(17+advance,16));
        }
        else if(kind=="resume")
        {
            double p=(double)GetValue(PulseProperty);
            Path("M5,7 A8,8 0 1 0 18,6 M5,3 L5,8 10,8");
            // The return trail stays upright; its forward tip opens separately.
            d.DrawLine(pen,new Point(9,13),new Point(16+p*1.5,13));
            d.DrawLine(pen,new Point(13+p,9.5),new Point(17+p*1.5,13));
            d.DrawLine(pen,new Point(17+p*1.5,13),new Point(13+p,16.5));
        }
        else if(kind.Contains("undo"))
        {d.PushTransform(new RotateTransform(-18*(double)GetValue(PulseProperty),12,12));Path("M8,5 L3,10 8,15 M3,10 L15,10 C23,10 23,20 15,20");d.Pop();}
        else if(kind=="power")
        {d.PushOpacity(.5+.5*fill);Path("M7,5 A8,8 0 1 0 17,5 M12,2 L12,12");d.Pop();}
        else if(kind=="plus" || kind=="add")
        {Path("M5,12 L19,12 M12,5 L12,19");}
        else if(kind=="sport")
        {
            // Both supported sports belong to this navigation step; bar-chart
            // fallback falsely suggested match statistics.
            d.PushTransform(new RotateTransform(25,7,12));d.DrawEllipse(null,pen,new Point(7,12),4,8);Path("M7,7 L7,17 M5,10 L9,10 M5,14 L9,14");d.Pop();
            Path("M15,12 L18,4 21,5 18,13 M14,13 L18,14 15,22 11,20 Z");
        }
        else if(kind is "afl" or "sport-afl")
        {
            d.PushTransform(new RotateTransform(30,12,12));
            d.DrawEllipse(Foreground,null,new Point(12,12),6.5,10);
            var seam=new Pen(System.Windows.Media.Brushes.Black,1.2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            d.DrawLine(seam,new Point(12,6),new Point(12,18));
            // Sport choice accents only the laces; the football silhouette and
            // hit target stay fixed. Other football symbols keep their old form.
            double accent=kind=="sport-afl"?(double)GetValue(PulseProperty)*OperatorMotion.TravelScale:0;
            for(int i=0;i<4;i++)
            {
                double spread=accent*(i is 1 or 2?1.1:.5);
                d.DrawLine(seam,new Point(9.5-spread,7.5+i*3),new Point(14.5+spread,7.5+i*3));
            }
            d.Pop();
        }
        else if(kind is "cricket" or "sport-cricket")
        {
            d.PushTransform(new RotateTransform(25,12,12));d.DrawRoundedRectangle(Foreground,null,new Rect(5,9,7,13),2,2);d.DrawRoundedRectangle(Foreground,null,new Rect(7,1,3,10),1,1);d.Pop();
            // A brief ball-to-bat accent identifies the choice; it is not a
            // delivery, progress or score indicator, and never moves the bat.
            double accent=kind=="sport-cricket"?(double)GetValue(PulseProperty)*OperatorMotion.TravelScale:0;
            d.DrawEllipse(Foreground,null,new Point(19-3*accent,18-2.2*accent),3.3,3.3);
        }
        else if(kind=="training-transport")
        {
            // Session state, not press progress, chooses the action. Triangle
            // and bars have separate paths inside a stationary optical frame.
            double pause=Math.Clamp(FillProgress,0,1);
            d.PushOpacity(1-pause);d.PushTransform(new TranslateTransform((double)GetValue(PulseProperty)*1.2*OperatorMotion.TravelScale,0));
            d.DrawGeometry(Foreground,null,Shape("M6,3 L21,12 6,21 Z"));d.Pop();d.Pop();
            double height=12+6*pause;
            d.PushOpacity(pause);
            d.DrawRoundedRectangle(Foreground,null,new Rect(5,12-height/2,5,height),1,1);
            d.DrawRoundedRectangle(Foreground,null,new Rect(15,12-height/2,5,height),1,1);d.Pop();
        }
        else if(kind is "training" or "training-entry")
        {
            // The cone and base remain fixed. Only its stripe greets entry;
            // this accent represents no timer value or completion progress.
            double accent=kind=="training-entry"?(double)GetValue(PulseProperty)*OperatorMotion.TravelScale:0;
            d.DrawRoundedRectangle(Foreground,null,new Rect(2,19,20,3),1.5,1.5);
            Path("M9,3 Q12,1 15,3 L19,18 5,18 Z",Foreground);
            d.DrawLine(new Pen(System.Windows.Media.Brushes.Black,1.5+.3*accent),new Point(8+.5*accent,10-1.8*accent),new Point(16-.5*accent,10-1.8*accent));
        }
        else if(kind.Contains("scorecard"))
        {Tint(new Rect(3,3,18,18),3);Path("M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M3,9 L21,9 M10,9 L10,21 M3,15 L21,15");}
        else if(kind=="partnership")
        {
            // The two players stay fixed; their connection is the accent.
            d.DrawEllipse(null,pen,new Point(6,6),3,3);d.DrawEllipse(null,pen,new Point(18,6),3,3);
            Path("M1,19 L1,16 Q1,12 6,12 Q9,12 10,15 M23,19 L23,16 Q23,12 18,12 Q15,12 14,15");
            double p=(double)GetValue(PulseProperty)*OperatorMotion.TravelScale;
            d.DrawLine(new Pen(Foreground,1.7+p),new Point(8-p,19),new Point(16+p,19));
        }
        else if(kind=="equation")
        {
            d.DrawEllipse(null,pen,new Point(12,12),9,9);d.DrawEllipse(null,pen,new Point(12,12),4,4);
            d.PushTransform(new RotateTransform(-24*(double)GetValue(PulseProperty)*OperatorMotion.TravelScale,12,12));
            Path("M12,12 L21,3 M16,3 L21,3 21,8");d.Pop();
        }
        else if(kind=="innings-recap")
        {
            for(int i=0;i<3;i++)
            {
                double y=5+i*7;d.DrawEllipse(Foreground,null,new Point(3,y),1.5,1.5);
                double progress=Math.Clamp((turn-i*.12)/.55,0,1);
                double extension=Math.Sin(Math.PI*progress)*2*OperatorMotion.TravelScale;
                d.DrawLine(pen,new Point(8,y),new Point(19+extension,y));
            }
        }
        else if(kind=="bat")
        {Path("M5,18 L12,8 17,12 10,22 Z M13,8 L17,2 20,4 16,10",Foreground);}
        else if(kind=="ball")
        {d.DrawEllipse(null,pen,new Point(12,12),9,9);Path("M6,5 C15,9 9,16 18,19 M8,4 C17,8 11,15 20,17");}
        else if(kind=="eyedropper")
        {Path("M4,17 L15,6 19,10 8,21 3,22 Z M12,4 L20,12 M16,3 L21,8");}
        else if(kind.Contains("more"))
        {for(int i=0;i<3;i++)d.DrawEllipse(Foreground,null,new Point(5+i*7,12+MoreDotOffset(i,turn)),1.6+fill*.4,1.6+fill*.4);}
        else if(kind=="audio")
        {
            // The speaker stays upright. Waves and the mute slash have their
            // own state, independent of the button shell and press accent.
            Path("M2,9 L6,9 12,4 12,20 6,15 2,15 Z",fill>.01?Foreground:null);
            d.PushOpacity(Math.Clamp(RenderedAudioLevel*2,0,1));Path("M15,8 Q20,12 15,16");d.Pop();
            d.PushOpacity(Math.Clamp((RenderedAudioLevel-.35)/.65,0,1));Path("M18,5 Q26,12 18,19");d.Pop();
            d.PushOpacity(Math.Clamp(RenderedMute,0,1));Path("M16,8 L23,16 M23,8 L16,16");d.Pop();
        }
        else if(kind.Contains("display"))
        {Tint(new Rect(2,3,20,14),2);Path("M2,3 L22,3 22,17 2,17 Z M12,17 L12,22 M7,22 L17,22");d.PushOpacity((double)GetValue(PulseProperty));d.DrawLine(new Pen(Foreground,2),new Point(5,14),new Point(19,14));d.Pop();}
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
        else if(kind is "clock-up" or "clock-down")
        {
            // The face and short hand remain upright. Only the minute hand
            // gestures in the chosen timing direction; it is not a live clock.
            d.DrawEllipse(null,pen,new Point(12,13),8,8);Path("M9,2 L15,2 M12,2 L12,5 M12,13 L16,13");
            double direction=kind=="clock-up"?1:-1;
            d.PushTransform(new RotateTransform(direction*42*(double)GetValue(PulseProperty)*OperatorMotion.TravelScale,12,13));
            Path("M12,6 L12,13");d.Pop();
        }
        else if(kind.Contains("timer") || kind.Contains("clock") || kind.Contains("session"))
        {d.DrawEllipse(null,pen,new Point(12,13),8,8);Path("M9,2 L15,2 M12,2 L12,5");d.PushTransform(new RotateTransform(turn*360,12,13));Path("M12,7 L12,13 16,13");d.Pop();}
        else if(kind=="format")
        {Tint(new Rect(3,3,18,18),3);Path("M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 Z M3,9 L21,9 M8,2 L8,5 M16,2 L16,5 M7,13 L11,13 M7,17 L17,17");}
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
