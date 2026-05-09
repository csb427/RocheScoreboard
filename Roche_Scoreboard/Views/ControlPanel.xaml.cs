using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class ControlPanel : UserControl
    {
        private int _homeGoals;
        private int _homeBehinds;
        private int _awayGoals;
        private int _awayBehinds;
        private int _quarter = 1;
        private readonly int _maxQuarters = 4;
        private bool _timerRunning;
        private TimeSpan _timerValue = TimeSpan.Zero;
        private DispatcherTimer? _timer;

        public ControlPanel()
        {
            InitializeComponent();
            QuarterSelector.ItemsSource = new[] { "Q1", "Q2", "Q3", "Q4" };
            QuarterSelector.SelectedIndex = 0;
            Timer.Text = FormatTime(_timerValue);
            RefreshScoreDisplays();
            HookEvents();
        }

        private void HookEvents()
        {
            HomeGoalBtn.Click += (s, e) => AddScore(true, isGoal: true);
            HomeBehindBtn.Click += (s, e) => AddScore(true, isGoal: false);
            AwayGoalBtn.Click += (s, e) => AddScore(false, isGoal: true);
            AwayBehindBtn.Click += (s, e) => AddScore(false, isGoal: false);
            ClockStartBtn.Click += (s, e) => StartTimer();
            ClockPauseBtn.Click += (s, e) => PauseTimer();
            ClockResetBtn.Click += (s, e) => ResetTimer();
            QuarterSelector.SelectionChanged += (s, e) => ChangeQuarter(QuarterSelector.SelectedIndex + 1);
        }

        private void AddScore(bool isHome, bool isGoal)
        {
            int points = isGoal ? 6 : 1;
            string action = isGoal ? "Goal" : "Behind";

            if (isHome)
            {
                if (isGoal) _homeGoals++; else _homeBehinds++;
                AddEventLogEntry($"{Now()}  HOME  {action} (+{points})");
            }
            else
            {
                if (isGoal) _awayGoals++; else _awayBehinds++;
                AddEventLogEntry($"{Now()}  AWAY  {action} (+{points})");
            }

            RefreshScoreDisplays();
        }

        private void RefreshScoreDisplays()
        {
            int homeTotal = _homeGoals * 6 + _homeBehinds;
            int awayTotal = _awayGoals * 6 + _awayBehinds;

            HomeScore.Text = homeTotal.ToString();
            AwayScore.Text = awayTotal.ToString();
            HomeScoreDisplay.Text = $"{_homeGoals}.{_homeBehinds}.{homeTotal}";
            AwayScoreDisplay.Text = $"{_awayGoals}.{_awayBehinds}.{awayTotal}";
        }

        private void StartTimer()
        {
            if (_timerRunning) return;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += Timer_Tick;
            }
            _timer.Start();
            _timerRunning = true;
            AddEventLogEntry($"{Now()}  Clock started");
        }

        private void PauseTimer()
        {
            if (!_timerRunning) return;
            _timer?.Stop();
            _timerRunning = false;
            AddEventLogEntry($"{Now()}  Clock paused");
        }

        private void ResetTimer()
        {
            PauseTimer();
            _timerValue = TimeSpan.Zero;
            Timer.Text = FormatTime(_timerValue);
            AddEventLogEntry($"{Now()}  Clock reset");
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
            QuarterBadgeText.Text = $"Q{_quarter}";
            AddEventLogEntry($"{Now()}  Quarter → Q{_quarter}");
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
