using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Media.Imaging;

namespace Roche_Scoreboard.Views
{
    public partial class ScorewormControl : UserControl
    {
        private readonly DispatcherTimer _wallClockTimer;
        private bool _isDisposed;

        // Cached params from the most recent Populate so we can redraw the
        // graph when the host control is resized (canvas dimensions change).
        private MatchManager? _lastMatch;
        private int _lastEndedQuarter;
        private Color _lastHomeColor;
        private Color _lastAwayColor;

        public ScorewormControl()
        {
            InitializeComponent();

            WormTimeText.Text = DateTime.Now.ToString("h:mm tt");
            _wallClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _wallClockTimer.Tick += OnWallClockTick;
            _wallClockTimer.Start();

            // Redraw the graph whenever the canvas resizes so the worm
            // adapts to new window dimensions instead of staying on stale
            // pixel coordinates from the first Populate.
            GraphCanvas.SizeChanged += OnGraphCanvasSizeChanged;

            SizeChanged += (_, _) => ApplyResponsiveScale();

            Unloaded += (_, _) => Cleanup();
        }

        /// <summary>
        /// Chrome rows are star-proportional in XAML; this method is
        /// kept for compatibility with size-changed wiring but no longer
        /// overwrites row heights.
        /// </summary>
        private void ApplyResponsiveScale()
        {
        }

        private void OnGraphCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isDisposed || _lastMatch is null) return;
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
            DrawGraph(_lastMatch, _lastEndedQuarter, _lastHomeColor, _lastAwayColor);
        }

        private void OnWallClockTick(object? sender, EventArgs e)
        {
            WormTimeText.Text = DateTime.Now.ToString("h:mm tt");
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _wallClockTimer.Stop();
        }

        public void Populate(MatchManager match, int endedQuarter,
            string homeColorHex, string awayColorHex,
            string homeSecondaryHex, string awaySecondaryHex,
            string? homeLogoPath, string? awayLogoPath,
            string? barTitleOverride = null)
        {
            ArgumentNullException.ThrowIfNull(match);

            var homeColor = SafeColor(homeColorHex, "#4488FF");
            var awayColor = SafeColor(awayColorHex, "#FF6644");
            var homeSecondary = SafeColor(homeSecondaryHex, "#FFFFFF");
            var awaySecondary = SafeColor(awaySecondaryHex, "#FFFFFF");

            // Background gradient with team colour tints at edges
            WormBgGradient.GradientStops.Clear();
            WormBgGradient.GradientStops.Add(new System.Windows.Media.GradientStop(Darken(homeColor, 0.85), 0.0));
            WormBgGradient.GradientStops.Add(new System.Windows.Media.GradientStop(Color.FromRgb(0x0D, 0x11, 0x17), 0.5));
            WormBgGradient.GradientStops.Add(new System.Windows.Media.GradientStop(Darken(awayColor, 0.85), 1.0));

            // Title accent: team colours split left/right
            WormTitleAccent.GradientStops.Clear();
            WormTitleAccent.GradientStops.Add(new System.Windows.Media.GradientStop(homeColor, 0.0));
            WormTitleAccent.GradientStops.Add(new System.Windows.Media.GradientStop(Color.FromArgb(0x60, homeColor.R, homeColor.G, homeColor.B), 0.35));
            WormTitleAccent.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 0.5));
            WormTitleAccent.GradientStops.Add(new System.Windows.Media.GradientStop(Color.FromArgb(0x60, awayColor.R, awayColor.G, awayColor.B), 0.65));
            WormTitleAccent.GradientStops.Add(new System.Windows.Media.GradientStop(awayColor, 1.0));

            // Team headers: subtle dark bg, bold team-colour border, vibrant text.
            // Border falls back to secondary if primary is too dark to register
            // against the dark header background.
            var wormHomeHeaderBg = Darken(homeColor, 0.7);
            var wormAwayHeaderBg = Darken(awayColor, 0.7);
            WormHomeTeamHeader.Background = new SolidColorBrush(wormHomeHeaderBg);
            WormHomeTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(wormHomeHeaderBg, homeColor, homeSecondary);
            WormAwayTeamHeader.Background = new SolidColorBrush(wormAwayHeaderBg);
            WormAwayTeamHeader.BorderBrush = Services.ContrastHelper.GetVisibleBorderBrush(wormAwayHeaderBg, awayColor, awaySecondary);
            WormHomeGlow.Color = Color.FromArgb(0xFF, homeColor.R, homeColor.G, homeColor.B);
            WormAwayGlow.Color = Color.FromArgb(0xFF, awayColor.R, awayColor.G, awayColor.B);
            WormHomeTeamText.Text = match.HomeName.ToUpperInvariant();
            WormAwayTeamText.Text = match.AwayName.ToUpperInvariant();
            WormHomeTeamText.Foreground = new SolidColorBrush(Lighten(homeColor, 0.4));
            WormAwayTeamText.Foreground = new SolidColorBrush(Lighten(awayColor, 0.4));

            // Hide all logo placeholders if at least one team has no logo
            bool hideLogos = Services.ImageLoadHelper.Load(homeLogoPath) is null
                          || Services.ImageLoadHelper.Load(awayLogoPath) is null;
            ApplyLogo(WormHomeLogoImage, WormHomeLogoFallback, homeLogoPath, hideLogos);
            ApplyLogo(WormAwayLogoImage, WormAwayLogoFallback, awayLogoPath, hideLogos);

            // Bottom score bar
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride);

            // Cache for resize redraws
            _lastMatch = match;
            _lastEndedQuarter = endedQuarter;
            _lastHomeColor = homeColor;
            _lastAwayColor = awayColor;

            DrawGraph(match, endedQuarter, homeColor, awayColor);
        }

        private static void ApplyLogo(System.Windows.Controls.Image image, Ellipse fallback, string? path, bool hideLogos = false)
        {
            if (hideLogos)
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Collapsed;
                return;
            }

            var source = Services.ImageLoadHelper.LoadTrimmed(path);
            image.Source = source;
            image.Visibility = source is not null ? Visibility.Visible : Visibility.Collapsed;
            fallback.Visibility = source is not null ? Visibility.Collapsed : Visibility.Visible;
        }

        private double ScaleUnit => ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);

        private double ScalePx(double designValue)
            => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);

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

            GraphCanvas.UpdateLayout();
            double canvasW = GraphCanvas.ActualWidth;
            double canvasH = GraphCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            double scale = ScaleUnit;
            double padTop = ScoreboardScaleHelper.Scale(20, scale);
            double padBottom = ScoreboardScaleHelper.Scale(20, scale);
            double graphH = Math.Max(1, canvasH - padTop - padBottom);

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
            DrawGridlines(canvasW, canvasH, padTop, padBottom, yCenter, yScale, maxAbsMargin, homeColor, awayColor, scale);

            // ---- Draw quarter dividers ----
            DrawQuarterDividers(events, canvasH, xStep, scale);

            // ---- Draw zero line ----
            var zeroLine = new Line
            {
                X1 = 0,
                Y1 = yCenter,
                X2 = canvasW,
                Y2 = yCenter,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                StrokeThickness = ScoreboardScaleHelper.Scale(1.5, scale)
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
                StrokeThickness = ScoreboardScaleHelper.Scale(2.5, scale),
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
                    Width = ScoreboardScaleHelper.Scale(i == 0 ? 6 : 7, scale),
                    Height = ScoreboardScaleHelper.Scale(i == 0 ? 6 : 7, scale),
                    Fill = new SolidColorBrush(dotColor),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = ScoreboardScaleHelper.Scale(1.2, scale),
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
            DrawYAxisLabels(yCenter, yScale, maxAbsMargin, canvasH, scale);
        }

        private void DrawGridlines(double canvasW, double canvasH, double padTop, double padBottom,
            double yCenter, double yScale, int maxAbsMargin, Color homeColor, Color awayColor, double scale)
        {
            int step = NiceStep(maxAbsMargin);
            var gridBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            double gridThickness = ScoreboardScaleHelper.Scale(0.8, scale);

            for (int val = step; val <= maxAbsMargin; val += step)
            {
                // Home side (positive = above center)
                double yUp = yCenter - val * yScale;
                if (yUp >= padTop)
                {
                    var line = new Line { X1 = 0, Y1 = yUp, X2 = canvasW, Y2 = yUp, Stroke = gridBrush, StrokeThickness = gridThickness };
                    GraphCanvas.Children.Add(line);
                }

                // Away side (negative = below center)
                double yDown = yCenter + val * yScale;
                if (yDown <= canvasH - padBottom)
                {
                    var line = new Line { X1 = 0, Y1 = yDown, X2 = canvasW, Y2 = yDown, Stroke = gridBrush, StrokeThickness = gridThickness };
                    GraphCanvas.Children.Add(line);
                }
            }
        }

        private void DrawQuarterDividers(IReadOnlyList<ScoreEvent> events, double canvasH, double xStep, double scale)
        {
            // Find the last event index of each quarter
            var quarterBreaks = new HashSet<int>();
            for (int i = 0; i < events.Count - 1; i++)
            {
                if (events[i].Quarter != events[i + 1].Quarter)
                    quarterBreaks.Add(i + 1); // +1 because margins list is offset by 1 (starts with 0)
            }

            var divBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            double dividerThickness = ScoreboardScaleHelper.Scale(1, scale);
            foreach (int idx in quarterBreaks)
            {
                double x = idx * xStep;
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = canvasH,
                    Stroke = divBrush,
                    StrokeThickness = dividerThickness,
                    StrokeDashArray = new DoubleCollection
                    {
                        ScoreboardScaleHelper.Scale(4, scale),
                        ScoreboardScaleHelper.Scale(3, scale)
                    }
                };
                GraphCanvas.Children.Add(line);

                // Quarter label (AFL: QT, HT, 3QT)
                int q = events[idx - 1].Quarter;
                string qLabel = q switch { 1 => "QT", 2 => "HT", 3 => "3QT", _ => $"Q{q}" };
                var label = new TextBlock
                {
                    Text = qLabel,
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    FontSize = ScoreboardScaleHelper.Scale(15, scale),
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(label, x + ScoreboardScaleHelper.Scale(3, scale));
                Canvas.SetTop(label, ScoreboardScaleHelper.Scale(2, scale));
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

        private void DrawYAxisLabels(double yCenter, double yScale, int maxAbsMargin, double canvasH, double scale)
        {
            double axisCanvasH = YAxisCanvas.ActualHeight > 0 ? YAxisCanvas.ActualHeight : canvasH;

            int step = NiceStep(maxAbsMargin);

            var labelBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            double labelWidth = Math.Max(ScoreboardScaleHelper.Scale(50, scale), YAxisCanvas.ActualWidth);
            double zeroFontSize = ScoreboardScaleHelper.Scale(15, scale);
            double minorFontSize = ScoreboardScaleHelper.Scale(13, scale);
            double zeroYOffset = ScoreboardScaleHelper.Scale(10, scale);
            double minorYOffset = ScoreboardScaleHelper.Scale(9, scale);

            // Zero label
            var zeroLabel = new TextBlock
            {
                Text = "0",
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = zeroFontSize,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Width = labelWidth,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(zeroLabel, 0);
            Canvas.SetTop(zeroLabel, yCenter - zeroYOffset);
            YAxisCanvas.Children.Add(zeroLabel);

            for (int val = step; val <= maxAbsMargin; val += step)
            {
                // Positive (home leads)
                double yUp = yCenter - val * yScale;
                var upLabel = new TextBlock
                {
                    Text = $"+{val}",
                    Foreground = labelBrush,
                    FontSize = minorFontSize,
                    Width = labelWidth,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(upLabel, 0);
                Canvas.SetTop(upLabel, yUp - minorYOffset);
                YAxisCanvas.Children.Add(upLabel);

                // Negative (away leads)
                double yDown = yCenter + val * yScale;
                var downLabel = new TextBlock
                {
                    Text = $"-{val}",
                    Foreground = labelBrush,
                    FontSize = minorFontSize,
                    Width = labelWidth,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(downLabel, 0);
                Canvas.SetTop(downLabel, yDown - minorYOffset);
                YAxisCanvas.Children.Add(downLabel);
            }
        }

        private static void DrawEmptyMessage()
        {
            var tb = new TextBlock
            {
                Text = "No scoring events yet",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private static int NiceMax(int val)
        {
            if (val <= 0) return 1;
            int magnitude = 1;
            while (magnitude * 10 <= val) magnitude *= 10;
            if (val <= magnitude * 2) return magnitude * 2;
            if (val <= magnitude * 3) return magnitude * 3;
            if (val <= magnitude * 5) return magnitude * 5;
            return magnitude * 10;
        }

        private static int NiceStep(int range)
        {
            if (range <= 0) return 1;
            int raw = Math.Max(1, range / 5);
            int magnitude = 1;
            while (magnitude * 10 <= raw) magnitude *= 10;
            if (raw <= magnitude) return magnitude;
            if (raw <= magnitude * 2) return magnitude * 2;
            if (raw <= magnitude * 5) return magnitude * 5;
            return magnitude * 10;
        }

        private static Color SafeColor(string? hex, string fallbackHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    hex = fallbackHex;
                if (!hex.StartsWith("#"))
                    hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(fallbackHex);
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

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromRgb(
                (byte)(c.R * (1 - amount)),
                (byte)(c.G * (1 - amount)),
                (byte)(c.B * (1 - amount)));
        }
    }
}
