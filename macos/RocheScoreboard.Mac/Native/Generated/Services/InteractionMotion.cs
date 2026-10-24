// Generated from Services/InteractionMotion.cs; edit translator/adapters.
// Source SHA-256: b823931406cb29676d707dcc983e73df6b46b39a971937897117f81521f57669
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Adds consistent, tactile pointer feedback to operator-facing controls.
/// Existing transforms are retained inside a transform group so specialised
/// card and button animations can continue to run alongside this behaviour.
/// </summary>
internal static class InteractionMotion
{
    private const double HoverScale = 1.03;
    private const double PressedScale = 0.965;
    private const double RewardScale = 1.05;
    private const double HoverLift = -2.0;
    private const int HoverMilliseconds = 150;
    private const int PressMilliseconds = 80;
    private const int RewardMilliseconds = 220;
    private sealed class MotionState
    {
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Lift { get; init; }
        public bool Pressed { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, MotionState> States = new();
    private static readonly CubicEase SmoothOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase RewardOut = new() { Amplitude = 0.42, EasingMode = EasingMode.EaseOut };

    public static void Enable(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);


        Control? hover = null;
        root.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (sender, e) =>
        {
            var next = FindInteractive(e.Source as AvaloniaObject, root);
            if (ReferenceEquals(next, hover)) return;
            if (hover is not null) AnimateTo(hover, GetState(hover), 1, 0, 210, SmoothOut);
            hover = next;
            OnMouseEnter(sender!, e);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.PointerExited += (_, _) => { if (hover is not null) AnimateTo(hover, GetState(hover), 1, 0, 210, SmoothOut); hover = null; };
        root.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerDown, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnPointerUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(Button.ClickEvent, OnButtonClick, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.GotFocusEvent, OnKeyboardFocus, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.LostFocusEvent, OnKeyboardFocusLost, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public static void Pulse(Control element)
    {
        if (!NativeSystemPreferences.ClientAreaAnimation || !element.IsEnabled) return;
        MotionState state = GetState(element);
        bool isButton = element is Button;
        double peakScale = isButton ? 1.0 : 1.012;
        double peakLift = isButton ? 0 : -0.75;
        Animate(state.Scale, ScaleTransform.ScaleXProperty, isButton ? 1.0 : 0.985, peakScale, RewardMilliseconds, RewardOut);
        Animate(state.Scale, ScaleTransform.ScaleYProperty, isButton ? 1.0 : 0.985, peakScale, RewardMilliseconds, RewardOut);
        Animate(state.Lift, TranslateTransform.YProperty, isButton ? 0 : 0.75, peakLift, RewardMilliseconds, SmoothOut, (_, _) =>
        {
            AnimateToRest(element, state);
        });
    }

    private static void OnMouseEnter(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        MotionState state = GetState(element);
        if (!state.Pressed) AnimateTo(element, state, HoverScale, HoverLift, HoverMilliseconds, RewardOut);
    }

    private static void OnMouseLeave(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        MotionState state = GetState(element);
        state.Pressed = false;
        AnimateTo(element, state, 1.0, 0, 210, SmoothOut);
    }

    private static void OnPointerDown(object? sender, PointerEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        MotionState state = GetState(element);
        state.Pressed = true;
        AnimateTo(element, state, PressedScale, 1.5, PressMilliseconds, SmoothOut);
    }

    private static void OnPointerUp(object? sender, PointerEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        MotionState state = GetState(element);
        if (!state.Pressed) return;
        state.Pressed = false;
        if (element is Button)
            AnimateToRest(element, state); // Button.Click supplies the reward pulse.
        else
            Pulse(element); // Clickable cards use pointer-up as their activation event.
    }

    private static void OnButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is Control element && IsInteractive(element))
            Pulse(element);
    }

    private static void OnKeyboardFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        AnimateTo(element, GetState(element), 1.025, -1.0, 140, RewardOut);
    }

    private static void OnKeyboardFocusLost(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (FindInteractive(e.Source as AvaloniaObject, sender as AvaloniaObject) is not { } element) return;
        AnimateToRest(element, GetState(element));
    }

    private static Control? FindInteractive(AvaloniaObject? current, AvaloniaObject? root)
    {
        while (current is not null)
        {
            if (current is Control element && IsInteractive(element)) return element;
            if (ReferenceEquals(current, root)) break;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool IsInteractive(Control element)
        => element.IsEnabled &&
           (element is Button || element is ListBoxItem || element is TabItem ||
            (element is Border && element.Cursor?.ToString() == "Hand"));

    private static MotionState GetState(Control element)
        => States.GetValue(element, static target =>
        {
            var existing = target.RenderTransform;
            var group = existing as TransformGroup;
            if (group is null)
            {
                group = new TransformGroup();
                if (existing is not null && existing != new MatrixTransform(Matrix.Identity))
                    group.Children.Add(existing as Transform ?? new MatrixTransform(existing.Value));
                target.RenderTransform = group;
            }

            var scale = new ScaleTransform(1, 1);
            var lift = new TranslateTransform();
            group.Children.Add(scale);
            group.Children.Add(lift);
            target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            return new MotionState { Scale = scale, Lift = lift };
        });

    private static void AnimateToRest(Control element, MotionState state)
        => AnimateTo(element, state, element.IsPointerOver ? HoverScale : 1.0, element.IsPointerOver ? HoverLift : 0, RewardMilliseconds, SmoothOut);

    private static void AnimateTo(Control element, MotionState state, double scale, double y, int ms, IEasingFunction easing)
    {
        // Buttons already animate their internal template surface. Scaling the
        // outer Button as well compounded to 8–11%, painted over neighbouring
        // controls and made dense command grids feel unstable. Keep the outer
        // response tactile but inside its allocated hit target.
        if (element is Button)
        {
            // Button templates already own their visual response. Applying a
            // second transform here compounded the scale, painted over nearby
            // commands and left controls looking as if they had jumped.
            scale = 1.0;
            y = 0;
        }
        else if (element is Border or TabItem or ListBoxItem)
        {
            // Card hit targets often sit in dense UniformGrids. Keep their
            // response inside the spacing gutter so the painted surface never
            // covers the adjacent command or creates a false click target.
            scale = scale switch
            {
                < 0.99 => 0.985,
                > 1.04 => 1.012,
                _ => Math.Min(scale, 1.008),
            };
            y = Math.Clamp(y, -0.75, 0.75);
        }

        if (!NativeSystemPreferences.ClientAreaAnimation)
        {
            state.Scale.ScaleX = scale;
            state.Scale.ScaleY = scale;
            state.Lift.Y = y;
            return;
        }

        Animate(state.Scale, ScaleTransform.ScaleXProperty, null, scale, ms, easing);
        Animate(state.Scale, ScaleTransform.ScaleYProperty, null, scale, ms, easing);
        Animate(state.Lift, TranslateTransform.YProperty, null, y, ms, easing);
    }

    private static void Animate(AvaloniaObject target, AvaloniaProperty property, double? from, double to,
        int milliseconds, IEasingFunction easing, EventHandler? completed = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
        if (completed is not null) animation.Completed += completed;
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
