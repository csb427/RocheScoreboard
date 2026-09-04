using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>
/// Developer-only, in-memory visual regression gallery for AFL output surfaces.
/// Launch with --afl-presentation-gallery. It never reads or writes match state.
/// </summary>
public partial class AflPresentationGalleryWindow : Window
{
    private readonly Dictionary<int, Action> _repeatRefreshes = new();
    private readonly Dictionary<int, Action> _quarterBreakRefreshes = new();
    private readonly Dictionary<int, List<FrameworkElement>> _presentationViews = new();
    private readonly List<(string Name, ScorebugControl View)> _scorebugPreviews = new();
    private readonly List<(string Name, ScorebugControl View, Action<ScorebugControl> Refresh)> _overlayPreviews = new();
    private readonly List<(string Name, QuarterBreakScorebugControl View)> _quarterBreakScorebugPreviews = new();
    private StatsScreenControl? _whitePrimaryStatsPreview;
    private const string HomePrimary = "#FF3338";
    private const string AwayPrimary = "#FACC0D";
    private const string HomeSecondary = "#FFFFFF";
    private const string AwaySecondary = "#015290";
    private static string? SampleLogoPath
    {
        get
        {
            string path = Path.Combine(AppContext.BaseDirectory, "app.png");
            if (File.Exists(path)) return path;
            string sourceTreePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "app.png"));
            return File.Exists(sourceTreePath) ? sourceTreePath : null;
        }
    }

    public AflPresentationGalleryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(BuildGallerySafely);
    }

    private void BuildGallerySafely()
    {
        try
        {
            BuildGallery();
        }
        catch (Exception ex)
        {
            GalleryErrorText.Text = $"Gallery population failed: {ex.GetType().Name}: {ex.Message}";
            GalleryErrorText.Visibility = Visibility.Visible;
        }
    }

    private void BuildGallery()
    {
        MatchManager match = BuildRepresentativeMatch();
        MatchManager quarterBreakMatch = BuildRepresentativeMatch();
        quarterBreakMatch.EndQuarter();
        string halfTimeLabel = QuarterLabelService.GetBreakLabel(2);
        AddScorebugPreview("default-386x193", "DEFAULT 386 × 193", 386, 193);
        AddScorebugPreview("compact-286x193", "COMPACT 286 × 193", 286, 193);
        AddScorebugPreview("distance-386x193", "DISTANCE 386 × 193", 386, 193,
            view => view.SetLayout(ScorebugLayout.Distance));
        AddScorebugPreview("distance-286x193", "DISTANCE 286 × 193", 286, 193,
            view => view.SetLayout(ScorebugLayout.Distance));
        AddScorebugPreview("finals-386x193", "FINALS 386 × 193", 386, 193,
            view => view.SetFinalsMode(true));
        AddScorebugPreview("finals-286x193", "FINALS 286 × 193", 286, 193,
            view => view.SetFinalsMode(true));
        AddScorebugPreview("design-707x420", "LARGE 707 × 420", 707, 420);
        AddScorebugPreview("thin-240x320", "THIN 240 × 320", 240, 320);
        AddScorebugPreview("small-wide-320x120", "SMALL WIDE 320 × 120", 320, 120);
        AddScorebugPreview("short-wide-480x135", "SHORT WIDE 480 × 135", 480, 135);
        AddScorebugPreview("ultra-compact-280x105", "ULTRA COMPACT 280 × 105", 280, 105);
        AddScorebugPreview("hidden-names-320x120", "HIDDEN NAMES 320 × 120", 320, 120,
            view => view.SetHideTeamNames(true));
        AddScorebugPreview("long-team-names-386x193", "LONG TEAM NAMES 386 × 193", 386, 193,
            view => view.SetTeams("University Blacks Football Club", "UBL", "Old Paradians Football Club", "OPC"));
        AddScorebugPreview("ticker-lock-386x193", "TICKER LOCK 386 × 193", 386, 193,
            view =>
            {
                view.SetMarqueeMessages(new List<MarqueeMessage>
                {
                    new("Welcome to today's match", "#FFFFFF", "#1E40AF", isEnabled: true)
                });
                view.ShowStatsBar(MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter));
            });

        AddOverlayPreview("SCORING RUN", view =>
            view.ShowGoalRun("Xavier", 7, 8, consecutive: false, colorHex: HomePrimary));
        AddOverlayPreview("RECENT SCORES", view => view.ShowRecentScores(match.Events, match.Quarter));
        AddOverlayPreview("MATCH STAT", view =>
            view.ShowStatsBar(MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter)));
        AddOverlayPreview("QUARTER SCORE", view => view.ShowQuarterScores(2, 3, 2, 2, 4));
        AddOverlayPreview("WIN PROBABILITY", view => view.ShowWinProbability(new WinProbabilityResult
        {
            HomeWinPct = 0.62,
            AwayWinPct = 0.36,
            DrawPct = 0.02,
            HomeDelta = 0.08
        }));
        AddOverlayPreview("TEAM DROUGHT", view =>
            view.ShowTeamDrought("Carey", DateTime.Now.AddMinutes(-8.5), AwayPrimary, AwaySecondary));
        AddOverlayPreview("LONG TEAM DROUGHT", view =>
        {
            view.SetTeams("Xavier College Football Club", "XCF", "Carey", "CY");
            view.ShowTeamDrought("Xavier College Football Club", "XCF", "Goal", TimeSpan.FromMinutes(18), HomePrimary);
        });
        // Long legal values exercise the fixed 88:88 width contract instead of
        // letting the common one-minute case hide compact-output clipping.
        AddOverlayPreview("LAST SCORE", view =>
            view.ShowLastEventTime("Score", TimeSpan.FromMinutes(88) + TimeSpan.FromSeconds(58)));
        AddOverlayPreview("LAST GOAL", view =>
            view.ShowLastEventTime("Goal", TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(8)));
        AddOverlayPreview("LEAD CHANGES", view => view.ShowLeadChangesBar(7));
        AddOverlayPreview("FIVE MINUTE WARNING", view => view.ShowFiveMinuteWarning());
        AddOverlayPreview("WHITE PRIMARY STAT", view =>
        {
            view.SetTeamColors("#FFFFFF", AwayPrimary, "#111827", AwaySecondary);
            view.ShowStatsBar(MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter));
        });

        var breakViews = new List<BreakScreenControl>();
        var breakLifecycleViews = new List<FrameworkElement>();
        AddPresentationPair(BreakHost, () =>
        {
            var view = new BreakScreenControl();
            view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
            breakViews.Add(view);
            breakLifecycleViews.Add(view);
            return view;
        });
        _presentationViews[2] = breakLifecycleViews;
        _repeatRefreshes[2] = () =>
        {
            foreach (BreakScreenControl view in breakViews)
                view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
        };
        _quarterBreakRefreshes[2] = () =>
        {
            foreach (BreakScreenControl view in breakViews)
                view.Populate(quarterBreakMatch, 2, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, halfTimeLabel);
        };

        foreach ((string name, double width, double height) in new[]
                 {
                     ("386x193", 386d, 193d),
                     ("286x193", 286d, 193d)
                 })
        {
            var quarterScorebug = new QuarterBreakScorebugControl();
            quarterScorebug.Populate(quarterBreakMatch, 1,
                HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary,
                SampleLogoPath, SampleLogoPath,
                new WeatherSnapshot(22, 20, 14, 27, 0, "☀", "Clear sky", Array.Empty<HourlyForecast>()));
            _quarterBreakScorebugPreviews.Add((name, quarterScorebug));
            AddPreview(BreakHost, $"BREAK SCOREBUG {width:0} × {height:0}", quarterScorebug, width, height);
        }

        var wormViews = new List<ScorewormControl>();
        var wormLifecycleViews = new List<FrameworkElement>();
        AddPresentationPair(WormHost, () =>
        {
            var view = new ScorewormControl();
            view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
            wormViews.Add(view);
            wormLifecycleViews.Add(view);
            return view;
        });
        _presentationViews[3] = wormLifecycleViews;
        _repeatRefreshes[3] = () =>
        {
            foreach (ScorewormControl view in wormViews)
                view.Populate(match, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
        };
        _quarterBreakRefreshes[3] = () =>
        {
            foreach (ScorewormControl view in wormViews)
                view.Populate(quarterBreakMatch, 2, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, halfTimeLabel);
        };

        var statsViews = new List<StatsScreenControl>();
        var statsLifecycleViews = new List<FrameworkElement>();
        AddPresentationPair(StatsHost, () =>
        {
            var view = new StatsScreenControl();
            view.Populate(match, MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter), 1,
                HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
            statsViews.Add(view);
            statsLifecycleViews.Add(view);
            return view;
        });
        var whitePrimaryStats = new StatsScreenControl();
        whitePrimaryStats.Populate(match, MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter), 1,
            "#FFFFFF", AwayPrimary, "#111827", AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
        AddPreview(StatsHost, "WHITE PRIMARY 386 × 193", whitePrimaryStats, 386, 193);
        _whitePrimaryStatsPreview = whitePrimaryStats;
        _presentationViews[4] = statsLifecycleViews;
        _repeatRefreshes[4] = () =>
        {
            MatchStats stats = MatchStats.Calculate(match, match.Quarter, match.ElapsedInQuarter);
            foreach (StatsScreenControl view in statsViews)
                view.Populate(match, stats, 1, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
        };
        _quarterBreakRefreshes[4] = () =>
        {
            MatchStats stats = MatchStats.Calculate(quarterBreakMatch, quarterBreakMatch.Quarter, quarterBreakMatch.ElapsedInQuarter);
            foreach (StatsScreenControl view in statsViews)
                view.Populate(quarterBreakMatch, stats, 2, HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, halfTimeLabel);
        };
        AddPresentationPair(WeatherHost, () =>
        {
            var view = new WeatherScreenControl();
            var weather = new WeatherSnapshot(22, 20, 14, 27, 0, "☀", "Clear sky", Array.Empty<HourlyForecast>());
            view.Populate(weather, "Melbourne", match, 1,
                HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary, SampleLogoPath, SampleLogoPath, "Q2");
            return view;
        });
    }

    /// <summary>
    /// Writes one deterministic PNG per gallery tab for visual regression QA.
    /// The gallery uses only its representative in-memory match.
    /// </summary>
    public async Task CaptureSnapshotsAsync(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("A snapshot output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);
        string[] names = ["scorebug", "overlays", "quarter-breakdown", "margin-flow", "match-stats", "weather"];
        for (int index = 0; index < GalleryTabs.Items.Count; index++)
        {
            GalleryTabs.SelectedIndex = index;
            UpdateLayout();
            // Loaded handlers drive overlay expansion and responsive reflow.
            // Let those settle before capturing the tab's stable frame.
            await Task.Delay(index == 1 ? 900 : 120);
            UpdateLayout();

            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string name = index < names.Length ? names[index] : $"tab-{index + 1}";
            using FileStream output = File.Create(Path.Combine(outputDirectory, name + ".png"));
            encoder.Save(output);
        }

        // Save every scorebug dimension independently as well. The live gallery
        // intentionally scrolls when its preview cards exceed the window width;
        // isolated captures ensure compact cases can never escape visual review.
        GalleryTabs.SelectedIndex = 0;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        foreach ((string name, ScorebugControl view) in _scorebugPreviews)
        {
            view.UpdateLayout();
            await Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            CaptureElement(view, outputDirectory, "scorebug-" + name);
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "scorebug-typography.txt"),
            _scorebugPreviews.Select(preview =>
            {
                (double gb, double total) = preview.View.GetScoreFontSizesForQa();
                return $"{preview.Name}: goals/behinds={gb:0.##}px, total={total:0.##}px";
            }));
        await CaptureGoalAnimationPackagesAsync(outputDirectory);
        GalleryTabs.SelectedIndex = 2;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        foreach ((string name, QuarterBreakScorebugControl view) in _quarterBreakScorebugPreviews)
        {
            view.UpdateLayout();
            CaptureElement(view, outputDirectory, "quarter-break-scorebug-" + name);
        }
        var screensaver = new OutputScreensaverControl { Width = 386, Height = 193 };
        screensaver.ShowDefault();
        screensaver.Measure(new System.Windows.Size(386, 193));
        screensaver.Arrange(new System.Windows.Rect(0, 0, 386, 193));
        screensaver.UpdateLayout();
        CaptureElement(screensaver, outputDirectory, "screensaver-default-386x193");
        ScorebugControl? tickerLock = _scorebugPreviews
            .FirstOrDefault(preview => preview.Name == "ticker-lock-386x193").View;
        if (tickerLock is not null)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "ticker-overlay-lock.txt"),
                $"ticker-active={tickerLock.IsTickerActive}{Environment.NewLine}" +
                $"overlay-active={tickerLock.IsOverlayActive}{Environment.NewLine}");
        }
        // Overlay previews live in the second tab. Keep their ancestor visible
        // while refreshing dynamic card content; WPF intentionally skips layout
        // for descendants of a collapsed TabItem.
        GalleryTabs.SelectedIndex = 1;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        foreach ((string name, ScorebugControl view, Action<ScorebugControl> refresh) in _overlayPreviews)
        {
            // Each isolated frame must show the settled, fully-open state. Some
            // overlays intentionally auto-hide in production, and gallery-wide
            // capture can legitimately take longer than that timeout.
            view.ResetAllOverlayState();
            refresh(view);
            await Task.Delay(1000);
            view.UpdateLayout();
            await Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            CaptureElement(view, outputDirectory, "overlay-" + name);
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "overlay-scorebug-metrics.txt"),
            _overlayPreviews.Select(preview =>
            {
                (double gb, double total) = preview.View.GetScoreFontSizesForQa();
                (double homeName, double awayName) = preview.View.GetTeamNamePanelHeightsForQa();
                return $"{preview.Name}: goals/behinds={gb:0.##}px, total={total:0.##}px, " +
                       $"name-rows={homeName:0.##}/{awayName:0.##}px; {preview.View.GetOverlayStateDiagnosticsForQa()}";
            }));
        File.WriteAllLines(Path.Combine(outputDirectory, "recent-scores-layout.txt"),
            _overlayPreviews
                .Where(preview => preview.Name.StartsWith("recent-scores", StringComparison.Ordinal))
                .Select(preview => $"{preview.Name}: {preview.View.GetRecentScoresDiagnosticsForQa()}"));
        File.WriteAllLines(Path.Combine(outputDirectory, "goal-run-detector.txt"),
            RunGoalDetectorChecks());
        File.WriteAllLines(Path.Combine(outputDirectory, "drought-calculator.txt"),
            RunDroughtCalculatorChecks());
        if (_whitePrimaryStatsPreview is not null)
        {
            _whitePrimaryStatsPreview.UpdateLayout();
            await Dispatcher.InvokeAsync(_whitePrimaryStatsPreview.UpdateLayout,
                System.Windows.Threading.DispatcherPriority.Render);
            CaptureElement(_whitePrimaryStatsPreview, outputDirectory, "match-stats-white-primary-386x193");
        }
        await CaptureScorebugLifecycleStressAsync(outputDirectory);
        await CaptureDistanceOperationsStressAsync(outputDirectory);
        await CaptureOverlayNamePanelStressAsync(outputDirectory);
        await CaptureImmediateDroughtSwitchCheckAsync(outputDirectory);
        await CaptureOverlayHandoffAndTickerCheckAsync(outputDirectory);
        await CaptureTeamStoryScoreDeferralAsync(outputDirectory);

        // Capture the three stateful data presentations again after the same
        // Populate calls production makes during clock ticks and repeated
        // selection. These companion frames make lifecycle regressions visible
        // instead of validating only the first exposure.
        foreach (int index in new[] { 2, 3, 4 })
        {
            if (!_repeatRefreshes.TryGetValue(index, out Action? refresh))
                continue;

            GalleryTabs.SelectedIndex = index;
            UpdateLayout();
            refresh();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Loaded);

            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string name = names[index] + "-repeat";
            using FileStream output = File.Create(Path.Combine(outputDirectory, name + ".png"));
            encoder.Save(output);
        }

        foreach (int index in new[] { 2, 3, 4 })
            await CaptureLifecycleStressAsync(index, names[index], outputDirectory);

        // Repeat the same stability check at a real quarter break. This guards
        // the shared title/timer/bottom-bar state as the model advances from a
        // live quarter to the next quarter with zero elapsed time.
        foreach (int index in new[] { 2, 3, 4 })
        {
            if (!_quarterBreakRefreshes.TryGetValue(index, out Action? refresh))
                continue;

            GalleryTabs.SelectedIndex = index;
            UpdateLayout();
            refresh();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Loaded);
            CaptureCurrentWindow(outputDirectory, names[index] + "-quarter-break");

            refresh();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Loaded);
            CaptureCurrentWindow(outputDirectory, names[index] + "-quarter-break-repeat");
        }
    }

    private static IReadOnlyList<string> RunDroughtCalculatorChecks()
    {
        var scoreMatch = new MatchManager();
        scoreMatch.SetElapsed(TimeSpan.FromMinutes(12));
        scoreMatch.AddGoal(TeamSide.Home);
        scoreMatch.SetElapsed(TimeSpan.FromMinutes(18));
        scoreMatch.EndQuarter();
        scoreMatch.SetElapsed(TimeSpan.FromMinutes(2));

        AflDroughtSnapshot afterBreak = AflDroughtCalculator.Calculate(scoreMatch);
        var lines = new List<string>
        {
            $"quarter-break-excluded: expected-score=8; actual-score={afterBreak.Match.Elapsed.TotalMinutes:0}; pass={Math.Abs(afterBreak.Match.Elapsed.TotalMinutes - 8) < 0.01}",
        };

        var behindMatch = new MatchManager();
        behindMatch.SetElapsed(TimeSpan.FromMinutes(12));
        behindMatch.AddBehind(TeamSide.Home);
        behindMatch.SetElapsed(TimeSpan.FromMinutes(18));
        behindMatch.EndQuarter();
        behindMatch.SetElapsed(TimeSpan.FromMinutes(2));
        AflDroughtSnapshot afterBehind = AflDroughtCalculator.Calculate(behindMatch);
        lines.Add($"goal-story-after-behind: expected=Goal; actual={afterBehind.Match.Kind}; pass={afterBehind.Match.Kind == AflDroughtEventKind.Goal}");

        behindMatch.SetElapsed(TimeSpan.FromMinutes(4));
        AflDroughtSnapshot tenMinuteScore = AflDroughtCalculator.Calculate(behindMatch);
        lines.Add($"score-precedence-at-ten: expected=Score/10; actual={tenMinuteScore.Match.Kind}/{tenMinuteScore.Match.Elapsed.TotalMinutes:0}; pass={tenMinuteScore.Match.Kind == AflDroughtEventKind.Score && Math.Abs(tenMinuteScore.Match.Elapsed.TotalMinutes - 10) < 0.01}");
        return lines;
    }

    private async Task CaptureImmediateDroughtSwitchCheckAsync(string outputDirectory)
    {
        ScorebugControl? view = _overlayPreviews
            .FirstOrDefault(preview => preview.Name == "team-drought-386x193").View;
        if (view is null) return;

        GalleryTabs.SelectedIndex = 1;
        view.ResetAllOverlayState();
        view.ShowTeamDrought("Xavier", "XAV", "Score", TimeSpan.FromMinutes(11), HomePrimary);
        await Task.Delay(90);
        view.ShowTeamDrought("Carey", "CY", "Goal", TimeSpan.FromMinutes(14), AwayPrimary);
        await Task.Delay(2700);
        view.UpdateLayout();
        File.WriteAllText(Path.Combine(outputDirectory, "drought-immediate-switch.txt"),
            view.GetOverlayStateDiagnosticsForQa());
        CaptureElement(view, outputDirectory, "overlay-team-drought-immediate-switch-386x193");
        view.ResetAllOverlayState();
    }

    private async Task CaptureOverlayHandoffAndTickerCheckAsync(string outputDirectory)
    {
        ScorebugControl? handoff = _overlayPreviews
            .FirstOrDefault(preview => preview.Name == "scoring-run-386x193").View;
        if (handoff is not null)
        {
            handoff.ResetAllOverlayState();
            handoff.ShowGoalRun("Xavier", 5, 6, false, HomePrimary);
            await Task.Delay(90);
            handoff.ShowTeamDrought("Carey", "CY", "Goal", TimeSpan.FromMinutes(12), AwayPrimary);
            await Task.Delay(90);
            handoff.ShowFiveMinuteWarning();
            await Task.Delay(1700);
            File.WriteAllText(Path.Combine(outputDirectory, "overlay-handoff-stress.txt"),
                handoff.GetOverlayStateDiagnosticsForQa());
            CaptureElement(handoff, outputDirectory, "overlay-handoff-final-warning-386x193");
            handoff.ResetAllOverlayState();
        }

        ScorebugControl? ticker = _scorebugPreviews
            .FirstOrDefault(preview => preview.Name == "ticker-lock-386x193").View;
        if (ticker is null) return;
        ticker.ResetAllOverlayState();
        bool tickerBefore = ticker.IsTickerActive;
        ticker.ShowFiveMinuteWarning();
        await Task.Delay(1000);
        bool warningActive = ticker.IsOverlayActive;
        bool tickerDuring = ticker.IsTickerActive;
        ticker.HideFiveMinuteWarning();
        await Task.Delay(1200);
        File.WriteAllText(Path.Combine(outputDirectory, "warning-ticker-handoff.txt"),
            $"ticker-before={tickerBefore}{Environment.NewLine}" +
            $"warning-active={warningActive}{Environment.NewLine}" +
            $"ticker-during={tickerDuring}{Environment.NewLine}" +
            $"ticker-restored={ticker.IsTickerActive}{Environment.NewLine}" +
            $"overlay-after={ticker.IsOverlayActive}{Environment.NewLine}");
    }

    private async Task CaptureTeamStoryScoreDeferralAsync(string outputDirectory)
    {
        ScorebugControl? view = _overlayPreviews
            .FirstOrDefault(preview => preview.Name == "scoring-run-386x193").View;
        if (view is null) return;

        view.ResetAllOverlayState();
        view.ShowGoalRun("Xavier", 6, 7, false, HomePrimary);
        await Task.Delay(1000);
        bool featuredDeferred = view.DeferScoreUntilTeamPanelsVisible(scoringTeamIsHome: true, () => { });
        bool released = false;
        bool deferred = view.DeferScoreUntilTeamPanelsVisible(scoringTeamIsHome: false, () => released = true);
        await Task.Delay(350);
        view.UpdateLayout();
        CaptureElement(view, outputDirectory, "team-story-score-exit-midpoint-386x193");
        bool releasedMidTransition = released;
        await Task.Delay(650);
        File.WriteAllText(Path.Combine(outputDirectory, "team-story-score-deferral.txt"),
            $"featured-team-deferred={featuredDeferred}{Environment.NewLine}" +
            $"deferred={deferred}{Environment.NewLine}" +
            $"released-mid-transition={releasedMidTransition}{Environment.NewLine}" +
            $"released-after-exit={released}{Environment.NewLine}" +
            $"overlay-after-exit={view.IsOverlayActive}{Environment.NewLine}" +
            view.GetOverlayStateDiagnosticsForQa());
        CaptureElement(view, outputDirectory, "team-story-score-after-exit-386x193");
        view.ResetAllOverlayState();
    }

    private static IReadOnlyList<string> RunGoalDetectorChecks()
    {
        static ScoreEvent Event(TeamSide team, ScoreType type, int sequence) => new()
        {
            Team = team,
            Type = type,
            Quarter = 2,
            GameTime = TimeSpan.FromMinutes(sequence),
            Timestamp = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc).AddMinutes(sequence)
        };

        static string Check(string name, IReadOnlyList<ScoreEvent> events, string expected)
        {
            AflGoalRunDetector.Result? result = AflGoalRunDetector.Detect(events, TeamSide.Home);
            string actual = result is null
                ? "none"
                : result.IsConsecutive
                    ? $"{result.TeamGoals}-straight"
                    : $"{result.TeamGoals}-of-{result.WindowGoals}";
            return $"{name}: expected={expected}; actual={actual}; pass={actual == expected}";
        }

        var checks = new List<string>
        {
            Check("below-threshold",
            [
                Event(TeamSide.Home, ScoreType.Goal, 1),
                Event(TeamSide.Home, ScoreType.Goal, 2),
                Event(TeamSide.Home, ScoreType.Goal, 3)
            ], "none"),
            Check("four-straight-ignores-behind",
            [
                Event(TeamSide.Home, ScoreType.Goal, 1),
                Event(TeamSide.Away, ScoreType.Behind, 2),
                Event(TeamSide.Home, ScoreType.Goal, 3),
                Event(TeamSide.Home, ScoreType.Goal, 4),
                Event(TeamSide.Home, ScoreType.Goal, 5)
            ], "4-straight"),
            Check("four-of-five",
            [
                Event(TeamSide.Home, ScoreType.Goal, 1),
                Event(TeamSide.Home, ScoreType.Goal, 2),
                Event(TeamSide.Away, ScoreType.Goal, 3),
                Event(TeamSide.Home, ScoreType.Goal, 4),
                Event(TeamSide.Home, ScoreType.Goal, 5)
            ], "4-of-5"),
            Check("six-of-seven-over-four-straight",
            [
                Event(TeamSide.Home, ScoreType.Goal, 1),
                Event(TeamSide.Home, ScoreType.Goal, 2),
                Event(TeamSide.Away, ScoreType.Goal, 3),
                Event(TeamSide.Home, ScoreType.Goal, 4),
                Event(TeamSide.Home, ScoreType.Goal, 5),
                Event(TeamSide.Home, ScoreType.Goal, 6),
                Event(TeamSide.Home, ScoreType.Goal, 7)
            ], "6-of-7"),
            Check("balanced-sequence-rejected",
            [
                Event(TeamSide.Home, ScoreType.Goal, 1),
                Event(TeamSide.Home, ScoreType.Goal, 2),
                Event(TeamSide.Away, ScoreType.Goal, 3),
                Event(TeamSide.Home, ScoreType.Goal, 4),
                Event(TeamSide.Away, ScoreType.Goal, 5),
                Event(TeamSide.Home, ScoreType.Goal, 6)
            ], "none")
        };

        AflGoalRunDetector.Result? oneGoal = AflGoalRunDetector.Detect(
            [Event(TeamSide.Home, ScoreType.Goal, 1)], TeamSide.Home, minimumFeaturedGoals: 1);
        checks.Add($"manual-one-goal: expected=1-straight; actual={(oneGoal is null ? "none" : $"{oneGoal.TeamGoals}-straight")}; pass={oneGoal?.TeamGoals == 1 && oneGoal.IsConsecutive}");
        AflGoalRunDetector.Result? twoGoals = AflGoalRunDetector.Detect(
            [Event(TeamSide.Home, ScoreType.Goal, 1), Event(TeamSide.Home, ScoreType.Goal, 2)],
            TeamSide.Home, minimumFeaturedGoals: 1);
        checks.Add($"manual-two-goals: expected=2-straight; actual={(twoGoals is null ? "none" : $"{twoGoals.TeamGoals}-straight")}; pass={twoGoals?.TeamGoals == 2 && twoGoals.IsConsecutive}");
        AflGoalRunDetector.Result? replyStory = AflGoalRunDetector.DetectBest(
        [
            Event(TeamSide.Home, ScoreType.Goal, 1),
            Event(TeamSide.Home, ScoreType.Goal, 2),
            Event(TeamSide.Home, ScoreType.Goal, 3),
            Event(TeamSide.Home, ScoreType.Goal, 4),
            Event(TeamSide.Away, ScoreType.Goal, 5)
        ]);
        checks.Add($"run-survives-one-opposition-reply: expected=home-4-of-5; actual={(replyStory is null ? "none" : $"{replyStory.Team}-{replyStory.TeamGoals}-of-{replyStory.WindowGoals}")}; pass={replyStory is { Team: TeamSide.Home, TeamGoals: 4, WindowGoals: 5, IsConsecutive: false }}");

        int combinations = 0;
        int failures = 0;
        int bestStoryFailures = 0;
        for (int length = 1; length <= 12; length++)
        {
            int limit = 1 << length;
            for (int mask = 0; mask < limit; mask++)
            {
                var sequence = new List<ScoreEvent>(length);
                for (int index = 0; index < length; index++)
                {
                    TeamSide team = (mask & (1 << index)) != 0 ? TeamSide.Home : TeamSide.Away;
                    ScoreEvent score = Event(team, ScoreType.Goal, index + 1);
                    sequence.Add(new ScoreEvent
                    {
                        Team = score.Team,
                        Type = score.Type,
                        Quarter = index < length / 2 ? 1 : 2,
                        GameTime = TimeSpan.FromMinutes((index % Math.Max(1, length / 2)) + 1),
                        Timestamp = score.Timestamp
                    });
                }

                bool expected = Qualifies(sequence, TeamSide.Home, requireLatest: true);
                bool actual = AflGoalRunDetector.Detect(sequence, TeamSide.Home) is not null;
                bool expectedBest = Qualifies(sequence, TeamSide.Home, requireLatest: false)
                                    || Qualifies(sequence, TeamSide.Away, requireLatest: false);
                bool actualBest = AflGoalRunDetector.DetectBest(sequence) is not null;
                combinations++;
                if (expected != actual) failures++;
                if (expectedBest != actualBest) bestStoryFailures++;
            }
        }
        checks.Add($"exhaustive-binary-sequences-through-12-goals: combinations={combinations}; failures={failures}; pass={failures == 0}");
        checks.Add($"exhaustive-best-story-through-12-goals: combinations={combinations}; failures={bestStoryFailures}; pass={bestStoryFailures == 0}");
        return checks;

        static bool Qualifies(IReadOnlyList<ScoreEvent> goals, TeamSide team, bool requireLatest)
        {
            if (goals.Count == 0 || (requireLatest && goals[^1].Team != team)) return false;
            int straight = 0;
            for (int index = goals.Count - 1; index >= 0 && goals[index].Team == team; index--)
                straight++;
            if (straight >= 4) return true;

            for (int start = 0; start < goals.Count; start++)
            {
                int window = goals.Count - start;
                int home = 0;
                for (int index = start; index < goals.Count; index++)
                    if (goals[index].Team == team) home++;
                int away = window - home;
                if (home >= 4 && away >= 1 && (double)home / window >= 0.75)
                    return true;
            }
            return false;
        }
    }

    private async Task CaptureLifecycleStressAsync(int index, string name, string outputDirectory)
    {
        if (!_presentationViews.TryGetValue(index, out List<FrameworkElement>? views) || views.Count == 0)
            return;

        GalleryTabs.SelectedIndex = index;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        CaptureCurrentWindow(outputDirectory, name + "-lifecycle-baseline");

        var originalSizes = new List<(FrameworkElement View, double Width, double Height)>();
        foreach (FrameworkElement view in views)
            originalSizes.Add((view, view.Width, view.Height));

        // Reproduce production switching: the current surface is collapsed,
        // measured at no size, then made visible again without a data update.
        for (int cycle = 0; cycle < 8; cycle++)
        {
            foreach (FrameworkElement view in views) view.Visibility = Visibility.Collapsed;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Background);
            foreach (FrameworkElement view in views) view.Visibility = Visibility.Visible;
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        }

        // Exercise geometry changes independently from absolute size: small
        // wide, large wide, horizontally thin, and the original dimensions.
        (double Width, double Height)[] stressSizes =
        [
            (320, 120),
            (960, 360),
            (240, 320),
            (520, 180),
        ];
        foreach ((double width, double height) in stressSizes)
        {
            foreach (FrameworkElement view in views)
            {
                view.Width = width;
                view.Height = height;
            }
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        }

        foreach ((FrameworkElement view, double width, double height) in originalSizes)
        {
            view.Width = width;
            view.Height = height;
        }
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        CaptureCurrentWindow(outputDirectory, name + "-lifecycle-after");
    }

    private void CaptureCurrentWindow(string outputDirectory, string name)
    {
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(outputDirectory, name + ".png"));
        encoder.Save(output);
    }

    private static void CaptureElement(FrameworkElement element, string outputDirectory, string name)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(Path.Combine(outputDirectory, name + ".png"));
        encoder.Save(output);
    }

    private async Task CaptureScorebugLifecycleStressAsync(string outputDirectory)
    {
        if (_scorebugPreviews.Count == 0) return;

        GalleryTabs.SelectedIndex = 0;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);

        ScorebugControl view = _scorebugPreviews[0].View;
        double originalWidth = view.Width;
        double originalHeight = view.Height;
        CaptureElement(view, outputDirectory, "scorebug-responsive-baseline");

        for (int cycle = 0; cycle < 8; cycle++)
        {
            view.Visibility = Visibility.Collapsed;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Background);
            view.Visibility = Visibility.Visible;
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);

            foreach ((double width, double height) in new[]
                     {
                         (320d, 120d), (480d, 135d), (240d, 320d),
                         (280d, 105d), (386d, 193d), (707d, 420d)
                     })
            {
                view.Width = width;
                view.Height = height;
                await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        view.Width = originalWidth;
        view.Height = originalHeight;
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        CaptureElement(view, outputDirectory, "scorebug-responsive-after");
    }

    private async Task CaptureDistanceOperationsStressAsync(string outputDirectory)
    {
        ScorebugControl? view = _scorebugPreviews
            .FirstOrDefault(preview => preview.Name == "distance-386x193").View;
        if (view is null) return;

        GalleryTabs.SelectedIndex = 0;
        view.Visibility = Visibility.Visible;
        view.ResetAllOverlayState();
        for (int cycle = 0; cycle < 12; cycle++)
        {
            view.SetLayout(cycle % 2 == 0 ? ScorebugLayout.Expanded : ScorebugLayout.Distance);
            view.SetTeams($"Home Club {cycle}", "HOM", $"Away Club {cycle}", "AWA");
            view.SetScores(cycle, cycle + 1, cycle + 2, cycle + 3);
            view.SetQuarter((cycle % 4) + 1);
            view.SetClock($"{cycle:D2}:{(cycle * 7) % 60:D2}");
            view.SetHideTeamNames(cycle % 3 == 0);
            view.SetFinalsMode(cycle % 4 == 0);
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        }

        view.SetLayout(ScorebugLayout.Distance);
        view.SetTeams("University Football Club", "UFC", "Old Collegians Football Club", "OCF");
        view.SetScores(88, 88, 87, 89);
        view.SetQuarter(4);
        view.SetClock("19:59");
        view.SetHideTeamNames(false);
        view.SetFinalsMode(true);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        CaptureElement(view, outputDirectory, "scorebug-distance-finals-operations-386x193");

        view.SetFinalsMode(false);
        view.AnimateQuarterToBreakLabel(4);
        await Task.Delay(650);
        view.SetQuarter(4);
        view.ShowStatsBar(MatchStats.Calculate(BuildRepresentativeMatch(), 4, TimeSpan.FromMinutes(19)));
        await Task.Delay(950);
        string overlayState = view.GetCoreStateDiagnosticsForQa();
        view.ResetAllOverlayState();

        view.PlayScoreAnimation(true, true, 89, 88, 622);
        await Task.Delay(700);
        string scoreAnimationState = view.GetCoreStateDiagnosticsForQa();
        await Task.Delay(3000);

        string unsafeMessage = new string('X', 180) + "\r\nSECOND LINE";
        view.SetMarqueeMessages(new[]
        {
            new MarqueeMessage(unsafeMessage, "not-a-colour", "#3300FF00", isEnabled: true)
        });
        await Task.Delay(250);
        bool tickerBeforeWarning = view.IsTickerActive;
        view.ShowFiveMinuteWarning();
        await Task.Delay(1000);
        bool warningActive = view.IsOverlayActive;
        bool tickerDuringWarning = view.IsTickerActive;
        view.HideFiveMinuteWarning();
        await Task.Delay(1200);
        bool tickerRestored = view.IsTickerActive;
        string finalState = view.GetCoreStateDiagnosticsForQa();

        File.WriteAllText(Path.Combine(outputDirectory, "distance-operations-stress.txt"),
            $"cycles=12{Environment.NewLine}" +
            $"overlay-state={overlayState}{Environment.NewLine}" +
            $"score-animation-state={scoreAnimationState}{Environment.NewLine}" +
            $"ticker-before-warning={tickerBeforeWarning}{Environment.NewLine}" +
            $"warning-active={warningActive}{Environment.NewLine}" +
            $"ticker-during-warning={tickerDuringWarning}{Environment.NewLine}" +
            $"ticker-restored={tickerRestored}{Environment.NewLine}" +
            $"final-state={finalState}{Environment.NewLine}" +
            $"pass={view.CurrentLayout == ScorebugLayout.Distance && tickerBeforeWarning && warningActive && !tickerDuringWarning && tickerRestored}");

        view.SetMarqueeMessages(Array.Empty<MarqueeMessage>());
        view.ResetAllOverlayState();
    }

    private async Task CaptureGoalAnimationPackagesAsync(string outputDirectory)
    {
        if (_scorebugPreviews.Count == 0) return;

        GalleryTabs.SelectedIndex = 0;
        ScorebugControl view = _scorebugPreviews[0].View;
        view.Visibility = Visibility.Visible;
        view.ResetAllOverlayState();
        view.SetScores(18, 12, 17, 11);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);

        foreach ((GoalAnimationStyle style, string name, int sampleMilliseconds) in new[]
                 {
                     (GoalAnimationStyle.Broadcast, "tv-signal", 500),
                     (GoalAnimationStyle.Electric, "voltage", 500),
                     (GoalAnimationStyle.Cinematic, "club-cinema", 1200),
                     (GoalAnimationStyle.Clean, "clean-impact", 300),
                     (GoalAnimationStyle.Classic, "flipboard", 500)
                 })
        {
            view.SetGoalAnimationStyle(style);
            view.PlayScoreAnimation(true, true, 19, 12, 126);
            await Task.Delay(sampleMilliseconds);
            view.UpdateLayout();
            CaptureElement(view, outputDirectory, "goal-animation-" + name + "-386x193");
            view.SetScores(18, 12, 17, 11);
        }

        // Allow the final package's completion guard to settle so subsequent
        // lifecycle captures always begin from an animation-free surface.
        await Task.Delay(2200);
        view.SetGoalAnimationStyle(GoalAnimationStyle.Broadcast);
        view.SetScores(18, 12, 17, 11);
    }

    private async Task CaptureOverlayNamePanelStressAsync(string outputDirectory)
    {
        if (_scorebugPreviews.Count == 0) return;

        GalleryTabs.SelectedIndex = 0;
        ScorebugControl view = _scorebugPreviews[0].View;
        view.ResetAllOverlayState();
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        CaptureElement(view, outputDirectory, "overlay-name-lifecycle-baseline");

        MatchStats stats = MatchStats.Calculate(BuildRepresentativeMatch(), 2, TimeSpan.FromMinutes(8));
        for (int cycle = 0; cycle < 8; cycle++)
        {
            view.ShowStatsBar(stats);
            await Task.Delay(380);
            view.ResetAllOverlayState();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        }
        CaptureElement(view, outputDirectory, "overlay-name-lifecycle-after");

        view.ShowStatsBar(stats);
        await Task.Delay(380);
        UpdateLayout();
        CaptureElement(view, outputDirectory, "overlay-name-panels-collapsed-386x193");
        (double gb, double total) = view.GetScoreFontSizesForQa();
        (double homeName, double awayName) = view.GetTeamNamePanelHeightsForQa();
        File.WriteAllText(Path.Combine(outputDirectory, "overlay-name-panel-stress.txt"),
            $"cycles=8{Environment.NewLine}" +
            $"active-overlay-goals/behinds={gb:0.##}px{Environment.NewLine}" +
            $"active-overlay-total={total:0.##}px{Environment.NewLine}" +
            $"active-overlay-name-rows={homeName:0.##}/{awayName:0.##}px{Environment.NewLine}");
        view.ResetAllOverlayState();
    }

    private void AddScorebugPreview(string name, string label, double width, double height,
        Action<ScorebugControl>? configure = null)
    {
        ScorebugControl view = CreateScorebug();
        configure?.Invoke(view);
        _scorebugPreviews.Add((name, view));
        AddPreview(ScorebugHost, label, view, width, height);
    }

    private static void AddPresentationPair(System.Windows.Controls.Panel host, Func<System.Windows.Controls.UserControl> factory)
    {
        AddPreview(host, "LARGE 772 × 386", factory(), 772, 386);
        AddPreview(host, "COMPACT 300 × 150", factory(), 300, 150);
    }

    private static ScorebugControl CreateScorebug()
    {
        var view = new ScorebugControl();
        view.FreezeWallClockForQa(new DateTime(2026, 8, 30, 8, 5, 0));
        view.SetTeams("Xavier", "XC", "Carey", "CY");
        view.SetTeamColors(HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary);
        view.SetScores(18, 12, 17, 11);
        view.SetQuarter(4);
        view.SetClock("04:59");
        view.SetLogos(SampleLogoPath, SampleLogoPath);
        view.UpdateWeatherSnapshot(new WeatherSnapshot(
            22, 20, 14, 27, 0, "☀", "Clear sky", Array.Empty<HourlyForecast>()));
        return view;
    }

    private void AddOverlayPreview(string label, Action<ScorebugControl> showOverlay)
    {
        AddOverlayPreviewAtSize(label, showOverlay, 386, 193, "386x193");
        AddOverlayPreviewAtSize(label, showOverlay, 286, 193, "286x193");
    }

    private void AddOverlayPreviewAtSize(string label, Action<ScorebugControl> showOverlay,
        double width, double height, string suffix)
    {
        ScorebugControl view = CreateScorebug();
        // Overlay animations need a measured visual tree. Starting them before
        // the preview is loaded can leave the gallery showing the zero-height
        // first frame even though the production scorebug works correctly.
        view.Loaded += (_, _) => showOverlay(view);
        _overlayPreviews.Add(($"{SanitizeFileName(label)}-{suffix}", view, showOverlay));
        AddPreview(OverlayHost, $"{label} {width:0} × {height:0}", view, width, height);
    }

    private static string SanitizeFileName(string value)
        => string.Join("-", value.ToLowerInvariant().Split(
            [' ', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries));

    private static void AddPreview(System.Windows.Controls.Panel host, string label, FrameworkElement view, double width, double height)
    {
        view.Width = width;
        view.Height = height;

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x4D, 0x65)),
            BorderThickness = new Thickness(1),
            Background = System.Windows.Media.Brushes.Black,
            Child = view
        };

        var card = new StackPanel { Margin = new Thickness(0, 0, 18, 18) };
        card.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift"),
            FontWeight = FontWeights.Black,
            FontSize = 14,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC7, 0xD6, 0xE5)),
            Margin = new Thickness(0, 0, 0, 7)
        });
        card.Children.Add(frame);
        host.Children.Add(card);
    }

    private static MatchManager BuildRepresentativeMatch()
    {
        var match = new MatchManager();
        match.SetTeams("Xavier", "XC", "Carey", "CY");
        match.SetClockMode(ClockMode.Countdown);
        match.SetQuarterDuration(TimeSpan.FromMinutes(20));

        ScoreAt(match, TimeSpan.FromMinutes(2), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(5), TeamSide.Away, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(8), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(12), TeamSide.Away, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(17), TeamSide.Home, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(19), TeamSide.Away, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(19.5), TeamSide.Away, goal: false);
        match.EndQuarter();

        ScoreAt(match, TimeSpan.FromMinutes(1), TeamSide.Away, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(2.5), TeamSide.Home, goal: false);
        ScoreAt(match, TimeSpan.FromMinutes(3.2), TeamSide.Home, goal: true);
        ScoreAt(match, TimeSpan.FromMinutes(3.7), TeamSide.Away, goal: true);
        return match;
    }

    private static void ScoreAt(MatchManager match, TimeSpan elapsed, TeamSide team, bool goal)
    {
        match.SetElapsed(elapsed);
        if (goal) match.AddGoal(team);
        else match.AddBehind(team);
    }
}
