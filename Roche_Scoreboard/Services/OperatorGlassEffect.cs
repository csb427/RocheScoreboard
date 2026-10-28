using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Roche_Scoreboard.Services;

/// <summary>A packaged PS2 background lens; no runtime compiler, SDK or native package is required.</summary>
internal sealed class OperatorGlassEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() =>
    {
        try
        {
            var shader = new PixelShader { UriSource = new Uri("/Roche Scoreboard;component/Resources/Shaders/OperatorGlass.ps", UriKind.Relative) };
            shader.Freeze(); return shader;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException)
        { FailureReason = error.Message; return null; }
    });
    internal static string? FailureReason { get; private set; }
    internal static bool IsAvailable => Shader.Value is not null;
    internal static OperatorGlassEffect? Create() => RenderCapability.IsPixelShaderVersionSupported(2, 0) && Shader.Value is not null ? new() : null;
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(OperatorGlassEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(OperatorGlassEffect), new UIPropertyMetadata(Color.FromArgb(135, 30, 35, 43), PixelShaderConstantCallback(0)));
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public static readonly DependencyProperty DimensionsProperty = DependencyProperty.Register(nameof(Dimensions), typeof(Point), typeof(OperatorGlassEffect), new UIPropertyMetadata(new Point(100, 40), PixelShaderConstantCallback(1)));
    public Point Dimensions { get => (Point)GetValue(DimensionsProperty); set => SetValue(DimensionsProperty, value); }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(OperatorGlassEffect), new UIPropertyMetadata(22d, PixelShaderConstantCallback(2)));
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point), typeof(OperatorGlassEffect), new UIPropertyMetadata(new Point(.28, .08), PixelShaderConstantCallback(3)));
    public Point Light { get => (Point)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(nameof(Activity), typeof(double), typeof(OperatorGlassEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(4)));
    public double Activity { get => (double)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }
    public static readonly DependencyProperty OpticsProperty = DependencyProperty.Register(nameof(Optics), typeof(Point), typeof(OperatorGlassEffect), new UIPropertyMetadata(new Point(8.4,.50), PixelShaderConstantCallback(5)));
    public Point Optics { get => (Point)GetValue(OpticsProperty); set => SetValue(OpticsProperty,value); }
    public static readonly DependencyProperty EdgeDepthProperty=DependencyProperty.Register(nameof(EdgeDepth),typeof(double),typeof(OperatorGlassEffect),new UIPropertyMetadata(.055d,PixelShaderConstantCallback(6)));
    public double EdgeDepth{get=>(double)GetValue(EdgeDepthProperty);set=>SetValue(EdgeDepthProperty,value);}
    private OperatorGlassEffect()
    {
        PixelShader = Shader.Value!;
        foreach (var property in new[] { InputProperty, TintProperty, DimensionsProperty, RadiusProperty, LightProperty, ActivityProperty, OpticsProperty, EdgeDepthProperty }) UpdateShaderValue(property);
    }
}
