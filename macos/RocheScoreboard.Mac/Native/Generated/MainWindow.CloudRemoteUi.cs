// Generated from MainWindow.CloudRemoteUi.cs; edit translator/adapters.
// Source SHA-256: 10137b5203f027924f3273e342f4376c1934eb4e149b21b69090bc1b29d9b102
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
        launcher.EndRequested          += async (_, _) => await LauncherEndRequestedAsync(launcher).ConfigureAwait(true);
        launcher.CopyJoinCodeRequested += (_, _) => LauncherCopyJoinCode();
        launcher.CopyJoinUrlRequested  += (_, _) => LauncherCopyJoinUrl();

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
            if (IsCloudSessionActive) launcher.Expand();
        }
        finally
        {
            RefreshCloudRemoteUi();
        }
    }

    private async System.Threading.Tasks.Task LauncherEndRequestedAsync(CloudRemoteLauncher launcher)
    {
        NativeMessageBoxResult ok = RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
            "End the live cloud session?\n\nAll remote phones will be disconnected.",
            "Cloud Remote",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Question);
        if (ok != NativeMessageBoxResult.Yes)
        {
            RefreshCloudRemoteUi();
            return;
        }

        try
        {
            await EndCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            RefreshCloudRemoteUi();
        }
    }

    private void LauncherCopyJoinCode()
    {
        if (string.IsNullOrWhiteSpace(CloudJoinCode)) return;
        try { NativeClipboard.SetText(CloudJoinCode!); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ }
    }

    private void LauncherCopyJoinUrl()
    {
        string? url = CloudJoinUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try { NativeClipboard.SetText(url!); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ }
    }

    private void PushStateToLauncher(CloudRemoteLauncher launcher)
    {
        launcher.ApplyState(IsCloudConfigReady, IsCloudSessionActive, CloudJoinCode, CloudJoinUrl);
        launcher.ApplyConnection(IsCloudSessionActive && (_cloudRemote?.IsActive ?? false), IsCloudSessionActive);
    }

    /// <summary>
    /// Operator clicked "Start cloud session" in the Settings card.
    /// </summary>
    private async void StartCloudSession_Click(object? sender, RoutedEventArgs e)
    {
        if (CloudStartButton is not null) CloudStartButton.IsEnabled = false;
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
            RefreshCloudRemoteUi();
        }
    }

    /// <summary>
    /// Operator clicked "End session". Confirms first because it
    /// disconnects every remote phone.
    /// </summary>
    private async void EndCloudSession_Click(object? sender, RoutedEventArgs e)
    {
        NativeMessageBoxResult ok = RocheScoreboard.Mac.Native.NativeMessageBox.Show(this,
            "End the live cloud session?\n\nAll remote phones will be disconnected.",
            "Cloud Remote",
            NativeMessageBoxButton.YesNo,
            NativeMessageBoxImage.Question);
        if (ok != NativeMessageBoxResult.Yes) return;

        if (CloudEndButton is not null) CloudEndButton.IsEnabled = false;
        try
        {
            await EndCloudSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            RefreshCloudRemoteUi();
        }
    }

    /// <summary>Copies the join code to the clipboard with the standard Copied! flash.</summary>
    private void CopyCloudJoinCode_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CloudJoinCode) || CloudJoinCodeText is null) return;
        TrySetClipboardWithFeedback(CloudJoinCode!, CloudJoinCodeText);
    }

    /// <summary>Copies the full join URL to the clipboard.</summary>
    private void CopyCloudJoinUrl_Click(object? sender, RoutedEventArgs e)
    {
        string? url = CloudJoinUrl;
        if (string.IsNullOrWhiteSpace(url) || CloudJoinUrlText is null) return;
        TrySetClipboardWithFeedback(url!, CloudJoinUrlText);
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
        string? code     = CloudJoinCode;
        string? url      = CloudJoinUrl;

        // Show/hide the three states.
        if (CloudIdleState     is not null) CloudIdleState.IsVisible     = (!active && configReady) ? true : false;
        if (CloudLiveState     is not null) CloudLiveState.IsVisible     = active                    ? true : false;
        if (CloudDisabledState is not null) CloudDisabledState.IsVisible = (!active && !configReady) ? true : false;

        // Idle state: re-enable the start button once a previous attempt
        // returned (success or failure both end up here).
        if (CloudStartButton is not null) CloudStartButton.IsEnabled = configReady;
        if (CloudEndButton is not null) CloudEndButton.IsEnabled = active;

        if (CloudStatusText is not null)
        {
            if (active)
            {
                CloudStatusText.Text = "Session live · waiting for connection…";
                CloudStatusText.Foreground = CloudBadgeWarning;
            }
            else if (configReady)
            {
                CloudStatusText.Text = "Ready. Click Start to open a session.";
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
        else
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
            CloudConnectionLabel.Text = connected ? "Connected" : "Connecting…";
            CloudConnectionLabel.Foreground = connected ? CloudBadgeOnline : CloudBadgeOffline;
        }
        if (CloudStatusText is not null && IsCloudSessionActive)
        {
            CloudStatusText.Text = connected ? "Session live · cloud connected" : "Session live · reconnecting…";
            CloudStatusText.Foreground = connected ? CloudBadgeOnline : CloudBadgeWarning;
        }
    }
}
