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
        AddPreview(ScorebugHost, "DESIGN 707 × 420", CreateScorebug(), 707, 420);
        AddPreview(ScorebugHost, "COMPACT 386 × 193", CreateScorebug(), 386, 193);
        AddPreview(ScorebugHost, "THIN 240 × 320", CreateScorebug(), 240, 320);
        AddPreview(ScorebugHost, "SMALL WIDE 320 × 120", CreateScorebug(), 320, 120);

        AddOverlayPreview("SCORING RUN", view =>
            view.ShowScoringRun("Xavier", 24, 2, TimeSpan.FromMinutes(6.2), 5, HomePrimary));
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
        AddOverlayPreview("LAST SCORE", view => view.ShowLastScoreTime());
        AddOverlayPreview("LEAD CHANGES", view => view.ShowLeadChangesBar(7));

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

    private static void AddPresentationPair(System.Windows.Controls.Panel host, Func<System.Windows.Controls.UserControl> factory)
    {
        AddPreview(host, "LARGE 772 × 386", factory(), 772, 386);
        AddPreview(host, "COMPACT 300 × 150", factory(), 300, 150);
    }

    private static ScorebugControl CreateScorebug()
    {
        var view = new ScorebugControl();
        view.SetTeams("Xavier", "XC", "Carey", "CY");
        view.SetTeamColors(HomePrimary, AwayPrimary, HomeSecondary, AwaySecondary);
        view.SetScores(18, 12, 17, 11);
        view.SetQuarter(4);
        view.SetClock("04:59");
        view.SetLogos(SampleLogoPath, SampleLogoPath);
        return view;
    }

    private void AddOverlayPreview(string label, Action<ScorebugControl> showOverlay)
    {
        ScorebugControl view = CreateScorebug();
        // Overlay animations need a measured visual tree. Starting them before
        // the preview is loaded can leave the gallery showing the zero-height
        // first frame even though the production scorebug works correctly.
        view.Loaded += (_, _) => showOverlay(view);
        AddPreview(OverlayHost, label, view, 300, 150);
    }

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
