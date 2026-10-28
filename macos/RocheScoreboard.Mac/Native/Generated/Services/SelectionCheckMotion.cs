// Generated from Services/SelectionCheckMotion.cs; edit translator/adapters.
// Source SHA-256: fa770b5e851acdaca7069fff886a36107932d59989e4bb3e42d35f13f2d4ff90
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Shape=Avalonia.Controls.Shapes.Shape;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using ListBoxItem=Avalonia.Controls.ListBoxItem;

namespace Roche_Scoreboard.Services;

/// <summary>Draw confirmation inside a fixed selection marker, without moving the choice.</summary>
public static class SelectionCheckMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool),typeof(SelectionCheckMotion),new PropertyMetadata(false,Changed));
    private static readonly AttachedProperty<int> GenerationProperty=(AttachedProperty<int>)NativeProperty.RegisterAttached("Generation",typeof(int),typeof(SelectionCheckMotion),new PropertyMetadata(0));
    public static void SetEnabled(AvaloniaObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(AvaloniaObject target)=>(bool)target.GetValue(EnabledProperty);
    private static void Changed(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)
    {
        if(target is not TemplatedControl choice || choice is not (ToggleButton or ListBoxItem))return;
        if(choice is ToggleButton toggle){toggle.Checked-=Selected;toggle.Unchecked-=Selected;}
        if(choice is ListBoxItem item){NativeEvents.WatchSelection(item,false,true,Selected);NativeEvents.WatchSelection(item,false,false,Selected);}
        choice.Loaded-=Loaded;
        choice.Unloaded-=Loaded;NativeEvents.WatchProperty(choice,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged);
        if(args.NewValue is not true){Paint(choice,false);return;}
        if(choice is ToggleButton enabledToggle){enabledToggle.Checked+=Selected;enabledToggle.Unchecked+=Selected;}
        if(choice is ListBoxItem enabledItem){NativeEvents.WatchSelection(enabledItem,true,true,Selected);NativeEvents.WatchSelection(enabledItem,true,false,Selected);}
        choice.Loaded+=Loaded;
        choice.Unloaded+=Loaded;NativeEvents.WatchProperty(choice,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged);
    }
    private static bool IsSelected(TemplatedControl choice)=>choice is ToggleButton{IsChecked:true} or ListBoxItem{IsSelected:true};
    private static void Selected(object sender,RoutedEventArgs args){if(ReferenceEquals(sender,args.Source))Paint((TemplatedControl)sender,true);}
    private static void Loaded(object sender,RoutedEventArgs args)=>Paint((TemplatedControl)sender,false);
    private static void VisibilityChanged(object sender,AvaloniaPropertyChangedEventArgs args)=>Paint((TemplatedControl)sender,false);
    private static void Paint(TemplatedControl choice,bool animate)
    {
        int generation=(int)choice.GetValue(GenerationProperty)+1;choice.SetValue(GenerationProperty,generation);
        choice.ApplyTemplate();
        if(NativeTemplates.FindObject(choice,"SelectionStroke") is not Shape check)return;
        check.BeginAnimation(Shape.StrokeDashOffsetProperty,null);
        check.StrokeDashOffset=IsSelected(choice)?0:7;
        if(NativeTemplates.FindObject(choice,"SelectionPop") is not ScaleTransform pop)return;
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
