namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Animation preset used when a goal or behind is scored. Each style has
    /// a deliberately distinct motion fingerprint so the five presets do not
    /// feel like variants of the same animation.
    /// </summary>
    public enum GoalAnimationStyle
    {
        /// <summary>
        /// Premium TV graphics package — vertical-unfurl overlay entry,
        /// signature double-tap score glow, two distinct shimmer passes,
        /// controlled opacity fade. ~3.5s.
        /// </summary>
        Broadcast,

        /// <summary>
        /// Stadium hype — pre-charge edge-glow zaps, horizontal surge entry
        /// (no central scale slam), 5-beat strobe rhythm, reverse-direction
        /// gradient dashes, crackling collapse exit. ~2.9s.
        /// </summary>
        Electric,

        /// <summary>
        /// Champions League drama — slow horizontal slide entry, three
        /// breathing glow cycles, team logo gently zooms forward as an
        /// identity beat, slow slide exit. ~5.0s.
        /// </summary>
        Cinematic,

        /// <summary>
        /// Modern minimalist — no overlay panel, no GOAL! text. Two-stage
        /// heartbeat on the score area, soft edge accent, elastic swell on
        /// the total digit. Scoreboard remains fully readable. ~2.2s.
        /// </summary>
        Clean,

        /// <summary>
        /// Retro arcade — overlay stutters on like 1980s scoreboard bulbs
        /// warming up, irregular mid-hold flicker, bulb-burnout exit.
        /// No gradient sweep, no shimmer. ~2.4s.
        /// </summary>
        Classic,

        /// <summary>
        /// Custom team MP4 video played full-screen over the scoreboard.
        /// Score digits update only after the video finishes.
        /// Falls back to Broadcast if no video file is configured.
        /// </summary>
        CustomVideo
    }
}
