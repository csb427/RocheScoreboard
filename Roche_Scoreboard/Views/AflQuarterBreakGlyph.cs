using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Border=System.Windows.Controls.Border;
using Canvas=System.Windows.Controls.Canvas;
using Point=System.Windows.Point;

namespace Roche_Scoreboard.Views;

/// <summary>A paired-panel presentation emblem, never a clock, score or break-state indicator.</summary>
internal sealed class AflQuarterBreakGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(
        nameof(Progress),typeof(double),typeof(AflQuarterBreakGlyph),new PropertyMetadata(0d,ProgressChanged));
    public double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    internal OperatorChoiceSurface SelectionBridge {get;}=new(){Name="QuarterBreakPresentationSelectionBridge",Width=29,Height=29,Opacity=0};
    private readonly Border _frame=new(){Name="QuarterBreakPresentationFrame",Width=27,Height=19,CornerRadius=new CornerRadius(2),BorderThickness=new Thickness(1.5),IsHitTestVisible=false};
    private readonly Border _leftPanel=new(){Name="QuarterBreakPresentationLeftPanel",Width=8,Height=13,BorderThickness=new Thickness(1.25),IsHitTestVisible=false};
    private readonly Border _rightPanel=new(){Name="QuarterBreakPresentationRightPanel",Width=8,Height=13,BorderThickness=new Thickness(1.25),IsHitTestVisible=false};
    private readonly Border _centre=new(){Name="QuarterBreakPresentationCentreBand",Width=5,Height=3,IsHitTestVisible=false};
    private readonly ScaleTransform _leftFold=new(.68,1),_rightFold=new(.68,1),_centreUnfold=new(1,.50);
    internal double PanelsPhase=>Math.Clamp(Progress/.64,0,1);
    internal double CentrePhase=>Math.Clamp((Progress-.48)/.52,0,1);
    public AflQuarterBreakGlyph()
    {
        Name="QuarterBreakPresentationGlyph";Width=29;Height=29;ClipToBounds=true;Focusable=false;IsHitTestVisible=false;
        Children.Add(SelectionBridge);SetLeft(_frame,1);SetTop(_frame,5);Children.Add(_frame);
        _leftPanel.RenderTransform=_leftFold;_leftPanel.RenderTransformOrigin=new Point(0,.5);
        _rightPanel.RenderTransform=_rightFold;_rightPanel.RenderTransformOrigin=new Point(1,.5);
        _centre.RenderTransform=_centreUnfold;_centre.RenderTransformOrigin=new Point(.5,.5);
        SetLeft(_leftPanel,4);SetTop(_leftPanel,8);Children.Add(_leftPanel);
        SetLeft(_rightPanel,17);SetTop(_rightPanel,8);Children.Add(_rightPanel);
        SetLeft(_centre,12);SetTop(_centre,13);Children.Add(_centre);
        SetSelectedInk(false);ApplyProgress();
    }
    internal void SetSelectedInk(bool selected)
    {
        string key=selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush";
        foreach(var panel in new[]{_frame,_leftPanel,_rightPanel})panel.SetResourceReference(Border.BorderBrushProperty,key);
        _centre.SetResourceReference(Border.BackgroundProperty,key);
    }
    private static void ProgressChanged(DependencyObject target,DependencyPropertyChangedEventArgs args)=>((AflQuarterBreakGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        // Both neutral side panels unfold together about fixed outside hinges;
        // the short centre band settles later. Reversal retraces current parts.
        // Fixed frame/label/target, no loop or public-screen transition timing.
        // Calm uses the existing shared timing; geometry has no preference-
        // dependent endpoint that could become stale at unchanged Progress.
        _leftFold.ScaleX=_rightFold.ScaleX=.68+.32*PanelsPhase;
        _leftPanel.Opacity=_rightPanel.Opacity=.60+.40*PanelsPhase;
        _centreUnfold.ScaleY=.50+.50*CentrePhase;_centre.Opacity=.45+.55*CentrePhase;
    }
}
