using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Roche_Scoreboard.Controls;

/// <summary>
/// A single-line text presenter that horizontally scrolls (marquee) its text
/// when the rendered width exceeds the available width. When the text fits,
/// it behaves like a normal centered/aligned TextBlock.
/// </summary>
public class MarqueeTextBlock : ContentControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnVisualChanged));

    public new static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure, OnVisualChanged));

    public static readonly DependencyProperty FontFamilyOverrideProperty =
        DependencyProperty.Register(nameof(FontFamilyOverride), typeof(FontFamily), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnVisualChanged));

    public static readonly DependencyProperty SpeedProperty =
        DependencyProperty.Register(nameof(Speed), typeof(double), typeof(MarqueeTextBlock),
            new PropertyMetadata(60.0)); // pixels per second

    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(nameof(Gap), typeof(double), typeof(MarqueeTextBlock),
            new PropertyMetadata(40.0));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public new FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontFamily? FontFamilyOverride
    {
        get => (FontFamily?)GetValue(FontFamilyOverrideProperty);
        set => SetValue(FontFamilyOverrideProperty, value);
    }

    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private TextBlock? _primary;
    private TextBlock? _secondary;
    private Canvas? _canvas;
    private TranslateTransform? _xform;
    private Storyboard? _storyboard;

    public MarqueeTextBlock()
    {
        ClipToBounds = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => Rebuild();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock m) m.Rebuild();
    }

    private void Rebuild()
    {
        if (ActualWidth <= 0) return;

        _storyboard?.Stop(this);
        _storyboard = null;

        _canvas ??= new Canvas { ClipToBounds = true };
        Content = _canvas;
        _canvas.Children.Clear();

        var fam = FontFamilyOverride ?? FontFamily;
        _primary = new TextBlock
        {
            Text = Text ?? string.Empty,
            FontFamily = fam,
            FontWeight = FontWeight,
            FontSize = FontSize,
            Foreground = Foreground,
            TextWrapping = TextWrapping.NoWrap,
        };
        _primary.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = _primary.DesiredSize.Width;
        double textHeight = _primary.DesiredSize.Height;

        if (textWidth <= ActualWidth + 0.5)
        {
            // Fits: center horizontally, no animation.
            double x = Math.Max(0, (ActualWidth - textWidth) / 2.0);
            Canvas.SetLeft(_primary, x);
            Canvas.SetTop(_primary, Math.Max(0, (ActualHeight - textHeight) / 2.0));
            _canvas.Children.Add(_primary);
            return;
        }

        // Overflow: marquee with a second copy for seamless looping.
        _secondary = new TextBlock
        {
            Text = _primary.Text,
            FontFamily = _primary.FontFamily,
            FontWeight = _primary.FontWeight,
            FontSize = _primary.FontSize,
            Foreground = _primary.Foreground,
            TextWrapping = TextWrapping.NoWrap,
        };

        _xform = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(_xform);

        var holder = new Canvas
        {
            Width = (textWidth + Gap) * 2,
            Height = textHeight,
            RenderTransform = _xform,
        };
        Canvas.SetLeft(_primary, 0);
        Canvas.SetTop(_primary, 0);
        Canvas.SetLeft(_secondary, textWidth + Gap);
        Canvas.SetTop(_secondary, 0);
        holder.Children.Add(_primary);
        holder.Children.Add(_secondary);

        Canvas.SetTop(holder, Math.Max(0, (ActualHeight - textHeight) / 2.0));
        _canvas.Children.Add(holder);

        double distance = textWidth + Gap;
        double seconds = Math.Max(1.0, distance / Math.Max(10, Speed));
        var anim = new DoubleAnimation
        {
            From = 0,
            To = -distance,
            Duration = TimeSpan.FromSeconds(seconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(anim, holder);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
        _storyboard = new Storyboard();
        _storyboard.Children.Add(anim);
        _storyboard.Begin(this, true);
    }
}
