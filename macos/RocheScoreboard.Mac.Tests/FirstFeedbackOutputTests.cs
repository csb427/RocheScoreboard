using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory] [InlineData(386,193)] [InlineData(960,480)]
    [Trait("Evidence","FirstFeedbackOutput")]
    public async Task HeaderTextEnlargesInsideExistingPanelsAndWicketWordStaysCompact(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var view=new CricketScorebugControl {RollingFiguresEnabled=false,EventAnimationsEnabled=false};
        try
        {
            view.UpdateFromMatch(match);var window=scope.Window(view,width,height);await Settle(300);
            var wickets=Named<TextBlock>(view,"ScoreWickets");var word=Named<TextBlock>(view,"ScoreSlash");
            var batting=Named<TextBlock>(view,"BattingHeaderText");var bowling=Named<TextBlock>(view,"BowlingHeaderText");
            var clock=Named<TextBlock>(view,"TopTimeText");var weather=Named<TextBlock>(view,"TopWeatherText");
            var top=Named<Border>(view,"BattingHeaderPanel");var bottom=Named<Border>(view,"BowlingHeaderPanel");
            view.UpdateWeatherSnapshot(new WeatherSnapshot(28,27,18,30,0,"☀","Clear",Array.Empty<HourlyForecast>()));await Settle(80);
            PromptCapture(window,$"first-feedback-output-short-{width}");
            var topBox=Painted(window,top);var bottomBox=Painted(window,bottom);
            Assert.True(batting.FontSize>38);Assert.True(bowling.FontSize>38);Assert.True(clock.FontSize>34);Assert.True(weather.FontSize>36);
            double one=wickets.MinWidth,scoreFont=wickets.FontSize;
            Assert.True(word.Margin.Left<=8);Assert.True(one<scoreFont*.8);
            for(int i=0;i<3;i++)
            {
                match.CurrentInnings!.TotalWickets=i==1?10:9;view.UpdateFromMatch(match);await Settle(80);
                Assert.Equal(scoreFont,wickets.FontSize);Assert.InRange(wickets.MinWidth/one,i==1?1.99:.99,i==1?2.01:1.01);
                Assert.True(Painted(window,wickets).Right<=Painted(window,word).Left+.5);
            }
            match.CurrentInnings!.TotalWickets=0;
            match.CurrentInnings.BattingTeamName="An extraordinarily long cricket club name";
            match.CurrentInnings.BowlingTeamName="Another extraordinarily long cricket club name";
            string before=State(match);view.UpdateFromMatch(match);await Settle(80);
            Assert.Equal(topBox,Painted(window,top));Assert.Equal(bottomBox,Painted(window,bottom));
            Assert.True(Painted(window,batting).Right<=Painted(window,weather).Left+.5);
            Assert.True(Painted(window,weather).Right<=Painted(window,clock).Left+.5);
            Assert.True(Painted(window,clock).Right<=topBox.Right+.5);
            Assert.Equal(before,State(match));
            PromptCapture(window,$"first-feedback-output-long-{width}");
        }
        finally {view.Dispose();}
    }
}
