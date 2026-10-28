using RocheScoreboard.Mac.Production;
// Generated from Views/IntroScreenControl.xaml.cs; edit translator/adapters.
// Source SHA-256: 75fb56828b46362fdce6739bdc69541a83580611fd9350a6eca0a8a2116240b0
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using Image = Avalonia.Controls.Image;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class IntroScreenControl : UserControl
    {
        private Storyboard? _idleStoryboard;
        private Storyboard? _dismissStoryboard;
        private bool _isDisposed;

        public IntroScreenControl()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            Unloaded += (_, _) => Cleanup();
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _idleStoryboard?.Stop();
            _idleStoryboard = null;
            _dismissStoryboard?.Stop();
            _dismissStoryboard = null;
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
            SetLogoImage(HomeLogoImage, HomeNameFallback, homeLogoPath, homeName);

            // Away logo
            SetLogoImage(AwayLogoImage, AwayNameFallback, awayLogoPath, awayName);
        }

        private static void SetLogoImage(Image image, TextBlock fallback, string? logoPath, string teamName)
        {
            if (string.IsNullOrWhiteSpace(logoPath) || !System.IO.File.Exists(logoPath))
            {
                image.IsVisible = false;
                fallback.IsVisible = true;
                fallback.Text = teamName.ToUpperInvariant();
                return;
            }

            try
            {
                var bi = new NativeBitmap();
                bi.BeginInit();
                bi.UriSource = new Uri(logoPath, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                image.Source = bi;
                image.IsVisible = true;
                fallback.IsVisible = false;
            }
            catch
            {
                image.IsVisible = false;
                fallback.IsVisible = true;
                fallback.Text = teamName.ToUpperInvariant();
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
            { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4.0), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
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
            { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(2.0), Duration = TimeSpan.FromSeconds(4.0), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(awayShimX, AwayShimmer);
            Storyboard.SetTargetProperty(awayShimX, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(awayShimX);

            // --- Logo subtle pulse (continuous) ---
            var logoPulse = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(1.5))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };

            foreach (var element in new[] { HomeLogoImage, AwayLogoImage })
            {
                var logoSX = logoPulse.Clone();
                Storyboard.SetTarget(logoSX, element);
                Storyboard.SetTargetProperty(logoSX, new PropertyPath("RenderTransform.ScaleX"));
                sb.Children.Add(logoSX);

                var logoSY = logoPulse.Clone();
                Storyboard.SetTarget(logoSY, element);
                Storyboard.SetTargetProperty(logoSY, new PropertyPath("RenderTransform.ScaleY"));
                sb.Children.Add(logoSY);
            }

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

            var sb = new Storyboard();
            sb.Completed += (_, _) => onComplete?.Invoke();

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
            var homeSlide = new DoubleAnimation(0, -800, TimeSpan.FromSeconds(0.7))
            { BeginTime = TimeSpan.FromSeconds(0.15), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(homeSlide, HomeHalf);
            Storyboard.SetTargetProperty(homeSlide, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(homeSlide);

            // Away half slides right
            var awaySlide = new DoubleAnimation(0, 800, TimeSpan.FromSeconds(0.7))
            { BeginTime = TimeSpan.FromSeconds(0.15), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(awaySlide, AwayHalf);
            Storyboard.SetTargetProperty(awaySlide, new PropertyPath("RenderTransform.X"));
            sb.Children.Add(awaySlide);

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

            IsVisible = true;

            Populate(homeColorHex, awayColorHex, homeName, awayName, homeLogoPath, awayLogoPath, homeSecondaryHex, awaySecondaryHex);
            StartIdleAnimations();
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

        /// <summary>Applies a component-level finals accent without recolouring the teams.</summary>
        public void SetFinalsMode(bool enabled)
        {
            VsText.Foreground = enabled
                ? new SolidColorBrush(Services.FinalsPalette.GoldLight)
                : Avalonia.Media.Brushes.White;
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class IntroScreenControl
{
    private TranslateTransform HomeHalfTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeHalfTranslate"];
    private SolidColorBrush HomeBgBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeBgBrush"];
    private TranslateTransform HomeShimmerTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["HomeShimmerTranslate"];
    private ScaleTransform HomeLogoScale => (ScaleTransform)ProductionNames.RegistryFor(this)["HomeLogoScale"];
    private SolidColorBrush HomeNameBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["HomeNameBrush"];
    private TranslateTransform AwayHalfTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayHalfTranslate"];
    private SolidColorBrush AwayBgBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayBgBrush"];
    private TranslateTransform AwayShimmerTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["AwayShimmerTranslate"];
    private ScaleTransform AwayLogoScale => (ScaleTransform)ProductionNames.RegistryFor(this)["AwayLogoScale"];
    private SolidColorBrush AwayNameBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["AwayNameBrush"];
    private BlurEffect DiagonalGlowBlur => (BlurEffect)ProductionNames.RegistryFor(this)["DiagonalGlowBlur"];
    private DropShadowEffect VsGlow => (DropShadowEffect)ProductionNames.RegistryFor(this)["VsGlow"];
    private ScaleTransform VsScale => (ScaleTransform)ProductionNames.RegistryFor(this)["VsScale"];
}
}
