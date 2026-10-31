using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace RocheScoreboard.Mac.Native;

/// <summary>WPF effective-visibility semantics for the three shared motion owners.</summary>
public static class NativeEffectiveVisibility
{
    private static readonly ConditionalWeakTable<Control, State> States = new();

    public static bool IsVisible(Control owner)
    {
        if (States.TryGetValue(owner, out var state) && state.Detached) return false;
        Visual? last = null;
        for (Visual? current = owner; current is not null; current = current.GetVisualParent())
        {
            if (!current.IsVisible) return false;
            last = current;
        }
        return last is TopLevel && owner.GetVisualRoot() is not null;
    }

    public static void WatchProperty(AvaloniaObject target, AvaloniaProperty property, bool adding,
        EventHandler<AvaloniaPropertyChangedEventArgs> handler)
    {
        if (property != Control.IsVisibleProperty || target is not Control owner)
        {
            NativeEvents.WatchProperty(target, property, adding, handler);
            return;
        }
        if (adding) States.GetValue(owner, key => new State(key)).Handlers.Add(handler);
        else if (States.TryGetValue(owner, out var state))
        {
            state.Handlers.Remove(handler);
            if (state.Handlers.Count == 0) { States.Remove(owner); state.Dispose(); }
        }
    }

    private sealed class State
    {
        private readonly Control _owner;
        private readonly List<Visual> _ancestors = new();
        private bool _visible, _disposed;
        private int _notificationGeneration;
        internal bool Detached { get; private set; }
        internal HashSet<EventHandler<AvaloniaPropertyChangedEventArgs>> Handlers { get; } = new();
        internal State(Control owner)
        {
            _owner = owner;
            Detached = owner.GetVisualRoot() is null;
            _visible = IsVisible(owner);
            owner.AttachedToVisualTree += Attached;
            owner.DetachedFromVisualTree += Detaching;
            owner.PropertyChanged += PropertyChanged;
            Rebuild();
        }
        private void ClearAncestors()
        {
            foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= PropertyChanged;
            _ancestors.Clear();
        }
        private void Rebuild()
        {
            ClearAncestors();
            if (Detached || _disposed) return;
            for (var parent = _owner.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
            {
                _ancestors.Add(parent);
                parent.PropertyChanged += PropertyChanged;
            }
        }
        private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            Detached = false;
            Rebuild();
            Publish();
        }
        private void Detaching(object? sender, VisualTreeAttachmentEventArgs args)
        {
            // Framework tree pointers may still describe the old root during
            // this callback. Readers and the event must agree immediately.
            Detached = true;
            ClearAncestors();
            Publish();
        }
        private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_disposed) return;
            if (args.Property == Visual.VisualParentProperty) Rebuild();
            if (args.Property == Visual.IsVisibleProperty || args.Property == Visual.VisualParentProperty) Publish();
        }
        private void Publish()
        {
            if (_disposed) return;
            bool visible = IsVisible(_owner);
            if (_visible == visible) return;
            bool previous = _visible;
            _visible = visible;
            // Notify only the registered compatibility callbacks. The actual
            // Avalonia local visibility property and its subscribers are untouched.
            var args = new AvaloniaPropertyChangedEventArgs<bool>(_owner, Control.IsVisibleProperty,
                new Optional<bool>(previous), new BindingValue<bool>(visible), BindingPriority.LocalValue);
            int generation = ++_notificationGeneration;
            foreach (var handler in Handlers.ToArray())
            {
                if (_disposed || generation != _notificationGeneration) break;
                if (Handlers.Contains(handler)) handler(_owner, args);
            }
        }
        internal void Dispose()
        {
            _disposed = true;
            ++_notificationGeneration;
            ClearAncestors();
            _owner.AttachedToVisualTree -= Attached;
            _owner.DetachedFromVisualTree -= Detaching;
            _owner.PropertyChanged -= PropertyChanged;
            Handlers.Clear();
        }
    }
}
