using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Roche_Scoreboard.Models;

/// <summary>
/// A scrolling ticker message with optional per-message text and highlight colors.
/// Colors are hex strings (e.g. "#FFFFFF") or null/empty for defaults.
/// Implements <see cref="INotifyPropertyChanged"/> so per-row controls in the
/// operator UI update the live preview without a converter.
/// </summary>
public sealed class MarqueeMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string? _textColor;
    private string? _highlightColor;

    public MarqueeMessage() { }

    public MarqueeMessage(string text, string? textColor = null, string? highlightColor = null)
    {
        _text = text ?? string.Empty;
        _textColor = textColor;
        _highlightColor = highlightColor;
    }

    public string Text
    {
        get => _text;
        set { if (!string.Equals(_text, value, StringComparison.Ordinal)) { _text = value ?? string.Empty; OnChanged(); } }
    }

    public string? TextColor
    {
        get => _textColor;
        set
        {
            if (!string.Equals(_textColor ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
            {
                _textColor = value;
                OnChanged();
                OnChanged(nameof(TextBrush));
            }
        }
    }

    public string? HighlightColor
    {
        get => _highlightColor;
        set
        {
            if (!string.Equals(_highlightColor ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
            {
                _highlightColor = value;
                OnChanged();
                OnChanged(nameof(HighlightBrush));
            }
        }
    }

    public Brush TextBrush => ParseBrush(_textColor, Brushes.White);
    public Brush HighlightBrush => ParseBrush(_highlightColor, Brushes.Transparent);

    private static Brush ParseBrush(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch
        {
            return fallback;
        }
    }

    public bool ContentEquals(MarqueeMessage? other)
    {
        if (other is null) return false;
        return string.Equals(Text, other.Text, StringComparison.Ordinal)
            && string.Equals(TextColor ?? string.Empty, other.TextColor ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(HighlightColor ?? string.Empty, other.HighlightColor ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
