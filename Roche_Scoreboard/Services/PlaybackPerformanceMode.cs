using System;
using System.Threading;

namespace Roche_Scoreboard.Services
{
    /// <summary>
    /// Lightweight, process-wide signal that one or more MP4 video surfaces
    /// are actively playing. Other UI components (scorebug, ticker, overlays)
    /// subscribe to <see cref="StateChanged"/> so they can pause non-essential
    /// animations and disable expensive visual effects while video is on
    /// screen. Once playback ends the state automatically reverts.
    /// </summary>
    public static class PlaybackPerformanceMode
    {
        private static int _activeCount;

        /// <summary>Raised when playback becomes active or fully ends.</summary>
        public static event EventHandler<bool>? StateChanged;

        /// <summary>True while at least one video surface is playing.</summary>
        public static bool IsActive => Volatile.Read(ref _activeCount) > 0;

        /// <summary>
        /// Reports that a video surface has begun (or resumed) playback.
        /// Calls are reference counted, so concurrent goal videos and a
        /// presentation video stay coordinated.
        /// </summary>
        public static void Begin()
        {
            if (Interlocked.Increment(ref _activeCount) == 1)
            {
                Raise(true);
            }
        }

        /// <summary>Reports that a previously-active video surface stopped.</summary>
        public static void End()
        {
            int updated = Interlocked.Decrement(ref _activeCount);
            if (updated <= 0)
            {
                // Floor at zero in case of unbalanced calls.
                if (updated < 0)
                {
                    Interlocked.Exchange(ref _activeCount, 0);
                }
                Raise(false);
            }
        }

        private static void Raise(bool active)
        {
            try { StateChanged?.Invoke(null, active); }
            catch { /* never let a subscriber crash the app */ }
        }
    }
}
