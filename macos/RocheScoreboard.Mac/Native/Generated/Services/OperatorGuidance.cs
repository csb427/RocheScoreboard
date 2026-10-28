// Generated from Services/OperatorGuidance.cs; edit translator/adapters.
// Source SHA-256: 354404ad3e496f11e8ce286856b9ec0839d908e9df0d9fbc6901b827ba33279d
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Services;

/// <summary>Navigation hints are opt-in. Does not disable field validation or match alerts.</summary>
public static class OperatorGuidance
{
    public static readonly AttachedProperty<bool> ShowHintsProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("ShowHints",typeof(bool),typeof(OperatorGuidance),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetShowHints(AvaloniaObject target)=>(bool)target.GetValue(ShowHintsProperty);
    public static void SetShowHints(AvaloniaObject target,bool value)=>target.SetValue(ShowHintsProperty,value);
}
