// Generated from Services/BroadcastMotion.cs; edit translator/adapters.
// Source SHA-256: 18a4bed104a0c4e3badf62700b16ca80caab32a6b29e91ed5d818493ea30111c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Runtime.CompilerServices;

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
        surface.BeginAnimation(Control.HeightProperty, null);
        surface.BeginAnimation(Control.OpacityProperty, null);
        var move = surface.RenderTransform as TranslateTransform ?? new TranslateTransform();
        surface.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, null);
        surface.Height = height;
        surface.Opacity = 1;
        move.Y = 0;
        surface.IsVisible = true;
        surface.ClipToBounds = true;
        surface.BeginAnimation(Control.HeightProperty, Animation(0, height));
        surface.BeginAnimation(Control.OpacityProperty, Animation(0, 1));
        move.BeginAnimation(TranslateTransform.YProperty, Animation(-Math.Max(8, height * .28), 0));
    }

    internal static void ExitOverlay(Border surface, Action finished)
    {
        var state = States.GetOrCreateValue(surface);
        int generation = ++state.Generation;
        double height = surface.NativeHeight(), opacity = surface.Opacity;
        surface.BeginAnimation(Control.HeightProperty, null);
        surface.BeginAnimation(Control.OpacityProperty, null);
        surface.Height = 0;
        surface.Opacity = 0;
        var animation = Animation(height, 0);
        animation.Completed += (_, _) =>
        {
            if (generation != state.Generation) return;
            surface.IsVisible = false;
            finished();
        };
        surface.BeginAnimation(Control.HeightProperty, animation);
        surface.BeginAnimation(Control.OpacityProperty, Animation(opacity, 0));
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
