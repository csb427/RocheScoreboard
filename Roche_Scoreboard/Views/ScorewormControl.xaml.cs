using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Roche_Scoreboard.Views
{
    public partial class ScorewormControl : System.Windows.Controls.UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;

        public ScorewormControl()
        {
            InitializeComponent();

            WormTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += (_, __) => WormTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer.Start();
        }

        public void Populate(MatchManager match, int endedQuarter,
            string homeColorHex, string awayColorHex,
            string homeSecondaryHex, string awaySecondaryHex,
            string? barTitleOverride = null)
        {
            var homeColor = SafeColor(homeColorHex, "#4488FF");
            var awayColor = SafeColor(awayColorHex, "#FF6644");

            // Legend
            HomeLegendRect.Fill = new SolidColorBrush(homeColor);
            AwayLegendRect.Fill = new SolidColorBrush(awayColor);
            HomeLegendText.Text = match.HomeAbbr.ToUpper();
            AwayLegendText.Text = match.AwayAbbr.ToUpper();

            // Bottom bar
            WormBarHomeAbbr.Text = match.HomeAbbr.ToUpper();
            WormBarAwayAbbr.Text = match.AwayAbbr.ToUpper();
            WormBarHomeAbbrBrush.Color = Lighten(homeColor, 0.3);
            WormBarAwayAbbrBrush.Color = Lighten(awayColor, 0.3);
            WormBarHomeScore.Text = $"{match.HomeGoals}.{match.HomeBehinds}.{match.HomeTotal}";
            WormBarAwayScore.Text = $"{match.AwayGoals}.{match.AwayBehinds}.{match.AwayTotal}";
            if (!string.IsNullOrEmpty(barTitleOverride))
            {
                WormBarTitle.Text = barTitleOverride;
            }
            else
            {
                WormBarTitle.Text = endedQuarter switch
                {
                    1 => "QT",
                    2 => "HT",
                    3 => "3QT",
                    _ => "FT"
                };
            }

            // Build the graph
            DrawGraph(match, endedQuarter, homeColor, awayColor);
        }

        private void DrawGraph(MatchManager match, int endedQuarter, Color homeColor, Color awayColor)
        {
            GraphCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();

            var events = match.Events;
            if (events.Count == 0)
            {
                DrawEmptyMessage();
                return;
            }

            // Force layout so ActualWidth/Height are available
            GraphCanvas.UpdateLayout();
            double canvasW = GraphCanvas.ActualWidth > 0 ? GraphCanvas.ActualWidth : 620;
            double canvasH = GraphCanvas.ActualHeight > 0 ? GraphCanvas.ActualHeight : 260;

            double padTop = 20;
            double padBottom = 20;
            double graphH = canvasH - padTop - padBottom;

            // Build margin data points: start at 0, then each event's margin
            var margins = new List<int> { 0 };
            foreach (var ev in events)
                margins.Add(ev.Margin);

            int maxAbsMargin = margins.Max(m => Math.Abs(m));
            if (maxAbsMargin == 0) maxAbsMargin = 6;
            // Round up to nice number
            maxAbsMargin = NiceMax(maxAbsMargin);

            double yScale = (graphH / 2.0) / maxAbsMargin;
            double yCenter = padTop + graphH / 2.0;

            int totalPoints = margins.Count;
            double xStep = canvasW / Math.Max(1, totalPoints - 1);

            // ---- Draw gridlines ----
            DrawGridlines(canvasW, canvasH, padTop, padBottom, yCenter, yScale, maxAbsMargin, homeColor, awayColor);

            // ---- Draw quarter dividers ----
            DrawQuarterDividers(events, canvasH, xStep);

            // ---- Draw zero line ----
            var zeroLine = new Line
            {
                X1 = 0, Y1 = yCenter, X2 = canvasW, Y2 = yCenter,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                StrokeThickness = 1.5
            };
            GraphCanvas.Children.Add(zeroLine);

            // ---- Build point list ----
            var points = new List<Point>();
            for (int i = 0; i < totalPoints; i++)
            {
                double x = i * xStep;
                double y = yCenter - margins[i] * yScale;
                points.Add(new Point(x, y));
            }

            // ---- Draw filled areas (start invisible, fade in) ----
            var fills = DrawFilledAreas(points, yCenter, canvasW, homeColor, awayColor);

            // ---- Calculate cumulative path lengths ----
            var cumLengths = new List<double> { 0 };
            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                cumLengths.Add(cumLengths[i - 1] + Math.Sqrt(dx * dx + dy * dy));
            }
            double totalLength = cumLengths[^1];
            if (totalLength < 1) totalLength = 1;

            // ---- Draw line with dash-offset draw animation ----
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashArray = new DoubleCollection { totalLength, totalLength },
                StrokeDashOffset = totalLength
            };
            foreach (var p in points)
                polyline.Points.Add(p);
            GraphCanvas.Children.Add(polyline);

            const double drawSeconds = 1.6;
            var drawAnim = new DoubleAnimation(totalLength, 0, TimeSpan.FromSeconds(drawSeconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            polyline.BeginAnimation(Shape.StrokeDashOffsetProperty, drawAnim);

            // ---- Fade in fills as line progresses ----
            foreach (var fill in fills)
            {
                fill.Opacity = 0;
                var fillFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.6))
                {
                    BeginTime = TimeSpan.FromSeconds(drawSeconds * 0.3)
                };
                fill.BeginAnimation(OpacityProperty, fillFade);
            }

            // ---- Draw dots (fade in as line reaches each point) ----
            for (int i = 0; i < totalPoints; i++)
            {
                bool isHomeAbove = margins[i] > 0;
                bool isZero = margins[i] == 0;
                var dotColor = isZero ? Colors.White : (isHomeAbove ? homeColor : awayColor);

                var dot = new Ellipse
                {
                    Width = i == 0 ? 6 : 7,
                    Height = i == 0 ? 6 : 7,
                    Fill = new SolidColorBrush(dotColor),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1.2,
                    Opacity = 0
                };

                Canvas.SetLeft(dot, points[i].X - dot.Width / 2);
                Canvas.SetTop(dot, points[i].Y - dot.Height / 2);
                GraphCanvas.Children.Add(dot);

                // Delay each dot's appearance to match the line reaching it
                double fraction = cumLengths[i] / totalLength;
                double delay = fraction * drawSeconds;
                var dotFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2))
                {
                    BeginTime = TimeSpan.FromSeconds(delay)
                };
                dot.BeginAnimation(OpacityProperty, dotFade);
            }

            // ---- Y-axis labels ----
            DrawYAxisLabels(yCenter, yScale, maxAbsMargin, canvasH);
        }

        private void DrawGridlines(double canvasW, double canvasH, double padTop, double padBottom,
            double yCenter, double yScale, int maxAbsMargin, Color homeColor, Color awayColor)
        {
            int step = NiceStep(maxAbsMargin);
            var gridBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));

            for (int val = step; val <= maxAbsMargin; val += step)
            {
                // Home side (positive = above center)
                double yUp = yCenter - val * yScale;
                if (yUp >= padTop)
                {
                    var line = new Line { X1 = 0, Y1 = yUp, X2 = canvasW, Y2 = yUp, Stroke = gridBrush, StrokeThickness = 0.8 };
                    GraphCanvas.Children.Add(line);
                }

                // Away side (negative = below center)
                double yDown = yCenter + val * yScale;
                if (yDown <= canvasH - padBottom)
                {
                    var line = new Line { X1 = 0, Y1 = yDown, X2 = canvasW, Y2 = yDown, Stroke = gridBrush, StrokeThickness = 0.8 };
                    GraphCanvas.Children.Add(line);
                }
            }
        }

        private void DrawQuarterDividers(IReadOnlyList<ScoreEvent> events, double canvasH, double xStep)
        {
            // Find the last event index of each quarter
            var quarterBreaks = new HashSet<int>();
            for (int i = 0; i < events.Count - 1; i++)
            {
                if (events[i].Quarter != events[i + 1].Quarter)
                    quarterBreaks.Add(i + 1); // +1 because margins list is offset by 1 (starts with 0)
            }

            var divBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            foreach (int idx in quarterBreaks)
            {
                double x = idx * xStep;
                var line = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = canvasH, Stroke = divBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
                GraphCanvas.Children.Add(line);

                // Quarter label (AFL: QT, HT, 3QT)
                int q = events[idx - 1].Quarter;
                string qLabel = q switch { 1 => "QT", 2 => "HT", 3 => "3QT", _ => $"Q{q}" };
                var label = new TextBlock
                {
                    Text = qLabel,
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(label, x + 3);
                Canvas.SetTop(label, 2);
                GraphCanvas.Children.Add(label);
            }
        }

        private List<Polygon> DrawFilledAreas(List<Point> points, double yCenter, double canvasW, Color homeColor, Color awayColor)
        {
            var result = new List<Polygon>();

            // Split into segments above and below zero, fill with translucent team colour
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                // Determine if this segment is above or below the center
                double avgY = (p1.Y + p2.Y) / 2;
                bool isAbove = avgY < yCenter;
                Color fillColor = isAbove ? homeColor : awayColor;

                var poly = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(50, fillColor.R, fillColor.G, fillColor.B)),
                    Stroke = null
                };

                poly.Points.Add(p1);
                poly.Points.Add(p2);
                poly.Points.Add(new Point(p2.X, yCenter));
                poly.Points.Add(new Point(p1.X, yCenter));

                GraphCanvas.Children.Add(poly);
                result.Add(poly);
            }

            return result;
        }

        private void DrawYAxisLabels(double yCenter, double yScale, int maxAbsMargin, double canvasH)
        {
            double axisCanvasH = YAxisCanvas.ActualHeight > 0 ? YAxisCanvas.ActualHeight : canvasH;
            // The Y-axis canvas is the same height as the graph canvas
            // We need to place labels to match the graph's coordinate system

            int step = NiceStep(maxAbsMargin);
            var labelBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));

            // Zero label
            var zeroLabel = new TextBlock
            {
                Text = "0",
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Width = 34,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(zeroLabel, 0);
            Canvas.SetTop(zeroLabel, yCenter - 8);
            YAxisCanvas.Children.Add(zeroLabel);

            for (int val = step; val <= maxAbsMargin; val += step)
            {
                // Positive (home leads)
                double yUp = yCenter - val * yScale;
                var upLabel = new TextBlock
                {
                    Text = $"+{val}",
                    Foreground = labelBrush,
                    FontSize = 10,
                    Width = 34,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(upLabel, 0);
                Canvas.SetTop(upLabel, yUp - 7);
                YAxisCanvas.Children.Add(upLabel);

                // Negative (away leads)
                double yDown = yCenter + val * yScale;
                var downLabel = new TextBlock
                {
                    Text = $"-{val}",
                    Foreground = labelBrush,
                    FontSize = 10,
                    Width = 34,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(downLabel, 0);
                Canvas.SetTop(downLabel, yDown - 7);
                YAxisCanvas.Children.Add(downLabel);
            }
        }

        private void DrawEmptyMessage()
        {
            var msg = new TextBlock
            {
                Text = "No scores yet",
                Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            GraphCanvas.Children.Add(msg);
            Canvas.SetLeft(msg, 250);
            Canvas.SetTop(msg, 110);
        }

        private static int NiceMax(int val)
        {
            if (val <= 6) return 6;
            if (val <= 12) return 12;
            if (val <= 18) return 18;
            if (val <= 24) return 24;
            if (val <= 36) return 36;
            if (val <= 48) return 48;
            if (val <= 60) return 60;
            return ((val / 12) + 1) * 12;
        }

        private static int NiceStep(int maxVal)
        {
            if (maxVal <= 6) return 6;
            if (maxVal <= 12) return 6;
            if (maxVal <= 24) return 12;
            if (maxVal <= 48) return 12;
            return 24;
        }

        private static Color SafeColor(string? hex, string fallbackHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) hex = fallbackHex;
                if (!hex.StartsWith("#")) hex = "#" + hex.Trim();
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex);
            }
        }

        private static Color Lighten(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                (byte)Math.Min(255, c.B + (255 - c.B) * amount));
        }
    }
}
