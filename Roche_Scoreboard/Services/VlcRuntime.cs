using System;
using LibVLCSharp.Shared;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Process-wide LibVLC runtime. The native library must be initialised
/// exactly once via <see cref="Core.Initialize"/> before any
/// <see cref="LibVLC"/> instance is created; this helper hides both that
/// init call and the shared <see cref="LibVLC"/> handle behind a
/// thread-safe lazy so any view that needs a player can just call
/// <see cref="Instance"/>.
/// </summary>
internal static class VlcRuntime
{
    private const string LogCategory = "VlcRuntime";

    private static readonly object _gate = new();
    private static LibVLC? _instance;
    private static Exception? _initializationError;
    private static bool _initialisationAttempted;

    /// <summary>
    /// True once the native runtime has been initialised successfully and
    /// the shared <see cref="LibVLC"/> instance is ready.
    /// </summary>
    public static bool IsAvailable
    {
        get { lock (_gate) return _instance is not null; }
    }

    /// <summary>
    /// The exception captured the last time <see cref="Core.Initialize"/>
    /// or the <see cref="LibVLC"/> constructor threw. <c>null</c> when
    /// initialisation has not yet been attempted or succeeded.
    /// </summary>
    public static Exception? InitializationError
    {
        get { lock (_gate) return _initializationError; }
    }

    /// <summary>
    /// Returns the shared <see cref="LibVLC"/> instance, performing native
    /// runtime initialisation on first use. Throws <see cref="InvalidOperationException"/>
    /// when the native runtime cannot be loaded so callers fail fast rather
    /// than spinning on a black playback surface.
    /// </summary>
    public static LibVLC Instance
    {
        get
        {
            if (_instance is not null) return _instance;
            EnsureInitialised();
            return _instance ?? throw new InvalidOperationException(
                "LibVLC native runtime is not available.", _initializationError);
        }
    }

    /// <summary>
    /// Called from application startup so a missing/broken VLC install
    /// surfaces immediately as a diagnostic error instead of becoming an
    /// unexplained black-screen state during the first playback attempt.
    /// </summary>
    public static void ValidateOrThrow()
    {
        EnsureInitialised();
        if (_instance is null)
        {
            throw new InvalidOperationException(
                "LibVLC native runtime failed to initialise. Video playback is unavailable.",
                _initializationError);
        }
    }

    private static void EnsureInitialised()
    {
        if (_instance is not null) return;
        lock (_gate)
        {
            if (_instance is not null) return;
            if (_initialisationAttempted && _initializationError is not null) return;
            _initialisationAttempted = true;

            try
            {
                VideoDiagnostics.Info(LogCategory, "Initialising LibVLC native runtime...");
                Core.Initialize();
                VideoDiagnostics.Info(LogCategory, "Core.Initialize() succeeded.");
            }
            catch (Exception ex)
            {
                _initializationError = ex;
                VideoDiagnostics.Error(LogCategory,
                    "Core.Initialize() failed - native VLC libraries are missing or incompatible.", ex);
                return;
            }

            try
            {
                // --quiet keeps VLC's own logger out of stderr; --no-video-title-show
                // suppresses the on-screen file-name overlay VLC shows by default.
                _instance = new LibVLC(
                    "--quiet",
                    "--no-video-title-show",
                    "--no-snapshot-preview",
                    "--avcodec-hw=any");
                VideoDiagnostics.Info(LogCategory,
                    $"LibVLC ready - runtime version {_instance.Version}");
            }
            catch (Exception ex)
            {
                _initializationError = ex;
                _instance = null;
                VideoDiagnostics.Error(LogCategory,
                    "LibVLC instance construction failed.", ex);
            }
        }
    }
}
