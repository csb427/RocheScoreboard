using System.Windows;
using System.Windows.Media.Animation;

namespace Roche_Scoreboard.Services;

/// <summary>Native operator interaction timings. Broadcast/score pacing has its own scale.</summary>
public static class OperatorMotion
{
    public static bool Enabled => AnimationOverride ?? (!OperatorAppearance.ReduceMotion && SystemParameters.ClientAreaAnimation);
    internal static bool? AnimationOverride { get; set; }
    // Public output pacing remains in BroadcastMotion. These preferences only
    // adjust operator feedback, and never postpone an action's acceptance.
    public static double TravelScale => OperatorAppearance.MotionStyle == OperatorMotionStyle.Calm ? .4 : 1;
    public static double SpringResponse => OperatorAppearance.MotionStyle == OperatorMotionStyle.Calm ? 1.5 : 1;
    private static TimeSpan Timing(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds * (OperatorAppearance.MotionStyle == OperatorMotionStyle.Calm ? .65 : 1));
    public static TimeSpan HoverIn => Timing(150);
    public static TimeSpan HoverOut => Timing(250);
    public static TimeSpan Press => Timing(80);
    public static TimeSpan SurfaceOpen => Timing(250);
    public static TimeSpan SurfaceClose => Timing(150);
    public static TimeSpan PanelOpen => Timing(400);
    public static TimeSpan PanelClose => Timing(350);
    public static TimeSpan Page => Timing(250);
    public static TimeSpan Stagger => Timing(40);
    public static TimeSpan Toggle => Timing(350);
    public static TimeSpan CheckDraw => Timing(350);
    public static TimeSpan MaterialArrival => Timing(500);
    public static TimeSpan ChoiceArrival => Timing(440);
    public static TimeSpan SymbolArrival => Timing(580);
    public static TimeSpan SymbolAccent => Timing(140);
    public static TimeSpan SymbolFill => Timing(320);
    public static IEasingFunction SmoothOut => new CubicEase { EasingMode = EasingMode.EaseOut };
}
