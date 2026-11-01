// Public ScorebugControl component route, following NativeProductionTests.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
using Path = System.IO.Path;

namespace RocheScoreboard.Mac.Tests;

public sealed class RecentScoreCardMotionTests
{
    private static async Task Pump(int ms = 100)
    { await Task.Delay(ms); Dispatcher.UIThread.RunJobs(); }

    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(root.FindControl<T>(name));

    private sealed record Pose(double Opacity, bool Clock);
    private static async Task<Pose[]> Sample(Border card)
    {
        var poses = new List<Pose>();
        for (int i = 0; i < 6; i++)
        {
            await Pump(150);
            poses.Add(new(card.Opacity, Motion.IsAnimating(card)));
        }
        return poses.ToArray();
    }
    private static void Static(Border card)
    {
        Assert.False(Motion.IsAnimating(card), "Recent event card must have no perpetual opacity clock.");
        Assert.Equal(1d, card.Opacity, 6);
    }
    private static void Static(IEnumerable<Pose> poses) => Assert.All(poses, pose =>
    {
        Assert.False(pose.Clock, "Newest card must remain static through the former 640ms pulse phase.");
        Assert.Equal(1d, pose.Opacity, 6);
    });

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly MatchManager Match = new();
        internal readonly ScorebugControl View = new();
        internal readonly Window Output;
        internal int Changes;
        private readonly string assets = Path.Combine(Path.GetTempPath(), "roche-recent-card-" + Guid.NewGuid().ToString("N"));
        internal Grid Cards => Named<Grid>(View, "ExpRecentScoresCards");
        internal Border[] Rows => Cards.Children.OfType<Border>().ToArray();
        internal string State => JsonSerializer.Serialize(Match.ToState());

        internal Fixture(ScorebugLayout layout, bool logos, int events = 4)
        {
            Match.SetTeams("Roche Home", "RCH", "Visitors", "VIS");
            if (events > 0) Match.AddGoal(TeamSide.Home);
            if (events > 1) Match.AddBehind(TeamSide.Away);
            if (events > 2) Match.AddGoal(TeamSide.Away);
            if (events > 3) Match.AddBehind(TeamSide.Home);
            Match.MatchChanged += () => Changes++;
            View.SetLayout(layout);
            View.SetTeams(Match.HomeName, Match.HomeAbbr, Match.AwayName, Match.AwayAbbr);
            View.SetTeamColors("#123456", "#9A1821", "#FFFFFF", "#FFFFFF");
            View.SetScores(Match.HomeGoals, Match.HomeBehinds, Match.AwayGoals, Match.AwayBehinds);
            View.SetQuarter(Match.Quarter);
            View.SetClock("03:21");
            if (logos)
            {
                Directory.CreateDirectory(assets);
                Logo(Path.Combine(assets, "home.png"), Colors.Navy);
                Logo(Path.Combine(assets, "away.png"), Colors.DarkRed);
                View.SetLogos(Path.Combine(assets, "home.png"), Path.Combine(assets, "away.png"));
            }
            else View.SetLogos(null, null);
            Output = new Window { Width = 386, Height = 193, Content = View };
        }

        // Ordinary owned logo assets, following AflLogoCropIsolationTests.
        // This never fabricates a scoreboard frame or injects a private visual.
        private static void Logo(string path, Color color)
        {
            var content = new Border { Width = 48, Height = 48, Background = new SolidColorBrush(color),
                Child = new Border { Width = 20, Height = 28, Background = Brushes.White } };
            content.Measure(new Size(48, 48)); content.Arrange(new Rect(0, 0, 48, 48));
            using var bitmap = new RenderTargetBitmap(new PixelSize(48, 48), new Vector(96, 96));
            bitmap.Render(content); bitmap.Save(path);
        }
        internal async Task Show()
        {
            Output.Show(); await Pump(200);
            Assert.True(View.IsLoaded); Assert.True(View.IsEffectivelyVisible);
            Assert.False(View.IsTickerActive);
            View.ShowRecentScores(Match.Events, Match.Quarter);
            await Pump(800);
            Assert.True(View.IsOverlayActive);
            Assert.True(Named<Border>(View, "ExpRecentScoresBarBorder").IsEffectivelyVisible);
        }
        internal void Unchanged(string before)
        {
            Assert.Equal(before, State); Assert.Equal(0, Changes);
            Assert.Equal(Match.HomeTotal.ToString(), Named<TextBlock>(View, "ExpHomeTotalText").Text);
            Assert.Equal(Match.AwayTotal.ToString(), Named<TextBlock>(View, "ExpAwayTotalText").Text);
            Assert.Equal("03:21", Named<TextBlock>(View, "ExpClockText").Text);
        }
        internal void Capture(string name, object evidence)
        {
            string directory = Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "roche-recent-card-evidence"), name);
            Directory.CreateDirectory(directory);
            // No capture or PNG encoding occurs inside the phase samples.
            using var frame = Output.CaptureRenderedFrame();
            Assert.NotNull(frame); frame.Save(Path.Combine(directory, "drawer.png"));
            File.WriteAllText(Path.Combine(directory, "observations.json"), JsonSerializer.Serialize(evidence,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        public async ValueTask DisposeAsync()
        {
            View.ClearOverlayQueue(); View.SetMarqueeMessages(Array.Empty<string>());
            Output.Content = View; Output.Close(); await Pump(150);
            if (Directory.Exists(assets)) Directory.Delete(assets, true);
        }
    }

    private static void Identity(Border card, string label, Color team, byte alpha, bool logos)
    {
        Assert.Equal(label, Assert.Single(card.GetVisualDescendants().OfType<TextBlock>()).Text);
        Assert.Equal(Color.FromArgb(alpha, team.R, team.G, team.B),
            Assert.IsAssignableFrom<ISolidColorBrush>(card.BorderBrush).Color);
        Assert.IsAssignableFrom<ILinearGradientBrush>(card.Background);
        if (logos)
        {
            Assert.Equal(2, card.GetVisualDescendants().OfType<Image>().Count());
            Assert.All(card.GetVisualDescendants().OfType<Image>(), image => Assert.NotNull(image.Source));
            Assert.Empty(card.GetVisualDescendants().OfType<Ellipse>());
        }
        else
        {
            Assert.Empty(card.GetVisualDescendants().OfType<Image>());
            Assert.Equal(team, Assert.IsAssignableFrom<ISolidColorBrush>(
                Assert.Single(card.GetVisualDescendants().OfType<Ellipse>()).Fill).Color);
        }
    }

    [AvaloniaTheory]
    [InlineData(ScorebugLayout.Expanded, false)]
    [InlineData(ScorebugLayout.Expanded, true)]
    [InlineData(ScorebugLayout.Distance, false)]
    [InlineData(ScorebugLayout.Distance, true)]
    public async Task ActualCompactDrawerKeepsThreeNewestTeamCardsOpaque(ScorebugLayout layout, bool logos)
    {
        await using var f = new Fixture(layout, logos);
        string before = f.State; await f.Show();
        var cards = f.Rows; Assert.Equal(3, cards.Length);
        Identity(cards[0], "B", Color.Parse("#123456"), 0xFF, logos);
        Identity(cards[1], "G", Color.Parse("#9A1821"), 0xA0, logos);
        Identity(cards[2], "B", Color.Parse("#9A1821"), 0xA0, logos);
        Assert.Equal(new[] { 0, 1, 2 }, cards.Select(Grid.GetRow));
        Assert.All(cards, card =>
        {
            Assert.True(card.IsEffectivelyVisible); Assert.True(card.Bounds.Width > 0); Assert.True(card.Bounds.Height > 0);
            var top = card.TranslatePoint(default, f.Output);
            var bottom = card.TranslatePoint(new Point(card.Bounds.Width, card.Bounds.Height), f.Output);
            Assert.NotNull(top); Assert.NotNull(bottom);
            Assert.InRange(top.Value.X, -1, f.Output.Bounds.Width + 1);
            Assert.InRange(top.Value.Y, -1, f.Output.Bounds.Height + 1);
            Assert.InRange(bottom.Value.X, -1, f.Output.Bounds.Width + 1);
            Assert.InRange(bottom.Value.Y, -1, f.Output.Bounds.Height + 1);
        });
        if (logos)
            for (int i = 0; i < cards.Length; i++)
                Assert.All(cards[i].GetVisualDescendants().OfType<Image>(), image =>
                    Assert.Same(Named<Image>(f.View, i == 0 ? "ExpHomeLogoImage" : "ExpAwayLogoImage").Source, image.Source));
        var samples = await Sample(cards[0]);
        f.Capture($"compact-{layout}-logos-{logos}", new { samples, diagnostics = f.View.GetRecentScoresDiagnosticsForQa() });
        f.Unchanged(before); Static(samples); Assert.All(cards, Static);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task EmptyAndSingleEventKeepExactContentWithoutInventingScores(int count)
    {
        await using var f = new Fixture(ScorebugLayout.Distance, false, count);
        string before = f.State; await f.Show();
        if (count == 0)
        {
            Assert.Empty(f.Rows);
            Assert.Equal("NO SCORES RECORDED", Assert.Single(f.Cards.Children.OfType<TextBlock>()).Text);
        }
        else
        {
            Identity(Assert.Single(f.Rows), "G", Color.Parse("#123456"), 0xFF, false);
        }
        f.Capture("event-count-" + count, new { count, diagnostics = f.View.GetRecentScoresDiagnosticsForQa() });
        f.Unchanged(before); Assert.All(f.Rows, Static);
    }

    [AvaloniaTheory]
    [InlineData("hide")]
    [InlineData("clear")]
    [InlineData("replace")]
    [InlineData("detach")]
    public async Task PublicDrawerLifetimeLeavesOldAndCurrentCardsWithoutClocks(string boundary)
    {
        await using var f = new Fixture(ScorebugLayout.Distance, false);
        string before = f.State; await f.Show(); var old = f.Rows;
        bool oldClockBefore = Motion.IsAnimating(old[0]);
        if (boundary == "hide")
        {
            f.View.HideRecentScores(); await Pump(850); Assert.False(f.View.IsOverlayActive);
            Assert.Contains("progress=0", f.View.GetRecentScoresDiagnosticsForQa());
        }
        if (boundary is "clear" or "replace")
        { f.View.ClearOverlayQueue(); await Pump(100); Assert.False(f.View.IsOverlayActive); }
        if (boundary == "detach")
        { f.Output.Content = null; await Pump(150); Assert.False(f.View.IsLoaded); }
        bool oldClockAfterBoundary = Motion.IsAnimating(old[0]);
        if (boundary == "detach")
        { f.Output.Content = f.View; await Pump(200); Assert.True(f.View.IsLoaded); f.View.ClearOverlayQueue(); }
        // Replacement is reached through an ordinary clear + public show. The
        // same valid historical event list remains owned by the untouched model.
        var shown = boundary == "replace" ? f.Match.Events.Take(1).ToArray() : f.Match.Events.ToArray();
        f.View.ShowRecentScores(shown, f.Match.Quarter); await Pump(800);
        Assert.True(f.View.IsOverlayActive); var current = f.Rows;
        Assert.Equal(boundary == "replace" ? 1 : 3, current.Length);
        Assert.NotSame(old[0], current[0]); Assert.Null(old[0].GetVisualParent());
        Identity(current[0], boundary == "replace" ? "G" : "B", Color.Parse("#123456"), 0xFF, false);
        var samples = await Sample(current[0]);
        f.Capture("lifetime-" + boundary, new { boundary, oldClockBefore, oldClockAfterBoundary, samples,
            oldClocks = old.Select(Motion.IsAnimating).ToArray(), diagnostics = f.View.GetRecentScoresDiagnosticsForQa() });
        f.Unchanged(before);
        Assert.False(oldClockAfterBoundary); Assert.All(old, Static); Assert.All(current, Static); Static(samples);
        f.View.ClearOverlayQueue(); f.Output.Content = null; await Pump(150);
        Assert.All(current, Static); Assert.False(f.View.IsLoaded);
    }
}
