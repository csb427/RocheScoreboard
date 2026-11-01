// Generated from Views/CricketBowlingFiguresGlyph.cs; edit translator/adapters.
// Source SHA-256: f49e767434ae6dae1b2febb17eadad1424c03d4ed4e4eae1a5448118747b01b4
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Border = Avalonia.Controls.Border;
using Canvas = Avalonia.Controls.Canvas;

namespace Roche_Scoreboard.Views;

/// <summary>A ball beside four bowling-stat columns; decorative selection, never live figures.</summary>
public sealed class CricketBowlingFiguresGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double), typeof(CricketBowlingFiguresGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private readonly List<Border> _outlines = new(), _ink = new();
    private readonly Border[] _headers = new Border[4], _figures = new Border[12];
    private readonly ScaleTransform[] _columnGrowth = new ScaleTransform[12];
    private readonly RotateTransform _seamTurn = new();

    public CricketBowlingFiguresGlyph()
    {
        Width = 44; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        Add(Outline("CricketBowlingFrame", 34, 28, 4), 10, 0);
        var ball = Outline("CricketBowlingBall", 7, 7, 3.5);
        var seam = Ink("CricketBowlingSeam", 1, 4);
        seam.RenderTransform = _seamTurn; seam.RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative);
        ball.Child = seam; Add(ball, 0, 3);
        // The frame, ball and cell outlines stay readable in either state.
        // Four statistic columns arrive in their own sequence, unlike the
        // batting card's roster-first row choreography.
        for (int column = 0; column < 4; column++)
        {
            double x = 14 + column * 6.5;
            _headers[column] = Ink("CricketBowlingHeader" + column, 4, 1.5);
            Add(_headers[column], x, 4);
            for (int row = 0; row < 3; row++)
            {
                int index = column * 3 + row;
                var cell = Outline("CricketBowlingCell" + index, 5, 4, .5);
                _figures[index] = Ink("CricketBowlingFigure" + index, 3, 2);
                _columnGrowth[index] = new ScaleTransform(1, .35);
                _figures[index].RenderTransform = _columnGrowth[index];
                _figures[index].RenderTransformOrigin = new RelativePoint(.5, 1,RelativeUnit.Relative);
                cell.Child = _figures[index]; Add(cell, x - .5, 9 + row * 5);
            }
        }
        SetSelectedInk(false); ApplyProgress();
    }

    private Border Outline(string name, double width, double height, double radius)
    {
        var border = new NativeBorder { Name = name, Width = width, Height = height,
            CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1),
            IsHitTestVisible = false, Focusable = false };
        _outlines.Add(border); return border;
    }
    private Border Ink(string name, double width, double height)
    {
        var border = new NativeBorder { Name = name, Width = width, Height = height,
            IsHitTestVisible = false, Focusable = false };
        _ink.Add(border); return border;
    }
    private void Add(Control part, double x, double y) { SetLeft(part, x); SetTop(part, y); Children.Add(part); }
    internal void SetSelectedInk(bool selected)
    {
        string resource = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        foreach (var part in _outlines) part.SetResourceReference(Border.BorderBrushProperty, resource);
        foreach (var part in _ink) part.SetResourceReference(Border.BackgroundProperty, resource);
    }
    private static double Phase(double progress, double start, double span) => Math.Clamp((progress - start) / span, 0, 1);
    private static void ProgressChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args) => ((CricketBowlingFiguresGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        double progress = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        // Only the seam inclines briefly; the ball, table and input stay fixed.
        _seamTurn.Angle = Math.Sin(progress * Math.PI) * 12;
        for (int column = 0; column < 4; column++)
        {
            _headers[column].Opacity = .4 + .6 * Phase(progress, column * .12, .35);
            for (int row = 0; row < 3; row++)
            {
                int index = column * 3 + row;
                double phase = Phase(progress, column * .12 + row * .06, .45);
                _figures[index].Opacity = .25 + .65 * phase;
                _columnGrowth[index].ScaleY = .35 + .65 * phase;
            }
        }
    }
}
