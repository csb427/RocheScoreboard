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
        /// TV sports graphics package — sharp snap-in overlay, signature
        /// double-tap score glow, text unfurls horizontally then vertically
        /// (lower-third reveal), authoritative gradient sweep with trailing shimmer.
        /// </summary>
        Broadcast,

        /// <summary>
        /// Lightning storm — rapid strobe flashes on edge and score glow,
        /// text snaps in instantly with micro-vibration from electrical surge,
        /// reversed shimmer discharge (right-to-left), hard cut-out.
        /// </summary>
        Electric,

        /// <summary>
        /// Epic title drop — slow dramatic overlay reveal, intense long-building
        /// glow, text starts massive (3×) and shrinks to 1× with elastic landing
        /// bounce, dual shimmer passes, very slow cinematic fade-out.
        /// </summary>
        Cinematic,

        /// <summary>
        /// Breathing pulse of light — no overlay panel or text, organic
        /// heartbeat-like score glow (rise, dip, second pulse, slow exhale),
        /// barely-there edge accent, gentle low-opacity shimmer pass.
        /// </summary>
        Clean,

        /// <summary>
        /// Vintage flicker — overlay flickers on with rapid opacity stutters
        /// (incandescent bulbs warming up), text visible through the flickering
        /// panel, warm slow score glow, no sweep or shimmer, flickers off.
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
