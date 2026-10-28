// Generated from Models/SetupResult.cs; edit translator/adapters.
// Source SHA-256: 0cbc8f95ef9358adf1ac93067ca3ebe316e6b0ad76ddb1640fdbbb7e23d9750c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
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
        public bool ShowControlPanelCountdown { get; set; } = true;
        public int ControlPanelCountdownMinutes { get; set; } = 20;
        public int ControlPanelCountdownSeconds { get; set; } = 0;
        public bool FinalsMode { get; set; }

        // Weather
        public string? WeatherLocation { get; set; } = "Melbourne, Australia";

        // Messages (styled — supports per-item text and highlight colours)
        public List<MarqueeMessage> Messages { get; set; } = [];
    }
}
