using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Roche_Scoreboard.Views
{
    public partial class ControlPanel : UserControl
    {
        private int _homeScore = 0;
        private int _awayScore = 0;
        private int _quarter = 1;
        private readonly int _maxQuarters = 4;
        private bool _timerRunning = false;
        private TimeSpan _timerValue = TimeSpan.Zero;
        private DispatcherTimer? _timer;

        public ControlPanel()
        {
            InitializeComponent();
            QuarterSelector.ItemsSource = new[] { "Q1", "Q2", "Q3", "Q4" };
            QuarterSelector.SelectedIndex = 0;
            Timer.Text = FormatTime(_timerValue);
            HookEvents();
        }

        private void HookEvents()
        {
            HomeGoalBtn.Click += (s, e) => AddScore(true, 6, "Goal");
            HomeBehindBtn.Click += (s, e) => AddScore(true, 1, "Behind");
            AwayGoalBtn.Click += (s, e) => AddScore(false, 6, "Goal");
            AwayBehindBtn.Click += (s, e) => AddScore(false, 1, "Behind");
            ClockStartBtn.Click += (s, e) => StartTimer();
            ClockPauseBtn.Click += (s, e) => PauseTimer();
            ClockResetBtn.Click += (s, e) => ResetTimer();
            QuarterSelector.SelectionChanged += (s, e) => ChangeQuarter(QuarterSelector.SelectedIndex + 1);
        }

        private void AddScore(bool isHome, int points, string action)
        {
            if (isHome)
            {
                _homeScore += points;
                HomeScore.Text = _homeScore.ToString();
                AddEventLogEntry($"{Now()} Home: {action} (+{points})");
            }
            else
            {
                _awayScore += points;
                AwayScore.Text = _awayScore.ToString();
                AddEventLogEntry($"{Now()} Away: {action} (+{points})");
            }
        }

        private void StartTimer()
        {
            if (_timerRunning) return;
            _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            _timerRunning = true;
            AddEventLogEntry($"{Now()} Clock started");
        }

        private void PauseTimer()
        {
            if (!_timerRunning) return;
            _timer?.Stop();
            _timerRunning = false;
            AddEventLogEntry($"{Now()} Clock paused");
        }

        private void ResetTimer()
        {
            PauseTimer();
            _timerValue = TimeSpan.Zero;
            Timer.Text = FormatTime(_timerValue);
            AddEventLogEntry($"{Now()} Clock reset");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timerValue = _timerValue.Add(TimeSpan.FromSeconds(1));
            Timer.Text = FormatTime(_timerValue);
        }

        private void ChangeQuarter(int quarter)
        {
            if (quarter < 1 || quarter > _maxQuarters) return;
            _quarter = quarter;
            AddEventLogEntry($"{Now()} Quarter set to Q{_quarter}");
        }

        public void AddEventLogEntry(string text)
        {
            var item = new ListBoxItem { Content = text, Opacity = 0 };
            EventLog.Items.Insert(0, item);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            item.BeginAnimation(OpacityProperty, fade);
        }

        private static string FormatTime(TimeSpan ts) => $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        private static string Now() => DateTime.Now.ToString("HH:mm:ss");
    }
}
