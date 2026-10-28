using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Roche_Scoreboard.Services;

/// <summary>A filled selection signal, independent of checkmarks and toggle thumbs.</summary>
public static class OperatorChoiceMotion
{
    private static readonly HashSet<ToggleButton> Active = [];
    static OperatorChoiceMotion() => OperatorAppearance.Changed += () =>
    { if (!OperatorMotion.Enabled) foreach (var button in Active.ToArray()) Settle(button); };
    public static readonly DependencyProperty ProgressProperty=DependencyProperty.RegisterAttached("Progress",typeof(double),typeof(OperatorChoiceMotion),new PropertyMetadata(0d));
    public static double GetProgress(DependencyObject element)=>(double)element.GetValue(ProgressProperty);
    public static void SetProgress(DependencyObject element,double value)=>element.SetValue(ProgressProperty,value);
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(OperatorChoiceMotion),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element,bool value)=>element.SetValue(EnabledProperty,value);
    private static void Changed(DependencyObject element,DependencyPropertyChangedEventArgs args)
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
