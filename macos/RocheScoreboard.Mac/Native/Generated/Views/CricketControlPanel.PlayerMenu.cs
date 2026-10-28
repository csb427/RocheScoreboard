// Generated from Views/CricketControlPanel.PlayerMenu.cs; edit translator/adapters.
// Source SHA-256: be0c03e8769b054e7d3f5efd6aee9b6197f19d80c8daaf77bfa0176131a11bb5
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using Button = Avalonia.Controls.Button;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private Popup? _playerMenu;
    private Popup? _retiringPlayerMenu;
    private Control? _playerMenuAnchor;
    private Action<bool, bool>? _dismissPlayerMenu;
    private int _playerMenuGeneration;
    private static Avalonia.Controls.Templates.IControlTemplate? _playerMenuRowTemplate;
    private void ClosePlayerMenu()
    {
        _dismissPlayerMenu?.Invoke(false, !IsLoaded);
    }

    private void ShowPlayerMenu(Control? anchor, bool bowling, int playerIndex)
    {
        if (_playerMenu is { IsOpen: true } && ReferenceEquals(anchor, _playerMenuAnchor))
        {
            _dismissPlayerMenu?.Invoke(true, false);
            return;
        }
        ClosePlayerMenu();
        var match = _match; var innings = match?.CurrentInnings;
        if (anchor is null || match is null || innings is null || _workflowBusy || match.TossPending || playerIndex < 0
            || playerIndex >= (bowling ? innings.BowlingAttack.Count : innings.BattingOrder.Count)) return;
        int revision = match.DeliveryRevision;
        int generation = ++_playerMenuGeneration;
        var owner = NativeWindow.GetWindow(this);
        var options = new StackPanel();
        OperatorDesign.SetIsEnabled(options, true);
        // Menu rows share press physics with command buttons, while their glass
        // belongs to the popover rather than another material layer per row.
        var rowStyle = new NativeControlTheme(typeof(Button), options.TryFindResource("ControlCentre.Button") as Style);
        rowStyle.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty,
            _playerMenuRowTemplate ??= NativeMenuTemplate.Create()));
        var heading = new NativeTextBlock { Text = bowling ? "Bowler" : "Batter", FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(14, 10, 14, 8) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        options.Children.Add(heading);
        var surface = new OperatorGlassSurface
        {
            Material = GlassMaterial.Regular,
            GlassSource = owner?.Content as Control ?? this,
            Background = new SolidColorBrush(Color.FromArgb(148, 33, 35, 43)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(26),
            Padding = new Thickness(7), Child = options, Width = 280,
            RenderTransformOrigin = new RelativePoint(.88, 0,RelativeUnit.Relative)
        };
        var scale = new ScaleTransform(1, 1);
        var shift = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(shift);
        surface.RenderTransform = transform;
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, IsLightDismissEnabled = false,
              Child = surface,
            VerticalOffset=8,};
        _playerMenu = popup;
        _playerMenuAnchor = anchor;
        bool closing = false, completed = false;
        bool Current() => ReferenceEquals(match, _match) && ReferenceEquals(innings, match.CurrentInnings) && revision == match.DeliveryRevision;
        bool Active() => !closing && !completed && generation == _playerMenuGeneration && ReferenceEquals(_playerMenu, popup);
        void Detach()
        {
            OperatorAppearance.Changed -= PreferenceChanged;
            anchor.Unloaded -= AnchorUnloaded; Unloaded -= PanelUnloaded;
            if (owner is not null)
            {
                owner.PointerPressed -= OwnerPointerDown; owner.Deactivated -= OwnerDeactivated;
                owner.PositionChanged -= OwnerMoved; owner.SizeChanged -= OwnerResized;
            }
        }
        void Complete()
        {
            if (completed) return;
            completed = true; Detach();
            surface.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(_playerMenu, popup)) { _playerMenu = null; _playerMenuAnchor = null; _dismissPlayerMenu = null; }
            if (ReferenceEquals(_retiringPlayerMenu, popup)) _retiringPlayerMenu = null;
            popup.IsOpen = false;
            popup.Child = null;
        }
        void PreferenceChanged()
        {
            if (OperatorMotion.Enabled || completed) return;
            if (closing) { Complete(); return; }
            surface.BeginAnimation(OpacityProperty, null); surface.Opacity = 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null); scale.ScaleX = scale.ScaleY = 1; shift.Y = 0;
        }
        void Dismiss(bool restoreFocus, bool immediate)
        {
            if (completed || closing) return;
            closing = true; Detach();
            int dismissalGeneration = ++_playerMenuGeneration;
            if (ReferenceEquals(_playerMenu, popup)) { _playerMenu = null; _playerMenuAnchor = null; _dismissPlayerMenu = null; }
            if (_retiringPlayerMenu is { } previous && !ReferenceEquals(previous, popup)) previous.IsOpen = false;
            _retiringPlayerMenu = popup;
            options.IsHitTestVisible = false;
            if (restoreFocus)
                Dispatcher.UIThread.Post(new Action(() =>
                {
                    if (dismissalGeneration != _playerMenuGeneration || _playerMenu is not null || owner is not { IsActive: true } || !anchor.IsLoaded || !anchor.IsVisible || !anchor.IsEnabled) return;
                    anchor.Focus(); NativeKeyboard.Focus(anchor);
                }), DispatcherPriority.Input);
            if (immediate || !OperatorMotion.Enabled || !surface.IsLoaded || !popup.IsOpen) { Complete(); return; }
            var fade = new DoubleAnimation(surface.Opacity, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = OperatorMotion.SmoothOut };
            fade.Completed += (_, _) => Complete();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, .94, TimeSpan.FromMilliseconds(140)) { EasingFunction = OperatorMotion.SmoothOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, .94, TimeSpan.FromMilliseconds(140)) { EasingFunction = OperatorMotion.SmoothOut });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift.Y, -5, TimeSpan.FromMilliseconds(140)) { EasingFunction = OperatorMotion.SmoothOut });
            surface.BeginAnimation(OpacityProperty, fade);
        }
        void AnchorUnloaded(object? sender, RoutedEventArgs e) => Dismiss(false, true);
        void PanelUnloaded(object? sender, RoutedEventArgs e) => Dismiss(false, true);
        void OwnerDeactivated(object? sender, EventArgs e) => Dismiss(false, true);
        void OwnerMoved(object? sender, EventArgs e) => Dismiss(false, true);
        void OwnerResized(object? sender, SizeChangedEventArgs e) => Dismiss(false, true);
        void OwnerPointerDown(object? sender, PointerEventArgs e)
        {
            if (e.Source is AvaloniaObject target && (ReferenceEquals(target, anchor) || anchor.IsAncestorOf(target))) return;
            // Let the original scoring click continue. The menu exit is visual only.
            Dismiss(false, false);
        }
        _dismissPlayerMenu = Dismiss;
        anchor.Unloaded += AnchorUnloaded; Unloaded += PanelUnloaded;
        if (owner is not null)
        {
            owner.PointerPressed += OwnerPointerDown; owner.Deactivated += OwnerDeactivated;
            owner.PositionChanged += OwnerMoved; owner.SizeChanged += OwnerResized;
        }
        void Add(string label, string key, Func<Task> action, bool enabled = true)
        {
            var button = WorkflowButton(label, () => { }, key); button.IsEnabled = enabled;
            button.Theme = rowStyle;
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
            button.MinHeight = 48; button.Margin = new Thickness(0, 1, 0, 1); button.Padding = new Thickness(12, 9, 12, 9);
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; button.ContentTemplate = null;
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var symbol = new OperatorSymbol { Kind = key switch { "change-bowler" or "replace-batter" => "replace", "swap-strike" => "swap", _ => "edit" }, Width = 20, Height = 20, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty, "ControlCentre.TextBrush"); row.Children.Add(symbol);
            var text = new NativeTextBlock { Text = label, FontSize = 15, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1); row.Children.Add(text); button.Content = row;
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            if (key == "figures") options.Children.Add(new NativeBorder { Height = 1, Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), Margin = new Thickness(12, 6, 12, 6) });
            options.Children.Add(button);
            button.Click += async (_, _) => { if (!Active()) return; Dismiss(false, false); if (Current()) await action(); };
        }
        if (bowling)
            Add("Choose another bowler", "change-bowler", () => { ShowBowlerSelection(false); return Task.CompletedTask; }, !innings.IsComplete && !match.IsMatchFinalized);
        else
        {
            Add("Replace batter", "replace-batter", () => OpenBatterActionsAsync(playerIndex, true), !innings.IsComplete && !match.IsMatchFinalized);
            Add("Swap strike", "swap-strike", () => { match.RotateStrike(); return Task.CompletedTask; }, !innings.IsComplete && !match.IsMatchFinalized && innings.Striker is not null && innings.NonStriker is not null);
            if (playerIndex == innings.NonStrikerIndex)
                Add("Run out before delivery", "before-delivery", () => OpenUndeliveredRunOutAsync(playerIndex), match.CanRecordNonDeliveryRunOut);
        }
        Add("Edit figures", "figures", () => BeginPlayerFiguresEdit(bowling, playerIndex));
        options.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Dismiss(true, false); e.Handled = true; return; }
            if (e.Key is not (Key.Down or Key.Up or Key.Home or Key.End)) return;
            var buttons = options.Children.OfType<Button>().Where(b => b.IsEnabled && b.IsVisible).ToArray();
            if (buttons.Length == 0) return;
            int index = Array.FindIndex(buttons, b => b.IsKeyboardFocusWithin);
            int next = e.Key switch { Key.Home => 0, Key.End => buttons.Length - 1, Key.Up => (index + buttons.Length - 1) % buttons.Length, _ => (index + 1) % buttons.Length };
            buttons[next].Focus(); e.Handled = true;
        };
        KeyboardNavigation.SetTabNavigation(options, KeyboardNavigationMode.Cycle);
        popup.Closed += (_, _) => Complete();
        popup.Opened += (_, _) =>
        {
            if (!Active()) return;
            if (OperatorMotion.Enabled)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.94, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.68, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
                surface.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
            }
            Dispatcher.UIThread.Post(new Action(() =>
            {
                if (!Active() || owner is not { IsActive: true }) return;
                options.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled)?.Focus();
            }), DispatcherPriority.Input);
        };
        OperatorAppearance.Changed += PreferenceChanged;
        popup.IsOpen = true;
    }
}
