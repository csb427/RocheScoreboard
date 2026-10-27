using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Roche_Scoreboard.Services;

/// <summary>Broadcast timing shared by the AFL and cricket output surfaces.</summary>
internal static class BroadcastMotion
{
    internal static readonly TimeSpan OverlayDuration = TimeSpan.FromMilliseconds(560);
    internal static readonly TimeSpan OverlayHandoffGap = TimeSpan.FromMilliseconds(110);
    internal static readonly TimeSpan PresentationDuration = TimeSpan.FromMilliseconds(350);
    private sealed class MotionState { public int Generation; }
    private static readonly ConditionalWeakTable<Border, MotionState> States = new();

    internal static IEasingFunction CreateOverlayEase() => new CubicEase { EasingMode = EasingMode.EaseInOut };

    internal static void EnterOverlay(Border surface, double height)
    {
        var state = States.GetOrCreateValue(surface);
        state.Generation++;
        surface.BeginAnimation(FrameworkElement.HeightProperty, null);
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        var move = surface.RenderTransform as TranslateTransform ?? new TranslateTransform();
        surface.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, null);
        surface.Height = height;
        surface.Opacity = 1;
        move.Y = 0;
        surface.Visibility = Visibility.Visible;
        surface.ClipToBounds = true;
        surface.BeginAnimation(FrameworkElement.HeightProperty, Animation(0, height));
        surface.BeginAnimation(UIElement.OpacityProperty, Animation(0, 1));
        move.BeginAnimation(TranslateTransform.YProperty, Animation(-Math.Max(8, height * .28), 0));
    }

    internal static void ExitOverlay(Border surface, Action finished)
    {
        var state = States.GetOrCreateValue(surface);
        int generation = ++state.Generation;
        double height = surface.ActualHeight, opacity = surface.Opacity;
        surface.BeginAnimation(FrameworkElement.HeightProperty, null);
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.Height = 0;
        surface.Opacity = 0;
        var animation = Animation(height, 0);
        animation.Completed += (_, _) =>
        {
            if (generation != state.Generation) return;
            surface.Visibility = Visibility.Collapsed;
            finished();
        };
        surface.BeginAnimation(FrameworkElement.HeightProperty, animation);
        surface.BeginAnimation(UIElement.OpacityProperty, Animation(opacity, 0));
        if (surface.RenderTransform is TranslateTransform move)
        {
            double y = move.Y;
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.Y = 0;
            move.BeginAnimation(TranslateTransform.YProperty, Animation(y, -Math.Max(8, height * .28)));
        }
    }

    private static DoubleAnimation Animation(double from, double to) => new(from, to, OverlayDuration)
    {
        FillBehavior = FillBehavior.Stop,
        EasingFunction = CreateOverlayEase()
    };
}
