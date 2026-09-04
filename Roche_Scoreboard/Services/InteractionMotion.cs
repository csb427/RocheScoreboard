using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    private static readonly ConditionalWeakTable<FrameworkElement, MotionState> States = new();
    private static readonly CubicEase SmoothOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase RewardOut = new() { Amplitude = 0.42, EasingMode = EasingMode.EaseOut };

    public static void Enable(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        root.AddHandler(UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler(OnMouseEnter), true);
        root.AddHandler(UIElement.MouseLeaveEvent, new System.Windows.Input.MouseEventHandler(OnMouseLeave), true);
        root.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPointerDown), true);
        root.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnPointerUp), true);
        root.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick), true);
        root.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocus), true);
        root.AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusLost), true);
    }

    public static void Pulse(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation || !element.IsEnabled) return;
        MotionState state = GetState(element);
        bool isButton = element is System.Windows.Controls.Primitives.ButtonBase;
        double peakScale = isButton ? 1.0 : 1.012;
        double peakLift = isButton ? 0 : -0.75;
        Animate(state.Scale, ScaleTransform.ScaleXProperty, isButton ? 1.0 : 0.985, peakScale, RewardMilliseconds, RewardOut);
        Animate(state.Scale, ScaleTransform.ScaleYProperty, isButton ? 1.0 : 0.985, peakScale, RewardMilliseconds, RewardOut);
        Animate(state.Lift, TranslateTransform.YProperty, isButton ? 0 : 0.75, peakLift, RewardMilliseconds, SmoothOut, (_, _) =>
        {
            AnimateToRest(element, state);
        });
    }

    private static void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject, sender as DependencyObject) is not { } element) return;
        MotionState state = GetState(element);
        if (!state.Pressed) AnimateTo(element, state, HoverScale, HoverLift, HoverMilliseconds, RewardOut);
    }

    private static void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject, sender as DependencyObject) is not { } element) return;
        MotionState state = GetState(element);
        state.Pressed = false;
        AnimateTo(element, state, 1.0, 0, 210, SmoothOut);
    }

    private static void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject, sender as DependencyObject) is not { } element) return;
        MotionState state = GetState(element);
        state.Pressed = true;
        AnimateTo(element, state, PressedScale, 1.5, PressMilliseconds, SmoothOut);
    }

    private static void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (FindInteractive(e.OriginalSource as DependencyObject, sender as DependencyObject) is not { } element) return;
        MotionState state = GetState(element);
        if (!state.Pressed) return;
        state.Pressed = false;
        if (element is System.Windows.Controls.Primitives.ButtonBase)
            AnimateToRest(element, state); // Button.Click supplies the reward pulse.
        else
            Pulse(element); // Clickable cards use pointer-up as their activation event.
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && IsInteractive(element))
            Pulse(element);
    }

    private static void OnKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindInteractive(e.NewFocus as DependencyObject, sender as DependencyObject) is not { } element) return;
        AnimateTo(element, GetState(element), 1.025, -1.0, 140, RewardOut);
    }

    private static void OnKeyboardFocusLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindInteractive(e.OldFocus as DependencyObject, sender as DependencyObject) is not { } element) return;
        AnimateToRest(element, GetState(element));
    }

    private static FrameworkElement? FindInteractive(DependencyObject? current, DependencyObject? root)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && IsInteractive(element)) return element;
            if (ReferenceEquals(current, root)) break;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool IsInteractive(FrameworkElement element)
        => element.IsEnabled &&
           (element is System.Windows.Controls.Primitives.ButtonBase || element is ListBoxItem || element is TabItem ||
            (element is Border && element.Cursor == System.Windows.Input.Cursors.Hand));

    private static MotionState GetState(FrameworkElement element)
        => States.GetValue(element, static target =>
        {
            Transform existing = target.RenderTransform;
            var group = existing as TransformGroup;
            if (group is null)
            {
                group = new TransformGroup();
                if (existing is not null && existing != Transform.Identity)
                    group.Children.Add(existing);
                target.RenderTransform = group;
            }

            var scale = new ScaleTransform(1, 1);
            var lift = new TranslateTransform();
            group.Children.Add(scale);
            group.Children.Add(lift);
            target.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            return new MotionState { Scale = scale, Lift = lift };
        });

    private static void AnimateToRest(FrameworkElement element, MotionState state)
        => AnimateTo(element, state, element.IsMouseOver ? HoverScale : 1.0, element.IsMouseOver ? HoverLift : 0, RewardMilliseconds, SmoothOut);

    private static void AnimateTo(FrameworkElement element, MotionState state, double scale, double y, int ms, IEasingFunction easing)
    {
        // Buttons already animate their internal template surface. Scaling the
        // outer Button as well compounded to 8–11%, painted over neighbouring
        // controls and made dense command grids feel unstable. Keep the outer
        // response tactile but inside its allocated hit target.
        if (element is System.Windows.Controls.Primitives.ButtonBase)
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

        if (!SystemParameters.ClientAreaAnimation)
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

    private static void Animate(Animatable target, DependencyProperty property, double? from, double to,
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
