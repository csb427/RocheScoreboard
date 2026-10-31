// Generated from Views/AflMarginFlowGlyph.cs; edit translator/adapters.
// Source SHA-256: eaafc0c79bed52353d09765173a7dbca63bcd2b4ab5fce28cd8ea31d89bfa99f
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
using Point=Avalonia.Point;

namespace Roche_Scoreboard.Views;

/// <summary>A schematic event-indexed margin step, never current score/history data.</summary>
internal sealed class AflMarginFlowGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double),typeof(AflMarginFlowGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="MarginFlowSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly ShapePath _zero=new(){Name="MarginZeroRail",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,
        Data=Geometry.Parse("M3,4 L3,25 M3,14 L26,14"),StrokeThickness=.8,Opacity=.45};
    private readonly ShapePath _reference=new(){Name="MarginStepReference",Width=29,Height=29,Stretch=Avalonia.Media.Stretch.None,
        Data=Geometry.Parse("M4,18 L9,18 9,9 14,9 14,20 20,20 20,14 25,14"),StrokeThickness=1.6,StrokeJoin=PenLineJoin.Miter,Opacity=.72};
    private readonly Border[] _holds=new NativeBorder[4],_jumps=new NativeBorder[3];
    private readonly ScaleTransform[] _holdReveals=new ScaleTransform[4],_jumpReveals=new ScaleTransform[3];
    internal double HoldPhase=>Math.Clamp(Progress/.55,0,1);
    internal double JumpPhase=>Math.Clamp((Progress-.30)/.70,0,1);
    public AflMarginFlowGlyph()
    {
        Name="MarginFlowGlyph";Width=29;Height=29;ClipToBounds=true;Focusable=false;IsHitTestVisible=false;
        Children.Add(SelectionBridge);Children.Add(_zero);Children.Add(_reference);
        // A complete fixed path remains visible underneath. These short local
        // accents express horizontal holds and vertical event jumps. They are
        // not match events, period counts, a changing lead or a drawing-progress trace.
        double[] holdX={4,9,14,20},holdY={18,9,20,14},holdWidth={5,5,6,5};
        for(int i=0;i<_holds.Length;i++)
        {
            _holdReveals[i]=new ScaleTransform(.60,1);
            _holds[i]=new NativeBorder{Name="MarginHoldShelf"+(i+1),Width=holdWidth[i],Height=1.7,
                IsHitTestVisible=false,RenderTransform=_holdReveals[i],RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative)};
            SetLeft(_holds[i],holdX[i]);SetTop(_holds[i],holdY[i]-.85);Children.Add(_holds[i]);
        }
        double[] jumpX={9,14,20},jumpY={9,9,14},jumpHeight={9,11,6};
        for(int i=0;i<_jumps.Length;i++)
        {
            _jumpReveals[i]=new ScaleTransform(1,.55);
            _jumps[i]=new NativeBorder{Name="MarginJumpConnector"+(i+1),Width=1.7,Height=jumpHeight[i],
                IsHitTestVisible=false,RenderTransform=_jumpReveals[i],RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative)};
            SetLeft(_jumps[i],jumpX[i]-.85);SetTop(_jumps[i],jumpY[i]);Children.Add(_jumps[i]);
        }
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _zero.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,key);
        _reference.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty,key);
        foreach(var hold in _holds)hold.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var jump in _jumps)jump.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflMarginFlowGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // Source selection is the only clock. Each short ledge expands about
        // its own center first; event connectors settle later. No endpoint dot,
        // chart-width expansion, loop or score-bound geometry is introduced.
        foreach(var hold in _holds)if(hold is not null)hold.Opacity=.15+.70*HoldPhase;
        foreach(var reveal in _holdReveals)if(reveal is not null)reveal.ScaleX=1-.40*OperatorMotion.TravelScale*(1-HoldPhase);
        foreach(var jump in _jumps)if(jump is not null)jump.Opacity=.10+.75*JumpPhase;
        foreach(var reveal in _jumpReveals)if(reveal is not null)reveal.ScaleY=1-.45*OperatorMotion.TravelScale*(1-JumpPhase);
    }
}
