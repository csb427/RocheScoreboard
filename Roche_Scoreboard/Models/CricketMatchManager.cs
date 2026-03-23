using System;
using System.Collections.Generic;
using System.Linq;

namespace Roche_Scoreboard.Models
{
    public enum CricketFormat { LimitedOvers, MultiDay }

    public enum CricketDeliveryType
    {
        Dot, Runs, Four, Six,
        Wide, NoBall, Bye, LegBye,
        Wicket
    }

    public sealed class CricketDelivery
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public int Innings { get; init; }
        public int OverNumber { get; init; }
        public int BallInOver { get; init; }
        public CricketDeliveryType Type { get; init; }
        public int Runs { get; init; }
        public string? BatsmanName { get; init; }
        public string? BowlerName { get; init; }
        public string? DismissalText { get; init; }
        public bool IsExtra { get; init; }
        public int TotalAfter { get; init; }
        public int WicketsAfter { get; init; }

        public string Description => Type switch
        {
            CricketDeliveryType.Dot => "Dot ball",
            CricketDeliveryType.Four => "FOUR",
            CricketDeliveryType.Six => "SIX",
            CricketDeliveryType.Wide => $"Wide (+{Runs})",
            CricketDeliveryType.NoBall => $"No ball (+{Runs})",
            CricketDeliveryType.Bye => $"Bye (+{Runs})",
            CricketDeliveryType.LegBye => $"Leg bye (+{Runs})",
            CricketDeliveryType.Wicket => $"WICKET — {DismissalText}",
            _ => Runs == 1 ? "1 run" : $"{Runs} runs"
        };
    }

    public sealed class CricketInnings
    {
        public int InningsNumber { get; init; }
        public string BattingTeamName { get; set; } = "";
        public string BowlingTeamName { get; set; } = "";
        public List<CricketPlayer> BattingOrder { get; set; } = new();
        public List<CricketPlayer> BowlingAttack { get; set; } = new();

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
        public string OversDisplay => BallsInCurrentOver == 0
            ? CompletedOvers.ToString()
            : $"{CompletedOvers}.{BallsInCurrentOver}";

        // Current over balls for display
        public List<string> CurrentOverBalls { get; set; } = new();

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
        public bool IsAllOut => TotalWickets >= BattingOrder.Count - 1;
        public bool IsComplete { get; set; }
        public bool NeedsBatterSelection { get; set; }
        public bool NeedsBowlerSelection { get; set; }

        public CricketPlayer? Striker => StrikerIndex >= 0 && StrikerIndex < BattingOrder.Count
            ? BattingOrder[StrikerIndex] : null;
        public CricketPlayer? NonStriker => NonStrikerIndex >= 0 && NonStrikerIndex < BattingOrder.Count
            ? BattingOrder[NonStrikerIndex] : null;
        public CricketPlayer? CurrentBowler => CurrentBowlerIndex >= 0 && CurrentBowlerIndex < BowlingAttack.Count
            ? BowlingAttack[CurrentBowlerIndex] : null;

        public List<CricketDelivery> Deliveries { get; set; } = new();

        public double RunRate => LegalBallsBowled > 0
            ? Math.Round(6.0 * TotalRuns / LegalBallsBowled, 2) : 0;
    }

    public sealed class CricketMatchManager
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

        public List<CricketPlayer> TeamAPlayers { get; set; } = new();
        public List<CricketPlayer> TeamBPlayers { get; set; } = new();

        // Match format
        public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;
        public int TotalOvers { get; set; } = 50;
        public int TotalInnings { get; set; } = 2;

        // Match state
        public int CurrentInningsNumber { get; private set; } = 1;
        public List<CricketInnings> AllInnings { get; } = new();
        public CricketInnings? CurrentInnings => CurrentInningsNumber <= AllInnings.Count
            ? AllInnings[CurrentInningsNumber - 1] : null;

        public bool TeamABatsFirst { get; set; } = true;
        public List<string> Messages { get; set; } = new();

        // Events
        public event Action? MatchChanged;
        public event Action<CricketDelivery>? DeliveryAdded;
        /// <summary>Fired when a wicket falls and the UI should prompt for next batter.</summary>
        public event Action? BatterSelectionNeeded;
        /// <summary>Fired when an over is completed and the UI should prompt for next bowler.</summary>
        public event Action? BowlerSelectionNeeded;

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
        public string OversDisplay => CurrentInnings?.OversDisplay ?? "0";
        public string ScoreDisplay => $"{TotalWickets}/{TotalRuns}";

        public int? Target
        {
            get
            {
                if (CurrentInnings == null) return null;

                if (Format == CricketFormat.LimitedOvers)
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
                int totalBalls = TotalOvers * 6;
                int ballsRemaining = totalBalls - CurrentInnings.LegalBallsBowled;
                if (ballsRemaining <= 0) return null;
                int needed = Target.Value - CurrentInnings.TotalRuns;
                if (needed <= 0) return 0;
                return Math.Round(6.0 * needed / ballsRemaining, 2);
            }
        }

        public double RunRate => CurrentInnings?.RunRate ?? 0;

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
            TeamAName = string.IsNullOrWhiteSpace(teamAName) ? "Team A" : teamAName.Trim();
            TeamAAbbr = string.IsNullOrWhiteSpace(teamAAbbr) ? "TMA" : teamAAbbr.Trim();
            TeamBName = string.IsNullOrWhiteSpace(teamBName) ? "Team B" : teamBName.Trim();
            TeamBAbbr = string.IsNullOrWhiteSpace(teamBAbbr) ? "TMB" : teamBAbbr.Trim();
            MatchChanged?.Invoke();
        }

        public void StartMatch()
        {
            AllInnings.Clear();
            CurrentInningsNumber = 1;
            StartNewInnings();
            MatchChanged?.Invoke();
        }

        private void StartNewInnings()
        {
            bool teamABats;
            if (CurrentInningsNumber == 1)
                teamABats = TeamABatsFirst;
            else if (CurrentInningsNumber == 2)
                teamABats = !TeamABatsFirst;
            else
                teamABats = (CurrentInningsNumber % 2 == 1) == TeamABatsFirst;

            var battingPlayers = teamABats ? TeamAPlayers : TeamBPlayers;
            var bowlingPlayers = teamABats ? TeamBPlayers : TeamAPlayers;

            foreach (var p in battingPlayers) p.ResetBatting();
            foreach (var p in bowlingPlayers) p.ResetBowling();

            var innings = new CricketInnings
            {
                InningsNumber = CurrentInningsNumber,
                BattingTeamName = teamABats ? TeamAName : TeamBName,
                BowlingTeamName = teamABats ? TeamBName : TeamAName,
                BattingOrder = new List<CricketPlayer>(battingPlayers),
                BowlingAttack = new List<CricketPlayer>(bowlingPlayers),
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
            if (inn == null || index < 0 || index >= inn.BattingOrder.Count) return;
            if (inn.BattingOrder[index].IsOut) return;
            inn.StrikerIndex = index;
            MatchChanged?.Invoke();
        }

        /// <summary>Set the non-striker by index into BattingOrder.</summary>
        public void SetNonStriker(int index)
        {
            var inn = CurrentInnings;
            if (inn == null || index < 0 || index >= inn.BattingOrder.Count) return;
            if (inn.BattingOrder[index].IsOut) return;
            inn.NonStrikerIndex = index;
            MatchChanged?.Invoke();
        }

        /// <summary>Set the new batter after a wicket (replaces the striker slot).</summary>
        public void SelectNewBatter(int index)
        {
            var inn = CurrentInnings;
            if (inn == null || index < 0 || index >= inn.BattingOrder.Count) return;
            inn.StrikerIndex = index;
            inn.NeedsBatterSelection = false;
            // Reset partnership for new pair
            inn.PartnershipRuns = 0;
            inn.PartnershipBalls = 0;
            MatchChanged?.Invoke();
        }

        /// <summary>Confirm bowler selection (end-of-over prompt).</summary>
        public void ConfirmBowler(int index)
        {
            var inn = CurrentInnings;
            if (inn == null || index < 0 || index >= inn.BowlingAttack.Count) return;
            inn.PreviousBowlerIndex = inn.CurrentBowlerIndex;
            inn.CurrentBowlerIndex = index;
            inn.NeedsBowlerSelection = false;
            MatchChanged?.Invoke();
        }

        /// <summary>Get the suggested next bowler (auto-rotation: alternates between last 2).</summary>
        public int SuggestedNextBowler()
        {
            var inn = CurrentInnings;
            if (inn == null) return 0;
            if (inn.PreviousBowlerIndex >= 0 && inn.PreviousBowlerIndex != inn.CurrentBowlerIndex)
                return inn.PreviousBowlerIndex;
            // Suggest the next in line
            return (inn.CurrentBowlerIndex + 1) % inn.BowlingAttack.Count;
        }

        // ---- Scoring ----

        public void RecordDelivery(int runs, CricketDeliveryType type, string? dismissalText = null)
        {
            var inn = CurrentInnings;
            if (inn == null || inn.IsComplete) return;

            // Block scoring if batsmen or bowler not yet selected
            if (inn.StrikerIndex < 0 || inn.NonStrikerIndex < 0 || inn.CurrentBowlerIndex < 0) return;

            bool isLegal = type != CricketDeliveryType.Wide && type != CricketDeliveryType.NoBall;
            bool isExtra = type == CricketDeliveryType.Wide || type == CricketDeliveryType.NoBall
                        || type == CricketDeliveryType.Bye || type == CricketDeliveryType.LegBye;

            var striker = inn.Striker;
            var bowler = inn.CurrentBowler;

            // Apply runs
            inn.TotalRuns += runs;

            if (isExtra)
            {
                inn.TotalExtras += runs;
                switch (type)
                {
                    case CricketDeliveryType.Wide:
                        inn.Wides += runs;
                        if (bowler != null) { bowler.RunsConceded += runs; bowler.Wides++; }
                        break;
                    case CricketDeliveryType.NoBall:
                        inn.NoBalls += runs;
                        if (bowler != null) { bowler.RunsConceded += runs; bowler.NoBalls++; }
                        if (runs > 1 && striker != null)
                        {
                            striker.Runs += runs - 1;
                            if (runs - 1 == 4) striker.Fours++;
                            else if (runs - 1 == 6) striker.Sixes++;
                        }
                        break;
                    case CricketDeliveryType.Bye:
                        inn.Byes += runs;
                        break;
                    case CricketDeliveryType.LegBye:
                        inn.LegByes += runs;
                        break;
                }
            }
            else
            {
                if (striker != null)
                {
                    striker.Runs += runs;
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
                if (striker != null) striker.BallsFaced++;
                if (bowler != null) bowler.BallsBowled++;
                inn.PartnershipBalls++;

                string ballDisplay = type switch
                {
                    CricketDeliveryType.Wicket => "W",
                    CricketDeliveryType.Four => "4",
                    CricketDeliveryType.Six => "6",
                    CricketDeliveryType.Dot => "•",
                    _ => runs.ToString()
                };
                inn.CurrentOverBalls.Add(ballDisplay);
            }
            else
            {
                string ballDisplay = type switch
                {
                    CricketDeliveryType.Wide => $"{runs}wd",
                    CricketDeliveryType.NoBall => $"{runs}nb",
                    _ => runs.ToString()
                };
                inn.CurrentOverBalls.Add(ballDisplay);
            }

            inn.PartnershipRuns += runs;

            // Handle wicket — mark out but don't auto-select next batter
            if (type == CricketDeliveryType.Wicket)
            {
                inn.TotalWickets++;
                if (striker != null)
                {
                    striker.IsOut = true;
                    striker.DismissalText = dismissalText;
                }
                // Bowler gets wicket credit unless it's a run out
                bool isRunOut = dismissalText != null && dismissalText.StartsWith("run out", StringComparison.OrdinalIgnoreCase);
                if (bowler != null && !isRunOut) bowler.Wickets++;

                // Flag that batter selection is needed (unless all out)
                if (!inn.IsAllOut)
                    inn.NeedsBatterSelection = true;
            }

            // Create delivery record
            var delivery = new CricketDelivery
            {
                Innings = inn.InningsNumber,
                OverNumber = deliveryOver,
                BallInOver = isLegal ? deliveryBall : 0,
                Type = type,
                Runs = runs,
                BatsmanName = striker?.DisplayName,
                BowlerName = bowler?.DisplayName,
                DismissalText = dismissalText,
                IsExtra = isExtra,
                TotalAfter = inn.TotalRuns,
                WicketsAfter = inn.TotalWickets
            };
            inn.Deliveries.Add(delivery);

            // Rotate strike indicator on odd runs (don't reorder, just swap indices)
            if (isLegal && runs % 2 == 1)
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);

            // End of over
            if (isLegal && inn.BallsInCurrentOver == 0 && inn.LegalBallsBowled > 0)
            {
                // Check maiden
                if (bowler != null)
                {
                    var lastSixLegal = inn.Deliveries
                        .Where(d => d.BowlerName == bowler.DisplayName && !d.IsExtra)
                        .TakeLast(6);
                    if (lastSixLegal.Count() == 6 && lastSixLegal.All(d => d.Runs == 0))
                        bowler.Maidens++;
                }

                // Swap strike at end of over
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
                inn.CurrentOverBalls.Clear();

                // Track when this bowler last bowled
                if (bowler != null)
                    bowler.LastOverBowledAt = inn.CompletedOvers;

                // Flag bowler selection needed
                if (!inn.IsComplete && !inn.IsAllOut)
                    inn.NeedsBowlerSelection = true;
            }

            CheckInningsComplete(inn);

            DeliveryAdded?.Invoke(delivery);
            MatchChanged?.Invoke();

            // Fire selection prompts after MatchChanged so UI is up to date
            if (type == CricketDeliveryType.Wicket && inn.NeedsBatterSelection)
                BatterSelectionNeeded?.Invoke();
            if (inn.NeedsBowlerSelection)
                BowlerSelectionNeeded?.Invoke();
        }

        public void RotateStrike()
        {
            var inn = CurrentInnings;
            if (inn == null) return;
            (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
        }

        public void SetCurrentBowler(int bowlerIndex)
        {
            var inn = CurrentInnings;
            if (inn == null) return;
            if (bowlerIndex >= 0 && bowlerIndex < inn.BowlingAttack.Count)
            {
                inn.CurrentBowlerIndex = bowlerIndex;
                MatchChanged?.Invoke();
            }
        }

        private void CheckInningsComplete(CricketInnings inn)
        {
            if (inn.IsAllOut) { inn.IsComplete = true; return; }
            if (Format == CricketFormat.LimitedOvers && inn.LegalBallsBowled >= TotalOvers * 6)
            { inn.IsComplete = true; return; }
            if (Target != null && inn.TotalRuns >= Target.Value)
            { inn.IsComplete = true; return; }
        }

        public bool CanStartNextInnings() =>
            CurrentInnings?.IsComplete == true && CurrentInningsNumber < TotalInnings;

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
            if (inn == null || Format != CricketFormat.MultiDay) return;
            inn.IsDeclared = true;
            inn.IsComplete = true;
            MatchChanged?.Invoke();
        }

        public bool UndoLastDelivery()
        {
            var inn = CurrentInnings;
            if (inn == null || inn.Deliveries.Count == 0) return false;
            inn.Deliveries.RemoveAt(inn.Deliveries.Count - 1);
            RebuildInningsFromDeliveries(inn);
            MatchChanged?.Invoke();
            return true;
        }

        private void RebuildInningsFromDeliveries(CricketInnings inn)
        {
            inn.TotalRuns = inn.TotalWickets = inn.TotalExtras = 0;
            inn.Wides = inn.NoBalls = inn.Byes = inn.LegByes = 0;
            inn.LegalBallsBowled = 0;
            inn.PartnershipRuns = inn.PartnershipBalls = 0;
            inn.CurrentOverBalls.Clear();
            inn.NeedsBatterSelection = false;
            inn.NeedsBowlerSelection = false;

            foreach (var p in inn.BattingOrder) p.ResetBatting();
            foreach (var p in inn.BowlingAttack) p.ResetBowling();

            var deliveries = new List<CricketDelivery>(inn.Deliveries);
            inn.Deliveries.Clear();

            foreach (var d in deliveries)
                ReplayDelivery(inn, d);
        }

        private void ReplayDelivery(CricketInnings inn, CricketDelivery d)
        {
            bool isLegal = d.Type != CricketDeliveryType.Wide && d.Type != CricketDeliveryType.NoBall;
            bool isExtra = d.IsExtra;

            var striker = inn.Striker;
            var bowler = inn.CurrentBowler;

            inn.TotalRuns += d.Runs;
            if (isExtra)
            {
                inn.TotalExtras += d.Runs;
                switch (d.Type)
                {
                    case CricketDeliveryType.Wide: inn.Wides += d.Runs; break;
                    case CricketDeliveryType.NoBall: inn.NoBalls += d.Runs; break;
                    case CricketDeliveryType.Bye: inn.Byes += d.Runs; break;
                    case CricketDeliveryType.LegBye: inn.LegByes += d.Runs; break;
                }
            }
            else if (striker != null)
            {
                striker.Runs += d.Runs;
                if (d.Type == CricketDeliveryType.Four) striker.Fours++;
                else if (d.Type == CricketDeliveryType.Six) striker.Sixes++;
            }

            if (isLegal)
            {
                inn.LegalBallsBowled++;
                if (striker != null) striker.BallsFaced++;
                if (bowler != null) bowler.BallsBowled++;
                inn.PartnershipBalls++;
            }
            inn.PartnershipRuns += d.Runs;

            if (d.Type == CricketDeliveryType.Wicket)
            {
                inn.TotalWickets++;
                if (striker != null) { striker.IsOut = true; striker.DismissalText = d.DismissalText; }
                if (bowler != null) bowler.Wickets++;
                inn.PartnershipRuns = 0;
                inn.PartnershipBalls = 0;
            }

            inn.Deliveries.Add(d);

            if (isLegal && d.Runs % 2 == 1)
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
            if (isLegal && inn.BallsInCurrentOver == 0 && inn.LegalBallsBowled > 0)
            {
                (inn.StrikerIndex, inn.NonStrikerIndex) = (inn.NonStrikerIndex, inn.StrikerIndex);
                inn.CurrentOverBalls.Clear();
            }
        }

        // ---- Match result ----

        public string? MatchResult
        {
            get
            {
                if (AllInnings.Count < 2) return null;
                var lastInnings = AllInnings.Last();
                if (!lastInnings.IsComplete) return null;

                if (Format == CricketFormat.LimitedOvers && AllInnings.Count == 2)
                {
                    int firstTotal = AllInnings[0].TotalRuns;
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

                // Multi-day: result after all innings complete
                if (Format == CricketFormat.MultiDay && AllInnings.Count >= TotalInnings)
                {
                    int teamATotal = AllInnings.Where(i => i.BattingTeamName == TeamAName).Sum(i => i.TotalRuns);
                    int teamBTotal = AllInnings.Where(i => i.BattingTeamName == TeamBName).Sum(i => i.TotalRuns);

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
                    return "Match drawn";
                }

                return null;
            }
        }

        public void ResetForNewGame()
        {
            AllInnings.Clear();
            CurrentInningsNumber = 1;
            foreach (var p in TeamAPlayers) { p.ResetBatting(); p.ResetBowling(); }
            foreach (var p in TeamBPlayers) { p.ResetBatting(); p.ResetBowling(); }
            MatchChanged?.Invoke();
        }

        public void RaiseMatchChanged() => MatchChanged?.Invoke();
    }
}
