using Avalonia;
using System.Runtime.CompilerServices;

namespace RocheScoreboard.Mac.Production;

/// <summary>
/// Avalonia only permits x:Name on styled elements. Retain the production names
/// of transforms, brushes and grid definitions through an attached identifier.
/// Each synchronous XAML load owns its registry, so two outputs never share objects.
/// </summary>
public sealed class ProductionNames : AvaloniaObject
{
    private static readonly ConditionalWeakTable<AvaloniaObject, Identifier> Identifiers = new();
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<string, AvaloniaObject>> Registries = new();

    [ThreadStatic] private static Dictionary<string, AvaloniaObject>? _loading;
    [ThreadStatic] private static Stack<Dictionary<string, AvaloniaObject>?>? _parents;

    public static string GetId(AvaloniaObject target) => Identifiers.TryGetValue(target, out var id) ? id.Value : "";
    public static void SetId(AvaloniaObject target, string value)
    {
        Identifiers.Remove(target);
        Identifiers.Add(target, new Identifier(value));
        if (!string.IsNullOrEmpty(value) && _loading is not null) _loading.Add(value, target);
    }

    public static string GetTemplateId(AvaloniaObject target) => GetId(target);
    public static void SetTemplateId(AvaloniaObject target, string value)
    {
        Identifiers.Remove(target); Identifiers.Add(target, new Identifier(value));
    }
    public static bool GetRoot(AvaloniaObject target) => Registries.TryGetValue(target, out _);
    public static void SetRoot(AvaloniaObject target, bool value)
    {
        if (target is not Avalonia.Controls.Control || !value || ReferenceEquals(_loading, RegistryFor(target))) return;
        (_parents ??= new()).Push(_loading);
        _loading = RegistryFor(target);
    }

    public static Dictionary<string, AvaloniaObject> RegistryFor(AvaloniaObject target) =>
        Registries.GetValue(target, _ => new Dictionary<string, AvaloniaObject>());

    public static void BeginLoad(AvaloniaObject target)=>SetRoot(target,true);

    public static void CompleteLoad(AvaloniaObject target)
    {
        if (target is Avalonia.Controls.Control control) RocheScoreboard.Mac.Native.NativeLifecycle.Bind(control);
        if (ReferenceEquals(_loading, RegistryFor(target)))
            _loading = _parents is { Count: > 0 } ? _parents.Pop() : null;
    }

    private sealed record Identifier(string Value);
}
