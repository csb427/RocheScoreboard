using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
namespace RocheScoreboard.Mac.Native;
public sealed class NativeMenuFlags:AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<NativeMenuFlags,MenuItem,bool>("Enabled");
    public static readonly AttachedProperty<bool> HasItemsProperty=AvaloniaProperty.RegisterAttached<NativeMenuFlags,MenuItem,bool>("HasItems");
    private static readonly ConditionalWeakTable<MenuItem,State> States=new();
    static NativeMenuFlags(){EnabledProperty.Changed.AddClassHandler<MenuItem>((target,e)=>{if(e.NewValue is true)States.GetValue(target,m=>new State(m)).Update();});}
    public static bool GetEnabled(MenuItem target)=>target.GetValue(EnabledProperty);
    public static void SetEnabled(MenuItem target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetHasItems(MenuItem target)=>target.GetValue(HasItemsProperty);
    public static void SetHasItems(MenuItem target,bool value)=>target.SetValue(HasItemsProperty,value);
    private sealed class State
    {
        private readonly MenuItem _menu;
        public State(MenuItem menu){_menu=menu;menu.Items.CollectionChanged+=(_,_)=>Update();menu.PropertyChanged+=(_,e)=>{if(e.Property==ItemsControl.ItemsSourceProperty)Update();};}
        public void Update()=>SetHasItems(_menu,_menu.HasSubMenu);
    }
}
