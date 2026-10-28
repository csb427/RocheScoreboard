using System.Windows;
using System.Windows.Media.Animation;

namespace Roche_Scoreboard.Services;

/// <summary>Native operator interaction timings. Broadcast/score pacing has its own scale.</summary>
public static class OperatorMotion
{
    public static bool Enabled => AnimationOverride ?? (!OperatorAppearance.ReduceMotion && SystemParameters.ClientAreaAnimation);
    internal static bool? AnimationOverride { get; set; }
    public static TimeSpan HoverIn => TimeSpan.FromMilliseconds(150);
    public static TimeSpan HoverOut => TimeSpan.FromMilliseconds(250);
    public static TimeSpan Press => TimeSpan.FromMilliseconds(80);
    public static TimeSpan SurfaceOpen => TimeSpan.FromMilliseconds(250);
    public static TimeSpan SurfaceClose => TimeSpan.FromMilliseconds(150);
    public static TimeSpan PanelOpen => TimeSpan.FromMilliseconds(400);
    public static TimeSpan PanelClose => TimeSpan.FromMilliseconds(350);
    public static TimeSpan Page => TimeSpan.FromMilliseconds(250);
    public static TimeSpan Stagger => TimeSpan.FromMilliseconds(40);
    public static TimeSpan Toggle => TimeSpan.FromMilliseconds(350);
    public static TimeSpan CheckDraw => TimeSpan.FromMilliseconds(350);
    public static IEasingFunction SmoothOut => new CubicEase { EasingMode = EasingMode.EaseOut };
}
