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
    public void SetClockVisible(bool visible) => ClockText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public OutputScreensaverControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ClockText.FontSize = Math.Max(16, Math.Min(ActualHeight * .2, ActualWidth * .11));
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => { _clockTimer.Stop(); _customVideo?.Stop(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && CustomVideoHost.IsVisible)
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
            if (IsVisible) _customVideo.Play();
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
        // Source selection can happen before VideoPlayer's native HWND and
        // MediaPlayer exist. Retry after its own Loaded handler creates them.
        _customVideo.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        { if (IsVisible && CustomVideoHost.Visibility == Visibility.Visible && _customVideo?.HasSource == true) _customVideo.Play(); }));
        var overlay = new Grid { IsHitTestVisible = false, Margin = new Thickness(12, 8, 12, 8) };
        var clock = new System.Windows.Controls.TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Arial"),
            FontWeight = FontWeights.Black,
            Foreground = System.Windows.Media.Brushes.White,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = .8 }
        };
        clock.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("Text") { Source = ClockText });
        clock.SetBinding(System.Windows.Controls.TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { Source = ClockText });
        clock.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = ClockText });
        overlay.Children.Add(clock); _customVideo.SetPresentationOverlay(overlay);
        _customVideo.MediaLoadFailed += (_, _) => ShowDefault();
        CustomVideoHost.Children.Add(_customVideo);
    }
}
