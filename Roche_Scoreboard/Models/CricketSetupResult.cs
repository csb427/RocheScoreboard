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
        public List<CricketPlayer> TeamAPlayers { get; set; } = new();

        // Team B
        public string TeamBName { get; set; } = "Team B";
        public string TeamBAbbr { get; set; } = "TMB";
        public string TeamBPrimaryColor { get; set; } = "#000080";
        public string TeamBSecondaryColor { get; set; } = "#FFFFFF";
        public string? TeamBLogoPath { get; set; }
        public List<CricketPlayer> TeamBPlayers { get; set; } = new();

        // Match settings
        public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;
        public int TotalOvers { get; set; } = 50;

        // Toss
        public string TossWinner { get; set; } = "";  // team name that won the toss
        public bool TossWinnerElectedToBat { get; set; } = true;
        public bool TeamABatsFirst { get; set; } = true;

        // Messages
        public List<string> Messages { get; set; } = new();
    }
}
