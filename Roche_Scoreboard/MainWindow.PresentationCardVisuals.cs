using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard;

// Tracks actual output visibility with interruptible, policy-aware thumb motion.
public partial class MainWindow
{
    private bool _presentationWindowHooksInstalled;
    private bool _presentationWindowPolicyAttached;
    private double? _presentationWindowThumbTarget;
    private int _presentationWindowThumbGeneration;

    private void InstallPresentationWindowMotion()
    {
        if (_presentationWindowHooksInstalled) return;
        _presentationWindowHooksInstalled = true;
        PresCardWindow.Loaded += PresentationWindowCardLoaded;
        PresCardWindow.Unloaded += PresentationWindowCardUnloaded;
        PresCardWindow.IsVisibleChanged += PresentationWindowCardVisibilityChanged;
        PresCardWindow.IsEnabledChanged += PresentationWindowCardEnabledChanged;
        // Observe these actual owners explicitly: compatibility controls may
        // expose their own visibility without an ancestor visibility event.
        IsVisibleChanged += PresentationWindowCardVisibilityChanged;
        IsEnabledChanged += PresentationWindowCardEnabledChanged;
        MainContent.IsVisibleChanged += PresentationWindowCardVisibilityChanged;
        MainContent.IsEnabledChanged += PresentationWindowCardEnabledChanged;
        MainTabControl.SelectionChanged += PresentationWindowTabChanged;
        Closed += PresentationWindowOwnerClosed;
        if (PresCardWindow.IsLoaded) PresentationWindowCardLoaded(PresCardWindow, new RoutedEventArgs());
    }

    private bool PresentationOutputIsOpen() => _sportMode == SportMode.AFL
        ? _displayWindow is { IsVisible: true }
        : _cricketDisplayWindow is { IsVisible: true };

    private void PresentationWindowCardLoaded(object sender, RoutedEventArgs args)
    {
        if (!_presentationWindowPolicyAttached)
        {
            OperatorAppearance.Changed += PresentationWindowMotionPolicyChanged;
            SystemParameters.StaticPropertyChanged += PresentationWindowSystemPolicyChanged;
            _presentationWindowPolicyAttached = true;
        }
        SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardUnloaded(object sender, RoutedEventArgs args)
        => DetachPresentationWindowMotionPolicy();

    private void PresentationWindowOwnerClosed(object? sender, EventArgs args)
        => DetachPresentationWindowMotionPolicy();

    private void DetachPresentationWindowMotionPolicy()
    {
        OperatorAppearance.Changed -= PresentationWindowMotionPolicyChanged;
        SystemParameters.StaticPropertyChanged -= PresentationWindowSystemPolicyChanged;
        _presentationWindowPolicyAttached = false;
        SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.Property.Name == nameof(UIElement.IsVisible)) SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.Property.Name == nameof(UIElement.IsEnabled)) SettlePresentationWindowThumb();
    }

    private void PresentationWindowTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs args)
    {
        if (args.Source == MainTabControl) SettlePresentationWindowThumb();
    }

    private void PresentationWindowMotionPolicyChanged() => SettlePresentationWindowThumb();

    private void PresentationWindowSystemPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) SettlePresentationWindowThumb();
        else Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_presentationWindowPolicyAttached) SettlePresentationWindowThumb();
        }));
    }

    private void SettlePresentationWindowThumb()
    {
        ++_presentationWindowThumbGeneration;
        double target = PresentationOutputIsOpen() ? 20 : 0;
        _presentationWindowThumbTarget = target;
        WindowToggleThumbTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        WindowToggleThumbTranslate.X = target;
    }

    private void UpdatePresentationWindowThumb(bool isOpen)
    {
        InstallPresentationWindowMotion();
        double target = isOpen ? 20 : 0;
        bool animate = OperatorMotion.Enabled && IsVisible && IsEnabled
            && MainContent.IsVisible && MainContent.IsEnabled
            && ReferenceEquals(MainTabControl.SelectedItem, OutputStudioTab)
            && PresCardWindow.IsLoaded && PresCardWindow.IsVisible && PresCardWindow.IsEnabled
            && !(PresCardWindow.IsKeyboardFocusWithin && OperatorInputFocus.GetIsKeyboardMode(PresCardWindow));
        if (!animate) { SettlePresentationWindowThumb(); return; }
        // Unchanged state retains a current flight, but never starts another.
        if (_presentationWindowThumbTarget == target) return;
        double from = WindowToggleThumbTranslate.X;
        int generation = ++_presentationWindowThumbGeneration;
        _presentationWindowThumbTarget = target;
        WindowToggleThumbTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        WindowToggleThumbTranslate.X = target;
        var motion = new DoubleAnimation(from, target, OperatorMotion.HoverIn)
        {
            EasingFunction = OperatorMotion.SmoothOut,
            FillBehavior = FillBehavior.Stop
        };
        motion.Completed += (_, _) =>
        {
            if (generation == _presentationWindowThumbGeneration) SettlePresentationWindowThumb();
        };
        WindowToggleThumbTranslate.BeginAnimation(TranslateTransform.XProperty, motion,
            HandoffBehavior.SnapshotAndReplace);
    }
}
