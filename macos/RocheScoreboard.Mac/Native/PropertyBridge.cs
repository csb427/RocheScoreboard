using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
namespace RocheScoreboard.Mac.Native;
[Flags] public enum NativePropertyOptions { None=0, AffectsRender=1, AffectsMeasure=2, AffectsArrange=4, Inherits=8, BindsTwoWayByDefault=16 }
public sealed class NativePropertyMetadata
{
    public object? DefaultValue { get; }
    public NativePropertyOptions Options { get; }
    public Action<AvaloniaObject,AvaloniaPropertyChangedEventArgs>? Changed { get; }
    public NativePropertyMetadata(object? value=null, Action<AvaloniaObject,AvaloniaPropertyChangedEventArgs>? changed=null) { DefaultValue=value; Changed=changed; }
    public NativePropertyMetadata(object? value, NativePropertyOptions options, Action<AvaloniaObject,AvaloniaPropertyChangedEventArgs>? changed=null) : this(value,changed) { Options=options; }
}
public sealed record NativePropertyKey(AvaloniaProperty DependencyProperty);
public static class NativeProperty
{
    public static AvaloniaProperty Register(string name, Type valueType, Type owner, NativePropertyMetadata? metadata=null)
        => RegisterAttached(name,valueType,owner,metadata);
    public static NativePropertyKey RegisterReadOnly(string name, Type valueType, Type owner, NativePropertyMetadata? metadata=null)
        => new(Register(name,valueType,owner,metadata));
    public static AvaloniaProperty RegisterAttached(string name, Type valueType, Type owner, NativePropertyMetadata? metadata=null)
        => (AvaloniaProperty)typeof(NativeProperty).GetMethod(nameof(Create),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(valueType).Invoke(null,new object?[]{name,owner,metadata??new NativePropertyMetadata()})!;
    private static AvaloniaProperty Create<T>(string name, Type owner, NativePropertyMetadata metadata)
    {
        var property=AvaloniaProperty.RegisterAttached<AvaloniaObject,T>(name,owner,metadata.DefaultValue is T value?value:default!,metadata.Options.HasFlag(NativePropertyOptions.Inherits));
        property.Changed.Subscribe(new Observer(change=>
        {
            metadata.Changed?.Invoke(change.Sender,change);
            if(change.Sender is Control c)
            {
                if(metadata.Options.HasFlag(NativePropertyOptions.AffectsMeasure))c.InvalidateMeasure();
                if(metadata.Options.HasFlag(NativePropertyOptions.AffectsArrange))c.InvalidateArrange();
                if(metadata.Options.HasFlag(NativePropertyOptions.AffectsRender))c.InvalidateVisual();
            }
        }));
        return property;
    }
    private sealed class Observer(Action<AvaloniaPropertyChangedEventArgs> callback):IObserver<AvaloniaPropertyChangedEventArgs>
    { public void OnNext(AvaloniaPropertyChangedEventArgs value)=>callback(value);public void OnError(Exception error){}public void OnCompleted(){} }
    public static void SetValue(this AvaloniaObject target, NativePropertyKey key,object? value)=>target.SetValue(key.DependencyProperty,value);
}
public sealed class NativeDataCondition : AvaloniaObject
{
    public static readonly AttachedProperty<bool> ActiveProperty=AvaloniaProperty.RegisterAttached<NativeDataCondition,Control,bool>("Active");
    public static bool GetActive(Control control)=>control.GetValue(ActiveProperty);
    public static void SetActive(Control control,bool value)=>control.SetValue(ActiveProperty,value);
}
public sealed class NativePropertyDescriptor(AvaloniaProperty property)
{
    private readonly Dictionary<(AvaloniaObject,EventHandler),EventHandler<AvaloniaPropertyChangedEventArgs>> _handlers=[];
    public static NativePropertyDescriptor FromProperty(AvaloniaProperty property,Type owner)=>new(property);
    public void AddValueChanged(object owner,EventHandler handler)
    {if(owner is not AvaloniaObject target||_handlers.ContainsKey((target,handler)))return;EventHandler<AvaloniaPropertyChangedEventArgs> callback=(s,e)=>{if(e.Property==property)handler(s,EventArgs.Empty);};_handlers[(target,handler)]=callback;target.PropertyChanged+=callback;}
    public void RemoveValueChanged(object owner,EventHandler handler)
    {if(owner is AvaloniaObject target&&_handlers.Remove((target,handler),out var callback))target.PropertyChanged-=callback;}
}
public class NativeCacheMode { }
public sealed class NativeBitmapCache : NativeCacheMode { public NativeBitmapCache(double scale=1) {} }
public static class NativeCache
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AvaloniaObject,State> States=new();
    private sealed class State {public NativeCacheMode? Mode;}
    public static NativeCacheMode? Get(AvaloniaObject target)=>States.GetOrCreateValue(target).Mode;
    public static void Set(AvaloniaObject target,NativeCacheMode? mode)=>States.GetOrCreateValue(target).Mode=mode;
}
public enum NativeBaseValueSource {Default,Inherited,Local}
public static class NativeValueSource
{
    public readonly record struct Source(NativeBaseValueSource BaseValueSource);
    public static Source GetValueSource(AvaloniaObject target,AvaloniaProperty property)=>new(target.IsSet(property)?NativeBaseValueSource.Local:NativeBaseValueSource.Inherited);
    public static object? ReadLocalValue(this AvaloniaObject target,AvaloniaProperty property)=>target.IsSet(property)?target.GetValue(property):AvaloniaProperty.UnsetValue;
}
