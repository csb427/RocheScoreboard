using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Actual WPF class-handler/template/clock checks. The translator excludes
/// *Checks.cs: these do not claim the generic handler runs in native Mac startup.
/// </summary>
internal static class OperatorFallbackMotionChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync()
    {
        var checks = new Dictionary<string, bool>();
        // Configure writes preferences. Never use this QA helper on operator
        // files or silently enable the process isolation latch after startup.
        if (!SelfTestStorage.IsEnabled)
        {
            checks["fallback-motion-qa-requires-process-isolated-storage"] = false;
            return checks;
        }

        bool? originalOverride = OperatorMotion.AnimationOverride;
        bool originalGlass = OperatorAppearance.GlassEnabled;
        bool originalReduction = OperatorAppearance.ReduceMotion;
        string appearancePath = OperatorAppearance.StoragePath;
        string directory = Path.GetDirectoryName(appearancePath)!;
        bool directoryExisted = Directory.Exists(directory);
        var files = new[] { "", ".bak", ".tmp" }.ToDictionary(suffix => appearancePath + suffix,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var body = new StackPanel { Margin = new Thickness(20) };
        var legacy = new Button
        {
            Content = "Legacy native command", Width = 260, Height = 44,
            // An explicit empty style keeps this actual Button on the fallback
            // route rather than forcing a second adorner onto managed controls.
            Style = new Style(typeof(Button))
        };
        var viewer = new ScrollViewer
        {
            Width = 480, Height = 240, Margin = new Thickness(0, 20, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 1600, Height = 1800, Background = System.Windows.Media.Brushes.SteelBlue }
        };
        var handA = new Border { Width = 260, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, Child = new TextBlock { Text = "Uncaptured action A" } };
        var handB = new Border { Width = 260, Height = 30, Cursor = System.Windows.Input.Cursors.Hand, Child = new TextBlock { Text = "Uncaptured action B" } };
        body.Children.Add(legacy); body.Children.Add(handA); body.Children.Add(handB); body.Children.Add(viewer);
        var decorator = new AdornerDecorator { Child = body };
        var window = new Window
        {
            Title = "WPF fallback motion checks", Width = 660, Height = 470,
            ShowInTaskbar = false, Content = decorator
        };
        ContextMenu? menu = null;
        ContextMenu? fallback = null;
        Thumb? captured = null;
        Exception? primaryFailure = null;
        async Task Pump(int milliseconds = 0)
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
            if (milliseconds > 0) await Task.Delay(milliseconds);
        }
        void ReduceNow()
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(originalGlass, true);
        }
        void AllowMotion()
        {
            OperatorAppearance.Configure(originalGlass, false);
            OperatorMotion.AnimationOverride = true;
        }
        try
        {
            // WPF App.OnStartup installs this in production. This idempotent
            // call tests that WPF contract, not an invented native Mac route.
            OperatorFeedback.Install();
            OperatorDesign.Apply(window);
            AllowMotion();
            window.Show(); window.Activate(); await Pump(40);

            int commands = 0;
            var command = new MenuItem { Header = "One native command", IsCheckable = true, InputGestureText = "Ctrl+K" };
            command.Click += (_, _) => commands++;
            menu = new ContextMenu { PlacementTarget = legacy };
            menu.Items.Add(command); legacy.ContextMenu = menu;
            menu.IsOpen = true; await Pump();
            var surface = menu.Template?.FindName("PART_MenuSurface", menu) as FrameworkElement;
            checks["wpf-implicit-menu-uses-one-dedicated-arrival-owner"] = menu.IsOpen
                && OperatorMenuMotion.GetEnabled(menu) && surface is not null
                && OperatorMenuMotion.OwnsArrival(menu)
                && !HasOpacityClock(menu) && !HasTransformClock(menu.RenderTransform)
                && menu.RenderTransform.Value.IsIdentity
                && HasTransformClock(surface.RenderTransform);
            ReduceNow(); await Pump();
            checks["wpf-live-reduction-settles-the-open-menu-material"] = menu.IsOpen && surface is not null
                && !HasOpacityClock(surface) && !HasTransformClock(surface.RenderTransform)
                && surface.RenderTransform.Value.IsIdentity && commands == 0;
            // UIA invokes the native MenuItem path, with no animation deadline
            // between input acceptance and the one command notification.
            ((IInvokeProvider)new MenuItemAutomationPeer(command).GetPattern(PatternInterface.Invoke)).Invoke();
            await Pump(40);
            checks["wpf-menu-keeps-one-native-command-invocation"] = commands == 1;
            menu.IsOpen = false; await Pump();

            AllowMotion();
            fallback = new ContextMenu { PlacementTarget = legacy, Style = new Style(typeof(ContextMenu)) };
            fallback.Items.Add(new MenuItem { Header = "Fallback native menu" });
            legacy.ContextMenu = fallback;
            fallback.IsOpen = true; await Pump();
            checks["wpf-menu-without-dedicated-part-retains-generic-arrival"] = fallback.IsOpen
                && !OperatorMenuMotion.OwnsArrival(fallback)
                && (HasOpacityClock(fallback) || HasTransformClock(fallback.RenderTransform));
            fallback.IsOpen = false; await Pump();

            AllowMotion();
            OperatorInputFocus.SetIsKeyboardMode(window, true);
            bool focusAccepted = legacy.Focus() && ReferenceEquals(Keyboard.Focus(legacy), legacy);
            OperatorFeedback.Pulse(legacy);
            var beforeSize = legacy.RenderSize;
            var beforeTransform = legacy.RenderTransform.Value;
            var adorner = FallbackAdorner(legacy);
            bool hadConfirmationClock = adorner is not null && adorner.HasAnimatedProperties;
            ReduceNow(); await Pump();
            adorner = FallbackAdorner(legacy);
            checks["wpf-live-reduction-keeps-static-keyboard-focus-without-feedback-clocks"] = focusAccepted
                && hadConfirmationClock && ReferenceEquals(Keyboard.FocusedElement, legacy)
                && adorner is not null && !adorner.HasAnimatedProperties
                && LocalDouble(adorner, "Focus") == 1 && LocalDouble(adorner, "Confirm") == 0
                && legacy.RenderSize == beforeSize && legacy.RenderTransform.Value == beforeTransform;
            legacy.Visibility = Visibility.Collapsed;
            checks["wpf-hidden-fallback-target-removes-its-feedback"] = FallbackAdorner(legacy) is null;
            legacy.Visibility = Visibility.Visible;

            AllowMotion();
            // WPF routed phases expose the off-target release independently
            // of ButtonBase capture. This does not inject physical mouse input.
            handA.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = UIElement.PreviewMouseMoveEvent, Source = handA });
            handA.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = handA });
            var pressedAdorner = FallbackAdorner(handA);
            var pressureProperty = pressedAdorner?.GetType().GetField("PressProperty",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as DependencyProperty;
            bool reachedPress = pressedAdorner is not null && pressureProperty is not null
                && DependencyPropertyHelper.GetValueSource(pressedAdorner, pressureProperty).IsAnimated;
            checks["wpf-uncaptured-action-down-reaches-an-owned-pressure-clock"] = reachedPress && !handA.IsMouseCaptureWithin;
            handB.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = UIElement.PreviewMouseMoveEvent, Source = handB });
            handB.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = handB });
            ReduceNow();
            var released = FallbackAdorner(handA);
            checks["wpf-off-target-release-cannot-strand-static-pressure-on-an-uncaptured-action"] =
                reachedPress && !handA.IsMouseCaptureWithin && (released is null || !released.HasAnimatedProperties && LocalDouble(released, "Pressure") == 0);

            AllowMotion();
            viewer.ApplyTemplate(); viewer.UpdateLayout();
            var vertical = viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
            var horizontal = viewer.Template?.FindName("PART_HorizontalScrollBar", viewer) as ScrollBar;
            // Resolve the nearer operator dictionary, not an explicitly forced
            // global Scrollbars.xaml style that the operator root shadows.
            checks["wpf-operator-scrollbars-resolve-the-nearer-style-with-one-fade-owner"] = vertical is not null && horizontal is not null
                && ReferenceEquals(vertical.Style, window.FindResource(typeof(ScrollBar)))
                && ReferenceEquals(horizontal.Style, window.FindResource(typeof(ScrollBar)))
                && ScrollActivity.GetFadeEnabled(vertical) && ScrollActivity.GetFadeEnabled(horizontal)
                && ScrollActivity.GetEnabled(viewer);
            if (vertical is not null && horizontal is not null)
            {
                viewer.ScrollToVerticalOffset(40); viewer.ScrollToHorizontalOffset(30);
                viewer.UpdateLayout();
                bool enteringClock = HasOpacityClock(vertical);
                double verticalBefore = viewer.VerticalOffset, horizontalBefore = viewer.HorizontalOffset;
                ScrollBar.PageDownCommand.Execute(null, vertical);
                ScrollBar.PageRightCommand.Execute(null, horizontal);
                await Pump();
                checks["wpf-scrollbars-retain-native-page-range-and-orientation"] = viewer.VerticalOffset > verticalBefore
                    && viewer.HorizontalOffset > horizontalBefore && vertical.Maximum > vertical.Minimum
                    && horizontal.Maximum > horizontal.Minimum && vertical.ViewportSize > 0 && horizontal.ViewportSize > 0
                    && vertical.Orientation == Orientation.Vertical && horizontal.Orientation == Orientation.Horizontal
                    && Math.Abs(vertical.Value - viewer.VerticalOffset) < .01
                    && Math.Abs(horizontal.Value - viewer.HorizontalOffset) < .01;
                double offset = viewer.VerticalOffset, value = vertical.Value, maximum = vertical.Maximum;
                ReduceNow(); await Pump();
                checks["wpf-live-reduction-clears-scrollbar-entry-clock-without-changing-range"] = enteringClock
                    && ScrollActivity.GetActive(viewer) && !HasOpacityClock(vertical) && Math.Abs(vertical.Opacity - .85) < .001
                    && viewer.VerticalOffset == offset && vertical.Value == value && vertical.Maximum == maximum;

                AllowMotion();
                ScrollActivity.SetActive(viewer, false);
                bool exitingClock = HasOpacityClock(vertical);
                ReduceNow(); await Pump();
                checks["wpf-live-reduction-clears-scrollbar-exit-clock-to-style-rest"] = exitingClock
                    && !HasOpacityClock(vertical) && vertical.Opacity == 0 && viewer.VerticalOffset == offset;

                AllowMotion();
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + 10); viewer.UpdateLayout();
                vertical.ApplyTemplate();
                var track = vertical.Template?.FindName("PART_Track", vertical) as Track;
                captured = track?.Thumb;
                bool ownsCapture = captured is not null && Mouse.Capture(captured, CaptureMode.Element);
                await Pump(1450);
                checks["wpf-scrollbar-quiet-timeout-preserves-native-thumb-capture"] = ownsCapture
                    && ReferenceEquals(Mouse.Captured, captured) && viewer.IsMouseCaptureWithin && ScrollActivity.GetActive(viewer);
                if (ownsCapture && ReferenceEquals(Mouse.Captured, captured)) Mouse.Capture(null);
                captured = null;
                await Pump(1450);
                checks["wpf-scrollbar-idle-timeout-retires-after-capture-release"] = !ScrollActivity.GetActive(viewer);

                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + 10); viewer.UpdateLayout();
                viewer.Visibility = Visibility.Collapsed;
                checks["wpf-hidden-scrollbar-stops-owned-opacity-clock"] = !HasOpacityClock(vertical);
                viewer.Visibility = Visibility.Visible; await Pump();
                body.Children.Remove(viewer); await Pump();
                checks["wpf-unloaded-scrollbar-releases-clocks-and-activity"] = !HasOpacityClock(vertical) && !ScrollActivity.GetActive(viewer);
                body.Children.Add(viewer); await Pump();
                ReduceNow();
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + 10); viewer.UpdateLayout();
                var reloaded = viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
                checks["wpf-reloaded-scrollbar-accepts-activity-with-immediate-reduced-state"] = ScrollActivity.GetActive(viewer)
                    && reloaded is not null && ScrollActivity.GetFadeEnabled(reloaded)
                    && !HasOpacityClock(reloaded) && Math.Abs(reloaded.Opacity - .85) < .001;
            }
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            var cleanupErrors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { cleanupErrors.Add(error); } }
            Attempt(() => { if (captured is not null && ReferenceEquals(Mouse.Captured, captured)) Mouse.Capture(null); });
            Attempt(() => { if (menu is not null) menu.IsOpen = false; });
            Attempt(() => { if (fallback is not null) fallback.IsOpen = false; });
            Attempt(() => window.Close());
            Attempt(() => OperatorAppearance.Configure(originalGlass, originalReduction));
            Attempt(() => OperatorMotion.AnimationOverride = originalOverride);
            foreach (var (path, bytes) in files)
            {
                Attempt(() =>
                {
                    if (bytes is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(path, bytes); }
                    else if (File.Exists(path)) File.Delete(path);
                });
            }
            Attempt(() => { if (!directoryExisted && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); });
            if (cleanupErrors.Count > 0)
            {
                if (primaryFailure is not null) cleanupErrors.Insert(0, primaryFailure);
                throw new AggregateException("WPF fallback motion QA restoration failed.", cleanupErrors);
            }
        }
        return checks;
    }

    private static bool HasOpacityClock(FrameworkElement element)
        => DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated;
    private static bool HasTransformClock(Transform transform)
        => transform.HasAnimatedProperties || transform is TransformGroup group && group.Children.Any(HasTransformClock);
    private static Adorner? FallbackAdorner(UIElement target)
        => AdornerLayer.GetAdornerLayer(target)?.GetAdorners(target)?.FirstOrDefault(adorner => adorner.GetType().DeclaringType == typeof(OperatorFeedback));
    private static double LocalDouble(DependencyObject target, string name)
    {
        var values = target.GetLocalValueEnumerator();
        while (values.MoveNext())
            if (values.Current.Property.Name == name && target.GetValue(values.Current.Property) is double value) return value;
        return double.NaN;
    }
}
