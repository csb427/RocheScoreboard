using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Border = System.Windows.Controls.Border;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace Roche_Scoreboard.Views;

internal sealed class AflQuarterSummaryGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(
        nameof(Progress),typeof(double),typeof(AflQuarterSummaryGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress { get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value); }
    // Invisible typed binding bridge only; no duplicate visible selection wash.
    internal OperatorChoiceSurface SelectionBridge { get; }=new(){Name="QuarterSummarySelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly Border _page=new(){Name="SummaryPageFrame",Width=25,Height=26,CornerRadius=new CornerRadius(3),BorderThickness=new Thickness(1.6),IsHitTestVisible=false};
    private readonly Border _header=new(){Name="SummaryHeaderUnderline",Width=18,Height=1.5,IsHitTestVisible=false};
    private readonly Border _totals=new(){Name="SummaryTotalsUnderline",Width=18,Height=1.5,IsHitTestVisible=false};
    private readonly ScaleTransform _headerReveal=new(.55,1),_totalsReveal=new(.35,1);
    private readonly Border[] _rules=new Border[3];
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
        SetLeft(_header,5.5);SetTop(_header,8);_header.RenderTransform=_headerReveal;_header.RenderTransformOrigin=new Point(0,.5);Children.Add(_header);
        // These are an abstract ledger grid, not literal scores, quarter
        // completion counters or the number of periods allowed by a ruleset.
        for(int i=0;i<3;i++)
        {
            _ruleReveals[i]=new ScaleTransform(1,.55);
            _rules[i]=new Border{Name="SummaryQuarterRule"+(i+1),Width=1,Height=10,
                IsHitTestVisible=false,RenderTransform=_ruleReveals[i],RenderTransformOrigin=new Point(.5,0)};
            SetLeft(_rules[i],9+i*5);SetTop(_rules[i],11);Children.Add(_rules[i]);
        }
        SetLeft(_totals,5.5);SetTop(_totals,23);_totals.RenderTransform=_totalsReveal;_totals.RenderTransformOrigin=new Point(1,.5);Children.Add(_totals);
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        _page.SetResourceReference(Border.BorderBrushProperty,key);
        _header.SetResourceReference(Border.BackgroundProperty,key);_totals.SetResourceReference(Border.BackgroundProperty,key);
        foreach(var rule in _rules)rule.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)=>((AflQuarterSummaryGlyph)target).ApplyProgress();
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
