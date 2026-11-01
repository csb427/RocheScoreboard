// Generated from Views/CricketManhattanGlyph.cs; edit translator/adapters.
// Source SHA-256: 53b22103537f2cb54dc058c865132c99f41fc877df02b9a80352749f8b4232e1
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Canvas = Avalonia.Controls.Canvas;

namespace Roche_Scoreboard.Views;

/// <summary>Abstract paired bars for the presentation selector, never live over figures.</summary>
public sealed class CricketManhattanGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double), typeof(CricketManhattanGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    private readonly Border[] _bars = new Border[6];
    private readonly ScaleTransform[] _barScales = new ScaleTransform[6];
    private readonly Border[] _wicketMarks = new Border[2];
    private readonly Border _baseline;

    public CricketManhattanGlyph()
    {
        Width = 44; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        _baseline = Ink("CricketManhattanBaseline", 42, 1, .6);
        SetLeft(_baseline, 1); SetTop(_baseline, 25); Children.Add(_baseline);
        double[] heights = { 12, 17, 20, 13, 16, 22 };
        for (int i = 0; i < 6; i++)
        {
            _bars[i] = Ink("CricketManhattanBar" + i, 4, heights[i], i % 2 == 0 ? 1 : .55);
            _bars[i].CornerRadius = new CornerRadius(1, 1, 0, 0);
            _bars[i].RenderTransformOrigin = new RelativePoint(.5, 1,RelativeUnit.Relative);
            _barScales[i] = new ScaleTransform(1, .45);
            _bars[i].RenderTransform = _barScales[i];
            SetLeft(_bars[i], 4 + i * 6); SetTop(_bars[i], 24 - heights[i]); Children.Add(_bars[i]);
        }
        for (int i = 0; i < 2; i++)
        {
            _wicketMarks[i] = Ink("CricketManhattanMarker" + i, 2, 2, 0);
            _wicketMarks[i].CornerRadius = new CornerRadius(1);
            SetLeft(_wicketMarks[i], 17 + i * 18); SetTop(_wicketMarks[i], i == 0 ? 1 : 0);
            Children.Add(_wicketMarks[i]);
        }
        SetSelectedInk(false); ApplyProgress();
    }
    private static Border Ink(string name, double width, double height, double opacity)
        => new() { Name = name, Width = width, Height = height, Opacity = opacity, IsHitTestVisible = false, Focusable = false };
    internal void SetSelectedInk(bool selected)
    {
        string resource = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        _baseline.SetResourceReference(Border.BackgroundProperty, resource);
        foreach (var bar in _bars) bar.SetResourceReference(Border.BackgroundProperty, resource);
        foreach (var mark in _wicketMarks) mark.SetResourceReference(Border.BackgroundProperty, resource);
    }
    private static double Phase(double p, double start, double span) => Math.Clamp((p - start) / span, 0, 1);
    private static void ProgressChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args) => ((CricketManhattanGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        double p = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        for (int i = 0; i < _bars.Length; i++)
        {
            // Each bar keeps its foot on the baseline; selection fills in the
            // paired-over motif rather than moving the entire card or label.
            double phase = Phase(p, i * .065, .65);
            _barScales[i].ScaleY = .45 + .55 * phase;
        }
        for (int i = 0; i < _wicketMarks.Length; i++) _wicketMarks[i].Opacity = Phase(p, .72 + i * .1, .18);
    }
}
