// Generated from Views/AflMatchStatsGlyph.cs; edit translator/adapters.
// Source SHA-256: 39ff839c3b39d476fd664b81c7cff1c80b3de144ec63673c76a9396a8765b638
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Border=Avalonia.Controls.Border;
using Canvas=Avalonia.Controls.Canvas;
using ShapePath=Avalonia.Controls.Shapes.Path;

namespace Roche_Scoreboard.Views;

/// <summary>Neutral paired metric layout; no fabricated figures or comparative bar values.</summary>
internal sealed class AflMatchStatsGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double),typeof(AflMatchStatsGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="MatchStatsSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly ShapePath _spine=new(){Name="StatsMetricSpine",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,
        Data=Geometry.Parse("M14.5,7 L14.5,25"),StrokeThickness=1,Opacity=.65};
    private readonly ShapePath _brace=new(){Name="StatsComparisonBrace",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,
        Data=Geometry.Parse("M4,7 L4,4 25,4 25,7"),StrokeThickness=1.6,StrokeJoin=PenLineJoin.Round};
    private readonly TranslateTransform _braceArrival=new();
    private readonly Border[] _keys=new NativeBorder[3],_left=new NativeBorder[3],_right=new NativeBorder[3];
    private readonly TranslateTransform[] _leftArrival=new TranslateTransform[3],_rightArrival=new TranslateTransform[3];
    internal double HeaderPhase=>Math.Clamp(Progress/.38,0,1);
    internal double RowPhase(int index)=>Math.Clamp((Progress-(.10+index*.14))/.62,0,1);
    public AflMatchStatsGlyph()
    {
        Name="MatchStatsGlyph";Width=29;Height=29;ClipToBounds=true;Focusable=false;IsHitTestVisible=false;
        Children.Add(SelectionBridge);Children.Add(_spine);_brace.RenderTransform=_braceArrival;Children.Add(_brace);
        // Three schematic paired rows describe comparison structure, not the
        // number of metrics, their values or team advantage. All rails retain
        // identical dimensions. Each neutral pair moves together, never fills
        // toward an invented statistic or grows like a live data bar.
        for(int i=0;i<3;i++)
        {
            double y=10+i*6;
            _keys[i]=new NativeBorder{Name="StatsMetricKey"+(i+1),Width=2,Height=1.4,Opacity=.85,IsHitTestVisible=false};
            SetLeft(_keys[i],13.5);SetTop(_keys[i],y+.3);Children.Add(_keys[i]);
            _leftArrival[i]=new TranslateTransform();_rightArrival[i]=new TranslateTransform();
            _left[i]=new NativeBorder{Name="StatsLeftMetricRail"+(i+1),Width=6,Height=2,CornerRadius=new CornerRadius(.6),IsHitTestVisible=false,RenderTransform=_leftArrival[i]};
            _right[i]=new NativeBorder{Name="StatsRightMetricRail"+(i+1),Width=6,Height=2,CornerRadius=new CornerRadius(.6),IsHitTestVisible=false,RenderTransform=_rightArrival[i]};
            SetLeft(_left[i],5);SetLeft(_right[i],18);SetTop(_left[i],y);SetTop(_right[i],y);
            Children.Add(_left[i]);Children.Add(_right[i]);
        }
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _spine.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,key);_brace.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,key);
        foreach(var metric in _keys)metric.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var metric in _left)metric.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var metric in _right)metric.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflMatchStatsGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // One original selection source: comparison header settles, then paired
        // rows align top-to-bottom. Spine/metric keys/text/card stay stationary;
        // current match values and the public statistics screen never enter this icon.
        _brace.Opacity=.65+.35*HeaderPhase;_braceArrival.Y=-2.4*OperatorMotion.TravelScale*(1-HeaderPhase);
        for(int i=0;i<3;i++)
        {
            double p=RowPhase(i);
            if(_left[i] is not null)_left[i].Opacity=.60+.35*p;
            if(_right[i] is not null)_right[i].Opacity=.60+.35*p;
            if(_leftArrival[i] is not null)_leftArrival[i].X=-3.0*OperatorMotion.TravelScale*(1-p);
            if(_rightArrival[i] is not null)_rightArrival[i].X=3.0*OperatorMotion.TravelScale*(1-p);
        }
    }
}
