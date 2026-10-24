using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RocheScoreboard.Mac.Native;

public enum HandoffBehavior { SnapshotAndReplace }
public readonly record struct KeyTime(double Value, bool IsPercent)
{
    public static KeyTime FromTimeSpan(TimeSpan value) => new(value.TotalMilliseconds, false);
    public static KeyTime FromPercent(double value) => new(value, true);
    internal double Milliseconds(double duration) => IsPercent ? Value * duration : Value;
}
public abstract record DoubleKeyFrame(double Value, KeyTime KeyTime);
public sealed record LinearDoubleKeyFrame(double Value, KeyTime KeyTime) : DoubleKeyFrame(Value, KeyTime);
public sealed record EasingDoubleKeyFrame(double Value, KeyTime KeyTime) : DoubleKeyFrame(Value, KeyTime)
{
    public IEasingFunction? EasingFunction { get; set; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction easing) : this(value, keyTime) => EasingFunction = easing;
}
public sealed record DiscreteDoubleKeyFrame(double Value, KeyTime KeyTime) : DoubleKeyFrame(Value, KeyTime);
public sealed class DoubleAnimationUsingKeyFrames : DoubleAnimation
{
    public List<DoubleKeyFrame> KeyFrames { get; } = new();
    internal override double EffectiveDuration => HasExplicitDuration ? Duration.TotalMilliseconds :
        KeyFrames.Where(x => !x.KeyTime.IsPercent).Select(x => x.KeyTime.Value).DefaultIfEmpty(1000).Max();
    internal override double Interpolate(double progress, double from, double to)
    {
        double previousTime = 0, previousValue = from, time = progress * EffectiveDuration;
        foreach (var frame in KeyFrames.OrderBy(x => x.KeyTime.Milliseconds(EffectiveDuration)))
        {
            double frameTime = frame.KeyTime.Milliseconds(EffectiveDuration);
            if (time < frameTime)
                {
                    double fraction = Math.Clamp((time - previousTime) / Math.Max(.001, frameTime - previousTime), 0, 1);
                    if (frame is EasingDoubleKeyFrame easing) fraction = easing.EasingFunction?.Ease(fraction) ?? fraction;
                    return frame is DiscreteDoubleKeyFrame ? previousValue : previousValue + (frame.Value - previousValue) * fraction;
                }
            previousTime = frameTime; previousValue = frame.Value;
        }
        return previousValue;
    }
}
public sealed class PropertyPath
{
    public object Value { get; }
    public PropertyPath(object value) => Value = value;
    internal (AvaloniaObject Target, AvaloniaProperty Property) Resolve(AvaloniaObject root)
    {
        if (Value is AvaloniaProperty property) return (root, property);
        string path = Regex.Replace((string)Value, @"\((?:[\w:]+\.)?([\w]+)\)", "$1");
        string[] parts = path.Split('.');
        AvaloniaObject target = root;
        foreach (string part in parts[..^1])
        {
            var match = Regex.Match(part, @"^(\w+)\[(\d+)\]$");
            object? value = target.GetType().GetProperty(match.Success ? match.Groups[1].Value : part)?.GetValue(target);
            if (match.Success) value = ((System.Collections.IList)value!)[int.Parse(match.Groups[2].Value)];
            target = value as AvaloniaObject ?? throw new InvalidOperationException($"Cannot animate native path {Value} on {root.GetType().Name}.");
        }
        for (Type? type = target.GetType(); type is not null; type = type.BaseType)
            if (type.GetField(parts[^1] + "Property", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(null) is AvaloniaProperty result)
                return (target, result);
        // Clean's score swell wraps the existing digit translation in a group.
        // Resolve the typed child so the digit slide retains that translation.
        if (target is Avalonia.Media.TransformGroup group)
        {
            foreach (var child in group.Children)
                if (child.GetType().GetField(parts[^1] + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) is AvaloniaProperty childProperty)
                    return (child, childProperty);
        }
        throw new InvalidOperationException($"Native animation property {Value} was not resolved.");
    }
}
public sealed class Storyboard
{
    private sealed class Binding { public AvaloniaObject? Target; public PropertyPath? Property; }
    private static readonly ConditionalWeakTable<DoubleAnimation, Binding> Targets = new();
    public List<DoubleAnimation> Children { get; } = new();
    public event EventHandler? Completed;
    private readonly List<(AvaloniaObject Target, AvaloniaProperty Property)> _running = new();
    private IDisposable? _completion;
    public bool IsRunning { get; private set; }
    public static void SetTarget(DoubleAnimation animation, AvaloniaObject target) => Targets.GetOrCreateValue(animation).Target = target;
    public static void SetTargetProperty(DoubleAnimation animation, PropertyPath path) => Targets.GetOrCreateValue(animation).Property = path;
    public void Begin(Control containingObject, HandoffBehavior handoffBehavior, bool isControllable) => Begin(containingObject, isControllable);
    public void Begin(Control? containingObject = null, bool isControllable = false)
    {
        Stop();
        IsRunning = true;
        var groups = Children.Select(animation =>
        {
            Binding binding = Targets.GetValue(animation, _ => throw new InvalidOperationException("Animation target missing."));
            var address = binding.Property!.Resolve(binding.Target!);
            return (address.Target, address.Property, Animation: animation);
        }).GroupBy(x => (x.Target, x.Property));
        foreach (var group in groups)
        {
            Motion.BeginGroup(group.Key.Target, group.Key.Property, group.Select(x => x.Animation).ToArray());
            _running.Add(group.Key);
        }
        double duration = Children.Select(x => (x.BeginTime ?? TimeSpan.Zero).TotalMilliseconds + x.EffectiveDuration *
            (x.AutoReverse ? 2 : 1) * x.RepeatBehavior.Count).DefaultIfEmpty(0).Max();
        if (double.IsFinite(duration)) _completion = DispatcherTimer.RunOnce(() => { IsRunning = false; Completed?.Invoke(this, EventArgs.Empty); }, TimeSpan.FromMilliseconds(Math.Max(0, duration)));
    }
    public void Stop(Control? containingObject = null)
    {
        IsRunning = false;
        _completion?.Dispose(); _completion = null;
        foreach (var entry in _running) entry.Target.BeginAnimation(entry.Property, null);
        _running.Clear();
    }
    public void Remove(Control? containingObject = null) => Stop(containingObject);
}
