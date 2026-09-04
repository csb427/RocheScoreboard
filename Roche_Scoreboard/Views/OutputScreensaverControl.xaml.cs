using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class OutputScreensaverControl : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private string? _configuredPath;
    private VideoPlayerControl? _customVideo;

    public OutputScreensaverControl()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => { _clockTimer.Stop(); _customVideo?.Stop(); };
        IsVisibleChanged += (_, _) =>
        {
            if (Visibility == Visibility.Visible && CustomVideoHost.Visibility == Visibility.Visible)
                _customVideo?.Play();
            else if (_customVideo?.IsPlaying == true)
                _customVideo.Pause();
        };
    }

    public void Configure(string? mediaPath)
    {
        _configuredPath = string.IsNullOrWhiteSpace(mediaPath) ? null : mediaPath.Trim();
        if (_configuredPath is null || !File.Exists(_configuredPath))
        {
            ShowDefault();
            return;
        }

        string extension = Path.GetExtension(_configuredPath).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_configuredPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                _customVideo?.Stop();
                CustomVideoHost.Visibility = Visibility.Collapsed;
                CustomImage.Source = bitmap;
                CustomImage.Visibility = Visibility.Visible;
                DefaultScene.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                ShowDefault();
                return;
            }
        }

        try
        {
            CustomImage.Visibility = Visibility.Collapsed;
            EnsureVideoSurface();
            CustomVideoHost.Visibility = Visibility.Visible;
            DefaultScene.Visibility = Visibility.Collapsed;
            _customVideo!.Loop = true;
            _customVideo.SetVideoSource(_configuredPath);
            if (Visibility == Visibility.Visible) _customVideo.Play();
        }
        catch
        {
            ShowDefault();
        }
    }

    public void ShowDefault()
    {
        _customVideo?.Stop();
        CustomVideoHost.Visibility = Visibility.Collapsed;
        CustomImage.Source = null;
        CustomImage.Visibility = Visibility.Collapsed;
        DefaultScene.Visibility = Visibility.Visible;
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("h:mm tt");

    private void EnsureVideoSurface()
    {
        if (_customVideo is not null) return;
        _customVideo = new VideoPlayerControl { Loop = true };
        _customVideo.MediaLoadFailed += (_, _) => ShowDefault();
        CustomVideoHost.Children.Add(_customVideo);
    }
}
