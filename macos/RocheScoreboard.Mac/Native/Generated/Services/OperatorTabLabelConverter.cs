// Generated from Services/OperatorTabLabelConverter.cs; edit translator/adapters.
// Source SHA-256: 5b55fbda19473de1c8b60669f20fe9fa87eb259eb000f4b337d4c7ee55ef0e68
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Globalization;
using Avalonia.Data;

namespace Roche_Scoreboard.Services;

/// <summary>Keep existing tab IDs and shortcuts; show concise, sentence-case navigation.</summary>
public sealed class OperatorTabLabelConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        string text=value?.ToString()?.Trim()??string.Empty;
        int start=0;while(start<text.Length && (char.IsDigit(text[start]) || char.IsWhiteSpace(text[start])))start++;
        if(start>0 && start<text.Length)text=text[start..];
        if(text.Length==0)return text;
        return char.ToUpper(text[0],culture)+text[1..].ToLower(culture);
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Avalonia.Data.BindingOperations.DoNothing;
}
