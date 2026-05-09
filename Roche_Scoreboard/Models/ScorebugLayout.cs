namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Which visual layout style the scorebug uses.
    /// </summary>
    public enum ScorebugLayout
    {
        /// <summary>Classic horizontal-bar layout with bottom clock/marquee.</summary>
        Classic,

        /// <summary>Full-panel expanded layout — teams side-by-side, clock centred, message bar on top.</summary>
        Expanded,

        /// <summary>Retro layout — classic bar style with persistent G/B columns and full-width clock bar.</summary>
        Retro,

        /// <summary>No-logos layout — abbreviation cells with inline quarter/timer and full-width marquee.</summary>
        NoLogos,

        /// <summary>Broadcast layout — left clock panel, logo squares, team name strips over stat boxes.</summary>
        Broadcast
    }
}
