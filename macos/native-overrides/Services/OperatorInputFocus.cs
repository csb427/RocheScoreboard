using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Runtime.CompilerServices;
namespace Roche_Scoreboard.Services;
public sealed class OperatorInputFocus:AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsKeyboardModeProperty=AvaloniaProperty.RegisterAttached<OperatorInputFocus,Control,bool>("IsKeyboardMode",false,true);
    private static readonly ConditionalWeakTable<Control,object> Roots=new();
    public static bool GetIsKeyboardMode(AvaloniaObject target)=>target.GetValue(IsKeyboardModeProperty);
    public static void SetIsKeyboardMode(AvaloniaObject target,bool value)=>target.SetValue(IsKeyboardModeProperty,value);
    internal static void Install(Control root)
    {
        if(Roots.TryGetValue(root,out _))return;Roots.Add(root,new());
        root.AddHandler(InputElement.PointerPressedEvent,(_,_)=>SetIsKeyboardMode(root,false),RoutingStrategies.Tunnel,true);
        root.AddHandler(InputElement.KeyDownEvent,(_,e)=>{if(e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Space)SetIsKeyboardMode(root,true);},RoutingStrategies.Tunnel,true);
    }
}
