using SystemColors = System.Windows.SystemColors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using Control = System.Windows.Controls.Control;
using UserControl = System.Windows.Controls.UserControl;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard.Services;

/// <summary>Opt-in operator resources. Never merged into Application or a public display.</summary>
public static class OperatorDesign
{
    static OperatorDesign()
    {
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if(args.PropertyName != nameof(SystemParameters.HighContrast))return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if(dispatcher is null || dispatcher.HasShutdownStarted)return;
            dispatcher.BeginInvoke(new Action(() => UpdateTheme(_theme)));
        };
    }
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(OperatorDesign), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits, Changed));
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    private static readonly DependencyProperty BackdropProperty = DependencyProperty.RegisterAttached("Backdrop", typeof(OperatorSceneBackdrop), typeof(OperatorDesign), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    internal static OperatorSceneBackdrop? GetBackdrop(DependencyObject target) => target.GetValue(BackdropProperty) as OperatorSceneBackdrop;
    private static readonly ConditionalWeakTable<FrameworkElement, object> Roots = new();
    private static readonly List<WeakReference<FrameworkElement>> Registered = new();
    private static UiDensity _density = UiDensity.Comfortable;
    private static AppTheme _theme = AppTheme.Dark;
    private static bool _showHints;
    internal static void UpdateGuidance(bool show)
    {
        _showHints=show;
        foreach(var entry in Registered)if(entry.TryGetTarget(out var root))OperatorGuidance.SetShowHints(root,show);
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is true && target is FrameworkElement root &&
            root.ReadLocalValue(IsEnabledProperty) is true) Apply(root);
    }
    internal static void Apply(FrameworkElement root)
    {
        if (Roots.TryGetValue(root, out _)) return;
        Roots.Add(root, new object());
        OperatorInputFocus.Install(root);
        if (!GetIsEnabled(root)) SetIsEnabled(root, true);
        if (!root.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("ControlCentre.xaml", StringComparison.Ordinal) == true))
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Roche Scoreboard;component/Resources/ControlCentre.xaml", UriKind.Relative) });
        Registered.Add(new(root));
        OperatorGuidance.SetShowHints(root,_showHints);
        root.SetResourceReference(Control.FontFamilyProperty, "ControlCentre.Font");
        SetDensity(root, _density);
        SetTheme(root, _theme);
        // XAML can assign Resources after attached root attributes. Reapply against
        // the final dictionary, including when a view is unloaded and reopened.
        root.Loaded += (_, _) => { SetDensity(root, _density); SetTheme(root, _theme); if(root is Window window)OperatorWindowChrome.Enable(window); InstallBackdrop(root); };
        if(root.IsLoaded)InstallBackdrop(root);
    }
    private static void InstallBackdrop(FrameworkElement root)
    {
        // Integrated sheets/menus inherit a scene; do not inject one inside their glass.
        object? content = root switch { Window window when window is MainWindow || window.GetType().Namespace == "Roche_Scoreboard.Views" => window.Content, UserControl control => control.Content, _ => null };
        Grid? stage = content as Grid ?? (content as ScrollViewer)?.Content as Grid;
        if(stage is null)return;
        var scene = stage.Children.OfType<OperatorSceneBackdrop>().FirstOrDefault();
        if(scene is null)
        {
            scene = new OperatorSceneBackdrop();
            System.Windows.Controls.Panel.SetZIndex(scene,int.MinValue);
            Grid.SetRowSpan(scene,Math.Max(1,stage.RowDefinitions.Count)); Grid.SetColumnSpan(scene,Math.Max(1,stage.ColumnDefinitions.Count));
            stage.Children.Insert(0,scene);
        }
        root.SetValue(BackdropProperty,scene);
    }
    internal static void UpdateDensity(UiDensity density)
    {
        if (_density != density)
        {
            // Capture once per visible operator window, never once per descendant.
            var windows = new HashSet<Window>();
            foreach (var entry in Registered)
                if (entry.TryGetTarget(out var root) && root.IsVisible && Window.GetWindow(root) is { } window && windows.Add(window) && window.Content is FrameworkElement content)
                    OperatorDensityTransition.Begin(content);
        }
        _density = density;
        for (int i = Registered.Count - 1; i >= 0; --i)
            if (Registered[i].TryGetTarget(out var root)) SetDensity(root, density);
            else Registered.RemoveAt(i);
    }
    internal static void UpdateTheme(AppTheme theme)
    {
        _theme = theme;
        for (int i = Registered.Count - 1; i >= 0; --i)
            if (Registered[i].TryGetTarget(out var root)) SetTheme(root, theme);
            else Registered.RemoveAt(i);
    }
    private static void SetTheme(FrameworkElement root, AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        foreach (var (key, hex) in new[]
        {
            ("Canvas", dark ? "#09090B" : "#EFEFF3"), ("Surface", dark ? "#19191C" : "#FAFAFC"),
            ("Raised", dark ? "#29292D" : "#E1E1E8"), ("Text", dark ? "#FAFAFC" : "#19191E"),
            ("SecondaryText", dark ? "#B6B6C0" : "#545461"), ("Border", dark ? "#44444B" : "#858590"),
            ("Selection", dark ? "#253D55" : "#D0E5FF"), ("Accent", dark ? "#7BB8FF" : "#145699")
        }) root.Resources["ControlCentre." + key + "Brush"] = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        root.Resources["ControlCentre.SelectedTextBrush"] = root.Resources["ControlCentre.TextBrush"];
        root.Resources["ControlCentre.SegmentSelectedInkBrush"] = System.Windows.Media.Brushes.Black;
        root.Resources["ControlCentre.TabSelectedInkBrush"] = (SolidColorBrush)new BrushConverter().ConvertFromString(dark ? "#ACD7FF" : "#104B7B")!;
        root.Resources["ControlCentre.GlassBrush"] = dark
            ? new LinearGradientBrush(System.Windows.Media.Color.FromArgb(105, 24, 26, 32), System.Windows.Media.Color.FromArgb(135, 9, 11, 16), 90)
            : new LinearGradientBrush(System.Windows.Media.Color.FromArgb(165, 250, 252, 255), System.Windows.Media.Color.FromArgb(180, 224, 233, 245), 90);
        GetBackdrop(root)?.InvalidateVisual();
        if (SystemParameters.HighContrast) ApplyContrast(root);
    }
    private static void SetDensity(FrameworkElement root, UiDensity density)
    {
        bool compact = density == UiDensity.Compact;
        root.Resources["ControlCentre.BodySize"] = compact ? 14d : 15d;
        root.Resources["ControlCentre.CaptionSize"] = compact ? 12d : 13d;
        root.Resources["ControlCentre.ControlHeight"] = compact ? 36d : 42d;
        root.Resources["ControlCentre.ControlPadding"] = compact ? new Thickness(12, 7, 12, 7) : new Thickness(16, 10, 16, 10);
    }
    private static void ApplyContrast(FrameworkElement root)
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
    }
}
