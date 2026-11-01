using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Border = System.Windows.Controls.Border;
using Canvas = System.Windows.Controls.Canvas;

namespace Roche_Scoreboard.Views;

/// <summary>A schematic score rail, two batter bands and over detail, without match values.</summary>
public sealed class CricketLiveScorebugGlyph : Canvas
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(CricketLiveScorebugGlyph), new PropertyMetadata(0d, ProgressChanged));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    internal double ScorePhase => Math.Clamp(Progress / .45, 0, 1);
    internal double BatterPhase => Math.Clamp((Progress - .15) / .55, 0, 1);
    internal double DetailPhase => Math.Clamp((Progress - .45) / .55, 0, 1);
    private readonly Border _frame = new() { Name = "CricketLiveFrame", Width = 36, Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Focusable = false };
    private readonly Border _score = Part("CricketLiveScoreRail", 7, 20);
    private readonly Border _topBatter = Part("CricketLiveTopBatter", 20, 5);
    private readonly Border _bottomBatter = Part("CricketLiveBottomBatter", 20, 5);
    private readonly Border _detail = Part("CricketLiveDetailRail", 20, 4);
    private readonly Border _scoreFill = Fill("CricketLiveScoreAccent", 5, 18);
    private readonly Border _topFill = Fill("CricketLiveTopBatterAccent", 18, 3);
    private readonly Border _bottomFill = Fill("CricketLiveBottomBatterAccent", 18, 3);
    private readonly Border _detailFill = Fill("CricketLiveDetailAccent", 18, 2);
    private readonly TranslateTransform _scoreTravel = new(), _topTravel = new(), _bottomTravel = new(), _detailTravel = new();

    public CricketLiveScorebugGlyph()
    {
        Width = 36; Height = 28; ClipToBounds = true; IsHitTestVisible = false; Focusable = false;
        Children.Add(_frame);
        AddPart(_score, _scoreFill, _scoreTravel, 3, 4);
        AddPart(_topBatter, _topFill, _topTravel, 13, 4);
        AddPart(_bottomBatter, _bottomFill, _bottomTravel, 13, 12);
        AddPart(_detail, _detailFill, _detailTravel, 13, 20);
        SetSelectedInk(false); ApplyProgress();
    }

    private static Border Part(string name, double width, double height) => new() { Name = name, Width = width, Height = height, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1), ClipToBounds = true, IsHitTestVisible = false, Focusable = false };
    private static Border Fill(string name, double width, double height) => new() { Name = name, Width = width, Height = height, IsHitTestVisible = false, Focusable = false };
    private void AddPart(Border outline, Border fill, TranslateTransform travel, double x, double y)
    {
        fill.RenderTransform = travel; outline.Child = fill; SetLeft(outline, x); SetTop(outline, y); Children.Add(outline);
    }
    internal void SetSelectedInk(bool selected)
    {
        string ink = selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush";
        foreach (var outline in new[] { _frame, _score, _topBatter, _bottomBatter, _detail }) outline.SetResourceReference(Border.BorderBrushProperty, ink);
        foreach (var fill in new[] { _scoreFill, _topFill, _bottomFill, _detailFill }) fill.SetResourceReference(Border.BackgroundProperty, ink);
    }
    private static void ProgressChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((CricketLiveScorebugGlyph)target).ApplyProgress();
    private void ApplyProgress()
    {
        _scoreFill.Opacity = .55 * ScorePhase;
        _topFill.Opacity = _bottomFill.Opacity = .55 * BatterPhase;
        _detailFill.Opacity = .8 * DetailPhase;
        _scoreTravel.Y = (1 - ScorePhase) * OperatorMotion.TravelScale;
        _topTravel.X = _bottomTravel.X = (1 - BatterPhase) * OperatorMotion.TravelScale;
        _detailTravel.Y = (1 - DetailPhase) * OperatorMotion.TravelScale;
    }
}
