using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using RocheScoreboard.Mac.Native;

namespace Roche_Scoreboard.Controls;

// Native property registration; layout and seamless loop below retain the
// production control's measurement, speed, gap, duplicate text and centering.
public class MarqueeTextBlock : ContentControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MarqueeTextBlock, string>(nameof(Text), "");
    public static readonly StyledProperty<FontFamily?> FontFamilyOverrideProperty = AvaloniaProperty.Register<MarqueeTextBlock, FontFamily?>(nameof(FontFamilyOverride));
    public static readonly StyledProperty<double> SpeedProperty = AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Speed), 60);
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Gap), 40);
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily? FontFamilyOverride { get => GetValue(FontFamilyOverrideProperty); set => SetValue(FontFamilyOverrideProperty, value); }
    public double Speed { get => GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }
    protected override Type StyleKeyOverride => typeof(ContentControl);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontFamilyOverrideProperty || change.Property == FontWeightProperty || change.Property == FontSizeProperty || change.Property == SpeedProperty || change.Property == GapProperty || change.Property == ForegroundProperty) Rebuild();
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
        Unloaded += (_, _) => _storyboard?.Stop();
    }

    private void Rebuild()
    {
        if (Bounds.Width <= 0) return;

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

        if (textWidth <= Bounds.Width + 0.5)
        {
            // Fits: center horizontally, no animation.
            double x = Math.Max(0, (Bounds.Width - textWidth) / 2.0);
            Canvas.SetLeft(_primary, x);
            Canvas.SetTop(_primary, Math.Max(0, (Bounds.Height - textHeight) / 2.0));
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

        Canvas.SetTop(holder, Math.Max(0, (Bounds.Height - textHeight) / 2.0));
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
