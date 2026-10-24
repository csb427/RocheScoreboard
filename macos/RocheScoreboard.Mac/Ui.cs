using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RocheScoreboard.Mac;

internal static class Ui
{
    public static readonly IBrush Background = Brush.Parse("#081629");
    public static readonly IBrush Surface = Brush.Parse("#0B1D3A");
    public static readonly IBrush Border = Brush.Parse("#263C58");
    public static readonly IBrush TextColor = Brush.Parse("#F7F9FC");
    public static readonly IBrush Secondary = Brush.Parse("#A8B6C8");
    public static readonly IBrush Accent = Brush.Parse("#0A57FF");
    public static readonly IBrush Success = Brush.Parse("#22C88A");

    public static TextBlock Text(string text, double size = 14, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? TextColor,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    public static TextBlock Heading(string text) => new()
    { Text = text, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = TextColor, Margin = new Thickness(0, 8, 0, 8) };

    public static StackPanel Column(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }

    public static WrapPanel Row(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 8);
            panel.Children.Add(control);
        }
        return panel;
    }

    public static Button Button(string text, Action action, string? name = null, bool primary = false)
    {
        var button = new Button { Content = text, Name = name, MinHeight = 40, Padding = new Thickness(16, 10) };
        if (primary) { button.Background = Accent; button.Foreground = Brushes.White; }
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBox Input(string value = "", string? placeholder = null, double width = 220) => new()
    { Text = value, Watermark = placeholder, Width = width, MinHeight = 36 };

    public static NumericUpDown Number(decimal value, decimal min = 0, decimal max = 1440, double width = 120) => new()
    { Value = value, Minimum = min, Maximum = max, Increment = 1, Width = width, FormatString = "0" };

    public static StackPanel Field(string label, Control input) => Column(Text(label, 12, Secondary), input);

    public static Border Card(Control child) => new()
    {
        Background = Surface, BorderBrush = Border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Child = child
    };

    public static ScrollViewer Scroll(Control content) => new()
    { Content = content, Padding = new Thickness(20), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    public static IBrush SafeBrush(string? value, IBrush? fallback = null) =>
        Color.TryParse(value, out var color) ? new SolidColorBrush(color) : fallback ?? Accent;

    public static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && Color.TryParse(value, out _);
}
