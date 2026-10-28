using RocheScoreboard.Mac.Production;
// Generated from Views/CloudRemoteLauncher.xaml.cs; edit translator/adapters.
// Source SHA-256: e5248f647a6ab3c74427cc71cf5d4d80c07fd86a5808e559cf37ad8c2c8d8b4c
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
// ─────────────────────────────────────────────────────────────────────────────
// CloudRemoteLauncher.xaml.cs
//
// Reusable compact widget for surfacing the cloud remote (start session +
// QR + join code) on tabs other than the Settings → Cloud Remote card.
// It owns no transport — the host (MainWindow) wires the launcher to the
// existing cloud session lifecycle by handling StartRequested /
// EndRequested and calling ApplyState/ApplyConnection to refresh visuals.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Avalonia.Media.Imaging;
using Brush = Avalonia.Media.IBrush;
using Color = Avalonia.Media.Color;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CloudRemoteLauncher : UserControl
    {
        private static readonly SolidColorBrush BadgeOnline =
            new(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly SolidColorBrush BadgeOffline =
            new(Color.FromRgb(0x6E, 0x76, 0x81));
        private static readonly SolidColorBrush BadgeWarning =
            new(Color.FromRgb(0xD2, 0x99, 0x22));

        /// <summary>Raised when the operator taps "Start cloud session".</summary>
        public event EventHandler? StartRequested;

        /// <summary>Raised when the operator taps "End session".</summary>
        public event EventHandler? EndRequested;

        /// <summary>Raised when the operator taps the join code (clipboard copy).</summary>
        public event EventHandler? CopyJoinCodeRequested;

        /// <summary>Raised when the operator taps "Copy URL".</summary>
        public event EventHandler? CopyJoinUrlRequested;

        public CloudRemoteLauncher()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        }

        /// <summary>True when the expanded controls are shown.</summary>
        public bool IsExpanded { get; private set; }

        /// <summary>Refreshes the launcher visuals from a single state snapshot.</summary>
        public void ApplyState(bool configReady, bool active, string? joinCode, string? joinUrl)
        {
            IdleState.IsVisible     = (!active && configReady) ? true : false;
            LiveState.IsVisible     = active                    ? true : false;
            DisabledState.IsVisible = (!active && !configReady) ? true : false;

            StartButton.IsEnabled = configReady;
            EndButton.IsEnabled = active;

            if (active)
            {
                StatusText.Text = "Session live · waiting for connection…";
                StatusText.Foreground = BadgeWarning;
                CollapsedStatusText.Text = string.IsNullOrWhiteSpace(joinCode)
                    ? "Session live"
                    : $"Live · code {joinCode}";
            }
            else if (configReady)
            {
                StatusText.Text = "Ready. Tap Start to open a session.";
                StatusText.Foreground = BadgeOffline;
                CollapsedStatusText.Text = "Tap to start";
            }
            else
            {
                StatusText.Text = "supabase.config.json missing or incomplete.";
                StatusText.Foreground = BadgeWarning;
                CollapsedStatusText.Text = "Setup required";
            }

            if (active)
            {
                JoinCodeText.Text = joinCode ?? "—";
                JoinUrlText.Text = joinUrl ?? "(set remoteAppBaseUrl in supabase.config.json)";
                JoinUrlPanel.IsVisible = string.IsNullOrWhiteSpace(joinUrl) ? false : true;
                CopyUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(joinUrl);

                try
                {
                    string qrPayload = joinUrl ?? joinCode ?? "";
                    if (!string.IsNullOrWhiteSpace(qrPayload))
                    {
                        NativeBitmap bmp = NativeQr.GenerateQrCode(qrPayload);
                        QrImage.Source = bmp;
                        QrImage.IsVisible = true;
                    }
                    else
                    {
                        QrImage.Source = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudRemoteLauncher] QR build failed: {ex.Message}");
                    QrImage.Source = null;
                }
            }
            else
            {
                QrImage.Source = null;
            }
        }

        /// <summary>Updates the small "connected" pip on the live state.</summary>
        public void ApplyConnection(bool connected, bool active)
        {
            ConnectionDot.Fill = connected ? BadgeOnline : BadgeOffline;
            ConnectionLabel.Text = connected ? "Connected" : "Connecting…";
            ConnectionLabel.Foreground = connected ? BadgeOnline : BadgeOffline;

            if (active)
            {
                StatusText.Text = connected ? "Session live · cloud connected" : "Session live · reconnecting…";
                StatusText.Foreground = connected ? BadgeOnline : BadgeWarning;
            }
        }

        /// <summary>Force the panel open (e.g. immediately after the operator started a session).</summary>
        public void Expand()
        {
            if (IsExpanded) return;
            IsExpanded = true;
            ExpandedPanel.IsVisible = true;
            CollapsedChevron.Text = "▴";
        }

        /// <summary>Force the panel closed.</summary>
        public void Collapse()
        {
            if (!IsExpanded) return;
            IsExpanded = false;
            ExpandedPanel.IsVisible = false;
            CollapsedChevron.Text = "▾";
        }

        private void CollapsedButton_Click(object? sender, RoutedEventArgs e)
        {
            if (IsExpanded) Collapse(); else Expand();
        }

        private void StartButton_Click(object? sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            StatusText.Text = "Creating session…";
            StatusText.Foreground = BadgeWarning;
            StartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EndButton_Click(object? sender, RoutedEventArgs e)
        {
            EndButton.IsEnabled = false;
            EndRequested?.Invoke(this, EventArgs.Empty);
        }

        private void JoinCodeBox_Click(object? sender, RoutedEventArgs e)
        {
            CopyJoinCodeRequested?.Invoke(this, EventArgs.Empty);
            FlashCopied(JoinCodeText);
        }

        private void CopyUrlButton_Click(object? sender, RoutedEventArgs e)
        {
            CopyJoinUrlRequested?.Invoke(this, EventArgs.Empty);
            FlashCopied(JoinUrlText);
        }

        private static void FlashCopied(TextBlock block)
        {
            string prev = (block.Text ?? string.Empty);
            Brush? prevBrush = block.Foreground;
            block.Text = "✓ Copied!";
            block.Foreground = BadgeOnline;

            var revert = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            revert.Tick += (_, _) =>
            {
                revert.Stop();
                block.Text = prev;
                block.Foreground = prevBrush;
            };
            revert.Start();
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class CloudRemoteLauncher
{
    private void Native_JoinCodeBox_Click(object? sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; JoinCodeBox_Click(sender!, e); }
}
}
