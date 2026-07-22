// ─────────────────────────────────────────────────────────────────────────────
// MainWindow.CloudRemote.cs
//
// Bridges the WPF operator UI to the Supabase-backed cloud remote session.
// All cloud transport details live in Services/Cloud/*; this partial only
// implements <see cref="IRemoteCommandTarget"/> (so the dispatcher can press
// our existing local click handlers), builds <see cref="LiveStateSnapshot"/>
// instances on demand, and owns the lifecycle of the cloud session service.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;

namespace Roche_Scoreboard;

public partial class MainWindow : IRemoteCommandTarget
{
    private RemoteControlService? _cloudRemote;
    private SupabaseConfig? _cloudConfig;

    /// <summary>True while a cloud session has been created.</summary>
    public bool IsCloudSessionActive => _cloudRemote?.IsActive == true;

    /// <summary>Join code shown to remote operators (e.g. <c>K7HM-2QPR</c>).</summary>
    public string? CloudJoinCode => _cloudRemote?.JoinCode;

    /// <summary>Server-assigned game id.</summary>
    public Guid? CloudSessionId => _cloudRemote?.SessionId;

    /// <summary>
    /// Public join URL the remote scorer types or scans into their phone.
    /// Returns <c>null</c> when either the operator hasn't set
    /// <c>remoteAppBaseUrl</c> in <c>supabase.config.json</c> or no session
    /// is active yet.
    /// </summary>
    public string? CloudJoinUrl
    {
        get
        {
            string? code = CloudJoinCode;
            string? baseUrl = _cloudConfig?.RemoteAppBaseUrl;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(baseUrl)) return null;

            string trimmed = baseUrl.TrimEnd('/');
            return $"{trimmed}/?code={Uri.EscapeDataString(code)}";
        }
    }

    /// <summary>
    /// True when <c>supabase.config.json</c> exists and has the keys we
    /// need to start a session. The Cloud Remote card uses this to decide
    /// whether the Start button is enabled.
    /// </summary>
    public bool IsCloudConfigReady =>
        _cloudConfig is { IsValid: true } cfg && !string.IsNullOrWhiteSpace(cfg.ServiceRoleKey);

    /// <summary>
    /// Fires once the operator triggers a cloud session start (from the
    /// Settings card). The arg is the new join code so the UI can render
    /// the QR + heading without polling.
    /// </summary>
    public event Action<string>? CloudSessionStarted;

    /// <summary>Fires when the cloud session is fully torn down.</summary>
    public event Action? CloudSessionEnded;

    /// <summary>
    /// Called from <c>MainWindow_Loaded</c>. The cloud session is opt-in —
    /// the operator must click "Start cloud session" in the Settings card.
    /// On load we only probe for config presence so the UI can offer the
    /// button or explain why it is disabled.
    /// </summary>
    private void StartCloudRemoteOnLoad()
    {
        _cloudConfig = SupabaseConfig.TryLoad();
        RefreshCloudRemoteUi();
    }

    /// <summary>
    /// Pushes the current match snapshot to the active cloud remote session.
    /// Safe to call when no session is active.
    /// </summary>
    private void PublishCloudSnapshot()
    {
        if (_cloudRemote is null || !_cloudRemote.IsActive) return;
        try
        {
            _cloudRemote.PublishSnapshot(BuildLiveStateSnapshot());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudRemote] snapshot build failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Operator action: starts a brand-new cloud session. Called from the
    /// Settings → Cloud Remote card.
    /// </summary>
    public async Task StartCloudSessionAsync()
    {
        if (_cloudRemote is not null) return;

        SupabaseConfig? cfg = _cloudConfig ??= SupabaseConfig.TryLoad();
        if (cfg is null || !cfg.IsValid)
        {
            System.Windows.MessageBox.Show(this,
                "supabase.config.json was not found next to the executable.\n\n" +
                "See supabase/README.md for setup instructions.",
                "Cloud Remote",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.ServiceRoleKey))
        {
            System.Windows.MessageBox.Show(this,
                "supabase.config.json is missing 'serviceRoleKey'. " +
                "This key is required on the host scoreboard so it can " +
                "publish the live match state.\n\nSee supabase/README.md.",
                "Cloud Remote",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _cloudRemote = new RemoteControlService(cfg, this, Dispatcher);
            _cloudRemote.ConnectedChanged += HandleCloudConnectedChanged;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            bool isTraining = _trainingSession is not null;
            string sport = isTraining ? "training" : "afl";
            string sessionHome = isTraining ? "Training" : _match.HomeName;
            string sessionAway = isTraining ? "Session"  : _match.AwayName;

            LiveStateSnapshot initial = BuildLiveStateSnapshot() with { SessionStatus = "lobby" };
            await _cloudRemote.StartAsync(
                sport: sport,
                homeName: sessionHome,
                awayName: sessionAway,
                initialState: initial,
                ct: cts.Token).ConfigureAwait(true);

            CloudSessionStarted?.Invoke(_cloudRemote.JoinCode ?? "");
            RefreshCloudRemoteUi();
        }
        catch (Exception ex)
        {
            await SafeDisposeCloudAsync().ConfigureAwait(true);
            System.Windows.MessageBox.Show(this,
                $"Could not start cloud session:\n{ex.Message}",
                "Cloud Remote",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Operator action: ends the live cloud session.</summary>
    public async Task EndCloudSessionAsync()
    {
        await SafeDisposeCloudAsync().ConfigureAwait(true);
        CloudSessionEnded?.Invoke();
        RefreshCloudRemoteUi();
    }

    private async Task SafeDisposeCloudAsync()
    {
        if (_cloudRemote is null) return;
        _cloudRemote.ConnectedChanged -= HandleCloudConnectedChanged;
        try { await _cloudRemote.DisposeAsync().ConfigureAwait(true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CloudRemote] dispose: {ex.Message}"); }
        _cloudRemote = null;
    }

    private void HandleCloudConnectedChanged(bool connected)
    {
        if (Dispatcher.CheckAccess())
        {
            OnCloudConnectedChanged(connected);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => OnCloudConnectedChanged(connected)));
        }
    }

    /// <summary>
    /// Builds a snapshot of everything the remote app needs to render the
    /// current host state. Routes by active sport — produces an AFL
    /// payload normally, or a Training payload while training mode is
    /// active. Must run on the UI thread.
    /// </summary>
    private LiveStateSnapshot BuildLiveStateSnapshot()
    {
        if (_trainingSession is not null)
        {
            return BuildTrainingLiveStateSnapshot(_trainingSession);
        }

        var dc = _match.DisplayClock;
        bool q4Ended = _match.Quarter == 4 && _match.GetQuarterSnapshot(4) != null;

        var quarterSnapshots = new List<QuarterSnapshotDto>(4);
        for (int q = 1; q <= 4; q++)
        {
            QuarterSnapshot? snap = _match.GetQuarterSnapshot(q);
            if (snap is null) continue;

            quarterSnapshots.Add(new QuarterSnapshotDto
            {
                Q  = q,
                Hg = snap.HomeGoals,
                Hb = snap.HomeBehinds,
                Ht = snap.HomeTotal,
                Ag = snap.AwayGoals,
                Ab = snap.AwayBehinds,
                At = snap.AwayTotal,
            });
        }

        string[] messages = new string[_styledMessages.Count];
        for (int i = 0; i < _styledMessages.Count; i++)
            messages[i] = _styledMessages[i].Text ?? "";

        return new LiveStateSnapshot
        {
            Sport = "afl",
            HomeName = _match.HomeName,
            HomeAbbr = _match.HomeAbbr,
            AwayName = _match.AwayName,
            AwayAbbr = _match.AwayAbbr,
            HomeGoals = _match.HomeGoals,
            HomeBehinds = _match.HomeBehinds,
            HomeTotal = _match.HomeTotal,
            AwayGoals = _match.AwayGoals,
            AwayBehinds = _match.AwayBehinds,
            AwayTotal = _match.AwayTotal,
            Margin = _match.Margin,
            Quarter = _match.Quarter,
            Clock = $"{(int)dc.TotalMinutes:D2}:{dc.Seconds:D2}",
            ClockRunning = _match.ClockRunning,
            ClockMode = _match.ClockMode.ToString(),
            QuarterMinutes = (int)_match.QuarterDuration.TotalMinutes,
            QuarterSeconds = _match.QuarterDuration.Seconds,
            Q4Ended = q4Ended,
            HomePrimaryColor = HomeColorBox?.Text ?? "#000000",
            HomeSecondaryColor = HomeSecondaryColorBox?.Text ?? "#FFFFFF",
            AwayPrimaryColor = AwayColorBox?.Text ?? "#FFFFFF",
            AwaySecondaryColor = AwaySecondaryColorBox?.Text ?? "#000000",
            ActiveScreen = _activeScreenKey,
            EventCount = _match.Events.Count,
            LastEvent = _match.Events.Count > 0
                ? _match.Events[^1].FormatLog(_match.HomeName, _match.AwayName)
                : "",
            MatchResult = MatchResultText?.Text ?? "",
            FinalsMode = _finalsMode,
            WeatherLocation = _weatherLocation ?? "",
            Messages = messages,
            QuarterSnapshots = quarterSnapshots,
            SessionStatus = q4Ended ? "fullTime" : (_match.ClockRunning ? "live" : "lobby"),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Hook for the Settings → Cloud Remote card UI partial to refresh its
    /// rendered state whenever the session lifecycle changes. The card
    /// visuals live in <c>MainWindow.CloudRemoteUi.cs</c>; this is a
    /// partial method so the refresh is a no-op when that partial is
    /// missing (e.g. unit tests, headless builds).
    /// </summary>
    partial void RefreshCloudRemoteUi();

    /// <summary>
    /// Hook for the UI partial to react to realtime connection changes
    /// (WebSocket opened / closed). Used by the card to flip its
    /// "Connected" badge.
    /// </summary>
    partial void OnCloudConnectedChanged(bool connected);

    // ── IRemoteCommandTarget — every cloud event presses an existing local
    //    handler so animations / validation / snapshots keep working. ──

    void IRemoteCommandTarget.HomeGoal()    => HomeGoal_Click(this, new RoutedEventArgs());
    void IRemoteCommandTarget.HomeBehind()  => HomeBehind_Click(this, new RoutedEventArgs());
    void IRemoteCommandTarget.AwayGoal()    => AwayGoal_Click(this, new RoutedEventArgs());
    void IRemoteCommandTarget.AwayBehind()  => AwayBehind_Click(this, new RoutedEventArgs());
    void IRemoteCommandTarget.Undo()        => Undo_Click(this, new RoutedEventArgs());

    void IRemoteCommandTarget.ToggleClock() => StartPause_Click(this, new RoutedEventArgs());

    void IRemoteCommandTarget.SetClockMode(string mode, int minutes, int seconds)
    {
        seconds = Math.Clamp(seconds, 0, 59);
        if (string.Equals(mode, "Countdown", StringComparison.OrdinalIgnoreCase))
        {
            if (ClockModeCountdown is not null) ClockModeCountdown.IsChecked = true;
            _match.SetClockMode(ClockMode.Countdown);
            _match.SetQuarterDuration(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));
            if (QuarterMinutesBox is not null) QuarterMinutesBox.Text = minutes.ToString();
            if (QuarterSecondsBox is not null) QuarterSecondsBox.Text = seconds.ToString("D2");
        }
        else
        {
            if (ClockModeCountUp is not null) ClockModeCountUp.IsChecked = true;
            _match.SetClockMode(ClockMode.CountUp);
        }
        PushAllToScoreboard();
    }

    void IRemoteCommandTarget.SetElapsedClock(int minutes, int seconds)
    {
        seconds = Math.Clamp(seconds, 0, 59);
        TimeSpan target = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        if (_match.ClockMode == ClockMode.Countdown)
            target = _match.QuarterDuration - target;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        _match.SetElapsed(target);
        UpdateLiveUI();
    }

    void IRemoteCommandTarget.Siren()
    {
        // Mirrors a manual siren overlay trigger. The existing overlay
        // scheduler fires the siren automatically when the quarter clock
        // expires; a remote-triggered siren overlay is queued for v1.1.
    }

    void IRemoteCommandTarget.EndQuarter()      => EndQuarter_Click(this, new RoutedEventArgs());
    void IRemoteCommandTarget.ContinueQuarter() => ContinueQuarter_Click(this, new RoutedEventArgs());

    void IRemoteCommandTarget.ResetMatch()      => ResetMatch_Click(this, new RoutedEventArgs());

    void IRemoteCommandTarget.SetTeams(string homeName, string homeAbbr, string awayName, string awayAbbr)
    {
        _match.SetTeams(homeName, homeAbbr, awayName, awayAbbr);
        SyncControlPanelFields();
        ApplyTeamColorsToScoringPanels();
        PushAllToScoreboard();
    }

    void IRemoteCommandTarget.SetColors(string homePrimary, string homeSecondary, string awayPrimary, string awaySecondary)
    {
        if (HomeColorBox is not null) HomeColorBox.Text = homePrimary;
        if (HomeSecondaryColorBox is not null) HomeSecondaryColorBox.Text = homeSecondary;
        if (AwayColorBox is not null) AwayColorBox.Text = awayPrimary;
        if (AwaySecondaryColorBox is not null) AwaySecondaryColorBox.Text = awaySecondary;

        TrySetPreview(HomeColorPreview, homePrimary);
        TrySetPreview(HomeSecondaryColorPreview, homeSecondary);
        TrySetPreview(AwayColorPreview, awayPrimary);
        TrySetPreview(AwaySecondaryColorPreview, awaySecondary);

        ApplyTeamColorsToScoringPanels();
        PushAllToScoreboard();
    }

    void IRemoteCommandTarget.SetFinalsMode(bool enabled)
    {
        _finalsMode = enabled;
        if (SettingsFinalsToggle is not null) SettingsFinalsToggle.IsChecked = enabled;
        UpdateSettingsFinalsCardVisuals();
        PushAllToScoreboard();
    }

    void IRemoteCommandTarget.SetWeatherLocation(string location)
    {
        _weatherLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (SettingsWeatherLocation is not null) SettingsWeatherLocation.Text = _weatherLocation ?? "";
        _scorebug?.SetWeatherLocation(_weatherLocation);
        _ = StartOrUpdateWeatherServiceAsync(_weatherLocation);
    }

    void IRemoteCommandTarget.SwitchScreen(string screen)
    {
        switch (screen)
        {
            case "scorebug": SwitchToScorebug_Click(this, new RoutedEventArgs()); break;
            case "summary":  SwitchToBreakScreen_Click(this, new RoutedEventArgs()); break;
            case "worm":     SwitchToScoreworm_Click(this, new RoutedEventArgs()); break;
            case "stats":    SwitchToStatsScreen_Click(this, new RoutedEventArgs()); break;
            case "weather":  SwitchToWeatherScreen_Click(this, new RoutedEventArgs()); break;
            case "video":    SwitchToVideo_Click(this, new RoutedEventArgs()); break;
        }
    }

    void IRemoteCommandTarget.ShowScoreboardWindow() => ShowScoreboard_Click(this, new RoutedEventArgs());

    void IRemoteCommandTarget.AddMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var styled = new Models.MarqueeMessage(text);
        _styledMessages.Add(styled);
        _messages.Add(text);
        if (MessageList.ItemsSource is null)
            MessageList.ItemsSource = _styledMessages;

        _scorebug?.SetMarqueeMessages((IList<Models.MarqueeMessage>)_styledMessages);
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.RemoveMessage(int index)
    {
        if (index < 0 || index >= _styledMessages.Count) return;
        _styledMessages.RemoveAt(index);
        if (index < _messages.Count) _messages.RemoveAt(index);
        _scorebug?.SetMarqueeMessages((IList<Models.MarqueeMessage>)_styledMessages);
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.SetMessages(string[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _styledMessages.Clear();
        _messages.Clear();
        foreach (string text in messages)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            _styledMessages.Add(new Models.MarqueeMessage(text));
            _messages.Add(text);
        }
        if (MessageList.ItemsSource is null)
            MessageList.ItemsSource = _styledMessages;
        _scorebug?.SetMarqueeMessages((IList<Models.MarqueeMessage>)_styledMessages);
        PublishCloudSnapshot();
    }

    void IRemoteCommandTarget.SetLayout(string layout)
    {
        switch (layout)
        {
            case "classic":
                if (LayoutClassicRadio is not null) LayoutClassicRadio.IsChecked = true;
                break;
            // Future: compact / wide / etc.
        }
        UpdateLayoutCards();
        PushAllToScoreboard();
    }

    void IRemoteCommandTarget.SetGoalAnimation(string style)
    {
        switch (style)
        {
            case "classic":     if (AnimClassicRadio is not null)     AnimClassicRadio.IsChecked = true;     break;
            case "broadcast":   if (AnimBroadcastRadio is not null)   AnimBroadcastRadio.IsChecked = true;   break;
            case "customVideo": if (AnimCustomVideoRadio is not null) AnimCustomVideoRadio.IsChecked = true; break;
        }
        UpdateAnimCards();
    }
}
