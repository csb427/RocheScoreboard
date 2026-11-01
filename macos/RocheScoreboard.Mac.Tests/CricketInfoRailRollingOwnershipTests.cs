using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketInfoRailRollingOwnershipTests
{
    private static bool IsRunning(RollingScoreVisual reel) =>
        (bool)typeof(RollingScoreVisual).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reel)!;

    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task UnchangedContextRefreshKeepsTheNativeValueHiddenUntilItsReelFinishes()
    {
        var match = CricketReliabilityChecks.CreateMatch(ready: true);
        match.RecordDelivery(2, CricketDeliveryType.Runs);
        var scorebug = new CricketScorebugControl
        {
            EventAnimationsEnabled = false,
            RollingFiguresEnabled = false,
            BowlerDetailsEnabled = false
        };
        var window = new NativeWindow
        {
            Title = "Cricket Display",
            Width = 386,
            Height = 193,
            Content = scorebug
        };
        window.Show();
        try
        {
            await Settle(180);
            Assert.True(scorebug.IsLoaded);
            scorebug.UpdateFromMatch(match);
            await Settle(650);
            var label = Assert.IsType<TextBlock>(scorebug.FindControl<TextBlock>("InfoLabel"));
            var value = Assert.IsType<TextBlock>(scorebug.FindControl<TextBlock>("InfoValue"));
            Assert.Equal("RUN RATE", label.Text);
            Assert.Equal("12.00", value.Text);
            Assert.Equal(1, value.Opacity, 3);

            scorebug.RollingFiguresEnabled = true;
            match.RecordDelivery(1, CricketDeliveryType.Runs);
            scorebug.UpdateFromMatch(match);

            Assert.Equal(3, match.TotalRuns);
            Assert.Equal(2, match.CurrentInnings!.LegalBallsBowled);
            Assert.Equal("RUN RATE", label.Text);
            Assert.Equal("9.00", value.Text);
            var cell = Assert.IsType<Grid>(value.Parent);
            var reel = Assert.Single(cell.Children.OfType<RollingScoreVisual>());
            Assert.True(reel.MovingDigitCount > 0);
            Assert.Equal(0, value.Opacity, 3);
            string state = JsonSerializer.Serialize(match.CapturePersistenceState());

            // No await: these unchanged refreshes occur before the real 760ms
            // reel can finish and must not reveal a second copy of its digits.
            for (int refresh = 0; refresh < 3; refresh++)
            {
                scorebug.UpdateFromMatch(match);
                Assert.Equal("RUN RATE", label.Text);
                Assert.Equal("9.00", value.Text);
                Assert.Equal(0, value.Opacity, 3);
                Assert.Equal(1, label.Opacity, 3);
            }

            await Settle(950);

            Assert.Equal("RUN RATE", label.Text);
            Assert.Equal("9.00", value.Text);
            Assert.Equal(1, value.Opacity, 3);
            Assert.Equal(1, label.Opacity, 3);
            Assert.Equal(state, JsonSerializer.Serialize(match.CapturePersistenceState()));
        }
        finally
        {
            scorebug.Dispose();
            window.Close();
        }
    }

    private sealed class RollingFixture : IDisposable
    {
        internal readonly CricketMatchManager Match = CricketReliabilityChecks.CreateMatch(ready: true);
        internal readonly CricketScorebugControl Scorebug = new()
        {
            EventAnimationsEnabled = false,
            RollingFiguresEnabled = false,
            BowlerDetailsEnabled = false
        };
        private readonly NativeWindow window;
        internal TextBlock Label => Assert.IsType<TextBlock>(Scorebug.FindControl<TextBlock>("InfoLabel"));
        internal TextBlock Value => Assert.IsType<TextBlock>(Scorebug.FindControl<TextBlock>("InfoValue"));
        internal RollingScoreVisual Reel => Assert.Single(Assert.IsType<Grid>(Value.Parent).Children.OfType<RollingScoreVisual>());

        internal RollingFixture()
        {
            Match.RecordDelivery(2, CricketDeliveryType.Runs);
            window = new NativeWindow { Title = "Cricket Display", Width = 386, Height = 193, Content = Scorebug };
            window.Show();
        }

        internal async Task StartValueReel()
        {
            await Settle(180);
            Assert.True(Scorebug.IsLoaded);
            Scorebug.UpdateFromMatch(Match);
            await Settle(650);
            Assert.Equal("RUN RATE", Label.Text);
            Assert.Equal("12.00", Value.Text);
            Assert.Equal(1, Value.Opacity, 3);
            Scorebug.RollingFiguresEnabled = true;
            Match.RecordDelivery(1, CricketDeliveryType.Runs);
            Scorebug.UpdateFromMatch(Match);
            Assert.Equal("9.00", Value.Text);
            Assert.Equal(0, Value.Opacity, 3);
            Assert.True(IsRunning(Reel));
        }

        public void Dispose()
        {
            Scorebug.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ACategoryChangeStopsTheOldValueReelBeforeItsCaptionFade()
    {
        using var f = new RollingFixture();
        await f.StartValueReel();

        f.Match.SetBreak(CricketBreakKind.Rain);
        f.Scorebug.UpdateFromMatch(f.Match);
        Assert.False(IsRunning(f.Reel));
        string state = JsonSerializer.Serialize(f.Match.CapturePersistenceState());

        await Settle(650);

        Assert.Equal("BREAK", f.Label.Text);
        Assert.Equal("RAIN", f.Value.Text);
        Assert.Equal(1, f.Label.Opacity, 3);
        Assert.Equal(1, f.Value.Opacity, 3);
        // A later ordinary refresh also clears the completed category fade.
        f.Scorebug.UpdateFromMatch(f.Match);
        Assert.Equal(1, f.Value.Opacity, 3);
        Assert.Equal(state, JsonSerializer.Serialize(f.Match.CapturePersistenceState()));
    }

    [AvaloniaFact]
    public async Task RebindingToTheSameValueStopsThePreviousMatchReel()
    {
        using var f = new RollingFixture();
        await f.StartValueReel();
        var replacement = CricketReliabilityChecks.CreateMatch(ready: true);
        replacement.RecordDelivery(1, CricketDeliveryType.Runs);
        replacement.RecordDelivery(2, CricketDeliveryType.Runs);
        Assert.Equal(3, replacement.TotalRuns);
        Assert.Equal(2, replacement.CurrentInnings!.LegalBallsBowled);
        string oldState = JsonSerializer.Serialize(f.Match.CapturePersistenceState());
        string replacementState = JsonSerializer.Serialize(replacement.CapturePersistenceState());

        f.Scorebug.UpdateFromMatch(replacement);

        Assert.False(IsRunning(f.Reel));
        Assert.Equal("RUN RATE", f.Label.Text);
        Assert.Equal("9.00", f.Value.Text);
        Assert.Equal(1, f.Value.Opacity, 3);
        await Settle(950);
        Assert.Equal("RUN RATE", f.Label.Text);
        Assert.Equal("9.00", f.Value.Text);
        Assert.Equal(1, f.Label.Opacity, 3);
        Assert.Equal(1, f.Value.Opacity, 3);
        Assert.Equal(oldState, JsonSerializer.Serialize(f.Match.CapturePersistenceState()));
        Assert.Equal(replacementState, JsonSerializer.Serialize(replacement.CapturePersistenceState()));
    }
}
