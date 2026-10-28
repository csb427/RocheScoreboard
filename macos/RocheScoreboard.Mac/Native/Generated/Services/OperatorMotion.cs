// Generated from Services/OperatorMotion.cs; edit translator/adapters.
// Source SHA-256: 635357d16110c2dccff926b782d3378176f03a6ab780299331ad6775caa1a316
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>Native operator interaction timings. Broadcast/score pacing has its own scale.</summary>
public static class OperatorMotion
{
    public static bool Enabled => AnimationOverride ?? (!OperatorAppearance.ReduceMotion && NativeSystemPreferences.ClientAreaAnimation);
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
    public static TimeSpan MaterialArrival => TimeSpan.FromMilliseconds(500);
    public static TimeSpan ChoiceArrival => TimeSpan.FromMilliseconds(440);
    public static IEasingFunction SmoothOut => new CubicEase { EasingMode = EasingMode.EaseOut };
}
