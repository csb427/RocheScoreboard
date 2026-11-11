// Generated from MainWindow.CloudRemoteUi.cs; edit translator/adapters.
// Source SHA-256: dc081dc76e33546ddd2f3a271dc60d67c28ac33bd6735094d183acd3e69be23a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
// ─────────────────────────────────────────────────────────────────────────────
// MainWindow.CloudRemoteUi.cs
//
// Drives the Settings → Cloud Remote card. The cloud transport itself lives
// in MainWindow.CloudRemote.cs; this file only owns the card's visual state
// machine and click handlers so the XAML stays clean.
//
// It also owns a list of compact <see cref="Views.CloudRemoteLauncher"/>
// widgets that are embedded on other tabs (AFL Scoring & Clock, Training
// Mode). Each registered launcher receives the same state + connection
// updates, and routes its Start / End / Copy clicks back through the same
// methods the Settings card uses.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private static readonly SolidColorBrush CloudBadgeOnline =
        new(Avalonia.Media.Color.FromRgb(0x3F, 0xB9, 0x50)); // GitHub-green like other "live" badges in the app.

    private static readonly SolidColorBrush CloudBadgeOffline =
        new(Avalonia.Media.Color.FromRgb(0x6E, 0x76, 0x81));

    private static readonly SolidColorBrush CloudBadgeWarning =
        new(Avalonia.Media.Color.FromRgb(0xD2, 0x99, 0x22));

    private readonly List<CloudRemoteLauncher> _cloudLaunchers = new();
    private bool _cloudStartPending, _cloudEndPending;
    private bool _asyncBusyMotionWatching;

    /// <summary>
    /// Registers a compact cloud-remote launcher placed on another tab so
    /// it stays in sync with the Settings card. Safe to call multiple
    /// times with the same instance.
    /// </summary>
    internal void RegisterCloudRemoteLauncher(CloudRemoteLauncher? launcher)
    {
        if (launcher is null) return;
        if (_cloudLaunchers.Contains(launcher)) return;

        launcher.StartRequested        += async (_, _) => await LauncherStartRequestedAsync(launcher).ConfigureAwait(true);
        launcher.EndRequested          += async (_, args) => await LauncherEndRequestedAsync(launcher, (args as CloudRemoteLauncher.EndRequestEventArgs)?.HostGeneration).ConfigureAwait(true);
        launcher.CopyJoinCodeRequested += (_, args) =>
        {
            if (args is CloudRemoteLauncher.CopyRequestEventArgs request)
                request.Succeeded = TryCopyLauncherValue(request.Value);
        };
        launcher.CopyJoinUrlRequested += (_, args) =>
        {
            if (args is CloudRemoteLauncher.CopyRequestEventArgs request)
                request.Succeeded = TryCopyLauncherValue(request.Value);
        };

        _cloudLaunchers.Add(launcher);

        // Push current state immediately so the embedded widget shows the
        // right idle/disabled/live visuals as soon as the host loads it.
        PushStateToLauncher(launcher);
    }

    private async System.Threading.Tasks.Task LauncherStartRequestedAsync(CloudRemoteLauncher launcher)
    {
        try
        {
            await StartCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            launcher.SetStartPending(false);
            RefreshCloudRemoteUi();
        }
    }

    private async System.Threading.Tasks.Task LauncherEndRequestedAsync(CloudRemoteLauncher launcher, long? expectedGeneration = null)
    {
        if (!IsCloudSessionActive || expectedGeneration is long expected && expected != _cloudSessionGeneration)
        { RefreshCloudRemoteUi(); return; }
        var scope = CloudShareScope;
        NativeMessageBoxResult ok = RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
            "End the live cloud session?\n\nAll remote phones will be disconnected.",
            "Cloud Remote",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Question, NativeMessageBoxResult.No);
        if (ok != NativeMessageBoxResult.Yes || !IsCloudSessionActive || scope != CloudShareScope)
        {
            RefreshCloudRemoteUi();
            return;
        }

        _cloudEndPending = true;
        launcher.Expand();
        launcher.SetEndPending(true);
        try
        {
            await EndCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            _cloudEndPending = false;
            launcher.SetEndPending(false);
            RefreshCloudRemoteUi();
        }
    }

    private static bool TryCopyLauncherValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { NativeClipboard.SetText(value); return true; }
        catch (System.Runtime.InteropServices.ExternalException) { return false; }
    }

    private void PushStateToLauncher(CloudRemoteLauncher launcher)
    {
        launcher.ApplyState(IsCloudConfigReady, IsCloudSessionActive, CloudJoinCode, CloudJoinUrl, _cloudSessionGeneration);
        launcher.ApplyConnection(CloudUiConnected && IsCloudSessionActive, IsCloudSessionActive);
    }

    /// <summary>
    /// Operator clicked "Start cloud session" in the Settings card.
    /// </summary>
    private async void StartCloudSession_Click(object? sender, RoutedEventArgs e)
    {
        if (_cloudStartPending || IsCloudSessionActive || _cloudEndPending) return;
        _cloudStartPending = true;
        SetCloudButtonBusy(CloudStartButton, CloudStartButtonLabel, CloudStartBusyProgress, CloudStartBusyStatic,
            true, "Start cloud session", "Creating cloud session", "Creating session…", "Creating session…");
        if (CloudStatusText is not null)
        {
            CloudStatusText.Text = "Creating session…";
            CloudStatusText.Foreground = CloudBadgeWarning;
        }

        try
        {
            await StartCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            // RefreshCloudRemoteUi handles the post-success / post-failure
            // visuals — re-running it here covers the case where Start
            // returned without raising CloudSessionStarted (e.g. config
            // missing, MessageBox shown).
            _cloudStartPending = false;
            SetCloudButtonBusy(CloudStartButton, CloudStartButtonLabel, CloudStartBusyProgress, CloudStartBusyStatic,
                false, "Start cloud session", "Creating cloud session", "Start cloud session", "");
            RefreshCloudRemoteUi();
        }
    }

    /// <summary>
    /// Operator clicked "End session". Confirms first because it
    /// disconnects every remote phone.
    /// </summary>
    private async void EndCloudSession_Click(object? sender, RoutedEventArgs e)
    {
        if (_cloudEndPending || _cloudStartPending || !IsCloudSessionActive) return;
        var scope = CloudShareScope;
        NativeMessageBoxResult ok = RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
            "End the live cloud session?\n\nAll remote phones will be disconnected.",
            "Cloud Remote",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Question, NativeMessageBoxResult.No);
        if (ok != NativeMessageBoxResult.Yes || !IsCloudSessionActive || scope != CloudShareScope) return;

        _cloudEndPending = true;
        SetCloudButtonBusy(CloudEndButton, CloudEndButtonLabel, CloudEndBusyProgress, CloudEndBusyStatic,
            true, "End cloud session", "Ending cloud session", "End session…", "Ending session…");
        if (CloudStatusText is not null)
        {
            CloudStatusText.Text = "Ending cloud session…";
            CloudStatusText.Foreground = CloudBadgeWarning;
        }
        try
        {
            await EndCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            _cloudEndPending = false;
            SetCloudButtonBusy(CloudEndButton, CloudEndButtonLabel, CloudEndBusyProgress, CloudEndBusyStatic,
                false, "End cloud session", "Ending cloud session", "End session…", "");
            RefreshCloudRemoteUi();
        }
    }

    private void SetCloudButtonBusy(Avalonia.Controls.Button? button,
        Avalonia.Controls.TextBlock? buttonLabel, Avalonia.Controls.ProgressBar? progress,
        Avalonia.Controls.TextBlock? reducedMotionStatus, bool busy, string name, string busyName,
        string restingLabel, string status)
    {
        if (button is null) return;
        OperatorControlMotion.SetIsBusy(button, busy);
        button.IsEnabled = !busy;
        if (buttonLabel is not null) buttonLabel.Text = busy ? status : restingLabel;
        Avalonia.Automation.AutomationProperties.SetName(button, busy ? busyName : name);
        Avalonia.Automation.AutomationProperties.SetItemStatus(button, busy ? status : "Ready");
        if (progress is not null) progress.IsVisible = busy && OperatorMotion.Enabled ? true : false;
        if (reducedMotionStatus is not null)
        {
            reducedMotionStatus.Text = "";
            reducedMotionStatus.IsVisible = false;
        }
        UpdateAsyncBusyMotionWatch();
    }

    private void UpdateAsyncBusyMotionWatch()
    {
        bool pending = _cloudStartPending || _cloudEndPending || _updateCheckInFlight;
        if (pending && !_asyncBusyMotionWatching)
        {
            OperatorAppearance.Changed += AsyncBusyMotionPreferenceChanged;
            _asyncBusyMotionWatching = true;
        }
        else if (!pending && _asyncBusyMotionWatching)
        {
            OperatorAppearance.Changed -= AsyncBusyMotionPreferenceChanged;
            _asyncBusyMotionWatching = false;
        }
        AsyncBusyMotionPreferenceChanged();
    }

    private void AsyncBusyMotionPreferenceChanged()
    {
        if (CloudStartBusyProgress is not null)
            CloudStartBusyProgress.IsVisible = _cloudStartPending && OperatorMotion.Enabled ? true : false;
        if (CloudEndBusyProgress is not null)
            CloudEndBusyProgress.IsVisible = _cloudEndPending && OperatorMotion.Enabled ? true : false;
        if (CheckForUpdateBusyProgress is not null)
            CheckForUpdateBusyProgress.IsVisible = _updateCheckInFlight && OperatorMotion.Enabled ? true : false;
    }

    // Per-window clipboard seam: production keeps the existing Windows API;
    // controlled UI tests need no desktop clipboard or external service.
    internal Func<string, bool> CloudShareClipboardWriter { get; set; } = TryCopyLauncherValue;
    private (long Generation, Guid? Session, string? Code, string? Url)? _cloudShareReceipt;
    private (long Generation, bool Connected)? _cloudUiConnection;
    private bool CloudUiConnected => _cloudUiConnection is { } value && value.Generation == _cloudSessionGeneration && value.Connected;
    private (long Generation, Guid? Session, string? Code, string? Url) CloudShareScope =>
        (_cloudSessionGeneration, CloudSessionId, CloudJoinCode, CloudJoinUrl);

    private void CopyCloudJoinCode_Click(object? sender, RoutedEventArgs e) => CopyCloudShare(false);
    private void CopyCloudJoinUrl_Click(object? sender, RoutedEventArgs e) => CopyCloudShare(true);
    private void CopyCloudShare(bool link)
    {
        var scope = CloudShareScope;
        string? value = link ? scope.Url : scope.Code;
        if (!IsCloudSessionActive || string.IsNullOrWhiteSpace(value)) return;
        bool copied = CloudShareClipboardWriter(value);
        // Clipboard operations can pump messages. Never acknowledge an old
        // session after End/replacement while copying. Share values never move.
        if (!IsCloudSessionActive || scope != CloudShareScope) return;
        _cloudShareReceipt = scope;
        if (CloudCopyFeedback is not null)
            CloudCopyFeedback.Text = copied ? (link ? "Join link copied." : "Join code copied.")
                : "Clipboard unavailable. Select Copy again, or use the QR code.";
    }

    /// <summary>
    /// Implements the partial declared in <c>MainWindow.CloudRemote.cs</c>
    /// — switches the card between idle / live / disabled visuals.
    /// </summary>
    partial void RefreshCloudRemoteUi()
    {
        // Always sync the launchers, even before the Settings XAML is
        // ready, so that a tab embed loaded earlier doesn't show stale UI.
        foreach (CloudRemoteLauncher l in _cloudLaunchers)
        {
            PushStateToLauncher(l);
        }

        if (CloudRemoteCard is null) return; // XAML not loaded yet.

        bool configReady = IsCloudConfigReady;
        bool active      = IsCloudSessionActive;
        bool showingSession = active || _cloudEndPending;
        string? code     = CloudJoinCode;
        string? url      = CloudJoinUrl;

        if (!active || _cloudShareReceipt != CloudShareScope)
        {
            _cloudShareReceipt = null;
            if (CloudCopyFeedback is not null) CloudCopyFeedback.Text = "";
        }
        foreach (var button in new[] { CloudStartButton, CloudCopyCodeButton, CloudCopyUrlButton, CloudEndButton })
            if (button is not null) { OperatorFeedback.SetManaged(button, true); OperatorControlMotion.SetEnabled(button, false); }
        if (CloudCopyCodeButton is not null) CloudCopyCodeButton.IsEnabled = active && !string.IsNullOrWhiteSpace(code);
        if (CloudConnectionDot is not null) CloudConnectionDot.Fill = CloudUiConnected ? CloudBadgeOnline : CloudBadgeOffline;
        if (CloudConnectionLabel is not null)
        {
            CloudConnectionLabel.Text = CloudUiConnected ? "Cloud connected" : "Waiting for cloud connection";
            CloudConnectionLabel.Foreground = CloudUiConnected ? CloudBadgeOnline : CloudBadgeOffline;
        }

        // Show/hide the three states.
        if (CloudIdleState     is not null) CloudIdleState.IsVisible     = (!showingSession && configReady) ? true : false;
        if (CloudLiveState     is not null) CloudLiveState.IsVisible     = showingSession                ? true : false;
        if (CloudDisabledState is not null) CloudDisabledState.IsVisible = (!showingSession && !configReady) ? true : false;

        // Idle state: re-enable the start button once a previous attempt
        // returned (success or failure both end up here).
        if (CloudStartButton is not null) CloudStartButton.IsEnabled = configReady && !_cloudStartPending && !_cloudEndPending;
        if (CloudEndButton is not null) CloudEndButton.IsEnabled = active && !_cloudEndPending;

        if (CloudStatusText is not null)
        {
            if (_cloudEndPending)
            {
                CloudStatusText.Text = "Ending cloud session…";
                CloudStatusText.Foreground = CloudBadgeWarning;
            }
            else if (active)
            {
                CloudStatusText.Text = CloudUiConnected ? "Session open · cloud connected" : "Session open · waiting for cloud connection…";
                CloudStatusText.Foreground = CloudUiConnected ? CloudBadgeOnline : CloudBadgeWarning;
            }
            else if (configReady)
            {
                CloudStatusText.Text = "Ready to invite a scorer.";
                CloudStatusText.Foreground = CloudBadgeOffline;
            }
            else
            {
                CloudStatusText.Text = "supabase.config.json missing or incomplete.";
                CloudStatusText.Foreground = CloudBadgeWarning;
            }
        }

        if (active)
        {
            if (CloudJoinCodeText is not null) CloudJoinCodeText.Text = code ?? "";
            if (CloudJoinUrlText  is not null) CloudJoinUrlText.Text  = url ?? "(set remoteAppBaseUrl in supabase.config.json)";
            if (CloudJoinUrlPanel is not null) CloudJoinUrlPanel.IsVisible = string.IsNullOrWhiteSpace(url) ? false : true;
            if (CloudCopyUrlButton is not null) CloudCopyUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(url);

            if (CloudQrImage is not null)
            {
                try
                {
                    string qrPayload = url ?? code ?? "";
                    NativeBitmap bmp = GenerateQrCode(qrPayload);
                    CloudQrImage.Source = bmp;
                    CloudQrImage.IsVisible = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudRemote] QR build failed: {ex.Message}");
                    CloudQrImage.Source = null;
                    CloudQrImage.IsVisible = false;
                }
            }
        }
        else if (!showingSession)
        {
            if (CloudQrImage is not null) CloudQrImage.Source = null;
        }
    }

    /// <summary>
    /// Implements the partial declared in <c>MainWindow.CloudRemote.cs</c>.
    /// Flips the small "connected" pip on the live card and every embedded
    /// launcher.
    /// </summary>
    partial void OnCloudConnectedChanged(bool connected)
    {
        _cloudUiConnection = (_cloudSessionGeneration, connected);
        foreach (CloudRemoteLauncher l in _cloudLaunchers)
        {
            l.ApplyConnection(connected, IsCloudSessionActive);
        }

        if (CloudConnectionDot is not null)
        {
            CloudConnectionDot.Fill = connected ? CloudBadgeOnline : CloudBadgeOffline;
        }
        if (CloudConnectionLabel is not null)
        {
            CloudConnectionLabel.Text = connected ? "Cloud connected" : "Waiting for cloud connection";
            CloudConnectionLabel.Foreground = connected ? CloudBadgeOnline : CloudBadgeOffline;
        }
        if (CloudStatusText is not null && IsCloudSessionActive)
        {
            CloudStatusText.Text = connected ? "Session open · cloud connected" : "Session open · reconnecting to cloud…";
            CloudStatusText.Foreground = connected ? CloudBadgeOnline : CloudBadgeWarning;
        }
    }
}
