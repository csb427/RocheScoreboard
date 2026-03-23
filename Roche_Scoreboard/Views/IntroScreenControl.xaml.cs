using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Roche_Scoreboard.Views
{
    public partial class IntroScreenControl : System.Windows.Controls.UserControl
    {
        private Storyboard? _idleStoryboard;
        private Storyboard? _dismissStoryboard;

        public IntroScreenControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Set team colours, logos, and names. Call before showing.
        /// </summary>
        public void Populate(string homeColorHex, string awayColorHex,
                             string homeName, string awayName,
                             string? homeLogoPath, string? awayLogoPath,
                             string homeSecondaryHex = "#FFFFFF", string awaySecondaryHex = "#FFFFFF")
        {
            var homeColor = SafeColor(homeColorHex, "#8B1A1A");
            var awayColor = SafeColor(awayColorHex, "#2D5A27");
            HomeBgBrush.Color = homeColor;
            AwayBgBrush.Color = awayColor;

            // Auto-detect readable text colour for team backgrounds
            HomeNameBrush.Color = ContrastHelper.GetContrastForeground(homeColor);
            AwayNameBrush.Color = ContrastHelper.GetContrastForeground(awayColor);

            // Home logo
            if (!string.IsNullOrWhiteSpace(homeLogoPath) && File.Exists(homeLogoPath))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(homeLogoPath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                HomeLogoImage.Source = bi;
                HomeLogoImage.Visibility = Visibility.Visible;
                HomeNameFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                HomeLogoImage.Visibility = Visibility.Collapsed;
                HomeNameFallback.Visibility = Visibility.Visible;
                HomeNameFallback.Text = homeName.ToUpperInvariant();
            }

            // Away logo
            if (!string.IsNullOrWhiteSpace(awayLogoPath) && File.Exists(awayLogoPath))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(awayLogoPath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                AwayLogoImage.Source = bi;
                AwayLogoImage.Visibility = Visibility.Visible;
                AwayNameFallback.Visibility = Visibility.Collapsed;
            }
            else
            {
                AwayLogoImage.Visibility = Visibility.Collapsed;
                AwayNameFallback.Visibility = Visibility.Visible;
                AwayNameFallback.Text = awayName.ToUpperInvariant();
            }
        }

        /// <summary>
        /// Start the dramatic idle loop animations (shimmer, logo pulse, VS glow).
        /// </summary>
        public void StartIdleAnimations()
        {
            _idleStoryboard?.Stop();

            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // --- VS text slam in ---
            // Scale from 0.5 → 1.1 → 1.0  (slam with overshoot)
            var vsScaleX = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(0.3) };
            vsScaleX.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            vsScaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            vsScaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)),
                new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(vsScaleX, VsText);
            Storyboard.SetTargetProperty(vsScaleX, new PropertyPath("RenderTransform.Children[0].ScaleX"));
            sb.Children.Add(vsScaleX);

            var vsScaleY = vsScaleX.Clone();
            Storyboard.SetTarget(vsScaleY, VsText);
            Storyboard.SetTargetProperty(vsScaleY, new PropertyPath("RenderTransform.Children[0].ScaleY"));
            sb.Children.Add(vsScaleY);

            // VS fade in
            var vsFade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
            { BeginTime = TimeSpan.FromSeconds(0.3), EasingFunction = ease };
            Storyboard.SetTarget(vsFade, VsText);
            Storyboard.SetTargetProperty(vsFade, new PropertyPath(OpacityProperty));
            sb.Children.Add(vsFade);

            // --- Diagonal seam glow pulse (continuous) ---
            var seamPulse = new DoubleAnimation(0.0, 0.6, TimeSpan.FromSeconds(1.2))
            { BeginTime = TimeSpan.FromSeconds(0.6), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            Storyboard.SetTarget(seamPulse, DiagonalGlow);
            Storyboard.SetTargetProperty(seamPulse, new PropertyPath(OpacityProperty));
            sb.Children.Add(seamPulse);

            // --- VS glow pulse (continuous) ---
            var vsGlowPulse = new DoubleAnimation(20, 50, TimeSpan.FromSeconds(1.0))
            { BeginTime = TimeSpan.FromSeconds(0.8), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(vsGlowPulse, VsText);
            Storyboard.SetTargetProperty(vsGlowPulse, new PropertyPath("Effect.BlurRadius"));
            sb.Children.Add(vsGlowPulse);

            // --- Home shimmer sweep (repeating) ---
            var homeShimOpacity = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            homeShimOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            homeShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
            homeShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            homeShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            homeShimOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.0))));
            Storyboard.SetTarget(homeShimOpacity, HomeShimmer);
            Storyboard.SetTargetProperty(homeShimOpacity, new PropertyPath(OpacityProperty));
            sb.Children.Add(homeShimOpacity);

            var homeShimX = new DoubleAnimation(-300, 900, TimeSpan.FromSeconds(1.5))
            { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            homeShimX.Duration = TimeSpan.FromSeconds(4.0);
            Storyboard.SetTarget(homeShimX, HomeShimmer);
            Storyboard.SetTargetProperty(homeShimX, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(homeShimX);

            // --- Away shimmer sweep (repeating, offset) ---
            var awayShimOpacity = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(2.0) };
            awayShimOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            awayShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
            awayShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            awayShimOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            awayShimOpacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.0))));
            Storyboard.SetTarget(awayShimOpacity, AwayShimmer);
            Storyboard.SetTargetProperty(awayShimOpacity, new PropertyPath(OpacityProperty));
            sb.Children.Add(awayShimOpacity);

            var awayShimX = new DoubleAnimation(900, -300, TimeSpan.FromSeconds(1.5))
            { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(2.0), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            awayShimX.Duration = TimeSpan.FromSeconds(4.0);
            Storyboard.SetTarget(awayShimX, AwayShimmer);
            Storyboard.SetTargetProperty(awayShimX, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(awayShimX);

            // --- Logo subtle pulse (continuous) ---
            var logoPulse = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(1.5))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };

            var homeLogoSX = logoPulse.Clone();
            Storyboard.SetTarget(homeLogoSX, HomeLogoImage);
            Storyboard.SetTargetProperty(homeLogoSX, new PropertyPath("RenderTransform.ScaleX"));
            sb.Children.Add(homeLogoSX);
            var homeLogoSY = logoPulse.Clone();
            Storyboard.SetTarget(homeLogoSY, HomeLogoImage);
            Storyboard.SetTargetProperty(homeLogoSY, new PropertyPath("RenderTransform.ScaleY"));
            sb.Children.Add(homeLogoSY);

            var awayLogoSX = logoPulse.Clone();
            Storyboard.SetTarget(awayLogoSX, AwayLogoImage);
            Storyboard.SetTargetProperty(awayLogoSX, new PropertyPath("RenderTransform.ScaleX"));
            sb.Children.Add(awayLogoSX);
            var awayLogoSY = logoPulse.Clone();
            Storyboard.SetTarget(awayLogoSY, AwayLogoImage);
            Storyboard.SetTargetProperty(awayLogoSY, new PropertyPath("RenderTransform.ScaleY"));
            sb.Children.Add(awayLogoSY);

            _idleStoryboard = sb;
            sb.Begin(this, true);
        }

        /// <summary>
        /// Dramatic split dismiss: home half slides left, away half slides right, VS fades out.
        /// Calls <paramref name="onComplete"/> when done.
        /// </summary>
        public void DismissWithSplit(Action? onComplete)
        {
            _idleStoryboard?.Stop();
            _idleStoryboard = null;

            var duration = TimeSpan.FromSeconds(0.7);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            double splitDistance = 800;

            var sb = new Storyboard();

            // VS flash bright then fade
            var vsFlash = new DoubleAnimationUsingKeyFrames();
            vsFlash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            vsFlash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.1))));
            vsFlash.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.35)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            Storyboard.SetTarget(vsFlash, VsText);
            Storyboard.SetTargetProperty(vsFlash, new PropertyPath(OpacityProperty));
            sb.Children.Add(vsFlash);

            // VS glow flare
            var vsGlowFlare = new DoubleAnimation(30, 80, TimeSpan.FromSeconds(0.2));
            Storyboard.SetTarget(vsGlowFlare, VsText);
            Storyboard.SetTargetProperty(vsGlowFlare, new PropertyPath("Effect.BlurRadius"));
            sb.Children.Add(vsGlowFlare);

            // Diagonal seam flash
            var seamFlash = new DoubleAnimationUsingKeyFrames();
            seamFlash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            seamFlash.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));
            Storyboard.SetTarget(seamFlash, DiagonalGlow);
            Storyboard.SetTargetProperty(seamFlash, new PropertyPath(OpacityProperty));
            sb.Children.Add(seamFlash);

            // Home half slides left
            var homeSlide = new DoubleAnimation(0, -splitDistance, duration)
            { BeginTime = TimeSpan.FromSeconds(0.15), EasingFunction = ease };
            Storyboard.SetTarget(homeSlide, HomeHalf);
            Storyboard.SetTargetProperty(homeSlide, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(homeSlide);

            // Away half slides right
            var awaySlide = new DoubleAnimation(0, splitDistance, duration)
            { BeginTime = TimeSpan.FromSeconds(0.15), EasingFunction = ease };
            Storyboard.SetTarget(awaySlide, AwayHalf);
            Storyboard.SetTargetProperty(awaySlide, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(awaySlide);

            sb.Completed += (_, __) =>
            {
                Visibility = Visibility.Collapsed;
                onComplete?.Invoke();
            };

            _dismissStoryboard = sb;
            sb.Begin(this, true);
        }

        /// <summary>
        /// Reset the intro to its initial state so it can be shown again.
        /// </summary>
        public void ResetAndShow(string homeColorHex, string awayColorHex,
                                  string homeName, string awayName,
                                  string? homeLogoPath, string? awayLogoPath,
                                  string homeSecondaryHex = "#FFFFFF", string awaySecondaryHex = "#FFFFFF")
        {
            _idleStoryboard?.Stop();
            _idleStoryboard = null;

            // Stop the dismiss storyboard to release all held animation values
            if (_dismissStoryboard != null)
            {
                _dismissStoryboard.Stop(this);
                _dismissStoryboard = null;
            }

            // Clear any remaining animations on translate transforms
            HomeHalfTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            AwayHalfTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            HomeHalfTranslate.X = 0;
            AwayHalfTranslate.X = 0;

            // Reset VS text
            VsText.BeginAnimation(OpacityProperty, null);
            VsText.Opacity = 0;
            DiagonalGlow.BeginAnimation(OpacityProperty, null);
            DiagonalGlow.Opacity = 0;

            Visibility = Visibility.Visible;

            Populate(homeColorHex, awayColorHex, homeName, awayName, homeLogoPath, awayLogoPath, homeSecondaryHex, awaySecondaryHex);
            StartIdleAnimations();
        }

        private static Color SafeColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }
    }
}
