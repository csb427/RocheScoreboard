using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        private bool _responsiveLayoutQueued;

        public void SetFinalsMode(bool enabled)
        {
            WormTitlePanel.Background = enabled ? FinalsPalette.PanelBrush() : new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
            WormTitleText.Foreground = WormTimeText.Foreground = enabled
                ? new SolidColorBrush(FinalsPalette.Ink)
                : System.Windows.Media.Brushes.White;
            ScoreBar?.SetFinalsMode(enabled);
        }

        public void PlayScoreAnimation(ScoreEvent scoreEvent)
            => ScoreBar?.PlayScoreAnimation(scoreEvent);

        // Cached params from the most recent Populate so we can redraw the
        // graph when the host control is resized (canvas dimensions change).
        private MatchManager? _lastMatch;
        private int _lastEndedQuarter;
        private Color _lastHomeColor;
        private Color _lastAwayColor;
        private int? _lastGraphSignature;

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

            SizeChanged += (_, _) => QueueResponsiveLayout();
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true) QueueResponsiveLayout();
            };
            Loaded += (_, _) =>
            {
                _isDisposed = false;
                _wallClockTimer.Start();
                QueueResponsiveLayout();
            };

            Unloaded += (_, _) => Cleanup();
        }

        /// <summary>
        /// Chrome rows are star-proportional in XAML; this method is
        /// kept for compatibility with size-changed wiring but no longer
        /// overwrites row heights.
        /// </summary>
        private void ApplyResponsiveScale()
        {
            if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            double scale = ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);
            ResponsivePresentationTypography.Apply(this, scale);

            // The chart font scales with the output, so its axis gutter must
            // scale too. A fixed 36 px gutter clipped two-digit bounds (12,
            // 18, 24...) at larger scoreboard dimensions.
            YAxisColumn.Width = new GridLength(Math.Clamp(
                ScoreboardScaleHelper.Scale(36, scale), 28, 58));
            if (_lastMatch is not null)
                UpdateMarginHeadline(_lastMatch);
        }

        private void QueueResponsiveLayout()
        {
            if (_responsiveLayoutQueued || _isDisposed)
                return;

            _responsiveLayoutQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _responsiveLayoutQueued = false;
                ApplyResponsiveScale();
                if (_lastMatch is not null)
                    DrawGraphIfChanged(_lastMatch, _lastEndedQuarter, _lastHomeColor, _lastAwayColor);
            }, DispatcherPriority.Render);
        }

        private void UpdateMarginHeadline(MatchManager match)
        {
            int margin = match.Margin;
            string leadingTeam = margin > 0 ? match.HomeAbbr ?? "HOM" : match.AwayAbbr ?? "AWY";
            bool compact = ActualWidth < 320;
            WormMarginText.Text = margin == 0
                ? (compact ? "LEVEL" : "SCORES LEVEL")
                : compact
                    ? $"{leadingTeam.ToUpperInvariant()} +{Math.Abs(margin)}"
                    : $"{leadingTeam.ToUpperInvariant()} BY {Math.Abs(margin)}";
        }

        private void OnGraphCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isDisposed || _lastMatch is null) return;
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
            DrawGraphIfChanged(_lastMatch, _lastEndedQuarter, _lastHomeColor, _lastAwayColor);
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

            int margin = match.Margin;
            UpdateMarginHeadline(match);
            Color preferredMargin = margin > 0 ? Lighten(homeColor, 0.30)
                : margin < 0 ? Lighten(awayColor, 0.30)
                : Colors.White;
            WormMarginText.Foreground = new SolidColorBrush(SmartContrast.PickReadableForeground(
                preferredMargin, Color.FromRgb(0x0D, 0x11, 0x17)));

            // Bottom score bar
            ScoreBar.Populate(match, endedQuarter, homeColorHex, awayColorHex,
                homeSecondaryHex, awaySecondaryHex, homeLogoPath, awayLogoPath,
                barTitleOverride);

            // Cache for resize redraws
            _lastMatch = match;
            _lastEndedQuarter = endedQuarter;
            _lastHomeColor = homeColor;
            _lastAwayColor = awayColor;

            DrawGraphIfChanged(match, endedQuarter, homeColor, awayColor);
        }

        private double ScaleUnit => ScoreboardScaleHelper.GetScale(ActualWidth, ActualHeight);

        private double ScalePx(double designValue)
            => ScoreboardScaleHelper.Scale(designValue, ScaleUnit);

        private void DrawGraphIfChanged(MatchManager match, int endedQuarter, Color homeColor, Color awayColor)
        {
            WormTitleRow.Height = new GridLength(0);
            WormGraphRow.Height = new GridLength(149, GridUnitType.Star);
            WormTitlePanel.Visibility = Visibility.Collapsed;
            int signature = BuildGraphSignature(match, endedQuarter, homeColor, awayColor,
                GraphCanvas.ActualWidth, GraphCanvas.ActualHeight);
            if (_lastGraphSignature == signature)
                return;

            DrawGraph(match, endedQuarter, homeColor, awayColor);
            if (GraphCanvas.ActualWidth > 0 && GraphCanvas.ActualHeight > 0)
                _lastGraphSignature = signature;
        }

        private static int BuildGraphSignature(MatchManager match, int endedQuarter,
            Color homeColor, Color awayColor, double width, double height)
        {
            HashCode hash = new();
            hash.Add(endedQuarter);
            hash.Add(homeColor);
            hash.Add(awayColor);
            hash.Add((int)Math.Round(width));
            hash.Add((int)Math.Round(height));
            foreach (ScoreEvent scoreEvent in match.Events)
            {
                hash.Add(scoreEvent.Quarter);
                hash.Add(scoreEvent.Team);
                hash.Add(scoreEvent.Type);
                hash.Add(scoreEvent.Margin);
            }
            return hash.ToHashCode();
        }

        private void DrawGraph(MatchManager match, int endedQuarter, Color homeColor, Color awayColor)
        {
            GraphCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
            ZoneCanvas.Children.Clear();

            var events = match.Events;
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

            int observedHome = Math.Max(0, margins.Max());
            int observedAway = Math.Max(0, -margins.Min());
            int dominant = Math.Max(observedHome, observedAway);
            // Let each side size itself independently.  A small reserve keeps an
            // unused side visible without forcing a one-point lead to consume the
            // same height as a much larger margin on the other side.
            int quietSideReserve = Math.Max(1, (int)Math.Ceiling(dominant * 0.20));
            int homeRange = NiceBound(observedHome > 0 ? observedHome : quietSideReserve);
            int awayRange = NiceBound(observedAway > 0 ? observedAway : quietSideReserve);
            if (dominant == 0) homeRange = awayRange = 6;

            double yCenter = padTop + graphH * homeRange / (homeRange + (double)awayRange);
            double homeScale = (yCenter - padTop) / Math.Max(1, homeRange);
            double awayScale = (padTop + graphH - yCenter) / Math.Max(1, awayRange);

            DrawTeamZones(canvasH, yCenter, homeColor, awayColor);

            int totalPoints = margins.Count;
            double xStep = canvasW / Math.Max(1, totalPoints - 1);

            // ---- Draw gridlines ----
            DrawGridlines(canvasW, yCenter, homeScale, awayScale, homeRange, awayRange, scale);

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

            // ---- Build a true scoreworm step path ----
            // The margin is constant between score events, then changes at the
            // event itself. Horizontal travel followed by a vertical switch
            // produces the familiar 90-degree broadcast scoreworm geometry.
            var points = new List<Point> { new(0, yCenter) };
            for (int i = 1; i < totalPoints; i++)
            {
                double x = i * xStep;
                int previousMargin = margins[i - 1];
                int currentMargin = margins[i];
                double previousY = previousMargin >= 0
                    ? yCenter - previousMargin * homeScale
                    : yCenter - previousMargin * awayScale;
                double currentY = currentMargin >= 0
                    ? yCenter - currentMargin * homeScale
                    : yCenter - currentMargin * awayScale;
                points.Add(new Point(x, previousY));
                points.Add(new Point(x, currentY));
            }

            // ---- Draw filled areas and the stable step path ----
            DrawFilledAreas(points, yCenter, canvasW, homeColor, awayColor);

            // The screen is refreshed while the clock runs. A per-populate draw
            // animation used to restart during those refreshes, making repeated
            // exposure look like a different layout. Keep the graph itself
            // deterministic; the presentation transition supplies the motion.
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = ScoreboardScaleHelper.Scale(2.4, scale),
                StrokeLineJoin = PenLineJoin.Miter,
                StrokeStartLineCap = PenLineCap.Square,
                StrokeEndLineCap = PenLineCap.Square,
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

            // ---- Identify only meaningful step corners for markers ----
            // The final point and quarter transitions are enough; marking every
            // score would fight the crisp right-angle geometry.
            var emphasizedMarginIdx = new HashSet<int> { 0, totalPoints - 1 };
            for (int i = 0; i < events.Count - 1; i++)
            {
                if (events[i].Quarter != events[i + 1].Quarter)
                    emphasizedMarginIdx.Add(i + 1);
            }

            // ---- Draw markers (fade in as the step reaches each corner) ----
            foreach (int marginIndex in emphasizedMarginIdx.OrderBy(i => i))
            {
                int pointIndex = marginIndex == 0 ? 0 : marginIndex * 2;
                if (pointIndex >= points.Count) continue;
                bool isHomeAbove = margins[marginIndex] > 0;
                bool isZero = margins[marginIndex] == 0;
                var dotColor = isZero ? Colors.White : (isHomeAbove ? homeColor : awayColor);
                double dotSize = ScoreboardScaleHelper.Scale(
                    marginIndex == totalPoints - 1 ? 10 : 7, scale);

                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = new SolidColorBrush(dotColor),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = ScoreboardScaleHelper.Scale(1.1, scale),
                    Opacity = 1
                };

                Canvas.SetLeft(dot, points[pointIndex].X - dot.Width / 2);
                Canvas.SetTop(dot, points[pointIndex].Y - dot.Height / 2);
                GraphCanvas.Children.Add(dot);

            }

            // ---- Y-axis labels ----
            DrawYAxisLabels(yCenter, homeScale, awayScale, homeRange, awayRange, scale, homeColor, awayColor);

            if (events.Count == 0)
                DrawEmptyMessage(canvasW, yCenter, scale);
        }

        private void DrawTeamZones(double canvasH, double yCenter, Color homeColor, Color awayColor)
        {
            var homeZone = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0, ZoneCanvas.ActualWidth),
                Height = Math.Max(0, yCenter + 3),
                Fill = new SolidColorBrush(Color.FromArgb(0x36, homeColor.R, homeColor.G, homeColor.B))
            };
            ZoneCanvas.Children.Add(homeZone);

            var awayZone = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0, ZoneCanvas.ActualWidth),
                Height = Math.Max(0, canvasH - yCenter + 3),
                Fill = new SolidColorBrush(Color.FromArgb(0x36, awayColor.R, awayColor.G, awayColor.B))
            };
            Canvas.SetTop(awayZone, yCenter + 3);
            ZoneCanvas.Children.Add(awayZone);

            // Thin edge rails make ownership clear without consuming a labelled
            // row or competing with the graph itself.
            double railWidth = Math.Max(2, ScalePx(3));
            ZoneCanvas.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = railWidth,
                Height = Math.Max(0, yCenter),
                Fill = new SolidColorBrush(homeColor)
            });
            var awayRail = new System.Windows.Shapes.Rectangle
            {
                Width = railWidth,
                Height = Math.Max(0, canvasH - yCenter),
                Fill = new SolidColorBrush(awayColor)
            };
            Canvas.SetTop(awayRail, yCenter);
            ZoneCanvas.Children.Add(awayRail);
        }

        private void DrawGridlines(double canvasW, double yCenter, double homeScale, double awayScale,
            int homeRange, int awayRange, double scale)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            double gridThickness = ScoreboardScaleHelper.Scale(0.65, scale);

            foreach (int val in AxisTicks(homeRange))
            {
                double y = yCenter - val * homeScale;
                GraphCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = canvasW, Y2 = y, Stroke = gridBrush, StrokeThickness = gridThickness });
            }
            foreach (int val in AxisTicks(awayRange))
            {
                double y = yCenter + val * awayScale;
                GraphCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = canvasW, Y2 = y, Stroke = gridBrush, StrokeThickness = gridThickness });
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

                // Label the section that begins after this divider. This uses the
                // same ordinal language as the quarter-by-quarter presentation.
                int q = events[idx - 1].Quarter;
                string qLabel = (q + 1) switch { 2 => "2ND", 3 => "3RD", 4 => "4TH", _ => $"Q{q + 1}" };
                var label = new TextBlock
                {
                    Text = qLabel,
                    Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    FontSize = ScoreboardScaleHelper.Scale(12, scale),
                    FontWeight = FontWeights.Black,
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

        private void DrawYAxisLabels(double yCenter, double homeScale, double awayScale,
            int homeRange, int awayRange, double scale, Color homeColor, Color awayColor)
        {
            double labelWidth = YAxisCanvas.ActualWidth > 0
                ? YAxisCanvas.ActualWidth
                : ScoreboardScaleHelper.Scale(34, scale);
            // Right-pad so labels don't kiss the chart border
            double rightPad = ScoreboardScaleHelper.Scale(3, scale);
            double zeroFontSize = ScoreboardScaleHelper.Scale(15, scale);
            double minorFontSize = ScoreboardScaleHelper.Scale(13, scale);
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
                    FontFamily = new System.Windows.Media.FontFamily("Arial"),
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

            Color graphSurface = Color.FromRgb(0x08, 0x0D, 0x14);
            var homeBrush = new SolidColorBrush(SmartContrast.PickReadableForeground(
                Lighten(homeColor, 0.38), graphSurface, Darken(homeColor, 0.70)));
            var awayBrush = new SolidColorBrush(SmartContrast.PickReadableForeground(
                Lighten(awayColor, 0.38), graphSurface, Darken(awayColor, 0.70)));

            foreach (int val in AxisTicks(homeRange))
            {
                double yUp = yCenter - val * homeScale;
                AddLabel(val.ToString(), yUp, minorFontSize, minorYOffset, FontWeights.SemiBold, homeBrush);
            }
            foreach (int val in AxisTicks(awayRange))
            {
                double yDown = yCenter + val * awayScale;
                AddLabel(val.ToString(), yDown, minorFontSize, minorYOffset, FontWeights.SemiBold, awayBrush);
            }
        }

        private void DrawEmptyMessage(double canvasW, double yCenter, double scale)
        {
            var message = new TextBlock
            {
                Text = "MARGIN FLOW BEGINS WITH THE FIRST SCORE",
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
                FontSize = ScoreboardScaleHelper.Scale(14, scale),
                FontWeight = FontWeights.Black,
                TextAlignment = TextAlignment.Center,
                Width = canvasW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(message, 0);
            Canvas.SetTop(message, yCenter + ScoreboardScaleHelper.Scale(12, scale));
            GraphCanvas.Children.Add(message);
        }

        private static int NiceBound(int value)
        {
            if (value <= 1) return 1;
            if (value <= 3) return 3;
            if (value <= 6) return 6;
            if (value <= 12) return 12;
            if (value <= 18) return 18;
            if (value <= 24) return 24;
            if (value <= 30) return 30;
            if (value <= 40) return 40;
            if (value <= 50) return 50;
            return (int)Math.Ceiling(value / 10d) * 10;
        }

        private static IEnumerable<int> AxisTicks(int range)
        {
            if (range <= 0)
                yield break;

            // At most two labels per side. The bound itself is always shown;
            // larger ranges gain one useful midpoint without a picket fence of
            // numbers competing with the scoreworm.
            if (range > 18)
            {
                int midpoint = NiceBound((int)Math.Ceiling(range / 2d));
                if (midpoint < range)
                    yield return midpoint;
            }

            yield return range;
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
