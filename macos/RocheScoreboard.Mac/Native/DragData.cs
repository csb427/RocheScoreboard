using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Input;

namespace RocheScoreboard.Mac.Native;
public sealed class NativeDataObject
{
    internal string Format { get; }
    internal string Token { get; } = Guid.NewGuid().ToString("N");
    internal object Value { get; }
    public NativeDataObject(string format, object value) { Format = format; Value = value; }
}
public static class NativeDragData
{
    internal static readonly ConcurrentDictionary<string, object> Active = new();
    internal static DataFormat<string> Format(string identifier) => DataFormat.CreateStringApplicationFormat(identifier);
    public static bool Contains(IDataTransfer data, string identifier) => Get(data, identifier) is not null;
    public static object? Get(IDataTransfer data, string identifier)
        => data.TryGetValue(Format(identifier)) is { } token && Active.TryGetValue(token, out var value) ? value : null;
}
public static class NativeDragDrop
{
    public static async Task<DragDropEffects> DoDragDropAsync(PointerEventArgs pointer, NativeDataObject source, DragDropEffects effects)
    {
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(NativeDragData.Format(source.Format), source.Token));
        NativeDragData.Active[source.Token] = source.Value;
        try { return await Avalonia.Input.DragDrop.DoDragDropAsync(pointer, data, effects); }
        finally { NativeDragData.Active.TryRemove(source.Token, out _); }
    }
}
public static class NativeKeyboard
{
    private static KeyModifiers _modifiers;
    private static WeakReference<IInputElement>? _focused;
    static NativeKeyboard()
    {
        InputElement.GotFocusEvent.AddClassHandler<InputElement>((_, e) => { if (e.Source is IInputElement item) _focused = new(item); });
        InputElement.KeyDownEvent.AddClassHandler<InputElement>((_, e) => _modifiers = e.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<InputElement>((_, e) => _modifiers = e.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<InputElement>((_, e) => _modifiers = e.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public static void EnsureInitialized() { }
    public static KeyModifiers Modifiers => (_modifiers & KeyModifiers.Meta) != 0 ? _modifiers | KeyModifiers.Control : _modifiers;
    public static void ClearFocus() => NativeDesktop.MainWindow?.FocusManager?.ClearFocus();
    public static IInputElement? FocusedElement => NativeDesktop.MainWindow?.FocusManager?.GetFocusedElement() ?? (_focused?.TryGetTarget(out var item) == true ? item : null);
    public static bool Focus(Control target) => target.Focus();
}
