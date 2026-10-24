using RocheScoreboard.Mac.Production;
// Generated from Views/OutputScreensaverControl.xaml.cs; edit translator/adapters.
// Source SHA-256: c5c6fed612ce92ea3d696c9ca00ab647bc321fdf54538b327ec23a7800a5fb3e
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

    public OutputScreensaverControl()
    {
        NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        Loaded += (_, _) => _clockTimer.Start();
        Unloaded += (_, _) => { _clockTimer.Stop(); _customVideo?.Stop(); };
        PropertyChanged += (_, _) =>
        {
            if (IsVisible == true && CustomVideoHost.IsVisible == true)
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
