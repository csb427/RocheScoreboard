// Generated from Services/OperatorGlassEffect.cs; native platform equivalent in macos/native-overrides.
// Source SHA-256: 8ef4e785816201c8603d8a8a0e2510af1a20488f8d00379f1ba2d6d3a1d335c0
using Avalonia;
using Avalonia.Media;
namespace Roche_Scoreboard.Services;
// WPF's DirectX pixel shader cannot run on Cocoa. The source material renderer
// explicitly supports this fallback, retaining gradients, lens edges and content.
internal sealed class OperatorGlassEffect : AvaloniaObject
{
    internal bool HasAnimatedProperties=>RocheScoreboard.Mac.Native.Motion.IsAnimating(this);
    internal static bool IsAvailable => false;
    internal static OperatorGlassEffect? Create()=>null;
    public IBrush? Input{get;set;}
    public Color Tint{get;set;}
    public Point Dimensions{get;set;}
    public double Radius{get;set;}
    public Point Light{get;set;}
    public static readonly StyledProperty<double> ActivityProperty=AvaloniaProperty.Register<OperatorGlassEffect,double>(nameof(Activity));
    public double Activity{get=>GetValue(ActivityProperty);set=>SetValue(ActivityProperty,value);}
    public Point Optics{get;set;}
    public double EdgeDepth{get;set;}
}
