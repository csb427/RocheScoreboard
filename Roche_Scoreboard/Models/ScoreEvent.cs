using System;

namespace Roche_Scoreboard.Models
{
    public enum ScoreType { Goal, Behind }
    public enum TeamSide { Home, Away }

    public sealed class ScoreEvent
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Quarter { get; init; }
        public TimeSpan GameTime { get; init; }         // elapsed in quarter
        public TeamSide Team { get; init; }
        public ScoreType Type { get; init; }

        // Snapshots after event (for scoreworm/stats)
        public int HomeGoals { get; init; }
        public int HomeBehinds { get; init; }
        public int AwayGoals { get; init; }
        public int AwayBehinds { get; init; }

        public int HomeTotal => HomeGoals * 6 + HomeBehinds;
        public int AwayTotal => AwayGoals * 6 + AwayBehinds;
        public int Margin => HomeTotal - AwayTotal;

        public string Description =>
            $"{(Team == TeamSide.Home ? "Home" : "Away")} {(Type == ScoreType.Goal ? "Goal" : "Behind")}";

        public string FormatLog(string homeName, string awayName)
        {
            string teamName = Team == TeamSide.Home ? homeName : awayName;
            string type = Type == ScoreType.Goal ? "Goal" : "Behind";
            int goals = Team == TeamSide.Home ? HomeGoals : AwayGoals;
            int behinds = Team == TeamSide.Home ? HomeBehinds : AwayBehinds;
            int total = Team == TeamSide.Home ? HomeTotal : AwayTotal;
            string clock = $"{(int)GameTime.TotalMinutes:D2}:{GameTime.Seconds:D2}";
            return $"Q{Quarter} {clock}  —  {teamName} {type}  ({goals}.{behinds}.{total})";
        }
    }
}