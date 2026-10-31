using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using RocheScoreboard.Mac.Production;

namespace RocheScoreboard.Mac.Native;

public sealed class TemplateMotion
{
    private static readonly ConditionalWeakTable<Control, Registration> Registrations = new();
    public static string GetSpec(Control target) => Registrations.TryGetValue(target, out var value) ? value.Spec : "[]";
    public static void SetSpec(Control target, string value) => Register(target, value, false);
    internal static void SetSceneSpec(Control target, string value) => Register(target, value, true);
    private static void Register(Control target, string value, bool scene)
    {
        var registration = new Registration(target, value, scene);
        Registrations.Add(target, registration);
        target.Loaded += registration.Loaded;
        target.Unloaded += registration.Unloaded;
    }
    private sealed class Registration(Control root, string spec, bool scene)
    {
        public string Spec => spec;
        private Control? _owner;
        private readonly List<Storyboard> _boards = new();
        public void Loaded(object? sender, RoutedEventArgs e)
        {
            if (scene) { _owner = root; Play("FrameworkElement.Loaded"); return; }
            _owner = root.TemplatedParent as Control ?? root.GetVisualAncestors().OfType<Control>().FirstOrDefault(x => x is Button or Avalonia.Controls.Primitives.TemplatedControl);
            if (_owner is null) throw new InvalidOperationException("Production template has no control owner.");
            _owner.PointerEntered += Enter; _owner.PointerExited += Leave;
            if (_owner is Button button) button.Click += Click;
        }
        public void Unloaded(object? sender, RoutedEventArgs e)
        {
            if (_owner is not null)
            {
                _owner.PointerEntered -= Enter; _owner.PointerExited -= Leave;
                if (_owner is Button button) button.Click -= Click;
            }
            foreach (var board in _boards) board.Stop(); _boards.Clear(); _owner = null;
        }
        private void Enter(object? sender, PointerEventArgs e) => Play("MouseEnter");
        private void Leave(object? sender, PointerEventArgs e) => Play("MouseLeave");
        private void Click(object? sender, RoutedEventArgs e) => Play("Button.Click");
        private void Play(string eventName)
        {
            using var document = JsonDocument.Parse(spec);
            foreach (var trigger in document.RootElement.EnumerateArray().Where(x => x.GetProperty("event").GetString() == eventName))
            {
                var board = new Storyboard();
                bool repeat = false;
                foreach (var item in trigger.GetProperty("animations").EnumerateArray())
                {
                    repeat |= item.TryGetProperty("_StoryboardRepeat", out var repeatValue) && repeatValue.GetString() == "Forever";
                    var targetName = item.GetProperty("Storyboard.TargetName").GetString()!;
                    var target = NativeObjectGraph.Walk(root).FirstOrDefault(x => ProductionNames.GetId(x) == targetName || x is Control c && c.Name == targetName)
                        ?? throw new InvalidOperationException($"Production template animation target {targetName} missing.");
                    var animation = new DoubleAnimation();
                    if (item.TryGetProperty("From", out var from)) animation.From = double.Parse(from.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    if (item.TryGetProperty("To", out var to)) animation.To = double.Parse(to.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    if (item.TryGetProperty("Duration", out var duration)) animation.Duration = TimeSpan.Parse(duration.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    if (item.TryGetProperty("BeginTime", out var delay)) animation.BeginTime = TimeSpan.Parse(delay.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    if (item.TryGetProperty("AutoReverse", out var reverse)) animation.AutoReverse = bool.Parse(reverse.GetString()!);
                    if (item.TryGetProperty("Easing", out var easing))
                    {
                        EasingFunction function = easing.GetString() switch { "CubicEase" => new CubicEase(), "QuadraticEase" => new QuadraticEase(), "BackEase" => new BackEase(), "SineEase" => new SineEase(), _ => throw new NotSupportedException($"Template easing {easing} requires a native mapping.") };
                        if (item.TryGetProperty("EasingMode", out var mode)) function.EasingMode = Enum.Parse<EasingMode>(mode.GetString()!);
                        if (function is BackEase back && item.TryGetProperty("Amplitude", out var amp)) back.Amplitude = double.Parse(amp.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        animation.EasingFunction = function;
                    }
                    Storyboard.SetTarget(animation, target);
                    Storyboard.SetTargetProperty(animation, new PropertyPath(item.GetProperty("Storyboard.TargetProperty").GetString()!));
                    board.Children.Add(animation);
                }
                _boards.RemoveAll(x => !x.IsRunning);
                if (repeat) board.Completed += (_, _) => { if (_owner is not null) board.Begin(); };
                _boards.Add(board); board.Begin();
            }
        }
    }
}
public static class NativeObjectGraph
{
    public static IEnumerable<AvaloniaObject> Walk(AvaloniaObject root)
    {
        var seen = new HashSet<AvaloniaObject>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<AvaloniaObject>(); queue.Enqueue(root);
        while (queue.TryDequeue(out var next))
        {
            if (!seen.Add(next)) continue;
            yield return next;
            if (next is Visual visual) foreach (var child in visual.GetVisualChildren()) queue.Enqueue(child);
            foreach (string name in new[] { "RenderTransform", "LayoutTransform", "Effect", "Background", "Fill", "Stroke", "Children", "GradientStops", "ColumnDefinitions", "RowDefinitions" })
            {
                object? value = next.GetType().GetProperty(name)?.GetValue(next);
                if (value is AvaloniaObject item) queue.Enqueue(item);
                else if (value is IEnumerable items) foreach (var child in items.OfType<AvaloniaObject>()) queue.Enqueue(child);
            }
        }
    }
}
public static class NativePointer
{
    private static IPointer? _pointer;
    static NativePointer()
    {
        InputElement.PointerPressedEvent.AddClassHandler<InputElement>((_, e) => _pointer = e.Pointer, RoutingStrategies.Tunnel);
    }
    public static void EnsureInitialized() => NativeKeyboard.EnsureInitialized();
    public static void Capture(Control target) => (_pointer ?? throw new InvalidOperationException("No active pointer to capture.")).Capture(target);
    public static void Capture(Control target,PointerEventArgs args) { _pointer=args.Pointer;_pointer.Capture(target); }
    public static bool IsCaptureWithin(Control root)=>_pointer?.Captured is Avalonia.Visual visual&&(ReferenceEquals(visual,root)||Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(visual).Contains(root));
    public static bool IsCaptured(Control? target) => target is not null && ReferenceEquals(_pointer?.Captured, target);
    public static void Release(Control? target) { if (target is not null && _pointer is not null && ReferenceEquals(_pointer.Captured, target)) _pointer.Capture(null); }
}

public sealed class SceneMotion
{
    public static string GetSpec(Control target) => TemplateMotion.GetSpec(target);
    public static void SetSpec(Control target, string value) => TemplateMotion.SetSceneSpec(target, value);
}
