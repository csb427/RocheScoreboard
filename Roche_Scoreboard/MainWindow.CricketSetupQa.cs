using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    internal async Task<Dictionary<string, bool>> RunCricketTargetedWorkflowChecksAsync(string directory)
    {
        var checks = await RunSetupReflowChecksAsync(directory);
        foreach (var check in await Services.CricketWorkflowUiChecks.RunAsync(directory)) checks[check.Key] = check.Value;
        foreach (var check in Services.CricketOutputChecks.Run()) checks[check.Key] = check.Value;
        foreach (var check in await RunCricketReviewChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketFreeHitChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketRulesSetupChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketInteractionChecksAsync(directory)) checks[check.Key]=check.Value;
        foreach (var check in await RunCricketOutputHandoffChecksAsync(directory)) checks[check.Key]=check.Value;
        return checks;
    }
    private async Task<Dictionary<string, bool>> RunSetupReflowChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var window = new Window { Width = 1000, Height = 760, ShowInTaskbar = false };
        window.Show();
        try
        {
            foreach (var setup in new (string Name, FrameworkElement View, int Steps)[]
                { ("afl", new SetupWizard(), 3), ("cricket", new CricketSetupWizard(), 4) })
            {
                window.Content = setup.View; await Task.Delay(1000);
                var scroll = FindQaVisual<WizardViewport>(setup.View)!;
                foreach (var dimensions in new[] { (420d, 520d), (760d, 680d), (1500d, 800d) })
                {
                    window.Width = dimensions.Item1; window.Height = dimensions.Item2;
                    await Task.Delay(150);
                    for (int step = 1; step <= setup.Steps; step++)
                    {
                        InvokeCricketQa(setup.View, "ShowStep", step, false);
                        await Task.Delay(100); setup.View.UpdateLayout();
                        var pairs = QaDescendants(setup.View).OfType<Grid>()
                            .Where(g => g.IsVisible && WizardPair.GetMinimumColumnWidth(g) >= 300).ToArray();
                        bool symmetric = pairs.All(g =>
                        {
                            var children = g.Children.OfType<FrameworkElement>().Where(c => c.IsVisible).ToArray();
                            var home = children.FirstOrDefault(c => Grid.GetColumn(c) == 0 && Grid.GetRow(c) == 0);
                            var away = children.FirstOrDefault(c => Grid.GetRow(c) == 1 || Grid.GetColumn(c) == 2);
                            return home is null || away is null || Math.Abs(home.ActualWidth - away.ActualWidth) <= 2;
                        });
                        checks[$"{setup.Name}-setup-step-{step}-symmetric-pairs-{dimensions.Item1}"] = symmetric;
                        checks[$"{setup.Name}-setup-step-{step}-natural-content-{dimensions.Item1}"] = scroll.NaturalHeight
                            && double.IsNaN(((FrameworkElement)scroll.Content).Height) && scroll.ScrollableWidth < 1;
                        scroll.ScrollToBottom(); await Task.Delay(30);
                        var nav = (FrameworkElement)setup.View.FindName("NavBar");
                        checks[$"{setup.Name}-setup-step-{step}-navigation-reachable-{dimensions.Item1}"] = CricketQaBoundsWithin(nav, setup.View);
                        CaptureCricketQa(setup.View, Path.Combine(directory, $"{setup.Name}-setup-step-{step}-{dimensions.Item1}-bottom.png"));
                        scroll.ScrollToTop();
                    }
                }
                if (setup.View is CricketSetupWizard cricket)
                {
                    var first = new CricketPlayer { FirstName = "Alex" };
                    var second = new CricketPlayer { FirstName = "Sam" };
                    var players = (System.Collections.ObjectModel.ObservableCollection<CricketPlayer>)typeof(CricketSetupWizard)
                        .GetField("_homePlayers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cricket)!;
                    players.Clear(); players.Add(first); players.Add(second);
                    checks["setup-keepers-and-captains-are-not-selected-by-default"] = !first.IsWicketkeeper && !second.IsWicketkeeper && !first.IsCaptain;
                    var one = new System.Windows.Controls.Button { Tag = first };
                    var two = new System.Windows.Controls.Button { Tag = second };
                    InvokeCricketQa(cricket, "HomeKeeper_Click", one, new RoutedEventArgs());
                    InvokeCricketQa(cricket, "HomeKeeper_Click", two, new RoutedEventArgs());
                    checks["setup-can-select-multiple-wicketkeepers"] = first.IsWicketkeeper && second.IsWicketkeeper;
                    InvokeCricketQa(cricket, "HomeCaptain_Click", one, new RoutedEventArgs());
                    InvokeCricketQa(cricket, "HomeCaptain_Click", two, new RoutedEventArgs());
                    checks["setup-selects-one-captain-without-changing-keepers"] = !first.IsCaptain && second.IsCaptain && first.IsWicketkeeper && second.IsWicketkeeper;
                }
            }
        }
        finally { window.Close(); }
#if DEBUG
        var source = Services.CricketReliabilityChecks.CreateMatch(true).CapturePersistenceState();
        bool Reconciled(CricketInnings innings) => innings.TotalRuns == innings.Deliveries.Sum(delivery => delivery.Runs)
            && innings.TotalRuns == innings.BattingOrder.Sum(player => player.Runs) + innings.TotalExtras
            && innings.TotalExtras == innings.Wides + innings.NoBalls + innings.Byes + innings.LegByes
            && innings.TotalWickets == innings.BattingOrder.Count(player => player.IsOut)
            && innings.LegalBallsBowled == innings.BowlingAttack.Sum(player => player.BallsBowled)
            && innings.LegalBallsBowled == innings.Deliveries.Count(delivery => delivery.ScoringType is not CricketDeliveryType.Wide and not CricketDeliveryType.NoBall)
            && innings.BowlingAttack.Sum(player => player.RunsConceded) == innings.TotalRuns - innings.Byes - innings.LegByes;
        foreach (string scenario in new[] { "ready", "middle", "second", "chase", "lastover", "target", "firstbreak", "large", "extras", "collapse", "third", "fourth", "rain" })
        {
            var fixture = CreateCricketPreviewScenario(source, scenario);
            checks["debug-scenario-" + scenario] = scenario switch
            {
                "ready" => fixture.TotalRuns == 0 && !fixture.TossPending,
                "middle" => fixture.CurrentInnings!.LegalBallsBowled == 72 && fixture.TotalWickets >= 3 && fixture.TotalRuns > 72,
                "second" => fixture.CurrentInningsNumber==2 && fixture.Target==fixture.AllInnings[0].TotalRuns+1 && fixture.TotalRuns==0,
                "lastover" => fixture.CurrentInningsNumber==2 && fixture.RunsRequired is > 0 and <= 18 && fixture.CurrentInnings!.LegalBallsBowled==114
                    && fixture.TotalOvers*6-fixture.CurrentInnings.LegalBallsBowled==6 && fixture.TotalWickets>0 && !fixture.CurrentInnings.IsComplete,
                "firstbreak" => fixture.CurrentInningsNumber==1 && fixture.CompletionDecisionRequired,
                "large" => fixture.TotalRuns>=240 && fixture.TotalWickets>0 && fixture.CurrentInnings!.LegalBallsBowled==120,
                "extras" => fixture.CurrentInnings!.LegalBallsBowled==4 && fixture.CurrentInnings.Deliveries.Count==6 && !fixture.CurrentInnings.NeedsBowlerSelection,
                "collapse" => fixture.TotalWickets==8,
                "fourth" => fixture.CurrentInningsNumber==4 && fixture.Target==fixture.AllInnings[1].TotalRuns+fixture.AllInnings[2].TotalRuns-fixture.AllInnings[0].TotalRuns+1
                    && fixture.RunsRequired>0 && fixture.AllInnings.Select(innings=>innings.BattingTeamName).SequenceEqual(new[]{fixture.TeamAName,fixture.TeamBName,fixture.TeamBName,fixture.TeamAName}),
                "chase" => fixture.CurrentInningsNumber == 2 && fixture.Target > fixture.TotalRuns && fixture.CurrentInnings!.LegalBallsBowled == 90 && fixture.TotalWickets>0,
                "target" => fixture.CompletionDecisionRequired && fixture.TotalRuns>=fixture.Target && fixture.MatchResult is not null
                    && fixture.CurrentInnings!.Deliveries[^1].Type==CricketDeliveryType.Six,
                "third" => fixture.CurrentInningsNumber == 3 && fixture.TotalRuns>0 && fixture.TotalWickets>0 && fixture.LeadTrailRuns>0,
                "rain" => fixture.BreakKind == CricketBreakKind.Rain,
                _ => false
            };
            checks["debug-scenario-" + scenario + "-ledger-reconciles"] = fixture.AllInnings.All(Reconciled);
            if (scenario is not "ready" and not "second")
                checks["debug-scenario-" + scenario + "-varied-deliveries"] = fixture.CurrentInnings!.Deliveries.Select(delivery=>delivery.Runs).Distinct().Count()>=3
                    && fixture.CurrentInnings.Deliveries.Any(delivery=>delivery.Type==CricketDeliveryType.Dot)
                    && fixture.CurrentInnings.Deliveries.Any(delivery=>delivery.Type is CricketDeliveryType.Four or CricketDeliveryType.Six);
            if (fixture.CurrentInnings!.LegalBallsBowled>=60)
                checks["debug-scenario-" + scenario + "-varied-player-figures"] = fixture.CurrentInnings.BattingOrder.Count(player=>player.BallsFaced>0)>=3
                    && fixture.CurrentInnings.BowlingAttack.Count(player=>player.BallsBowled>0)>=3
                    && fixture.CurrentInnings.BattingOrder.Where(player=>player.BallsFaced>0).Any(player=>player.Runs!=player.BallsFaced);
        }
        checks["debug-scenarios-do-not-mutate-source-match"] = source.AllInnings[0].TotalRuns == 0 && source.AllInnings[0].Deliveries.Count == 0;
        foreach (var check in await RunCricketPreviewRoundtripChecksAsync()) checks[check.Key] = check.Value;
#endif
        return checks;
    }
}
