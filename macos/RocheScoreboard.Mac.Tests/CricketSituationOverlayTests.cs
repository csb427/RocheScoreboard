using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketSituationOverlayTests
{
    [Theory]
    [InlineData(CricketOverlayKind.MatchState)]
    [InlineData(CricketOverlayKind.InningsRecap)]
    public void AnUnchangedEquationOrRecapDoesNotEarnAnotherAutomaticSlotForAnOrdinaryDot(CricketOverlayKind kind)
    {
        var match=Scores(("A",500,6),("B",200,10),("B",310,10),("A",10,0));
        var before=Assert.Single(CricketOverlayAnalysis.Build(match),f=>f.Kind==kind);
        var scheduler=new CricketOverlayScheduler();scheduler.Observe(match,0);scheduler.MarkShown(before,20);
        match.RecordDelivery(0,CricketDeliveryType.Dot);Assert.Single(match.CurrentInnings!.Deliveries);
        var after=Assert.Single(CricketOverlayAnalysis.Build(match),f=>f.Kind==kind);
        Assert.Equal(before.Metrics,after.Metrics);Assert.Equal(before.Fingerprint,after.Fingerprint);
        scheduler.Observe(match,30);Assert.Null(scheduler.Select(new[]{after},400));
    }
    private static CricketOverlayFact Fact(CricketMatchManager match,string kind)
        =>Assert.Single(CricketOverlayAnalysis.Build(match),fact=>fact.Kind.ToString()==kind);
    internal static CricketMatchManager Scores(params (string Team,int Runs,int Wickets)[] scores)
    {
        List<CricketPlayer> Players(string team)=>Enumerable.Range(0,11).Select(i=>new CricketPlayer{FirstName=team,LastName="Player "+(i+1),RosterIndex=i}).ToList();
        var a=Players("A");var b=Players("B");
        var state=new CricketSerializableState{TeamAName="A",TeamBName="B",TeamAAbbr="A",TeamBAbbr="B",TeamAPlayers=a,TeamBPlayers=b,
            Format=CricketFormat.MultiInnings,TotalInnings=4,CurrentInningsNumber=scores.Length};
        for(int n=0;n<scores.Length;n++)
        {
            var row=scores[n];var batting=(row.Team=="A"?a:b).Select(p=>p.Clone()).ToList();
            for(int i=0;i<row.Wickets;i++){batting[i].IsOut=true;batting[i].HowOut=DismissalType.Bowled;}
            state.AllInnings.Add(new CricketInnings{InningsNumber=n+1,BattingTeamName=row.Team,BowlingTeamName=row.Team=="A"?"B":"A",
                TotalRuns=row.Runs,TotalWickets=row.Wickets,IsComplete=n<scores.Length-1,BattingOrder=batting,
                BowlingAttack=(row.Team=="A"?b:a).Select(p=>p.Clone()).ToList(),StrikerIndex=row.Wickets,NonStrikerIndex=row.Wickets+1,CurrentBowlerIndex=0});
        }
        var match=new CricketMatchManager();match.LoadState(state);return match;
    }
    [AvaloniaFact]
    public void PartnershipNeverOccupiesEitherRotatingRail()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);
        for(int phase=-8;phase<32;phase++)
        {
            Assert.NotEqual("PARTNERSHIP",CricketScorebugControl.SelectRailContext(match,phase).Label);
            Assert.NotEqual("PARTNERSHIP",CricketScorebugControl.SelectChaseMiniContext(match,phase).Label);
        }
    }
    [Fact]
    public void PartnershipCountsDeliveredWidesNoBallsByesAndRecordedRebowlsRatherThanLegalOvers()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(2,CricketDeliveryType.Wide);
        match.RecordDelivery(5,CricketDeliveryType.NoBall);
        match.RecordDelivery(4,CricketDeliveryType.Bye);
        match.RecordDelivery(0,CricketDeliveryType.Dot);
        // The live API deliberately ignores arbitrary legality overrides.
        // A historical delivered/re-bowled ball is represented by the saved
        // CountsInOver flag used during replay, not a new scoring-law option.
        var innings=match.CurrentInnings!;var last=innings.Deliveries[^1];
        innings.Deliveries[^1]=new CricketDelivery{Type=last.Type,Runs=last.Runs,StrikerIndex=last.StrikerIndex,NonStrikerIndex=last.NonStrikerIndex,
            BowlerIndex=last.BowlerIndex,CountsInOver=false,StateBefore=last.StateBefore,TotalAfter=last.TotalAfter,WicketsAfter=last.WicketsAfter,
            OverNumber=last.OverNumber,BallInOver=last.BallInOver};
        innings.LegalBallsBowled--;
        Assert.Equal(4,match.CurrentInnings!.Deliveries.Count);
        var fact=Fact(match,"Partnership");Assert.True(fact.Available);
        Assert.Contains(fact.Metrics,m=>m.Value=="11 (4)");
        Assert.Equal(1,match.CurrentInnings.LegalBallsBowled);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void PartnershipRetirementAndReturnStartANewPairWithoutJoiningEarlierStands(bool reload)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        int original=match.CurrentInnings!.StrikerIndex;
        Assert.True(match.TryReplaceBatter(true,2,CricketBatterReplacementReason.RetiredHurt,false,out string error),error);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (0)");
        match.RecordDelivery(2,CricketDeliveryType.Runs);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="2 (1)");
        Assert.True(match.TryReplaceBatter(true,original,CricketBatterReplacementReason.RetiredNotOut,false,out error),error);
        if(reload){var restored=new CricketMatchManager();restored.LoadState(match.CapturePersistenceState());match=restored;}
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (0)");
        match.RecordDelivery(0,CricketDeliveryType.Dot);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (1)");
    }
    [Fact]
    public void PartnershipPreDeliveryRunOutAddsNoBallAndUndoRestoresTheOldPair()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(3,CricketDeliveryType.Runs);
        Assert.True(match.TryRecordNonStrikerRunOutBeforeDelivery(match.WorkflowToken,out string error),error);
        Assert.False(Fact(match,"Partnership").Available);Assert.Single(match.CurrentInnings!.Deliveries);
        match.SelectNewBatter(2);Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (0)");
        Assert.True(match.TryUndoLastScoringAction(match.WorkflowToken,out error),error);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="3 (1)");
    }
    [Fact]
    public void PartnershipRunOutDeliveryRunsBelongToTheOldStand()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        match.RecordDelivery(2,CricketDeliveryType.Wicket,dismissal:DismissalType.RunOut,fielder:"Keeper",
            dismissedBatterIndex:match.CurrentInnings!.StrikerIndex,completedRunsOnly:true);
        Assert.Equal(6,match.TotalRuns);Assert.Equal(1,match.TotalWickets);
        Assert.False(Fact(match,"Partnership").Available);
        match.SelectNewBatter(2);Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (0)");
        match.RecordDelivery(0,CricketDeliveryType.Dot);Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (1)");
    }
    [Fact]
    public void PartnershipAChangedScorecardWaitsForAKnownResetButAnUnchangedOnePreservesTheStand()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);
        Assert.True(match.TryApplyScorecard(match.CreateScorecardDraft(1),out string error),error);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="4 (1)");
        var draft=match.CreateScorecardDraft(1);draft.TotalRuns=9;draft.Batters[0].Runs=9;draft.Bowlers[0].RunsConceded=9;
        Assert.True(match.TryApplyScorecard(draft,out error),error);Assert.False(Fact(match,"Partnership").Available);
        match.RecordDelivery(0,CricketDeliveryType.Dot);Assert.False(Fact(match,"Partnership").Available);
        match.RecordDelivery(0,CricketDeliveryType.Wicket);match.SelectNewBatter(2);
        Assert.Contains(Fact(match,"Partnership").Metrics,m=>m.Value=="0 (0)");
    }
    [Fact]
    public void PartnershipMissingPairOrUnattributedStoredRunsNeverFabricateZeroFigures()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.CurrentInnings!.TotalRuns=5;
        Assert.False(Fact(match,"Partnership").Available);Assert.Empty(Fact(match,"Partnership").Metrics);
        match.CurrentInnings.StrikerIndex=-1;Assert.False(Fact(match,"Partnership").Available);
        Assert.Equal(5,match.TotalRuns);Assert.Equal(0,match.CurrentInnings.LegalBallsBowled);
    }
    [Theory]
    [InlineData(100,8,"151","2")]
    [InlineData(250,9,"1",null)]
    public void FinalInningsEquationDistinguishesAFieldingWinFromATie(int runs,int wickets,string needed,string? wicketsToWin)
    {
        var match=Scores(("A",300,8),("B",250,10),("A",200,5),("B",runs,wickets));
        var fact=Fact(match,"MatchState");Assert.True(fact.Available);
        Assert.Contains(fact.Metrics,m=>m.Label.Contains("B") && m.Label.Contains("RUNS TO WIN") && m.Value==needed);
        if(wicketsToWin is null)Assert.DoesNotContain(fact.Metrics,m=>m.Label.Contains("WICKETS TO WIN"));
        else Assert.Contains(fact.Metrics,m=>m.Label.Contains("A") && m.Label.Contains("WICKETS TO WIN") && m.Value==wicketsToWin);
    }
    [Theory]
    [InlineData(250,8,true)][InlineData(300,9,false)][InlineData(301,9,false)]
    public void FollowOnEquationNeedsStrictAggregateLeadForTheBowlingTeam(int currentRuns,int wickets,bool canWinByWickets)
    {
        var match=Scores(("A",500,5),("B",200,10),("B",currentRuns,wickets));
        var fact=Fact(match,"MatchState");Assert.True(fact.Available);
        Assert.DoesNotContain(fact.Metrics,m=>m.Label.Contains("RUNS TO WIN"));
        Assert.Equal(canWinByWickets,fact.Metrics.Any(m=>m.Label.Contains("A") && m.Label.Contains("WICKETS TO WIN")));
    }
    [Fact]
    public void MatchStateThirdInningsInningsVictoryAndRevisedTargetUseTheConfiguredEquation()
    {
        var inningsWin=Scores(("A",200,10),("B",500,5),("A",250,8));
        Assert.Contains(Fact(inningsWin,"MatchState").Metrics,m=>m.Label.Contains("B WICKETS TO WIN") && m.Value=="2");
        var chase=Scores(("A",200,10),("B",140,8));chase.Format=CricketFormat.LimitedOvers;chase.TotalInnings=2;
        chase.CurrentInnings!.TargetOverride=151;
        var revised=Fact(chase,"MatchState");Assert.Contains(revised.Metrics,m=>m.Label.Contains("RUNS TO WIN") && m.Value=="11");
        chase.CurrentInnings.TotalRuns=150;chase.CurrentInnings.TotalWickets=9;
        chase.CurrentInnings.BattingOrder[8].IsOut=true;chase.CurrentInnings.StrikerIndex=9;chase.CurrentInnings.NonStrikerIndex=10;
        var tiedIfClosed=Fact(chase,"MatchState");Assert.Contains(tiedIfClosed.Metrics,m=>m.Label.Contains("RUNS TO WIN") && m.Value=="1");
        Assert.DoesNotContain(tiedIfClosed.Metrics,m=>m.Label.Contains("WICKETS TO WIN"));
        chase.CurrentInnings.TotalRuns=151;Assert.False(Fact(chase,"MatchState").Available);
    }
    [Fact]
    public void MatchStateWicketsUseEightPlayerRosterAndSuppressUncertainReturnability()
    {
        var match=Scores(("A",300,8),("B",250,10),("A",200,5),("B",100,5));
        match.CurrentInnings!.BattingOrder.RemoveRange(8,3);
        var fact=Fact(match,"MatchState");Assert.Contains(fact.Metrics,m=>m.Label.Contains("WICKETS TO WIN") && m.Value=="2");
        match.CurrentInnings.BattingOrder[7].IsRetired=true;match.CurrentInnings.BattingOrder[7].RetiredReason="retired hurt";
        var unknown=Fact(match,"MatchState");Assert.DoesNotContain(unknown.Metrics,m=>m.Label.Contains("WICKETS TO WIN"));
        Assert.Contains(unknown.Metrics,m=>m.Label.Contains("RUNS TO WIN") && m.Value=="151");
    }
    [Fact]
    public void MatchStateManualContinuationAndEndedStateNeverClaimANewWinningRequirement()
    {
        var match=Scores(("A",300,8),("B",250,10),("A",200,5),("B",100,8));
        match.CurrentInnings!.IgnoreTarget=true;
        var continued=Fact(match,"MatchState");Assert.DoesNotContain(continued.Metrics,m=>m.Label.Contains("TO WIN"));
        match.CurrentInnings.IsComplete=true;Assert.False(Fact(match,"MatchState").Available);
    }
    [Fact]
    public void RecapUsesActualChronologicalFollowOnOrderAndCurrentCorrectedTotals()
    {
        var match=Scores(("A",500,5),("B",200,10),("B",350,8),("A",20,0));
        match.AllInnings[0].IsDeclared=true;
        var fact=Fact(match,"InningsRecap");Assert.True(fact.Available);Assert.Equal(3,fact.Metrics.Count);
        Assert.Contains("A",fact.Metrics[0].Label);Assert.Contains("B",fact.Metrics[1].Label);Assert.Contains("B",fact.Metrics[2].Label);
        Assert.Contains("B 1ST",fact.Metrics[1].Label);Assert.Contains("B 2ND",fact.Metrics[2].Label);
        Assert.Equal("5/500 dec",fact.Metrics[0].Value);Assert.Equal("10/200",fact.Metrics[1].Value);Assert.Equal("8/350",fact.Metrics[2].Value);
        match.AllInnings[0].TotalRuns=510;var corrected=Fact(match,"InningsRecap");
        Assert.Equal("5/510 dec",corrected.Metrics[0].Value);Assert.NotEqual(fact.Fingerprint,corrected.Fingerprint);
    }
    [Fact]
    public void RecapUnknownHistoricalTeamIdentityDoesNotInventAnInningsOrdinal()
    {
        var match=Scores(("A",300,8),("B",200,10),("A",150,3),("B",20,0));
        match.AllInnings[0].BattingTeamName="Historical name without a team ID";
        Assert.False(Fact(match,"InningsRecap").Available);
    }
    [Fact]
    public void RecapNormalOrderSurvivesReloadAndAllSituationProjectionsAreReadOnly()
    {
        var match=Scores(("A",300,8),("B",200,10),("A",150,3),("B",20,0));
        string before=JsonSerializer.Serialize(match.CapturePersistenceState());
        var fact=Fact(match,"InningsRecap");
        Assert.Contains("A 1ST",fact.Metrics[0].Label);Assert.Contains("B 1ST",fact.Metrics[1].Label);Assert.Contains("A 2ND",fact.Metrics[2].Label);
        _=Fact(match,"MatchState");_=Fact(match,"Partnership");Assert.Equal(before,JsonSerializer.Serialize(match.CapturePersistenceState()));
        var restored=new CricketMatchManager();restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(before)!);
        Assert.Equal(fact.Metrics.ToArray(),Fact(restored,"InningsRecap").Metrics.ToArray());
    }
    [Fact]
    public void RecapSkipsUnfinishedInningsWithoutInventingAForfeitFromZero()
    {
        var match=Scores(("A",0,0),("B",200,10),("A",150,3),("B",20,0));
        match.AllInnings[2].IsComplete=false;
        var fact=Fact(match,"InningsRecap");Assert.True(fact.Available);Assert.Equal(2,fact.Metrics.Count);
        Assert.Equal("0/0",fact.Metrics[0].Value);Assert.DoesNotContain("forfeit",string.Join(" ",fact.Metrics.Select(m=>m.Label+" "+m.Value)),StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fact.Metrics,m=>m.Value.Contains("150"));
    }
    [Fact]
    public void RecapExcludesTheUnfinishedInningsForcedClosedByAbandonment()
    {
        var match=Scores(("A",200,10),("B",50,2));var state=match.CapturePersistenceState();
        state.IsMatchFinalized=true;state.ResultKind=CricketMatchResultKind.Abandoned;state.FinalResult="Match abandoned";
        state.AllInnings[^1].IsComplete=true;
        state.ClosureBefore=new(2,false,false,false,CricketBreakKind.Rain,"Rain"){NaturallyComplete=false};
        var restored=new CricketMatchManager();restored.LoadState(state);
        var fact=Fact(restored,"InningsRecap");Assert.True(fact.Available);Assert.Single(fact.Metrics);
        Assert.Equal("10/200",fact.Metrics[0].Value);Assert.False(fact.Automatic);
    }
    [Fact]
    public void RecapIncludesACompletedSuccessfulChaseAndRemainsManualAfterTheResult()
    {
        var match=Scores(("A",100,10),("B",101,3));var state=match.CapturePersistenceState();
        state.Format=CricketFormat.LimitedOvers;state.TotalInnings=2;
        state.IsMatchFinalized=true;state.ResultKind=CricketMatchResultKind.ScoreBased;state.FinalResult="B won";
        state.AllInnings[^1].IsComplete=true;
        state.ClosureBefore=new(2,true,false,false,CricketBreakKind.None,""){NaturallyComplete=true};
        var restored=new CricketMatchManager();restored.LoadState(state);
        var fact=Fact(restored,"InningsRecap");Assert.True(fact.Available);Assert.Equal(2,fact.Metrics.Count);
        Assert.Equal("3/101",fact.Metrics[1].Value);Assert.False(fact.Automatic);
    }
}
