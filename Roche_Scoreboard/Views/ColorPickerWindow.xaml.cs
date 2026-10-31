using KeyEventArgs=System.Windows.Input.KeyEventArgs;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Roche_Scoreboard.Services;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class ColorPickerWindow : Window
{
    private const int WheelSize = 260;
    private const double WheelRadius = 128;
    private bool _updating;
    private bool _dragging;
    private double _hue;
    private double _saturation;
    private double _value;
    private Action<Color>? _livePreview;
    public Color SelectedColor { get; private set; }

    private ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        SharedSurfaceMotion.Attach(PickerSurface);
        ColourField.GotKeyboardFocus += (_, _) => { ColourMarker.Stroke = new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF)); ColourMarker.StrokeThickness = 4; };
        ColourField.LostKeyboardFocus += (_, _) => { ColourMarker.Stroke = Brushes.White; ColourMarker.StrokeThickness = 3; };
        Closed += (_, _) => { _dragging = false; if (ColourField.IsMouseCaptured) ColourField.ReleaseMouseCapture(); };
        MaxHeight = Math.Max(560, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(760, MaxHeight);
        SelectedColor = initial;
        RgbToHsv(initial, out _hue, out _saturation, out _value);
        Loaded += (_, _) =>
        {
            ColourWheelImage.Source = BuildColourWheel();
            PopulateSwatches();
            RefreshFieldAndSelection();
            // The primary action keeps its established template and press/focus
            // behavior, with a solid face beneath its dark ink. Secondary
            // controls and the picker shell retain their glass material.
            // Preserve this template's authored primary face as a concrete
            // brush across platform brush implementations.
            UseColourButton.Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF7));
            UseColourButton.ApplyTemplate();
            if (UseColourButton.Template.FindName("Bd", UseColourButton) is OperatorGlassSurface face)
                face.Material = GlassMaterial.Opaque;
        };
    }

    public static bool TryPick(Window? owner, Color initial, out Color selected, Action<Color>? livePreview = null)
    {
        var dialog = new ColorPickerWindow(initial) { _livePreview = livePreview };
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        bool accepted = dialog.ShowDialog() == true;
        selected = accepted ? dialog.SelectedColor : initial;
        if (!accepted) livePreview?.Invoke(initial);
        return accepted;
    }

    private void ColourField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColourField.Focus();
        _dragging = true;
        ColourField.CaptureMouse();
        UpdateFromPoint(e.GetPosition(ColourField));
    }

    private void ColourField_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed) UpdateFromPoint(e.GetPosition(ColourField));
    }

    private void ColourField_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        UpdateFromPoint(e.GetPosition(ColourField));
        _dragging = false;
        ColourField.ReleaseMouseCapture();
    }

    private void ColourField_LostMouseCapture(object sender, MouseEventArgs e) => _dragging = false;

    private void ColourField_KeyDown(object sender, KeyEventArgs e)
    {
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: _hue = (_hue - step + 360) % 360; break;
            case Key.Right: _hue = (_hue + step) % 360; break;
            case Key.Up: _saturation = Math.Clamp(_saturation + step / 100, 0, 1); break;
            case Key.Down: _saturation = Math.Clamp(_saturation - step / 100, 0, 1); break;
            case Key.PageUp: _value = Math.Clamp(_value + step / 100, 0, 1); break;
            case Key.PageDown: _value = Math.Clamp(_value - step / 100, 0, 1); break;
            default: return;
        }
        e.Handled = true;
        RefreshFieldAndSelection();
    }

    private void UpdateFromPoint(Point point)
    {
        double cx = point.X - WheelSize / 2.0, cy = point.Y - WheelSize / 2.0;
        double radius = Math.Sqrt(cx * cx + cy * cy);
        if (radius > WheelRadius + 3) return;
        _hue = (Math.Atan2(cy, cx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp(radius / WheelRadius, 0, 1);
        RefreshSelection();
    }

    private void FineTuneSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || !IsLoaded) return;
        _saturation = SaturationSlider.Value;
        _value = BrightnessSlider.Value;
        RefreshSelection();
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !IsLoaded) return;
        if (TryParseHex(HexBox.Text, out Color color))
        {
            SetHexValidation(string.Empty);
            SelectedColor = color;
            RgbToHsv(color, out _hue, out _saturation, out _value);
            RefreshFieldAndSelection();
        }
        else SetHexValidation("Enter a six-digit colour such as #0A2A6A.");
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex } || !TryParseHex(hex, out Color color)) return;
        SelectedColor = color;
        RgbToHsv(color, out _hue, out _saturation, out _value);
        RefreshFieldAndSelection();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!IsVisible) return;
        if (!TryParseHex(HexBox.Text, out Color color))
        {
            SetHexValidation("Enter a valid six-digit colour before continuing.");
            HexBox.Focus();
            OperatorViewport.RevealValidationError(HexEntryRegion);
            return;
        }
        SelectedColor = color;
        RecentColorStorage.Record(color);
        DialogResult = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsVisible) return;
        DialogResult = false;
    }

    private void SetHexValidation(string message)
    {
        ValidationText.Text = message;
        System.Windows.Automation.AutomationProperties.SetHelpText(HexBox, string.IsNullOrEmpty(message)
            ? "Enter six hexadecimal digits, with an optional #." : message);
    }

    private void PopulateSwatches()
    {
        string[] defaults = ["#FFFFFF", "#111827", "#E11D48", "#F97316", "#FACC15", "#16A34A", "#2563EB", "#7C3AED", "#EC4899", "#0891B2"];
        foreach (string colour in defaults) DefaultColoursPanel.Children.Add(CreateSwatch(colour));
        foreach (string colour in RecentColorStorage.Load()) RecentColoursPanel.Children.Add(CreateSwatch(colour));
        if (RecentColoursPanel.Children.Count == 0)
            RecentColoursPanel.Children.Add(new TextBlock { Text = "Colours you use will appear here.", Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x93, 0xA8)), FontSize = 11 });
    }

    private Button CreateSwatch(string hex)
    {
        var colour = (Color)ColorConverter.ConvertFromString(hex);
        var button = new Button { Tag = hex, Background = new SolidColorBrush(colour), ToolTip = hex };
        System.Windows.Automation.AutomationProperties.SetName(button, $"Use colour {hex}");
        button.SetResourceReference(StyleProperty, "ColourSwatch");
        button.Click += Swatch_Click;
        return button;
    }

    private void RefreshFieldAndSelection()
    {
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedColor = HsvToRgb(_hue, _saturation, _value);
        _updating = true;
        CurrentPreview.Background = new SolidColorBrush(SelectedColor);
        HexBox.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        SaturationSlider.Value = _saturation;
        BrightnessSlider.Value = _value;
        SetHexValidation(string.Empty);
        _updating = false;
        UpdateSliderBrushes();
        double angle = _hue * Math.PI / 180;
        double markerRadius = _saturation * WheelRadius;
        Canvas.SetLeft(ColourMarker, WheelSize / 2 + Math.Cos(angle) * markerRadius - ColourMarker.Width / 2);
        Canvas.SetTop(ColourMarker, WheelSize / 2 + Math.Sin(angle) * markerRadius - ColourMarker.Height / 2);
        _livePreview?.Invoke(SelectedColor);
    }

    private void UpdateSliderBrushes()
    {
        Color saturationStart = HsvToRgb(_hue, 0, _value);
        Color saturationEnd = HsvToRgb(_hue, 1, _value);
        SaturationSlider.Background = new LinearGradientBrush(saturationStart, saturationEnd, 0);
        Color brightnessEnd = HsvToRgb(_hue, _saturation, 1);
        BrightnessSlider.Background = new LinearGradientBrush(Colors.Black, brightnessEnd, 0);
    }

    private static BitmapSource BuildColourWheel()
    {
        int[] pixels = new int[WheelSize * WheelSize];
        double center = (WheelSize - 1) / 2.0;
        for (int y = 0; y < WheelSize; y++) for (int x = 0; x < WheelSize; x++)
        {
            double dx = x - center, dy = y - center, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > WheelRadius) continue;
            Color c = HsvToRgb((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360,
                Math.Clamp(r / WheelRadius, 0, 1), 1);
            pixels[y * WheelSize + x] = unchecked((int)(0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B));
        }
        return CreateBitmap(WheelSize, WheelSize, pixels);
    }

    private static BitmapSource CreateBitmap(int width, int height, int[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        double chroma = value * saturation, h = hue / 60, x = chroma * (1 - Math.Abs(h % 2 - 1));
        (double r, double g, double b) = h switch { < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x), < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        double m = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * (((b - r) / delta) + 2) : 60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private static bool TryParseHex(string? value, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
