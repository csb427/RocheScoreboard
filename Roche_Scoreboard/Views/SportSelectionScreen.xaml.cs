using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class SportSelectionScreen : UserControl
{
    public event Action<SportMode>? SportSelected;

    // ── Easing curves ──
    private static readonly CubicEase Smooth = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase Overshoot = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
    private static readonly SineEase Sine = new() { EasingMode = EasingMode.EaseInOut };

    // ── Timing ──
    private static readonly TimeSpan HoverIn = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HoverOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FlashDur = TimeSpan.FromMilliseconds(100);

    public SportSelectionScreen()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ═══════════════════════════════════════════════════════
    //  ENTRY ANIMATION — staggered cinematic reveal
    // ═══════════════════════════════════════════════════════

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateChangelog();
        PlayEntryAnimation();
    }

    private void PlayEntryAnimation()
    {
        // Header: fade + slide down
        Animate(HeaderPanel, OpacityProperty, 1, 500, delay: 100, easing: Smooth);
        Animate(HeaderTranslate, TranslateTransform.YProperty, 0, 500, delay: 100, easing: Smooth);

        // AFL card: fade + slide from left
        Animate(AflCard, OpacityProperty, 1, 600, delay: 200);
        Animate(AflCardTranslate, TranslateTransform.XProperty, 0, 700, delay: 200, easing: Smooth);

        // Cricket card: fade + slide from right
        Animate(CricketCard, OpacityProperty, 1, 600, delay: 300);
        Animate(CricketCardTranslate, TranslateTransform.XProperty, 0, 700, delay: 300, easing: Smooth);

        // Divider: grow from centre
        Animate(DividerScale, ScaleTransform.ScaleYProperty, 1, 800, delay: 350, easing: Smooth);
        Animate(DividerGlowLine, OpacityProperty, 0.6, 800, delay: 350);

        // AFL icon: scale up with overshoot + fade
        Animate(AflIconGroup, OpacityProperty, 1, 500, delay: 500);
        Animate(AflIconScale, ScaleTransform.ScaleXProperty, 1, 700, delay: 500, easing: Overshoot);
        Animate(AflIconScale, ScaleTransform.ScaleYProperty, 1, 700, delay: 500, easing: Overshoot);

        // Cricket icon: scale up with overshoot + fade
        Animate(CricketIconGroup, OpacityProperty, 1, 500, delay: 600);
        Animate(CricketIconScale, ScaleTransform.ScaleXProperty, 1, 700, delay: 600, easing: Overshoot);
        Animate(CricketIconScale, ScaleTransform.ScaleYProperty, 1, 700, delay: 600, easing: Overshoot);

        // AFL text: fade + slide up
        Animate(AflTextPanel, OpacityProperty, 1, 500, delay: 700);
        Animate(AflTextTranslate, TranslateTransform.YProperty, 0, 600, delay: 700, easing: Smooth);

        // Cricket text: fade + slide up
        Animate(CricketTextPanel, OpacityProperty, 1, 500, delay: 800);
        Animate(CricketTextTranslate, TranslateTransform.YProperty, 0, 600, delay: 800, easing: Smooth);

        // Hero glows: fade in
        Animate(AflGlow, OpacityProperty, 0.30, 600, delay: 600);
        Animate(CricketGlow, OpacityProperty, 0.30, 600, delay: 700);

        // Bottom accent: last to appear, triggers ambient loop
        var lastAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(500))
        {
            BeginTime = TimeSpan.FromMilliseconds(900)
        };
        lastAnim.Completed += (_, _) => StartAmbientAnimations();
        BottomAccent.BeginAnimation(OpacityProperty, lastAnim);

        // Changelog version badge: fade in
        Animate(ChangelogToggle, OpacityProperty, 0.7, 400, delay: 1000, easing: Smooth);

        // Training mode tile: fade + drop in from top
        Animate(TrainingTile, OpacityProperty, 1, 600, delay: 850, easing: Smooth);
        Animate(TrainingTileTranslate, TranslateTransform.YProperty, 0, 700, delay: 850, easing: Smooth);
    }

    // ═══════════════════════════════════════════════════════
    //  AMBIENT ANIMATIONS — continuous atmosphere
    // ═══════════════════════════════════════════════════════

    private void StartAmbientAnimations()
    {
        // Gentle icon float
        PulseForever(AflIconFloat, TranslateTransform.YProperty, -4, 4, 3200);
        PulseForever(CricketIconFloat, TranslateTransform.YProperty, -4, 4, 3600);

        // Glow pulse
        PulseForever(AflGlow, OpacityProperty, 0.20, 0.45, 2800);
        PulseForever(CricketGlow, OpacityProperty, 0.20, 0.45, 3100);

        // Light beam sweep
        PulseForever(AflBeamTranslate, TranslateTransform.XProperty, -250, 900, 5000);
        PulseForever(CricketBeamTranslate, TranslateTransform.XProperty, -250, 900, 6000);
    }

    // ═══════════════════════════════════════════════════════
    //  HOVER — scale, glow, dim the other half
    // ═══════════════════════════════════════════════════════

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Border card) return;

        bool isAfl = card == AflCard;
        Border glow = isAfl ? AflGlow : CricketGlow;
        Border hover = isAfl ? AflHover : CricketHover;
        ScaleTransform iconScale = isAfl ? AflIconScale : CricketIconScale;
        TranslateTransform iconHover = isAfl ? AflIconHover : CricketIconHover;
        ScaleTransform cardScale = isAfl ? AflCardScale : CricketCardScale;
        Border other = isAfl ? CricketCard : AflCard;

        // Intensify hero glow (overrides ambient pulse)
        glow.BeginAnimation(OpacityProperty, Anim(0.65, HoverIn, Smooth));

        // Subtle white wash
        hover.BeginAnimation(OpacityProperty, Anim(0.05, HoverIn, Smooth));

        // Scale up icon
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.12, HoverIn, Smooth));
        iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.12, HoverIn, Smooth));

        // Float icon upward
        iconHover.BeginAnimation(TranslateTransform.YProperty, Anim(-8, HoverIn, Smooth));

        // Subtle card expansion
        cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.015, HoverIn, Smooth));
        cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.015, HoverIn, Smooth));

        // Dim the other half
        other.BeginAnimation(OpacityProperty, Anim(0.35, HoverIn, Smooth));
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border card) return;

        bool isAfl = card == AflCard;
        Border glow = isAfl ? AflGlow : CricketGlow;
        Border hover = isAfl ? AflHover : CricketHover;
        ScaleTransform iconScale = isAfl ? AflIconScale : CricketIconScale;
        TranslateTransform iconHover = isAfl ? AflIconHover : CricketIconHover;
        ScaleTransform cardScale = isAfl ? AflCardScale : CricketCardScale;
        Border other = isAfl ? CricketCard : AflCard;

        // Smoothly transition back to ambient glow pulse
        RestoreAmbientGlow(glow, isAfl);

        // Remove white wash
        hover.BeginAnimation(OpacityProperty, Anim(0, HoverOut, Smooth));

        // Restore icon scale
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.0, HoverOut, Smooth));
        iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.0, HoverOut, Smooth));

        // Drop icon back
        iconHover.BeginAnimation(TranslateTransform.YProperty, Anim(0, HoverOut, Smooth));

        // Restore card scale
        cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.0, HoverOut, Smooth));
        cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1.0, HoverOut, Smooth));

        // Restore the other half
        other.BeginAnimation(OpacityProperty, Anim(1.0, HoverOut, Smooth));
    }

    private void RestoreAmbientGlow(Border glow, bool isAfl)
    {
        int periodMs = isAfl ? 2800 : 3100;
        var transition = new DoubleAnimation(0.30, HoverOut) { EasingFunction = Smooth };
        transition.Completed += (_, _) =>
            PulseForever(glow, OpacityProperty, 0.20, 0.45, periodMs);
        glow.BeginAnimation(OpacityProperty, transition);
    }

    // ═══════════════════════════════════════════════════════
    //  CLICK — punch + flash → raise event
    // ═══════════════════════════════════════════════════════

    private void AFL_Click(object sender, MouseButtonEventArgs e)
    {
        PlayClickFlash(AflFlash, AflIconScale, () => SportSelected?.Invoke(SportMode.AFL));
    }

    private void Cricket_Click(object sender, MouseButtonEventArgs e)
    {
        PlayClickFlash(CricketFlash, CricketIconScale, () => SportSelected?.Invoke(SportMode.Cricket));
    }

    private void Training_Click(object sender, MouseButtonEventArgs e)
    {
        // Quick scale-pulse + glow flare, then raise the selected event.
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.08, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.08, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });
        TrainingTileGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.95, 0.4, TimeSpan.FromMilliseconds(380)) { EasingFunction = Smooth });

        var raise = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        raise.Tick += (_, _) =>
        {
            raise.Stop();
            SportSelected?.Invoke(SportMode.Training);
        };
        raise.Start();
    }

    private void TrainingTile_MouseEnter(object sender, MouseEventArgs e)
    {
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.04, HoverIn) { EasingFunction = Smooth });
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.04, HoverIn) { EasingFunction = Smooth });
        TrainingTileGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.75, HoverIn) { EasingFunction = Smooth });
    }

    private void TrainingTile_MouseLeave(object sender, MouseEventArgs e)
    {
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.0, HoverOut) { EasingFunction = Smooth });
        TrainingTileScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.0, HoverOut) { EasingFunction = Smooth });
        TrainingTileGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.4, HoverOut) { EasingFunction = Smooth });
    }

    private static void PlayClickFlash(Border flash, ScaleTransform iconScale, Action onComplete)
    {
        // Icon punch
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });
        iconScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Smooth });

        // White flash
        var fadeIn = new DoubleAnimation(0.2, FlashDur) { EasingFunction = Smooth };
        fadeIn.Completed += (_, _) =>
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) => onComplete();
            flash.BeginAnimation(OpacityProperty, fadeOut);
        };
        flash.BeginAnimation(OpacityProperty, fadeIn);
    }

    // ═══════════════════════════════════════════════════════
    //  CHANGELOG — populate and hover expand/collapse
    // ═══════════════════════════════════════════════════════

    private bool _changelogOpen;

    // ── Tag colour mapping for changelog entries ──
    private static readonly Dictionary<string, (string bg, string fg, string border)> TagColors = new()
    {
        ["New"]      = ("#0D2818", "#3FB950", "#1A4028"),
        ["Improved"] = ("#0D1A30", "#58A6FF", "#1A3050"),
        ["Fixed"]    = ("#2D1A0D", "#F0C040", "#3D2A1A"),
        ["Changed"]  = ("#1A0D2D", "#C084FC", "#2A1A3D"),
    };

    private void PopulateChangelog()
    {
        VersionLabel.Text = $"v{AutoUpdateService.CurrentVersion.ToString(3)}";
        ChangelogEntries.Children.Clear();

        bool isFirst = true;
        foreach (var ver in ChangelogService.Versions)
        {
            if (!isFirst)
                ChangelogEntries.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 12, 0, 12),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C2838"))
                });
            isFirst = false;

            // Version header row
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            var versionBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A58A6FF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33579A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 4, 12, 4)
            };
            versionBadge.Child = new TextBlock
            {
                Text = $"v{ver.Version}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF")),
                FontSize = 15,
                FontWeight = FontWeights.Black,
                FontFamily = new System.Windows.Media.FontFamily("Bahnschrift")
            };
            header.Children.Add(versionBadge);

            header.Children.Add(new TextBlock
            {
                Text = ver.Date,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6E7681")),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });

            ChangelogEntries.Children.Add(header);

            // Change items — parse [Tag] prefix into coloured badge
            foreach (string change in ver.Changes)
            {
                var item = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(6, 0, 0, 5)
                };

                string text = change;
                string? tag = null;

                // Extract [Tag] prefix if present
                if (text.StartsWith('['))
                {
                    int end = text.IndexOf(']');
                    if (end > 1)
                    {
                        tag = text[1..end];
                        text = text[(end + 1)..].TrimStart();
                    }
                }

                if (tag is not null && TagColors.TryGetValue(tag, out var colors))
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.bg)),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.border)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(7, 2, 7, 2),
                        Margin = new Thickness(0, 1, 10, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    badge.Child = new TextBlock
                    {
                        Text = tag.ToUpperInvariant(),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.fg)),
                        FontSize = 10,
                        FontWeight = FontWeights.Black,
                        FontFamily = new System.Windows.Media.FontFamily("Bahnschrift")
                    };
                    item.Children.Add(badge);
                }
                else
                {
                    // No tag — bullet point
                    item.Children.Add(new TextBlock
                    {
                        Text = "•",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#484F58")),
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    });
                }

                item.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9D1D9")),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 660
                });

                ChangelogEntries.Children.Add(item);
            }
        }
    }

    private void ChangelogBar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_changelogOpen)
            ShowChangelog();
    }

    private void ChangelogBar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_changelogOpen)
            HideChangelog();
    }

    private void ShowChangelog()
    {
        _changelogOpen = true;

        // Fade out the small toggle pill
        Animate(ChangelogToggle, OpacityProperty, 0, 150, easing: Smooth);

        // Expand and show the full panel
        Animate(ChangelogPanel, OpacityProperty, 1, 250, delay: 100, easing: Smooth);
        var expandAnim = new DoubleAnimation(560, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(50),
            EasingFunction = Smooth
        };
        ChangelogPanel.BeginAnimation(FrameworkElement.MaxHeightProperty, expandAnim);
    }

    private void HideChangelog()
    {
        _changelogOpen = false;

        // Collapse the panel
        var collapseAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = Smooth
        };
        ChangelogPanel.BeginAnimation(FrameworkElement.MaxHeightProperty, collapseAnim);

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Smooth };
        ChangelogPanel.BeginAnimation(OpacityProperty, fadeOut);

        // Restore the toggle pill
        Animate(ChangelogToggle, OpacityProperty, 0.7, 300, delay: 200, easing: Smooth);
    }

    // ═══════════════════════════════════════════════════════
    //  ANIMATION HELPERS
    // ═══════════════════════════════════════════════════════

    private static DoubleAnimation Anim(double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        return new DoubleAnimation(to, duration) { EasingFunction = easing };
    }

    private static void Animate(IAnimatable target, DependencyProperty prop,
        double to, int durationMs, int delay = 0, IEasingFunction? easing = null)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = delay > 0 ? TimeSpan.FromMilliseconds(delay) : null,
            EasingFunction = easing
        };
        target.BeginAnimation(prop, anim);
    }

    private static void PulseForever(IAnimatable target, DependencyProperty prop,
        double from, double to, int periodMs)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(periodMs))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = Sine
        };
        target.BeginAnimation(prop, anim);
    }
}
