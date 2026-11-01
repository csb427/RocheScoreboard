// Generated from MainWindow.ResetDecision.cs; edit translator/adapters.
// Source SHA-256: f18ae40d680ad00a49e172b5c05e0a9c2cf8016cc156e91622c1e4b433cf8d55
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private bool _resetDecisionActive;
    private bool _resetContextChanged;
    private bool _resetClosing;
    private bool _resetInitialized;
    private bool _resetOwnerClosed;
    private int _resetGeneration;
    private int _resetQuarter;
    private Action? _resetMatchReceipt;
    private Control? _resetSource;
    private Control? _resetReturnFocus;

    private bool ResetContextEligible => !_resetOwnerClosed && IsLoaded && IsVisible
        && _sportMode == SportMode.AFL && _aflMatchReady && MainContent.IsVisible;

    private void InitializeResetDecision()
    {
        if (_resetInitialized) return;
        _resetInitialized = true;
        InitializeResetMotion();
        NativeEvents.WatchProperty(ResetOverlay,Avalonia.Controls.Control.IsVisibleProperty,true,ResetVisibilityChanged);
        Closing += ResetOwner_Closing;
        Closed += ResetOwner_Closed;
    }

    private void ShowResetOverlay(Control? source = null)
    {
        if (_resetDecisionActive) return;
        if (!ResetContextEligible || CommandOverlay.IsVisible || CommandContextBlocked) return;
        InitializeResetDecision();
        // A remote target passes the Window, not a visible operator trigger.
        _resetSource = source is { IsLoaded: true, IsVisible: true, Bounds.Width: > 0, Bounds.Height: > 0 }
            && source is not NativeWindow && ReferenceEquals(NativeWindow.GetWindow(source), this) ? source : null;
        _resetReturnFocus = _resetSource;
        if (_resetReturnFocus is null && NativeKeyboard.FocusedElement is Control
            { IsLoaded: true, IsVisible: true, IsEnabled: true, Focusable: true } focused
            && ReferenceEquals(NativeWindow.GetWindow(focused), this) && !ResetOverlay.IsAncestorOf(focused))
            _resetReturnFocus = focused;
        int generation = ++_resetGeneration;
        StopResetMotion();
        _resetClosing = false;
        _resetContextChanged = false;
        _resetQuarter = _match.Quarter;
        _resetDecisionActive = true;
        _resetMatchReceipt = () =>
        {
            if (!_resetDecisionActive || generation != _resetGeneration) return;
            // LoadState replaces data in the same readonly MatchManager. An
            // object/quarter comparison alone cannot protect this receipt.
            _resetContextChanged = true;
            ConfirmResetBtn.IsEnabled = false;
            ResetContextNotice.IsVisible = true;
        };
        _match.MatchChanged += _resetMatchReceipt;
        OperatorAppearance.Changed -= ResetAppearanceChanged;
        OperatorAppearance.Changed += ResetAppearanceChanged;

        ResetMatchContext.Text = $"{_match.HomeName}  {_match.HomeGoals}.{_match.HomeBehinds} ({_match.HomeTotal})\n"
            + $"{_match.AwayName}  {_match.AwayGoals}.{_match.AwayBehinds} ({_match.AwayTotal})";
        ResetPhaseText.Text = $"Quarter {_match.Quarter} · clock {(_match.ClockRunning ? "running" : "stopped")}";
        ResetContextNotice.IsVisible = false;
        ConfirmResetBtn.IsEnabled = true;
        CancelResetBtn.IsDefault = true;
        ResetOverlay.Opacity = 1;
        ResetOverlay.IsHitTestVisible = true;
        ResetOverlay.IsVisible = true;
        ResetOverlay.UpdateLayout();
        FitResetDecision();
        ResetHost.UpdateLayout();
        // Geometry uses the actual opener; glass samples only the inherited
        // decorative scene. Never capture operator labels or live output.
        ResetSurface.Material = OperatorDesign.GetBackdrop(ResetSurface) is { IsLoaded: true, Bounds.Width: > 0, Bounds.Height: > 0 }
            ? GlassMaterial.Regular : GlassMaterial.Opaque;
        ResetDetails.ScrollToVerticalOffset(0);
        NativeKeyboard.Focus(CancelResetBtn);
        OpenResetMotion();
    }

    private void RetireResetReceipt()
    {
        _resetDecisionActive = false;
        CancelResetBtn.IsDefault = false;
        if (_resetMatchReceipt is { } receipt) _match.MatchChanged -= receipt;
        _resetMatchReceipt = null;
        ResetOverlay.IsHitTestVisible = false;
    }

    private void CancelResetDecision()
    {
        if (!_resetDecisionActive) return;
        var source = _resetReturnFocus;
        RetireResetReceipt(); // No destructive authority remains during exit.
        _resetClosing = true;
        RestoreResetFocus(source);
        CloseResetMotion(_resetGeneration);
    }

    private void ConfirmResetDecision()
    {
        if (!_resetDecisionActive) return;
        if (!ResetContextEligible || _resetContextChanged || _resetQuarter != _match.Quarter)
        {
            CancelResetDecision();
            return;
        }
        RetireResetReceipt(); // Consume before MatchChanged and irreversible I/O.
        CompleteResetVisual(_resetGeneration);
        ExecuteMatchReset();
    }

    private void RestoreResetFocus(Control? source)
    {
        if (_resetOwnerClosed || !IsActive || source is not { IsLoaded: true, IsVisible: true, IsEnabled: true, Focusable: true }
            || !ReferenceEquals(NativeWindow.GetWindow(source), this)) return;
        // Synchronous restoration belongs to this Cancel. No animation callback
        // can later steal focus from a new command, window or reset decision.
        if (NativeKeyboard.FocusedElement is Control { IsVisible: true, IsEnabled: true, Focusable: true } focused
            && !ReferenceEquals(focused, source) && !ResetOverlay.IsAncestorOf(focused)) return;
        NativeKeyboard.Focus(source);
    }

    private void HandleResetPreviewKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (!_resetDecisionActive) return;
        Key key = e.Key;
        if (key == Key.F4 && (NativeKeyboard.Modifiers & KeyModifiers.Alt) != 0) return; // Native owner close remains available.
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (!NativeKeyboard.IsRepeated(e)) CancelResetDecision();
            return;
        }
        if (key == Key.Tab)
        {
            e.Handled = true;
            bool reverse = (NativeKeyboard.Modifiers & KeyModifiers.Shift) != 0;
            bool onCancel = ReferenceEquals(NativeKeyboard.FocusedElement, CancelResetBtn);
            bool onConfirm = ReferenceEquals(NativeKeyboard.FocusedElement, ConfirmResetBtn);
            NativeKeyboard.Focus(ConfirmResetBtn.IsEnabled && (reverse ? !onConfirm : onCancel) ? ConfirmResetBtn : CancelResetBtn);
            return;
        }
        if (key == Key.Enter)
        {
            e.Handled = true;
            if (!NativeKeyboard.IsRepeated(e))
            {
                if (ReferenceEquals(NativeKeyboard.FocusedElement, ConfirmResetBtn)) ConfirmResetDecision();
                else CancelResetDecision();
            }
            return;
        }
        if (key == Key.Space && (ReferenceEquals(NativeKeyboard.FocusedElement, CancelResetBtn)
            || ReferenceEquals(NativeKeyboard.FocusedElement, ConfirmResetBtn))) return;
        // The explanation can scroll with the keyboard while its actions stay
        // pinned. This never forwards score/navigation/command-bar shortcuts.
        if (key is Key.PageDown or Key.PageUp or Key.Down or Key.Up)
        {
            double step = key is Key.Down or Key.Up ? 32 : Math.Max(32, ResetDetails.Viewport.Height * .8);
            if (key is Key.PageUp or Key.Up) step = -step;
            ResetDetails.ScrollToVerticalOffset(Math.Max(0, ResetDetails.Offset.Y + step));
        }
        e.Handled = true;
    }

    private void ResetOverlay_SizeChanged(object? sender, SizeChangedEventArgs e) => FitResetDecision();
    private void FitResetDecision()
    {
        if (ResetHost is null || ResetOverlay is null || ResetOverlay.NativeHeight() <= 0) return;
        double maximum = Math.Max(0, Math.Min(430, ResetOverlay.NativeHeight() - 24));
        if (Math.Abs(ResetHost.MaxHeight - maximum) > .5) ResetHost.MaxHeight = maximum;
    }
    private void ResetVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // The native adapter uses PropertyChanged for this WPF event. Ignore
        // unrelated layout/opacity/enabled changes rather than retiring input.
        if (!_resetInitialized || e.Property.Name != nameof(Control.IsVisible) || ResetOverlay.IsVisible) return;
        RetireResetReceipt();
        CompleteResetVisual(_resetGeneration);
    }
    private void ResetOverlay_Unloaded(object? sender, RoutedEventArgs e)
    {
        if (!_resetInitialized) return;
        RetireResetReceipt();
        CompleteResetVisual(_resetGeneration);
    }
    private void ResetOwner_Closed(object? sender, EventArgs e)
    {
        _resetOwnerClosed = true;
        ++_resetGeneration;
        RetireResetReceipt();
        CompleteResetVisual(_resetGeneration);
        NativeEvents.WatchProperty(ResetOverlay,Avalonia.Controls.Control.IsVisibleProperty,false,ResetVisibilityChanged);
        Closing -= ResetOwner_Closing;
        Closed -= ResetOwner_Closed;
    }
    private void ResetOwner_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // A close attempt retires this decision even if another listener later
        // cancels window closure. No dispatcher continuation can confirm it.
        RetireResetReceipt();
        CompleteResetVisual(_resetGeneration);
    }
}
