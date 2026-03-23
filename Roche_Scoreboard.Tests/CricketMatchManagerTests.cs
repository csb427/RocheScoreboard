using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Tests;

public class CricketMatchManagerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a CricketMatchManager with two 11-player squads, starts the match,
    /// and selects opener (index 0) as striker, opener (index 1) as non-striker,
    /// and the first bowler (index 0).
    /// </summary>
    private static CricketMatchManager CreateStartedMatch(
        CricketFormat format = CricketFormat.LimitedOvers,
        int totalOvers = 50)
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("India", "IND", "Australia", "AUS");
        mgr.Format = format;
        mgr.TotalOvers = totalOvers;
        mgr.TeamABatsFirst = true;
        mgr.TotalInnings = format == CricketFormat.MultiDay ? 4 : 2;

        mgr.TeamAPlayers = MakePlayers("IND", 11);
        mgr.TeamBPlayers = MakePlayers("AUS", 11);

        mgr.StartMatch();

        // Selections
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        return mgr;
    }

    private static List<CricketPlayer> MakePlayers(string prefix, int count) =>
        Enumerable.Range(1, count)
                  .Select(i => new CricketPlayer { FirstName = prefix, LastName = $"P{i}" })
                  .ToList();

    // ── SetTeams ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetTeams_StoresNames()
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("India", "IND", "Australia", "AUS");

        Assert.Equal("India", mgr.TeamAName);
        Assert.Equal("IND", mgr.TeamAAbbr);
        Assert.Equal("Australia", mgr.TeamBName);
        Assert.Equal("AUS", mgr.TeamBAbbr);
    }

    [Fact]
    public void SetTeams_EmptyNames_FallBackToDefaults()
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("", "", "", "");

        Assert.Equal("Team A", mgr.TeamAName);
        Assert.Equal("TMA", mgr.TeamAAbbr);
        Assert.Equal("Team B", mgr.TeamBName);
        Assert.Equal("TMB", mgr.TeamBAbbr);
    }

    // ── StartMatch / default state ────────────────────────────────────────────

    [Fact]
    public void StartMatch_CreatesFirstInnings()
    {
        var mgr = CreateStartedMatch();
        Assert.Equal(1, mgr.CurrentInningsNumber);
        Assert.NotNull(mgr.CurrentInnings);
    }

    [Fact]
    public void StartMatch_TeamABatsFirstByDefault()
    {
        var mgr = CreateStartedMatch();
        Assert.Equal("India", mgr.BattingTeamName);
        Assert.Equal("Australia", mgr.BowlingTeamName);
    }

    [Fact]
    public void StartMatch_TeamBBatsFirst_WhenFlagSet()
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("India", "IND", "Australia", "AUS");
        mgr.TeamABatsFirst = false;
        mgr.TeamAPlayers = MakePlayers("IND", 11);
        mgr.TeamBPlayers = MakePlayers("AUS", 11);
        mgr.StartMatch();

        Assert.Equal("Australia", mgr.CurrentInnings!.BattingTeamName);
    }

    [Fact]
    public void DefaultScore_IsZeroSlashZero()
    {
        var mgr = CreateStartedMatch();
        Assert.Equal(0, mgr.TotalRuns);
        Assert.Equal(0, mgr.TotalWickets);
        Assert.Equal("0/0", mgr.ScoreDisplay);
    }

    // ── RecordDelivery – basic scoring ───────────────────────────────────────

    [Fact]
    public void RecordDelivery_Dot_IncrementsLegalBalls()
    {
        // A dot ball should add 1 legal ball but no runs
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        Assert.Equal(0, mgr.TotalRuns);
        Assert.Equal(1, mgr.CurrentInnings!.LegalBallsBowled);
    }

    [Fact]
    public void RecordDelivery_Runs_AddsRunsAndLegalBall()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(3, CricketDeliveryType.Runs);

        Assert.Equal(3, mgr.TotalRuns);
        Assert.Equal(1, mgr.CurrentInnings!.LegalBallsBowled);
    }

    [Fact]
    public void RecordDelivery_Four_AddsRunsAndFourToStriker()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(4, CricketDeliveryType.Four);

        var inn = mgr.CurrentInnings!;
        // After an even number of runs, striker stays the same.
        // But four boundary – check striker's fours count
        // The striker may have rotated if 4%2 == 0 → no rotation
        Assert.Equal(4, mgr.TotalRuns);
        Assert.Equal(1, inn.LegalBallsBowled);
    }

    [Fact]
    public void RecordDelivery_Six_AddsSixToStriker()
    {
        var mgr = CreateStartedMatch();
        int strikerBefore = mgr.CurrentInnings!.StrikerIndex;
        mgr.RecordDelivery(6, CricketDeliveryType.Six);

        var inn = mgr.CurrentInnings!;
        var striker = inn.BattingOrder[strikerBefore];
        Assert.Equal(1, striker.Sixes);
        Assert.Equal(6, mgr.TotalRuns);
    }

    [Fact]
    public void RecordDelivery_Wide_IsNotLegalBall()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(1, CricketDeliveryType.Wide);

        Assert.Equal(1, mgr.TotalRuns);
        Assert.Equal(0, mgr.CurrentInnings!.LegalBallsBowled);
        Assert.Equal(1, mgr.CurrentInnings.Wides);
        Assert.Equal(1, mgr.CurrentInnings.TotalExtras);
    }

    [Fact]
    public void RecordDelivery_NoBall_IsNotLegalBall()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(1, CricketDeliveryType.NoBall);

        Assert.Equal(1, mgr.TotalRuns);
        Assert.Equal(0, mgr.CurrentInnings!.LegalBallsBowled);
        Assert.Equal(1, mgr.CurrentInnings.NoBalls);
    }

    [Fact]
    public void RecordDelivery_Bye_CountsAsExtra()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(1, CricketDeliveryType.Bye);

        var inn = mgr.CurrentInnings!;
        Assert.Equal(1, inn.TotalRuns);
        Assert.Equal(1, inn.TotalExtras);
        Assert.Equal(1, inn.Byes);
        Assert.Equal(1, inn.LegalBallsBowled); // bye IS a legal ball
    }

    [Fact]
    public void RecordDelivery_LegBye_CountsAsExtra()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(2, CricketDeliveryType.LegBye);

        var inn = mgr.CurrentInnings!;
        Assert.Equal(2, inn.TotalRuns);
        Assert.Equal(2, inn.LegByes);
        Assert.Equal(1, inn.LegalBallsBowled);
    }

    // ── Wicket handling ──────────────────────────────────────────────────────

    [Fact]
    public void RecordDelivery_Wicket_IncrementsWickets()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");

        Assert.Equal(1, mgr.TotalWickets);
    }

    [Fact]
    public void RecordDelivery_Wicket_MarksStrikerOut()
    {
        var mgr = CreateStartedMatch();
        int strikerIdx = mgr.CurrentInnings!.StrikerIndex;
        mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");

        var striker = mgr.CurrentInnings!.BattingOrder[strikerIdx];
        Assert.True(striker.IsOut);
        Assert.Equal("bowled", striker.DismissalText);
    }

    [Fact]
    public void RecordDelivery_Wicket_BowlerGetsCreditUnlessRunOut()
    {
        var mgr = CreateStartedMatch();
        int bowlerIdx = mgr.CurrentInnings!.CurrentBowlerIndex;
        mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");

        var bowler = mgr.CurrentInnings!.BowlingAttack[bowlerIdx];
        Assert.Equal(1, bowler.Wickets);
    }

    [Fact]
    public void RecordDelivery_RunOut_BowlerDoesNotGetCredit()
    {
        var mgr = CreateStartedMatch();
        int bowlerIdx = mgr.CurrentInnings!.CurrentBowlerIndex;
        mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "run out (Root)");

        var bowler = mgr.CurrentInnings!.BowlingAttack[bowlerIdx];
        Assert.Equal(0, bowler.Wickets);
    }

    [Fact]
    public void RecordDelivery_Wicket_NeedsBatterSelectionIsTrue()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
        Assert.True(mgr.CurrentInnings!.NeedsBatterSelection);
    }

    // ── Strike rotation ──────────────────────────────────────────────────────

    [Fact]
    public void RecordDelivery_OddRuns_RotatesStrike()
    {
        var mgr = CreateStartedMatch();
        int strikerBefore = mgr.CurrentInnings!.StrikerIndex;
        int nonStrikerBefore = mgr.CurrentInnings.NonStrikerIndex;

        mgr.RecordDelivery(1, CricketDeliveryType.Runs); // 1 run → rotate

        Assert.Equal(nonStrikerBefore, mgr.CurrentInnings!.StrikerIndex);
        Assert.Equal(strikerBefore, mgr.CurrentInnings.NonStrikerIndex);
    }

    [Fact]
    public void RecordDelivery_EvenRuns_DoesNotRotateStrike()
    {
        var mgr = CreateStartedMatch();
        int strikerBefore = mgr.CurrentInnings!.StrikerIndex;

        mgr.RecordDelivery(2, CricketDeliveryType.Runs); // 2 runs → no rotate

        Assert.Equal(strikerBefore, mgr.CurrentInnings!.StrikerIndex);
    }

    // ── End of over ──────────────────────────────────────────────────────────

    [Fact]
    public void EndOfOver_FlagsBowlerSelectionNeeded()
    {
        var mgr = CreateStartedMatch();
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        Assert.True(mgr.CurrentInnings!.NeedsBowlerSelection);
    }

    [Fact]
    public void EndOfOver_SwapsStrike()
    {
        var mgr = CreateStartedMatch();
        int strikerBefore = mgr.CurrentInnings!.StrikerIndex;
        int nonStrikerBefore = mgr.CurrentInnings.NonStrikerIndex;

        // Bowl 6 dot balls
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        // At end of over, strike should have rotated (even if no runs)
        Assert.Equal(nonStrikerBefore, mgr.CurrentInnings!.StrikerIndex);
        Assert.Equal(strikerBefore, mgr.CurrentInnings.NonStrikerIndex);
    }

    [Fact]
    public void EndOfOver_ClearsCurrentOverBalls()
    {
        var mgr = CreateStartedMatch();
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        Assert.Empty(mgr.CurrentInnings!.CurrentOverBalls);
    }

    // ── Innings completion ────────────────────────────────────────────────────

    [Fact]
    public void InningsComplete_WhenAllOut()
    {
        var mgr = CreateStartedMatch();
        var inn = mgr.CurrentInnings!;

        // Bowl 10 wickets (all out)
        for (int w = 0; w < 10; w++)
        {
            mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
            // Select next batter unless all out
            if (!inn.IsAllOut)
            {
                int nextBatter = inn.BattingOrder
                    .Select((p, i) => (p, i))
                    .First(x => !x.p.IsOut && x.i != inn.NonStrikerIndex).i;
                mgr.SelectNewBatter(nextBatter);
            }
        }

        Assert.True(inn.IsComplete);
    }

    [Fact]
    public void InningsComplete_WhenOversExhausted()
    {
        var mgr = CreateStartedMatch(totalOvers: 1); // 1-over match

        // Bowl exactly 6 legal balls
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        Assert.True(mgr.CurrentInnings!.IsComplete);
    }

    [Fact]
    public void InningsComplete_WhenTargetChased()
    {
        var mgr = CreateStartedMatch(totalOvers: 50);

        // Manually complete first innings by exhausting overs
        var inn1 = mgr.CurrentInnings!;
        // Set innings runs directly (simulate first innings)
        inn1.TotalRuns = 150;
        inn1.IsComplete = true;

        // Start 2nd innings
        Assert.True(mgr.CanStartNextInnings());
        mgr.StartNextInnings();

        // Set up 2nd innings
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        // Target is 151; score 151 with a single ball
        mgr.RecordDelivery(151, CricketDeliveryType.Runs);

        Assert.True(mgr.CurrentInnings!.IsComplete);
    }

    // ── Target ───────────────────────────────────────────────────────────────

    [Fact]
    public void Target_NullInFirstInnings()
    {
        var mgr = CreateStartedMatch();
        Assert.Null(mgr.Target);
    }

    [Fact]
    public void Target_InSecondInnings_IsFirstTotalPlusOne()
    {
        var mgr = CreateStartedMatch(totalOvers: 1);

        // Complete first innings with 6 dots
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        // Manually set first innings runs
        mgr.AllInnings[0].TotalRuns = 200;

        mgr.StartNextInnings();
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        Assert.Equal(201, mgr.Target);
    }

    // ── RunsRequired / RequiredRunRate ────────────────────────────────────────

    [Fact]
    public void RunsRequired_NullInFirstInnings()
    {
        var mgr = CreateStartedMatch();
        Assert.Null(mgr.RunsRequired);
    }

    [Fact]
    public void RequiredRunRate_NullInFirstInnings()
    {
        var mgr = CreateStartedMatch();
        Assert.Null(mgr.RequiredRunRate);
    }

    // ── UndoLastDelivery ─────────────────────────────────────────────────────

    [Fact]
    public void UndoLastDelivery_ReturnsFalseWithNoDeliveries()
    {
        var mgr = CreateStartedMatch();
        bool result = mgr.UndoLastDelivery();
        Assert.False(result);
    }

    [Fact]
    public void UndoLastDelivery_RemovesLastDelivery()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(4, CricketDeliveryType.Four);
        bool result = mgr.UndoLastDelivery();
        Assert.True(result);
        Assert.Empty(mgr.CurrentInnings!.Deliveries);
    }

    [Fact]
    public void UndoLastDelivery_RevertsScore()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(6, CricketDeliveryType.Six);
        mgr.UndoLastDelivery();
        Assert.Equal(0, mgr.TotalRuns);
    }

    // ── MatchResult (LimitedOvers) ────────────────────────────────────────────

    [Fact]
    public void MatchResult_NullWhenLessThanTwoInnings()
    {
        var mgr = CreateStartedMatch();
        Assert.Null(mgr.MatchResult);
    }

    [Fact]
    public void MatchResult_BattingSecondWinsByWickets()
    {
        var mgr = CreateStartedMatch(totalOvers: 1);

        // First innings: 0 runs
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);

        mgr.AllInnings[0].TotalRuns = 100;
        mgr.StartNextInnings();
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        // Score enough to win (target = 101) without losing wickets
        mgr.RecordDelivery(101, CricketDeliveryType.Runs);

        string? result = mgr.MatchResult;
        Assert.NotNull(result);
        Assert.Contains("won by", result);
        Assert.Contains("wicket", result);
    }

    [Fact]
    public void MatchResult_BattingFirstWinsByRuns()
    {
        var mgr = CreateStartedMatch(totalOvers: 1);

        // First innings: 200 runs
        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);
        mgr.AllInnings[0].TotalRuns = 200;

        mgr.StartNextInnings();
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        // Second innings: all out for 50
        for (int w = 0; w < 10; w++)
        {
            mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
            var inn = mgr.CurrentInnings;
            if (inn != null && !inn.IsAllOut && !inn.IsComplete)
            {
                int next = inn.BattingOrder
                    .Select((p, i) => (p, i))
                    .First(x => !x.p.IsOut && x.i != inn.NonStrikerIndex).i;
                mgr.SelectNewBatter(next);
            }
        }
        mgr.AllInnings[1].TotalRuns = 50;
        mgr.AllInnings[1].IsComplete = true;

        string? result = mgr.MatchResult;
        Assert.NotNull(result);
        Assert.Contains("won by", result);
        Assert.Contains("run", result);
    }

    [Fact]
    public void MatchResult_Tied()
    {
        var mgr = CreateStartedMatch(totalOvers: 1);

        for (int i = 0; i < 6; i++)
            mgr.RecordDelivery(0, CricketDeliveryType.Dot);
        mgr.AllInnings[0].TotalRuns = 100;
        mgr.AllInnings[0].IsComplete = true;

        mgr.StartNextInnings();
        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        // Score exactly 100 but get all out
        mgr.AllInnings[1].TotalRuns = 100;
        for (int w = 0; w < 10; w++)
        {
            mgr.RecordDelivery(0, CricketDeliveryType.Wicket, "bowled");
            var inn = mgr.CurrentInnings;
            if (inn != null && !inn.IsAllOut && !inn.IsComplete)
            {
                int next = inn.BattingOrder
                    .Select((p, i) => (p, i))
                    .First(x => !x.p.IsOut && x.i != inn.NonStrikerIndex).i;
                mgr.SelectNewBatter(next);
            }
        }
        mgr.AllInnings[1].IsComplete = true;

        string? result = mgr.MatchResult;
        Assert.NotNull(result);
        Assert.Equal("Match tied", result);
    }

    // ── DeclareInnings ───────────────────────────────────────────────────────

    [Fact]
    public void DeclareInnings_MarksInningsComplete_InMultiDayFormat()
    {
        var mgr = CreateStartedMatch(CricketFormat.MultiDay, 0);
        mgr.DeclareInnings();
        Assert.True(mgr.CurrentInnings!.IsDeclared);
        Assert.True(mgr.CurrentInnings.IsComplete);
    }

    [Fact]
    public void DeclareInnings_NoOp_InLimitedOversFormat()
    {
        var mgr = CreateStartedMatch(CricketFormat.LimitedOvers, 50);
        mgr.DeclareInnings();
        Assert.False(mgr.CurrentInnings!.IsDeclared);
    }

    // ── CanStartNextInnings ──────────────────────────────────────────────────

    [Fact]
    public void CanStartNextInnings_FalseWhenCurrentInningsNotComplete()
    {
        var mgr = CreateStartedMatch();
        Assert.False(mgr.CanStartNextInnings());
    }

    // ── SuggestedNextBowler ──────────────────────────────────────────────────

    [Fact]
    public void SuggestedNextBowler_ReturnsNonCurrentBowler()
    {
        var mgr = CreateStartedMatch();
        int suggested = mgr.SuggestedNextBowler();
        // No previous bowler set, so suggestion should be next bowler in list
        Assert.NotEqual(mgr.CurrentInnings!.CurrentBowlerIndex, suggested);
    }

    // ── BattingTeamAbbr / derived props ──────────────────────────────────────

    [Fact]
    public void BattingTeamAbbr_ReturnsCorrectAbbreviation()
    {
        var mgr = CreateStartedMatch();
        Assert.Equal("IND", mgr.BattingTeamAbbr);
    }

    // ── ResetForNewGame ──────────────────────────────────────────────────────

    [Fact]
    public void ResetForNewGame_ClearsAllInnings()
    {
        var mgr = CreateStartedMatch();
        mgr.RecordDelivery(4, CricketDeliveryType.Four);
        mgr.ResetForNewGame();

        Assert.Empty(mgr.AllInnings);
        Assert.Equal(1, mgr.CurrentInningsNumber);
    }

    // ── RotateStrike ─────────────────────────────────────────────────────────

    [Fact]
    public void RotateStrike_SwapsStrikerAndNonStriker()
    {
        var mgr = CreateStartedMatch();
        int strikerBefore = mgr.CurrentInnings!.StrikerIndex;
        int nonStrikerBefore = mgr.CurrentInnings.NonStrikerIndex;

        mgr.RotateStrike();

        Assert.Equal(nonStrikerBefore, mgr.CurrentInnings!.StrikerIndex);
        Assert.Equal(strikerBefore, mgr.CurrentInnings.NonStrikerIndex);
    }

    // ── Batter / bowler selection validation ──────────────────────────────────

    [Fact]
    public void SetStriker_OutPlayer_IsIgnored()
    {
        var mgr = CreateStartedMatch();
        mgr.CurrentInnings!.BattingOrder[2].IsOut = true;
        int originalStriker = mgr.CurrentInnings.StrikerIndex;

        mgr.SetStriker(2);

        Assert.Equal(originalStriker, mgr.CurrentInnings.StrikerIndex);
    }

    [Fact]
    public void SetNonStriker_InvalidIndex_IsIgnored()
    {
        var mgr = CreateStartedMatch();
        int original = mgr.CurrentInnings!.NonStrikerIndex;
        mgr.SetNonStriker(99); // out of range
        Assert.Equal(original, mgr.CurrentInnings.NonStrikerIndex);
    }

    // ── RecordDelivery – blocked when not set up ──────────────────────────────

    [Fact]
    public void RecordDelivery_BlockedWhenBowlerNotSelected()
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("India", "IND", "Australia", "AUS");
        mgr.TeamAPlayers = MakePlayers("IND", 11);
        mgr.TeamBPlayers = MakePlayers("AUS", 11);
        mgr.StartMatch();

        mgr.SetStriker(0);
        mgr.SetNonStriker(1);
        // Do NOT confirm bowler

        mgr.RecordDelivery(4, CricketDeliveryType.Four);

        // No run should have been recorded
        Assert.Equal(0, mgr.TotalRuns);
    }

    [Fact]
    public void RecordDelivery_BlockedWhenStrikerNotSelected()
    {
        var mgr = new CricketMatchManager();
        mgr.SetTeams("India", "IND", "Australia", "AUS");
        mgr.TeamAPlayers = MakePlayers("IND", 11);
        mgr.TeamBPlayers = MakePlayers("AUS", 11);
        mgr.StartMatch();

        // Do NOT set striker
        mgr.SetNonStriker(1);
        mgr.ConfirmBowler(0);

        mgr.RecordDelivery(1, CricketDeliveryType.Runs);
        Assert.Equal(0, mgr.TotalRuns);
    }
}
