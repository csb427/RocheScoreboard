using System.Globalization;
using System.Windows.Data;

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
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
}
