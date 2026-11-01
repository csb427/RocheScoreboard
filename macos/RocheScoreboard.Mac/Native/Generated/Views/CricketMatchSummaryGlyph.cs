// Generated from Views/CricketMatchSummaryGlyph.cs; edit translator/adapters.
// Source SHA-256: cf0715a0e92e1b825980c71345ce8dd6a5a3f1ad56bea80b37f80fb5617ade8f
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

/// <summary>Two team panels with independent arrival; an abstract diagram, not match figures.</summary>
public sealed class CricketMatchSummaryGlyph : Canvas
{
    public static readonly AttachedProperty<double> ProgressProperty = (AttachedProperty<double>)NativeProperty.Register(nameof(Progress),typeof(double), typeof(CricketMatchSummaryGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    private readonly List<Border> _outlines = new(), _ink = new();
    private readonly ScaleTransform[] _panels = new ScaleTransform[2], _names = new ScaleTransform[2];
    private readonly Border[] _badges = new Border[2], _scoreMarks = new Border[2];

    public CricketMatchSummaryGlyph()
    {
        Width = 44; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        for (int row = 0; row < 2; row++)
        {
            var panel = new Canvas { Name = "CricketSummaryTeamPanel" + row, Width = 40, Height = 11,
                IsHitTestVisible = false, Focusable = false };
            _panels[row] = new ScaleTransform(.88, 1);
            panel.RenderTransform = _panels[row]; panel.RenderTransformOrigin = new RelativePoint(row, .5,RelativeUnit.Relative);
            SetLeft(panel, 2); SetTop(panel, row == 0 ? 1 : 16); Children.Add(panel);
            panel.Children.Add(Outline("CricketSummaryTeamFrame" + row, 40, 11, 4));
            var badge = Outline("CricketSummaryBadgeOutline" + row, 5, 5, 2.5);
            _badges[row] = Ink("CricketSummaryBadgeInk" + row, 3, 3); badge.Child = _badges[row]; Add(panel, badge, 4, 3);
            var name = Ink("CricketSummaryName" + row, 12, 1.5);
            _names[row] = new ScaleTransform(.45, 1); name.RenderTransform = _names[row];
            name.RenderTransformOrigin = new RelativePoint(0, .5,RelativeUnit.Relative); Add(panel, name, 12, 4.75);
            var well = Outline("CricketSummaryScoreWell" + row, 7, 5, 1.5);
            _scoreMarks[row] = Ink("CricketSummaryScoreMark" + row, 3, 1.5); well.Child = _scoreMarks[row]; Add(panel, well, 29, 3);
        }
        SetSelectedInk(false); ApplyProgress();
    }
    private Border Outline(string name, double width, double height, double radius)
    {
        var border = new NativeBorder { Name = name, Width = width, Height = height, CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1), IsHitTestVisible = false, Focusable = false };
        _outlines.Add(border); return border;
    }
    private Border Ink(string name, double width, double height)
    {
        var border = new NativeBorder { Name = name, Width = width, Height = height, IsHitTestVisible = false, Focusable = false };
        _ink.Add(border); return border;
    }
    private static void Add(Canvas owner, Control part, double x, double y) { SetLeft(part, x); SetTop(part, y); owner.Children.Add(part); }
    internal void SetSelectedInk(bool selected)
    {
        string resource = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        foreach (var part in _outlines) part.SetResourceReference(Border.BorderBrushProperty, resource);
        foreach (var part in _ink) part.SetResourceReference(Border.BackgroundProperty, resource);
    }
    private static double Phase(double p, double start, double span) => Math.Clamp((p - start) / span, 0, 1);
    private static void ProgressChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args) => ((CricketMatchSummaryGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        double p = double.IsFinite(Progress) ? Math.Clamp(Progress, 0, 1) : 0;
        for (int row = 0; row < 2; row++)
        {
            // Top panel grows from its leading badge, bottom from its score
            // well; their content then resolves in the same reading order.
            _panels[row].ScaleX = .88 + .12 * Phase(p, row * .18, .55);
            _badges[row].Opacity = .45 + .55 * Phase(p, row * .18, .35);
            _names[row].ScaleX = .45 + .55 * Phase(p, .12 + row * .18, .4);
            _scoreMarks[row].Opacity = .25 + .65 * Phase(p, .4 + row * .15, .4);
        }
    }
}
