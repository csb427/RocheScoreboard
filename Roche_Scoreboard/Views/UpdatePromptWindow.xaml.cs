using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Modal dialog that informs the user about an available update and
/// optionally downloads + applies it before the application exits.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly AutoUpdateService _updateService;
    private bool _isDownloading;

    /// <summary>
    /// True when the user chose to update and the download/apply succeeded.
    /// The caller should shut down the application so the updater script can finish.
    /// </summary>
    public bool UpdateApplied { get; private set; }

    public UpdatePromptWindow(AutoUpdateService updateService)
    {
        ArgumentNullException.ThrowIfNull(updateService);

        InitializeComponent();
        _updateService = updateService;

        // Plain version numbers \u2014 the chip labels (\"INSTALLED\" / \"LATEST\")
        // are baked into the XAML, so we only display the value.
        CurrentVersionText.Text = AutoUpdateService.CurrentVersion.ToString(3);

        string tag = updateService.LatestRelease?.TagName?.TrimStart('v', 'V') ?? "?";
        NewVersionText.Text = tag;

        string notes = updateService.LatestRelease?.Body ?? "";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? "A new version is available. Update now to get the latest features and fixes."
            : notes;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
            return;

        DialogResult = false;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
            return;

        _isDownloading = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING…";
        SkipButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        double progressBarMaxWidth = ProgressPanel.ActualWidth > 0
            ? ProgressPanel.ActualWidth
            : 480;

        var progress = new Progress<double>(p =>
        {
            ProgressFill.Width = p * progressBarMaxWidth;
            int pct = (int)(p * 100);
            ProgressText.Text = pct >= 100 ? "Extracting…" : $"Downloading… {pct}%";
        });

        try
        {
            bool success = await _updateService.DownloadAndApplyAsync(progress, CancellationToken.None);

            if (success)
            {
                ProgressText.Text = "Update ready — restarting…";
                UpdateApplied = true;
                DialogResult = true;
            }
            else
            {
                ProgressText.Text = "Update failed. Please try again later.";
                _isDownloading = false;
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "RETRY";
                SkipButton.IsEnabled = true;
            }
        }
        catch
        {
            ProgressText.Text = "Update failed. Please try again later.";
            _isDownloading = false;
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "RETRY";
            SkipButton.IsEnabled = true;
        }
    }
}
