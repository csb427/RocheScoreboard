using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;

namespace Roche_Scoreboard.Views;

/// <summary>Loaded native checks for interrupted and reduced-motion shared dialog entry.</summary>
internal static class SharedSurfaceMotionChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync()
    {
        var checks = new Dictionary<string, bool>();
        bool? previous = OperatorMotion.AnimationOverride;
        var surface = new Border { Width = 240, Height = 120, Background = Brushes.SteelBlue };
        var stage = new Grid(); stage.Children.Add(surface);
        var window = new Window { Content = stage, Width = 360, Height = 240, ShowInTaskbar = false };
        SharedSurfaceMotion.Attach(surface);
        try
        {
            OperatorMotion.AnimationOverride = true;
            window.Show();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Loaded);
            Size before = surface.RenderSize;
            SharedSurfaceMotion.Reveal(surface);
            checks["shared-surface-entry-animates-without-resizing-its-target"] = surface.HasAnimatedProperties && surface.RenderSize == before;
            surface.Visibility = Visibility.Hidden;
            checks["shared-surface-hide-cancels-entry-and-restores-opacity"] = !surface.HasAnimatedProperties && surface.Opacity == 1 && surface.RenderTransform.Value.IsIdentity;
            surface.Visibility = Visibility.Visible;
            SharedSurfaceMotion.Reveal(surface);
            SharedSurfaceMotion.Reveal(surface);
            await Task.Delay(300);
            // Desktop composition may throttle background fixture windows. Wait for
            // a rendered final state with a bound, rather than assuming one frame rate.
            for(int frame=0;frame<40 && (surface.Opacity!=1 || !surface.RenderTransform.Value.IsIdentity);frame++)await Task.Delay(16);
            checks["shared-surface-rapid-reentry-settles-without-offset-or-fade"] = surface.Opacity == 1 && surface.RenderTransform.Value.IsIdentity && surface.RenderSize == before;
            OperatorMotion.AnimationOverride = false;
            SharedSurfaceMotion.Reveal(surface);
            checks["shared-surface-reduced-motion-restores-static-visible-state"] = !surface.HasAnimatedProperties && surface.Opacity == 1 && surface.RenderTransform.Value.IsIdentity;
            OperatorMotion.AnimationOverride = true;
            SharedSurfaceMotion.Reveal(surface);
            stage.Children.Remove(surface);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            checks["shared-surface-unload-clears-interrupted-animation"] = !surface.HasAnimatedProperties && surface.Opacity == 1 && surface.RenderTransform.Value.IsIdentity;
        }
        finally
        {
            window.Close();
            OperatorMotion.AnimationOverride = previous;
        }
        var picker = (ColorPickerWindow)Activator.CreateInstance(typeof(ColorPickerWindow),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new object[] { Colors.Red }, null)!;
        try
        {
            picker.Show();
            await picker.Dispatcher.InvokeAsync(() => picker.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Loaded);
            var beforeColour = picker.SelectedColor;
            picker.ColourField.Focus();
            picker.ColourField.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(picker)!, Environment.TickCount, System.Windows.Input.Key.Right)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            checks["colour-wheel-keyboard-adjustment-updates-the-selected-colour"] = picker.ColourField.Focusable && picker.SelectedColor != beforeColour;
            checks["colour-fine-tuning-sliders-have-names-and-keyboard-steps"] =
                System.Windows.Automation.AutomationProperties.GetName(picker.SaturationSlider) == "Saturation"
                && System.Windows.Automation.AutomationProperties.GetName(picker.BrightnessSlider) == "Brightness"
                && picker.SaturationSlider.SmallChange == .01 && picker.BrightnessSlider.SmallChange == .01;
            checks["colour-palette-buttons-have-colour-specific-accessible-names"] = picker.DefaultColoursPanel.Children.OfType<Button>()
                .All(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("Use colour #", StringComparison.Ordinal));
        }
        finally { picker.Close(); }
        return checks;
    }
}
