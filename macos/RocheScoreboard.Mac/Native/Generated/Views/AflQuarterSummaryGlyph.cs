// Generated from Views/AflQuarterSummaryGlyph.cs; edit translator/adapters.
// Source SHA-256: da8e29a58a41e704479ca9be6b33441279043f74a661bd87c900209b9455ea3b
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Border = Avalonia.Controls.Border;
using Canvas = Avalonia.Controls.Canvas;
using Point = Avalonia.Point;

namespace Roche_Scoreboard.Views;

internal sealed class AflQuarterSummaryGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double),typeof(AflQuarterSummaryGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress { get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value); }
    // Invisible typed binding bridge only; no duplicate visible selection wash.
    internal OperatorChoiceSurface SelectionBridge { get; }=new(){Name="QuarterSummarySelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly Border _page=new NativeBorder(){Name="SummaryPageFrame",Width=25,Height=26,CornerRadius=new CornerRadius(3),BorderThickness=new Thickness(1.6),IsHitTestVisible=false};
    private readonly Border _header=new NativeBorder(){Name="SummaryHeaderUnderline",Width=18,Height=1.5,IsHitTestVisible=false};
    private readonly Border _totals=new NativeBorder(){Name="SummaryTotalsUnderline",Width=18,Height=1.5,IsHitTestVisible=false};
    private readonly ScaleTransform _headerReveal=new(.55,1),_totalsReveal=new(.35,1);
    private readonly Border[] _rules=new NativeBorder[3];
    private readonly ScaleTransform[] _ruleReveals=new ScaleTransform[3];
    internal double HeaderPhase=>Phase(0,.38);
    internal double QuarterPhase(int index)=>Phase(.16+index*.12,.50);
    internal double TotalsPhase=>Phase(.60,.40);
    private double Phase(double start,double span)=>Math.Clamp((Progress-start)/span,0,1);
    public AflQuarterSummaryGlyph()
    {
        Name="QuarterSummaryGlyph";Width=29;Height=29;Focusable=false;IsHitTestVisible=false;ClipToBounds=true;
        Children.Add(SelectionBridge);
        SetLeft(_page,2);SetTop(_page,1.5);Children.Add(_page);
        SetLeft(_header,5.5);SetTop(_header,8);_header.RenderTransform=_headerReveal;_header.RenderTransformOrigin=new RelativePoint(0,.5,RelativeUnit.Relative);Children.Add(_header);
        // These are an abstract ledger grid, not literal scores, quarter
        // completion counters or the number of periods allowed by a ruleset.
        for(int i=0;i<3;i++)
        {
            _ruleReveals[i]=new ScaleTransform(1,.55);
            _rules[i]=new NativeBorder{Name="SummaryQuarterRule"+(i+1),Width=1,Height=10,
                IsHitTestVisible=false,RenderTransform=_ruleReveals[i],RenderTransformOrigin=new RelativePoint(.5,0,RelativeUnit.Relative)};
            SetLeft(_rules[i],9+i*5);SetTop(_rules[i],11);Children.Add(_rules[i]);
        }
        SetLeft(_totals,5.5);SetTop(_totals,23);_totals.RenderTransform=_totalsReveal;_totals.RenderTransformOrigin=new RelativePoint(1,.5,RelativeUnit.Relative);Children.Add(_totals);
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _page.SetResourceReference(Border.BorderBrushProperty,key);
        _header.SetResourceReference(Border.BackgroundProperty,key);_totals.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var rule in _rules)rule.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)=>((AflQuarterSummaryGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // Stable page/label/target. Header lays out first, quarter dividers
        // extend in sequence, totals underline collates last; closing retraces.
        _headerReveal.ScaleX=.55+.45*HeaderPhase;
        _header.Opacity=.55+.45*HeaderPhase;
        for(int i=0;i<3;i++)
        {
            double p=QuarterPhase(i);_ruleReveals[i].ScaleY=.55+.45*p;_rules[i].Opacity=.35+.65*p;
        }
        _totalsReveal.ScaleX=.35+.65*TotalsPhase;_totals.Opacity=.35+.65*TotalsPhase;
    }
}
