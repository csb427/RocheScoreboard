using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Operator-side control panel for Training mode. Owns the
/// <see cref="TrainingSession"/> and exposes Add / Start / Pause / Skip /
/// Reset to drive the live scorebug.
/// </summary>
public partial class TrainingControlPanel : UserControl
{
    public event Action? ExitRequested;
    // Exactly one root owner commits a captured session. The public event remains
    // a notification for standalone hosts; it is not root teardown authority.
    internal Func<TrainingSession, bool>? ConfirmedExitCommit { get; set; }

    private TrainingSession? _session;
    private readonly DispatcherTimer _ui;
    private Guid? _editingTimerId;
    private readonly HashSet<Guid> _revealedTimerIds = [];
    private readonly Dictionary<Guid, Button> _editCommands = [];
    private enum QueueAction { MoveUp, MoveDown, Remove }
    private readonly Dictionary<(Guid TimerId, QueueAction Action), Button> _queueCommands = [];
    private int _queueCommandGeneration;
    private readonly Dictionary<Button, (TrainingSession Session, int Generation, string Title, int Seconds)> _recentCommands = [];
    private int _recentRefreshGeneration;
    private TimerCorrection? _correction;
    private bool _refreshingAutoAdvance;

    // Keep the actual editor attached across unrelated queue changes. Its raw
    // text, caret, selection and keyboard focus belong to the operator until
    // Save/Cancel, rather than to a transient rendering of the model.
    private sealed class TimerCorrection
    {
        internal readonly TrainingSession Session;
        internal readonly TrainingTimer Timer;
        internal readonly string BaselineTitle;
        internal readonly TimeSpan BaselineDuration;
        internal readonly string InitialMinutes;
        internal readonly string InitialSeconds;
        internal Border? Row;
        internal TextBlock? IndexText;

        internal TimerCorrection(TrainingSession session, TrainingTimer timer)
        {
            Session = session;
            Timer = timer;
            BaselineTitle = timer.Title;
            BaselineDuration = timer.Duration;
            long seconds = Math.Max(1L, (long)Math.Round(timer.Duration.TotalSeconds));
            InitialMinutes = (seconds / 60).ToString(CultureInfo.InvariantCulture);
            InitialSeconds = (seconds % 60).ToString("D2", CultureInfo.InvariantCulture);
        }
    }

    public TrainingControlPanel()
    {
        InitializeComponent();
        SharedSurfaceMotion.AttachFrom(TrainingClearReviewShell, ClearAllButton);
        SharedSurfaceMotion.AttachFrom(TrainingExitReviewShell, ExitButton);
        TrainingEntrySymbol.Loaded += (_, _) => { if (IsVisible) TrainingEntrySymbol.Activate(); };
        OperatorFeedback.SetManaged(StartPauseButton,true);
        CricketActionMotion.SetEnabled(StartPauseButton,false);
        _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _ui.Tick += (_, _) => RefreshLiveText();
        Loaded += (_, _) =>
        {
            ++_queueCommandGeneration;
            _ui.Start();
            // Pull keyboard focus when the panel loads so global shortcuts work
            // straight away (Space/S/R/Esc).
            if (!IsKeyboardFocusWithin) { Focus(); Keyboard.Focus(this); }
            RefreshRecentTimers();
        };
        Unloaded += (_, _) =>
        {
            RetireExitReview(restoreFocus: false);
            RetireClearReview(restoreFocus: false);
            ++_queueCommandGeneration;
            _ui.Stop(); HideDropIndicator(); _dragArmed = false;
            foreach (var row in QueueItems.Items.OfType<Border>())
            {
                row.BeginAnimation(OpacityProperty, null);
                if (row.RenderTransform is TranslateTransform shift) { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = 0; }
            }
        };
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is false) { RetireExitReview(restoreFocus: false); RetireClearReview(restoreFocus: false); ++_queueCommandGeneration; _ui.Stop(); }
            else if (IsLoaded) { _ui.Start(); RefreshLiveText(); RefreshRecentTimers(); }
        };
        // Operator shortcuts at the panel level — only when focus isn't in a
        // TextBox so typing into the title or number fields still works.
        PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only OS auto-repeat is suppressed: a fresh press/release still acts.
        // A held Enter must not accept a newer scope exposed by cancellation.
        if (e.Key == Key.Enter && e.IsRepeat) { e.Handled = true; return; }
        if (_exitReview is { } exitReview)
        {
            // Preserve native owner close/system-menu affordances before this
            // panel owns decision and transport keys. The translator already
            // supports this WPF SystemKey normalization expression.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && (key == Key.F4 || key == Key.Space)) return;
            if (e.Key == Key.Escape)
            { if (!e.IsRepeat) RetireExitReview(restoreFocus: true); e.Handled = true; return; }
            if (e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                var target = exitReview.Cancel.IsKeyboardFocusWithin ? exitReview.Confirm : exitReview.Cancel;
                target.Focus(); Keyboard.Focus(target); target.BringIntoView();
                e.Handled = true; return;
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (exitReview.Confirm.IsKeyboardFocusWithin) ConfirmExitReview(exitReview);
                else RetireExitReview(restoreFocus: true);
                e.Handled = true; return;
            }
            // Read long context without leaving the safe decision buttons or
            // forwarding transport/scoring keys to the underlying console.
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                if (key is Key.PageDown or Key.PageUp or Key.Down or Key.Up)
                {
                    double step = key is Key.Down or Key.Up ? 32 : Math.Max(32, TrainingExitReviewScroll.ViewportHeight * .8);
                    if (key is Key.PageUp or Key.Up) step = -step;
                    TrainingExitReviewScroll.ScrollToVerticalOffset(Math.Max(0, TrainingExitReviewScroll.VerticalOffset + step));
                }
                else if (key == Key.Home) TrainingExitReviewScroll.ScrollToVerticalOffset(0);
                else if (key == Key.End) TrainingExitReviewScroll.ScrollToEnd();
            }
            // Space belongs to the two actual focused buttons, never transport.
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None
                && (exitReview.Cancel.IsKeyboardFocusWithin || exitReview.Confirm.IsKeyboardFocusWithin)) return;
            e.Handled = true; return;
        }
        if (_clearReview is { } clearReview)
        {
            // Preserve native owner close/system-menu affordances before this
            // panel owns decision and transport keys. The translator already
            // supports this WPF SystemKey normalization expression.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && (key == Key.F4 || key == Key.Space)) return;
            if (e.Key == Key.Escape)
            { RetireClearReview(restoreFocus: true); e.Handled = true; return; }
            if (e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                var target = clearReview.Cancel.IsKeyboardFocusWithin ? clearReview.Confirm : clearReview.Cancel;
                target.Focus(); Keyboard.Focus(target); target.BringIntoView();
                e.Handled = true; return;
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (clearReview.Confirm.IsKeyboardFocusWithin) ConfirmClearReview(clearReview);
                else RetireClearReview(restoreFocus: true);
                e.Handled = true; return;
            }
            // Read long context without leaving the safe decision buttons or
            // forwarding transport/scoring keys to the underlying console.
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                if (key is Key.PageDown or Key.PageUp or Key.Down or Key.Up)
                {
                    double step = key is Key.Down or Key.Up ? 32 : Math.Max(32, TrainingClearReviewScroll.ViewportHeight * .8);
                    if (key is Key.PageUp or Key.Up) step = -step;
                    TrainingClearReviewScroll.ScrollToVerticalOffset(Math.Max(0, TrainingClearReviewScroll.VerticalOffset + step));
                }
                else if (key == Key.Home) TrainingClearReviewScroll.ScrollToVerticalOffset(0);
                else if (key == Key.End) TrainingClearReviewScroll.ScrollToEnd();
            }
            // Space belongs to the two actual focused buttons, never transport.
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None
                && (clearReview.Cancel.IsKeyboardFocusWithin || clearReview.Confirm.IsKeyboardFocusWithin)) return;
            e.Handled = true; return;
        }
        // A nested remote disclosure owns Escape before correction/Exit. WPF
        // routes its owner preview first; the native bridge must preserve that
        // priority even when its owner handler is reached later in the route.
        if (e.Key == Key.Escape && TrainingCloudLauncher.OwnsEscapeKey) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        // A held Escape must not act on the scope revealed by cancellation.
        if (e.Key == Key.Escape && e.IsRepeat) { e.Handled = true; return; }
        // Escape dismisses the current correction scope even when Save or
        // Cancel, rather than an input field, owns keyboard focus.
        if (e.Key == Key.Escape && _correction is { } correction)
        {
            CancelCorrection(correction);
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;
        // Space/Enter belong to a focused command or check box. Do not
        // hijack their native activation to start or pause the session.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase && e.Key is Key.Space or Key.Enter) return;

        switch (e.Key)
        {
            case Key.Space:
                OnStartPauseClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S:
                OnSkipClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.R:
                OnResetActiveClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Escape:
                OnExitClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    /// <summary>Pressing Enter in the title box adds the timer.</summary>
    private void OnNewTitleBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddTimerClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>Restrict numeric input fields to digits only.</summary>
    private void OnNumericPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>Pressing Enter in any numeric field adds the timer.</summary>
    private void OnNumericKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // For interval-builder fields, build the session; otherwise add one timer.
            if (sender is TextBox tb && (tb.Name == nameof(IntervalRoundsBox) ||
                                          tb.Name == nameof(IntervalWorkBox) ||
                                          tb.Name == nameof(IntervalRestBox)))
            {
                OnBuildIntervalClicked(this, new RoutedEventArgs());
            }
            else
            {
                OnAddTimerClicked(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
    }

    internal void ShowSessionEntry() => TrainingEntrySymbol.Activate();

    public void Bind(TrainingSession session)
    {
        RetireExitReview(restoreFocus: false);
        RetireClearReview(restoreFocus: false);
        ++_queueCommandGeneration;
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session.StructuralChanged -= OnStructuralChanged;
        }
        _correction = null;
        _editingTimerId = null;
        TrainingCorrectionNotice.Text = "";
        TrainingCorrectionNotice.Visibility = Visibility.Collapsed;
        _session = session;
        _revealedTimerIds.IntersectWith(session.Timers.Select(timer => timer.Id));
        _session.Changed += OnSessionChanged;
        _session.StructuralChanged += OnStructuralChanged;
        OnStructuralChanged();
        OnSessionChanged();
        RefreshRecentTimers();
    }

    private void OnAutoAdvanceChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshingAutoAdvance || _session is null) return;
        _session.AutoAdvance = AutoAdvanceToggle.IsChecked == true;
    }

    private void RefreshAutoAdvance()
    {
        if (_session is null || AutoAdvanceToggle.IsChecked == _session.AutoAdvance) return;
        bool previousRefresh = _refreshingAutoAdvance;
        _refreshingAutoAdvance = true;
        try { AutoAdvanceToggle.IsChecked = _session.AutoAdvance; }
        finally { _refreshingAutoAdvance = previousRefresh; }
    }

    // ─────────── Live UI ───────────

    /// <summary>
    /// Light-weight refresh fired on every model tick. Intentionally avoids
    /// rebuilding the queue list (which animates rows in) so the UI doesn't
    /// glitch 30× per second while the clock runs.
    /// </summary>
    private void OnSessionChanged()
    {
        RefreshAutoAdvance();
        RefreshActiveCard();
        RefreshTransport();
        RefreshSessionTotal();
    }

    /// <summary>
    /// Heavy refresh fired only when the queue's structure changes (add /
    /// remove / reorder / advance / clear). Rebuilds the visible queue list.
    /// </summary>
    private void OnStructuralChanged()
    {
        RebuildQueueList();
    }

    private void RefreshLiveText()
    {
        if (_session is null) return;
        var active = _session.Active;
        ActiveRemainingText.Text = active is null
            ? "00:00"
            : FormatTime(_session.ActiveRemaining);
        SessionTotalText.Text = $"Total: {FormatTime(_session.TotalRemaining)}";

        // Status colour
        if (active is null)
            ActiveStatusText.Foreground = ColorFromHex("#58A6FF");
        else if (active.HasFinished)
            ActiveStatusText.Foreground = ColorFromHex("#FF6B6B");
        else if (!_session.IsRunning)
            ActiveStatusText.Foreground = ColorFromHex("#FBBF24");
        else
            ActiveStatusText.Foreground = ColorFromHex("#3FB950");
    }

    private void RefreshActiveCard()
    {
        if (_session is null) return;
        var active = _session.Active;

        if (active is null)
        {
            ActiveTitleText.Text = "No timer";
            ActiveStatusText.Text = "Add a timer below to begin";
            return;
        }

        ActiveTitleText.Text = active.Title;
        if (active.HasFinished) ActiveStatusText.Text = "FINISHED";
        else if (!_session.IsRunning) ActiveStatusText.Text = "PAUSED";
        else ActiveStatusText.Text = "RUNNING";
    }

    private void RefreshTransport()
    {
        if (_session is null) return;
        bool running = _session.IsRunning;
        TrainingTransportLabel.Text = running ? "PAUSE" : "START";
        TrainingTransportSymbol.IsActive = running;
        System.Windows.Automation.AutomationProperties.SetName(StartPauseButton,running?"Pause active timer":"Start active timer");
        // High-contrast colours: when running we show a strong red (pause to
        // stop), when paused we show a strong green (start to go).
        StartPauseButton.Background = running ? ColorFromHex("#7F1D1D") : ColorFromHex("#166534");
        StartPauseButton.Foreground = running ? ColorFromHex("#FEE2E2") : ColorFromHex("#DCFCE7");
        StartPauseButton.BorderBrush = running ? ColorFromHex("#EF4444") : ColorFromHex("#22C55E");

        ClearAllButton.IsEnabled = _session.Timers.Count > 0;
        bool hasActive = _session.Active is not null && !_session.IsExhausted;
        StartPauseButton.IsEnabled = hasActive;
        ResetActiveButton.IsEnabled = hasActive;
        SkipButton.IsEnabled = hasActive;
        AddOneMinButton.IsEnabled = hasActive;
        AddTenSecButton.IsEnabled = hasActive;
    }

    private void RefreshSessionTotal()
    {
        if (_session is null) return;
        QueueCountText.Text = $"{_session.Timers.Count} timer{(_session.Timers.Count == 1 ? "" : "s")}";
    }

    private void RebuildQueueList()
    {
        if (_session is null) return;
        if (_correction is { } correction && !IsCurrentCorrection(correction))
        {
            _correction = null;
            _editingTimerId = null;
            ShowCorrectionNotice("The timer being corrected is no longer available. No correction was saved.");
        }

        // Removing/readding the editor would fire Loaded again and drop its
        // focus/selection. Reconcile its neighbors while it stays attached.
        Border? editor = _correction?.Row;
        if (editor is null) QueueItems.Items.Clear();
        else
            for (int i = QueueItems.Items.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(QueueItems.Items[i], editor)) QueueItems.Items.RemoveAt(i);
        _editCommands.Clear();
        _queueCommands.Clear();
        _revealedTimerIds.IntersectWith(_session.Timers.Select(timer => timer.Id));
        for (int i = 0; i < _session.Timers.Count; i++)
        {
            var t = _session.Timers[i];
            var row = BuildQueueRow(t, i, _session.ActiveIndex);
            if (editor is null) QueueItems.Items.Add(row);
            else if (!ReferenceEquals(row, editor)) QueueItems.Items.Insert(i, row);
        }
    }

    private bool IsCurrentCorrection(TimerCorrection correction)
    {
        if (!ReferenceEquals(_correction, correction) || !ReferenceEquals(_session, correction.Session)
            || _editingTimerId != correction.Timer.Id || correction.Timer.HasFinished) return false;
        int index = correction.Session.Timers.IndexOf(correction.Timer);
        return index >= 0 && index >= correction.Session.ActiveIndex;
    }

    private void ShowCorrectionNotice(string message)
    {
        TrainingCorrectionNotice.Text = message;
        TrainingCorrectionNotice.Visibility = Visibility.Visible;
    }

    private void BeginCorrection(TrainingTimer timer)
    {
        if (_session is null || timer.HasFinished) return;
        int index = _session.Timers.IndexOf(timer);
        if (index < 0 || index < _session.ActiveIndex) return;
        if (_correction is not null)
        {
            ShowCorrectionNotice("Save or cancel the open timer correction before editing another timer. Your edits are still intact.");
            OperatorViewport.RevealValidationError(TrainingCorrectionNotice);
            return;
        }
        TrainingCorrectionNotice.Text = "";
        TrainingCorrectionNotice.Visibility = Visibility.Collapsed;
        _correction = new TimerCorrection(_session, timer);
        _editingTimerId = timer.Id;
        RebuildQueueList();
    }

    private void FocusCorrectionCommand(TimerCorrection correction)
    {
        if (!ReferenceEquals(_session, correction.Session) || _correction is not null || !IsLoaded || !IsVisible) return;
        QueueItems.UpdateLayout();
        if (_editCommands.TryGetValue(correction.Timer.Id, out var edit))
        {
            edit.Focus();
            Keyboard.Focus(edit);
        }
    }

    private void CancelCorrection(TimerCorrection correction)
    {
        if (!IsCurrentCorrection(correction)) return;
        _correction = null;
        _editingTimerId = null;
        TrainingCorrectionNotice.Text = "";
        TrainingCorrectionNotice.Visibility = Visibility.Collapsed;
        RebuildQueueList();
        FocusCorrectionCommand(correction);
    }

    private void ExecuteQueueCommand(TrainingSession session, TrainingTimer timer, QueueAction action, Button source)
    {
        var owner = Window.GetWindow(this);
        int generation = _queueCommandGeneration;
        bool CurrentScope() => generation == _queueCommandGeneration && ReferenceEquals(_session, session)
            && IsLoaded && IsVisible && IsEnabled && owner is { IsVisible: true, IsEnabled: true }
            && ReferenceEquals(Window.GetWindow(this), owner);
        if (!CurrentScope() || !source.IsEnabled
            || !_queueCommands.TryGetValue((timer.Id, action), out var current) || !ReferenceEquals(current, source)) return;
        int index = session.Timers.IndexOf(timer);
        if (index < 0 || action == QueueAction.MoveUp && index == 0
            || action == QueueAction.MoveDown && index == session.Timers.Count - 1) return;
        bool restoreFocus = source.IsKeyboardFocusWithin;

        // The model operation stays immediate. Its synchronous observers can
        // replace/hide this scope or deliberately focus another input.
        if (action == QueueAction.Remove) session.RemoveTimer(timer);
        else session.MoveTimer(timer, index + (action == QueueAction.MoveUp ? -1 : 1));
        if (!restoreFocus || !CurrentScope()) return;

        bool FocusAvailable(Button? candidate = null)
        {
            var focused = Keyboard.FocusedElement;
            return focused is null || ReferenceEquals(focused, source) || ReferenceEquals(focused, candidate);
        }
        if (!FocusAvailable()) return;
        QueueItems.UpdateLayout();
        if (!CurrentScope() || !FocusAvailable()) return;
        Button target = FindQueueFocusTarget(session, timer, action, index);
        if (!target.IsEnabled || !target.IsVisible || !FocusAvailable(target)) return;
        if (target.Focus() && CurrentScope() && FocusAvailable(target)) Keyboard.Focus(target);
        // Explicit focus recovery should reveal its current command without
        // allowing unrelated pointer focus to scroll the console underneath it.
        if (CurrentScope() && target.IsKeyboardFocusWithin)
            OperatorViewport.RevealValidationError(target);
    }

    private Button FindQueueFocusTarget(TrainingSession session, TrainingTimer timer, QueueAction action, int previousIndex)
    {
        Button? Available(Guid id, QueueAction kind)
            => _queueCommands.TryGetValue((id, kind), out var command) && command.IsEnabled ? command : null;
        if (action != QueueAction.Remove)
        {
            if (Available(timer.Id, action) is { } equivalent) return equivalent;
            // Reaching the first/last slot disables that direction. Keep focus
            // on the same timer's safe Edit action without invoking it.
            if (_editCommands.TryGetValue(timer.Id, out var edit) && edit.IsEnabled) return edit;
            QueueAction opposite = action == QueueAction.MoveUp ? QueueAction.MoveDown : QueueAction.MoveUp;
            return Available(timer.Id, opposite) ?? AddTimerButton;
        }
        if (session.Timers.Count == 0) return AddTimerButton;
        int nearest = Math.Min(previousIndex, session.Timers.Count - 1);
        for (int index = nearest; index < session.Timers.Count; index++)
            if (Available(session.Timers[index].Id, QueueAction.Remove) is { } next) return next;
        for (int index = nearest - 1; index >= 0; index--)
            if (Available(session.Timers[index].Id, QueueAction.Remove) is { } previous) return previous;
        return AddTimerButton;
    }

    private FrameworkElement BuildQueueRow(TrainingTimer t, int index, int activeIndex)
    {
        bool isActive = index == activeIndex && !t.HasFinished;
        bool isFinished = t.HasFinished || index < activeIndex;
        string accent = isActive ? "#58A6FF" : isFinished ? "#374151" : "#6B7280";

        bool isEditing = _editingTimerId == t.Id && !isFinished;

        if (isEditing && _correction?.Row is { } editor)
        {
            if (_correction.IndexText is { } label) label.Text = $"#{index + 1}";
            return editor;
        }

        var border = new Border
        {
            Background = ColorFromHex("#10161D"),
            BorderBrush = ColorFromHex(isEditing ? "#FBBF24" : isActive ? "#58A6FF" : "#1F2937"),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(8, 6, 8, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Opacity = isFinished ? 0.45 : 1.0
        };
        if (isActive)
        {
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 0,
                Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF")!,
                Opacity = 0.4
            };
        }

        if (isEditing)
        {
            border.Child = BuildEditRowContent(t, index, accent);
            _correction!.Row = border;
            // No drag for the edit row — would steal focus from the inputs.
            border.Cursor = Cursors.Arrow;
            border.Opacity = 1.0;
            return border;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Index pill
        var idxBorder = new Border
        {
            Background = ColorFromHex("#0D1117"),
            BorderBrush = ColorFromHex(accent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        idxBorder.Child = new TextBlock
        {
            Text = $"#{index + 1}",
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = ColorFromHex(accent)
        };
        Grid.SetColumn(idxBorder, 0);
        grid.Children.Add(idxBorder);

        // Title
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = t.Title,
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Black,
            FontSize = 16,
            Foreground = ColorFromHex(isFinished ? "#6B7280" : "#FFFFFF"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (isActive)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = "ACTIVE",
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Black,
                FontSize = 10,
                Foreground = ColorFromHex("#58A6FF"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        else if (isFinished)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = "DONE",
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = ColorFromHex("#6B7280"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        Grid.SetColumn(titleStack, 1);
        grid.Children.Add(titleStack);

        // Duration
        var dur = new TextBlock
        {
            Text = FormatTime(t.Duration),
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Black,
            FontSize = 22,
            Foreground = ColorFromHex(isActive ? "#58A6FF" : isFinished ? "#6B7280" : "#E5E7EB"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        Grid.SetColumn(dur, 2);
        grid.Children.Add(dur);

        // Action stack: Up / Down / Remove
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var rowSession = _session!;
        Button up = null!;
        up = MakeIconButton("▲", "Move up", () => ExecuteQueueCommand(rowSession, t, QueueAction.MoveUp, up));
        up.IsEnabled = index > 0;
        System.Windows.Automation.AutomationProperties.SetName(up, $"Move up {t.Title}, timer {index + 1}");
        _queueCommands[(t.Id, QueueAction.MoveUp)] = up;
        actions.Children.Add(up);
        Button down = null!;
        down = MakeIconButton("▼", "Move down", () => ExecuteQueueCommand(rowSession, t, QueueAction.MoveDown, down));
        down.IsEnabled = index < rowSession.Timers.Count - 1;
        System.Windows.Automation.AutomationProperties.SetName(down, $"Move down {t.Title}, timer {index + 1}");
        _queueCommands[(t.Id, QueueAction.MoveDown)] = down;
        actions.Children.Add(down);
        if (!isFinished)
        {
            var edit = MakeIconButton("✎", "Edit title and duration", () => BeginCorrection(t));
            System.Windows.Automation.AutomationProperties.SetName(edit, $"Edit {t.Title}, title and duration");
            _editCommands[t.Id] = edit;
            actions.Children.Add(edit);
        }
        Button remove = null!;
        remove = MakeIconButton("✕", "Remove", () => ExecuteQueueCommand(rowSession, t, QueueAction.Remove, remove));
        System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove {t.Title}, timer {index + 1}");
        _queueCommands[(t.Id, QueueAction.Remove)] = remove;
        actions.Children.Add(remove);

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        border.Child = grid;

        var surface = (SolidColorBrush)border.Background;
        void PaintRow()
        {
            Color target = (Color)System.Windows.Media.ColorConverter.ConvertFromString(border.IsMouseOver || border.IsKeyboardFocusWithin ? "#192A3D" : "#10161D")!;
            Color from = surface.Color; surface.BeginAnimation(SolidColorBrush.ColorProperty, null); surface.Color = target;
            if (OperatorMotion.Enabled) surface.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, target,
                border.IsMouseOver || border.IsKeyboardFocusWithin ? OperatorMotion.HoverIn : OperatorMotion.HoverOut)
            { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
        }
        border.MouseEnter += (_, _) => PaintRow(); border.MouseLeave += (_, _) => PaintRow();
        border.GotKeyboardFocus += (_, _) => PaintRow(); border.LostKeyboardFocus += (_, _) => PaintRow();
        border.Unloaded += (_, _) => { surface.BeginAnimation(SolidColorBrush.ColorProperty, null); surface.Color = Color.FromRgb(0x10, 0x16, 0x1D); };

        // ─── Drag-to-reorder ──────────────────────────────────────────
        // Grab anywhere on the row (except the action buttons, which keep
        // their click semantics) and drag onto another row to reorder.
        border.Cursor = Cursors.SizeAll;
        border.ToolTip = "Drag to reorder";
        AttachRowDragHandlers(border, t);

        // Slide-in animation for newly added rows. Suppressed during a drag
        // reorder so the queue doesn't visibly re-animate every row when
        // the model rebuilds — that's what was causing the glitchy feel.
        bool firstAppearance = _revealedTimerIds.Add(t.Id);
        bool reveal = !_suppressNextRowAnim && !isFinished && firstAppearance && OperatorMotion.Enabled;
        border.Opacity = isFinished ? .45 : 1;
        border.RenderTransform = new TranslateTransform();
        if (reveal)
        {
            border.Loaded += (_, _) =>
            {
                if (!OperatorMotion.Enabled) return;
                border.BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
                ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(8, 0, OperatorMotion.Page) { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut });
            };
            border.Unloaded += (_, _) => { border.BeginAnimation(OpacityProperty, null); ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null); };
        }

        return border;
    }

    /// <summary>Set during a drag-drop reorder so newly built rows don't animate in.</summary>
    private bool _suppressNextRowAnim;

    /// <summary>
    /// Builds the inline-edit layout for a queued timer. Shows editable
    /// title, minutes and seconds fields plus Save / Cancel buttons. Enter
    /// saves, Escape cancels.
    /// </summary>
    private FrameworkElement BuildEditRowContent(TrainingTimer t, int index, string accent)
    {
        var correction = _correction ?? throw new InvalidOperationException("A timer correction must own its editor.");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var idxBorder = new Border
        {
            Background = ColorFromHex("#0D1117"),
            BorderBrush = ColorFromHex("#FBBF24"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        correction.IndexText = new TextBlock
        {
            Text = $"#{index + 1}",
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = ColorFromHex("#FBBF24")
        };
        idxBorder.Child = correction.IndexText;
        idxBorder.ToolTip = "Correcting this timer";
        Grid.SetColumn(idxBorder, 0);
        grid.Children.Add(idxBorder);

        var titleBox = new TextBox
        {
            Text = correction.BaselineTitle,
            Style = (Style)FindResource("InputBox"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 120
        };
        System.Windows.Automation.AutomationProperties.SetName(titleBox, $"Timer title for {correction.BaselineTitle}");
        Grid.SetColumn(titleBox, 1);
        grid.Children.Add(titleBox);

        var timeStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var minBox = new TextBox
        {
            Text = correction.InitialMinutes,
            Style = (Style)FindResource("InputBox"),
            Width = 50,
            TextAlignment = TextAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(minBox, $"Minutes for {correction.BaselineTitle}");
        minBox.ToolTip = "Whole, non-negative minutes";
        minBox.PreviewTextInput += OnNumericPreviewTextInput;
        var colon = new TextBlock
        {
            Text = ":",
            Foreground = ColorFromHex("#E5E7EB"),
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Black,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        var secBox = new TextBox
        {
            Text = correction.InitialSeconds,
            Style = (Style)FindResource("InputBox"),
            Width = 50,
            TextAlignment = TextAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(secBox, $"Seconds for {correction.BaselineTitle}");
        secBox.ToolTip = "Whole, non-negative seconds";
        secBox.PreviewTextInput += OnNumericPreviewTextInput;
        timeStack.Children.Add(minBox);
        timeStack.Children.Add(colon);
        timeStack.Children.Add(secBox);
        Grid.SetColumn(timeStack, 2);
        grid.Children.Add(timeStack);

        var error = new TextBlock
        {
            Tag = "training-edit-error", Visibility = Visibility.Collapsed,
            Foreground = FindResource("Training.ErrorBrush") as System.Windows.Media.Brush ?? throw new InvalidOperationException("Training validation brush is missing."), FontSize = 14,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(error, 1); Grid.SetColumnSpan(error, 5); grid.Children.Add(error);

        void Reject(string message, TextBox field)
        {
            error.Text = message;
            error.Visibility = Visibility.Visible;
            OperatorViewport.RevealValidationError(error);
            field.Focus();
            Keyboard.Focus(field);
        }

        void Commit()
        {
            if (!IsCurrentCorrection(correction) || !grid.IsLoaded || !IsVisible) return;
            string rawTitle = titleBox.Text ?? "";
            string title = string.IsNullOrWhiteSpace(rawTitle) ? "TIMER" : rawTitle.Trim();
            bool titleChanged = !string.Equals(title, correction.BaselineTitle, StringComparison.Ordinal);
            bool durationTextChanged = minBox.Text != correction.InitialMinutes || secBox.Text != correction.InitialSeconds;
            TimeSpan duration = correction.BaselineDuration;
            if (durationTextChanged)
            {
                if (!TryReadTimerDuration(minBox.Text, secBox.Text, out int totalSeconds, out string message))
                {
                    TextBox field = int.TryParse(minBox.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _) ? secBox : minBox;
                    Reject(message, field);
                    return;
                }
                duration = TimeSpan.FromSeconds(totalSeconds);
            }
            bool durationChanged = durationTextChanged && duration != correction.BaselineDuration;
            // Merge fields the operator did not change. In particular, a title
            // correction must not roll back time already added to the clock.
            // Conflicting edits to the same field require an explicit review.
            if (titleChanged && t.Title != correction.BaselineTitle && t.Title != title)
            {
                Reject("This timer's title changed elsewhere. Your correction is still intact. Cancel and reopen the timer to review its current title.", titleBox);
                return;
            }
            if (durationChanged && t.Duration != correction.BaselineDuration && t.Duration != duration)
            {
                Reject("This timer's duration changed while the correction was open. Your edits are still intact. Cancel and reopen the timer to review its current duration.", minBox);
                return;
            }
            bool changesModel = (titleChanged && t.Title != title) || (durationChanged && t.Duration != duration);
            _correction = null;
            _editingTimerId = null;
            TrainingCorrectionNotice.Text = "";
            TrainingCorrectionNotice.Visibility = Visibility.Collapsed;
            // UpdateTimer raises one synchronous structural event when changed;
            // do not rebuild the editor once inside it and again on completion.
            if (changesModel) correction.Session.UpdateTimer(t, titleChanged ? title : null, durationChanged ? duration : null);
            else RebuildQueueList();
            FocusCorrectionCommand(correction);
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelCorrection(correction); e.Handled = true; }
        }
        titleBox.KeyDown += OnKey;
        minBox.KeyDown += OnKey;
        secBox.KeyDown += OnKey;

        var saveBtn = MakeIconButton("✓", "Save (Enter)", Commit);
        saveBtn.Background = ColorFromHex("#166534");
        saveBtn.Foreground = ColorFromHex("#DCFCE7");
        saveBtn.BorderBrush = ColorFromHex("#22C55E");
        System.Windows.Automation.AutomationProperties.SetName(saveBtn, $"Save correction for {correction.BaselineTitle}");
        Grid.SetColumn(saveBtn, 3);
        grid.Children.Add(saveBtn);

        var cancelBtn = MakeIconButton("✕", "Cancel (Esc)", () => CancelCorrection(correction));
        System.Windows.Automation.AutomationProperties.SetName(cancelBtn, $"Cancel correction for {correction.BaselineTitle}");
        Grid.SetColumn(cancelBtn, 4);
        grid.Children.Add(cancelBtn);

        // Focus only on explicit entry, never on an unrelated queue refresh or
        // a late Loaded callback from a replaced correction/session.
        bool firstLoad = true;
        titleBox.Loaded += (_, _) =>
        {
            if (!firstLoad || !IsCurrentCorrection(correction) || !IsVisible) return;
            firstLoad = false;
            titleBox.SelectAll();
            titleBox.Focus();
            Keyboard.Focus(titleBox);
        };

        return grid;
    }

    // ─────────── Drag-and-drop reordering ───────────

    private const string DragDataFormat = "Roche.TrainingTimer";
    private System.Windows.Point _dragStart;
    private bool _dragArmed;
    private TrainingTimer? _draggedTimer;
    private Border? _draggedRow;

    private void AttachRowDragHandlers(Border row, TrainingTimer timer)
    {
        row.AllowDrop = true;

        row.PreviewMouseLeftButtonDown += (s, e) =>
        {
            // Don't start a drag if the click landed on a button (so the
            // up/down/remove buttons still work).
            if (e.OriginalSource is DependencyObject d && IsInsideButton(d)) return;
            _dragArmed = true;
            _draggedTimer = timer;
            _draggedRow = row;
            _dragStart = e.GetPosition(QueueItems);
        };

        row.PreviewMouseMove += (s, e) =>
        {
            if (!_dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragArmed = false;
                return;
            }
            var pos = e.GetPosition(QueueItems);
            // Only commit to a drag once the cursor has moved a meaningful
            // distance — prevents accidental drags on plain clicks.
            if (Math.Abs(pos.Y - _dragStart.Y) < 8 && Math.Abs(pos.X - _dragStart.X) < 8) return;

            _dragArmed = false;
            if (_draggedTimer is null) return;

            // Visual feedback: dim the source row while dragging. We restore
            // it in the finally block once DoDragDrop returns.
            double prevOpacity = row.Opacity;
            row.Opacity = 0.4;
            try
            {
                var data = new DataObject(DragDataFormat, _draggedTimer);
                DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
            }
            finally
            {
                row.Opacity = prevOpacity;
                HideDropIndicator();
                _draggedTimer = null;
                _draggedRow = null;
            }
        };

        row.DragOver += (s, e) =>
        {
            if (!e.Data.GetDataPresent(DragDataFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Move;

            var pt = e.GetPosition(row);
            bool dropAbove = pt.Y < row.ActualHeight / 2;
            ShowDropIndicatorAt(row, dropAbove);
            e.Handled = true;
        };

        row.Drop += (s, e) =>
        {
            HideDropIndicator();
            if (_session is null) return;
            if (e.Data.GetData(DragDataFormat) is not TrainingTimer source) return;
            if (ReferenceEquals(source, timer)) return;

            int targetIdx = _session.Timers.IndexOf(timer);
            if (targetIdx < 0) return;

            var pt = e.GetPosition(row);
            bool dropAbove = pt.Y < row.ActualHeight / 2;

            int sourceIdx = _session.Timers.IndexOf(source);
            if (sourceIdx < 0) return;

            int newIndex = dropAbove ? targetIdx : targetIdx + 1;
            // Account for source being removed before insertion when moving down.
            if (sourceIdx < newIndex) newIndex--;
            int maxIndex = _session.Timers.Count - 1;
            if (newIndex < 0) newIndex = 0;
            if (newIndex > maxIndex) newIndex = maxIndex;
            if (newIndex == sourceIdx) return;

            // Suppress the per-row slide-in animation for this rebuild — the
            // user just dragged, they don't need every row to fly in again.
            _suppressNextRowAnim = true;
            try
            {
                _session.MoveTimer(source, newIndex);
            }
            finally
            {
                _suppressNextRowAnim = false;
            }
            e.Handled = true;
        };
    }

    private static bool IsInsideButton(DependencyObject d)
    {
        while (d != null)
        {
            if (d is Button) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>
    /// Positions the drop indicator at the top or bottom edge of the given
    /// row by translating its margin within the QueueArea. The indicator
    /// floats over the rows so it doesn't cause any reflow.
    /// </summary>
    private void ShowDropIndicatorAt(Border row, bool above)
    {
        if (DropIndicator is null || QueueArea is null) return;

        // Convert the row's edge into QueueArea coordinates.
        var origin = row.TranslatePoint(new System.Windows.Point(0, above ? 0 : row.ActualHeight), QueueArea);
        // Centre the 3px line on the boundary so it reads as an insertion gap.
        double y = origin.Y - 1.5;
        if (y < 0) y = 0;

        DropIndicator.Margin = new Thickness(6, y, 6, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator()
    {
        if (DropIndicator is null) return;
        DropIndicator.Visibility = Visibility.Collapsed;
    }

    private Button MakeIconButton(string text, string tooltip, Action action)
    {
        var b = new Button
        {
            Content = text,
            // Pick up the templated style so Background/Foreground actually render.
            Style = (Style)FindResource("ActionButton"),
            Background = ColorFromHex("#1F2937"),
            Foreground = ColorFromHex("#E5E7EB"),
            BorderBrush = ColorFromHex("#374151"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 2, 0),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Cursor = Cursors.Hand,
            ToolTip = tooltip
        };
        b.Click += (_, _) => action();
        return b;
    }

    private static SolidColorBrush ColorFromHex(string hex)
    {
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is Color c)
                return new SolidColorBrush(c);
        }
        catch { }
        return new SolidColorBrush(Colors.Gray);
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    // ─────────── Click handlers ───────────

    private void OnStartPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_session.IsRunning) _session.Pause();
        else _session.Start();
    }

    private void OnResetActiveClicked(object sender, RoutedEventArgs e)
    {
        _session?.ResetActive();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        _session?.Skip();
    }

    private void OnAdd60sClicked(object sender, RoutedEventArgs e)
    {
        _session?.AddTimeToActive(TimeSpan.FromMinutes(1));
    }

    private void OnAdd10sClicked(object sender, RoutedEventArgs e)
    {
        _session?.AddTimeToActive(TimeSpan.FromSeconds(10));
    }

    private void OnAddTimerClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;

        string title = string.IsNullOrWhiteSpace(NewTitleBox.Text) ? "TIMER" : NewTitleBox.Text.Trim();
        if (!TryReadTimerDuration(NewMinutesBox.Text, NewSecondsBox.Text, out int totalSeconds, out string message))
        {
            TrainingEntryError.Text = message; TrainingEntryError.Visibility = Visibility.Visible;
            TextBox field = TryReadTimerPart(NewMinutesBox.Text, out _)
                && !TryReadTimerPart(NewSecondsBox.Text, out _) ? NewSecondsBox : NewMinutesBox;
            var session = _session;
            var owner = Window.GetWindow(this);
            bool CanFocusEntry() => ReferenceEquals(_session, session) && IsLoaded && IsVisible && IsEnabled
                && field.IsVisible && field.IsEnabled && owner is { IsVisible: true, IsEnabled: true }
                && ReferenceEquals(Window.GetWindow(this), owner);
            if (CanFocusEntry())
            {
                if (field.Focus() && CanFocusEntry()) Keyboard.Focus(field);
                // Native caret focus can scroll its input. Reveal the wrapped
                // explanation last so pointer and Enter use the same viewport.
                if (CanFocusEntry()) OperatorViewport.RevealValidationError(TrainingEntryError);
            }
            return;
        }
        TrainingEntryError.Text = ""; TrainingEntryError.Visibility = Visibility.Collapsed;
        TimeSpan dur = TimeSpan.FromSeconds(totalSeconds);

        _session.AddTimer(new TrainingTimer(title, dur));
        RecentTrainingTimerStorage.Record(title, totalSeconds);
        RefreshRecentTimers();
    }

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (sender is not Button b) return;
        if (b.Tag is RecentTrainingTimerStorage.Entry)
        {
            ExecuteRecentTimerCommand(b);
            return;
        }
        if (b.Tag is not string tag) return;
        string[] parts = tag.Split('|');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)) return;
        _session.AddTimer(new TrainingTimer(parts[0], TimeSpan.FromSeconds(seconds)));
        RecentTrainingTimerStorage.Record(parts[0], seconds);
        RefreshRecentTimers();
    }

    private void ExecuteRecentTimerCommand(Button source)
    {
        if (!_recentCommands.TryGetValue(source, out var command)) return;
        var session = command.Session;
        int generation = command.Generation;
        var owner = Window.GetWindow(this);
        bool CurrentScope() => generation == _queueCommandGeneration && ReferenceEquals(_session, session)
            && IsLoaded && IsVisible && IsEnabled && owner is { IsVisible: true, IsEnabled: true }
            && ReferenceEquals(Window.GetWindow(this), owner);
        if (!CurrentScope() || !source.IsLoaded || !source.IsVisible || !source.IsEnabled
            || !RecentTimersPanel.Children.Contains(source) || !ReferenceEquals(Window.GetWindow(source), owner)) return;
        bool restoreFocus = source.IsKeyboardFocusWithin;

        // Accept this exact value once, before synchronous model observers can
        // reenter its source. A rebind/hide cannot retarget the accepted append
        // or cancel its existing global recent-timer recording policy.
        _recentCommands.Remove(source);
        try
        {
            session.AddTimer(new TrainingTimer(command.Title, TimeSpan.FromSeconds(command.Seconds)));
            RecentTrainingTimerStorage.Record(command.Title, command.Seconds);
        }
        finally { RefreshRecentTimers(); }
        if (!restoreFocus || !CurrentScope()) return;

        bool FocusAvailable(Button? candidate = null)
        {
            var focused = Keyboard.FocusedElement;
            return focused is null || ReferenceEquals(focused, source) || ReferenceEquals(focused, candidate);
        }
        if (!FocusAvailable()) return;
        RecentTimersPanel.UpdateLayout();
        if (!CurrentScope() || !FocusAvailable()) return;
        var target = RecentTimersPanel.Children.OfType<Button>().FirstOrDefault(candidate =>
            _recentCommands.TryGetValue(candidate, out var current) && ReferenceEquals(current.Session, session)
            && current.Generation == generation && current.Seconds == command.Seconds
            && string.Equals(current.Title, command.Title, StringComparison.OrdinalIgnoreCase));
        if (target is null || !target.IsVisible || !target.IsEnabled || !FocusAvailable(target)) return;
        bool CurrentTarget() => CurrentScope() && _recentCommands.ContainsKey(target)
            && RecentTimersPanel.Children.Contains(target);
        if (target.Focus() && CurrentTarget() && FocusAvailable(target)) Keyboard.Focus(target);
        if (CurrentTarget() && target.IsKeyboardFocusWithin)
            OperatorViewport.RevealValidationError(target);
    }

    private void RefreshRecentTimers()
    {
        if (RecentTimersPanel is null) return;
        int refresh = ++_recentRefreshGeneration;
        var session = _session;
        int generation = _queueCommandGeneration;
        bool CurrentRefresh() => refresh == _recentRefreshGeneration && generation == _queueCommandGeneration
            && ReferenceEquals(_session, session);
        // A generic refresh (including manual Add) never restores chip focus.
        // Retired controls also stop accepting retained routed callbacks.
        _recentCommands.Clear();
        var retired = RecentTimersPanel.Children.OfType<Button>().ToArray();
        foreach (var button in retired) button.Click -= OnPresetClicked;
        RecentTimersPanel.Children.Clear();
        foreach (var button in retired) button.IsEnabled = false;
        if (!CurrentRefresh()) return;

        var entries = RecentTrainingTimerStorage.Load();
        if (entries.Count == 0)
        {
            RecentTimersEmptyText.Visibility = Visibility.Visible;
            return;
        }

        RecentTimersEmptyText.Visibility = Visibility.Collapsed;

        var chipStyle = (Style)FindResource("PresetChip");
        foreach (var entry in entries)
        {
            int mins = entry.Seconds / 60;
            int secs = entry.Seconds % 60;
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
            chipContent.Children.Add(new TextBlock
            {
                Text = entry.Title,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            chipContent.Children.Add(new TextBlock
            {
                Text = $" · {mins}:{secs:D2}",
                FontWeight = FontWeights.Black,
                VerticalAlignment = VerticalAlignment.Center
            });
            var btn = new Button
            {
                Style = chipStyle,
                Content = chipContent,
                Tag = entry,
                MaxWidth = 250,
                ToolTip = $"{entry.Title} · {mins}:{secs:D2}\nClick to append this timer to the queue"
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, $"Add {entry.Title}, {mins}:{secs:D2} timer to queue");
            btn.IsEnabled = session is not null;
            if (session is not null)
                _recentCommands[btn] = (session, generation, entry.Title, entry.Seconds);
            btn.Click += OnPresetClicked;
            RecentTimersPanel.Children.Add(btn);
            if (!CurrentRefresh()) return;
        }
    }

    private void OnBuildIntervalClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if(_editingTimerId is not null)
        {
            TrainingIntervalError.Text="Save or cancel the open timer correction before building intervals. Your edits are still intact.";
            TrainingIntervalError.Visibility=Visibility.Visible;OperatorViewport.RevealValidationError(TrainingIntervalError);return;
        }
        bool Read(TextBox field,int minimum,int maximum,string message,out int value)
        {
            if(int.TryParse(field.Text?.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out value) && value>=minimum && value<=maximum)return true;
            TrainingIntervalError.Text=message;TrainingIntervalError.Visibility=Visibility.Visible;
            OperatorViewport.RevealValidationError(TrainingIntervalError);field.Focus();Keyboard.Focus(field);return false;
        }
        if(!Read(IntervalRoundsBox,1,100,"Enter 1 to 100 whole rounds.",out int rounds)
            || !Read(IntervalWorkBox,1,int.MaxValue,"Enter at least 1 whole second of work.",out int work)
            || !Read(IntervalRestBox,0,int.MaxValue,"Enter whole rest seconds, or 0 for no rest.",out int rest))return;

        var additions=new List<TrainingTimer>();
        for (int i = 1; i <= rounds; i++)
        {
            additions.Add(new TrainingTimer($"WORK {i}/{rounds}", TimeSpan.FromSeconds(work)));
            if (rest > 0 && i < rounds)
                additions.Add(new TrainingTimer($"REST", TimeSpan.FromSeconds(rest)));
        }
        bool previousSuppression=_suppressNextRowAnim;
        // A bulk append has one structural handoff. Avoid hundreds of parallel
        // row clocks; remember their IDs so the next rebuild cannot replay them.
        _suppressNextRowAnim=previousSuppression || additions.Count>4;
        try
        {
            if(!_session.TryAppendTimers(additions,out string error))
            {TrainingIntervalError.Text=error;TrainingIntervalError.Visibility=Visibility.Visible;OperatorViewport.RevealValidationError(TrainingIntervalError);return;}
            TrainingIntervalError.Text="";TrainingIntervalError.Visibility=Visibility.Collapsed;
        }
        finally{_suppressNextRowAnim=previousSuppression;}
    }

    private ClearReviewReceipt? _clearReview;
    public bool HasActiveClearDecision => _clearReview is not null;

    private sealed class ClearReviewReceipt
    {
        internal readonly TrainingSession Session;
        internal readonly int Generation;
        internal readonly Window Owner;
        internal readonly FrameworkElement ReturnFocus;
        internal readonly TrainingTimer[] Timers;
        internal readonly string[] Titles;
        internal readonly TimeSpan[] Durations;
        internal readonly int ActiveIndex;
        internal readonly UIElement[] ObservedAncestors;
        internal Action Invalidated = null!;
        internal Button Cancel = null!, Confirm = null!;
        internal ClearReviewReceipt(TrainingSession session, int generation, Window owner, FrameworkElement returnFocus, UIElement[] visibilityAncestors)
        {
            Session = session; Generation = generation; Owner = owner; ReturnFocus = returnFocus;
            Timers = session.Timers.ToArray(); Titles = Timers.Select(timer => timer.Title).ToArray();
            Durations = Timers.Select(timer => timer.Duration).ToArray(); ActiveIndex = session.ActiveIndex;
            ObservedAncestors = visibilityAncestors;
        }
    }

    private IEnumerable<UIElement> ClearReviewVisualChain()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement element) yield return element;
    }
    private bool ClearReviewVisualChainVisible() => ClearReviewVisualChain().All(element => element.IsVisible);

    private bool CurrentClearReview(ClearReviewReceipt review)
    {
        if (!ReferenceEquals(_clearReview, review) || !ReferenceEquals(_session, review.Session)
            || review.Generation != _queueCommandGeneration || !IsLoaded || !IsVisible || !ClearReviewVisualChainVisible()
            || !review.Owner.IsLoaded || !review.Owner.IsVisible || !ReferenceEquals(Window.GetWindow(this), review.Owner)
            || review.Session.Timers.Count != review.Timers.Length || review.Session.ActiveIndex != review.ActiveIndex) return false;
        for (int index = 0; index < review.Timers.Length; index++)
        {
            var timer = review.Session.Timers[index];
            if (!ReferenceEquals(timer, review.Timers[index]) || timer.Title != review.Titles[index]
                || timer.Duration != review.Durations[index]) return false;
        }
        // Remaining/running time is intentionally absent: merely reviewing a
        // queue neither pauses it nor becomes stale on every ordinary clock tick.
        return true;
    }

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        if (_clearReview is not null || _exitReview is not null) return;
        if (_session is not { } session || session.Timers.Count == 0 || !IsLoaded || !IsVisible || !ClearReviewVisualChainVisible()
            || !ClearAllButton.IsEnabled || Window.GetWindow(this) is not { IsLoaded: true, IsVisible: true } owner) return;
        if (owner is Roche_Scoreboard.MainWindow applicationWindow && !applicationWindow.CanOpenTrainingClearReview) return;
        int requestGeneration = _queueCommandGeneration;
        TrainingCloudLauncher.DismissForDecision();
        // Popup closure can synchronously rebind, navigate or open another scope.
        if (_clearReview is not null || _exitReview is not null || requestGeneration != _queueCommandGeneration
            || !ReferenceEquals(_session, session) || !IsLoaded || !IsVisible || !ClearReviewVisualChainVisible()
            || !owner.IsLoaded || !owner.IsVisible || !ReferenceEquals(Window.GetWindow(this), owner)
            || !ClearAllButton.IsEnabled || session.Timers.Count == 0) return;
        if (owner is Roche_Scoreboard.MainWindow currentWindow && !currentWindow.CanOpenTrainingClearReview) return;
        FrameworkElement returnFocus = Keyboard.FocusedElement is FrameworkElement { IsLoaded: true, IsVisible: true, IsEnabled: true, Focusable: true } prior
            && this.IsAncestorOf(prior) ? prior : ClearAllButton;
        var ancestors = ClearReviewVisualChain().Where(element => !ReferenceEquals(element, this) && !ReferenceEquals(element, owner)).ToArray();
        var review = new ClearReviewReceipt(session, _queueCommandGeneration, owner, returnFocus, ancestors);
        review.Invalidated = () => { if (ReferenceEquals(_clearReview, review)) RetireClearReview(restoreFocus: true); };
        review.Cancel = new Button { Name = "TrainingClearReviewCancel", Tag = "training-clear-cancel", Content = "Cancel",
            IsDefault = true, MinHeight = 46, MinWidth = 110, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 10, 14, 10) };
        review.Cancel.SetResourceReference(FrameworkElement.StyleProperty, "ControlCentre.Button");
        review.Confirm = new Button { Name = "TrainingClearReviewConfirm", Tag = "training-clear-confirm", Content = "Clear all timers",
            IsDefault = false, MinHeight = 46, MinWidth = 150, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14, 10, 14, 10),
            Background = ColorFromHex("#3F1212"), Foreground = ColorFromHex("#FECACA"), BorderBrush = ColorFromHex("#B91C1C") };
        review.Confirm.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        System.Windows.Automation.AutomationProperties.SetName(review.Cancel, "Cancel clearing timers");
        System.Windows.Automation.AutomationProperties.SetName(review.Confirm, "Clear every timer in the reviewed session");
        // Each request owns new command objects/delegates. A retained old Click
        // cannot accept or close a newer review on this same panel.
        review.Cancel.Click += (_, _) => { if (ReferenceEquals(_clearReview, review)) RetireClearReview(restoreFocus: true); };
        review.Confirm.Click += (_, _) => ConfirmClearReview(review);
        _clearReview = review;
        TrainingClearReviewActions.Children.Clear();
        TrainingClearReviewActions.Children.Add(review.Cancel);
        TrainingClearReviewActions.Children.Add(review.Confirm);
        session.StructuralChanged += review.Invalidated;
        foreach (var ancestor in review.ObservedAncestors) ancestor.IsVisibleChanged += ClearReviewAncestorVisibilityChanged;
        owner.IsVisibleChanged += ClearReviewOwnerVisibilityChanged;
        owner.Closed += ClearReviewOwnerClosed;
        OperatorAppearance.Changed += ClearReviewAppearanceChanged;
        string active = session.Active is { } timer ? $" Current drill: {timer.Title}." : "";
        TrainingClearReviewContext.Text = $"Remove all {session.Timers.Count} timers and their progress?{active}"
            + (_correction is null ? "" : " The unfinished timer edit will also be discarded.");
        TrainingClearReviewOverlay.Visibility = Visibility.Visible;
        TrainingClearReviewOverlay.UpdateLayout();
        TrainingClearReviewScroll.ScrollToVerticalOffset(0); // Every new receipt starts with its current queue context.
        TrainingClearReviewShell.Visibility = Visibility.Visible;
        TrainingClearReviewContent.BeginAnimation(OpacityProperty, null);
        TrainingClearReviewContent.Opacity = 1;
        if (OperatorMotion.Enabled)
            TrainingClearReviewContent.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.75, 1, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut });
        review.Cancel.Focus(); Keyboard.Focus(review.Cancel);
        review.Cancel.BringIntoView();
    }

    private void ClearReviewAncestorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Observe only the currently owned visual chain. A stale hide callback
        // must not retire a new, visible request after its host was restored.
        if (_clearReview is { } review && sender is UIElement ancestor
            && review.ObservedAncestors.Any(element => ReferenceEquals(element, ancestor)) && !ancestor.IsVisible)
            RetireClearReview(restoreFocus: false);
    }
    private void ClearReviewOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_clearReview is { } review && ReferenceEquals(sender, review.Owner) && !review.Owner.IsVisible)
            RetireClearReview(restoreFocus: false);
    }
    private void ClearReviewOwnerClosed(object? sender, EventArgs e)
    {
        if (_clearReview is { } review && ReferenceEquals(sender, review.Owner)) RetireClearReview(restoreFocus: false);
    }
    private void ClearReviewAppearanceChanged()
    {
        if (!OperatorMotion.Enabled)
        {
            TrainingClearReviewContent.BeginAnimation(OpacityProperty, null);
            TrainingClearReviewContent.Opacity = 1;
        }
    }

    private void RetireClearReview(bool restoreFocus)
    {
        if (_clearReview is not { } review) return;
        bool ownedFocus = TrainingClearReviewOverlay.IsKeyboardFocusWithin;
        _clearReview = null; // Retire before any visual/focus observer can reenter.
        review.Session.StructuralChanged -= review.Invalidated;
        foreach (var ancestor in review.ObservedAncestors) ancestor.IsVisibleChanged -= ClearReviewAncestorVisibilityChanged;
        review.Owner.IsVisibleChanged -= ClearReviewOwnerVisibilityChanged;
        review.Owner.Closed -= ClearReviewOwnerClosed;
        OperatorAppearance.Changed -= ClearReviewAppearanceChanged;
        review.Cancel.IsDefault = false;
        TrainingClearReviewShell.Visibility = Visibility.Collapsed; // Existing motion owner resets its own shell clocks.
        TrainingClearReviewContent.BeginAnimation(OpacityProperty, null);
        TrainingClearReviewContent.Opacity = 1;
        TrainingClearReviewOverlay.Visibility = Visibility.Collapsed;
        TrainingClearReviewActions.Children.Clear();
        if (restoreFocus && ownedFocus) RestoreAfterClearReview(review);
    }

    private void RestoreAfterClearReview(ClearReviewReceipt review)
    {
        if (_clearReview is not null || _exitReview is not null || !ReferenceEquals(_session, review.Session) || review.Generation != _queueCommandGeneration
            || !IsLoaded || !IsVisible || !review.Owner.IsActive || !ReferenceEquals(Window.GetWindow(this), review.Owner)) return;
        // Do not steal focus deliberately claimed by a newer surface/observer.
        if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } focused
            && !ReferenceEquals(focused, review.Cancel) && !ReferenceEquals(focused, review.Confirm)
            && !TrainingClearReviewOverlay.IsAncestorOf(focused)) return;
        FrameworkElement target = review.ReturnFocus;
        if (!target.IsLoaded || !target.IsVisible || !target.IsEnabled || !target.Focusable
            || !ReferenceEquals(Window.GetWindow(target), review.Owner))
            target = NewTitleBox.IsLoaded && NewTitleBox.IsVisible && NewTitleBox.IsEnabled ? NewTitleBox : this;
        if (target.Focus()) { Keyboard.Focus(target); OperatorViewport.RevealOperatorTarget(target); }
    }

    private void ConfirmClearReview(ClearReviewReceipt review)
    {
        if (!ReferenceEquals(_clearReview, review)) return;
        if (!CurrentClearReview(review) || review.Session.Timers.Count == 0)
        { RetireClearReview(restoreFocus: true); return; }
        RetireClearReview(restoreFocus: false);
        // The existing model operation is accepted once, before decoration.
        // Synchronous observers may bind another session or focus another scope.
        review.Session.Clear();
        RestoreAfterClearReview(review);
    }

    private ExitReviewReceipt? _exitReview;
    public bool HasActiveExitDecision => _exitReview is not null;

    private sealed class ExitReviewReceipt
    {
        internal readonly TrainingSession Session;
        internal readonly int Generation;
        internal readonly Window Owner;
        internal readonly FrameworkElement ReturnFocus;
        internal readonly TrainingTimer[] Timers;
        internal readonly string[] Titles;
        internal readonly TimeSpan[] Durations;
        internal readonly int ActiveIndex;
        internal readonly UIElement[] ObservedAncestors;
        internal Action Invalidated = null!, StateChanged = null!;
        internal Button Cancel = null!, Confirm = null!;
        internal ExitReviewReceipt(TrainingSession session, int generation, Window owner, FrameworkElement returnFocus, UIElement[] visibilityAncestors)
        {
            Session = session; Generation = generation; Owner = owner; ReturnFocus = returnFocus;
            Timers = session.Timers.ToArray(); Titles = Timers.Select(timer => timer.Title).ToArray();
            Durations = Timers.Select(timer => timer.Duration).ToArray(); ActiveIndex = session.ActiveIndex;
            ObservedAncestors = visibilityAncestors;
        }
    }

    private IEnumerable<UIElement> ExitReviewVisualChain()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement element) yield return element;
    }
    private bool ExitReviewVisualChainVisible() => ExitReviewVisualChain().All(element => element.IsVisible);

    private bool CurrentExitReview(ExitReviewReceipt review)
    {
        if (!ReferenceEquals(_exitReview, review) || !ReferenceEquals(_session, review.Session)
            || review.Generation != _queueCommandGeneration || !IsLoaded || !IsVisible || !ExitReviewVisualChainVisible()
            || !review.Owner.IsLoaded || !review.Owner.IsVisible || !ReferenceEquals(Window.GetWindow(this), review.Owner)
            || review.Session.Timers.Count != review.Timers.Length || review.Session.ActiveIndex != review.ActiveIndex) return false;
        for (int index = 0; index < review.Timers.Length; index++)
        {
            var timer = review.Session.Timers[index];
            if (!ReferenceEquals(timer, review.Timers[index]) || timer.Title != review.Titles[index]
                || timer.Duration != review.Durations[index]) return false;
        }
        // Remaining/running time is intentionally absent: merely reviewing a
        // queue neither pauses it nor becomes stale on every ordinary clock tick.
        return true;
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        if (_exitReview is not null || _clearReview is not null) return;
        if (_session is not { } session || !IsLoaded || !IsVisible || !ExitReviewVisualChainVisible()
            || !ExitButton.IsEnabled || Window.GetWindow(this) is not { IsLoaded: true, IsVisible: true } owner) return;
        if (owner is Roche_Scoreboard.MainWindow applicationWindow && !applicationWindow.CanOpenTrainingExitReview) return;
        int requestGeneration = _queueCommandGeneration;
        TrainingCloudLauncher.DismissForDecision();
        // Popup closure can synchronously rebind, navigate or open another scope.
        if (_clearReview is not null || _exitReview is not null || requestGeneration != _queueCommandGeneration
            || !ReferenceEquals(_session, session) || !IsLoaded || !IsVisible || !ExitReviewVisualChainVisible()
            || !owner.IsLoaded || !owner.IsVisible || !ReferenceEquals(Window.GetWindow(this), owner)
            || !ExitButton.IsEnabled) return;
        if (owner is Roche_Scoreboard.MainWindow currentWindow && !currentWindow.CanOpenTrainingExitReview) return;
        // No queue or inline correction is being discarded. Builder fields
        // already survive Bind/return, so empty Exit needs no extra question.
        if (session.Timers.Count == 0 && _correction is null) { CommitTrainingExit(session); return; }
        FrameworkElement returnFocus = Keyboard.FocusedElement is FrameworkElement { IsLoaded: true, IsVisible: true, IsEnabled: true, Focusable: true } prior
            && this.IsAncestorOf(prior) ? prior : ExitButton;
        var ancestors = ExitReviewVisualChain().Where(element => !ReferenceEquals(element, this) && !ReferenceEquals(element, owner)).ToArray();
        var review = new ExitReviewReceipt(session, _queueCommandGeneration, owner, returnFocus, ancestors);
        review.Invalidated = () => { if (ReferenceEquals(_exitReview, review)) RetireExitReview(restoreFocus: true); };
        review.StateChanged = () =>
        {
            if (ReferenceEquals(_exitReview, review))
                TrainingExitReviewStatus.Text = session.IsExhausted
                    ? "Session complete · the queue is kept open."
                    : session.Active is null ? "No active timer · the session is kept open."
                    : session.Active.HasFinished ? "Interval complete · the queue is kept open."
                    : session.IsRunning ? "Timer running · it continues while you decide."
                    : "Timer paused · its progress is kept.";
        };
        review.Cancel = new Button { Name = "TrainingExitReviewCancel", Tag = "training-exit-cancel", Content = "Cancel",
            IsDefault = true, MinHeight = 46, MinWidth = 110, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 10, 14, 10) };
        review.Cancel.SetResourceReference(FrameworkElement.StyleProperty, "ControlCentre.Button");
        review.Confirm = new Button { Name = "TrainingExitReviewConfirm", Tag = "training-exit-confirm", Content = "Exit Training",
            IsDefault = false, MinHeight = 46, MinWidth = 150, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14, 10, 14, 10),
            Background = ColorFromHex("#3F1212"), Foreground = ColorFromHex("#FECACA"), BorderBrush = ColorFromHex("#B91C1C") };
        review.Confirm.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        System.Windows.Automation.AutomationProperties.SetName(review.Cancel, "Keep the current Training session");
        System.Windows.Automation.AutomationProperties.SetName(review.Confirm, "Exit the reviewed Training session");
        // Each request owns new command objects/delegates. A retained old Click
        // cannot accept or close a newer review on this same panel.
        review.Cancel.Click += (_, _) => { if (ReferenceEquals(_exitReview, review)) RetireExitReview(restoreFocus: true); };
        review.Confirm.Click += (_, _) => ConfirmExitReview(review);
        _exitReview = review;
        TrainingExitReviewActions.Children.Clear();
        TrainingExitReviewActions.Children.Add(review.Cancel);
        TrainingExitReviewActions.Children.Add(review.Confirm);
        session.StructuralChanged += review.Invalidated;
        session.Changed += review.StateChanged;
        foreach (var ancestor in review.ObservedAncestors) ancestor.IsVisibleChanged += ExitReviewAncestorVisibilityChanged;
        owner.IsVisibleChanged += ExitReviewOwnerVisibilityChanged;
        owner.Closed += ExitReviewOwnerClosed;
        OperatorAppearance.Changed += ExitReviewAppearanceChanged;
        string active = session.Active is { } timer ? $" Current drill: {timer.Title}." : "";
        TrainingExitReviewContext.Text = $"Leave this session with {session.Timers.Count} timers and return Home?{active}"
            + (_correction is null ? "" : " The unfinished timer correction will not be kept.");
        review.StateChanged();
        TrainingExitReviewOverlay.Visibility = Visibility.Visible;
        TrainingExitReviewOverlay.UpdateLayout();
        TrainingExitReviewScroll.ScrollToVerticalOffset(0); // Every new receipt starts with its current queue context.
        TrainingExitReviewShell.Visibility = Visibility.Visible;
        TrainingExitReviewContent.BeginAnimation(OpacityProperty, null);
        TrainingExitReviewContent.Opacity = 1;
        if (OperatorMotion.Enabled)
            TrainingExitReviewContent.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.75, 1, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut });
        review.Cancel.Focus(); Keyboard.Focus(review.Cancel);
        review.Cancel.BringIntoView();
    }

    private void ExitReviewAncestorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Observe only the currently owned visual chain. A stale hide callback
        // must not retire a new, visible request after its host was restored.
        if (_exitReview is { } review && sender is UIElement ancestor
            && review.ObservedAncestors.Any(element => ReferenceEquals(element, ancestor)) && !ancestor.IsVisible)
            RetireExitReview(restoreFocus: false);
    }
    private void ExitReviewOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_exitReview is { } review && ReferenceEquals(sender, review.Owner) && !review.Owner.IsVisible)
            RetireExitReview(restoreFocus: false);
    }
    private void ExitReviewOwnerClosed(object? sender, EventArgs e)
    {
        if (_exitReview is { } review && ReferenceEquals(sender, review.Owner)) RetireExitReview(restoreFocus: false);
    }
    private void ExitReviewAppearanceChanged()
    {
        if (!OperatorMotion.Enabled)
        {
            TrainingExitReviewContent.BeginAnimation(OpacityProperty, null);
            TrainingExitReviewContent.Opacity = 1;
        }
    }

    private void RetireExitReview(bool restoreFocus)
    {
        if (_exitReview is not { } review) return;
        bool ownedFocus = TrainingExitReviewOverlay.IsKeyboardFocusWithin;
        _exitReview = null; // Retire before any visual/focus observer can reenter.
        review.Session.StructuralChanged -= review.Invalidated;
        review.Session.Changed -= review.StateChanged;
        foreach (var ancestor in review.ObservedAncestors) ancestor.IsVisibleChanged -= ExitReviewAncestorVisibilityChanged;
        review.Owner.IsVisibleChanged -= ExitReviewOwnerVisibilityChanged;
        review.Owner.Closed -= ExitReviewOwnerClosed;
        OperatorAppearance.Changed -= ExitReviewAppearanceChanged;
        review.Cancel.IsDefault = false;
        TrainingExitReviewShell.Visibility = Visibility.Collapsed; // Existing motion owner resets its own shell clocks.
        TrainingExitReviewContent.BeginAnimation(OpacityProperty, null);
        TrainingExitReviewContent.Opacity = 1;
        TrainingExitReviewOverlay.Visibility = Visibility.Collapsed;
        TrainingExitReviewActions.Children.Clear();
        if (restoreFocus && ownedFocus) RestoreAfterExitReview(review);
    }

    private void RestoreAfterExitReview(ExitReviewReceipt review)
    {
        if (_exitReview is not null || _clearReview is not null || !ReferenceEquals(_session, review.Session) || review.Generation != _queueCommandGeneration
            || !IsLoaded || !IsVisible || !review.Owner.IsActive || !ReferenceEquals(Window.GetWindow(this), review.Owner)) return;
        // Do not steal focus deliberately claimed by a newer surface/observer.
        if (Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } focused
            && !ReferenceEquals(focused, review.Cancel) && !ReferenceEquals(focused, review.Confirm)
            && !TrainingExitReviewOverlay.IsAncestorOf(focused)) return;
        FrameworkElement target = review.ReturnFocus;
        if (!target.IsLoaded || !target.IsVisible || !target.IsEnabled || !target.Focusable
            || !ReferenceEquals(Window.GetWindow(target), review.Owner))
            target = NewTitleBox.IsLoaded && NewTitleBox.IsVisible && NewTitleBox.IsEnabled ? NewTitleBox : this;
        if (target.Focus()) { Keyboard.Focus(target); OperatorViewport.RevealOperatorTarget(target); }
    }

    private void ConfirmExitReview(ExitReviewReceipt review)
    {
        if (!ReferenceEquals(_exitReview, review)) return;
        if (!CurrentExitReview(review))
        { RetireExitReview(restoreFocus: true); return; }
        RetireExitReview(restoreFocus: false);
        // Home or a reentrant replacement owns focus after an accepted Exit.
        CommitTrainingExit(review.Session);
    }

    private void CommitTrainingExit(TrainingSession session)
    {
        if (!ReferenceEquals(_session, session)) return;
        var commit = ConfirmedExitCommit;
        // A detached/root-retired panel cannot use a notification as a backdoor.
        if (Window.GetWindow(this) is Roche_Scoreboard.MainWindow && commit is null) return;
        if (commit is not null)
        {
            if (!commit(session)) return;
        }
        else session.Pause(); // Standalone host: accepted, captured model only.
        ExitRequested?.Invoke();
    }

    /// <summary>Already-confirmed programmatic/phone Exit; never opens a second local review.</summary>
    public void RequestExit()
    {
        if (_session is not { } session) return;
        RetireExitReview(restoreFocus: false);
        RetireClearReview(restoreFocus: false);
        CommitTrainingExit(session);
    }

    private static bool TryReadTimerDuration(string? minutes, string? seconds, out int totalSeconds, out string error)
    {
        totalSeconds = 0; error = "Use whole, non-negative minutes and seconds.";
        if (!TryReadTimerPart(minutes, out int mins) || !TryReadTimerPart(seconds, out int secs)) return false;
        long total = (long)mins * 60 + secs;
        if (total == 0) { error = "Enter a duration of at least 1 second."; return false; }
        if (total > int.MaxValue) { error = "This duration is too long. Use fewer minutes or seconds."; return false; }
        totalSeconds = (int)total; error = ""; return true;
    }

    private static bool TryReadTimerPart(string? text, out int value)
        => int.TryParse(string.IsNullOrWhiteSpace(text) ? "0" : text.Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
