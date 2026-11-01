using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Border = System.Windows.Controls.Border;
using Canvas = System.Windows.Controls.Canvas;

namespace Roche_Scoreboard.Views;

/// <summary>A batting card's header, roster and paired runs/balls columns, without match data.</summary>
public sealed class CricketBattingScorecardGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CricketBattingScorecardGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private readonly Border _frame = Outline("CricketBattingFrame", 36, 28, 4);
    private readonly Border _header = Ink("CricketBattingHeader", 27, 2);
    private readonly Border[] _roster = new Border[3], _names = new Border[3], _runs = new Border[3], _balls = new Border[3];
    private readonly ScaleTransform[] _nameGrowth = new ScaleTransform[3];

    public CricketBattingScorecardGlyph()
    {
        Width = 36; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        Children.Add(_frame); Add(_header, 4, 4);
        // Each row stays legible before selection. Its name underline grows
        // first; the paired statistic cells arrive later, in reading order.
        for (int row = 0; row < 3; row++)
        {
            double y = 10 + row * 5;
            _roster[row] = Ink("CricketBattingRoster" + row, 11 - row, 1);
            _roster[row].Opacity = .45; Add(_roster[row], 4, y);
            var nameTrack = new Canvas { Width = 13, Height = 2, ClipToBounds = true, IsHitTestVisible = false, Focusable = false };
            _names[row] = Ink("CricketBattingNameAccent" + row, 13, 2);
            _nameGrowth[row] = new ScaleTransform(0, 1);
            _names[row].RenderTransform = _nameGrowth[row];
            _names[row].RenderTransformOrigin = new System.Windows.Point(0, 0);
            nameTrack.Children.Add(_names[row]); Add(nameTrack, 4, y + 1);
            var runsOutline = Outline("CricketBattingRunsCell" + row, 5, 4, .5);
            var ballsOutline = Outline("CricketBattingBallsCell" + row, 5, 4, .5);
            _runs[row] = Ink("CricketBattingRunsAccent" + row, 3, 2);
            _balls[row] = Ink("CricketBattingBallsAccent" + row, 3, 2);
            runsOutline.Child = _runs[row]; ballsOutline.Child = _balls[row];
            Add(runsOutline, 21, y); Add(ballsOutline, 28, y);
        }
        SetSelectedInk(false); ApplyProgress();
    }

    private static Border Outline(string name, double width, double height, double radius) => new()
    {
        Name = name, Width = width, Height = height, CornerRadius = new CornerRadius(radius),
        BorderThickness = new Thickness(1), IsHitTestVisible = false, Focusable = false
    };
    private static Border Ink(string name, double width, double height) => new()
    { Name = name, Width = width, Height = height, IsHitTestVisible = false, Focusable = false };
    private void Add(UIElement part, double x, double y) { SetLeft(part, x); SetTop(part, y); Children.Add(part); }

    internal void SetSelectedInk(bool selected)
    {
        string ink = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        foreach (var child in Children)
        {
            if (child is Border border)
            {
                border.SetResourceReference(Border.BorderBrushProperty, ink);
                // The enclosing frame and statistic cell outlines stay hollow.
                if (ReferenceEquals(border, _header) || _roster.Contains(border))
                    border.SetResourceReference(Border.BackgroundProperty, ink);
            }
        }
        foreach (var part in _names.Concat(_runs).Concat(_balls)) part.SetResourceReference(Border.BackgroundProperty, ink);
    }

    private static double Phase(double progress, double start, double span) => Math.Clamp((progress - start) / span, 0, 1);
    private static void ProgressChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((CricketBattingScorecardGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        for (int row = 0; row < 3; row++)
        {
            _nameGrowth[row].ScaleX = Phase(Progress, row * .13, .38);
            _runs[row].Opacity = .65 * Phase(Progress, .35 + row * .10, .30);
            _balls[row].Opacity = .65 * Phase(Progress, .50 + row * .10, .30);
        }
    }
}
