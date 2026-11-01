using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public enum OutputScreensaverMediaState { BuiltIn, CustomImage, CustomMediaRequested, CustomUnavailable }

public partial class OutputScreensaverControl : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    // Public decorative output follows OS accessibility or an explicit host
    // override, independently of operator UI motion and custom media playback.
    public static readonly DependencyProperty ReduceDisplayMotionProperty = DependencyProperty.Register(
        nameof(ReduceDisplayMotion), typeof(bool), typeof(OutputScreensaverControl),
        new PropertyMetadata(false, DisplayMotionPreferenceChanged));
    public bool ReduceDisplayMotion
    {
        get => (bool)GetValue(ReduceDisplayMotionProperty);
        set => SetValue(ReduceDisplayMotionProperty, value);
    }
    private static void DisplayMotionPreferenceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        => ((OutputScreensaverControl)target).RefreshOutputActivity();
    private readonly Storyboard _ambientMotion = new();
    private bool _ambientRunning, _observingDisplayMotion;
    private Window? _activityOwner;
    private int _activityGeneration;
    private bool CanPresent => IsLoaded && IsVisible && _activityOwner is { IsVisible: true };

    private string? _configuredPath;
    private VideoPlayerControl? _customVideo;
    private int _configurationGeneration, _requestedVideoGeneration = -1;
    public string? ConfiguredMediaPath => _configuredPath;
    public OutputScreensaverMediaState CurrentMediaState { get; private set; }
    public event Action? MediaStateChanged;
    private bool CurrentConfiguration(int generation) => generation == _configurationGeneration;
    private void PublishMediaState(int generation, OutputScreensaverMediaState state)
    {
        if (!CurrentConfiguration(generation)) return;
        CurrentMediaState = state;
        MediaStateChanged?.Invoke();
        RefreshOutputActivity();
    }
    public void SetClockVisible(bool visible) => ClockText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public OutputScreensaverControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ClockText.FontSize = Math.Max(16, Math.Min(ActualHeight * .2, ActualWidth * .11));
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        BuildAmbientMotion();
        Loaded += (_, _) =>
        {
            ++_activityGeneration;
            AttachActivityOwner();
            if (!_observingDisplayMotion)
            {
                _observingDisplayMotion = true;
                SystemParameters.StaticPropertyChanged += DisplaySystemMotionChanged;
            }
            RefreshOutputActivity();
        };
        Unloaded += (_, _) =>
        {
            ++_activityGeneration;
            StopObservingDisplayMotion();
            DetachActivityOwner();
            _clockTimer.Stop();
            StopAmbientMotion();
            _requestedVideoGeneration = -1;
            _customVideo?.Stop();
        };
        IsVisibleChanged += (_, _) => RefreshOutputActivity();
        DefaultScene.IsVisibleChanged += (_, _) => RefreshOutputActivity();
    }

    private void BuildAmbientMotion()
    {
        AddAmbient(BeamOneTranslate, TranslateTransform.XProperty, -20, 470, 18);
        AddAmbient(BeamTwoTranslate, TranslateTransform.XProperty, 40, -430, 22);
        AddAmbient(FieldTranslate, TranslateTransform.XProperty, -12, 12, 16);
        AddAmbient(FieldTranslate, TranslateTransform.YProperty, -5, 5, 21);
        AddAmbient(ParticleOneTranslate, TranslateTransform.XProperty, 0, 310, 13);
        AddAmbient(ParticleOneTranslate, TranslateTransform.YProperty, 0, 72, 9);
        AddAmbient(ParticleTwoTranslate, TranslateTransform.XProperty, 0, -250, 17);
        AddAmbient(ParticleTwoTranslate, TranslateTransform.YProperty, 0, -92, 14);
        AddAmbient(ParticleThreeTranslate, TranslateTransform.XProperty, 0, 135, 11);
        AddAmbient(HorizonGlow, UIElement.OpacityProperty, 0.16, 0.48, 4);
    }
    private void AddAmbient(DependencyObject target, DependencyProperty property, double from, double to, int seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        _ambientMotion.Children.Add(animation);
    }
    private void StopAmbientMotion()
    {
        if (!_ambientRunning) return;
        _ambientRunning = false;
        _ambientMotion.Remove(Root);
    }
    private void RefreshOutputActivity()
    {
        bool visible = CanPresent;
        if (visible) { UpdateClock(); _clockTimer.Start(); }
        else _clockTimer.Stop();
        bool animate = visible && DefaultScene.IsVisible && !ReduceDisplayMotion && SystemParameters.ClientAreaAnimation;
        if (animate && !_ambientRunning)
        {
            _ambientRunning = true;
            _ambientMotion.Begin(Root, true);
        }
        else if (!animate) StopAmbientMotion();
        if (visible && CustomVideoHost.IsVisible && _requestedVideoGeneration == _configurationGeneration
            && CurrentMediaState == OutputScreensaverMediaState.CustomMediaRequested)
            _customVideo?.Play();
        else if (_customVideo?.IsPlaying == true) _customVideo.Pause();
    }
    private void AttachActivityOwner()
    {
        var owner = Window.GetWindow(this);
        if (ReferenceEquals(owner, _activityOwner)) return;
        DetachActivityOwner();
        _activityOwner = owner;
        if (owner is not null)
        {
            owner.IsVisibleChanged += ActivityOwnerVisibilityChanged;
            owner.Closed += ActivityOwnerClosed;
        }
    }
    private void DetachActivityOwner()
    {
        if (_activityOwner is not null)
        {
            _activityOwner.IsVisibleChanged -= ActivityOwnerVisibilityChanged;
            _activityOwner.Closed -= ActivityOwnerClosed;
        }
        _activityOwner = null;
    }
    private void ActivityOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _activityOwner)) RefreshOutputActivity();
    }
    private void ActivityOwnerClosed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _activityOwner)) return;
        ++_activityGeneration;
        StopObservingDisplayMotion();
        DetachActivityOwner();
        RefreshOutputActivity();
    }
    private void StopObservingDisplayMotion()
    {
        if (!_observingDisplayMotion) return;
        _observingDisplayMotion = false;
        SystemParameters.StaticPropertyChanged -= DisplaySystemMotionChanged;
    }
    private void DisplaySystemMotionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        int generation = _activityGeneration;
        var owner = _activityOwner;
        void Apply()
        {
            if (_observingDisplayMotion && IsLoaded && generation == _activityGeneration
                && ReferenceEquals(owner, _activityOwner)) RefreshOutputActivity();
        }
        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.BeginInvoke(new Action(Apply));
    }

    public void Configure(string? mediaPath)
    {
        int generation = ++_configurationGeneration;
        _requestedVideoGeneration = -1;
        _configuredPath = string.IsNullOrWhiteSpace(mediaPath) ? null : mediaPath.Trim();
        if (_configuredPath is null || !File.Exists(_configuredPath))
        {
            ShowDefaultCore(generation, _configuredPath is null
                ? OutputScreensaverMediaState.BuiltIn : OutputScreensaverMediaState.CustomUnavailable);
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
                if (!CurrentConfiguration(generation)) return;
                CustomVideoHost.Visibility = Visibility.Collapsed;
                CustomImage.Source = bitmap;
                CustomImage.Visibility = Visibility.Visible;
                DefaultScene.Visibility = Visibility.Collapsed;
                PublishMediaState(generation, OutputScreensaverMediaState.CustomImage);
                return;
            }
            catch
            {
                ShowDefaultCore(generation, OutputScreensaverMediaState.CustomUnavailable);
                return;
            }
        }

        try
        {
            _requestedVideoGeneration = generation;
            CurrentMediaState = OutputScreensaverMediaState.CustomMediaRequested;
            EnsureVideoSurface();
            // Loaded can fail synchronously while the native surface is added.
            if (!CurrentConfiguration(generation) || _requestedVideoGeneration != generation) return;
            _customVideo!.Loop = true;
            _customVideo.SetVideoSource(_configuredPath);
            if (!CurrentConfiguration(generation) || _requestedVideoGeneration != generation) return;
            CustomImage.Visibility = Visibility.Collapsed;
            CustomImage.Source = null;
            CustomVideoHost.Visibility = Visibility.Visible;
            DefaultScene.Visibility = Visibility.Collapsed;
            if (IsVisible) _customVideo.Play();
            if (CurrentConfiguration(generation) && _requestedVideoGeneration == generation)
                PublishMediaState(generation, OutputScreensaverMediaState.CustomMediaRequested);
        }
        catch
        {
            ShowDefaultCore(generation, OutputScreensaverMediaState.CustomUnavailable);
        }
    }

    public void ShowDefault()
    {
        int generation = ++_configurationGeneration;
        _configuredPath = null;
        ShowDefaultCore(generation, OutputScreensaverMediaState.BuiltIn);
    }

    private void ShowDefaultCore(int generation, OutputScreensaverMediaState state)
    {
        if (!CurrentConfiguration(generation)) return;
        _requestedVideoGeneration = -1;
        _customVideo?.Stop();
        if (!CurrentConfiguration(generation)) return;
        CustomVideoHost.Visibility = Visibility.Collapsed;
        CustomImage.Source = null;
        CustomImage.Visibility = Visibility.Collapsed;
        DefaultScene.Visibility = Visibility.Visible;
        PublishMediaState(generation, state);
    }

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("h:mm tt");

    private void EnsureVideoSurface()
    {
        if (_customVideo is not null) return;
        var video = new VideoPlayerControl { Loop = true };
        _customVideo = video;
        // Source selection can happen before VideoPlayer's native HWND and
        // MediaPlayer exist. Retry after its own Loaded handler creates them.
        video.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (ReferenceEquals(_customVideo, video) && _requestedVideoGeneration == _configurationGeneration
                && CurrentMediaState == OutputScreensaverMediaState.CustomMediaRequested
                && CanPresent && CustomVideoHost.Visibility == Visibility.Visible && video.HasSource) video.Play();
        }));
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
        video.MediaLoadFailed += (_, _) =>
        {
            if (ReferenceEquals(_customVideo, video) && _requestedVideoGeneration == _configurationGeneration
                && CurrentMediaState == OutputScreensaverMediaState.CustomMediaRequested)
                ShowDefaultCore(_configurationGeneration, OutputScreensaverMediaState.CustomUnavailable);
        };
        CustomVideoHost.Children.Add(video);
    }
}
