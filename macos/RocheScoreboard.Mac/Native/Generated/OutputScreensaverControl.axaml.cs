using RocheScoreboard.Mac.Production;
// Generated from Views/OutputScreensaverControl.xaml.cs; edit translator/adapters.
// Source SHA-256: 378e2ae4349d012da66b0f82147486688c5ddc6b61980f3104345922a244763a
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using System.IO;
using Avalonia.Media.Imaging;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class OutputScreensaverControl : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private string? _configuredPath;
    private VideoPlayerControl? _customVideo;
    public void SetClockVisible(bool visible) => ClockText.IsVisible = visible ? true : false;

    public OutputScreensaverControl()
    {
        NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
        SizeChanged += (_, _) => ClockText.FontSize = Math.Max(16, Math.Min(Bounds.Height * .2, Bounds.Width * .11));
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => { _clockTimer.Stop(); _customVideo?.Stop(); };
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, _) =>
        {
            if (IsVisible == true && CustomVideoHost.IsVisible == true)
                _customVideo?.Play();
            else if (_customVideo?.IsPlaying == true)
                _customVideo.Pause();
        });
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
                var bitmap = new NativeBitmap();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_configuredPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                _customVideo?.Stop();
                CustomVideoHost.IsVisible = false;
                CustomImage.Source = bitmap;
                CustomImage.IsVisible = true;
                DefaultScene.IsVisible = false;
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
            CustomImage.IsVisible = false;
            EnsureVideoSurface();
            CustomVideoHost.IsVisible = true;
            DefaultScene.IsVisible = false;
            _customVideo!.Loop = true;
            _customVideo.SetVideoSource(_configuredPath);
            if (IsVisible == true) _customVideo.Play();
        }
        catch
        {
            ShowDefault();
        }
    }

    public void ShowDefault()
    {
        _customVideo?.Stop();
        CustomVideoHost.IsVisible = false;
        CustomImage.Source = null;
        CustomImage.IsVisible = false;
        DefaultScene.IsVisible = true;
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("h:mm tt");

    private void EnsureVideoSurface()
    {
        if (_customVideo is not null) return;
        _customVideo = new VideoPlayerControl { Loop = true };
        // Source selection can happen before VideoPlayer's native HWND and
        // MediaPlayer exist. Retry after its own Loaded handler creates them.
        _customVideo.Loaded += (_, _) => Dispatcher.UIThread.Post(DispatcherPriority.ContextIdle, new Action(() =>
        { if (IsVisible && CustomVideoHost.IsVisible == true && _customVideo?.HasSource == true) _customVideo.Play(); }));
        var overlay = new Grid { IsHitTestVisible = false, Margin = new Thickness(12, 8, 12, 8) };
        var clock = new NativeTextBlock
        {
            FontFamily = NativeFonts.Family("Arial"),
            FontWeight = FontWeight.Black,
            Foreground = Avalonia.Media.Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Effect = new Avalonia.Media.DropShadowEffect { Color = Avalonia.Media.Colors.Black, BlurRadius = 8, OffsetX = 0, OffsetY = 0, Opacity = .8 }
        };
        clock.SetBinding(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding("Text") { Source = ClockText });
        clock.SetBinding(Avalonia.Controls.TextBlock.FontSizeProperty, new Avalonia.Data.Binding("FontSize") { Source = ClockText });
        clock.SetBinding(Control.IsVisibleProperty, new Avalonia.Data.Binding("Visibility") { Source = ClockText });
        overlay.Children.Add(clock); _customVideo.SetPresentationOverlay(overlay);
        _customVideo.MediaLoadFailed += (_, _) => ShowDefault();
        CustomVideoHost.Children.Add(_customVideo);
    }
}

public partial class OutputScreensaverControl
{
    private TranslateTransform BeamOneTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BeamOneTranslate"];
    private TranslateTransform BeamTwoTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BeamTwoTranslate"];
    private TranslateTransform FieldTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["FieldTranslate"];
    private TranslateTransform ParticleOneTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ParticleOneTranslate"];
    private TranslateTransform ParticleTwoTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ParticleTwoTranslate"];
    private TranslateTransform ParticleThreeTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ParticleThreeTranslate"];
}
