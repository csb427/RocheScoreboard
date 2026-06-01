using System;
using System.Windows;
using LibVLCSharp.Shared;
using Roche_Scoreboard.Services;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    /// <summary>
    /// LibVLCSharp-backed video surface that preserves the public API the
    /// rest of the application relied on when this control wrapped a WPF
    /// <c>MediaElement</c>. The VLC engine handles VFR sources, mixed
    /// codecs and HW-decode fallback far more gracefully, eliminating the
    /// frame-stutter and tearing seen previously.
    /// </summary>
    public partial class VideoPlayerControl : UserControl
    {
        private MediaPlayer? _player;
        private string? _sourcePath;
        private bool _isDisposed;
        private bool _isPlaying;
        private bool _suspended;
        private bool _resumePlayingOnVisible;
        private long _suspendPositionMs;

        // Hardware decoding is preferred for performance, but some GPUs /
        // drivers / codec combinations produce black frames or fail outright.
        // We allow one automatic fallback to software decoding per source so
        // playback recovers without operator intervention. The flag is reset
        // by SetVideoSource so every new clip starts in HW mode again.
        private bool _hwDecodeEnabled = true;
        private bool _swDecodeFallbackAttempted;
        private DateTime _lastPlayStartedUtc;

        // Frozen-playback watchdog: after Play() is requested we expect VLC
        // to advance Time within a short window. If it doesn't, the decode or
        // render pipeline has stalled (typical for HW-decode incompatibilities
        // that don't surface as EncounteredError). The watchdog forces a
        // software-decode retry once, then surfaces a hard failure so the
        // operator sees a clear error rather than an indefinite black screen.
        private const string LogCategory = "VideoPlayer";
        private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(4);
        private System.Windows.Threading.DispatcherTimer? _watchdogTimer;
        private long _lastObservedTimeMs;
        private DateTime _lastTimeAdvanceUtc;
        // Mirror VLC values that consumers can read synchronously without
        // hopping onto the libvlc thread.
        private double _volume = 1.0;
        private bool _isMuted;
        private double _speedRatio = 1.0;

        /// <summary>Raised whenever <see cref="IsPlaying"/> changes.</summary>
        public event EventHandler? PlaybackStateChanged;

        /// <summary>
        /// Raised on the UI thread when the source reaches its end and looping
        /// is disabled. Used by one-shot scenarios (e.g. goal-celebration
        /// videos) to know exactly when to hand control back to the scorebug.
        /// </summary>
        public event EventHandler? PlaybackEnded;

        /// <summary>Raised when the underlying media fails to load.</summary>
        public event EventHandler<VideoLoadFailedEventArgs>? MediaLoadFailed;

        /// <summary>
        /// Raised (on the UI thread) when the current playback time advances.
        /// Lets the VIDEO-tab slider/time UI update on real decoder events
        /// instead of a polling timer that would either over- or under-sample.
        /// </summary>
        public event EventHandler? PositionChanged;

        /// <summary>
        /// When <c>true</c> (the default) the control restarts the source
        /// automatically after <see cref="MediaPlayer.EndReached"/>. Set to
        /// <c>false</c> for one-shot playback that must surface
        /// <see cref="PlaybackEnded"/> so callers can restore the previous
        /// screen.
        /// </summary>
        public bool Loop { get; set; } = true;

        public VideoPlayerControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            IsVisibleChanged += OnIsVisibleChanged;
            Unloaded += (_, _) => Cleanup();
        }

        /// <summary>True when the active player is using hardware-accelerated decoding.</summary>
        public bool IsHardwareDecoding => _hwDecodeEnabled;

        /// <summary>True when an automatic HW->SW fallback has already been attempted for the current source.</summary>
        public bool SoftwareFallbackAttempted => _swDecodeFallbackAttempted;

        /// <summary>The currently loaded source path, or <c>null</c> when no media is loaded.</summary>
        public string? CurrentSourcePath => _sourcePath;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_player is not null) return;
            try
            {
                VideoDiagnostics.Info(LogCategory, "Creating MediaPlayer (hardware decoding enabled).");
                _player = new MediaPlayer(VlcRuntime.Instance)
                {
                    EnableHardwareDecoding = true,
                    EnableKeyInput = false,
                    EnableMouseInput = false,
                    Volume = (int)Math.Round(_volume * 100),
                    Mute = _isMuted,
                };
                _player.Playing += OnVlcPlaying;
                _player.Paused += OnVlcPaused;
                _player.Stopped += OnVlcStopped;
                _player.EndReached += OnVlcEndReached;
                _player.EncounteredError += OnVlcError;
                _player.TimeChanged += OnVlcTimeChanged;
                VideoPlayer.MediaPlayer = _player;

                // If a source was requested before the native player existed,
                // apply it now that the VideoView is ready.
                if (_sourcePath is not null)
                {
                    string pending = _sourcePath;
                    _sourcePath = null;
                    SetVideoSource(pending);
                }
            }
            catch (Exception ex)
            {
                _player = null;
                VideoDiagnostics.Error(LogCategory, "MediaPlayer creation failed.", ex);
                MediaLoadFailed?.Invoke(this, new VideoLoadFailedEventArgs(ex));
            }
        }

        /// <summary>True when the video is currently advancing.</summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>True once a source has been loaded successfully.</summary>
        public bool HasSource => _sourcePath is not null;

        /// <summary>Current playback position. Returns <see cref="TimeSpan.Zero"/> when no media is loaded.</summary>
        public TimeSpan Position
        {
            get
            {
                try
                {
                    long ms = _player?.Time ?? 0;
                    return ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
                }
                catch { return TimeSpan.Zero; }
            }
            set
            {
                // Route through Seek so drag-scrub callers automatically get
                // the coalescing/clamping behavior.
                Seek(value);
            }
        }

        /// <summary>Total duration of the loaded media, or <see cref="TimeSpan.Zero"/> when unknown.</summary>
        public TimeSpan Duration
        {
            get
            {
                try
                {
                    long ms = _player?.Length ?? 0;
                    return ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
                }
                catch { return TimeSpan.Zero; }
            }
        }

        /// <summary>Playback speed multiplier (1.0 = normal). Matches YouTube speed options.</summary>
        public double SpeedRatio
        {
            get => _speedRatio;
            set
            {
                _speedRatio = value <= 0 ? 1.0 : value;
                try { _player?.SetRate((float)_speedRatio); }
                catch { /* ignore */ }
            }
        }

        /// <summary>Audio volume in the range 0.0–1.0.</summary>
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0, 1.0);
                try { if (_player is not null) _player.Volume = (int)Math.Round(_volume * 100); }
                catch { /* ignore */ }
            }
        }

        /// <summary>True when the audio output is muted.</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                try { if (_player is not null) _player.Mute = value; }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Seek to an absolute time, clamped to the loaded media's duration.
        /// Successive calls coalesce: only the most recent target is sent to
        /// libvlc, which prevents drag-scrubbing on the VIDEO-tab slider from
        /// stalling the decoder behind a queue of stale seeks.
        /// </summary>
        public void Seek(TimeSpan position)
        {
            if (_sourcePath is null || _player is null) return;
            TimeSpan max = Duration;
            if (max > TimeSpan.Zero && position > max) position = max;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            long ms = (long)position.TotalMilliseconds;
            System.Threading.Interlocked.Exchange(ref _pendingSeekMs, ms);

            if (System.Threading.Interlocked.Exchange(ref _seekDispatchPending, 1) == 1) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _seekDispatchPending = 0;
                if (_isDisposed || _player is null) return;
                long target = System.Threading.Interlocked.Exchange(ref _pendingSeekMs, -1);
                if (target < 0) return;
                try { _player.Time = target; }
                catch { /* media may be loading or unloading */ }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private long _pendingSeekMs = -1;
        private int _seekDispatchPending;

        private void Cleanup()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            IsVisibleChanged -= OnIsVisibleChanged;
            VideoDiagnostics.Info(LogCategory, "Cleanup() invoked - tearing down MediaPlayer.");
            if (_watchdogTimer is not null)
            {
                _watchdogTimer.Stop();
                _watchdogTimer.Tick -= OnWatchdogTick;
                _watchdogTimer = null;
            }
            if (_isPlaying) SetPlayingState(false);

            try
            {
                if (_player is not null)
                {
                    _player.Playing -= OnVlcPlaying;
                    _player.Paused -= OnVlcPaused;
                    _player.Stopped -= OnVlcStopped;
                    _player.EndReached -= OnVlcEndReached;
                    _player.EncounteredError -= OnVlcError;
                    _player.TimeChanged -= OnVlcTimeChanged;
                    try { _player.Stop(); } catch { }
                    var media = _player.Media;
                    _player.Media = null;
                    media?.Dispose();
                    _player.Dispose();
                    _player = null;
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Sets the video source. The actual native playback is deferred
        /// until the control is visible — libvlc's <c>VideoView</c> hosts
        /// a child HWND that is only created once WPF has laid out the
        /// control, and asking VLC to play before that HWND exists causes
        /// it to fall back to spawning its own top-level window (the
        /// "separate VLC window" the operator was seeing on the scoreboard).
        /// </summary>
        public void SetVideoSource(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            // Prefer the pre-optimised playback copy when one is available.
            string resolved = VideoOptimizer.GetPlaybackPath(filePath);
            _sourcePath = resolved;
            VideoDiagnostics.Info(LogCategory,
                resolved == filePath
                    ? $"Source set: {filePath}"
                    : $"Source set: {filePath} (optimised copy: {resolved})");

            // A fresh source must not inherit the previous clip's
            // suspend bookkeeping. Without this, the Suspend → Resume
            // cycle triggered by EnterVideoTakeover would seek the new
            // (e.g. away) goal video to the previous (home) clip's
            // end-of-stream position, which is exactly why the away
            // MP4 sometimes never appeared on the scoreboard.
            _suspended = false;
            _suspendPositionMs = 0;
            _resumePlayingOnVisible = false;

            // Re-arm hardware decoding for the new clip — every source gets
            // one chance at HW decode before we fall back to software.
            _hwDecodeEnabled = true;
            _swDecodeFallbackAttempted = false;
            if (_player is not null)
            {
                try { _player.EnableHardwareDecoding = true; } catch { /* best-effort */ }

                // Clear the previously-loaded Media so the next Play() call
                // forces a fresh CreateMediaForCurrentSource(). Without this,
                // back-to-back goal videos (e.g. away goal immediately after
                // home goal) would re-use the prior MediaPlayer.Media and the
                // new clip would never start.
                //
                // IMPORTANT: do NOT call _player.Stop() here. VLC's Stop is
                // asynchronous (the state transition runs on the media
                // thread), so calling Stop and then immediately reassigning
                // Media races with that transition and is what was causing
                // the away-side MP4 to occasionally never appear after a
                // home goal video. The player is already paused by
                // ExitVideoTakeover before this method is reached, so
                // simply nulling Media is the safe canonical pattern.
                try
                {
                    var prev = _player.Media;
                    _player.Media = null;
                    prev?.Dispose();
                }
                catch { /* best-effort */ }
            }

            // Do NOT call _player.Play() here. The scoreboard switcher calls
            // Play() once the control is on-screen, at which point VideoView's
            // child HWND has been realised and VLC can embed into it.
            SetPlayingState(false);
        }

        // Build a Media for the current source with the right HW/SW hint so
        // that retries after a HW-decode failure actually use software.
        private Media? CreateMediaForCurrentSource()
        {
            if (_sourcePath is null) return null;
            return _hwDecodeEnabled
                ? new Media(VlcRuntime.Instance, _sourcePath, FromType.FromPath)
                : new Media(VlcRuntime.Instance, _sourcePath, FromType.FromPath, ":avcodec-hw=none");
        }

        /// <summary>Starts playback from the current position.</summary>
        public void Play()
        {
            if (_sourcePath is null || _player is null) return;
            try
            {
                if (_player.Media is null)
                {
                    using var media = CreateMediaForCurrentSource();
                    if (media is null) return;
                    _player.Media = media;
                }
                _player.Mute = _isMuted;
                _lastPlayStartedUtc = DateTime.UtcNow;
                _player.Play();
                SetPlayingState(true);
                StartWatchdog();
                VideoDiagnostics.Info(LogCategory,
                    $"Play() requested · hwDecode={_hwDecodeEnabled} · source={_sourcePath}");
            }
            catch (Exception ex)
            {
                VideoDiagnostics.Error(LogCategory, "Play() failed.", ex);
                SetPlayingState(false);
            }
        }

        /// <summary>Pauses playback while retaining the current frame.</summary>
        public void Pause()
        {
            try { _player?.SetPause(true); }
            catch { /* control may be unloading */ }
            StopWatchdog();
            VideoDiagnostics.Info(LogCategory, "Pause() requested.");
            SetPlayingState(false);
        }

        /// <summary>
        /// Toggles between play and pause. Returns the new playing state.
        /// </summary>
        public bool TogglePlayPause()
        {
            if (_isPlaying) Pause();
            else Play();
            return _isPlaying;
        }

        /// <summary>Stops playback and clears the display.</summary>
        public void Stop()
        {
            try
            {
                if (_player is not null)
                {
                    _player.Stop();
                    var media = _player.Media;
                    _player.Media = null;
                    media?.Dispose();
                }
            }
            catch { /* control may already be unloaded */ }
            StopWatchdog();
            VideoDiagnostics.Info(LogCategory, "Stop() requested.");
            _sourcePath = null;
            SetPlayingState(false);
        }

        private void OnVlcPlaying(object? sender, EventArgs e) => SetPlayingStateOnDispatcher(true);
        private void OnVlcPaused(object? sender, EventArgs e) => SetPlayingStateOnDispatcher(false);
        private void OnVlcStopped(object? sender, EventArgs e) => SetPlayingStateOnDispatcher(false);

        // libvlc raises TimeChanged ~4×/s. We mirror it onto the UI thread
        // with BeginInvoke so consumers (the VIDEO tab slider/clock) can
        // listen without a polling DispatcherTimer that would either burn
        // CPU at higher rates or feel laggy at lower ones.
        private int _positionDispatchPending;
        private void OnVlcTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            // Watchdog: record the most recent time we observed VLC advancing.
            // The watchdog timer compares this against UtcNow to detect stalls.
            System.Threading.Interlocked.Exchange(ref _lastObservedTimeMs, e.Time);
            _lastTimeAdvanceUtc = DateTime.UtcNow;

            // Coalesce: if a prior tick is still queued, drop this one. Stops
            // the UI thread from being drowned when VLC fires bursts (e.g.
            // immediately after a seek).
            if (System.Threading.Interlocked.Exchange(ref _positionDispatchPending, 1) == 1) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _positionDispatchPending = 0;
                if (_isDisposed) return;
                try { PositionChanged?.Invoke(this, EventArgs.Empty); }
                catch { /* never let a subscriber crash playback */ }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnVlcEndReached(object? sender, EventArgs e)
        {
            // VLC raises EndReached on the media thread, so hop to the
            // dispatcher before mutating UI-bound state or calling back into
            // application code.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isDisposed || _player is null || _sourcePath is null) return;

                VideoDiagnostics.Info(LogCategory, $"EndReached (loop={Loop}).");

                if (!Loop)
                {
                    StopWatchdog();
                    SetPlayingState(false);
                    try { PlaybackEnded?.Invoke(this, EventArgs.Empty); }
                    catch { /* never let a subscriber crash playback */ }
                    return;
                }

                try
                {
                    // VLC requires a fresh Media instance to restart after
                    // EndReached — Time = 0 on the same media is a no-op.
                    using var media = CreateMediaForCurrentSource();
                    if (media is null) return;
                    _player.Media = media;
                    _lastPlayStartedUtc = DateTime.UtcNow;
                    _player.Play();
                    SetPlayingState(true);
                }
                catch { /* ignore */ }
            }));
        }

        private void OnVlcError(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                VideoDiagnostics.Warn(LogCategory,
                    $"VLC EncounteredError · source={_sourcePath} · hwDecode={_hwDecodeEnabled}");

                // If the error fired shortly after Play() with hardware
                // decoding enabled, transparently retry once in software
                // mode before surfacing the failure to the operator.
                if (TryFallbackToSoftware("EncounteredError")) return;

                StopWatchdog();
                VideoDiagnostics.Error(LogCategory, "Playback failed and software fallback unavailable.");
                _sourcePath = null;
                SetPlayingState(false);
                MediaLoadFailed?.Invoke(this, new VideoLoadFailedEventArgs(
                    new InvalidOperationException("VLC reported a playback error.")));
            }));
        }

        // Shared HW->SW retry path used by both EncounteredError and the
        // frozen-playback watchdog. Returns true when a fallback attempt was
        // launched so callers can short-circuit failure reporting.
        private bool TryFallbackToSoftware(string reason)
        {
            if (!_hwDecodeEnabled || _swDecodeFallbackAttempted) return false;
            if (_sourcePath is null || _player is null) return false;

            _swDecodeFallbackAttempted = true;
            _hwDecodeEnabled = false;
            VideoDiagnostics.Warn(LogCategory,
                $"Falling back to software decoding ({reason}).");
            try { _player.EnableHardwareDecoding = false; } catch { /* best-effort */ }

            try
            {
                using var media = CreateMediaForCurrentSource();
                if (media is null) return false;
                _player.Media = media;
                _lastPlayStartedUtc = DateTime.UtcNow;
                _lastTimeAdvanceUtc = DateTime.UtcNow;
                System.Threading.Interlocked.Exchange(ref _lastObservedTimeMs, 0);
                _player.Play();
                SetPlayingState(true);
                StartWatchdog();
                return true;
            }
            catch (Exception ex)
            {
                VideoDiagnostics.Error(LogCategory, "Software fallback attempt threw.", ex);
                return false;
            }
        }

        private void StartWatchdog()
        {
            _lastTimeAdvanceUtc = DateTime.UtcNow;
            System.Threading.Interlocked.Exchange(ref _lastObservedTimeMs, 0);

            if (_watchdogTimer is null)
            {
                _watchdogTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                _watchdogTimer.Tick += OnWatchdogTick;
            }
            _watchdogTimer.Start();
        }

        private void StopWatchdog()
        {
            _watchdogTimer?.Stop();
        }

        private void OnWatchdogTick(object? sender, EventArgs e)
        {
            if (_isDisposed || _player is null || _sourcePath is null || !_isPlaying)
            {
                StopWatchdog();
                return;
            }

            // Allow VLC a grace period after Play() before judging the pipeline frozen.
            TimeSpan sincePlay = DateTime.UtcNow - _lastPlayStartedUtc;
            if (sincePlay < WatchdogTimeout) return;

            TimeSpan sinceAdvance = DateTime.UtcNow - _lastTimeAdvanceUtc;
            if (sinceAdvance < WatchdogTimeout) return;

            long observed = System.Threading.Interlocked.Read(ref _lastObservedTimeMs);
            VideoDiagnostics.Warn(LogCategory,
                $"Frozen playback detected · observedMs={observed} · sincePlay={sincePlay.TotalSeconds:F1}s · sinceAdvance={sinceAdvance.TotalSeconds:F1}s");

            StopWatchdog();
            if (TryFallbackToSoftware("watchdog")) return;

            VideoDiagnostics.Error(LogCategory,
                "Playback stalled with no remaining fallback - surfacing failure to operator.");
            _sourcePath = null;
            SetPlayingState(false);
            MediaLoadFailed?.Invoke(this, new VideoLoadFailedEventArgs(
                new InvalidOperationException("Playback stalled - VLC stopped advancing.")));
        }

        private void SetPlayingStateOnDispatcher(bool playing)
        {
            if (Dispatcher.CheckAccess()) SetPlayingState(playing);
            else Dispatcher.BeginInvoke(new Action(() => SetPlayingState(playing)));
        }

        private void SetPlayingState(bool playing)
        {
            if (_isPlaying == playing) return;
            _isPlaying = playing;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Releases the underlying decoder while the control is hidden. The
        /// last playback position and play/pause state are remembered so a
        /// subsequent <see cref="Resume"/> can restore exactly where the
        /// operator left off, but until then no decoding is happening —
        /// freeing budget for the live scorebug.
        /// </summary>
        public void Suspend()
        {
            if (_suspended || _sourcePath is null || _player is null) return;
            _suspended = true;
            _resumePlayingOnVisible = _isPlaying;
            try { _suspendPositionMs = _player.Time; } catch { _suspendPositionMs = 0; }

            try
            {
                _player.Stop();
                var media = _player.Media;
                _player.Media = null;
                media?.Dispose();
            }
            catch { /* ignore */ }
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
            if (_sourcePath is null || _player is null) return;

            try
            {
                using var media = CreateMediaForCurrentSource();
                if (media is null) return;
                _player.Mute = !_resumePlayingOnVisible || _isMuted;
                _player.Media = media;
                _lastPlayStartedUtc = DateTime.UtcNow;
                _player.Play();
                if (_suspendPositionMs > 0)
                {
                    try { _player.Time = _suspendPositionMs; } catch { }
                }
                if (!_resumePlayingOnVisible)
                {
                    _player.SetPause(true);
                    SetPlayingState(false);
                }
                else
                {
                    SetPlayingState(true);
                }
            }
            catch { /* ignore */ }
            finally
            {
                _suspendPositionMs = 0;
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_isDisposed || _sourcePath is null) return;
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

    /// <summary>
    /// EventArgs for <see cref="VideoPlayerControl.MediaLoadFailed"/>. Carries
    /// the originating exception (if any) so consumers can surface a
    /// friendly diagnostic message in the UI.
    /// </summary>
    public sealed class VideoLoadFailedEventArgs : EventArgs
    {
        public VideoLoadFailedEventArgs(Exception? error)
        {
            ErrorException = error;
        }

        /// <summary>Underlying exception, when available.</summary>
        public Exception? ErrorException { get; }
    }
}
