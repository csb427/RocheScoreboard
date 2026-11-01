using System;
using System.Collections.Generic;
using System.Linq;

namespace Roche_Scoreboard.Models
{
    public enum CricketFormat { LimitedOvers, MultiDay, MultiInnings }

    public enum CricketDeliveryType
    {
        Dot, Runs, Four, Six,
        Wide, NoBall, Bye, LegBye,
        Wicket
    }

    public enum CricketNoBallRuns { Bat, Byes, LegByes }
    public enum CricketWicketRuns { Bat, Wide, NoBall, Byes, LegByes }

    public sealed class CricketDelivery
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Innings { get; init; }
        public int OverNumber { get; init; }
        public int BallInOver { get; init; }
        public CricketDeliveryType Type { get; init; }
        public int Runs { get; init; }
        public int StrikerIndex { get; init; } = -1;
        public int NonStrikerIndex { get; init; } = -1;
        public int BowlerIndex { get; init; } = -1;
        public int DismissedBatterIndex { get; init; } = -1;
        public bool RunOutCrossed { get; init; }
        public bool? CountsInOver { get; init; }
        public bool WasFreeHit { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsCountedBall => CountsInOver ?? ScoringType is not CricketDeliveryType.Wide and not CricketDeliveryType.NoBall;
        public CricketNoBallRuns NoBallRuns { get; init; }
        public CricketWicketRuns WicketRuns { get; init; }
        /// <summary>Completed running, not a boundary allowance; retained when a winning run precedes a run out.</summary>
        public bool CompletedRunsOnly { get; init; }
        [System.Text.Json.Serialization.JsonIgnore]
        public CricketDeliveryType ScoringType => Type == CricketDeliveryType.Wicket ? WicketScoringType(WicketRuns) : Type;
        internal static CricketDeliveryType WicketScoringType(CricketWicketRuns source) => source switch
        {
            CricketWicketRuns.Wide => CricketDeliveryType.Wide,
            CricketWicketRuns.NoBall => CricketDeliveryType.NoBall,
            CricketWicketRuns.Byes => CricketDeliveryType.Bye,
            CricketWicketRuns.LegByes => CricketDeliveryType.LegBye,
            _ => CricketDeliveryType.Runs
        };
        public DismissalType Dismissal { get; init; } = DismissalType.NotOut;
        public string? Fielder { get; init; }
        public string? BatsmanName { get; init; }
        public string? BowlerName { get; init; }
        public string? DismissalText { get; init; }
        public bool IsExtra { get; init; }
        public int TotalAfter { get; init; }
        public int WicketsAfter { get; init; }
        public CricketInningsState? StateBefore { get; init; }

        public string Description => Type switch
        {
            CricketDeliveryType.Dot => "Dot ball",
            CricketDeliveryType.Four => "FOUR",
            CricketDeliveryType.Six => "SIX",
            CricketDeliveryType.Wide => $"Wide (+{Runs})",
            CricketDeliveryType.NoBall => Runs <= 1 ? "No ball"
                : $"No ball + {Runs - 1} {(NoBallRuns == CricketNoBallRuns.Bat ? "off bat" : NoBallRuns == CricketNoBallRuns.Byes ? Runs == 2 ? "bye" : "byes" : Runs == 2 ? "leg bye" : "leg byes")}",
            CricketDeliveryType.Bye => $"Bye (+{Runs})",
            CricketDeliveryType.LegBye => $"Leg bye (+{Runs})",
            CricketDeliveryType.Wicket => $"Wicket · {DismissalText}" + (Runs > 0 ? $" · +{Runs} {WicketRuns switch { CricketWicketRuns.Wide => "wides", CricketWicketRuns.NoBall => "incl. no-ball", CricketWicketRuns.Byes => "byes", CricketWicketRuns.LegByes => "leg-byes", _ => "runs" }}" : ""),
            _ => Runs == 1 ? "1 run" : $"{Runs} runs"
        };
    }

    public sealed class CricketInningsState
    {
        public int TotalRuns { get; init; }
        public int TotalWickets { get; init; }
        public int TotalExtras { get; init; }
        public int Wides { get; init; }
        public int NoBalls { get; init; }
        public int Byes { get; init; }
        public int LegByes { get; init; }
        public int LegalBallsBowled { get; init; }
        public int StrikerIndex { get; init; }
        public int NonStrikerIndex { get; init; }
        public int CurrentBowlerIndex { get; init; }
        public int PreviousBowlerIndex { get; init; }
        public int PartnershipRuns { get; init; }
        public int PartnershipBalls { get; init; }
        public bool IsDeclared { get; init; }
        public bool IsComplete { get; init; }
        public bool IgnoreTarget { get; init; }
        public bool IgnoreOverLimit { get; init; }
        public int ScorecardAnchor { get; init; } = -1;
        public List<string>? ManualFallOfWickets { get; init; }
        public bool NeedsBatterSelection { get; init; }
        public bool NeedsBowlerSelection { get; init; }
        public bool FreeHitPending { get; init; }
        public string ManualScorecardWarning { get; init; } = "";
        public List<string> CurrentOverBalls { get; init; } = [];
        public List<CricketPlayer> BattingOrder { get; init; } = [];
        public List<CricketPlayer> BowlingAttack { get; init; } = [];
    }

    public sealed class CricketInnings
    {
        public int InningsNumber { get; init; }
        public string BattingTeamName { get; set; } = "";
        public string BowlingTeamName { get; set; } = "";
        public List<CricketPlayer> BattingOrder { get; set; } = [];
        public List<CricketPlayer> BowlingAttack { get; set; } = [];

        public int TotalRuns { get; set; }
        public int TotalWickets { get; set; }
        public int TotalExtras { get; set; }
        public int Wides { get; set; }
        public int NoBalls { get; set; }
        public int Byes { get; set; }
        public int LegByes { get; set; }

        // Ball tracking
        public int LegalBallsBowled { get; set; }
        public int CompletedOvers => LegalBallsBowled / 6;
        public int BallsInCurrentOver => LegalBallsBowled % 6;
        public string OversDisplay => $"{CompletedOvers}.{BallsInCurrentOver}";

        // Current over balls for display
        public List<string> CurrentOverBalls { get; set; } = [];

        // Active batsmen indices into BattingOrder
        public int StrikerIndex { get; set; }
        public int NonStrikerIndex { get; set; }

        // Active bowler index into BowlingAttack
        public int CurrentBowlerIndex { get; set; }

        // Bowler rotation history (indices of last 2 bowlers for auto-suggest)
        public int PreviousBowlerIndex { get; set; } = -1;

        // Partnership
        public int PartnershipRuns { get; set; }
        public int PartnershipBalls { get; set; }

        public bool IsDeclared { get; set; }
        public bool IsAllOut => BattingOrder.Count >= 2 && TotalWickets >= BattingOrder.Count - 1;
        public bool IsComplete { get; set; }
        public bool IgnoreTarget { get; set; }
        public bool IgnoreOverLimit { get; set; }
        public int ScorecardAnchor { get; set; } = -1;
        public List<string>? ManualFallOfWickets { get; set; }
        public List<CricketScorecardChange> ScorecardChanges { get; set; } = [];
        public int? OverLimitOverride { get; set; }
        public int? TargetOverride { get; set; }
        public string AdjustmentNote { get; set; } = "";
        public int ConditionsDeliveryCount { get; set; }
        public bool NeedsBatterSelection { get; set; }
        public bool NeedsBowlerSelection { get; set; }
        public bool FreeHitPending { get; set; }
        public string ManualScorecardWarning { get; set; } = "";

        public CricketPlayer? Striker => StrikerIndex >= 0 && StrikerIndex < BattingOrder.Count
            ? BattingOrder[StrikerIndex] : null;
        public CricketPlayer? NonStriker => NonStrikerIndex >= 0 && NonStrikerIndex < BattingOrder.Count
            ? BattingOrder[NonStrikerIndex] : null;
        public CricketPlayer? CurrentBowler => CurrentBowlerIndex >= 0 && CurrentBowlerIndex < BowlingAttack.Count
            ? BowlingAttack[CurrentBowlerIndex] : null;

        public List<CricketDelivery> Deliveries { get; set; } = [];

        public double RunRate => LegalBallsBowled > 0
            ? Math.Round(6.0 * TotalRuns / LegalBallsBowled, 2) : 0;

        /// <summary>Number of consecutive legal deliveries without a boundary (4 or 6).</summary>
        public int BoundaryDroughtBalls
        {
            get
            {
                int count = 0;
                for (int i = Deliveries.Count - 1; i >= 0; i--)
                {
                    var d = Deliveries[i];
                    if (d.Type == CricketDeliveryType.Four || d.Type == CricketDeliveryType.Six)
                        break;
                    if (d.IsCountedBall)
                        count++;
                }
                return count;
            }
        }

        /// <summary>Runs scored in the last N completed overs (approximation from deliveries).</summary>
        public int RunsInLastOvers(int oversCount)
        {
            int ballsWanted = oversCount * 6;
            int runs = 0;
            int legalCounted = 0;
            for (int i = Deliveries.Count - 1; i >= 0 && legalCounted < ballsWanted; i--)
            {
                runs += Deliveries[i].Runs;
                if (Deliveries[i].IsCountedBall)
                    legalCounted++;
            }
            return runs;
        }

        /// <summary>Boundaries (4s + 6s) hit in this innings.</summary>
        public int TotalBoundaries => BattingOrder.Sum(p => p.Fours + p.Sixes);

        /// <summary>Dot ball percentage in this innings.</summary>
        public double DotBallPercentage
        {
            get
            {
                int legalBalls = Deliveries.Count(d =>
                    d.IsCountedBall);
                if (legalBalls == 0) return 0;
                int dots = Deliveries.Count(d => d.Type == CricketDeliveryType.Dot);
                return Math.Round(100.0 * dots / legalBalls, 1);
            }
        }
    }

    public sealed partial class CricketMatchManager
    {
        // Teams
        public string TeamAName { get; private set; } = "Team A";
        public string TeamAAbbr { get; private set; } = "TMA";
        public string TeamBName { get; private set; } = "Team B";
        public string TeamBAbbr { get; private set; } = "TMB";

        public string TeamAPrimaryColor { get; set; } = "#CC0000";
        public string TeamASecondaryColor { get; set; } = "#FFFFFF";
        public string TeamBPrimaryColor { get; set; } = "#000080";
        public string TeamBSecondaryColor { get; set; } = "#FFFFFF";

        public string? TeamALogoPath { get; set; }
        public string? TeamBLogoPath { get; set; }

        public List<CricketPlayer> TeamAPlayers { get; set; } = [];
        public List<CricketPlayer> TeamBPlayers { get; set; } = [];

        // Match format
        public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;
        public int TotalOvers { get; set; } = 50;
        public int TotalInnings { get; set; } = 2;

        // Match state
        public int CurrentInningsNumber { get; private set; } = 1;
        public List<CricketInnings> AllInnings { get; } = [];
        public CricketInnings? CurrentInnings => CurrentInningsNumber <= AllInnings.Count
            ? AllInnings[CurrentInningsNumber - 1] : null;

        public bool TeamABatsFirst { get; set; } = true;
        public string TossWinner { get; set; } = "";
        public bool TossPending { get; set; }
        public bool TossWinnerElectedToBat { get; set; } = true;
        public List<string> Messages { get; set; } = [];
        public string WeatherLocation { get; set; } = "Melbourne, Australia";

        // Events
        public event Action? MatchChanged;
        public event Action<CricketDelivery>? DeliveryAdded;
        /// <summary>Fired when a wicket falls and the UI should prompt for next batter.</summary>
        public event Action? BatterSelectionNeeded;
        /// <summary>Fired when an over is completed and the UI should prompt for next bowler.</summary>
        public event Action? BowlerSelectionNeeded;
        /// <summary>Fired when a batter reaches a milestone (50, 100, 150, 200). Args: player name, milestone value.</summary>
        public event Action<string, int>? MilestoneReached;

        // ---- Derived display properties ----

        public string BattingTeamName => CurrentInnings?.BattingTeamName ?? "";
        public string BowlingTeamName => CurrentInnings?.BowlingTeamName ?? "";

        public string BattingTeamAbbr
        {
            get
            {
                if (CurrentInnings == null) return "";
                return CurrentInnings.BattingTeamName == TeamAName ? TeamAAbbr : TeamBAbbr;
            }
        }

        public string BattingTeamPrimaryColor =>
            CurrentInnings?.BattingTeamName == TeamAName ? TeamAPrimaryColor : TeamBPrimaryColor;
        public string BattingTeamSecondaryColor =>
            CurrentInnings?.BattingTeamName == TeamAName ? TeamASecondaryColor : TeamBSecondaryColor;
        public string BowlingTeamPrimaryColor =>
            CurrentInnings?.BowlingTeamName == TeamAName ? TeamAPrimaryColor : TeamBPrimaryColor;
        public string BowlingTeamSecondaryColor =>
            CurrentInnings?.BowlingTeamName == TeamAName ? TeamASecondaryColor : TeamBSecondaryColor;

        public int TotalRuns => CurrentInnings?.TotalRuns ?? 0;
        public int TotalWickets => CurrentInnings?.TotalWickets ?? 0;
        public string OversDisplay => CurrentInnings?.OversDisplay ?? "0.0";
        public string ScoreDisplay => $"{TotalWickets}/{TotalRuns}";

        public int? Target
        {
            get
            {
                if (CurrentInnings == null) return null;
                if (CurrentInnings.TargetOverride is int revisedTarget) return revisedTarget;

                if (Format == CricketFormat.LimitedOvers && AllInnings.Count <= 2)
                {
                    // Target only in 2nd innings
                    if (CurrentInningsNumber <= 1) return null;
                    return AllInnings[0].TotalRuns + 1;
                }

                // Multi-day: target only in the final (4th) innings
                if (CurrentInningsNumber < TotalInnings) return null;

                // Sum runs for each team across previous innings
                string battingTeamName = CurrentInnings.BattingTeamName;
                int opponentTotal = 0, battingPrevTotal = 0;
                for (int i = 0; i < AllInnings.Count - 1; i++)
                {
                    if (AllInnings[i].BattingTeamName == battingTeamName)
                        battingPrevTotal += AllInnings[i].TotalRuns;
                    else
                        opponentTotal += AllInnings[i].TotalRuns;
                }
                return opponentTotal - battingPrevTotal + 1;
            }
        }

        public int? RunsRequired
        {
            get
            {
                var t = Target;
                if (t == null || CurrentInnings == null) return null;
                int needed = t.Value - CurrentInnings.TotalRuns;
                return needed > 0 ? needed : 0;
            }
        }

        public double? RequiredRunRate
        {
            get
            {
                if (Format != CricketFormat.LimitedOvers || CurrentInnings == null || Target == null)
                    return null;
                int totalBalls = CurrentOverLimit * 6;
                int ballsRemaining = totalBalls - CurrentInnings.LegalBallsBowled;
                if (ballsRemaining <= 0) return null;
                int needed = Target.Value - CurrentInnings.TotalRuns;
                if (needed <= 0) return 0;
                return Math.Round(6.0 * needed / ballsRemaining, 2);
            }
        }

        public double RunRate => CurrentInnings?.RunRate ?? 0;

        /// <summary>Balls remaining in the innings (limited overs only).</summary>
        public int? BallsRemaining
        {
            get
            {
                if (Format != CricketFormat.LimitedOvers || CurrentInnings == null) return null;
                return Math.Max(0, CurrentOverLimit * 6 - CurrentInnings.LegalBallsBowled);
            }
        }

        /// <summary>Overs remaining as a display string (limited overs only).</summary>
        public string? OversRemainingDisplay
        {
            get
            {
                var br = BallsRemaining;
                if (br == null) return null;
                int fullOvers = br.Value / 6;
                int balls = br.Value % 6;
                return $"{fullOvers}.{balls}";
            }
        }

        /// <summary>Opening field-restriction overs, scaled for common short and one-day formats.</summary>
        public int PowerplayOvers => CurrentOverLimit switch
        {
            <= 10 => Math.Max(1, (int)Math.Ceiling(CurrentOverLimit * 0.30)),
            <= 20 => 6,
            <= 40 => 8,
            _ => 10
        };

        /// <summary>True while the limited-overs innings is in its opening powerplay.</summary>
        public bool IsInPowerplay =>
            Format == CricketFormat.LimitedOvers
            && CurrentInnings != null
            && CurrentInnings.CompletedOvers < PowerplayOvers;

        /// <summary>For multi-day: how many runs the batting team leads or trails by.
        /// Positive = lead, negative = trail. Null if first innings.</summary>
        public int? LeadTrailRuns
        {
            get
            {
                if (CurrentInningsNumber <= 1 || CurrentInnings == null) return null;
                // Sum runs for each team across all innings
                int battingTeamTotal = 0, bowlingTeamTotal = 0;
                string battingTeamName = CurrentInnings.BattingTeamName;
                foreach (var inn in AllInnings)
                {
                    if (inn.BattingTeamName == battingTeamName)
                        battingTeamTotal += inn.TotalRuns;
                    else
                        bowlingTeamTotal += inn.TotalRuns;
                }
                return battingTeamTotal - bowlingTeamTotal;
            }
        }

        /// <summary>Display string like "LEAD BY 45" or "TRAIL BY 12".</summary>
        public string? LeadTrailDisplay
        {
            get
            {
                var lt = LeadTrailRuns;
                if (lt == null) return null;
                if (lt > 0) return $"LEAD BY {lt}";
                if (lt < 0) return $"TRAIL BY {Math.Abs(lt.Value)}";
                return "SCORES LEVEL";
            }
        }

        /// <summary>Get the most recent completed innings by the opposing team (for display).
        /// Returns (teamAbbr, score display) or null if none.</summary>
        public (string Label, string Score)? OpponentPreviousInningsScore
        {
            get
            {
                if (CurrentInnings == null || CurrentInningsNumber <= 1) return null;
                string bowlingTeam = CurrentInnings.BowlingTeamName;
                // Find the most recent completed innings by the bowling team
                for (int i = AllInnings.Count - 2; i >= 0; i--)
                {
                    if (AllInnings[i].BattingTeamName == bowlingTeam)
                    {
                        var prev = AllInnings[i];
                        string abbr = bowlingTeam == TeamAName ? TeamAAbbr : TeamBAbbr;
                        int innNum = 0;
                        // Count which innings of that team this was
                        for (int j = 0; j <= i; j++)
                            if (AllInnings[j].BattingTeamName == bowlingTeam) innNum++;
                        string ordinal = innNum == 1 ? "1ST" : innNum == 2 ? "2ND" : $"{innNum}TH";
                        return ($"{abbr} {ordinal} INN", $"{prev.TotalWickets}/{prev.TotalRuns}");
                    }
                }
                return null;
            }
        }

        // ---- Setup ----

        public void SetTeams(string teamAName, string teamAAbbr,
                             string teamBName, string teamBAbbr)
        {
            string oldA=TeamAName,oldB=TeamBName;
            TeamAName = string.IsNullOrWhiteSpace(teamAName) ? "Team A" : teamAName.Trim();
            TeamAAbbr = string.IsNullOrWhiteSpace(teamAAbbr) ? "TMA" : teamAAbbr.Trim();
            TeamBName = string.IsNullOrWhiteSpace(teamBName) ? "Team B" : teamBName.Trim();
            TeamBAbbr = string.IsNullOrWhiteSpace(teamBAbbr) ? "TMB" : teamBAbbr.Trim();
            if(string.Equals(TeamAName,TeamBName,StringComparison.OrdinalIgnoreCase)) TeamBName+=" II";
            foreach(var innings in AllInnings)
            {
                innings.BattingTeamName=innings.BattingTeamName==oldA ? TeamAName : innings.BattingTeamName==oldB ? TeamBName : innings.BattingTeamName;
                innings.BowlingTeamName=innings.BowlingTeamName==oldA ? TeamAName : innings.BowlingTeamName==oldB ? TeamBName : innings.BowlingTeamName;
            }
            if(TossWinner==oldA) TossWinner=TeamAName; else if(TossWinner==oldB) TossWinner=TeamBName;
            if(AllInnings.Count>0 && (oldA!=TeamAName || oldB!=TeamBName)) DeliveryRevision++;
            MatchChanged?.Invoke();
        }

        public bool CompleteToss(bool teamAWon, bool electedToBat)
        {
            if (IsMatchFinalized || !TossPending || CurrentInningsNumber != 1 || AllInnings.Any(i => i.Deliveries.Count > 0 || i.TotalRuns > 0 || i.TotalWickets > 0 || i.LegalBallsBowled > 0)) return false;
            TossWinner = teamAWon ? TeamAName : TeamBName; TossWinnerElectedToBat = electedToBat;
            TeamABatsFirst = teamAWon == electedToBat;
            // No players may be selected while pending: safely create the correctly ordered innings once.
            AllInnings.Clear(); StartNewInnings(); TossPending = false;
            MatchChanged?.Invoke(); return true;
        }

        public void StartMatch()
        {
            ResetWorkflowState();
            foreach(var roster in new[] { TeamAPlayers,TeamBPlayers })
                for(int i=0;i<roster.Count;i++) { roster[i].RosterIndex=i; if(roster[i].BattingPosition<=0) roster[i].BattingPosition=i+1; }
            AllInnings.Clear();
            CurrentInningsNumber = 1;
            StartNewInnings();
            MatchChanged?.Invoke();
        }

        private void StartNewInnings()
        {
            _conditionsVersion++;
            BreakKind = CricketBreakKind.None;
            BreakNote = "";
            bool teamABats;
            if (CurrentInningsNumber == 1)
                teamABats = TeamABatsFirst;
            else if (CurrentInningsNumber == 2)
                teamABats = !TeamABatsFirst;
            else
                teamABats = (CurrentInningsNumber % 2 == 1) == TeamABatsFirst;
            if(_nextBattingTeamOverride is bool selected) { teamABats=selected; _nextBattingTeamOverride=null; }
            if(AllInnings.Count(i=>i.BattingTeamName==(teamABats ? TeamAName : TeamBName))>=2) teamABats=!teamABats;

            var battingPlayers = teamABats ? TeamAPlayers : TeamBPlayers;
            var bowlingPlayers = teamABats ? TeamBPlayers : TeamAPlayers;

            var innings = new CricketInnings
            {
                InningsNumber = CurrentInningsNumber,
                BattingTeamName = teamABats ? TeamAName : TeamBName,
                BowlingTeamName = teamABats ? TeamBName : TeamAName,
                // Every innings owns its statistical player objects. Reusing the
                // setup roster objects caused a new innings to erase the old scorecard.
                BattingOrder = battingPlayers.OrderBy(p=>p.BattingPosition).Select(p =>
                {
                    var copy = p.Clone();
                    copy.ResetBatting();
                    copy.ResetBowling();
                    return copy;
                }).ToList(),
                BowlingAttack = bowlingPlayers.OrderBy(p=>p.BattingPosition).Select(p =>
                {
                    var copy = p.Clone();
                    copy.ResetBatting();
                    copy.ResetBowling();
                    return copy;
                }).ToList(),
                StrikerIndex = -1,
                NonStrikerIndex = -1,
                CurrentBowlerIndex = -1,
                NeedsBatterSelection = true,
                NeedsBowlerSelection = true
            };

            AllInnings.Add(innings);
        }

        // ---- Batter/bowler selection ----

        /// <summary>Set the striker by index into BattingOrder.</summary>
        public void SetStriker(int index)
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || inn.IsComplete || index < 0 || index >= inn.BattingOrder.Count) return;
            if (inn.BattingOrder[index].IsOut || index == inn.NonStrikerIndex) return;
            inn.StrikerIndex = index;
            MarkBattingEntry(inn,index);
            ClearRetirementState(inn.BattingOrder[index]);
            RefreshBatterSelectionState(inn);
            MatchChanged?.Invoke();
        }

        /// <summary>Set the non-striker by index into BattingOrder.</summary>
        public void SetNonStriker(int index)
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || inn.IsComplete || index < 0 || index >= inn.BattingOrder.Count) return;
            if (inn.BattingOrder[index].IsOut || index == inn.StrikerIndex) return;
            inn.NonStrikerIndex = index;
            MarkBattingEntry(inn,index);
            ClearRetirementState(inn.BattingOrder[index]);
            RefreshBatterSelectionState(inn);
            MatchChanged?.Invoke();
        }

        private static void RefreshBatterSelectionState(CricketInnings inn)
        {
            inn.NeedsBatterSelection = !inn.IsComplete &&
                (inn.Striker is null || inn.NonStriker is null || inn.Striker.IsOut
                 || inn.NonStriker.IsOut || inn.StrikerIndex == inn.NonStrikerIndex);
        }

        private static void MarkBattingEntry(CricketInnings innings,int index)
        {
            if(index<0 || index>=innings.BattingOrder.Count)return;
            var player=innings.BattingOrder[index];
            if(player.BattingEntryOrder>0)return;
            foreach(var ball in innings.Deliveries)
                foreach(int actor in new[]{ball.StateBefore?.StrikerIndex??ball.StrikerIndex,ball.StateBefore?.NonStrikerIndex??ball.NonStrikerIndex})
                    if(actor>=0 && actor<innings.BattingOrder.Count && innings.BattingOrder[actor].BattingEntryOrder<=0)
                        innings.BattingOrder[actor].BattingEntryOrder=innings.BattingOrder.Max(p=>p.BattingEntryOrder)+1;
            if(player.BattingEntryOrder<=0)player.BattingEntryOrder=innings.BattingOrder.Max(p=>p.BattingEntryOrder)+1;
        }

        private static void ClearRetirementState(CricketPlayer player)
        {
            player.IsRetired = false; player.RetiredReason = null;
            if (player.HowOut is DismissalType.RetiredHurt or DismissalType.RetiredOut)
                player.HowOut = DismissalType.NotOut;
        }

        private static void RepairBowlerSelection(CricketInnings innings)
        {
            if (innings.IsComplete) { innings.NeedsBowlerSelection = false; return; }
            if (innings.CurrentBowler is null) { innings.NeedsBowlerSelection = true; return; }
            // Physical deliveries include wides/no-balls. Six display circles
            // cannot end an over which has fewer than six legal deliveries.
            if (innings.LegalBallsBowled == 0 || innings.BallsInCurrentOver != 0
                || innings.Deliveries.LastOrDefault()?.OverNumber == innings.CompletedOvers)
                innings.NeedsBowlerSelection = false;
        }

        /// <summary>Replace the dismissed slot, including a wicket on ball six.</summary>
        public void SelectNewBatter(int index)
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || index < 0 || index >= inn.BattingOrder.Count) return;
            if (!inn.NeedsBatterSelection || inn.IsComplete || inn.BattingOrder[index].IsOut
                || index == inn.StrikerIndex || index == inn.NonStrikerIndex) return;
            if (inn.Striker is null || inn.Striker.IsOut)
                inn.StrikerIndex = index;
            else if (inn.NonStriker is null || inn.NonStriker.IsOut)
                inn.NonStrikerIndex = index;
            else
                return;
            RefreshBatterSelectionState(inn);
            // Reset partnership for new pair
            MarkBattingEntry(inn,index);
            ClearRetirementState(inn.BattingOrder[index]);
            RepairBowlerSelection(inn);
            inn.PartnershipRuns = 0;
            inn.PartnershipBalls = 0;
            MatchChanged?.Invoke();
            if (inn.NeedsBowlerSelection)
                BowlerSelectionNeeded?.Invoke();
        }

        /// <summary>Confirm bowler selection (end-of-over prompt).</summary>
        public void ConfirmBowler(int index)
        {
            var inn = CurrentInnings;
            if (inn == null || index < 0 || index >= inn.BowlingAttack.Count) return;
            if (!CanSelectBowler(index)) return;
            inn.PreviousBowlerIndex = inn.CurrentBowlerIndex;
            inn.CurrentBowlerIndex = index;
            if(inn.BowlingAttack[index].BowlingEntryOrder<=0)
                inn.BowlingAttack[index].BowlingEntryOrder=inn.BowlingAttack.Max(player=>player.BowlingEntryOrder)+1;
            if (inn.NeedsBowlerSelection) inn.CurrentOverBalls.Clear();
            inn.NeedsBowlerSelection = false;
            MatchChanged?.Invoke();
        }

        public bool CanSelectBowler(int index)
        {
            var innings = CurrentInnings;
            if (TossPending || IsMatchFinalized || innings is null || innings.IsComplete || index < 0 || index >= innings.BowlingAttack.Count) return false;
            if (!innings.NeedsBowlerSelection || innings.LegalBallsBowled == 0) return true;
            int previousOver = innings.CompletedOvers - 1;
            return !innings.Deliveries.Any(delivery => delivery.OverNumber == previousOver
                && (delivery.BowlerIndex == index || (delivery.BowlerIndex < 0 && delivery.BowlerName == innings.BowlingAttack[index].DisplayName)));
        }

        /// <summary>Get the suggested next bowler (auto-rotation: alternates between last 2).</summary>
        public int SuggestedNextBowler()
        {
            var inn = CurrentInnings;
            if (inn == null || inn.BowlingAttack.Count == 0) return -1;
            if (inn.PreviousBowlerIndex >= 0 && CanSelectBowler(inn.PreviousBowlerIndex)) return inn.PreviousBowlerIndex;
            return Enumerable.Range(0, inn.BowlingAttack.Count).FirstOrDefault(CanSelectBowler, -1);
        }

        // ---- Scoring ----

        public void RecordDelivery(int runs, CricketDeliveryType type, string? dismissalText = null,
            CricketNoBallRuns noBallRuns = CricketNoBallRuns.Bat, DismissalType dismissal = DismissalType.Bowled,
            string? fielder = null, int? dismissedBatterIndex = null, DateTime? timestamp = null,
            CricketWicketRuns wicketRuns = CricketWicketRuns.Bat, bool completedRunsOnly = false, bool runOutCrossed = false,
            bool? countsInOverOverride = null)
        {
            var inn = CurrentInnings;
            LastOperationError = "";
            if (inn == null || inn.IsComplete || IsMatchFinalized || BreakKind!=CricketBreakKind.None || runs < 0 || runs > 999 || !Enum.IsDefined(type) || !Enum.IsDefined(noBallRuns) || !Enum.IsDefined(wicketRuns)) return;
            if (type == CricketDeliveryType.Wicket && (!Enum.IsDefined(dismissal) || dismissal is DismissalType.NotOut or DismissalType.RetiredHurt)) return;
            if (type == CricketDeliveryType.Dot) runs = 0;
            if (type == CricketDeliveryType.Four) runs = 4;
            if (type == CricketDeliveryType.Six) runs = 6;
            if (type is CricketDeliveryType.Wide or CricketDeliveryType.NoBall)
                runs = Math.Max(1, runs);

            // Block scoring if batsmen or bowler not yet selected
            RepairBowlerSelection(inn);
            if (TossPending || inn.Striker is null || inn.NonStriker is null || inn.CurrentBowler is null
                || inn.StrikerIndex == inn.NonStrikerIndex
                || inn.NeedsBatterSelection || inn.NeedsBowlerSelection
                || inn.Striker?.IsOut == true || inn.NonStriker?.IsOut == true) return;

            if (type == CricketDeliveryType.Wicket && dismissedBatterIndex is int outIndex
                && outIndex != inn.StrikerIndex && outIndex != inn.NonStrikerIndex) return;
            if (dismissalText?.StartsWith("run out", StringComparison.OrdinalIgnoreCase) == true) dismissal = DismissalType.RunOut;
            if (type == CricketDeliveryType.Wicket && !IsDismissalAllowedForDelivery(dismissal, wicketRuns, FreeHitsEnabled && inn.FreeHitPending))
            {
                LastOperationError = FreeHitsEnabled && inn.FreeHitPending && !IsDismissalAllowedOnFreeHit(dismissal)
                    ? "That dismissal is not allowed on a free hit. Record the delivery outcome without a wicket."
                    : "That dismissal is not allowed for this delivery outcome. Choose the correct outcome, or record a retirement from the player menu.";
                return;
            }
            if (type == CricketDeliveryType.Wicket && dismissal is not DismissalType.RunOut and not DismissalType.Other and not DismissalType.ObstructingField
                && dismissedBatterIndex is int victimIndex && victimIndex != inn.StrikerIndex) return;
            // Stumping/hit wicket on a wide retains its penalty; a caught or
            // bowled wicket has no running runs. Run-outs/obstruction retain
            // the completed runs supplied by the operator.
            if (type == CricketDeliveryType.Wicket && dismissal is not DismissalType.RunOut and not DismissalType.ObstructingField)
                runs = wicketRuns is CricketWicketRuns.Wide or CricketWicketRuns.NoBall ? 1 : 0;
            var scoringType = type == CricketDeliveryType.Wicket ? CricketDelivery.WicketScoringType(wicketRuns) : type;
            if (scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall) runs = Math.Max(1, runs);
            bool completedOnly = type == CricketDeliveryType.Wicket && dismissal is DismissalType.RunOut or DismissalType.ObstructingField
                || completedRunsOnly && type is CricketDeliveryType.Runs or CricketDeliveryType.Wide or CricketDeliveryType.NoBall or CricketDeliveryType.Bye or CricketDeliveryType.LegBye;
            // MCC 16.6.1: the result ends the match at the winning run. A
            // wide/no-ball penalty can win before any batting, running or run
            // out; completed runs that win likewise precede a later run out.
            // Genuine boundary events retain the full allowance (16.6.3).
            if (!inn.IgnoreTarget && Target is int winningTarget && inn.TotalRuns < winningTarget)
            {
                int required = winningTarget - inn.TotalRuns;
                bool penaltyWins = required == 1 && scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall;
                bool boundaryAllowance = type is CricketDeliveryType.Four or CricketDeliveryType.Six
                    || !completedOnly && (scoringType == CricketDeliveryType.Wide && runs == 5
                        || scoringType == CricketDeliveryType.NoBall && (runs == 5 || runs == 7 && noBallRuns == CricketNoBallRuns.Bat)
                        || scoringType is CricketDeliveryType.Bye or CricketDeliveryType.LegBye && runs == 4);
                bool completedRunsWin = runs >= required && !boundaryAllowance;
                if (penaltyWins || completedRunsWin)
                {
                    runs = penaltyWins ? 1 : required;
                    type = scoringType;
                    wicketRuns = CricketWicketRuns.Bat;
                    dismissal = DismissalType.NotOut; dismissalText = null; fielder = null; dismissedBatterIndex = null;
                    if (penaltyWins) noBallRuns = CricketNoBallRuns.Bat;
                }
            }
            bool isLegal = _replaying && countsInOverOverride is bool recordedCount ? recordedCount : CountsInOver(scoringType);
            bool wasFreeHit = FreeHitsEnabled && inn.FreeHitPending;
            bool isExtra = scoringType is CricketDeliveryType.Wide or CricketDeliveryType.NoBall or CricketDeliveryType.Bye or CricketDeliveryType.LegBye;

            var striker = inn.Striker;
            var bowler = inn.CurrentBowler;

            MarkBattingEntry(inn,inn.StrikerIndex);MarkBattingEntry(inn,inn.NonStrikerIndex);
            if(bowler is not null && bowler.BowlingEntryOrder<=0)
                bowler.BowlingEntryOrder=inn.BowlingAttack.Max(player=>player.BowlingEntryOrder)+1;

            var stateBefore = CaptureState(inn);
            var dismissedPlayer = type == CricketDeliveryType.Wicket ? inn.BattingOrder[dismissedBatterIndex ?? inn.StrikerIndex] : null;

            // Apply runs
            inn.TotalRuns += runs;

            int batterRunsCredited = 0;
            if (isExtra)
            {
                switch (scoringType)
                {
                    case CricketDeliveryType.Wide:
                        inn.TotalExtras += runs;
                        inn.Wides += runs;
                        if (bowler != null) { bowler.RunsConceded += runs; bowler.Wides += runs; }
                        break;
                    case CricketDeliveryType.NoBall:
                        // The first run is the no-ball penalty. Additional runs
                        // selected by the quick scorer are runs off the bat.
                        int noBallPenalty = runs > 0 ? 1 : 0;
                        int additional = Math.Max(0, runs - noBallPenalty);
                        batterRunsCredited = noBallRuns == CricketNoBallRuns.Bat ? additional : 0;
                        inn.TotalExtras += noBallPenalty + (noBallRuns == CricketNoBallRuns.Bat ? 0 : additional);
                        if (noBallRuns == CricketNoBallRuns.Byes) inn.Byes += additional;
                        if (noBallRuns == CricketNoBallRuns.LegByes) inn.LegByes += additional;
                        inn.NoBalls += noBallPenalty;
                        if (bowler != null) { bowler.RunsConceded += noBallPenalty + batterRunsCredited; bowler.NoBalls += noBallPenalty; }
                        if (batterRunsCredited > 0 && striker != null)
                        {
                            striker.Runs += batterRunsCredited;
                            if (!completedOnly && batterRunsCredited == 4) striker.Fours++;
                            else if (!completedOnly && batterRunsCredited == 6) striker.Sixes++;
                        }
                        break;
                    case CricketDeliveryType.Bye:
                        inn.TotalExtras += runs;
                        inn.Byes += runs;
                        break;
                    case CricketDeliveryType.LegBye:
                        inn.TotalExtras += runs;
                        inn.LegByes += runs;
                        break;
                }
            }
            else
            {
                if (striker != null)
                {
                    striker.Runs += runs;
                    batterRunsCredited = runs;
                    if (type == CricketDeliveryType.Four) striker.Fours++;
                    else if (type == CricketDeliveryType.Six) striker.Sixes++;
                }
                if (bowler != null) bowler.RunsConceded += runs;
            }

            // Capture over/ball before incrementing for the delivery record
            int deliveryOver = inn.CompletedOvers;
            int deliveryBall = inn.BallsInCurrentOver + 1; // 1-indexed ball within over

            // Legal ball tracking
            if (isLegal)
            {
                inn.LegalBallsBowled++;
                if (striker != null && scoringType != CricketDeliveryType.Wide) striker.BallsFaced++;
                if (bowler != null) bowler.BallsBowled++;
                inn.PartnershipBalls++;

                string ballDisplay = type switch
                {
                    CricketDeliveryType.Wicket => scoringType == CricketDeliveryType.Wide ? $"W+{runs}wd"
                        : scoringType == CricketDeliveryType.NoBall ? $"W+{runs}nb"
                        : runs == 0 ? "W" : scoringType == CricketDeliveryType.Bye ? $"W+{runs}b" : scoringType == CricketDeliveryType.LegBye ? $"W+{runs}lb" : $"W+{runs}",
                    CricketDeliveryType.Four => "4",
                    CricketDeliveryType.Six => "6",
                    CricketDeliveryType.Dot => "•",
                    CricketDeliveryType.Wide => $"{runs}wd",
                    CricketDeliveryType.NoBall => noBallRuns == CricketNoBallRuns.Bat || runs == 1 ? $"{runs}nb" : $"nb+{runs - 1}{(noBallRuns == CricketNoBallRuns.Byes ? "b" : "lb")}",
                    CricketDeliveryType.Bye => $"{runs}b",
                    CricketDeliveryType.LegBye => $"{runs}lb",
                    _ => runs.ToString()
                };
                inn.CurrentOverBalls.Add(ballDisplay);
            }
            else
            {
                // A no-ball is not a legal ball for the over or bowler, but it
                // is a delivery faced by the striker. Wides are not balls faced.
                if (scoringType == CricketDeliveryType.NoBall && striker != null)
                    striker.BallsFaced++;
                string ballDisplay = scoringType switch
                {
                    CricketDeliveryType.Wide => $"{runs}wd",
                    CricketDeliveryType.NoBall => noBallRuns == CricketNoBallRuns.Bat || runs == 1 ? $"{runs}nb" : $"nb+{runs - 1}{(noBallRuns == CricketNoBallRuns.Byes ? "b" : "lb")}",
                    _ => runs.ToString()
                };
                inn.CurrentOverBalls.Add(type == CricketDeliveryType.Wicket ? "W+" + ballDisplay : ballDisplay);
            }

            inn.PartnershipRuns += runs;

            // Handle wicket — mark out but don't auto-select next batter
            if (type == CricketDeliveryType.Wicket)
            {
                inn.TotalWickets++;
                if (dismissedPlayer != null)
                {
                    dismissedPlayer.IsOut = true;
                    dismissedPlayer.DismissalText = dismissalText;
                    dismissedPlayer.HowOut = dismissal;
                    dismissedPlayer.DismissalFielder = fielder;
                    dismissedPlayer.DismissalBowler = BowlerGetsWicket(dismissal) ? bowler?.DisplayName : null;
                }
                // Bowler gets wicket credit unless it's a run out
                if (bowler != null && BowlerGetsWicket(dismissal)) bowler.Wickets++;

                // Flag that batter selection is needed (unless all out)
                if (!inn.IsAllOut)
                    inn.NeedsBatterSelection = true;
            }

            // Create delivery record
            var delivery = new CricketDelivery
            {
                Timestamp = timestamp ?? DateTime.Now,
                Innings = inn.InningsNumber,
                OverNumber = deliveryOver,
                BallInOver = isLegal ? deliveryBall : 0,
                Type = type,
                Runs = runs,
                StrikerIndex = stateBefore.StrikerIndex,
                NonStrikerIndex = stateBefore.NonStrikerIndex,
                BowlerIndex = stateBefore.CurrentBowlerIndex,
                DismissedBatterIndex = type == CricketDeliveryType.Wicket ? dismissedBatterIndex ?? stateBefore.StrikerIndex : -1,
                RunOutCrossed = type == CricketDeliveryType.Wicket && dismissal is DismissalType.RunOut or DismissalType.ObstructingField && runOutCrossed,
                CountsInOver = isLegal,
                WasFreeHit = wasFreeHit,
                NoBallRuns = noBallRuns,
                WicketRuns = wicketRuns,
                CompletedRunsOnly = completedOnly,
                Dismissal = type == CricketDeliveryType.Wicket ? dismissal : DismissalType.NotOut,
                Fielder = fielder,
                BatsmanName = striker?.DisplayName,
                BowlerName = bowler?.DisplayName,
                DismissalText = dismissalText,
                IsExtra = isExtra,
                TotalAfter = inn.TotalRuns,
                WicketsAfter = inn.TotalWickets,
                StateBefore = stateBefore
            };
            inn.Deliveries.Add(delivery);
            inn.FreeHitPending = FreeHitsEnabled && (scoringType == CricketDeliveryType.NoBall
                || wasFreeHit && scoringType == CricketDeliveryType.Wide);
            _conditionsVersion++;
            if (type == CricketDeliveryType.Wicket && inn.ManualFallOfWickets is { } notes)
                notes.Add($"{inn.TotalWickets}-{inn.TotalRuns} ({inn.OversDisplay})");

            // Check for batter milestones (50, 100, 150, 200)
            if (!_replaying && striker != null && batterRunsCredited > 0 && type != CricketDeliveryType.Wicket)
            {
                int prevRuns = striker.Runs - batterRunsCredited;
                int[] milestones = [50, 100, 150, 200];
                foreach (int m in milestones)
                {
                    if (prevRuns < m && striker.Runs >= m)
                    {
                        MilestoneReached?.Invoke(striker.DisplayName, m);
                        break;
                    }
                }
            }

            // Rotate strike indicator on odd runs (don't reorder, just swap indices)
            int strikeRuns = scoringType switch
            {
                CricketDeliveryType.Wide => Math.Max(0, runs - 1),
                CricketDeliveryType.NoBall => Math.Max(0, runs - 1),
                _ => runs
            };
            if (strikeRuns % 2 == 1)
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
            // MCC18.12.1 also accounts for crossing on the uncompleted run.
            if (type == CricketDeliveryType.Wicket && dismissal is DismissalType.RunOut or DismissalType.ObstructingField && runOutCrossed)
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);

            // End of over
            if (isLegal && inn.BallsInCurrentOver == 0 && inn.LegalBallsBowled > 0)
            {
                // Check maiden
                if (bowler != null)
                {
                    var completed = inn.Deliveries.Where(d => d.OverNumber == deliveryOver).ToList();
                    if (completed.Count(d => d.IsCountedBall) == 6
                        && completed.All(d => d.BowlerIndex == stateBefore.CurrentBowlerIndex && BowlerRuns(d) == 0))
                        bowler.Maidens++;
                }

                // Swap strike at end of over
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
                // Keep the complete over visible until the next bowler is confirmed.

                // Track when this bowler last bowled
                if (bowler != null)
                    bowler.LastOverBowledAt = inn.CompletedOvers;

                // Flag bowler selection needed
                if (!inn.IsComplete && !inn.IsAllOut)
                    inn.NeedsBowlerSelection = true;
            }

            CheckInningsComplete(inn);

            if (_replaying) return;
            DeliveryAdded?.Invoke(delivery);
            MatchChanged?.Invoke();

            // Fire selection prompts after MatchChanged so UI is up to date
            if (type == CricketDeliveryType.Wicket && inn.NeedsBatterSelection)
                BatterSelectionNeeded?.Invoke();
            if (inn.NeedsBowlerSelection && !inn.NeedsBatterSelection)
                BowlerSelectionNeeded?.Invoke();
        }

        public void RotateStrike()
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || inn.IsComplete) return;
            (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
            MatchChanged?.Invoke();
        }

        public void SetCurrentBowler(int bowlerIndex)
        {
            var inn = CurrentInnings;
            if (inn == null) return;
            if (inn.BallsInCurrentOver != 0) return;
            if (bowlerIndex == inn.CurrentBowlerIndex) return;
            ConfirmBowler(bowlerIndex);
        }

        private void CheckInningsComplete(CricketInnings inn)
        {
            if (inn.IsAllOut
                || (!inn.IgnoreOverLimit && Format == CricketFormat.LimitedOvers && inn.LegalBallsBowled >= (inn.OverLimitOverride ?? TotalOvers) * 6)
                || (!inn.IgnoreTarget && Target is int target && inn.TotalRuns >= target))
            {
                inn.IsComplete = true;
                inn.NeedsBatterSelection = false;
                inn.NeedsBowlerSelection = false;
            }
        }

        public bool CanStartNextInnings() =>
            !TossPending && !IsMatchFinalized && CurrentInnings?.IsComplete == true && CurrentInningsNumber < TotalInnings;

        public void StartNextInnings()
        {
            if (!CanStartNextInnings()) return;
            CurrentInningsNumber++;
            StartNewInnings();
            MatchChanged?.Invoke();
        }

        public void DeclareInnings()
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || inn.IsComplete || Format == CricketFormat.LimitedOvers) return;
            inn.IsDeclared = true;
            inn.IsComplete = true;
            inn.NeedsBatterSelection = false;
            inn.NeedsBowlerSelection = false;
            MatchChanged?.Invoke();
        }

        public bool UndoLastDelivery()
        {
            var inn = CurrentInnings;
            LastOperationError="";
            if (inn == null || inn.Deliveries.Count == 0) return false;
            if(IsMatchFinalized) { LastOperationError="This match has been finalized. Use the scorecard to correct its totals."; return false; }
            if(inn.ScorecardAnchor>=0 && inn.Deliveries.Count<=inn.ScorecardAnchor)
            { LastOperationError="This ball predates a saved scorecard adjustment. Revert the latest scorecard edit before undoing earlier balls."; return false; }
            var delivery = inn.Deliveries[^1];
            // Older imports without an undo snapshot cannot be safely reversed.
            if (delivery.StateBefore is null) return false;
            inn.Deliveries.RemoveAt(inn.Deliveries.Count - 1);
            inn.ConditionsDeliveryCount = Math.Min(inn.ConditionsDeliveryCount, inn.Deliveries.Count);
            bool ignoreTarget = inn.IgnoreTarget, ignoreLimit = inn.IgnoreOverLimit;
            RestoreState(inn, delivery.StateBefore);
            inn.IgnoreTarget = ignoreTarget; inn.IgnoreOverLimit = ignoreLimit;
            // Counts alone cannot distinguish an old ball from a replacement
            // after Undo followed by re-scoring at the same ledger length.
            DeliveryRevision++;
            _conditionsVersion++;
            CheckInningsComplete(inn);
            MatchChanged?.Invoke();
            return true;
        }

        private static CricketInningsState CaptureState(CricketInnings inn) => new()
        {
            TotalRuns = inn.TotalRuns,
            TotalWickets = inn.TotalWickets,
            TotalExtras = inn.TotalExtras,
            Wides = inn.Wides,
            NoBalls = inn.NoBalls,
            Byes = inn.Byes,
            LegByes = inn.LegByes,
            LegalBallsBowled = inn.LegalBallsBowled,
            StrikerIndex = inn.StrikerIndex,
            NonStrikerIndex = inn.NonStrikerIndex,
            CurrentBowlerIndex = inn.CurrentBowlerIndex,
            PreviousBowlerIndex = inn.PreviousBowlerIndex,
            PartnershipRuns = inn.PartnershipRuns,
            PartnershipBalls = inn.PartnershipBalls,
            IsDeclared = inn.IsDeclared,
            IsComplete = inn.IsComplete,
            IgnoreTarget=inn.IgnoreTarget,
            IgnoreOverLimit=inn.IgnoreOverLimit,
            ScorecardAnchor=inn.ScorecardAnchor,
            ManualFallOfWickets=inn.ManualFallOfWickets is null ? null : [.. inn.ManualFallOfWickets],
            NeedsBatterSelection = inn.NeedsBatterSelection,
            NeedsBowlerSelection = inn.NeedsBowlerSelection,
            FreeHitPending = inn.FreeHitPending,
            ManualScorecardWarning = inn.ManualScorecardWarning,
            CurrentOverBalls = [.. inn.CurrentOverBalls],
            BattingOrder = inn.BattingOrder.Select(p => p.Clone()).ToList(),
            BowlingAttack = inn.BowlingAttack.Select(p => p.Clone()).ToList()
        };

        private static void RestoreState(CricketInnings inn, CricketInningsState state)
        {
            inn.TotalRuns = state.TotalRuns;
            inn.TotalWickets = state.TotalWickets;
            inn.TotalExtras = state.TotalExtras;
            inn.Wides = state.Wides;
            inn.NoBalls = state.NoBalls;
            inn.Byes = state.Byes;
            inn.LegByes = state.LegByes;
            inn.LegalBallsBowled = state.LegalBallsBowled;
            inn.StrikerIndex = state.StrikerIndex;
            inn.NonStrikerIndex = state.NonStrikerIndex;
            inn.CurrentBowlerIndex = state.CurrentBowlerIndex;
            inn.PreviousBowlerIndex = state.PreviousBowlerIndex;
            inn.PartnershipRuns = state.PartnershipRuns;
            inn.PartnershipBalls = state.PartnershipBalls;
            inn.IsDeclared = state.IsDeclared;
            inn.IsComplete = state.IsComplete;
            inn.IgnoreTarget=state.IgnoreTarget;
            inn.IgnoreOverLimit=state.IgnoreOverLimit;
            inn.ScorecardAnchor=state.ScorecardAnchor;
            inn.ManualFallOfWickets=state.ManualFallOfWickets is null ? null : [.. state.ManualFallOfWickets];
            inn.NeedsBatterSelection = state.NeedsBatterSelection;
            inn.NeedsBowlerSelection = state.NeedsBowlerSelection;
            inn.FreeHitPending = state.FreeHitPending;
            inn.ManualScorecardWarning = state.ManualScorecardWarning;
            inn.CurrentOverBalls = [.. state.CurrentOverBalls];
            inn.BattingOrder = state.BattingOrder.Select(p => p.Clone()).ToList();
            inn.BowlingAttack = state.BowlingAttack.Select(p => p.Clone()).ToList();
            RepairBowlerSelection(inn);
        }

        // ---- Match result ----

        public string? MatchResult
        {
            get
            {
                if(IsMatchFinalized && FinalResult is not null) return FinalResult;
                if (AllInnings.Count < 2) return null;
                var lastInnings = AllInnings.Last();
                if (!lastInnings.IsComplete) return null;

                if (Format == CricketFormat.LimitedOvers && AllInnings.Count == 2)
                {
                    // An early manual closure is not a result criterion. The
                    // competition/officials may instead declare no result.
                    bool naturalFinish = lastInnings.IsAllOut
                        || !lastInnings.IgnoreOverLimit && lastInnings.LegalBallsBowled >= (lastInnings.OverLimitOverride ?? TotalOvers) * 6
                        || !lastInnings.IgnoreTarget && Target is int target && lastInnings.TotalRuns >= target;
                    if (!naturalFinish) return null;
                    // An externally revised target replaces the first innings'
                    // par score for the chase result, without rewriting it.
                    int firstTotal = lastInnings.TargetOverride is int revisedTarget ? revisedTarget - 1 : AllInnings[0].TotalRuns;
                    int secondTotal = AllInnings[1].TotalRuns;

                    if (secondTotal > firstTotal)
                    {
                        int wktsRemaining = AllInnings[1].BattingOrder.Count - 1 - AllInnings[1].TotalWickets;
                        return $"{AllInnings[1].BattingTeamName} won by {wktsRemaining} wicket{(wktsRemaining != 1 ? "s" : "")}";
                    }
                    else if (firstTotal > secondTotal)
                    {
                        int margin = firstTotal - secondTotal;
                        return $"{AllInnings[0].BattingTeamName} won by {margin} run{(margin != 1 ? "s" : "")}";
                    }
                    return "Match tied";
                }

                // Multi-innings: an innings victory can finish after the third
                // innings; otherwise decide the result after the final chase.
                if (AllInnings.Count >= 3)
                {
                    int teamATotal = AllInnings.Where(i => i.BattingTeamName == TeamAName).Sum(i => i.TotalRuns);
                    int teamBTotal = AllInnings.Where(i => i.BattingTeamName == TeamBName).Sum(i => i.TotalRuns);

                    if (AllInnings.Count == 3 && lastInnings.IsComplete)
                    {
                        if (lastInnings.BattingTeamName == TeamAName && teamATotal < teamBTotal)
                            return $"{TeamBName} won by an innings and {teamBTotal - teamATotal} run{(teamBTotal - teamATotal == 1 ? "" : "s")}";
                        if (lastInnings.BattingTeamName == TeamBName && teamBTotal < teamATotal)
                            return $"{TeamAName} won by an innings and {teamATotal - teamBTotal} run{(teamATotal - teamBTotal == 1 ? "" : "s")}";
                    }

                    if (AllInnings.Count < TotalInnings)
                        return null;

                    if (teamATotal > teamBTotal)
                    {
                        int margin = teamATotal - teamBTotal;
                        // Check if it was won by wickets (last team batting won)
                        if (lastInnings.BattingTeamName == TeamAName)
                        {
                            int wkts = lastInnings.BattingOrder.Count - 1 - lastInnings.TotalWickets;
                            return $"{TeamAName} won by {wkts} wicket{(wkts != 1 ? "s" : "")}";
                        }
                        return $"{TeamAName} won by {margin} run{(margin != 1 ? "s" : "")}";
                    }
                    else if (teamBTotal > teamATotal)
                    {
                        int margin = teamBTotal - teamATotal;
                        if (lastInnings.BattingTeamName == TeamBName)
                        {
                            int wkts = lastInnings.BattingOrder.Count - 1 - lastInnings.TotalWickets;
                            return $"{TeamBName} won by {wkts} wicket{(wkts != 1 ? "s" : "")}";
                        }
                        return $"{TeamBName} won by {margin} run{(margin != 1 ? "s" : "")}";
                    }
                    // MCC 16.5.1: equal aggregates with the final innings all
                    // out are a tie, including a four-innings match. An early
                    // manual closure still needs an operator result decision.
                    return lastInnings.IsAllOut ? "Match tied" : "Match drawn";
                }

                return null;
            }
        }

        public void ResetForNewGame()
        {
            ResetWorkflowState();
            AllInnings.Clear();
            CurrentInningsNumber = 1;
            foreach (var p in TeamAPlayers) { p.ResetBatting(); p.ResetBowling(); }
            foreach (var p in TeamBPlayers) { p.ResetBatting(); p.ResetBowling(); }
            MatchChanged?.Invoke();
        }

        public void RaiseMatchChanged() => MatchChanged?.Invoke();

        public CricketSerializableState ToState() => new()
        {
            TeamAName = TeamAName,
            TeamAAbbr = TeamAAbbr,
            TeamBName = TeamBName,
            TeamBAbbr = TeamBAbbr,
            TeamAPrimaryColor = TeamAPrimaryColor,
            TeamASecondaryColor = TeamASecondaryColor,
            TeamBPrimaryColor = TeamBPrimaryColor,
            TeamBSecondaryColor = TeamBSecondaryColor,
            TeamALogoPath = TeamALogoPath,
            TeamBLogoPath = TeamBLogoPath,
            TeamAPlayers = TeamAPlayers.Select(p => p.Clone()).ToList(),
            TeamBPlayers = TeamBPlayers.Select(p => p.Clone()).ToList(),
            Format = Format,
            TotalOvers = TotalOvers,
            ReplaceWideBalls = ReplaceWideBalls,
            ReplaceNoBalls = ReplaceNoBalls,
            FreeHitsEnabled = FreeHitsEnabled,
            TotalInnings = TotalInnings,
            CurrentInningsNumber = CurrentInningsNumber,
            TeamABatsFirst = TeamABatsFirst,
            TossWinner = TossWinner,
            TossWinnerElectedToBat = TossWinnerElectedToBat,
            TossPending = TossPending,
            Messages = [.. Messages],
            WeatherLocation = WeatherLocation,
            BreakKind=BreakKind,
            BreakNote=BreakNote,
            IsMatchFinalized=IsMatchFinalized,
            FinalResult=FinalResult,
            ResultKind=ResultKind,
            ResultNote=ResultNote,
            ClosureBefore=ClosureBefore,
            AllInnings = [.. AllInnings]
        };

        public void LoadState(CricketSerializableState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            // A state replacement changes identity even when ball counts match.
            // Invalidate open editors and discard transient decisions from the
            // previous match without adding them to the recovery payload.
            DeliveryRevision++;
            _conditionsVersion++;
            LastOperationError = "";
            _nextBattingTeamOverride = null;
            TeamAName = state.TeamAName;
            TeamAAbbr = state.TeamAAbbr;
            TeamBName = state.TeamBName;
            TeamBAbbr = state.TeamBAbbr;
            TeamAPrimaryColor = state.TeamAPrimaryColor;
            TeamASecondaryColor = state.TeamASecondaryColor;
            TeamBPrimaryColor = state.TeamBPrimaryColor;
            TeamBSecondaryColor = state.TeamBSecondaryColor;
            TeamALogoPath = state.TeamALogoPath;
            TeamBLogoPath = state.TeamBLogoPath;
            TeamAPlayers = state.TeamAPlayers.Select(p => p.Clone()).ToList();
            TeamBPlayers = state.TeamBPlayers.Select(p => p.Clone()).ToList();
            Format = state.Format;
            TotalOvers = Math.Max(1, state.TotalOvers);
            ReplaceWideBalls = state.ReplaceWideBalls;
            ReplaceNoBalls = state.ReplaceNoBalls;
            FreeHitsEnabled = state.FreeHitsEnabled;
            TotalInnings = Math.Clamp(state.TotalInnings,2,4);
            TeamABatsFirst = state.TeamABatsFirst;
            TossWinner = state.TossWinner; TossWinnerElectedToBat = state.TossWinnerElectedToBat;
            TossPending = state.TossPending && state.CurrentInningsNumber == 1 && !state.AllInnings.Any(i => i.Deliveries.Count > 0 || i.TotalRuns > 0 || i.TotalWickets > 0 || i.LegalBallsBowled > 0);
            Messages = [.. state.Messages];
            WeatherLocation = state.WeatherLocation;
            BreakKind=Enum.IsDefined(state.BreakKind) ? state.BreakKind : CricketBreakKind.None;
            BreakNote=state.BreakNote ?? "";
            IsMatchFinalized=state.IsMatchFinalized;
            FinalResult=state.FinalResult;
            ResultKind=Enum.IsDefined(state.ResultKind) ? state.ResultKind : CricketMatchResultKind.Unspecified;
            ResultNote=state.ResultNote ?? "";
            ClosureBefore=state.ClosureBefore;
            AllInnings.Clear();
            AllInnings.AddRange(state.AllInnings);
            foreach(var roster in new[] { TeamAPlayers,TeamBPlayers })
                for(int i=0;i<roster.Count;i++) { roster[i].RosterIndex=i; if(roster[i].BattingPosition<=0) roster[i].BattingPosition=i+1; }
            foreach(var innings in AllInnings)
            {
                innings.ScorecardChanges ??= [];
                if (innings.OverLimitOverride is < 1 or > 1000) innings.OverLimitOverride = null;
                if (innings.TargetOverride is < 1 or > 1_000_000) innings.TargetOverride = null;
                innings.AdjustmentNote ??= "";
                innings.ManualScorecardWarning ??= "";
                innings.ConditionsDeliveryCount = Math.Clamp(innings.ConditionsDeliveryCount, 0, innings.Deliveries.Count);
                var battingRoster=innings.BattingTeamName==TeamAName ? TeamAPlayers : TeamBPlayers;
                var bowlingRoster=innings.BowlingTeamName==TeamAName ? TeamAPlayers : TeamBPlayers;
                if(innings.BattingOrder.All(p=>p.RosterIndex<0)) for(int i=0;i<innings.BattingOrder.Count;i++) { int found=battingRoster.FindIndex(p=>p.FullName==innings.BattingOrder[i].FullName); innings.BattingOrder[i].RosterIndex=found>=0 ? found : i; }
                if(innings.BowlingAttack.All(p=>p.RosterIndex<0)) for(int i=0;i<innings.BowlingAttack.Count;i++) { int found=bowlingRoster.FindIndex(p=>p.FullName==innings.BowlingAttack[i].FullName); innings.BowlingAttack[i].RosterIndex=found>=0 ? found : i; }
            }
            CurrentInningsNumber = Math.Clamp(state.CurrentInningsNumber, 1, Math.Max(1, AllInnings.Count));
            if (!IsMatchFinalized) ResetExceptionalState();
            else if (ClosureBefore?.InningsNumber != CurrentInningsNumber) ClosureBefore = null;
            if (CurrentInnings is { } current)
            {
                RefreshBatterSelectionState(current);
                RepairBowlerSelection(current);
                if (current.IsComplete) current.NeedsBowlerSelection = false;
                if (IsMatchFinalized) current.NeedsBatterSelection = current.NeedsBowlerSelection = false;
            }
            if (IsMatchFinalized && (ResultKind != CricketMatchResultKind.ScoreBased || FinalResult is null)) RefreshFinalizedResult();
            MatchChanged?.Invoke();
        }

        public void EndCurrentInnings()
        {
            var inn = CurrentInnings;
            if (TossPending || IsMatchFinalized || inn == null || inn.IsComplete) return;
            inn.IsComplete = true;
            inn.NeedsBatterSelection = false;
            inn.NeedsBowlerSelection = false;
            MatchChanged?.Invoke();
        }
    }

    public sealed class CricketSerializableState
    {
        public string TeamAName { get; set; } = "Team A";
        public string TeamAAbbr { get; set; } = "TMA";
        public string TeamBName { get; set; } = "Team B";
        public string TeamBAbbr { get; set; } = "TMB";
        public string TeamAPrimaryColor { get; set; } = "#CC0000";
        public string TeamASecondaryColor { get; set; } = "#FFFFFF";
        public string TeamBPrimaryColor { get; set; } = "#000080";
        public string TeamBSecondaryColor { get; set; } = "#FFFFFF";
        public string? TeamALogoPath { get; set; }
        public string? TeamBLogoPath { get; set; }
        public List<CricketPlayer> TeamAPlayers { get; set; } = [];
        public List<CricketPlayer> TeamBPlayers { get; set; } = [];
        public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;
        public int TotalOvers { get; set; } = 20;
        public bool ReplaceWideBalls { get; set; } = true;
        public bool ReplaceNoBalls { get; set; } = true;
        public bool FreeHitsEnabled { get; set; }
        public int TotalInnings { get; set; } = 2;
        public int CurrentInningsNumber { get; set; } = 1;
        public bool TeamABatsFirst { get; set; } = true;
        public string TossWinner { get; set; } = "";
        public bool TossPending { get; set; }
        public bool TossWinnerElectedToBat { get; set; } = true;
        public List<string> Messages { get; set; } = [];
        public string WeatherLocation { get; set; } = "Melbourne, Australia";
        public CricketBreakKind BreakKind { get; set; }
        public string BreakNote { get; set; } = "";
        public bool IsMatchFinalized { get; set; }
        public string? FinalResult { get; set; }
        public CricketMatchResultKind ResultKind { get; set; }
        public string ResultNote { get; set; } = "";
        public CricketMatchClosure? ClosureBefore { get; set; }
        public List<CricketInnings> AllInnings { get; set; } = [];
    }
}
