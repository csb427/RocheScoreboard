// Generated from Services/OperatorDesign.cs; edit translator/adapters.
// Source SHA-256: 4929c360d2620b118e278c181ce89f7ef81ed8c3e0e7c953f5e0cad56fcf934e
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;
using System.Runtime.CompilerServices;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using UserControl = Avalonia.Controls.UserControl;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard.Services;

/// <summary>Opt-in operator resources. Never merged into Application or a public display.</summary>
public static class OperatorDesign
{
    static OperatorDesign()
    {
        NativeSystemPreferences.StaticPropertyChanged += (_, args) =>
        {
            if(args.PropertyName != nameof(NativeSystemPreferences.HighContrast))return;
            var dispatcher = Dispatcher.UIThread;
            if(dispatcher is null)return;
            dispatcher.InvokeAsync(new Action(() => UpdateTheme(_theme, force: true)));
        };
    }
    public static readonly AttachedProperty<bool> IsEnabledProperty = (AttachedProperty<bool>)NativeProperty.RegisterAttached("IsEnabled",typeof(bool), typeof(OperatorDesign), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits, Changed));
    public static bool GetIsEnabled(AvaloniaObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(AvaloniaObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    private static readonly AttachedProperty<OperatorSceneBackdrop> BackdropProperty = (AttachedProperty<OperatorSceneBackdrop>)NativeProperty.RegisterAttached("Backdrop", typeof(OperatorSceneBackdrop), typeof(OperatorDesign), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    internal static OperatorSceneBackdrop? GetBackdrop(AvaloniaObject target) => target.GetValue(BackdropProperty) as OperatorSceneBackdrop;
    private static readonly ConditionalWeakTable<Control, object> Roots = new();
    private static readonly List<WeakReference<Control>> Registered = new();
    private static UiDensity _density = UiDensity.Comfortable;
    private static AppTheme _theme = AppTheme.Dark;
    private static bool _showHints;
    internal static void UpdateGuidance(bool show)
    {
        _showHints=show;
        foreach(var entry in Registered)if(entry.TryGetTarget(out var root))OperatorGuidance.SetShowHints(root,show);
    }
    private static void Changed(AvaloniaObject target, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true && target is Control root &&
            root.ReadLocalValue(IsEnabledProperty) is true) Apply(root);
    }
    internal static void Apply(Control root)
    {
        if (Roots.TryGetValue(root, out _)) return;
        Roots.Add(root, new object());
        OperatorInputFocus.Install(root);
        if (!GetIsEnabled(root)) SetIsEnabled(root, true);
        NativeAssets.EnsureDictionary(root,"ControlCentre");
        Registered.Add(new(root));
        OperatorGuidance.SetShowHints(root,_showHints);
        root.SetResourceReference(TextBlock.FontFamilyProperty, "ControlCentre.Font");
        SetDensity(root, _density);
        SetTheme(root, _theme);
        // XAML can assign Resources after attached root attributes. Reapply against
        // the final dictionary, including when a view is unloaded and reopened.
        root.Loaded += (_, _) => { SetDensity(root, _density); SetTheme(root, _theme); if(root is Window window)OperatorWindowChrome.Enable(window); InstallBackdrop(root); };
        if(root.IsLoaded)InstallBackdrop(root);
    }
    private static void InstallBackdrop(Control root)
    {
        // Integrated sheets/menus inherit a scene; do not inject one inside their glass.
        object? content = root switch { Window window when window is MainWindow || window.GetType().Namespace == "Roche_Scoreboard.Views" => window.Content, UserControl control => control.Content, _ => null };
        Grid? stage = content as Grid ?? (content as ScrollViewer)?.Content as Grid;
        if(stage is null)return;
        var scene = stage.Children.OfType<OperatorSceneBackdrop>().FirstOrDefault();
        if(scene is null)
        {
            scene = new OperatorSceneBackdrop();
            NativePanel.SetZIndex(scene,int.MinValue);
            Grid.SetRowSpan(scene,Math.Max(1,stage.RowDefinitions.Count)); Grid.SetColumnSpan(scene,Math.Max(1,stage.ColumnDefinitions.Count));
            stage.Children.Insert(0,scene);
        }
        root.SetValue(BackdropProperty,scene);
    }
    internal static void UpdateDensity(UiDensity density)
    {
        if (_density == density) return;
        if (_density != density)
        {
            // Capture once per visible operator window, never once per descendant.
            var windows = new HashSet<Window>();
            foreach (var entry in Registered)
                if (entry.TryGetTarget(out var root) && root.IsVisible && NativeWindow.GetWindow(root) is { } window && windows.Add(window) && window.Content is Control content)
                    OperatorDensityTransition.Begin(content);
        }
        _density = density;
        for (int i = Registered.Count - 1; i >= 0; --i)
            if (Registered[i].TryGetTarget(out var root)) SetDensity(root, density);
            else Registered.RemoveAt(i);
    }
    internal static void UpdateTheme(AppTheme theme, bool force = false)
    {
        // Apply/Loaded initialize each root directly. Repeating an accepted
        // global policy must not rebuild resources throughout every view.
        // System High Contrast changes explicitly force the palette refresh.
        if (!force && _theme == theme) return;
        _theme = theme;
        for (int i = Registered.Count - 1; i >= 0; --i)
            if (Registered[i].TryGetTarget(out var root)) SetTheme(root, theme);
            else Registered.RemoveAt(i);
    }
    private static void SetTheme(Control root, AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        foreach (var (key, hex) in new[]
        {
            ("Canvas", dark ? "#09090B" : "#EFEFF3"), ("Surface", dark ? "#19191C" : "#FAFAFC"),
            ("Raised", dark ? "#29292D" : "#E1E1E8"), ("Text", dark ? "#FAFAFC" : "#19191E"),
            ("SecondaryText", dark ? "#B6B6C0" : "#545461"), ("Border", dark ? "#44444B" : "#858590"),
            ("Selection", dark ? "#253D55" : "#D0E5FF"), ("Accent", dark ? "#7BB8FF" : "#145699"),
            ("FinalsInk", dark ? "#E9BF5C" : "#523B0C"), ("FinalsSelectedInk", dark ? "#FFE6A3" : "#30210A"),
            ("HeaderHomeInk", dark ? "#CEE8FF" : "#145699"), ("HeaderAwayInk", dark ? "#FFD9D2" : "#A12620")
        }) root.Resources["ControlCentre." + key + "Brush"] = (SolidColorBrush)new NativeBrushConverter().ConvertFromString(hex)!;
        root.Resources["ControlCentre.SelectedTextBrush"] = root.Resources["ControlCentre.TextBrush"];
        root.Resources["ControlCentre.SegmentSelectedInkBrush"] = Avalonia.Media.Brushes.Black;
        root.Resources["ControlCentre.TabSelectedInkBrush"] = (SolidColorBrush)new NativeBrushConverter().ConvertFromString(dark ? "#ACD7FF" : "#104B7B")!;
        root.Resources["ControlCentre.GlassBrush"] = dark
            ? NativeBrush.Linear(Avalonia.Media.Color.FromArgb(105, 24, 26, 32), Avalonia.Media.Color.FromArgb(135, 9, 11, 16), 90)
            : NativeBrush.Linear(Avalonia.Media.Color.FromArgb(165, 250, 252, 255), Avalonia.Media.Color.FromArgb(180, 224, 233, 245), 90);
        GetBackdrop(root)?.InvalidateVisual();
        if (NativeSystemPreferences.HighContrast) ApplyContrast(root);
    }
    private static void SetDensity(Control root, UiDensity density)
    {
        bool compact = density == UiDensity.Compact;
        root.Resources["ControlCentre.BodySize"] = compact ? 14d : 15d;
        root.Resources["ControlCentre.CaptionSize"] = compact ? 12d : 13d;
        root.Resources["ControlCentre.ControlHeight"] = compact ? 36d : 42d;
        root.Resources["ControlCentre.ControlPadding"] = compact ? new Thickness(12, 7, 12, 7) : new Thickness(16, 10, 16, 10);
    }
    private static void ApplyContrast(Control root)
    {
        root.Resources["ControlCentre.CanvasBrush"] = SystemColors.WindowBrush;
        root.Resources["ControlCentre.SurfaceBrush"] = SystemColors.WindowBrush;
        root.Resources["ControlCentre.TextBrush"] = SystemColors.WindowTextBrush;
        root.Resources["ControlCentre.SecondaryTextBrush"] = SystemColors.WindowTextBrush;
        root.Resources["ControlCentre.BorderBrush"] = SystemColors.WindowTextBrush;
        root.Resources["ControlCentre.RaisedBrush"] = SystemColors.WindowBrush;
        root.Resources["ControlCentre.AccentBrush"] = SystemColors.HighlightBrush;
        root.Resources["ControlCentre.SelectionBrush"] = SystemColors.HighlightBrush;
        root.Resources["ControlCentre.SelectedTextBrush"] = SystemColors.HighlightTextBrush;
        root.Resources["ControlCentre.SegmentSelectedInkBrush"] = SystemColors.HighlightTextBrush;
        root.Resources["ControlCentre.TabSelectedInkBrush"] = SystemColors.HighlightTextBrush;
        root.Resources["ControlCentre.FinalsInkBrush"] = SystemColors.WindowTextBrush;
        root.Resources["ControlCentre.FinalsSelectedInkBrush"] = SystemColors.HighlightTextBrush;
        root.Resources["ControlCentre.HeaderHomeInkBrush"] = SystemColors.WindowTextBrush;
        root.Resources["ControlCentre.HeaderAwayInkBrush"] = SystemColors.WindowTextBrush;
    }
}
