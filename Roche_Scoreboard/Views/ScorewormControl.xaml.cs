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

            // Indicator column: vertical brush splits home (top) / away
            // (bottom), each half carries the team abbreviation rotated 90°
            // and a zoomed-in logo. No gradients overlaid on the graph
            // itself — the side column does all the team-identity work.
            WormSideStripBrush.GradientStops.Clear();
            WormSideStripBrush.GradientStops.Add(new System.Windows.Media.GradientStop(homeColor, 0.0));
            WormSideStripBrush.GradientStops.Add(new System.Windows.Media.GradientStop(homeColor, 0.5));
            WormSideStripBrush.GradientStops.Add(new System.Windows.Media.GradientStop(awayColor, 0.5));
            WormSideStripBrush.GradientStops.Add(new System.Windows.Media.GradientStop(awayColor, 1.0));

            WormHomeAbbrText.Text = (match.HomeAbbr ?? "HOM").ToUpperInvariant();
            WormHomeAbbrText.Foreground = Services.ContrastHelper.GetContrastBrush(homeColor);

            WormAwayAbbrText.Text = (match.AwayAbbr ?? "AWY").ToUpperInvariant();
            WormAwayAbbrText.Foreground = Services.ContrastHelper.GetContrastBrush(awayColor);

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
            double padTop = ScoreboardScaleHelper.Scale(8, scale);
            double padBottom = ScoreboardScaleHelper.Scale(8, scale);
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
                StrokeThickness = ScoreboardScaleHelper.Scale(2.5, scale)
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
            // Slimmer line for a more modern look, with a soft glow + bright stroke
            // so the path remains highly visible against the dark graph background.
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = ScoreboardScaleHelper.Scale(2.4, scale),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { totalLength, totalLength },
                StrokeDashOffset = totalLength,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.White,
                    BlurRadius = ScoreboardScaleHelper.Scale(10.0, scale),
                    ShadowDepth = 0,
                    Opacity = 0.85
                }
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

            // ---- Identify which event points get an emphasized dot ----
            // To avoid clutter we only emphasize: first point, quarter-end points,
            // and the final/current point. All others get a small marker.
            var emphasizedIdx = new HashSet<int> { 0, totalPoints - 1 };
            for (int i = 0; i < events.Count - 1; i++)
            {
                if (events[i].Quarter != events[i + 1].Quarter)
                    emphasizedIdx.Add(i + 1);
            }

            // ---- Draw dots (fade in as line reaches each point) ----
            for (int i = 0; i < totalPoints; i++)
            {
                bool isHomeAbove = margins[i] > 0;
                bool isZero = margins[i] == 0;
                var dotColor = isZero ? Colors.White : (isHomeAbove ? homeColor : awayColor);
                bool emphasize = emphasizedIdx.Contains(i);

                double dotSize = emphasize
                    ? ScoreboardScaleHelper.Scale(i == totalPoints - 1 ? 11 : 8, scale)
                    : 0;

                if (dotSize <= 0)
                    continue;

                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = new SolidColorBrush(dotColor),
                    Stroke = emphasize ? new SolidColorBrush(Colors.White) : null,
                    StrokeThickness = emphasize ? ScoreboardScaleHelper.Scale(1.2, scale) : 0,
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
            var gridBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            double gridThickness = ScoreboardScaleHelper.Scale(0.6, scale);

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
                    Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    FontSize = ScoreboardScaleHelper.Scale(9, scale),
                    FontWeight = FontWeights.Bold,
                    FontFamily = new System.Windows.Media.FontFamily("Bahnschrift")
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
            int step = NiceStep(maxAbsMargin);

            var labelBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            double labelWidth = YAxisCanvas.ActualWidth > 0
                ? YAxisCanvas.ActualWidth
                : ScoreboardScaleHelper.Scale(34, scale);
            // Right-pad so labels don't kiss the chart border
            double rightPad = ScoreboardScaleHelper.Scale(3, scale);
            double zeroFontSize = ScoreboardScaleHelper.Scale(13, scale);
            double minorFontSize = ScoreboardScaleHelper.Scale(11, scale);
            double zeroYOffset = ScoreboardScaleHelper.Scale(8, scale);
            double minorYOffset = ScoreboardScaleHelper.Scale(7, scale);

            void AddLabel(string text, double y, double fontSize, double yOffset, FontWeight weight, Brush brush)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    Foreground = brush,
                    FontSize = fontSize,
                    FontWeight = weight,
                    FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
                    Width = labelWidth - rightPad,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(tb, 0);
                Canvas.SetTop(tb, y - yOffset);
                YAxisCanvas.Children.Add(tb);
            }

            // Zero label
            AddLabel("0", yCenter, zeroFontSize, zeroYOffset, FontWeights.Bold,
                new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));

            for (int val = step; val <= maxAbsMargin; val += step)
            {
                double yUp = yCenter - val * yScale;
                AddLabel("+" + val, yUp, minorFontSize, minorYOffset, FontWeights.SemiBold, labelBrush);

                double yDown = yCenter + val * yScale;
                AddLabel("-" + val, yDown, minorFontSize, minorYOffset, FontWeights.SemiBold, labelBrush);
            }
        }

        private static void DrawEmptyMessage()
        {
            // Intentional no-op: the empty graph background communicates the
            // pre-game state. Adding text here would clutter the small panel.
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
            // Aim for roughly 2-3 labelled ticks on each side of zero so the
            // axis stays readable and uncluttered regardless of margin magnitude.
            // For small ranges this returns small steps (1, 2, 5); for large
            // ranges it climbs through 10, 20, 50, 100... dynamically.
            if (range <= 0) return 1;
            if (range <= 3) return 1;
            if (range <= 6) return 2;
            if (range <= 12) return 5;

            const int targetTicksPerSide = 3;
            double raw = (double)range / targetTicksPerSide;
            int magnitude = 1;
            while (magnitude * 10 <= raw) magnitude *= 10;
            double normalized = raw / magnitude;
            int niceNormalized = normalized switch
            {
                <= 1.5 => 1,
                <= 3.5 => 2,
                <= 7.5 => 5,
                _ => 10
            };
            return niceNormalized * magnitude;
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
