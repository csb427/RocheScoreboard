using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using RocheScoreboard.Mac.Production;
namespace RocheScoreboard.Mac.Native;
public static class NativeLifecycle
{
    private static readonly ConditionalWeakTable<Control, object> Roots = new();
    private static readonly ConditionalWeakTable<AvaloniaObject, WeakReference<Control>> Owners = new();
    public static void Bind(Control root)
    {
        if (Roots.TryGetValue(root, out _)) return;
        Roots.Add(root, new object());
        foreach (var item in NativeObjectGraph.Walk(root).Concat(ProductionNames.RegistryFor(root).Values))
            Owners.GetValue(item, _ => new WeakReference<Control>(root));
        root.Unloaded += (_, _) =>
        {
            foreach (var item in NativeObjectGraph.Walk(root).Concat(ProductionNames.RegistryFor(root).Values).Distinct())
                Motion.StopAll(item);
        };
    }
    internal static bool Hidden(AvaloniaObject target) =>
        target is Visual visual ? !visual.IsEffectivelyVisible :
        Owners.TryGetValue(target, out var owner) && owner.TryGetTarget(out var root) && !root.IsEffectivelyVisible;
}
