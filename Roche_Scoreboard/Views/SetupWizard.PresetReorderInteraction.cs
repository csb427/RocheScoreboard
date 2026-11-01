using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Border = System.Windows.Controls.Border;

namespace Roche_Scoreboard.Views;

public partial class SetupWizard
{
    private bool _presetDragPolicyAttached;
    private int _presetDragPolicyEpoch;
    private Window? _presetDragOwner;
    private bool _presetDragMotionEnabled;
    private OperatorMotionStyle _presetDragMotionStyle;

    private void InitializePresetReorderInteraction()
    {
        Loaded += PresetReorderLoaded;
        Unloaded += PresetReorderUnloaded;
        IsEnabledChanged += PresetReorderAvailabilityChanged;
        IsVisibleChanged += PresetReorderAvailabilityChanged;
    }

    private void PresetReorderLoaded(object? sender, RoutedEventArgs args)
    {
        var owner = Window.GetWindow(this);
        if (!ReferenceEquals(owner, _presetDragOwner))
        {
            DetachPresetDragOwner(); _presetDragOwner = owner;
            if (owner is not null)
            {
                owner.Closed += PresetDragOwnerClosed;
                owner.IsVisibleChanged += PresetReorderAvailabilityChanged;
                owner.IsEnabledChanged += PresetReorderAvailabilityChanged;
            }
        }
        WatchPresetDragPolicy(IsVisible && IsEnabled);
    }
    private void PresetReorderUnloaded(object? sender, RoutedEventArgs args)
    { SettlePresetDragPreview(); WatchPresetDragPolicy(false); DetachPresetDragOwner(); }
    private void PresetDragOwnerClosed(object? sender, EventArgs args)
    { SettlePresetDragPreview(); WatchPresetDragPolicy(false); DetachPresetDragOwner(); }
    private void DetachPresetDragOwner()
    {
        ++_presetDragPolicyEpoch;
        if (_presetDragOwner is not null)
        {
            _presetDragOwner.Closed -= PresetDragOwnerClosed;
            _presetDragOwner.IsVisibleChanged -= PresetReorderAvailabilityChanged;
            _presetDragOwner.IsEnabledChanged -= PresetReorderAvailabilityChanged;
        }
        _presetDragOwner = null;
    }
    private void PresetReorderAvailabilityChanged(object? sender, DependencyPropertyChangedEventArgs args)
    {
        bool available = IsLoaded && IsVisible && IsEnabled
            && _presetDragOwner is { IsVisible: true, IsEnabled: true };
        if (!available) SettlePresetDragPreview();
        WatchPresetDragPolicy(available);
    }
    private void WatchPresetDragPolicy(bool active)
    {
        if (active == _presetDragPolicyAttached) return;
        ++_presetDragPolicyEpoch; _presetDragPolicyAttached = active;
        if (active)
        {
            _presetDragMotionEnabled = OperatorMotion.Enabled;
            _presetDragMotionStyle = OperatorAppearance.MotionStyle;
            OperatorAppearance.Changed += PresetDragAppearanceChanged;
            SystemParameters.StaticPropertyChanged += PresetDragSystemChanged;
        }
        else
        {
            OperatorAppearance.Changed -= PresetDragAppearanceChanged;
            SystemParameters.StaticPropertyChanged -= PresetDragSystemChanged;
        }
    }
    private void PresetDragSystemChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) PresetDragAppearanceChanged(); }
    private void PresetDragAppearanceChanged()
    {
        int epoch = _presetDragPolicyEpoch;
        int generation = _presetOwnerGeneration;
        var owner = _presetDragOwner;
        void ApplyPolicy()
        {
            if (!_presetDragPolicyAttached || epoch != _presetDragPolicyEpoch || generation != _presetOwnerGeneration || !ReferenceEquals(owner, _presetDragOwner)) return;
            bool enabled = OperatorMotion.Enabled;
            var style = OperatorAppearance.MotionStyle;
            if (!enabled || enabled != _presetDragMotionEnabled || style != _presetDragMotionStyle) SettlePresetDragPreview();
            _presetDragMotionEnabled = enabled; _presetDragMotionStyle = style;
        }
        if (Dispatcher.CheckAccess()) ApplyPolicy(); else Dispatcher.BeginInvoke(new Action(ApplyPolicy));
    }
    private void SettlePresetDragPreview()
    {
        if (_dragTile is not null) CancelPresetDrag(_dragIsHome ? HomePresetPanel : AwayPresetPanel);
        ResetAllTileTransforms(HomePresetPanel); ResetAllTileTransforms(AwayPresetPanel);
        _presetDragMotionEnabled = OperatorMotion.Enabled;
        _presetDragMotionStyle = OperatorAppearance.MotionStyle;
    }
    private bool TryCancelPresetDragOnEscape(System.Windows.Input.KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || _dragTile is null) return false;
        args.Handled = true; SettlePresetDragPreview();
        return true;
    }

    private void ConfigurePresetKeyboardReorder(Border tile, WrapPanel panel, bool isHome, TeamPreset preset)
    {
        System.Windows.Automation.AutomationProperties.SetHelpText(tile,
            "Press Enter or Space to choose this club. Press Alt+Up to move it earlier or Alt+Down to move it later in saved club order. Wait for a pending save before moving again.");
        tile.PreviewKeyDown += async (_, args) =>
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Up or Key.Down)) return;
            args.Handled = true;
            // Consume a busy repeat without accepting another speculative move.
            if (_presetOperationPending || _presetLoading || _dragTile is not null
                || !OwnsPresetReorderTile(tile, panel, preset)) return;
            int from = _presets.IndexOf(preset), to = from + (key == Key.Up ? -1 : 1);
            if (to < 0 || to >= _presets.Count)
            {
                WizardTeamEditor.SetPresetStatus(this, isHome ? "Home" : "Away", key == Key.Up ? "Already the first saved club." : "Already the last saved club.");
                return;
            }
            int generation = _presetOwnerGeneration;
            string[] order = _presets.Select(item => item.PresetName).ToArray();
            await PersistPresetChange(PresetStorage.ReorderAsync(PresetSport, order, from, to), isHome,
                "Saving club order…", "Saved club order updated.");
            if (generation != _presetOwnerGeneration || !IsLoaded || !IsVisible) return;
            // PublishPresetSnapshot already retains focus by the original typed
            // identity. Reveal only that still-focused current tile; never take
            // focus back from a newer field or another side after the await.
            var current = panel.Children.OfType<Border>().FirstOrDefault(item => ReferenceEquals(item.Tag, preset));
            if (current is not null && current.IsKeyboardFocusWithin && OwnsPresetReorderTile(current, panel, preset)) current.BringIntoView();
        };
    }
    private bool OwnsPresetReorderTile(Border tile, WrapPanel panel, TeamPreset preset)
    {
        if (!IsLoaded || (panel != HomePresetPanel && panel != AwayPresetPanel)
            || !panel.Children.Contains(tile) || !ReferenceEquals(tile.Tag, preset) || !_presets.Contains(preset)) return false;
        bool owned = false;
        var owner = Window.GetWindow(this);
        for (DependencyObject? current = tile; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element && (!element.IsVisible || !element.IsEnabled)) return false;
            if (ReferenceEquals(current, this)) owned = true;
            if (ReferenceEquals(current, owner)) return owned;
        }
        return false;
    }
}
