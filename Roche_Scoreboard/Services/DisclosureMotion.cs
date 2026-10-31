using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Size=System.Windows.Size;

namespace Roche_Scoreboard.Services;

/// <summary>Reversible disclosure of measured content; rapid changes cannot hide a newly opened panel.</summary>
public static class DisclosureMotion
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(DisclosureMotion),new PropertyMetadata(false,Changed));
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<Expander,Controller> States=new();
    // Validation takes precedence over decoration: reveal the complete group
    // before scrolling/focusing an invalid field, with no deferred stale work.
    internal static void RevealImmediately(Expander owner)
    {owner.IsExpanded=true;States.GetValue(owner,key=>new Controller(key)).Rest();owner.UpdateLayout();}
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {if(target is Expander expander && args.NewValue is true)States.GetValue(expander,key=>new Controller(key));}
    private sealed class Controller
    {
        private readonly Expander _owner;private FrameworkElement? _body;private int _version;
        internal Controller(Expander owner)
        {
            _owner=owner;
            owner.Loaded+=(_,_)=>{OperatorAppearance.Changed-=PreferenceChanged;OperatorAppearance.Changed+=PreferenceChanged;Rest();};
            owner.Unloaded+=(_,_)=>{OperatorAppearance.Changed-=PreferenceChanged;Rest();};
            owner.Expanded+=(_,_)=>Change(true);owner.Collapsed+=(_,_)=>Change(false);
            if(owner.IsLoaded)OperatorAppearance.Changed+=PreferenceChanged;
        }
        private void PreferenceChanged(){if(!OperatorMotion.Enabled)Rest();}
        private FrameworkElement? Body()
        {_owner.ApplyTemplate();return _body=(_owner.Template.FindName("Body",_owner)??_owner.Template.FindName("ExpandSite",_owner)) as FrameworkElement;}
        internal void Rest()
        {++_version;if(Body() is not {} body)return;body.BeginAnimation(FrameworkElement.HeightProperty,null);body.Height=double.NaN;body.BeginAnimation(UIElement.OpacityProperty,null);body.Opacity=1;body.Visibility=_owner.IsExpanded?Visibility.Visible:Visibility.Collapsed;}
        private void Change(bool open)
        {
            int version=++_version;if(Body() is not {} body)return;
            // The template may already have applied its Collapsed trigger.
            // ActualHeight still holds the last arranged frame until layout;
            // using Visibility here would make every close jump straight to 0.
            double start=body.ActualHeight;
            body.BeginAnimation(FrameworkElement.HeightProperty,null);body.Height=double.NaN;body.Visibility=Visibility.Visible;body.ClipToBounds=true;
            body.Measure(new Size(Math.Max(1,_owner.ActualWidth),double.PositiveInfinity));double end=open?body.DesiredSize.Height:0;
            if(!OperatorMotion.Enabled||!_owner.IsLoaded){Rest();return;}
            body.Height=start;
            var animation=new DoubleAnimation(start,end,OperatorMotion.Page){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.HoldEnd};
            animation.Completed+=(_,_)=>{if(version!=_version)return;body.BeginAnimation(FrameworkElement.HeightProperty,null);body.Height=double.NaN;body.Visibility=open?Visibility.Visible:Visibility.Collapsed;};
            body.BeginAnimation(FrameworkElement.HeightProperty,animation);
        }
    }
}
