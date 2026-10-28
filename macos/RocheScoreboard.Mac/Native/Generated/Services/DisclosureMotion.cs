// Generated from Services/DisclosureMotion.cs; edit translator/adapters.
// Source SHA-256: 7f5aa9871d170266773e27a9d6ae820a6a7655114a23934de65157660c44c1ba
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Size=Avalonia.Size;

namespace Roche_Scoreboard.Services;

/// <summary>Reversible disclosure of measured content; rapid changes cannot hide a newly opened panel.</summary>
public static class DisclosureMotion
{
    public static readonly AttachedProperty<bool> EnabledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Enabled",typeof(bool),typeof(DisclosureMotion),new PropertyMetadata(false,Changed));
    public static void SetEnabled(AvaloniaObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(AvaloniaObject target)=>(bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<Expander,Controller> States=new();
    private static void Changed(AvaloniaObject target,AvaloniaPropertyChangedEventArgs args)
    {if(target is Expander expander && args.NewValue is true)States.GetValue(expander,key=>new Controller(key));}
    private sealed class Controller
    {
        private readonly Expander _owner;private Control? _body;private int _version;
        internal Controller(Expander owner)
        {_owner=owner;owner.Loaded+=(_,_)=>Rest();owner.Unloaded+=(_,_)=>Rest();owner.Expanded+=(_,_)=>Change(true);owner.Collapsed+=(_,_)=>Change(false);}
        private Control? Body()
        {_owner.ApplyTemplate();return _body=(NativeTemplates.FindObject(_owner,"Body")??NativeTemplates.FindObject(_owner,"ExpandSite")) as Control;}
        private void Rest()
        {++_version;if(Body() is not {} body)return;body.BeginAnimation(Control.HeightProperty,null);body.Height=double.NaN;body.BeginAnimation(Control.OpacityProperty,null);body.Opacity=1;body.IsVisible=_owner.IsExpanded?true:false;}
        private void Change(bool open)
        {
            int version=++_version;if(Body() is not {} body)return;
            // The template may already have applied its Collapsed trigger.
            // Bounds.Height still holds the last arranged frame until layout;
            // using bool here would make every close jump straight to 0.
            double start=body.NativeHeight();
            body.BeginAnimation(Control.HeightProperty,null);body.Height=double.NaN;body.IsVisible=true;body.ClipToBounds=true;
            body.Measure(new Size(Math.Max(1,_owner.NativeWidth()),double.PositiveInfinity));double end=open?body.DesiredSize.Height:0;
            if(!OperatorMotion.Enabled||!_owner.IsLoaded){Rest();return;}
            body.Height=start;
            var animation=new DoubleAnimation(start,end,OperatorMotion.Page){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.HoldEnd};
            animation.Completed+=(_,_)=>{if(version!=_version)return;body.BeginAnimation(Control.HeightProperty,null);body.Height=double.NaN;body.IsVisible=open?true:false;};
            body.BeginAnimation(Control.HeightProperty,animation);
        }
    }
}
