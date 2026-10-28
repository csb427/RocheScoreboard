using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
namespace RocheScoreboard.Mac.Native;
public sealed class NativeActivation : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<NativeActivation,Control,bool>("Enabled");
    public static bool GetEnabled(Control target)=>target.GetValue(EnabledProperty);
    public static void SetEnabled(Control target,bool value)=>target.SetValue(EnabledProperty,value);
    static NativeActivation()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((target,args)=>
        {
            target.KeyDown-=ActivateKey;
            if(args.NewValue is true){target.Focusable=true;target.KeyDown+=ActivateKey;}
        });
    }
    private static void ActivateKey(object? sender,KeyEventArgs args)
    {
        if(args.Handled||args.Key is not (Key.Enter or Key.Space)||sender is not Control target||!ReferenceEquals(target,args.Source)||!target.IsEffectivelyEnabled)return;
        args.Handled=true;if(!NativeKeyboard.IsRepeated(args))target.RaiseEvent(Release(target));
    }
    private static readonly IPointer KeyboardPointer=new Pointer(-4271,PointerType.Mouse,true);
    public static PointerReleasedEventArgs Release(Control target)
    {
        var root=TopLevel.GetTopLevel(target);
        var center=new Point(target.Bounds.Width/2,target.Bounds.Height/2);
        var point=root is null?center:target.TranslatePoint(center,root)??center;
        return new PointerReleasedEventArgs(target,KeyboardPointer,root,point,0,default,NativeKeyboard.Modifiers,MouseButton.Left);
    }
}
