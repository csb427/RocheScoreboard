// Generated from Views/CricketScorewormGlyph.cs; edit translator/adapters.
// Source SHA-256: 0d76af619be2d1639d19f3489a1a9a764a482749b5f0731c4f6f74934e90f2a0
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using Canvas = Avalonia.Controls.Canvas;
using Path = Avalonia.Controls.Shapes.Path;

namespace Roche_Scoreboard.Views;

/// <summary>Two abstract progress trails; selection accents, never actual innings data.</summary>
public sealed class CricketScorewormGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double), typeof(CricketScorewormGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    private readonly List<Path> _strokes = new();
    private readonly RectangleGeometry[] _reveals = new RectangleGeometry[2];
    private readonly Ellipse[] _ends = new Ellipse[2];

    public CricketScorewormGlyph()
    {
        Width = 44; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        Children.Add(Stroke("CricketWormAxes", Geometry.Parse("M3,2 L3,25 L42,25"), .6));
        string[] trails = { "M5,22 C11,22 11,17 18,17 S25,10 30,10 S36,4 41,4",
            "M5,23 C10,23 14,20 20,20 S27,15 32,15 S37,10 41,10" };
        for (int series = 0; series < 2; series++)
        {
            var geometry = Geometry.Parse(trails[series]);
            Children.Add(Stroke("CricketWormGuide" + series, geometry, series == 0 ? .32 : .22));
            var accent = Stroke("CricketWormAccent" + series, geometry, series == 0 ? 1 : .7);
            _reveals[series] = new RectangleGeometry(new Rect(0, 0, 0, 28));
            accent.Clip = _reveals[series]; Children.Add(accent);
            _ends[series] = new Ellipse { Name = "CricketWormEnd" + series, Width = 3, Height = 3,
                IsHitTestVisible = false, Focusable = false };
            SetLeft(_ends[series], 39.5); SetTop(_ends[series], series == 0 ? 2.5 : 8.5); Children.Add(_ends[series]);
        }
        SetSelectedInk(false); ApplyProgress();
    }
    private Path Stroke(string name, Geometry geometry, double opacity)
    {
        var path = new Path { Name = name, Width = 44, Height = 28, Data = geometry, Fill = null,
            StrokeThickness = 1.5, Opacity = opacity, IsHitTestVisible = false, Focusable = false };
        _strokes.Add(path); return path;
    }
    internal void SetSelectedInk(bool selected)
    {
        string resource = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        foreach (var path in _strokes) path.SetResourceReference(Shape.StrokeProperty, resource);
        foreach (var end in _ends) end.SetResourceReference(Shape.FillProperty, resource);
    }
    private static double Phase(double p, double start, double span) => Math.Clamp((p - start) / span, 0, 1);
    private static void ProgressChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args) => ((CricketScorewormGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        double p = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        for (int series = 0; series < 2; series++)
        {
            // The axes and faint full trails remain stable. Only their
            // selection accents reveal from the common start, separately.
            _reveals[series].Rect = new Rect(0, 0, 44 * Phase(p, series * .15, series == 0 ? .8 : .85), 28);
            _ends[series].Opacity = Phase(p, .68 + series * .15, series == 0 ? .22 : .17);
        }
    }
}
