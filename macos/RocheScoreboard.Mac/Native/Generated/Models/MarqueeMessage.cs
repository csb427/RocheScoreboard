// Generated from Models/MarqueeMessage.cs; edit translator/adapters.
// Source SHA-256: c1c9af8fe4f65e957dab5ec1e572ab90b3f60290da2697158a37e1c6036fd06d
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Brush = Avalonia.Media.IBrush;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;

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
    private bool _isEnabled;

    public MarqueeMessage() { }

    public MarqueeMessage(string text, string? textColor = null, string? highlightColor = null, bool isEnabled = true)
    {
        _text = text ?? string.Empty;
        _textColor = textColor;
        _highlightColor = highlightColor;
        _isEnabled = isEnabled;
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

    /// <summary>Whether this preset slot is currently included in the live ticker rotation.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnChanged(); } }
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
        return string.Equals(Text, (other.Text ?? string.Empty), StringComparison.Ordinal)
            && string.Equals(TextColor ?? string.Empty, other.TextColor ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(HighlightColor ?? string.Empty, other.HighlightColor ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && IsEnabled == other.IsEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
