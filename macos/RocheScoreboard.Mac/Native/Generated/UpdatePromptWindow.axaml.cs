using RocheScoreboard.Mac.Production;
// Generated from Views/UpdatePromptWindow.xaml.cs; edit translator/adapters.
// Source SHA-256: 38f6c6b31440ac211e8614ae0e8fd516a2257851caf1e5ad0684631e47c930a0
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Stage-aware update decision and progress surface. Closing is safe before a
/// download and becomes a cancellation request while staging is in progress.
/// </summary>
public partial class UpdatePromptWindow : NativeWindow
{
    private readonly AutoUpdateService _updateService;
    private readonly Func<IProgress<UpdateProgressInfo>?, CancellationToken, Task<bool>> _applyUpdateOperation;
    private bool _isDownloading;
    private bool _cancellationRequested;
    private bool _closed;
    private CancellationTokenSource? _downloadCancellation;

    public bool UpdateApplied { get; private set; }

    public UpdatePromptWindow(AutoUpdateService updateService)
        : this(updateService, updateService?.LatestRelease)
    {
    }

    internal UpdatePromptWindow(AutoUpdateService updateService, GitHubRelease? releasePreview)
        : this(updateService, releasePreview, null)
    {
    }

    internal UpdatePromptWindow(AutoUpdateService updateService, GitHubRelease? releasePreview,
        Func<IProgress<UpdateProgressInfo>?, CancellationToken, Task<bool>>? applyUpdateOperation)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        InteractionMotion.Enable(this);
        _updateService = updateService;
        _applyUpdateOperation = applyUpdateOperation ?? updateService.DownloadAndApplyDetailedAsync;

        CurrentVersionText.Text = ProductionVersion.CurrentVersion.ToString(3);
        GitHubRelease? release = releasePreview ?? updateService.LatestRelease;
        NewVersionText.Text = release?.TagName?.TrimStart('v', 'V') ?? "Unknown";
        ReleaseNotesText.Text = FormatReleaseNotes(release?.Body);

        GitHubReleaseAsset? previewAsset = release?.Assets?.FirstOrDefault(asset =>
            (asset.Name ?? string.Empty)?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true && asset.Size > 0);
        long packageSize = previewAsset?.Size ?? updateService.LatestPackageSizeBytes;
        PackageSizeText.Text = packageSize > 0
            ? $"Mac  ·  {FormatBytes(packageSize)}"
            : "Mac package";
        bool hasDigest = previewAsset?.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ||
                         updateService.LatestPackageHasDigest;
        IntegrityText.Text = hasDigest
            ? "SHA-256 integrity check"
            : "Package validation enabled";

        SharedSurfaceMotion.Attach(DialogShell);
        UpdateClosePurpose();
        Avalonia.Automation.AutomationProperties.SetLiveSetting(ProgressText, Avalonia.Automation.AutomationLiveSetting.Polite);
        Loaded += (_, _) => { if (!_closed && !_isDownloading) SkipButton.Focus(); };
        Closing += (_, args) =>
        {
            if (!_isDownloading || UpdateApplied) return;
            args.Cancel = true;
            RequestDownloadCancellation();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            var cancellation = _downloadCancellation;
            _downloadCancellation = null;
            _isDownloading = false;
            _cancellationRequested = false;
            if (!UpdateApplied) cancellation?.Cancel();
            cancellation?.Dispose();
        };
    }

    private static string FormatReleaseNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "This release includes reliability, usability and presentation improvements.";

        string[] clean = notes.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, @"^#{1,6}\s*", string.Empty))
            .Select(line => Regex.Replace(line, @"^[-*]\s+", "• "))
            .Select(line => Regex.Replace(line, @"\[([^\]]+)\]\([^\)]+\)", "$1"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(32)
            .ToArray();
        return clean.Length == 0 ? "A new Roche Scoreboard release is ready." : string.Join(Environment.NewLine, clean);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024d / 1024d:0} MB";
        return $"{Math.Max(0, bytes) / 1024d:0} KB";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => SkipOrCancel();

    private void Skip_Click(object? sender, RoutedEventArgs e) => SkipOrCancel();

    private void SkipOrCancel()
    {
        if (_isDownloading)
        {
            RequestDownloadCancellation();
            return;
        }
        DialogResult = false;
    }

    private void ViewRelease_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? url = _updateService.LatestRelease?.HtmlUrl;
            if (string.IsNullOrWhiteSpace(url)) AutoUpdateService.OpenReleasePageInBrowser();
            else AutoUpdateService.OpenUrlInBrowser(url);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowOperationMessage("Could not open the release page.", ex.Message, isError: true);
        }
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_closed || _isDownloading || UpdateApplied) return;

        _isDownloading = true;
        _cancellationRequested = false;
        _downloadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        bool Current() => !_closed && ReferenceEquals(_downloadCancellation, cancellation);
        SetBusyState(true);
        ProgressPanel.IsVisible = true;
        UpdateProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        ProgressText.Text = "Preparing update…";
        ProgressText.Foreground = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
        ProgressPercentText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xCB, 0xFF));
        ProgressDetailText.Text = "Your current installation remains untouched until verification completes.";

        ReportStatusChanged();
        var progress = new Progress<UpdateProgressInfo>(info =>
        {
            if (Current() && _isDownloading && !_cancellationRequested) UpdateProgressUi(info);
        });
        try
        {
            if (!Current()) return;
            bool success = await _applyUpdateOperation(progress, cancellation.Token);
            if (!Current()) return;
            if (success)
            {
                UpdateApplied = true;
                DialogResult = true;
                return;
            }

            if (cancellation.IsCancellationRequested)
            {
                ShowOperationMessage("Download cancelled", "Nothing was installed and your current version is unchanged.", isError: false);
                return;
            }
            ShowOperationMessage("Update was not installed",
                string.IsNullOrWhiteSpace(_updateService.LastError)
                    ? "Nothing changed. You can retry or use View release for the portable download."
                    : _updateService.LastError,
                isError: true);
        }
        catch (OperationCanceledException)
        {
            if (!Current()) return;
            ShowOperationMessage("Download cancelled", "Nothing was installed and your current version is unchanged.", isError: false);
        }
        catch (Exception ex)
        {
            if (!Current()) return;
            ShowOperationMessage("Update was not installed",
                string.IsNullOrWhiteSpace(ex.Message) ? "The current installation is unchanged." : ex.Message,
                isError: true);
        }
        finally
        {
            if (ReferenceEquals(_downloadCancellation, cancellation))
            {
                _downloadCancellation = null;
                _isDownloading = false;
                _cancellationRequested = false;
                if (!_closed) SetBusyState(false);
            }
            cancellation.Dispose();
        }
    }

    private void UpdateProgressUi(UpdateProgressInfo info)
    {
        if (_closed) return;
        ProgressText.Text = info.Message;
        if (info.Stage == UpdateProgressStage.Downloading)
        {
            double percent = Math.Clamp(info.Progress * 100, 0, 100);
            UpdateProgressBar.Value = percent;
            ProgressPercentText.Text = $"{percent:0}%";
            ProgressDetailText.Text = info.TotalBytes > 0
                ? $"{FormatBytes(info.BytesReceived)} of {FormatBytes(info.TotalBytes)}" +
                  (info.Attempt > 1 ? $"  ·  attempt {info.Attempt} of {info.MaximumAttempts}" : string.Empty)
                : "Downloading securely from GitHub…";
        }
        else
        {
            UpdateProgressBar.Value = info.Stage == UpdateProgressStage.Preparing ? 4 : 100;
            ProgressPercentText.Text = info.Stage switch
            {
                UpdateProgressStage.Preparing => "PREP",
                UpdateProgressStage.Verifying => "VERIFY",
                UpdateProgressStage.Extracting => "EXTRACT",
                UpdateProgressStage.Staging => "CHECK",
                UpdateProgressStage.Ready => "READY",
                _ => string.Empty
            };
            ProgressDetailText.Text = info.Stage switch
            {
                UpdateProgressStage.Verifying => "Checking size, SHA-256 digest and archive paths.",
                UpdateProgressStage.Extracting => "Building a complete replacement folder outside the running app.",
                UpdateProgressStage.Staging => "Confirming the executable, .NET runtime and libVLC plugin set.",
                UpdateProgressStage.Ready => "A complete rollback copy will be kept until the new version starts.",
                _ => "Your current installation has not been changed."
            };
        }
        ReportStatusChanged();
    }

    private void ShowOperationMessage(string heading, string detail, bool isError)
    {
        if (_closed) return;
        ProgressPanel.IsVisible = true;
        ProgressText.Text = heading;
        ProgressText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0x9A, 0x94) : Color.FromRgb(0xC8, 0xD5, 0xE3));
        ProgressDetailText.Text = detail;
        ProgressPercentText.Text = isError ? "SAFE" : "STOPPED";
        ProgressPercentText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xB0, 0x69) : Color.FromRgb(0x9C, 0xCB, 0xFF));
        UpdateProgressBar.Value = 0;
        ReportStatusChanged();
    }

    private void SetBusyState(bool busy)
    {
        if (_closed) return;
        UpdateButton.IsEnabled = !busy;
        UpdateButton.Content = busy ? "Update in progress…" : "Retry update  →";
        ViewReleaseButton.IsEnabled = !busy;
        SkipButton.Content = busy ? "Cancel safely" : "Remind me later";
        SkipButton.IsEnabled = !busy || !_cancellationRequested;
        CloseButton.IsEnabled = !busy || !_cancellationRequested;
        UpdateClosePurpose();
    }

    private void UpdateClosePurpose()
    {
        string action = !_isDownloading ? "Remind me later" : _cancellationRequested ? "Cancelling update" : "Cancel update";
        Avalonia.Controls.ToolTip.SetTip(CloseButton, action);
        Avalonia.Automation.AutomationProperties.SetName(CloseButton, action);
        Avalonia.Automation.AutomationProperties.SetHelpText(CloseButton, _isDownloading
            ? "Request cancellation and wait for the current update operation to finish safely."
            : "Close this prompt without starting an update.");
    }

    private void ReportStatusChanged()
    {
        if (_closed) return;
        Avalonia.Automation.AutomationProperties.SetHelpText(UpdateProgressBar, (ProgressText.Text ?? string.Empty) + " " + (ProgressDetailText.Text ?? string.Empty));
        // WPF exposes explicit live-region events; the portable view retains
        // LiveSetting/Text notifications through its framework automation peer.
#if WINDOWS
        if (Avalonia.Automation.Peers.AutomationPeer.ListenerExists(Avalonia.Automation.Peers.AutomationEvents.LiveRegionChanged))
        {
            var peer = Avalonia.Automation.Peers.ControlAutomationPeer.FromElement(ProgressText)
                ?? Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(ProgressText);
            peer?.RaiseAutomationEvent(Avalonia.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
#endif
    }

    private void RequestDownloadCancellation()
    {
        if (_closed || !_isDownloading || _downloadCancellation is null || _cancellationRequested) return;
        _cancellationRequested = true;
        ProgressText.Text = "Cancelling safely…";
        ProgressDetailText.Text = "Cancellation requested. Waiting for the update operation to finish safely.";
        SkipButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        UpdateClosePurpose();
        ReportStatusChanged();
        _downloadCancellation.Cancel();
    }

    private void Window_PreviewKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        SkipOrCancel();
    }
}

public partial class UpdatePromptWindow
{
    private void Native_Window_PreviewKeyDown(object? sender, KeyEventArgs e) { Window_PreviewKeyDown(sender!, e); }
}
