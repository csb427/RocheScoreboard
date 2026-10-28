using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using TabControl = System.Windows.Controls.TabControl;
using RadioButton = System.Windows.Controls.RadioButton;
using Size = System.Windows.Size;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunLiquidSelectionChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var random=new Random(4319);bool singleBody=true,inside=true;
        for(int i=0;i<512;i++)
        {
            var size=new Size(random.Next(120,1400),random.Next(36,100));double target=random.Next(40,110);
            var shape=OperatorLiquidSelection.Calculate(size,random.NextDouble()*size.Width,random.NextDouble()*size.Width,target,random.Next(-8000,8000),random.Next(-8000,8000),3,1.7,true);
            var geometry=OperatorLiquidSelection.Geometry(shape);var r=shape.Bounds;
            if(r.Width<1)continue;
            inside&=r.Left>=0 && r.Right<=size.Width+.001 && r.Top>=0 && r.Bottom<=size.Height+.001 && shape.Deformation is >=0 and <=1;
            singleBody&=geometry.IsFrozen && geometry.GetFlattenedPathGeometry().Figures.Count==1 && geometry.FillContains(new System.Windows.Point(r.Left+r.Width*.5,r.Top+r.Height*.5)) && geometry.GetArea()>0;
        }
        checks["liquid-geometry-stays-inside-its-control-across-512-motion-poses"]=inside;
        checks["liquid-geometry-is-one-connected-frozen-body"]=singleBody;
        checks["liquid-geometry-rejects-invalid-input-without-an-exception"]=OperatorLiquidSelection.Calculate(new Size(400,60),double.NaN,120,100,0,0,2,1.7,true).Bounds.IsEmpty;
        var rest=OperatorLiquidSelection.Calculate(new Size(400,60),3,97,94,0,0,3,1.7,false);
        var moving=OperatorLiquidSelection.Calculate(new Size(400,60),40,180,94,700,1500,3,1.7,true);
        checks["liquid-body-deforms-in-motion-and-returns-to-rest"]=rest.Deformation==0 && moving.Deformation>0 && moving.Bounds.Height<rest.Bounds.Height;
        var toLeft=OperatorLiquidSelection.Calculate(new Size(400,60),40,180,94,-.1,-.1,3,1.7,true);
        var toRight=OperatorLiquidSelection.Calculate(new Size(400,60),40,180,94,.1,.1,3,1.7,true);
        checks["liquid-shape-direction-is-continuous-through-zero-velocity"]=Math.Abs(toLeft.Direction-toRight.Direction)<.001;
        bool? motion=OperatorMotion.AnimationOverride;bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var title=new TextBlock {Text="Continuous selection",FontSize=28,Margin=new Thickness(24,20,24,20)};title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");root.Children.Add(title);
        var segments=new OperatorSegmentedPanel {Columns=3,Rows=1,Height=56};
        var choices=new[]{"Comfortable","Compact","Automatic"}.Select(label=>new RadioButton {Content=label,GroupName="LiquidFixture",Padding=new Thickness(5),Margin=new Thickness(0),MinHeight=0}).ToArray();
        foreach(var choice in choices){choice.SetResourceReference(StyleProperty,"SettingsSegment");segments.Children.Add(choice);}choices[0].IsChecked=true;
        var surround=new OperatorGlassSurface {CornerRadius=new CornerRadius(30),Padding=new Thickness(4),Margin=new Thickness(24,0,24,12),Child=segments};Grid.SetRow(surround,1);root.Children.Add(surround);
        var tabs=new TabControl {Margin=new Thickness(12)};tabs.SetResourceReference(StyleProperty,"OperatorTabs");
        foreach(string label in new[]{"Match","Scorecard","Output","Settings"})tabs.Items.Add(new TabItem {Header=label,Content=new TextBlock {Text=label,Margin=new Thickness(24),FontSize=20}});
        Grid.SetRow(tabs,2);root.Children.Add(tabs);
        var window=new Window {Owner=this,Width=850,Height=520,Content=root,Title="Liquid selection regression"};OperatorDesign.Apply(window);root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"ControlCentre.CanvasBrush");
        async Task Pause(int delay=50){await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(delay);}
        async Task Settle(Func<bool> done){var timer=System.Diagnostics.Stopwatch.StartNew();while(!done() && timer.ElapsedMilliseconds<1800)await Pause(16);}
        try
        {
            OperatorAppearance.Configure(true,false);OperatorMotion.AnimationOverride=true;window.Show();await Pause(180);
            var lens=(OperatorSelectionLens)tabs.Template.FindName("PART_OperatorLens",tabs);
            var selected=(TabItem)tabs.Items[0];var hitSize=selected.RenderSize;var hitTransform=selected.RenderTransform.Value;
            tabs.SelectedIndex=3;
            checks["liquid-tab-selection-commits-without-waiting-for-the-body"]=tabs.SelectedIndex==3;
            bool deformed=false;
            for(int i=0;i<5;i++){await Pause(35);deformed|=lens.VisualShape.Deformation>0;CaptureCricketQa(root,Path.Combine(directory,$"liquid-tabs-travel-{i}.png"));}
            checks["liquid-tab-motion-deforms-the-background-only"]=deformed && selected.RenderSize==hitSize && selected.RenderTransform.Value==hitTransform && !lens.IsHitTestVisible;
            var before=lens.LensBounds;tabs.SelectedIndex=0;var after=lens.LensBounds;
            checks["liquid-tab-reversal-keeps-its-current-presentation-pose"]=Math.Abs(before.Left-after.Left)+Math.Abs(before.Width-after.Width)<.001;
            for(int i=0;i<20;i++){tabs.SelectedIndex=i%4;await Pause(10);}tabs.SelectedIndex=1;
            await Settle(()=>!lens.IsAnimating);
            checks["rapid-liquid-tab-switches-settle-without-an-idle-render-loop"]=tabs.SelectedIndex==1 && !lens.IsAnimating && lens.VisualShape.Deformation==0;
            choices[2].IsChecked=true;await Pause(60);
            checks["segmented-selection-has-the-same-liquid-motion-family"]=segments.IsAnimating && segments.VisualShape.Deformation>0;
            CaptureCricketQa(root,Path.Combine(directory,"liquid-segments-travel.png"));
            var segmentBefore=segments.VisualShape.Bounds;choices[0].IsChecked=true;
            checks["segmented-reversal-retains-its-visible-pose"]=segments.VisualShape.Bounds==segmentBefore;
            await Settle(()=>!segments.IsAnimating);
            foreach(var choice in choices)choice.IsChecked=false;await Pause(90);
            checks["clearing-a-segmented-choice-removes-the-stale-body"]=segments.VisualShape.Bounds.IsEmpty && !segments.IsAnimating;
            choices[1].IsChecked=true;await Pause(40);segments.Children.Remove(choices[1]);await Pause(90);
            checks["removing-a-selected-segment-cleans-up-its-body-and-ink"]=segments.VisualShape.Bounds.IsEmpty && choices[1].ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty)==DependencyProperty.UnsetValue;
            segments.Children.Insert(1,choices[1]);await Pause(90);
            checks["a-reinserted-selected-choice-recovers-after-layout"]=!segments.VisualShape.Bounds.IsEmpty && !segments.IsAnimating;
            tabs.Items.Clear();await Pause(90);
            checks["empty-navigation-hides-its-retired-selection-lens"]=lens.LensBounds.IsEmpty && !lens.IsAnimating;
            tabs.Items.Add("Loaded later");tabs.SelectedIndex=0;await Pause(90);
            checks["generated-tab-containers-recover-without-a-stale-lens"]=tabs.SelectedIndex==0 && !lens.LensBounds.IsEmpty;
            OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,true);choices[2].IsChecked=true;await Pause(40);
            checks["reduced-motion-keeps-selection-clear-without-deformation"]=choices[2].IsChecked==true && !segments.IsAnimating && segments.VisualShape.Deformation==0;
            OperatorAppearance.Configure(false,true);
            var drawing=new DrawingGroup();using(var dc=drawing.Open())OperatorLiquidSelection.Draw(dc,segments,rest,true);
            checks["solid-material-preference-removes-the-selection-gradient"]=drawing.Children.OfType<GeometryDrawing>().Single().Brush is SolidColorBrush;
            OperatorAppearance.Configure(true,true);
            drawing=new DrawingGroup();using(var dc=drawing.Open())OperatorLiquidSelection.Draw(dc,segments,rest,true);
            checks["restoring-glass-restores-the-selection-material"]=SystemParameters.HighContrast || drawing.Children.OfType<GeometryDrawing>().Single().Brush is LinearGradientBrush;
            window.Width=420;window.Height=450;await Pause(90);
            checks["liquid-selection-resize-keeps-the-body-in-bounds"]=segments.VisualShape.Bounds.Right<=segments.ActualWidth+.01 && lens.LensBounds.Right<=lens.ActualWidth+.01;
            CaptureCricketQa(root,Path.Combine(directory,"liquid-selection-narrow.png"));
        }
        finally {window.Close();OperatorAppearance.Configure(glass,reduced);OperatorMotion.AnimationOverride=motion;}
        return checks;
    }
}
