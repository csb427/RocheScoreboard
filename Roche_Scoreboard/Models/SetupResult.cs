namespace Roche_Scoreboard.Models
{
    /// <summary>
    /// Carries all setup wizard results back to MainWindow.
    /// </summary>
    public sealed class SetupResult
    {
        // Home team
        public string HomeName { get; set; } = "Home Team";
        public string HomeAbbr { get; set; } = "HOM";
        public string HomePrimaryColor { get; set; } = "#0A2A6A";
        public string HomeSecondaryColor { get; set; } = "#FFFFFF";
        public string? HomeLogoPath { get; set; }
        public string? HomeGoalVideoPath { get; set; }

        // Away team
        public string AwayName { get; set; } = "Away Team";
        public string AwayAbbr { get; set; } = "AWA";
        public string AwayPrimaryColor { get; set; } = "#7A1A1A";
        public string AwaySecondaryColor { get; set; } = "#FFFFFF";
        public string? AwayLogoPath { get; set; }
        public string? AwayGoalVideoPath { get; set; }

        // Clock
        public ClockMode ClockMode { get; set; } = ClockMode.CountUp;
        public int QuarterMinutes { get; set; } = 20;
        public int QuarterSeconds { get; set; } = 0;

        // Messages
        public List<string> Messages { get; set; } = new();
    }
}
