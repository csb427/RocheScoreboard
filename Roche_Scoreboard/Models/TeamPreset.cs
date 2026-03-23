using System.Collections.Generic;

namespace Roche_Scoreboard.Models
{
    public sealed class TeamPreset
    {
        public string PresetName { get; set; } = "";
        public string TeamName { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string PrimaryColor { get; set; } = "#0A2A6A";
        public string SecondaryColor { get; set; } = "#FFFFFF";
        public string? LogoPath { get; set; }
        public string? GoalVideoPath { get; set; }

        /// <summary>Cricket player list (first/last names). Null for AFL-only presets.</summary>
        public List<CricketPlayerEntry>? CricketPlayers { get; set; }
    }

    /// <summary>Serialisable player name pair for preset storage.</summary>
    public sealed class CricketPlayerEntry
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }
}
