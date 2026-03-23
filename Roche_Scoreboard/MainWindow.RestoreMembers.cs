using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WinForms = System.Windows.Forms;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // ── Scoring run auto-detection ──
    private enum ScoringRunKind { Standard, Blowout }

    private readonly record struct ScoringRunInfo(ScoringRunKind Kind, TeamSide Team, int Margin, int EventCount, DateTime StartTime);

    private TeamSide _lastShownRunTeam;
    private int _lastShownRunMargin;
    private DateTime _lastShownRunTime;
    private static readonly TimeSpan RunCooldown = TimeSpan.FromMinutes(3);

    // Scoring run thresholds — a run is a dominant stretch, not strict consecutive
    private const int RunMinScoringTeamPoints = 18;  // dominant team must score at least this
    private const double RunMinDominanceRatio = 3.0;  // dominant team must outscore opponent by this ratio
    private const int RunMaxOpponentPoints = 6;       // opponent can score up to this without breaking run
    private const int BlowoutMarginThreshold = 25;
    private const int BlowoutMinEvents = 4;
    private int _previousLeadChanges;
    private const int LeadChangeOverlayThreshold = 2;
    private const int LeadChangeMinTotal = 4;
    private DateTime _deferScorePushUntilUtc = DateTime.MinValue;

    // ── Periodic overlay schedule (based on clock elapsed seconds) ──
    private int _lastAutoStatsSecond = -1;
    private int _lastAutoWinProbSecond = -1;
    private const int AutoStatsIntervalSeconds = 240;     // every 4 minutes
    private const int AutoWinProbIntervalSeconds = 360;    // every 6 minutes
    private const int AutoWinProbOffsetSeconds = 120;      // offset by 2 minutes

    private void DeferScorePush(TimeSpan duration)
    {
        DateTime candidate = DateTime.UtcNow + duration;
        if (candidate > _deferScorePushUntilUtc)
        {
            _deferScorePushUntilUtc = candidate;
        }
    }

    private static TimeSpan GetGoalAnimationDuration(GoalAnimationStyle style)
    {
        return style switch
        {
            GoalAnimationStyle.Electric => TimeSpan.FromSeconds(1.7),
            GoalAnimationStyle.Cinematic => TimeSpan.FromSeconds(3.3),
            GoalAnimationStyle.Clean => TimeSpan.FromSeconds(0.9),
            GoalAnimationStyle.Classic => TimeSpan.FromSeconds(2.2),
            _ => TimeSpan.FromSeconds(1.7)
        };
    }

    private Border CreateScoreLogBar(ScoreEvent ev)
    {
        bool isHome = ev.Team == TeamSide.Home;
        MediaColor teamColor = GetTeamColor(isHome);
        MediaColor accentColor = MediaColor.FromArgb(0x60, teamColor.R, teamColor.G, teamColor.B);
        string teamName = isHome ? _match.HomeName : _match.AwayName;
        string type = ev.Type == ScoreType.Goal ? "GOAL" : "BEHIND";
        string clock = $"Q{ev.Quarter}  {(int)ev.GameTime.TotalMinutes:D2}:{ev.GameTime.Seconds:D2}";

        MediaColor homeColor = GetTeamColor(true);
        MediaColor awayColor = GetTeamColor(false);

        // ── Accent stripe (left edge, team colour) ──
        Border accentStripe = new()
        {
            Width = 4,
            Background = new SolidColorBrush(teamColor),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(2)
        };

        // ── Header row: GOAL/BEHIND label + team name + clock ──
        TextBlock typeLabel = new()
        {
            Text = type,
            Foreground = new SolidColorBrush(teamColor),
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock teamLabel = new()
        {
            Text = teamName.ToUpper(),
            Foreground = MediaBrushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock clockLabel = new()
        {
            Text = clock,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x6B, 0x7E, 0x95)),
            FontSize = 10,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        StackPanel headerLeft = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
        headerLeft.Children.Add(typeLabel);
        headerLeft.Children.Add(teamLabel);

        Grid headerRow = new();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerLeft, 0);
        Grid.SetColumn(clockLabel, 1);
        headerRow.Children.Add(headerLeft);
        headerRow.Children.Add(clockLabel);

        // ── Scoreline row: HOME G.B.T — AWAY G.B.T ──
        var dimBrush = new SolidColorBrush(MediaColor.FromRgb(0x7A, 0x8A, 0x9E));
        var brightBrush = MediaBrushes.White;
        bool homeIsScorer = isHome;

        TextBlock homeNameScore = new()
        {
            Text = _match.HomeName.ToUpper(),
            Foreground = homeIsScorer ? new SolidColorBrush(homeColor) : dimBrush,
            FontSize = 11,
            FontWeight = homeIsScorer ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };

        TextBlock homeScoreLabel = new()
        {
            Text = $"{ev.HomeGoals}.{ev.HomeBehinds}.{ev.HomeTotal}",
            Foreground = homeIsScorer ? brightBrush : dimBrush,
            FontSize = 12,
            FontWeight = homeIsScorer ? FontWeights.ExtraBold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock separator = new()
        {
            Text = "–",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x50, 0x5E, 0x70)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };

        TextBlock awayScoreLabel = new()
        {
            Text = $"{ev.AwayGoals}.{ev.AwayBehinds}.{ev.AwayTotal}",
            Foreground = !homeIsScorer ? brightBrush : dimBrush,
            FontSize = 12,
            FontWeight = !homeIsScorer ? FontWeights.ExtraBold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        TextBlock awayNameScore = new()
        {
            Text = _match.AwayName.ToUpper(),
            Foreground = !homeIsScorer ? new SolidColorBrush(awayColor) : dimBrush,
            FontSize = 11,
            FontWeight = !homeIsScorer ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        StackPanel scoreLine = new()
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0)
        };
        scoreLine.Children.Add(homeNameScore);
        scoreLine.Children.Add(homeScoreLabel);
        scoreLine.Children.Add(separator);
        scoreLine.Children.Add(awayScoreLabel);
        scoreLine.Children.Add(awayNameScore);

        // ── Assemble content ──
        StackPanel content = new() { Margin = new Thickness(10, 0, 0, 0) };
        content.Children.Add(headerRow);
        content.Children.Add(scoreLine);

        Grid inner = new();
        inner.Children.Add(accentStripe);
        inner.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(accentColor),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x40, teamColor.R, teamColor.G, teamColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(6, 5, 8, 5),
            Child = inner
        };
    }

    /// <summary>
    /// Creates a styled separator bar for the match log that marks the start of a quarter.
    /// </summary>
    private static Border CreateQuarterSplitBar(int quarter)
    {
        string label = quarter switch
        {
            1 => "QUARTER 1",
            2 => "QUARTER 2",
            3 => "QUARTER 3",
            4 => "QUARTER 4",
            _ => $"QUARTER {quarter}"
        };

        TextBlock text = new()
        {
            Text = label,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x94, 0xA3, 0xB8)),
            FontSize = 10,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lineBrush = new SolidColorBrush(MediaColor.FromRgb(0x2A, 0x3B, 0x4E));
        Border leftLine = new() { Height = 1, Background = lineBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        Border rightLine = new() { Height = 1, Background = lineBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center };

        Grid.SetColumn(leftLine, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(rightLine, 2);

        text.Margin = new Thickness(10, 0, 10, 0);

        content.Children.Add(leftLine);
        content.Children.Add(text);
        content.Children.Add(rightLine);

        return new Border
        {
            Margin = new Thickness(0, 6, 0, 6),
            Child = content
        };
    }

    private void RefreshVisibleScreens()
    {
        if (_displayWindow == null || !_displayWindow.IsVisible) return;

        if (_breakScreen?.Visibility == Visibility.Visible)
        {
            PopulateBreakScreen();
        }

        if (_scoreworm?.Visibility == Visibility.Visible)
        {
            PopulateScoreworm();
        }

        if (_statsScreen?.Visibility == Visibility.Visible)
        {
            PopulateStatsScreen();
        }
    }

    private void StartPause_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        if (_match.ClockRunning)
        {
            _match.PauseClock();
            UpdateLiveUI();
            return;
        }

        if (_showingBreakScreen || _isBreakRotating)
        {
            StopBreakRotation();
            ShowScorebug();
        }

        _match.StartClock();
        UpdateLiveUI();
    }

    private void HomeGoal_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        DeferScorePush(GetGoalAnimationDuration(style));

        ScoreEvent ev = _match.AddGoal(TeamSide.Home);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo() => _scorebug?.PlayScoreFlipOnly(true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
            PlayCustomGoalVideoIfConfigured(true, afterVideo);
            return;
        }

        _scorebug?.PlayScoreAnimation(true, true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
    }

    private void HomeBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        DeferScorePush(TimeSpan.FromMilliseconds(650));
        ScoreEvent ev = _match.AddBehind(TeamSide.Home);
        _scorebug?.PlayScoreFlipOnly(true, ev.HomeGoals, ev.HomeBehinds, ev.HomeTotal);
    }

    private void AwayGoal_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        GoalAnimationStyle style = _scorebug?.GetGoalAnimationStyle() ?? GoalAnimationStyle.Broadcast;
        DeferScorePush(GetGoalAnimationDuration(style));

        ScoreEvent ev = _match.AddGoal(TeamSide.Away);

        if (style == GoalAnimationStyle.CustomVideo)
        {
            void afterVideo() => _scorebug?.PlayScoreFlipOnly(false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
            PlayCustomGoalVideoIfConfigured(false, afterVideo);
            return;
        }

        _scorebug?.PlayScoreAnimation(false, true, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
    }

    private void AwayBehind_Click(object sender, RoutedEventArgs e)
    {
        EnsureScoreboardWindow();

        DeferScorePush(TimeSpan.FromMilliseconds(650));
        ScoreEvent ev = _match.AddBehind(TeamSide.Away);
        _scorebug?.PlayScoreFlipOnly(false, ev.AwayGoals, ev.AwayBehinds, ev.AwayTotal);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_match.UndoLastScore())
        {
            // Clear any pending score animation defer so undo shows immediately
            _deferScorePushUntilUtc = DateTime.MinValue;
            _scorebug?.SetScores(_match.HomeGoals, _match.HomeBehinds, _match.AwayGoals, _match.AwayBehinds);
            RebuildMatchLog();
            UpdateLiveUI();
        }
    }

    private void EndQuarter_Click(object sender, RoutedEventArgs e)
    {
        if (!_match.EndQuarter()) return;

        _fiveMinWarningShown = false;
        _lastAutoStatsSecond = -1;
        _lastAutoWinProbSecond = -1;
        UpdateLiveUI();

        // Animate the quarter tile label to the break abbreviation (Q1→QT, Q2→HT, etc.)
        int endedQuarter = GetEndedQuarter();
        _scorebug?.AnimateQuarterToBreakLabel(endedQuarter);

        DispatcherTimer delayTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };
        delayTimer.Tick += (_, _) =>
        {
            delayTimer.Stop();
            ShowBreakScreen();
            StartBreakRotation();
        };
        delayTimer.Start();
    }

    private void RebuildMatchLog()
    {
        if (EventList == null) return;

        EventList.Children.Clear();

        // Events are iterated oldest→newest and inserted at position 0,
        // so the visual order is newest-first. Insert quarter splits when
        // the quarter changes between consecutive events.
        int lastQuarter = -1;
        foreach (var ev in _match.Events)
        {
            if (lastQuarter != -1 && ev.Quarter != lastQuarter)
            {
                // Quarter boundary — insert a separator for the NEW quarter.
                // Because we Insert(0, ...), this separator ends up below the
                // events of the new quarter and above the events of the old one.
                EventList.Children.Insert(0, CreateQuarterSplitBar(ev.Quarter));
            }

            EventList.Children.Insert(0, CreateScoreLogBar(ev));
            lastQuarter = ev.Quarter;
        }
    }

    private void UpdateClockStatus()
    {
        var dc = _match.DisplayClock;
        ClockStatusText.Text = $"{(_match.ClockMode == ClockMode.Countdown ? "Countdown" : "Count Up")} · {(_match.ClockRunning ? "Running" : "Stopped")} · {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
    }

    private void StartBreakRotation()
    {
        StopBreakRotation();
        _rotationIndex = 0;
        _isBreakRotating = true;
        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _rotationTimer.Tick += (_, _) =>
        {
            _rotationIndex = (_rotationIndex + 1) % 3;
            switch (_rotationIndex)
            {
                case 1:
                    ShowScorewormRotation();
                    break;
                case 2:
                    ShowStatsScreenRotation();
                    break;
                default:
                    ShowBreakScreenRotation();
                    break;
            }
        };
        _rotationTimer.Start();
    }

    private void StopBreakRotation()
    {
        _rotationTimer?.Stop();
        _rotationTimer = null;
        _isBreakRotating = false;
    }

    private void ShowScorebug()
    {
        if (_scorebug == null) return;
        SwitchToScreen(_scorebug, "Live Scorebug");
    }

    private string? GetMatchStateBarTitle()
    {
        if (_match.ClockRunning)
        {
            var dc = _match.DisplayClock;
            return $"Q{_match.Quarter}  {(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}";
        }

        return null;
    }

    private int GetEndedQuarter()
    {
        // After EndQuarter(), Quarter is already incremented (except Q4 which stays 4).
        // If clock is not running and no time has elapsed in the current quarter,
        // we're at a break — the quarter that just ended is Quarter-1.
        // Special case: Q4 stays at 4, check if Q4 snapshot exists for full time.
        if (!_match.ClockRunning && _match.ElapsedInQuarter <= TimeSpan.Zero && _match.Quarter > 1)
        {
            // If Q4 snapshot exists, all quarters are done → FT (endedQuarter=4)
            if (_match.GetQuarterSnapshot(4) != null)
            {
                return 4;
            }

            return _match.Quarter - 1;
        }

        return _match.Quarter;
    }

    private void ShowBreakScreen()
    {
        PopulateBreakScreen();
        SwitchToScreen(_breakScreen!, "Quarter Summary");
    }

    private void ShowBreakScreenRotation()
    {
        PopulateBreakScreen();
        SwitchToScreen(_breakScreen!, "Quarter Summary", fromRotation: true);
    }

    private void PopulateBreakScreen()
    {
        if (_breakScreen == null) return;

        _breakScreen.Populate(_match, GetEndedQuarter(),
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text,
            _homeLogoPath, _awayLogoPath, GetMatchStateBarTitle());
    }

    private void ShowScoreworm()
    {
        PopulateScoreworm();
        SwitchToScreen(_scoreworm!, "Score Worm");
    }

    private void ShowScorewormRotation()
    {
        PopulateScoreworm();
        SwitchToScreen(_scoreworm!, "Score Worm", fromRotation: true);
    }

    private void PopulateScoreworm()
    {
        if (_scoreworm == null) return;

        _scoreworm.Populate(_match, GetEndedQuarter(),
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text, GetMatchStateBarTitle());
    }

    private void ShowStatsScreen()
    {
        PopulateStatsScreen();
        SwitchToScreen(_statsScreen!, "Stats Screen");
    }

    private void ShowStatsScreenRotation()
    {
        PopulateStatsScreen();
        SwitchToScreen(_statsScreen!, "Stats Screen", fromRotation: true);
    }

    private void PopulateStatsScreen()
    {
        if (_statsScreen == null) return;
        var stats = MatchStats.Calculate(_match);

        _statsScreen.Populate(_match, stats, GetEndedQuarter(),
            HomeColorBox.Text, AwayColorBox.Text,
            HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text, GetMatchStateBarTitle());
    }

    private void ApplyCurrentTeamColorsToScorebug()
    {
        _scorebug?.SetTeamColors(HomeColorBox.Text, AwayColorBox.Text, HomeSecondaryColorBox.Text, AwaySecondaryColorBox.Text);
    }

    private void ApplyTeamColorsToScoringPanels()
    {
        MediaColor homeColor = GetTeamColor(true);
        MediaColor awayColor = GetTeamColor(false);

        // Home panel: darker tinted background and border from team color
        MediaColor homeBg = MediaColor.FromRgb(
            (byte)(homeColor.R / 5), (byte)(homeColor.G / 5), (byte)(homeColor.B / 5));
        MediaColor homeBorder = MediaColor.FromArgb(0xAA, homeColor.R, homeColor.G, homeColor.B);
        MediaColor homeLabelFg = MediaColor.FromArgb(0xCC, 
            (byte)Math.Min(homeColor.R + 100, 255), 
            (byte)Math.Min(homeColor.G + 100, 255), 
            (byte)Math.Min(homeColor.B + 100, 255));

        HomeScoringBorder.Background = new SolidColorBrush(homeBg);
        HomeScoringBorder.BorderBrush = new SolidColorBrush(homeBorder);
        HomeScoringLabel.Foreground = new SolidColorBrush(homeLabelFg);
        HomeGoalButton.Background = new SolidColorBrush(homeColor);
        HomeGoalButton.BorderBrush = new SolidColorBrush(homeLabelFg);
        HomeGoalButton.Foreground = ContrastHelper.GetContrastBrush(homeColor);
        HomeBehindButton.Background = new SolidColorBrush(
            MediaColor.FromRgb((byte)(homeColor.R / 3), (byte)(homeColor.G / 3), (byte)(homeColor.B / 3)));
        HomeBehindButton.BorderBrush = new SolidColorBrush(
            MediaColor.FromArgb(0x88, homeColor.R, homeColor.G, homeColor.B));
        HomeScoreDisplay.Foreground = ContrastHelper.GetContrastBrush(homeBg);

        // Away panel
        MediaColor awayBg = MediaColor.FromRgb(
            (byte)(awayColor.R / 5), (byte)(awayColor.G / 5), (byte)(awayColor.B / 5));
        MediaColor awayBorder = MediaColor.FromArgb(0xAA, awayColor.R, awayColor.G, awayColor.B);
        MediaColor awayLabelFg = MediaColor.FromArgb(0xCC,
            (byte)Math.Min(awayColor.R + 100, 255),
            (byte)Math.Min(awayColor.G + 100, 255),
            (byte)Math.Min(awayColor.B + 100, 255));

        AwayScoringBorder.Background = new SolidColorBrush(awayBg);
        AwayScoringBorder.BorderBrush = new SolidColorBrush(awayBorder);
        AwayScoringLabel.Foreground = new SolidColorBrush(awayLabelFg);
        AwayGoalButton.Background = new SolidColorBrush(awayColor);
        AwayGoalButton.BorderBrush = new SolidColorBrush(awayLabelFg);
        AwayGoalButton.Foreground = ContrastHelper.GetContrastBrush(awayColor);
        AwayBehindButton.Background = new SolidColorBrush(
            MediaColor.FromRgb((byte)(awayColor.R / 3), (byte)(awayColor.G / 3), (byte)(awayColor.B / 3)));
        AwayBehindButton.BorderBrush = new SolidColorBrush(
            MediaColor.FromArgb(0x88, awayColor.R, awayColor.G, awayColor.B));
        AwayScoreDisplay.Foreground = ContrastHelper.GetContrastBrush(awayBg);
    }

    private void PlayCustomGoalVideoIfConfigured(bool isHome, Action fallbackAnimation)
    {
        if (_scorebug == null)
        {
            return;
        }

        if (_scorebug.GetGoalAnimationStyle() != GoalAnimationStyle.CustomVideo)
        {
            fallbackAnimation();
            return;
        }

        string? path = isHome ? _homeGoalVideoPath : _awayGoalVideoPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) || _goalVideoOverlay == null)
        {
            fallbackAnimation();
            return;
        }

        // If a goal video is already playing, stop it and fire its pending callback
        // so the previous score flip completes before starting the new one
        if (_goalVideoOverlay.Source != null && _goalVideoCompletionCallback != null)
        {
            _goalVideoOverlay.Stop();
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
            Action? prev = _goalVideoCompletionCallback;
            _goalVideoCompletionCallback = null;
            prev.Invoke();
        }

        try
        {
            _goalVideoCompletionCallback = fallbackAnimation;
            _goalVideoOverlay.Source = new Uri(path, UriKind.Absolute);
            _goalVideoOverlay.Visibility = Visibility.Visible;
            _goalVideoOverlay.Position = TimeSpan.Zero;
            _goalVideoOverlay.Play();
        }
        catch (UriFormatException)
        {
            _goalVideoCompletionCallback = null;
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
            fallbackAnimation();
        }
    }

    private void GoalVideo_MediaEnded(object? sender, RoutedEventArgs e)
    {
        _goalVideoOverlay?.Stop();
        if (_goalVideoOverlay != null)
        {
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
        }

        Action? callback = _goalVideoCompletionCallback;
        _goalVideoCompletionCallback = null;
        callback?.Invoke();
    }

    private void GoalVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _goalVideoOverlay?.Stop();
        if (_goalVideoOverlay != null)
        {
            _goalVideoOverlay.Visibility = Visibility.Collapsed;
        }

        Action? callback = _goalVideoCompletionCallback;
        _goalVideoCompletionCallback = null;
        callback?.Invoke();
    }

    private void StopStatsBarTimer()
    {
        _statsBarTimer?.Stop();
        _statsBarTimer = null;
        _statsBarHideTimer?.Stop();
        _statsBarHideTimer = null;
    }

    private void StopGoalVideo()
    {
        if (_goalVideoOverlay == null) return;

        _goalVideoOverlay.Stop();
        _goalVideoOverlay.Source = null;
        _goalVideoOverlay.Visibility = Visibility.Collapsed;
    }

    // ── Periodic overlay scheduling ──

    private void CheckPeriodicOverlays()
    {
        if (_scorebug == null || _showingBreakScreen || !_match.ClockRunning) return;
        if (_match.Events.Count == 0) return;

        int elapsed = (int)_match.ElapsedInQuarter.TotalSeconds;

        // Stats bar: every AutoStatsIntervalSeconds (4 min)
        if (elapsed > 0 && elapsed % AutoStatsIntervalSeconds == 0 && elapsed != _lastAutoStatsSecond)
        {
            _lastAutoStatsSecond = elapsed;
            ApplyCurrentTeamColorsToScorebug();
            var stats = MatchStats.Calculate(_match);
            _scorebug.ShowStatsBar(stats);
        }

        // Win probability: every AutoWinProbIntervalSeconds (6 min), offset by AutoWinProbOffsetSeconds (2 min)
        int winProbCheck = elapsed - AutoWinProbOffsetSeconds;
        if (winProbCheck > 0 && winProbCheck % AutoWinProbIntervalSeconds == 0 && elapsed != _lastAutoWinProbSecond)
        {
            _lastAutoWinProbSecond = elapsed;
            ApplyCurrentTeamColorsToScorebug();
            ComputeAndShowWinProbability();
        }
    }

    private void ResetPeriodicOverlayTracking()
    {
        _lastAutoStatsSecond = -1;
        _lastAutoWinProbSecond = -1;
        _previousLeadChanges = 0;
        _lastShownRunTime = DateTime.MinValue;
        _lastShownRunMargin = 0;
    }

    // ── Scoring run auto-detection ──

    private void CheckScoringRunAfterScore(ScoreEvent ev)
    {
        if (_scorebug == null || _showingBreakScreen) return;

        var events = _match.Events;
        if (events.Count < 3) return;

        // Scan backward from the latest event to find a dominant scoring stretch.
        // The run belongs to the team that scored last. Walk back and accumulate
        // points for both sides. The run window extends as long as the dominant team
        // maintains a strong ratio advantage. Stop when the opponent's accumulated
        // points exceed RunMaxOpponentPoints or the ratio drops below threshold.
        TeamSide runTeam = ev.Team;
        int runPoints = 0;
        int opponentPoints = 0;
        int runEventCount = 0;
        DateTime runStart = ev.Timestamp;

        for (int i = events.Count - 1; i >= 0; i--)
        {
            var e = events[i];
            int pts = e.Type == ScoreType.Goal ? 6 : 1;

            if (e.Team == runTeam)
            {
                runPoints += pts;
            }
            else
            {
                // Check if adding this opponent score would break the run
                int newOppPts = opponentPoints + pts;
                if (newOppPts > RunMaxOpponentPoints)
                    break;
                if (runPoints > 0 && (double)runPoints / (newOppPts + 1) < RunMinDominanceRatio)
                    break;
                opponentPoints = newOppPts;
            }

            runEventCount++;
            runStart = e.Timestamp;
        }

        if (runPoints < RunMinScoringTeamPoints) return;

        // Cooldown — don't re-show unless the run has grown significantly
        if (runTeam == _lastShownRunTeam &&
            (DateTime.Now - _lastShownRunTime) < RunCooldown &&
            runPoints <= _lastShownRunMargin)
        {
            return;
        }

        _lastShownRunTeam = runTeam;
        _lastShownRunMargin = runPoints;
        _lastShownRunTime = DateTime.Now;

        string teamName = runTeam == TeamSide.Home ? _match.HomeName : _match.AwayName;
        MediaColor teamColor = GetTeamColor(runTeam == TeamSide.Home);

        ApplyCurrentTeamColorsToScorebug();
        _scorebug.ShowScoringRun(teamName, runPoints, opponentPoints, runStart, teamColor);
    }

    // ── Lead change auto-detection ──

    private void CheckLeadChangesAfterScore()
    {
        if (_scorebug == null || _showingBreakScreen) return;

        var stats = MatchStats.Calculate(_match);
        if (stats.LeadChanges < LeadChangeMinTotal) return;

        int newChanges = stats.LeadChanges - _previousLeadChanges;
        if (newChanges >= LeadChangeOverlayThreshold)
        {
            _previousLeadChanges = stats.LeadChanges;
            ApplyCurrentTeamColorsToScorebug();
            _scorebug.ShowLeadChangesBar(stats.LeadChanges);
        }
    }

    private IntPtr DisplayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return IntPtr.Zero;
    }

    private bool _isBreakRotating;

    private void SwitchToScreen(UIElement target, string label, bool fromRotation = false)
    {
        EnsureScoreboardWindow();
        if (_displayWindow is { IsVisible: false })
        {
            _displayWindow.Show();
        }

        if (!fromRotation)
        {
            StopBreakRotation();
        }

        if (target != _videoPlayer)
        {
            VideoPickerPrompt.Visibility = Visibility.Collapsed;
        }

        var current = GetCurrentlyVisibleScreen();
        if (current == target)
        {
            CurrentDisplayText.Text = label;
            return;
        }

        if (current != null)
        {
            SlideTransition(current, target, true);
        }
        else
        {
            target.Visibility = Visibility.Visible;
            target.RenderTransform = new TranslateTransform(0, 0);
        }

        _showingBreakScreen = target != _scorebug;
        CurrentDisplayText.Text = label;

        if (target == _videoPlayer)
        {
            _videoPlayer?.Play();
        }
    }

    private void SlideTransition(UIElement from, UIElement to, bool slideLeft)
    {
        double width = _displayWindow?.ActualWidth ?? 752;
        double direction = slideLeft ? -1 : 1;

        to.Visibility = Visibility.Visible;
        to.RenderTransform = new TranslateTransform(-direction * width, 0);

        if (from.RenderTransform is not TranslateTransform)
        {
            from.RenderTransform = new TranslateTransform(0, 0);
        }

        CubicEase ease = new() { EasingMode = EasingMode.EaseInOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(360);

        DoubleAnimation fromAnim = new(0, direction * width, duration) { EasingFunction = ease };
        DoubleAnimation toAnim = new(-direction * width, 0, duration) { EasingFunction = ease };

        fromAnim.Completed += (_, _) =>
        {
            from.Visibility = Visibility.Collapsed;
            from.RenderTransform = new TranslateTransform(0, 0);
        };

        ((TranslateTransform)from.RenderTransform).BeginAnimation(TranslateTransform.XProperty, fromAnim);
        ((TranslateTransform)to.RenderTransform).BeginAnimation(TranslateTransform.XProperty, toAnim);
    }

    private void AnimationRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_scorebug == null) return;

        GoalAnimationStyle style;
        string title;
        string desc;
        string duration;
        string intensity;

        if (AnimElectricRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Electric;
            title = "Electric";
            desc = "Fast strobe hit with aggressive text impact.";
            duration = "~1.6s";
            intensity = "High";
        }
        else if (AnimCinematicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Cinematic;
            title = "Cinematic";
            desc = "Long dramatic reveal with slower pacing.";
            duration = "~3.2s";
            intensity = "Low";
        }
        else if (AnimCleanRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Clean;
            title = "Clean";
            desc = "Minimal score pulse with subtle polish.";
            duration = "~0.8s";
            intensity = "Minimal";
        }
        else if (AnimClassicRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.Classic;
            title = "Classic";
            desc = "Traditional sweep and shimmer sequence.";
            duration = "~2.1s";
            intensity = "Medium";
        }
        else if (AnimCustomVideoRadio?.IsChecked == true)
        {
            style = GoalAnimationStyle.CustomVideo;
            title = "Custom Video";
            desc = "Team-specific goal video playback if configured.";
            duration = "Video";
            intensity = "Custom";
        }
        else
        {
            style = GoalAnimationStyle.Broadcast;
            title = "Broadcast";
            desc = "Broadcast-style banner with team-color sweep.";
            duration = "~1.6s";
            intensity = "Medium";
        }

        _scorebug.SetGoalAnimationStyle(style);

        if (AnimPreviewTitle != null)
        {
            AnimPreviewTitle.Text = title;
            AnimPreviewDesc.Text = desc;
            AnimPreviewDuration.Text = duration;
            AnimPreviewIntensity.Text = intensity;
        }
    }

    private MediaColor GetTeamColor(bool home)
    {
        string hex = home ? HomeColorBox.Text : AwayColorBox.Text;
        try
        {
            return (MediaColor)MediaColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return home ? MediaColor.FromRgb(0x2C, 0x66, 0xFF) : MediaColor.FromRgb(0xD3, 0x3D, 0x3D);
        }
        catch (NotSupportedException)
        {
            return home ? MediaColor.FromRgb(0x2C, 0x66, 0xFF) : MediaColor.FromRgb(0xD3, 0x3D, 0x3D);
        }
    }

    private UIElement? GetCurrentlyVisibleScreen()
    {
        if (_videoPlayer?.Visibility == Visibility.Visible) return _videoPlayer;
        if (_breakScreen?.Visibility == Visibility.Visible) return _breakScreen;
        if (_scoreworm?.Visibility == Visibility.Visible) return _scoreworm;
        if (_statsScreen?.Visibility == Visibility.Visible) return _statsScreen;
        if (_scorebug?.Visibility == Visibility.Visible) return _scorebug;
        return null;
    }

    private void ApplyLogoCropToScorebug()
    {
        _scorebug?.SetLogoCrop(
            _homeLogoZoom, _homeLogoOffsetX, _homeLogoOffsetY,
            _awayLogoZoom, _awayLogoOffsetX, _awayLogoOffsetY);
    }

    private void ComputeAndShowWinProbability()
    {
        if (_scorebug == null || _showingBreakScreen) return;

        _scorebug.SetWinProbColors(GetTeamColor(true), GetTeamColor(false));
        var snapshot = MatchSnapshot.FromMatch(_match);
        WinProbabilityResult result = _winProbEngine.Compute(snapshot);
        _scorebug.ShowWinProbability(result);
    }

    private void LogoCropSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingLogoCropControls) return;

        if (HomeLogoZoomSlider == null || HomeLogoXSlider == null || HomeLogoYSlider == null ||
            AwayLogoZoomSlider == null || AwayLogoXSlider == null || AwayLogoYSlider == null)
        {
            return;
        }

        _homeLogoZoom = HomeLogoZoomSlider.Value;
        _homeLogoOffsetX = HomeLogoXSlider.Value;
        _homeLogoOffsetY = HomeLogoYSlider.Value;
        _awayLogoZoom = AwayLogoZoomSlider.Value;
        _awayLogoOffsetX = AwayLogoXSlider.Value;
        _awayLogoOffsetY = AwayLogoYSlider.Value;

        ApplyLogoCropToScorebug();
        UpdateInlineLogoCropPreviews();
        if (LogoCropSummaryText != null)
        {
            LogoCropSummaryText.Text =
                $"Home: z {_homeLogoZoom:0.00}, x {_homeLogoOffsetX:0}, y {_homeLogoOffsetY:0}    |    " +
                $"Away: z {_awayLogoZoom:0.00}, x {_awayLogoOffsetX:0}, y {_awayLogoOffsetY:0}";
        }

        PushAllToScoreboard();
    }

    private void ResetHomeCrop_Click(object sender, RoutedEventArgs e)
    {
        HomeLogoZoomSlider.Value = 1.0;
        HomeLogoXSlider.Value = 0;
        HomeLogoYSlider.Value = 0;
    }

    private void ResetAwayCrop_Click(object sender, RoutedEventArgs e)
    {
        AwayLogoZoomSlider.Value = 1.0;
        AwayLogoXSlider.Value = 0;
        AwayLogoYSlider.Value = 0;
    }

    private void ApplyTeamsAndStyling_Click(object sender, RoutedEventArgs e)
    {
        _match.SetTeams(HomeNameBox.Text, HomeAbbrBox.Text, AwayNameBox.Text, AwayAbbrBox.Text);
        EnsureScoreboardWindow();
        PushAllToScoreboard();
        ApplyTeamColorsToScoringPanels();
        ApplyStatusText.Text = "Applied.";
    }

    private void PickHomeColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(HomeColorBox, HomeColorPreview);
    private void PickAwayColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(AwayColorBox, AwayColorPreview);
    private void PickHomeSecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(HomeSecondaryColorBox, HomeSecondaryColorPreview);
    private void PickAwaySecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickColorInto(AwaySecondaryColorBox, AwaySecondaryColorPreview);

    private void DropperHomeColor_Click(object sender, MouseButtonEventArgs e) => PickHomeColor_Click(sender, e);
    private void DropperAwayColor_Click(object sender, MouseButtonEventArgs e) => PickAwayColor_Click(sender, e);
    private void DropperHomeSecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickHomeSecondaryColor_Click(sender, e);
    private void DropperAwaySecondaryColor_Click(object sender, MouseButtonEventArgs e) => PickAwaySecondaryColor_Click(sender, e);

    private void PickColorInto(System.Windows.Controls.TextBox target, Border preview)
    {
        using WinForms.ColorDialog dialog = new() { FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

        string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        target.Text = hex;
        preview.Background = new SolidColorBrush(MediaColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        PushAllToScoreboard();
    }

    private void AddMessage_Click(object sender, RoutedEventArgs e)
    {
        string text = NewMessageBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;

        _messages.Add(text);
        MessageList.Items.Add(text);
        NewMessageBox.Text = string.Empty;
        _scorebug?.SetMarqueeMessages(_messages);
    }

    private void RemoveMessage_Click(object sender, RoutedEventArgs e)
    {
        int index = MessageList.SelectedIndex;
        if (index < 0 || index >= _messages.Count) return;

        _messages.RemoveAt(index);
        MessageList.Items.RemoveAt(index);
        _scorebug?.SetMarqueeMessages(_messages);
    }

    private void NewMessageBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddMessage_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void QuarterDurationBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyQuarterDuration_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void ClockMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ClockModeCountdown == null || QuarterDurationPanel == null || ClockModeCountUp == null)
        {
            return;
        }

        if (ClockModeCountdown.IsChecked == true)
        {
            _match.SetClockMode(ClockMode.Countdown);
            QuarterDurationPanel.Visibility = Visibility.Visible;
        }
        else
        {
            _match.SetClockMode(ClockMode.CountUp);
            QuarterDurationPanel.Visibility = Visibility.Collapsed;
        }

        if (ClockStatusText != null)
        {
            UpdateClockStatus();
        }
    }

    private void ApplyQuarterDuration_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(QuarterMinutesBox.Text, out int mins)) return;
        if (!int.TryParse(QuarterSecondsBox.Text, out int secs)) return;
        if (mins < 0 || secs < 0 || secs > 59) return;

        _match.SetQuarterDuration(TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs));
        UpdateClockStatus();
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog picker = new()
        {
            Title = "Select Video File",
            Filter = "Video Files|*.mp4;*.avi;*.wmv;*.mov;*.mkv|All Files|*.*"
        };

        if (picker.ShowDialog() != true) return;

        _customVideoPath = picker.FileName;
        VideoFilePathText.Text = System.IO.Path.GetFileName(_customVideoPath);
        ApplyVideoButton.IsEnabled = true;
    }

    private void ApplyVideo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_customVideoPath) || !System.IO.File.Exists(_customVideoPath)) return;

        EnsureScoreboardWindow();
        if (_videoPlayer == null) return;

        _videoPlayer.SetVideoSource(_customVideoPath);
        SwitchToScreen(_videoPlayer, "Custom Video");
        VideoPickerPrompt.Visibility = Visibility.Collapsed;
    }

    private void LayoutRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (_scorebug == null) return;

        ScorebugLayout layout = LayoutExpandedRadio?.IsChecked == true
            ? ScorebugLayout.Expanded
            : ScorebugLayout.Classic;

        _scorebug.SetLayout(layout);
    }

    private void NewGame_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to reset the match?\n\nAll scores, events, and settings will be cleared and you'll return to the setup screen.",
            "Reset Match",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        StopBreakRotation();
        StopStatsBarTimer();
        StopGoalVideo();

        _match.ResetForNewGame();
        _fiveMinWarningShown = false;
        _deferScorePushUntilUtc = DateTime.MinValue;
        _showingBreakScreen = false;
        ResetPeriodicOverlayTracking();

        EventList.Children.Clear();
        LastEventText.Text = string.Empty;
        CurrentDisplayText.Text = "";

        // Reset and collapse all display screens
        _scorebug?.ResetAllOverlayState();
        _scorebug?.SetScores(0, 0, 0, 0);
        if (_scorebug != null) _scorebug.Visibility = Visibility.Collapsed;
        if (_breakScreen != null) _breakScreen.Visibility = Visibility.Collapsed;
        if (_scoreworm != null) _scoreworm.Visibility = Visibility.Collapsed;
        if (_statsScreen != null) _statsScreen.Visibility = Visibility.Collapsed;
        if (_videoPlayer != null) _videoPlayer.Visibility = Visibility.Collapsed;

        // Hide the display window
        _displayWindow?.Hide();

        // Return to the setup wizard
        MainContent.BeginAnimation(OpacityProperty, null);
        SetupWizardPanel.BeginAnimation(OpacityProperty, null);
        MainContent.Visibility = Visibility.Collapsed;
        SetupWizardPanel.Reset();
        SetupWizardPanel.Opacity = 1;
        SetupWizardPanel.Visibility = Visibility.Visible;
    }

    private void ManualStatsBar_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:statsbar");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        var stats = MatchStats.Calculate(_match);
        _scorebug.ShowStatsBar(stats);
    }

    private void ManualWarning_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:warning");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        _scorebug.ShowFiveMinuteWarning();
    }

    private void ManualScoreless_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:lastscore");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        _scorebug.ShowScorelessBar();
    }

    private void ManualScoringRun_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:run");
        if (_scorebug == null || _showingBreakScreen) return;

        ApplyCurrentTeamColorsToScorebug();

        // Calculate the actual current scoring run using the same dominance algorithm
        var events = _match.Events;
        if (events.Count == 0) return;

        TeamSide runTeam = events[^1].Team;
        int runPoints = 0;
        int opponentPoints = 0;
        DateTime runStart = events[^1].Timestamp;

        for (int i = events.Count - 1; i >= 0; i--)
        {
            var ev = events[i];
            int pts = ev.Type == ScoreType.Goal ? 6 : 1;

            if (ev.Team == runTeam)
            {
                runPoints += pts;
            }
            else
            {
                int newOppPts = opponentPoints + pts;
                if (newOppPts > RunMaxOpponentPoints) break;
                if (runPoints > 0 && (double)runPoints / (newOppPts + 1) < RunMinDominanceRatio) break;
                opponentPoints = newOppPts;
            }

            runStart = ev.Timestamp;
        }

        if (runPoints == 0) runPoints = 1;

        string teamName = runTeam == TeamSide.Home ? _match.HomeName : _match.AwayName;
        _scorebug.ShowScoringRun(teamName, runPoints, opponentPoints, runStart, GetTeamColor(runTeam == TeamSide.Home));
    }

    private void ManualHomeDrought_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:homedrought");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        var since = _scorebug.HomeLastScoreTime;
        if (since == DateTime.MinValue) since = DateTime.Now.AddMinutes(-1);
        _scorebug.ShowTeamDrought(_match.HomeName, since, GetTeamColor(true));
    }

    private void ManualAwayDrought_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:awaydrought");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        var since = _scorebug.AwayLastScoreTime;
        if (since == DateTime.MinValue) since = DateTime.Now.AddMinutes(-1);
        _scorebug.ShowTeamDrought(_match.AwayName, since, GetTeamColor(false));
    }

    private void ManualWinProb_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:winprob");
        if (_scorebug == null || _showingBreakScreen) return;
        ApplyCurrentTeamColorsToScorebug();
        ComputeAndShowWinProbability();
    }

    private void ManualGBCounts_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedPreview("overlay:gbcounts");
        if (_scorebug == null || _showingBreakScreen) return;
        _scorebug.ToggleGBColumns();
    }
}
