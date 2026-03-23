namespace Roche_Scoreboard.Models
{
    public enum DismissalType
    {
        NotOut,
        Bowled,
        Caught,
        LBW,
        RunOut,
        Stumped,
        HitWicket,
        RetiredHurt,
        CaughtAndBowled,
        Other
    }

    public sealed class CricketPlayer
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        public string DisplayName => string.IsNullOrWhiteSpace(FirstName)
            ? LastName
            : $"{FirstName[0]}. {LastName}";

        public string FullName => $"{FirstName} {LastName}".Trim();

        // Batting stats (current innings)
        public int Runs { get; set; }
        public int BallsFaced { get; set; }
        public int Fours { get; set; }
        public int Sixes { get; set; }
        public bool IsOut { get; set; }
        public string? DismissalText { get; set; }
        public DismissalType HowOut { get; set; } = DismissalType.NotOut;
        public string? DismissalBowler { get; set; }
        public string? DismissalFielder { get; set; }

        /// <summary>Formatted dismissal line for scorecards, e.g. "c: Smith b: Jones"</summary>
        public string DismissalSummary
        {
            get
            {
                if (!IsOut) return "not out";
                return HowOut switch
                {
                    DismissalType.Bowled => $"b: {DismissalBowler ?? "?"}",
                    DismissalType.Caught => $"c: {DismissalFielder ?? "?"} b: {DismissalBowler ?? "?"}",
                    DismissalType.CaughtAndBowled => $"c&b: {DismissalBowler ?? "?"}",
                    DismissalType.LBW => $"lbw b: {DismissalBowler ?? "?"}",
                    DismissalType.Stumped => $"st: {DismissalFielder ?? "?"} b: {DismissalBowler ?? "?"}",
                    DismissalType.RunOut => $"run out ({DismissalFielder ?? "?"})",
                    DismissalType.HitWicket => $"hit wicket b: {DismissalBowler ?? "?"}",
                    DismissalType.RetiredHurt => "retired hurt",
                    _ => DismissalText ?? "out"
                };
            }
        }

        public double StrikeRate => BallsFaced > 0
            ? Math.Round(100.0 * Runs / BallsFaced, 1) : 0;

        // Bowling stats (current innings)
        public int BallsBowled { get; set; }
        public int RunsConceded { get; set; }
        public int Wickets { get; set; }
        public int Maidens { get; set; }
        public int Wides { get; set; }
        public int NoBalls { get; set; }

        /// <summary>The completed-over number when this bowler last finished bowling (for "bowled N overs ago").</summary>
        public int LastOverBowledAt { get; set; } = -1;

        public int CompletedOvers => BallsBowled / 6;
        public int BallsInCurrentOver => BallsBowled % 6;

        public string OversDisplay
        {
            get
            {
                int completed = BallsBowled / 6;
                int remaining = BallsBowled % 6;
                return remaining == 0
                    ? completed.ToString()
                    : $"{completed}.{remaining}";
            }
        }

        public double Economy => BallsBowled > 0
            ? Math.Round(6.0 * RunsConceded / BallsBowled, 2) : 0;

        public string BowlingFigures => $"{Wickets}-{RunsConceded}";

        public void ResetBatting()
        {
            Runs = BallsFaced = Fours = Sixes = 0;
            IsOut = false;
            DismissalText = null;
            HowOut = DismissalType.NotOut;
            DismissalBowler = null;
            DismissalFielder = null;
        }

        public void ResetBowling()
        {
            BallsBowled = RunsConceded = Wickets = Maidens = Wides = NoBalls = 0;
            LastOverBowledAt = -1;
        }
    }
}
