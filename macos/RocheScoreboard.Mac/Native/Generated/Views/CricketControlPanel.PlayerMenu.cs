// Generated from Views/CricketControlPanel.PlayerMenu.cs; edit translator/adapters.
// Source SHA-256: 2daa24f52da2535cb932afe863372e900103abe5d6724516e42f02f6b9ed5539
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
            RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative)
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
        bool arrivalPending = OperatorMotion.Enabled, arrivalStarted = false;
        double sourceDirectionY = 0;
        surface.Opacity = arrivalPending ? 0 : 1;
        bool Current() => ReferenceEquals(match, _match) && ReferenceEquals(innings, match.CurrentInnings) && revision == match.DeliveryRevision;
        bool Active() => !closing && !completed && generation == _playerMenuGeneration && ReferenceEquals(_playerMenu, popup);
        void Detach()
        {
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
            surface.Loaded -= SurfaceLoaded;
            arrivalPending = false;
            OperatorAppearance.Changed -= PreferenceChanged;
            surface.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(_playerMenu, popup)) { _playerMenu = null; _playerMenuAnchor = null; _dismissPlayerMenu = null; }
            if (ReferenceEquals(_retiringPlayerMenu, popup)) _retiringPlayerMenu = null;
            popup.IsOpen = false;
            popup.Child = null;
            CricketRoot.Children.Remove(popup);
        }
        bool KeepArrivalOwner()
        {
            if (!Active()) return false;
            if (!Current() || !popup.IsOpen || owner is not { IsVisible: true }
                || !anchor.IsLoaded || !anchor.IsVisible || !anchor.IsEnabled)
            {
                Dismiss(false, true);
                return false;
            }
            return true;
        }
        void SettleArrival()
        {
            // A pending arrival cancelled by Reduce must not restart on Loaded.
            arrivalPending = false;
            if (!KeepArrivalOwner()) return;
            surface.BeginAnimation(OpacityProperty, null);
            if (!KeepArrivalOwner()) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            if (!KeepArrivalOwner()) return;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (!KeepArrivalOwner()) return;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            if (!KeepArrivalOwner()) return;
            scale.ScaleX = 1;
            if (!KeepArrivalOwner()) return;
            scale.ScaleY = 1;
            if (!KeepArrivalOwner()) return;
            shift.Y = 0;
            if (!KeepArrivalOwner()) return;
            surface.Opacity = 1;
        }
        bool ContinueArrival()
        {
            if (!KeepArrivalOwner()) return false;
            if (arrivalPending && OperatorMotion.Enabled) return true;
            SettleArrival();
            return false;
        }
        bool TryMeasureOrigin(out Avalonia.Point origin, out double direction)
        {
            origin = new Avalonia.Point(.5, .5);
            direction = 0;
            double width = surface.NativeWidth(), height = surface.NativeHeight();
            double anchorWidth = anchor.NativeWidth(), anchorHeight = anchor.NativeHeight();
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
                || !double.IsFinite(anchorWidth) || !double.IsFinite(anchorHeight) || anchorWidth <= 0 || anchorHeight <= 0) return false;
            try
            {
                // Popup and source can occupy different visual roots and DPI.
                // Let each root convert real screen coordinates; never infer
                // smart placement from the requested Custom/Bottom candidate.
                var source = surface.PointFromScreen(anchor.PointToScreen(
                    new Avalonia.Point(anchorWidth * .5, anchorHeight * .5)));
                if (!double.IsFinite(source.X) || !double.IsFinite(source.Y)) return false;
                origin = new Avalonia.Point(Math.Clamp(source.X / width, 0, 1), Math.Clamp(source.Y / height, 0, 1));
                // An overlapping/clamped source already lies at the pivot.
                // Extra vertical translation would move the shell away from it.
                direction = source.Y < 0 ? -1 : source.Y > height ? 1 : 0;
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        void TryStartArrival()
        {
            if (arrivalStarted || !surface.IsLoaded || !popup.IsOpen) return;
            if (!KeepArrivalOwner()) return;
            arrivalStarted = true;
            if (!TryMeasureOrigin(out var origin, out var direction))
            {
                // A disconnected/unmeasurable root has no truthful source
                // direction. Keep its commands usable without invented travel.
                SettleArrival();
                return;
            }
            if (!KeepArrivalOwner()) return;
            sourceDirectionY = direction;
            surface.RenderTransformOrigin = new RelativePoint(origin.X, origin.Y,RelativeUnit.Relative);
            if (!ContinueArrival()) return;
            double travel = OperatorMotion.TravelScale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1 - .06 * travel, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            if (!ContinueArrival()) return;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1 - .32 * travel, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            if (!ContinueArrival()) return;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(sourceDirectionY * 18 * travel, 0, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            if (!ContinueArrival()) return;
            surface.Opacity = 1;
            if (!ContinueArrival()) return;
            surface.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
            if (!ContinueArrival()) return;
            arrivalPending = false;
        }
        void SurfaceLoaded(object? sender, RoutedEventArgs e) => TryStartArrival();
        void PreferenceChanged()
        {
            if (OperatorMotion.Enabled || completed) return;
            if (closing) { Complete(); return; }
            SettleArrival();
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
            if (immediate || arrivalPending || !OperatorMotion.Enabled || !surface.IsLoaded || !popup.IsOpen) { Complete(); return; }
            double travel = OperatorMotion.TravelScale;
            var duration = OperatorMotion.SurfaceClose;
            var fade = new DoubleAnimation(surface.Opacity, 0, duration) { EasingFunction = OperatorMotion.SmoothOut };
            fade.Completed += (_, _) => Complete();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, 1 - .06 * travel, duration) { EasingFunction = OperatorMotion.SmoothOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, 1 - .06 * travel, duration) { EasingFunction = OperatorMotion.SmoothOut });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift.Y, sourceDirectionY * 5 * travel, duration) { EasingFunction = OperatorMotion.SmoothOut });
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
            Add("Choose another bowler", "change-bowler", () => { ShowBowlerSelection(false, anchor); return Task.CompletedTask; }, !innings.IsComplete && !match.IsMatchFinalized);
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
            TryStartArrival();
            Dispatcher.UIThread.Post(new Action(() =>
            {
                if (!Active() || owner is not { IsActive: true }) return;
                options.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled)?.Focus();
            }), DispatcherPriority.Input);
        };
        surface.Loaded += SurfaceLoaded;
        OperatorAppearance.Changed += PreferenceChanged;
        // Keep the transient popup in this panel's logical/resource lifetime.
        // Its child must be rooted before opening so menu rows can load, focus
        // and inherit the same resources as the persistent source control.
        CricketRoot.Children.Add(popup);
        if (!Active() || !Current()) { if (Active()) Dismiss(false, true); return; }
        popup.IsOpen = true;
    }
    private bool UpdatePlayerMenuNames()
    {
        var match = _match;
        var innings = match?.CurrentInnings;
        int top = _cpTopBatterIndex, bottom = _cpBottomBatterIndex;
        int striker = innings?.StrikerIndex ?? -1, nonStriker = innings?.NonStrikerIndex ?? -1;
        int bowlerIndex = innings?.CurrentBowlerIndex ?? -1;
        var topPlayer = innings is not null && top >= 0 && top < innings.BattingOrder.Count ? innings.BattingOrder[top] : null;
        var bottomPlayer = innings is not null && bottom >= 0 && bottom < innings.BattingOrder.Count ? innings.BattingOrder[bottom] : null;
        var bowler = innings?.CurrentBowler;
        string topName = topPlayer?.FullName ?? "", bottomName = bottomPlayer?.FullName ?? "", bowlerName = bowler?.FullName ?? "";
        bool Current() => ReferenceEquals(match, _match) && ReferenceEquals(innings, match?.CurrentInnings)
            && top == _cpTopBatterIndex && bottom == _cpBottomBatterIndex
            && striker == (innings?.StrikerIndex ?? -1) && nonStriker == (innings?.NonStrikerIndex ?? -1)
            && bowlerIndex == (innings?.CurrentBowlerIndex ?? -1) && ReferenceEquals(bowler, innings?.CurrentBowler)
            && ReferenceEquals(topPlayer, innings is not null && top >= 0 && top < innings.BattingOrder.Count ? innings.BattingOrder[top] : null)
            && ReferenceEquals(bottomPlayer, innings is not null && bottom >= 0 && bottom < innings.BattingOrder.Count ? innings.BattingOrder[bottom] : null)
            && topName == (topPlayer?.FullName ?? "") && bottomName == (bottomPlayer?.FullName ?? "") && bowlerName == (bowler?.FullName ?? "");
        string BatterLabel(int index, string name, bool hasPlayer)
        {
            if (!hasPlayer) return "Batter options";
            string role = index == striker ? "Striker" : index == nonStriker ? "Non-striker" : "Batter";
            return string.IsNullOrWhiteSpace(name) ? role + " options" : role + " options for " + name;
        }
        string bowlerLabel = string.IsNullOrWhiteSpace(bowlerName) ? "Bowler options" : "Bowler options for " + bowlerName;
        Avalonia.Automation.AutomationProperties.SetName(StrikerMenuButton, BatterLabel(top, topName, topPlayer is not null));
        if (!Current()) return false;
        Avalonia.Automation.AutomationProperties.SetName(NonStrikerMenuButton, BatterLabel(bottom, bottomName, bottomPlayer is not null));
        if (!Current()) return false;
        Avalonia.Automation.AutomationProperties.SetName(BowlerMenuButton, bowlerLabel);
        return Current();
    }

}
