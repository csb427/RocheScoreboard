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
        Other,
        RetiredOut,
        ObstructingField,
        HitBallTwice
    }

    public sealed class CricketPlayer : System.ComponentModel.INotifyPropertyChanged
    {
        private string _firstName="",_lastName="";
        public string FirstName { get=>_firstName; set{if(_firstName==value)return;_firstName=value;NameChanged(nameof(FirstName));} }
        public string LastName { get=>_lastName; set{if(_lastName==value)return;_lastName=value;NameChanged(nameof(LastName));} }
        private void NameChanged(string property)
        {PropertyChanged?.Invoke(this,new(property));PropertyChanged?.Invoke(this,new(nameof(FullName)));PropertyChanged?.Invoke(this,new(nameof(DisplayName)));}

        private bool _isWicketkeeper,_isCaptain;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public bool IsWicketkeeper { get=>_isWicketkeeper; set{if(_isWicketkeeper==value)return;_isWicketkeeper=value;PropertyChanged?.Invoke(this,new(nameof(IsWicketkeeper)));} }
        public bool IsCaptain { get=>_isCaptain; set{if(_isCaptain==value)return;_isCaptain=value;PropertyChanged?.Invoke(this,new(nameof(IsCaptain)));} }
        public int BattingPosition { get; set; }
        public int BattingEntryOrder { get; set; }
        public int BowlingEntryOrder { get; set; }
        public int RosterIndex { get; set; } = -1;
        public bool IsRetired { get; set; }
        public string? RetiredReason { get; set; }

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
                if (IsRetired) return RetiredReason ?? "retired not out";
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
                    DismissalType.ObstructingField => "obstructing the field",
                    DismissalType.HitBallTwice => "hit the ball twice",
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
                return $"{completed}.{remaining}";
            }
        }

        public double Economy => BallsBowled > 0
            ? Math.Round(6.0 * RunsConceded / BallsBowled, 2) : 0;

        public string BowlingFigures => $"{Wickets}-{RunsConceded}";

        public void ResetBatting()
        {
            BattingEntryOrder = 0;
            Runs = BallsFaced = Fours = Sixes = 0;
            IsOut = false;
            IsRetired = false; RetiredReason = null;
            DismissalText = null;
            HowOut = DismissalType.NotOut;
            DismissalBowler = null;
            DismissalFielder = null;
        }

        public void ResetBowling()
        {
            BowlingEntryOrder = 0;
            BallsBowled = RunsConceded = Wickets = Maidens = Wides = NoBalls = 0;
            LastOverBowledAt = -1;
        }

        public CricketPlayer Clone() => new()
        {
            FirstName = FirstName,
            LastName = LastName,
            IsWicketkeeper = IsWicketkeeper,
            IsCaptain = IsCaptain,
            BattingPosition = BattingPosition,
            BattingEntryOrder = BattingEntryOrder,
            BowlingEntryOrder = BowlingEntryOrder,
            RosterIndex = RosterIndex,
            IsRetired = IsRetired,
            RetiredReason = RetiredReason,
            Runs = Runs,
            BallsFaced = BallsFaced,
            Fours = Fours,
            Sixes = Sixes,
            IsOut = IsOut,
            DismissalText = DismissalText,
            HowOut = HowOut,
            DismissalBowler = DismissalBowler,
            DismissalFielder = DismissalFielder,
            BallsBowled = BallsBowled,
            RunsConceded = RunsConceded,
            Wickets = Wickets,
            Maidens = Maidens,
            Wides = Wides,
            NoBalls = NoBalls,
            LastOverBowledAt = LastOverBowledAt
        };
    }
}
