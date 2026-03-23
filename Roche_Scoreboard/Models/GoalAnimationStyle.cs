namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Animation preset used when a goal or behind is scored.
    /// Each style controls overlay appearance, score digit transitions,
    /// colour effects, and timing curves.
    /// </summary>
    public enum GoalAnimationStyle
    {
        /// <summary>
        /// Premier League / AFL TV style — team-colour panel flash,
        /// score scale-up with glow, count-up total, smooth settle.
        /// </summary>
        Broadcast,

        /// <summary>
        /// High-energy — lightning-edge glow burst, rapid-flip digits
        /// with overshoot, shimmer wipe, colour pulse fade.
        /// </summary>
        Electric,

        /// <summary>
        /// Dramatic hold — full overlay slow reveal, large-scale text pop,
        /// double shimmer pass, long hold, cinematic fade-out.
        /// </summary>
        Cinematic,

        /// <summary>
        /// Minimal and modern — subtle colour accent pulse on score area,
        /// smooth slide-flip digits, soft glow settle. No full overlay.
        /// </summary>
        Clean,

        /// <summary>
        /// Original animation preserved as a selectable option —
        /// gradient sweep, back-ease text pop, single shimmer, fade out.
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
