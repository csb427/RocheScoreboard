using System.Collections.Generic;

namespace Roche_Scoreboard.Models
{
    public sealed class CricketSetupResult
    {
        // Team A
        public string TeamAName { get; set; } = "Team A";
        public string TeamAAbbr { get; set; } = "TMA";
        public string TeamAPrimaryColor { get; set; } = "#CC0000";
        public string TeamASecondaryColor { get; set; } = "#FFFFFF";
        public string? TeamALogoPath { get; set; }
        public List<CricketPlayer> TeamAPlayers { get; set; } = [];

        // Team B
        public string TeamBName { get; set; } = "Team B";
        public string TeamBAbbr { get; set; } = "TMB";
        public string TeamBPrimaryColor { get; set; } = "#000080";
        public string TeamBSecondaryColor { get; set; } = "#FFFFFF";
        public string? TeamBLogoPath { get; set; }
        public List<CricketPlayer> TeamBPlayers { get; set; } = [];

        // Match settings
        public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;
        public int TotalOvers { get; set; } = 50;
        public bool ReplaceWideBalls { get; set; } = true;
        public bool ReplaceNoBalls { get; set; } = true;
        public bool FreeHitsEnabled { get; set; }
        public string WeatherLocation { get; set; } = "Melbourne, Australia";

        // Toss
        public string TossWinner { get; set; } = "";  // team name that won the toss
        public bool TossWinnerElectedToBat { get; set; } = true;
        public bool TeamABatsFirst { get; set; } = true;

        // Messages
        public List<string> Messages { get; set; } = [];

        /// <summary>Each empty squad gets its own numbered eleven.</summary>
        internal void FillEmptySquads()
        {
            if (TeamAPlayers.Count == 0) TeamAPlayers = CreateDefaultEleven();
            if (TeamBPlayers.Count == 0) TeamBPlayers = CreateDefaultEleven();
        }

        private static List<CricketPlayer> CreateDefaultEleven() =>
            System.Linq.Enumerable.Range(1, 11)
                .Select(number => new CricketPlayer { LastName = $"Player {number}" }).ToList();
    }
}
