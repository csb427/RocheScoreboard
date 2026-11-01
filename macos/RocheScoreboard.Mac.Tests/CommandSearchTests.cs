using System.Diagnostics;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CommandSearchTests
{
    private static readonly CommandSearchCandidate[] Candidates =
    [
        new("home","Home / resume","landing current session",10),
        new("resume","Resume saved match","continue recovery",25),
        new("scorecard","Cricket scorecard","edit batting bowling figures",30),
        new("bowler","Change bowler","choose bowling change",20),
        new("settings","Application settings","general cricket afl sound volume audio mute preferences",5),
        new("output","Show output window","enable presentation display on air",5),
        new("start-game","Start Game","new match setup afl footy football cricket choose sport",25)
    ];
    [Theory]
    [InlineData("sco","scorecard")][InlineData("scorecrd","scorecard")][InlineData("take me to the scorecard","scorecard")]
    [InlineData("change bolwer","bowler")][InlineData("bowling change","bowler")][InlineData("volume","settings")]
    [InlineData("sound","settings")][InlineData("preferneces","settings")][InlineData("continue","resume")]
    [InlineData("resuem","resume")][InlineData("display","output")][InlineData("footy","start-game")]
    [InlineData("new cri","start-game")]
    public void LocalSuggestionsHandleIntentPrefixesAndCommonTypos(string query,string expected)
    {
        var hits=OperatorCommandSearch.Rank(query,Candidates);Assert.NotEmpty(hits);Assert.Equal(expected,hits[0].Id);
    }
    [Fact]
    public void ExactMatchesBeatFuzzySuggestionsAndUnavailableMatchesRemainHonest()
    {
        var hits=OperatorCommandSearch.Rank("Change bowler",Candidates.Select(c=>c.Id=="bowler"?c with{Available=false}:c));
        Assert.Equal("bowler",hits[0].Id);Assert.False(hits[0].Approximate);
        Assert.True(OperatorCommandSearch.Rank("bolwer",Candidates)[0].Approximate);
        Assert.Empty(OperatorCommandSearch.Rank("delete every match",Candidates));
        Assert.Empty(OperatorCommandSearch.Rank("zzzzzz",Candidates));
    }
    [Fact]
    public void EmptyAndShortQueriesAreDeterministicAndContextual()
    {
        Assert.Equal("scorecard",OperatorCommandSearch.Rank("",Candidates)[0].Id);
        Assert.Equal("resume",OperatorCommandSearch.Rank("",Candidates.Select(c=>c.Id=="scorecard"?c with{Available=false,Context=0}:c))[0].Id);
        var shortQuery=OperatorCommandSearch.Rank("s",Candidates);Assert.NotEmpty(shortQuery);Assert.All(shortQuery,h=>Assert.False(h.Approximate));
        Assert.Equal(shortQuery,OperatorCommandSearch.Rank("s",Candidates.Reverse()));
    }
    [Fact]
    public void RepeatedTypingAndPathologicalInputStayBoundedWithoutExecutingAnything()
    {
        var elapsed=Stopwatch.StartNew();
        for(int repeat=0;repeat<250;repeat++)foreach(string query in new[]{"b","bo","bol","bolwer","","volume",new string('x',10000)})
            OperatorCommandSearch.Rank(query,Candidates);
        Assert.True(elapsed.Elapsed<TimeSpan.FromSeconds(4),$"1,750 local searches took {elapsed.Elapsed}.");
    }
}
