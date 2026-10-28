// Generated from Services/OperatorChoiceMotion.cs; edit translator/adapters.
// Source SHA-256: 3e8bca3f4f4f4441c549c138154431c8a03f8c5aec8f3d6144114e37a3f42738
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>A filled selection signal, independent of checkmarks and toggle thumbs.</summary>
public static class OperatorChoiceMotion
{
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.RegisterAttached("Progress",typeof(double),typeof(OperatorChoiceMotion),new PropertyMetadata(0d));
    public static double GetProgress(AvaloniaObject element)=>(double)element.GetValue(ProgressProperty);
    public static void SetProgress(AvaloniaObject element,double value)=>element.SetValue(ProgressProperty,value);
    public static readonly AttachedProperty<bool> EnabledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool),typeof(OperatorChoiceMotion),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(AvaloniaObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject element,bool value)=>element.SetValue(EnabledProperty,value);
    private static void Changed(AvaloniaObject element,AvaloniaPropertyChangedEventArgs args)
    {
        if(element is not ToggleButton button)return;
        if(args.NewValue is true){button.Checked+=Selected;button.Unchecked+=Selected;button.Loaded+=Loaded;button.Unloaded+=Unloaded;}
        else{button.Checked-=Selected;button.Unchecked-=Selected;button.Loaded-=Loaded;button.Unloaded-=Unloaded;button.BeginAnimation(ProgressProperty,null);}
    }
    private static void Loaded(object sender,RoutedEventArgs args)=>Settle((ToggleButton)sender);
    private static void Unloaded(object sender,RoutedEventArgs args)=>Settle((ToggleButton)sender);
    private static void Settle(ToggleButton button){button.BeginAnimation(ProgressProperty,null);SetProgress(button,button.IsChecked==true?1:0);}
    private static void Selected(object sender,RoutedEventArgs args)
    {
        var button=(ToggleButton)sender;
        if(!button.IsLoaded || !OperatorMotion.Enabled){Settle(button);return;}
        double target=button.IsChecked==true?1:0;
        var motion=new DoubleAnimation(target,TimeSpan.FromMilliseconds(210)){EasingFunction=OperatorMotion.SmoothOut};
        motion.Completed+=(_,_)=>{if(button.IsChecked==(target==1))Settle(button);};
        button.BeginAnimation(ProgressProperty,motion);
    }
}
