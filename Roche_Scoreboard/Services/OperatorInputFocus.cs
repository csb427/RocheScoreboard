using System.Windows;
using System.Windows.Input;

namespace Roche_Scoreboard.Services;

/// <summary>Mouse selection is not keyboard focus. Keep a visible, neutral ring for keyboard users.</summary>
public static class OperatorInputFocus
{
    public static readonly DependencyProperty IsKeyboardModeProperty = DependencyProperty.RegisterAttached(
        "IsKeyboardMode", typeof(bool), typeof(OperatorInputFocus), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsKeyboardMode(DependencyObject element) => (bool)element.GetValue(IsKeyboardModeProperty);
    public static void SetIsKeyboardMode(DependencyObject element, bool value) => element.SetValue(IsKeyboardModeProperty, value);
    internal static void Install(FrameworkElement root)
    {
        root.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => SetIsKeyboardMode(root, false)), true);
        root.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((_, _) => SetIsKeyboardMode(root, false)), true);
        root.AddHandler(UIElement.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, args) =>
        {
            if(args.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Space)
                SetIsKeyboardMode(root, true);
        }), true);
    }
}
