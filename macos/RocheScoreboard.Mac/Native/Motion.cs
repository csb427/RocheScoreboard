using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data;
using Avalonia.Threading;

namespace RocheScoreboard.Mac.Native;

// The production timelines run against native Avalonia properties. These types
// retain the source timing, interruption, easing and completion semantics.
public enum EasingMode { EaseIn, EaseOut, EaseInOut }
public interface IEasingFunction { double Ease(double progress); }
public abstract class EasingFunction : IEasingFunction
{
    public EasingMode EasingMode { get; set; } = EasingMode.EaseOut;
    protected abstract double EaseIn(double value);
    public double Ease(double value) => EasingMode switch
    {
        EasingMode.EaseIn => EaseIn(value),
        EasingMode.EaseOut => 1 - EaseIn(1 - value),
        _ => value < .5 ? EaseIn(value * 2) / 2 : 1 - EaseIn((1 - value) * 2) / 2
    };
}
public sealed class CubicEase : EasingFunction { protected override double EaseIn(double t) => t * t * t; }
public sealed class QuadraticEase : EasingFunction { protected override double EaseIn(double t) => t * t; }
public sealed class QuarticEase : EasingFunction { protected override double EaseIn(double t) => t * t * t * t; }
public sealed class QuinticEase : EasingFunction { protected override double EaseIn(double t) => t * t * t * t * t; }
public sealed class SineEase : EasingFunction { protected override double EaseIn(double t) => 1 - Math.Cos(t * Math.PI / 2); }
public sealed class BackEase : EasingFunction
{
    public double Amplitude { get; set; } = 1;
    protected override double EaseIn(double t) => t * t * t - t * Amplitude * Math.Sin(t * Math.PI);
}
public sealed class ExponentialEase : EasingFunction
{
    public double Exponent { get; set; } = 2;
    protected override double EaseIn(double t) => Exponent == 0 ? t : (Math.Exp(Exponent * t) - 1) / (Math.Exp(Exponent) - 1);
}
public readonly record struct RepeatBehavior(double Count)
{
    public static RepeatBehavior Forever => new(double.PositiveInfinity);
}
public enum FillBehavior { HoldEnd, Stop }
public class DoubleAnimation
{
    public double? From { get; set; }
    public double? To { get; set; }
    public double? By { get; set; }
    private TimeSpan _duration = TimeSpan.FromSeconds(1);
    internal bool HasExplicitDuration { get; private set; }
    public TimeSpan Duration { get => _duration; set { _duration = value; HasExplicitDuration = true; } }
    internal virtual double EffectiveDuration => Duration.TotalMilliseconds;
    internal virtual double Interpolate(double progress, double from, double to)
        => from + (to - from) * (EasingFunction?.Ease(progress) ?? progress);
    internal virtual object InterpolateValue(double progress, object baseValue)
    {
        double from = From ?? Convert.ToDouble(baseValue);
        return Interpolate(progress, from, To ?? (from + (By ?? 0)));
    }
    public DoubleAnimation Clone() => (DoubleAnimation)MemberwiseClone();
    public TimeSpan? BeginTime { get; set; } = TimeSpan.Zero;
    public bool AutoReverse { get; set; }
    public RepeatBehavior RepeatBehavior { get; set; } = new(1);
    public FillBehavior FillBehavior { get; set; }
    public IEasingFunction? EasingFunction { get; set; }
    public event EventHandler? Completed;
    public DoubleAnimation() { }
    public DoubleAnimation(double to, TimeSpan duration) { To = to; Duration = duration; }
    public DoubleAnimation(double from, double to, TimeSpan duration) { From = from; To = to; Duration = duration; }
    internal void Complete() => Completed?.Invoke(this, EventArgs.Empty);
}
public static class Motion
{
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, Running>> Active = new();
    private sealed class Running : IDisposable
    {
        public required AvaloniaObject Target;
        public required AvaloniaProperty Property;
        public required DoubleAnimation[] Animations;
        public readonly HashSet<DoubleAnimation> Completed = new();
        public required object From;
        public required object To;
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public DispatcherTimer? Timer;
        public IDisposable? Value;
        public bool Finished;
        public void Dispose() { Finished = true; Timer?.Stop(); Value?.Dispose(); Value = null; }
        public void Set(object value)
        {
            // Replace the animation-priority value without modifying the local base.
            var previous = Value;
            Value = Target.SetValue(Property, value, BindingPriority.Animation);
            previous?.Dispose();
        }
        public void Tick()
        {
            if (Finished) return;
            if (Animations.All(a => double.IsPositiveInfinity(a.RepeatBehavior.Count)) && NativeLifecycle.Hidden(Target)) return;
            var completions = new List<DoubleAnimation>();
            double elapsed = Watch.Elapsed.TotalMilliseconds;
            bool allComplete = true;
            object? value = null;
            foreach (var animation in Animations.OrderBy(x => x.BeginTime))
            {
                if (animation.BeginTime is null) continue;
                double local = elapsed - animation.BeginTime.Value.TotalMilliseconds;
                double duration = Math.Max(.001, animation.EffectiveDuration);
                double cycle = duration * (animation.AutoReverse ? 2 : 1);
                double total = cycle * animation.RepeatBehavior.Count;
                if (local < total) allComplete = false;
                if (local < 0) continue;
                bool complete = local >= total;
                double position = complete ? cycle : local % cycle;
                double fraction = animation.AutoReverse && position > duration ? 2 - position / duration : position / duration;
                if (complete) fraction = animation.AutoReverse ? 0 : 1;
                if (!complete || animation.FillBehavior != FillBehavior.Stop)
                    value = animation.InterpolateValue(Math.Clamp(fraction, 0, 1), From);
                if (complete && Completed.Add(animation)) completions.Add(animation);
            }
            if (value is not null) Set(value);
            else { Value?.Dispose(); Value = null; }
            if (allComplete) { Finished = true; Timer?.Stop(); }
            foreach (var animation in completions) animation.Complete();

        }
    }
    internal static void StopAll(AvaloniaObject target)
    {
        if (!Active.TryGetValue(target, out var entries)) return;
        foreach (var running in entries.Values) running.Dispose();
        entries.Clear();
    }
    public static void BeginAnimation(this AvaloniaObject target, AvaloniaProperty property, DoubleAnimation? animation,
        HandoffBehavior behavior = HandoffBehavior.SnapshotAndReplace)
    {
        if (animation is null)
        {
            if (Active.TryGetValue(target, out var entries) && entries.Remove(property, out var previous)) previous.Dispose();
            return;
        }
        BeginGroup(target, property, new[] { animation });
    }
    internal static void BeginGroup(AvaloniaObject target, AvaloniaProperty property, DoubleAnimation[] animations)
    {
        Dispatcher.UIThread.VerifyAccess();
        var entries = Active.GetOrCreateValue(target);
        object current = target.GetValue(property)!;
        if (entries.Remove(property, out var previous)) previous.Dispose();
        var running = new Running
        {
            Target = target, Property = property, Animations = animations,
            From = current, To = current
        };
        entries[property] = running;
        running.Timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => running.Tick());
        running.Tick();
        if (!running.Finished) running.Timer.Start();
    }
}
public sealed class ElasticEase : EasingFunction
{
    public int Oscillations { get; set; } = 3;
    public double Springiness { get; set; } = 3;
    protected override double EaseIn(double t)
    {
        double envelope = Springiness == 0 ? t : (Math.Exp(Springiness * t) - 1) / (Math.Exp(Springiness) - 1);
        return envelope * Math.Sin((Math.PI * 2 * Oscillations + Math.PI / 2) * t);
    }
}
public sealed class ColorAnimation : DoubleAnimation
{
    public new Avalonia.Media.Color? From { get; set; }
    public new Avalonia.Media.Color? To { get; set; }
    public ColorAnimation() { }
    public ColorAnimation(Avalonia.Media.Color to, TimeSpan duration) { To = to; Duration = duration; }
    public ColorAnimation(Avalonia.Media.Color from, Avalonia.Media.Color to, TimeSpan duration) { From = from; To = to; Duration = duration; }
    internal override object InterpolateValue(double progress, object baseValue)
    {
        var from = From ?? (Avalonia.Media.Color)baseValue;
        var to = To ?? (Avalonia.Media.Color)baseValue;
        double eased = EasingFunction?.Ease(progress) ?? progress;
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a + (b - a) * eased), 0, 255);
        return Avalonia.Media.Color.FromArgb(Mix(from.A, to.A), Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }
}
public sealed class PowerEase : EasingFunction
{
    public double Power { get; set; } = 2;
    protected override double EaseIn(double value) => Math.Pow(value, Power);
}
