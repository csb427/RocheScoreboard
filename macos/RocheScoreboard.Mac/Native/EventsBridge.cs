using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
namespace RocheScoreboard.Mac.Native;
public static class NativeEventManager
{
    public static void RegisterClassHandler(Type type,RoutedEvent routed,Delegate handler)
    {
        // Subscribe once to the framework route; preserve the declared owner type.
        routed.Raised.Subscribe(new Observer(pair=>{if(type.IsInstanceOfType(pair.Item1))handler.DynamicInvoke(pair.Item1,pair.Item2);}));
    }
    private sealed class Observer(Action<(object,RoutedEventArgs)> callback):IObserver<(object,RoutedEventArgs)>
    {public void OnNext((object,RoutedEventArgs) value)=>callback(value);public void OnCompleted(){}public void OnError(Exception error){}}
}
public static class NativeSelection
{
    public static readonly RoutedEvent<RoutedEventArgs> SelectedEvent=RoutedEvent.Register<SelectingItemsControl,RoutedEventArgs>("NativeSelected",RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> UnselectedEvent=RoutedEvent.Register<SelectingItemsControl,RoutedEventArgs>("NativeUnselected",RoutingStrategies.Bubble);
    static NativeSelection()
    {
        ListBoxItem.IsSelectedProperty.Changed.AddClassHandler<ListBoxItem>((target,e)=>target.RaiseEvent(new RoutedEventArgs(e.NewValue is true?SelectedEvent:UnselectedEvent,target)));
        TabItem.IsSelectedProperty.Changed.AddClassHandler<TabItem>((target,e)=>target.RaiseEvent(new RoutedEventArgs(e.NewValue is true?SelectedEvent:UnselectedEvent,target)));
    }
}
public static class NativeEvents
{
    public static void BringIntoView(Control root,EventHandler<RequestBringIntoViewEventArgs> handler)=>NativeBringIntoView.Attach(root,handler);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AvaloniaObject,Dictionary<(AvaloniaProperty,Delegate,bool?),EventHandler<AvaloniaPropertyChangedEventArgs>>> Handlers=new();
    public static void WatchProperty(AvaloniaObject target,AvaloniaProperty property,bool adding,EventHandler<AvaloniaPropertyChangedEventArgs> handler)=>Watch(target,property,adding,handler,null);
    public static void WatchSelection(AvaloniaObject target,bool adding,bool selected,EventHandler<RoutedEventArgs> handler)=>Watch(target,ListBoxItem.IsSelectedProperty,adding,handler,selected);
    private static void Watch(AvaloniaObject target,AvaloniaProperty property,bool adding,Delegate handler,bool? selected)
    {
        var entries=Handlers.GetOrCreateValue(target);var key=(property,handler,selected);
        if(!adding){if(entries.Remove(key,out var previous))target.PropertyChanged-=previous;return;}
        if(entries.ContainsKey(key))return;
        EventHandler<AvaloniaPropertyChangedEventArgs> callback=(sender,args)=>
        {
            if(args.Property!=property)return;
            if(selected is {} expected){if(args.NewValue is bool state&&state==expected)((EventHandler<RoutedEventArgs>)handler)(sender,new RoutedEventArgs { Source = sender });}
            else ((EventHandler<AvaloniaPropertyChangedEventArgs>)handler)(sender,args);
        };
        entries.Add(key,callback);target.PropertyChanged+=callback;
    }
}
public static class NativeBringIntoView
{
    public static void Attach(Control root,EventHandler<RequestBringIntoViewEventArgs> handler)=>root.AddHandler(Control.RequestBringIntoViewEvent,(sender,args)=>{if(!NativeKeyboard.IsNavigating)handler(sender,args);},RoutingStrategies.Tunnel|RoutingStrategies.Bubble);
}
