// Generated from Services/OperatorChoiceMotion.cs; edit translator/adapters.
// Source SHA-256: 3eebbcc99bcdaaab9f034fcbb984d2141e017f8feace258d5a74124604e8c3d1
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
    private static readonly HashSet<ToggleButton> Active = [];
    static OperatorChoiceMotion() => OperatorAppearance.Changed += () =>
    { if (!OperatorMotion.Enabled) foreach (var button in Active.ToArray()) Settle(button); };
    public static readonly AttachedProperty<double> ProgressProperty=(AttachedProperty<double>)NativeProperty.RegisterAttached("Progress",typeof(double),typeof(OperatorChoiceMotion),new PropertyMetadata(0d));
    public static double GetProgress(AvaloniaObject element)=>(double)element.GetValue(ProgressProperty);
    public static void SetProgress(AvaloniaObject element,double value)=>element.SetValue(ProgressProperty,value);
    public static readonly AttachedProperty<bool> EnabledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool),typeof(OperatorChoiceMotion),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(AvaloniaObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(AvaloniaObject element,bool value)=>element.SetValue(EnabledProperty,value);
    private static void Changed(AvaloniaObject element,AvaloniaPropertyChangedEventArgs args)
    {
        if(element is not ToggleButton button)return;
        if(args.NewValue is true){button.Checked+=Selected;button.Unchecked+=Selected;button.Loaded+=Loaded;button.Unloaded+=Unloaded;if(button.IsLoaded){Active.Add(button);Settle(button);}}
        else{Active.Remove(button);button.Checked-=Selected;button.Unchecked-=Selected;button.Loaded-=Loaded;button.Unloaded-=Unloaded;button.BeginAnimation(ProgressProperty,null);}
    }
    private static void Loaded(object sender,RoutedEventArgs args){var button=(ToggleButton)sender;Active.Add(button);Settle(button);}
    private static void Unloaded(object sender,RoutedEventArgs args){var button=(ToggleButton)sender;Active.Remove(button);Settle(button);}
    private static void Settle(ToggleButton button){button.BeginAnimation(ProgressProperty,null);SetProgress(button,button.IsChecked==true?1:0);}
    private static void Selected(object sender,RoutedEventArgs args)
    {
        var button=(ToggleButton)sender;
        if(!button.IsLoaded || !OperatorMotion.Enabled){Settle(button);return;}
        double target=button.IsChecked==true?1:0;
        var motion=new DoubleAnimation(target,OperatorMotion.ChoiceArrival){EasingFunction=OperatorMotion.SmoothOut};
        motion.Completed+=(_,_)=>{if(button.IsChecked==(target==1))Settle(button);};
        button.BeginAnimation(ProgressProperty,motion);
    }
}
