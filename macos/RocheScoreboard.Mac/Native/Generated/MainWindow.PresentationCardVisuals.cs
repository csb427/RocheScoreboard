// Generated from MainWindow.PresentationCardVisuals.cs; edit translator/adapters.
// Source SHA-256: c8c5bfeee153e4cd2bad8093b1d199a4bea9742cdcc37dfed41cda474576179c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
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
        NativeEvents.WatchProperty(PresCardWindow,Avalonia.Controls.Control.IsVisibleProperty,true,PresentationWindowCardVisibilityChanged);
        NativeEvents.WatchProperty(PresCardWindow,Avalonia.Controls.Control.IsEnabledProperty,true,PresentationWindowCardEnabledChanged);
        // Observe these actual owners explicitly: compatibility controls may
        // expose their own visibility without an ancestor visibility event.
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,PresentationWindowCardVisibilityChanged);
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,PresentationWindowCardEnabledChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,PresentationWindowCardVisibilityChanged);
        NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsEnabledProperty,true,PresentationWindowCardEnabledChanged);
        MainTabControl.SelectionChanged += PresentationWindowTabChanged;
        Closed += PresentationWindowOwnerClosed;
        if (PresCardWindow.IsLoaded) PresentationWindowCardLoaded(PresCardWindow, new RoutedEventArgs());
    }

    private bool PresentationOutputIsOpen() => _sportMode == SportMode.AFL
        ? _displayWindow is { IsVisible: true }
        : _cricketDisplayWindow is { IsVisible: true };

    private void PresentationWindowCardLoaded(object? sender, RoutedEventArgs args)
    {
        if (!_presentationWindowPolicyAttached)
        {
            OperatorAppearance.Changed += PresentationWindowMotionPolicyChanged;
            NativeSystemPreferences.StaticPropertyChanged += PresentationWindowSystemPolicyChanged;
            _presentationWindowPolicyAttached = true;
        }
        SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardUnloaded(object? sender, RoutedEventArgs args)
        => DetachPresentationWindowMotionPolicy();

    private void PresentationWindowOwnerClosed(object? sender, EventArgs args)
        => DetachPresentationWindowMotionPolicy();

    private void DetachPresentationWindowMotionPolicy()
    {
        OperatorAppearance.Changed -= PresentationWindowMotionPolicyChanged;
        NativeSystemPreferences.StaticPropertyChanged -= PresentationWindowSystemPolicyChanged;
        _presentationWindowPolicyAttached = false;
        SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property.Name == nameof(Control.IsVisible)) SettlePresentationWindowThumb();
    }

    private void PresentationWindowCardEnabledChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property.Name == nameof(Control.IsEnabled)) SettlePresentationWindowThumb();
    }

    private void PresentationWindowTabChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs args)
    {
        if (args.Source == MainTabControl) SettlePresentationWindowThumb();
    }

    private void PresentationWindowMotionPolicyChanged() => SettlePresentationWindowThumb();

    private void PresentationWindowSystemPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(NativeSystemPreferences.ClientAreaAnimation)) return;
        if (Dispatcher.UIThread.CheckAccess()) SettlePresentationWindowThumb();
        else Dispatcher.UIThread.Post(new Action(() =>
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
