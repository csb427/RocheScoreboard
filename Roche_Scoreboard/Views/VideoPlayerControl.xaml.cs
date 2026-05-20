using System;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class VideoPlayerControl : UserControl
    {
        private Uri? _source;
        private bool _isDisposed;
        private bool _isPlaying;
        private bool _mediaOpened;
        private bool _autoPlayPending;
        private bool _pauseOnOpenPending;
        private bool _suspended;
        private bool _resumePlayingOnVisible;
        private TimeSpan _suspendPosition;

        /// <summary>Raised whenever <see cref="IsPlaying"/> changes.</summary>
        public event EventHandler? PlaybackStateChanged;

        /// <summary>Raised when the underlying media fails to load.</summary>
        public event EventHandler<ExceptionRoutedEventArgs>? MediaLoadFailed;

        public VideoPlayerControl()
        {
            InitializeComponent();
            VideoPlayer.MediaOpened += OnMediaOpened;
            VideoPlayer.MediaEnded += OnMediaEnded;
            VideoPlayer.MediaFailed += OnMediaFailed;
            // Auto-release decoder resources whenever this control is taken
            // off screen (e.g. operator switches to another presentation
            // screen). Without this the MediaElement keeps decoding frames
            // even while collapsed, which is the dominant source of background
            // CPU load when video is the active presentation surface.
            IsVisibleChanged += OnIsVisibleChanged;
            Unloaded += (_, _) => Cleanup();
        }

        /// <summary>True when the video is currently advancing.</summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>True once a source has been loaded successfully.</summary>
        public bool HasSource => _source is not null;

        /// <summary>Current playback position. Returns <see cref="TimeSpan.Zero"/> when no media is loaded.</summary>
        public TimeSpan Position
        {
            get
            {
                try { return VideoPlayer.Position; }
                catch { return TimeSpan.Zero; }
            }
            set
            {
                try { VideoPlayer.Position = value; }
                catch { /* media may be unloading */ }
            }
        }

        /// <summary>Total duration of the loaded media, or <see cref="TimeSpan.Zero"/> when unknown.</summary>
        public TimeSpan Duration
        {
            get
            {
                try
                {
                    return VideoPlayer.NaturalDuration.HasTimeSpan
                        ? VideoPlayer.NaturalDuration.TimeSpan
                        : TimeSpan.Zero;
                }
                catch { return TimeSpan.Zero; }
            }
        }

        /// <summary>Playback speed multiplier (1.0 = normal). Matches YouTube speed options.</summary>
        public double SpeedRatio
        {
            get => VideoPlayer.SpeedRatio;
            set
            {
                try { VideoPlayer.SpeedRatio = value; }
                catch { /* ignore */ }
            }
        }

        /// <summary>Audio volume in the range 0.0–1.0.</summary>
        public double Volume
        {
            get => VideoPlayer.Volume;
            set
            {
                try { VideoPlayer.Volume = Math.Clamp(value, 0.0, 1.0); }
                catch { /* ignore */ }
            }
        }

        /// <summary>True when the audio output is muted.</summary>
        public bool IsMuted
        {
            get => VideoPlayer.IsMuted;
            set
            {
                try { VideoPlayer.IsMuted = value; }
                catch { /* ignore */ }
            }
        }

        /// <summary>Seek to an absolute time, clamped to the loaded media's duration.</summary>
        public void Seek(TimeSpan position)
        {
            if (_source is null) return;
            try
            {
                TimeSpan max = Duration;
                if (max > TimeSpan.Zero && position > max) position = max;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                VideoPlayer.Position = position;
            }
            catch { /* ignore */ }
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            VideoPlayer.MediaOpened -= OnMediaOpened;
            VideoPlayer.MediaEnded -= OnMediaEnded;
            VideoPlayer.MediaFailed -= OnMediaFailed;
            IsVisibleChanged -= OnIsVisibleChanged;
            // Ensure PlaybackPerformanceMode reference count is released even if
            // the control is unloaded while still playing — otherwise the rest
            // of the UI stays in low-perf mode for the remainder of the session.
            if (_isPlaying)
                SetPlayingState(false);
            Stop();
        }

        /// <summary>
        /// Sets the video source and prepares the player. The video is loaded
        /// paused on the first frame so the operator can review the content
        /// before broadcasting.
        /// </summary>
        public void SetVideoSource(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            // Prefer the pre-optimised playback copy when one is available.
            // Heavy 1080p/4K imports get re-encoded in the background by
            // VideoOptimizer; the first playback uses the original while the
            // optimised copy is produced for subsequent broadcasts.
            string resolved = VideoOptimizer.GetPlaybackPath(filePath);

            Uri? uri;
            try
            {
                uri = new Uri(resolved, UriKind.Absolute);
            }
            catch (UriFormatException)
            {
                _source = null;
                return;
            }

            _source = uri;
            _mediaOpened = false;
            _autoPlayPending = false;
            _pauseOnOpenPending = true;

            // Load paused on the first frame. The well-known WPF MediaElement
            // pattern is: set Source, then Play() immediately followed by
            // Pause(). MediaElement decodes the first frame for display but
            // never advances playback. Muting during the load window also
            // guarantees no audio escapes if the underlying codec briefly
            // ticks before Pause() takes effect.
            try
            {
                VideoPlayer.IsMuted = true;
                VideoPlayer.Source = _source;
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
                VideoPlayer.Pause();
                SetPlayingState(false);
            }
            catch
            {
                VideoPlayer.Source = null;
                _source = null;
                _pauseOnOpenPending = false;
                VideoPlayer.IsMuted = false;
            }
        }

        /// <summary>Starts playback from the current position.</summary>
        public void Play()
        {
            if (_source is null)
                return;

            try
            {
                if (!_mediaOpened)
                {
                    // Media isn't opened yet — request auto-play when it is.
                    _autoPlayPending = true;
                    _pauseOnOpenPending = false;
                    VideoPlayer.Source = _source;
                }

                VideoPlayer.IsMuted = false;
                VideoPlayer.Play();
                SetPlayingState(true);
            }
            catch
            {
                SetPlayingState(false);
            }
        }

        /// <summary>Pauses playback while retaining the current frame.</summary>
        public void Pause()
        {
            try
            {
                VideoPlayer.Pause();
            }
            catch
            {
                // Ignore — control may be unloading.
            }
            SetPlayingState(false);
        }

        /// <summary>
        /// Toggles between play and pause. Returns the new playing state.
        /// </summary>
        public bool TogglePlayPause()
        {
            if (_isPlaying)
                Pause();
            else
                Play();
            return _isPlaying;
        }

        /// <summary>Stops playback and clears the display.</summary>
        public void Stop()
        {
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
            catch
            {
                // Ignore — control may already be unloaded
            }
            _mediaOpened = false;
            _autoPlayPending = false;
            SetPlayingState(false);
        }

        private void OnMediaOpened(object? sender, RoutedEventArgs e)
        {
            _mediaOpened = true;

            // Restore the saved playback position when resuming from a
            // Suspend()/Resume() cycle. Cleared after consumption so a
            // subsequent SetVideoSource() starts from frame zero.
            if (_suspendPosition > TimeSpan.Zero)
            {
                try { VideoPlayer.Position = _suspendPosition; } catch { }
                _suspendPosition = TimeSpan.Zero;
            }

            if (_autoPlayPending)
            {
                _autoPlayPending = false;
                _pauseOnOpenPending = false;
                try
                {
                    VideoPlayer.IsMuted = false;
                    VideoPlayer.Play();
                    SetPlayingState(true);
                }
                catch
                {
                    SetPlayingState(false);
                }
                return;
            }

            if (_pauseOnOpenPending)
            {
                _pauseOnOpenPending = false;
                try
                {
                    VideoPlayer.Pause();
                    VideoPlayer.Position = TimeSpan.Zero;
                    // Now that the video is paused on frame 0, unmute so the
                    // operator hears audio as soon as they hit Play.
                    VideoPlayer.IsMuted = false;
                }
                catch
                {
                    // Ignore — control may be unloading.
                }
                SetPlayingState(false);
            }
        }

        private void OnMediaEnded(object? sender, RoutedEventArgs e)
        {
            // Loop the video
            try
            {
                VideoPlayer.Position = TimeSpan.Zero;
                if (_isPlaying)
                    VideoPlayer.Play();
            }
            catch
            {
                // Source may have been cleared mid-event
            }
        }

        private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            // Video codec unsupported or file corrupted — clear source
            try
            {
                VideoPlayer.Source = null;
            }
            catch
            {
                // Ignore
            }
            _source = null;
            _mediaOpened = false;
            _autoPlayPending = false;
            SetPlayingState(false);

            MediaLoadFailed?.Invoke(this, e);
        }

        private void SetPlayingState(bool playing)
        {
            if (_isPlaying == playing) return;
            _isPlaying = playing;
            if (playing) PlaybackPerformanceMode.Begin();
            else PlaybackPerformanceMode.End();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Releases the underlying decoder while the control is hidden. The
        /// last playback position and play/pause state are remembered so a
        /// subsequent <see cref="Resume"/> can restore exactly where the
        /// operator left off, but until then the MediaElement holds no GPU
        /// or decoder resources — freeing budget for the live scorebug.
        /// </summary>
        public void Suspend()
        {
            if (_suspended || _source is null) return;
            _suspended = true;
            _resumePlayingOnVisible = _isPlaying;
            try { _suspendPosition = VideoPlayer.Position; } catch { _suspendPosition = TimeSpan.Zero; }

            try
            {
                VideoPlayer.Pause();
                VideoPlayer.Source = null;
            }
            catch { /* ignore */ }
            _mediaOpened = false;
            _autoPlayPending = false;
            _pauseOnOpenPending = false;
            SetPlayingState(false);
        }

        /// <summary>
        /// Reattaches the source previously stored by <see cref="Suspend"/>
        /// and seeks back to the saved position. Resumes playback when the
        /// control was playing at suspend time, otherwise stays paused on
        /// the saved frame.
        /// </summary>
        public void Resume()
        {
            if (!_suspended) return;
            _suspended = false;
            if (_source is null) return;

            try
            {
                VideoPlayer.IsMuted = !_resumePlayingOnVisible;
                VideoPlayer.Source = _source;
                // Defer seek + play to MediaOpened — Position can't be set
                // before the decoder has initialised against the new source.
                _autoPlayPending = _resumePlayingOnVisible;
                _pauseOnOpenPending = !_resumePlayingOnVisible;
            }
            catch
            {
                _autoPlayPending = false;
                _pauseOnOpenPending = false;
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_isDisposed || _source is null) return;
            if (IsVisible)
            {
                if (_suspended) Resume();
            }
            else
            {
                if (!_suspended) Suspend();
            }
        }
    }
}
