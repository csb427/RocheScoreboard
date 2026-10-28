using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shape=System.Windows.Shapes.Shape;
using Control=System.Windows.Controls.Control;
using ListBoxItem=System.Windows.Controls.ListBoxItem;

namespace Roche_Scoreboard.Services;

/// <summary>Draw confirmation inside a fixed selection marker, without moving the choice.</summary>
public static class SelectionCheckMotion
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(SelectionCheckMotion),new PropertyMetadata(false,Changed));
    private static readonly DependencyProperty GenerationProperty=DependencyProperty.RegisterAttached("Generation",typeof(int),typeof(SelectionCheckMotion),new PropertyMetadata(0));
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        if(target is not Control choice || choice is not (ToggleButton or ListBoxItem))return;
        if(choice is ToggleButton toggle){toggle.Checked-=Selected;toggle.Unchecked-=Selected;}
        if(choice is ListBoxItem item){item.Selected-=Selected;item.Unselected-=Selected;}
        choice.Loaded-=Loaded;
        choice.Unloaded-=Loaded;choice.IsVisibleChanged-=VisibilityChanged;
        if(args.NewValue is not true){Paint(choice,false);return;}
        if(choice is ToggleButton enabledToggle){enabledToggle.Checked+=Selected;enabledToggle.Unchecked+=Selected;}
        if(choice is ListBoxItem enabledItem){enabledItem.Selected+=Selected;enabledItem.Unselected+=Selected;}
        choice.Loaded+=Loaded;
        choice.Unloaded+=Loaded;choice.IsVisibleChanged+=VisibilityChanged;
    }
    private static bool IsSelected(Control choice)=>choice is ToggleButton{IsChecked:true} or ListBoxItem{IsSelected:true};
    private static void Selected(object sender,RoutedEventArgs args){if(ReferenceEquals(sender,args.OriginalSource))Paint((Control)sender,true);}
    private static void Loaded(object sender,RoutedEventArgs args)=>Paint((Control)sender,false);
    private static void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)=>Paint((Control)sender,false);
    private static void Paint(Control choice,bool animate)
    {
        int generation=(int)choice.GetValue(GenerationProperty)+1;choice.SetValue(GenerationProperty,generation);
        choice.ApplyTemplate();
        if(choice.Template?.FindName("SelectionStroke",choice) is not Shape check)return;
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
        check.StrokeDashOffset=IsSelected(choice)?0:7;
        if(choice.Template.FindName("SelectionPop",choice) is not ScaleTransform pop)return;
        pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);pop.ScaleX=pop.ScaleY=1;
        if(!animate||!IsSelected(choice)||!choice.IsLoaded||!choice.IsVisible||!OperatorMotion.Enabled)return;
        var draw=new DoubleAnimation(7,0,OperatorMotion.CheckDraw){FillBehavior=FillBehavior.Stop,EasingFunction=OperatorMotion.SmoothOut};
        draw.Completed+=(_,_)=>
        {
            if((int)choice.GetValue(GenerationProperty)!=generation)return;
            check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
            pop.BeginAnimation(ScaleTransform.ScaleXProperty,null);pop.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        };
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,draw);
        var arrival=new DoubleAnimation(.72,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop,EasingFunction=new BackEase{Amplitude=.25,EasingMode=EasingMode.EaseOut}};
        pop.BeginAnimation(ScaleTransform.ScaleXProperty,arrival);pop.BeginAnimation(ScaleTransform.ScaleYProperty,arrival.Clone());
    }
}
