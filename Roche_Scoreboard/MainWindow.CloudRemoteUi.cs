// ─────────────────────────────────────────────────────────────────────────────
// MainWindow.CloudRemoteUi.cs
//
// Drives the Settings → Cloud Remote card. The cloud transport itself lives
// in MainWindow.CloudRemote.cs; this file only owns the card's visual state
// machine and click handlers so the XAML stays clean.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private static readonly SolidColorBrush CloudBadgeOnline =
        new(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50)); // GitHub-green like other "live" badges in the app.

    private static readonly SolidColorBrush CloudBadgeOffline =
        new(System.Windows.Media.Color.FromRgb(0x6E, 0x76, 0x81));

    private static readonly SolidColorBrush CloudBadgeWarning =
        new(System.Windows.Media.Color.FromRgb(0xD2, 0x99, 0x22));

    /// <summary>
    /// Operator clicked "Start cloud session" in the Settings card.
    /// </summary>
    private async void StartCloudSession_Click(object sender, RoutedEventArgs e)
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
    private async void EndCloudSession_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult ok = System.Windows.MessageBox.Show(this,
            "End the live cloud session?\n\nAll remote phones will be disconnected.",
            "Cloud Remote",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

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
    private void CopyCloudJoinCode_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CloudJoinCode) || CloudJoinCodeText is null) return;
        TrySetClipboardWithFeedback(CloudJoinCode!, CloudJoinCodeText);
    }

    /// <summary>Copies the full join URL to the clipboard.</summary>
    private void CopyCloudJoinUrl_Click(object sender, RoutedEventArgs e)
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
        if (CloudRemoteCard is null) return; // XAML not loaded yet.

        bool configReady = IsCloudConfigReady;
        bool active      = IsCloudSessionActive;
        string? code     = CloudJoinCode;
        string? url      = CloudJoinUrl;

        // Show/hide the three states.
        if (CloudIdleState     is not null) CloudIdleState.Visibility     = (!active && configReady) ? Visibility.Visible : Visibility.Collapsed;
        if (CloudLiveState     is not null) CloudLiveState.Visibility     = active                    ? Visibility.Visible : Visibility.Collapsed;
        if (CloudDisabledState is not null) CloudDisabledState.Visibility = (!active && !configReady) ? Visibility.Visible : Visibility.Collapsed;

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
            if (CloudJoinUrlPanel is not null) CloudJoinUrlPanel.Visibility = string.IsNullOrWhiteSpace(url) ? Visibility.Collapsed : Visibility.Visible;
            if (CloudCopyUrlButton is not null) CloudCopyUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(url);

            if (CloudQrImage is not null)
            {
                try
                {
                    string qrPayload = url ?? code ?? "";
                    BitmapSource bmp = GenerateQrCode(qrPayload);
                    CloudQrImage.Source = bmp;
                    CloudQrImage.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudRemote] QR build failed: {ex.Message}");
                    CloudQrImage.Source = null;
                    CloudQrImage.Visibility = Visibility.Collapsed;
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
    /// Flips the small "connected" pip on the live card.
    /// </summary>
    partial void OnCloudConnectedChanged(bool connected)
    {
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
