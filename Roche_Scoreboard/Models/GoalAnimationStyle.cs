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
        /// signature double-tap score glow, dual signal sweeps and a vertical
        /// broadcast score lift.
        /// </summary>
        Broadcast,

        /// <summary>
        /// Stadium hype — pre-charge edge-glow zaps, horizontal surge entry
        /// (no central scale slam), 5-beat strobe rhythm, reverse-direction
        /// gradient dashes, lateral score charge and crackling collapse exit.
        /// </summary>
        Electric,

        /// <summary>
        /// Champions League drama — slow horizontal slide entry, three
        /// breathing glow cycles, team logo gently zooms forward as an
        /// identity beat, centred score bloom and dissolving exit.
        /// </summary>
        Cinematic,

        /// <summary>
        /// Modern minimalist — restrained GOAL treatment, two-stage heartbeat
        /// on the score area and a fast dissolving score reveal. The scoreboard
        /// remains readable throughout.
        /// </summary>
        Clean,

        /// <summary>
        /// Retro arcade — overlay stutters on like 1980s scoreboard bulbs
        /// warming up, stepped flipboard score reveal, irregular mid-hold
        /// flicker and bulb-burnout exit. No gradient sweep or shimmer.
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
