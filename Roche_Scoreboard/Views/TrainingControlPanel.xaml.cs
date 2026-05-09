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

    private TrainingSession? _session;
    private readonly DispatcherTimer _ui;

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    public TrainingControlPanel()
    {
        InitializeComponent();
        _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _ui.Tick += (_, _) => RefreshLiveText();
        Loaded += (_, _) =>
        {
            _ui.Start();
            // Pull keyboard focus when the panel loads so global shortcuts work
            // straight away (Space/S/R/Esc).
            Focus();
            Keyboard.Focus(this);
        };
        Unloaded += (_, _) => _ui.Stop();
        // Operator shortcuts at the panel level — only when focus isn't in a
        // TextBox so typing into the title or number fields still works.
        PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

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

    public void Bind(TrainingSession session)
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session.StructuralChanged -= OnStructuralChanged;
        }
        _session = session;
        _session.Changed += OnSessionChanged;
        _session.StructuralChanged += OnStructuralChanged;
        OnStructuralChanged();
        OnSessionChanged();
    }

    // ─────────── Live UI ───────────

    /// <summary>
    /// Light-weight refresh fired on every model tick. Intentionally avoids
    /// rebuilding the queue list (which animates rows in) so the UI doesn't
    /// glitch 30× per second while the clock runs.
    /// </summary>
    private void OnSessionChanged()
    {
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
        StartPauseButton.Content = running ? "❚❚  PAUSE" : "▶  START";
        // High-contrast colours: when running we show a strong red (pause to
        // stop), when paused we show a strong green (start to go).
        StartPauseButton.Background = running ? ColorFromHex("#7F1D1D") : ColorFromHex("#166534");
        StartPauseButton.Foreground = running ? ColorFromHex("#FEE2E2") : ColorFromHex("#DCFCE7");
        StartPauseButton.BorderBrush = running ? ColorFromHex("#EF4444") : ColorFromHex("#22C55E");

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
        QueueItems.Items.Clear();
        for (int i = 0; i < _session.Timers.Count; i++)
        {
            var t = _session.Timers[i];
            QueueItems.Items.Add(BuildQueueRow(t, i, _session.ActiveIndex));
        }
    }

    private FrameworkElement BuildQueueRow(TrainingTimer t, int index, int activeIndex)
    {
        bool isActive = index == activeIndex && !t.HasFinished;
        bool isFinished = t.HasFinished || index < activeIndex;
        string accent = isActive ? "#58A6FF" : isFinished ? "#374151" : "#6B7280";

        var border = new Border
        {
            Background = ColorFromHex("#10161D"),
            BorderBrush = ColorFromHex(isActive ? "#58A6FF" : "#1F2937"),
            BorderThickness = new Thickness(isActive ? 2 : 1),
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

        actions.Children.Add(MakeIconButton("▲", "Move up", () =>
        {
            if (_session is null) return;
            int idx = _session.Timers.IndexOf(t);
            if (idx > 0) _session.MoveTimer(t, idx - 1);
        }));
        actions.Children.Add(MakeIconButton("▼", "Move down", () =>
        {
            if (_session is null) return;
            int idx = _session.Timers.IndexOf(t);
            if (idx >= 0 && idx < _session.Timers.Count - 1) _session.MoveTimer(t, idx + 1);
        }));
        actions.Children.Add(MakeIconButton("✕", "Remove", () => _session?.RemoveTimer(t)));

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        border.Child = grid;

        // ─── Drag-to-reorder ──────────────────────────────────────────
        // Grab anywhere on the row (except the action buttons, which keep
        // their click semantics) and drag onto another row to reorder.
        border.Cursor = Cursors.SizeAll;
        border.ToolTip = "Drag to reorder";
        AttachRowDragHandlers(border, t);

        // Slide-in animation for newly added rows. Suppressed during a drag
        // reorder so the queue doesn't visibly re-animate every row when
        // the model rebuilds — that's what was causing the glitchy feel.
        border.Opacity = _suppressNextRowAnim ? (isFinished ? 0.45 : 1.0) : 0;
        border.RenderTransform = new TranslateTransform(_suppressNextRowAnim ? 0 : 20, 0);
        if (!_suppressNextRowAnim)
        {
            border.Loaded += (_, _) =>
            {
                border.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, isFinished ? 0.45 : 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
                ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });
            };
        }

        return border;
    }

    /// <summary>Set during a drag-drop reorder so newly built rows don't animate in.</summary>
    private bool _suppressNextRowAnim;

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
        int mins = ParseInt(NewMinutesBox.Text, 0);
        int secs = ParseInt(NewSecondsBox.Text, 0);
        TimeSpan dur = TimeSpan.FromSeconds(Math.Max(1, mins * 60 + secs));

        _session.AddTimer(new TrainingTimer(title, dur));
    }

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (sender is not Button b || b.Tag is not string tag) return;
        string[] parts = tag.Split('|');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)) return;
        _session.AddTimer(new TrainingTimer(parts[0], TimeSpan.FromSeconds(seconds)));
    }

    private void OnBuildIntervalClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        int rounds = Math.Clamp(ParseInt(IntervalRoundsBox.Text, 1), 1, 100);
        int work = Math.Max(1, ParseInt(IntervalWorkBox.Text, 20));
        int rest = Math.Max(0, ParseInt(IntervalRestBox.Text, 10));

        for (int i = 1; i <= rounds; i++)
        {
            _session.AddTimer(new TrainingTimer($"WORK {i}/{rounds}", TimeSpan.FromSeconds(work)));
            if (rest > 0 && i < rounds)
                _session.AddTimer(new TrainingTimer($"REST", TimeSpan.FromSeconds(rest)));
        }
    }

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        _session?.Clear();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke();
    }

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
}
