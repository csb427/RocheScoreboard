using System.Windows;

namespace Roche_Scoreboard.Services;

/// <summary>Navigation hints are opt-in. Does not disable field validation or match alerts.</summary>
public static class OperatorGuidance
{
    public static readonly DependencyProperty ShowHintsProperty=DependencyProperty.RegisterAttached("ShowHints",typeof(bool),typeof(OperatorGuidance),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetShowHints(DependencyObject target)=>(bool)target.GetValue(ShowHintsProperty);
    public static void SetShowHints(DependencyObject target,bool value)=>target.SetValue(ShowHintsProperty,value);
}
