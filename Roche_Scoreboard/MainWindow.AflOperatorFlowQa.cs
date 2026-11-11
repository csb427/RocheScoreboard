using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBox = System.Windows.Controls.TextBox;
using Application = System.Windows.Application;

namespace Roche_Scoreboard;

/// <summary>Windows-only, explicitly activated integration fixture. Never an output-rendering test.</summary>
internal static class AflOperatorFlowQa
{
    internal const string Argument = "--operator-afl-flow-self-test-output=";
    internal const string ReportName = "operator-afl-flow-self-test.json";
    internal const string FixtureTitle = "ISOLATED AFL QA - NO EXTERNAL OUTPUT";

    internal static bool TryStart(App app, string[] args, int baseline)
    {
        if (!OperatorFlowIsolation.IsEnabled) return false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string directory;
        try { directory = ValidateActivation(args); }
        catch { app.Shutdown(2); return true; }
        var report = new Report(directory, baseline);
        app.Dispatcher.InvokeAsync(async () =>
        {
            var fixtures = new List<MainWindow>();
            var memory = new MemoryPresets();
            using var presetScope = PresetStorage.UseIoForTests(memory);
            try
            {
                Directory.CreateDirectory(directory);
                app.ThemeService.Initialize(app.UxPreferences.Theme, app.UxPreferences.Density);
                OperatorAccentPreferences.ApplyAccentResources();
                report.Check("isolation-latched-before-construction", app.IsSelfTestMode && SelfTestStorage.IsEnabled && OperatorFlowIsolation.IsEnabled);
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((_, _) => report.ObserveWindows("window-loaded", app.MainWindow as MainWindow)));
                MainWindow CreateFixture()
                {
                    var window = new MainWindow { Title = FixtureTitle, Width = 1100, Height = 820 };
                    window.AflQaInstallSentinels(report);
                    fixtures.Add(window); app.MainWindow = window; window.Show();
                    return window;
                }
                var fixture = CreateFixture();
                bool ready = await report.Stage("visible-wizard", () => fixture.AflQaCompleteWizardAsync(report, countdown: false));
                if (ready)
                {
                    await report.Stage("four-scoring-buttons", () => fixture.AflQaFourScoresAsync(report));
                    await report.Stage("undo", () => fixture.AflQaUndoAsync(report));
                    await report.Stage("history-editor", () => fixture.AflQaHistoryAsync(report));
                    await report.Stage("last-event-regression", () => fixture.AflQaLastEventRegressionAsync(report));
                    await report.Stage("count-up-clock", () => fixture.AflQaClockAsync(report, countdown: false));
                }
                else
                    foreach (string stage in new[] { "four-scoring-buttons", "undo", "history-editor", "last-event-regression", "count-up-clock" })
                        report.Skip(stage, "Visible wizard did not complete.");
                await report.Stage("sink-denial-probes", () => fixture.AflQaDenialProbesAsync(report));
                await fixture.AflQaRetireAsync(report, "first-fixture", memory);
                await report.Stage("countdown-clock", async () =>
                {
                    var countdown = CreateFixture();
                    await countdown.AflQaCompleteWizardAsync(report, countdown: true);
                    await countdown.AflQaClockAsync(report, countdown: true);
                });
            }
            catch (Exception error) { report.Failure("fixture-startup", error); }
            finally
            {
                await report.Stage("cleanup", async () =>
                {
                    foreach (var fixture in fixtures)
                        await fixture.AflQaRetireAsync(report, "final-fixture-" + fixtures.IndexOf(fixture), memory);
                    await DrainPresetsAsync();
                    int before = memory.Accesses;
                    await SettleAsync(400);
                    report.cleanup["presetsSettled"] = memory.Accesses == before && memory.Pending == 0;
                    report.cleanup["modelPaused"] = fixtures.All(window => window.AflQaClockPaused);
                    report.cleanup["timersStopped"] = fixtures.All(window => window.AflQaActiveTimers().Length == 0);
                    report.cleanup["editorsClosed"] = !Application.Current.Windows.OfType<Window>().Any(window => window.Owner is not null);
                    report.cleanup["rootClosed"] = fixtures.All(window => !window.IsVisible && !Application.Current.Windows.OfType<Window>().Contains(window));
                    report.cleanup["noWindows"] = Application.Current.Windows.Count == 0;
                    report.cleanup["outputReferencesNull"] = fixtures.All(window => window.AflQaOutputReferencesNull);
                    report.cleanup["isolationLatched"] = OperatorFlowIsolation.IsEnabled && SelfTestStorage.IsEnabled && app.IsSelfTestMode;
                    foreach (var check in report.cleanup) report.Check("cleanup-" + check.Key, check.Value);
                    report.ObserveWindows("after-cleanup", null);
                });
                report.sinkAttempts = OperatorFlowIsolation.Snapshot();
                report.diagnosticFinal = App.DiagnosticExceptionCount;
                report.diagnosticExceptions = report.diagnosticFinal - baseline;
                report.Check("no-diagnostic-exceptions", report.diagnosticExceptions == 0);
                report.Check("no-unexpected-sink-violations", report.sinkAttempts.Values.All(count => count.violations == 0));
                report.Check("automatic-output-was-intercepted", report.sinkAttempts.TryGetValue("output-afl", out var output) && output.suppressed > 0);
                report.Check("automatic-weather-was-intercepted", report.sinkAttempts.TryGetValue("weather-automatic", out var weather) && weather.suppressed > 0);
                report.Check("private-preset-paths-only", memory.PathViolations == 0 && memory.Accesses > 0);
                report.Check("no-stray-windows-observed", report.observedWindows.All(window => window.allowed));
                report.Check("provider-factories-never-called", report.weatherFactoryCalls == 0 && report.cloudFactoryCalls == 0);
                report.Check("vlc-never-initialized", !VlcRuntime.IsAvailable && VlcRuntime.InitializationError is null && !VlcRuntime.InitializationAttempted);
                report.completedUtc = DateTimeOffset.UtcNow;
                int exitCode = 1;
                try
                {
                    File.WriteAllText(Path.Combine(directory, ReportName), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                    exitCode = report.failures == 0 ? 0 : 1;
                }
                finally { app.Shutdown(exitCode); }
            }
        }, DispatcherPriority.ApplicationIdle);
        return true;
    }

    private static string ValidateActivation(string[] args)
    {
        if (args.Length != 1 || !args[0].StartsWith(Argument, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the exact isolated AFL argument is permitted.");
        static string Canonical(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidOperationException("An absolute local path is required.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        string temp = Canonical(Environment.GetEnvironmentVariable("TEMP"));
        if (!string.Equals(temp, Canonical(Environment.GetEnvironmentVariable("TMP")), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(temp, Canonical(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TEMP and TMP must identify the same private directory.");
        string output = Canonical(args[0][Argument.Length..]);
        if (!string.Equals(output, Path.Combine(temp, "afl-flow-report"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The report must be the fixed child of the private TEMP directory.");
        foreach (string candidate in new[] { temp, output })
        {
            for (var node = new DirectoryInfo(candidate); node is not null; node = node.Parent)
            {
                if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Reparse ancestors are not permitted.");
                if (Directory.Exists(Path.Combine(node.FullName, ".git")) || File.Exists(Path.Combine(node.FullName, ".git")))
                    throw new InvalidOperationException("The fixture must be outside a repository.");
            }
        }
        foreach (string forbidden in new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roche_Scoreboard"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RocheScoreboard"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RocheScoreboard")
        })
        {
            string root = Canonical(forbidden);
            if (temp.Equals(root, StringComparison.OrdinalIgnoreCase) || temp.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The fixture cannot use an executable or operator-data directory.");
        }
        if (!Directory.Exists(temp) || Directory.EnumerateFileSystemEntries(temp).Any()
            || Directory.Exists(output) || File.Exists(output) || Directory.Exists(SelfTestStorage.RootPath))
            throw new InvalidOperationException("The launcher must provide a fresh empty TEMP directory.");
        if (!SelfTestStorage.RootPath.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Self-test storage is outside the private TEMP directory.");
        return output;
    }

    internal static async Task SettleAsync(int milliseconds = 80)
    {
        await Task.Delay(milliseconds);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.WaitAsync(TimeSpan.FromSeconds(4));
    }

    internal static async Task WaitAsync(Func<bool> ready, string context, int milliseconds = 4000)
    {
        var time = Stopwatch.StartNew();
        while (!ready())
        {
            if (time.ElapsedMilliseconds > milliseconds) throw new TimeoutException(context);
            await Task.Delay(20);
        }
        await SettleAsync();
    }

    internal static async Task DrainPresetsAsync()
    {
        await Task.WhenAll(PresetStorage.ReadAsync(TeamLibrarySport.AustralianFootball), PresetStorage.ReadAsync(TeamLibrarySport.Cricket))
            .WaitAsync(TimeSpan.FromSeconds(4));
        await SettleAsync();
    }

    internal sealed class MemoryPresets : IPresetStorageIo
    {
        private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new();
        internal int Accesses, Pending, PathViolations;
        private void Validate(string path)
        {
            ++Accesses;
            string root = Path.Combine(SelfTestStorage.RootPath, "presets") + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            { ++PathViolations; throw new InvalidOperationException("Preset path escaped the private fixture."); }
        }
        public Task<string> ReadAllTextAsync(string path)
        {
            lock (gate)
            {
                ++Pending;
                try { Validate(path); return Task.FromResult(files.TryGetValue(path, out var value) ? value : "[]"); }
                finally { --Pending; }
            }
        }
        public Task WriteAllTextAsync(string path, string content)
        {
            lock (gate)
            {
                ++Pending;
                try { Validate(path); files[path] = content; return Task.CompletedTask; }
                finally { --Pending; }
            }
        }
    }

    internal sealed record ObservedWindow(string stage, string type, string title, bool owned, bool visible, bool allowed);
    internal sealed record StageResult(string status, string? error = null);
    internal sealed class Report(string directory, int baseline)
    {
        private int _failedObservations;
        public int schemaVersion => 1;
        public string mode => "operator-afl-flow";
        public string route => Argument + directory;
        public int processId => Environment.ProcessId;
        public string executablePath => Environment.ProcessPath ?? "";
        public string tempRoot => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        public string reportDirectory => directory;
        public string selfTestStorageRoot => SelfTestStorage.RootPath;
        public DateTimeOffset startedUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset completedUtc { get; set; }
        public Dictionary<string, bool> checks { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StageResult> stages { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> observations { get; } = new(StringComparer.Ordinal);
        public List<ObservedWindow> observedWindows { get; } = new();
        public Dictionary<string, bool> cleanup { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, OperatorFlowIsolation.SinkCounts> sinkAttempts { get; set; } = new();
        public int diagnosticBaseline => baseline;
        public int diagnosticFinal { get; set; }
        public int diagnosticExceptions { get; set; }
        public int weatherFactoryCalls { get; set; }
        public int cloudFactoryCalls { get; set; }
        public string[] passedChecks => checks.Where(pair => pair.Value).Select(pair => pair.Key).ToArray();
        public string[] failedChecks => checks.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray();
        public int failures => failedChecks.Length;
        public string status => failures == 0 ? "passed" : "failed";
        internal void Check(string name, bool pass)
        {
            if (!pass) ++_failedObservations;
            // A later observation can never erase an earlier failure.
            checks[name] = pass && (!checks.TryGetValue(name, out var before) || before);
        }
        internal async Task<bool> Stage(string name, Func<Task> run)
        {
            int before = _failedObservations;
            try { await run(); stages[name] = new(_failedObservations == before ? "passed" : "failed"); return true; }
            catch (Exception error) { Failure(name, error); return false; }
        }
        internal void Failure(string stage, Exception error)
        { Check(stage + "-completed", false); stages[stage] = new("failed", error.ToString()); }
        internal void Skip(string stage, string reason)
        { Check(stage + "-completed", false); stages[stage] = new("skipped", reason); }
        internal void ObserveWindows(string stage, MainWindow? fixture)
        {
            foreach (Window window in Application.Current.Windows)
            {
                bool ownedEditor = fixture is not null && window is ScoreEventEditorWindow && ReferenceEquals(window.Owner, fixture);
                bool ownedConfirmation = fixture is not null && window.Owner is ScoreEventEditorWindow editor
                    && ReferenceEquals(editor.Owner, fixture) && window.Title == "Delete score event";
                bool allowed = ReferenceEquals(window, fixture) || ownedEditor || ownedConfirmation;
                observedWindows.Add(new(stage, window.GetType().FullName ?? "", window.Title, window.Owner is not null, window.IsVisible, allowed));
                Check("only-owned-windows", allowed);
            }
        }
    }
}

public partial class MainWindow
{
    private bool _aflQaRetired;
    internal void AflQaInstallSentinels(AflOperatorFlowQa.Report report)
    {
        _match.MatchChanged += () => Title = AflOperatorFlowQa.FixtureTitle;
        WeatherServiceFactory = () =>
        {
            ++report.weatherFactoryCalls;
            throw new InvalidOperationException("The real weather factory boundary was reached.");
        };
        CloudSessionFactory = (_, _, _) =>
        {
            ++report.cloudFactoryCalls;
            throw new InvalidOperationException("The real cloud factory boundary was reached.");
        };
    }
    internal bool AflQaClockPaused => !_match.ClockRunning;
    internal bool AflQaOutputReferencesNull => _displayWindow is null && _scorebug is null && _outputScreensaver is null
        && _quarterBreakScorebug is null && _breakScreen is null && _scoreworm is null && _statsScreen is null
        && _videoPlayer is null && _weatherScreen is null && _displayContainer is null
        && _trainingDisplayWindow is null && _trainingScorebug is null && _cricketDisplayWindow is null
        && _cricketScorebug is null && _cricketSummary is null && _emergencyVideoWindow is null;

    private static IEnumerable<T> AflQaVisuals<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in AflQaVisuals<T>(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }

    private static T AflQaFind<T>(FrameworkElement root, string name) where T : FrameworkElement
        => root.FindName(name) as T ?? AflQaVisuals<T>(root).Single(item => item.Name == name);

    private static void AflQaClick(ButtonBase button)
    {
        if (!button.IsVisible || !button.IsEnabled || !button.IsLoaded || !button.IsHitTestVisible)
            throw new InvalidOperationException("The operator action is not available: " + button.Name);
        button.BringIntoView(); button.Focus();
        if (button is System.Windows.Controls.RadioButton radio) radio.IsChecked = true;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private async Task AflQaObserveAsync(AflOperatorFlowQa.Report report, string action)
    {
        await AflOperatorFlowQa.SettleAsync();
        report.ObserveWindows(action, this);
        report.Check("output-references-null-" + action, AflQaOutputReferencesNull);
        report.Check("service-references-null-" + action, _weatherService is null && _cloudRemote is null && _cloudConfig is null && !VlcRuntime.IsAvailable);
    }

    internal async Task AflQaCompleteWizardAsync(AflOperatorFlowQa.Report report, bool countdown)
    {
        string prefix = countdown ? "countdown" : "wizard";
        await AflOperatorFlowQa.WaitAsync(() => IsLoaded && IsVisible && SportSelectionPanel.IsVisible, "Operator fixture did not load.");
        Width = 1100; Height = Math.Min(820, SystemParameters.WorkArea.Height - 40);
        AflQaClick(AflQaFind<Button>(SportSelectionPanel, "AflLaunchAction"));
        await AflOperatorFlowQa.WaitAsync(() => SetupWizardPanel.IsVisible && _setupFlowActive, "Visible setup did not start.");
        var next = AflQaFind<Button>(SetupWizardPanel, "NextButton");
        AflQaClick(AflQaFind<System.Windows.Controls.RadioButton>(SetupWizardPanel, "AflSportChoice"));
        AflQaClick(next);
        await AflOperatorFlowQa.WaitAsync(() => AflQaFind<TextBox>(SetupWizardPanel, "SetupHomeNameBox").IsVisible, "Team step not visible.");
        AflQaFind<TextBox>(SetupWizardPanel, "SetupHomeNameBox").Text = "QA Home";
        AflQaFind<TextBox>(SetupWizardPanel, "SetupHomeAbbrBox").Text = "QAH";
        AflQaFind<TextBox>(SetupWizardPanel, "SetupAwayNameBox").Text = "QA Away";
        AflQaFind<TextBox>(SetupWizardPanel, "SetupAwayAbbrBox").Text = "QAA";
        await AflOperatorFlowQa.SettleAsync(400);
        AflQaClick(next);
        var clockChoice = AflQaFind<System.Windows.Controls.RadioButton>(SetupWizardPanel, countdown ? "SetupCountdown" : "SetupCountUp");
        await AflOperatorFlowQa.WaitAsync(() => clockChoice.IsVisible, "Clock step not visible.");
        AflQaClick(clockChoice);
        // This hidden checkbox is storage for the visible stoppage toggle. Use
        // its real operator toggle surface rather than mutating the stored value.
        var stoppage = AflQaFind<System.Windows.Controls.CheckBox>(SetupWizardPanel, "SetupControlPanelCountdown");
        if (!countdown && stoppage.IsChecked == true)
        {
            var toggle = AflQaFind<OperatorGlassSurface>(SetupWizardPanel, "ControlPanelCountdownOptionCard");
            await AflOperatorFlowQa.WaitAsync(() => toggle.IsVisible && toggle.IsHitTestVisible, "Stoppage timing toggle is not visible.");
            toggle.BringIntoView();
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = toggle });
        }
        await AflOperatorFlowQa.SettleAsync(400);
        AflQaClick(next);
        var location = AflQaFind<System.Windows.Controls.ComboBox>(SetupWizardPanel, "SetupWeatherLocation");
        await AflOperatorFlowQa.WaitAsync(() => location.IsVisible, "Options step not visible.");
        location.Text = "";
        await AflOperatorFlowQa.SettleAsync(400);
        AflQaClick(next);
        await AflOperatorFlowQa.WaitAsync(() => _aflMatchReady && MainContent.IsVisible && !SetupWizardPanel.IsVisible, "Real setup authorization/completion failed.");
        report.Check(prefix + "-completed-real-visible-route", SetupWizardPanel.Result is not null && !_setupFlowActive && !_setupCompletionAuthorized && !_setupSelectedSport.HasValue);
        report.Check(prefix + "-synthetic-team-identity", _match.HomeName == "QA Home" && _match.HomeAbbr == "QAH" && _match.AwayName == "QA Away" && _match.AwayAbbr == "QAA");
        report.Check(prefix + "-blank-media-and-venue", string.IsNullOrEmpty(_homeLogoPath) && string.IsNullOrEmpty(_awayLogoPath)
            && string.IsNullOrEmpty(_homeGoalVideoPath) && string.IsNullOrEmpty(_awayGoalVideoPath) && string.IsNullOrEmpty(_weatherLocation));
        report.Check(prefix + "-zero-score-and-paused", _match.HomeTotal == 0 && _match.AwayTotal == 0 && _match.Events.Count == 0 && !_match.ClockRunning);
        report.Check(prefix + "-requested-clock-mode", _match.ClockMode == (countdown ? ClockMode.Countdown : ClockMode.CountUp)
            && _match.QuarterDuration == TimeSpan.FromMinutes(20) && !_showControlPanelCountdown);
        report.Check(prefix + "-header-visible", HeaderScore.IsVisible && LastEventText.IsVisible && ActualWidth >= 760);
        await AflQaObserveAsync(report, prefix);
    }

    private void AflQaScore(AflOperatorFlowQa.Report report, string name, int homeGoals, int homeBehinds, int awayGoals, int awayBehinds,
        string homeText, string awayText, string header)
    {
        report.Check(name + "-model-score", _match.HomeGoals == homeGoals && _match.HomeBehinds == homeBehinds && _match.AwayGoals == awayGoals && _match.AwayBehinds == awayBehinds);
        report.Check(name + "-operator-score-text", HomeScoreDisplay.Text == homeText && AwayScoreDisplay.Text == awayText && HeaderScore.Text == header);
        report.observations[name + "-scores"] = HomeScoreDisplay.Text + " | " + AwayScoreDisplay.Text + " | " + HeaderScore.Text;
    }

    private void AflQaRows(AflOperatorFlowQa.Report report, string name, params string[] expected)
    {
        var rows = EventList.Children.OfType<Border>().Where(row => row.Tag is ScoreEvent).ToArray();
        // U+2013 is the production score separator, pinned explicitly rather
        // than normalized to an ASCII hyphen. Expected strings are handwritten
        // literals, never formatted by
        // ScoreEvent or the production row factory under test.
        string[] actual = rows.Select(row => string.Join("|", AflQaVisuals<TextBlock>(row).Select(text => text.Text))).ToArray();
        report.Check(name + "-history-rows", actual.SequenceEqual(expected));
        report.Check(name + "-history-count", MatchLogCountBadge.Text == "(" + expected.Length + ")" && _match.Events.Count == expected.Length && rows.Length == expected.Length);
        report.observations[name + "-history"] = string.Join("\n", actual);
    }

    internal async Task AflQaFourScoresAsync(AflOperatorFlowQa.Report report)
    {
        AflQaClick(HomeGoalButton); AflQaScore(report, "home-goal", 1, 0, 0, 0, "1.0.6", "0.0.0", "6 \u2013 0"); await AflQaObserveAsync(report, "home-goal");
        AflQaClick(HomeBehindButton); AflQaScore(report, "home-behind", 1, 1, 0, 0, "1.1.7", "0.0.0", "7 \u2013 0"); await AflQaObserveAsync(report, "home-behind");
        AflQaClick(AwayGoalButton); AflQaScore(report, "away-goal", 1, 1, 1, 0, "1.1.7", "1.0.6", "7 \u2013 6"); await AflQaObserveAsync(report, "away-goal");
        AflQaClick(AwayBehindButton); AflQaScore(report, "away-behind", 1, 1, 1, 1, "1.1.7", "1.1.7", "7 \u2013 7"); await AflQaObserveAsync(report, "away-behind");
        report.Check("four-ordered-event-snapshots", _match.Events.Select(ev => $"{ev.Team}/{ev.Type}/{ev.Quarter}/{ev.GameTime.Ticks}/{ev.HomeGoals}/{ev.HomeBehinds}/{ev.AwayGoals}/{ev.AwayBehinds}")
            .SequenceEqual(new[] { "Home/Goal/1/0/1/0/0/0", "Home/Behind/1/0/1/1/0/0", "Away/Goal/1/0/1/1/1/0", "Away/Behind/1/0/1/1/1/1" }));
        AflQaRows(report, "four-scores",
            "+1|BEHIND|QA AWAY|Q1  00:00|QAH|1.1.7|\u2013|1.1.7|QAA|Edit",
            "+6|GOAL|QA AWAY|Q1  00:00|QAH|1.1.7|\u2013|1.0.6|QAA|Edit",
            "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit",
            "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("four-scores-last-event", LastEventText.Text == "Last: QA Away Behind");
    }

    internal async Task AflQaUndoAsync(AflOperatorFlowQa.Report report)
    {
        AflQaClick(UndoButton);
        AflQaScore(report, "undo", 1, 1, 1, 0, "1.1.7", "1.0.6", "7 \u2013 6");
        AflQaRows(report, "undo", "+6|GOAL|QA AWAY|Q1  00:00|QAH|1.1.7|\u2013|1.0.6|QAA|Edit",
            "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("undo-last-event-receipt", LastEventText.Text == "Last: QA Away Goal");
        report.observations["undo-last-event"] = LastEventText.Text;
        await AflQaObserveAsync(report, "undo");
    }

    private async Task AflQaEditorAsync(AflOperatorFlowQa.Report report, string name, Func<ScoreEventEditorWindow, Task> interaction)
    {
        var row = EventList.Children.OfType<OperatorActionBorder>().First(item => item.Tag is ScoreEvent);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        deadline.Tick += (_, _) =>
        {
            deadline.Stop(); failure = new TimeoutException("Owned score editor exceeded its deadline.");
            foreach (var owned in OwnedWindows.OfType<Window>().ToArray()) owned.Close();
        };
        DispatcherOperation editorOperation = Dispatcher.BeginInvoke(new Action(async () =>
        {
            ScoreEventEditorWindow? editor = null;
            try
            {
                await AflOperatorFlowQa.WaitAsync(() => OwnedWindows.OfType<ScoreEventEditorWindow>().Any(window => window.IsVisible), "Owned score editor did not open.");
                editor = OwnedWindows.OfType<ScoreEventEditorWindow>().Single();
                report.ObserveWindows(name + "-editor", this);
                report.Check(name + "-editor-owned-visible", ReferenceEquals(editor.Owner, this) && editor.IsVisible);
                await interaction(editor);
            }
            catch (Exception error) { failure = error; editor?.Close(); }
            finally { finished.TrySetResult(); }
        }), DispatcherPriority.Background);
        try
        {
            row.BringIntoView(); row.Focus(); deadline.Start(); row.Activate();
            await editorOperation.Task.WaitAsync(TimeSpan.FromSeconds(7));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(7));
            if (failure is not null) throw failure;
            report.Check(name + "-editor-closed", !OwnedWindows.OfType<ScoreEventEditorWindow>().Any());
            await AflQaObserveAsync(report, name);
        }
        finally { deadline.Stop(); }
    }

    private async Task AflQaDeleteDecisionAsync(AflOperatorFlowQa.Report report, ScoreEventEditorWindow editor, bool accept)
    {
        Exception? failure = null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherOperation confirmationOperation = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await AflOperatorFlowQa.WaitAsync(() => editor.OwnedWindows.OfType<Window>().Any(window => window.IsVisible), "Owned deletion confirmation did not open.");
                var confirm = editor.OwnedWindows.OfType<Window>().Single();
                report.ObserveWindows(accept ? "delete-confirm" : "delete-cancel", this);
                report.Check("delete-confirmation-owned", ReferenceEquals(confirm.Owner, editor) && confirm.Title == "Delete score event");
                var cancel = AflQaVisuals<Button>(confirm).Single(button => Equals(button.Content, "Cancel"));
                report.Check("delete-confirmation-safe-default", cancel.IsDefault && cancel.IsCancel);
                AflQaClick(accept ? AflQaVisuals<Button>(confirm).Single(button => Equals(button.Content, "Delete score event")) : cancel);
            }
            catch (Exception error)
            {
                failure = error;
                foreach (var owned in editor.OwnedWindows.OfType<Window>().ToArray()) owned.Close();
            }
            finally { done.TrySetResult(); }
        }), DispatcherPriority.Background);
        AflQaClick(AflQaFind<Button>(editor, "DeleteButton"));
        await confirmationOperation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (failure is not null) throw failure;
    }

    internal async Task AflQaHistoryAsync(AflOperatorFlowQa.Report report)
    {
        var identities = _match.Events.ToArray();
        var timestamps = identities.Select(ev => ev.Timestamp).ToArray();
        string beforeCancel = JsonSerializer.Serialize(_match.ToState());
        await AflQaEditorAsync(report, "editor-cancel", editor =>
        {
            AflQaClick(AflQaFind<System.Windows.Controls.RadioButton>(editor, "HomeTeamBtn"));
            AflQaClick(AflQaFind<System.Windows.Controls.RadioButton>(editor, "BehindBtn"));
            AflQaClick(AflQaVisuals<Button>(editor).Single(button => Equals(button.Content, "CANCEL")));
            return Task.CompletedTask;
        });
        AflQaScore(report, "editor-cancel", 1, 1, 1, 0, "1.1.7", "1.0.6", "7 \u2013 6");
        AflQaRows(report, "editor-cancel", "+6|GOAL|QA AWAY|Q1  00:00|QAH|1.1.7|\u2013|1.0.6|QAA|Edit",
            "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("editor-cancel-preserves-event-identities", identities.SequenceEqual(_match.Events) && timestamps.SequenceEqual(_match.Events.Select(ev => ev.Timestamp))
            && JsonSerializer.Serialize(_match.ToState()) == beforeCancel);
        string beforeNoOp = JsonSerializer.Serialize(_match.ToState());
        await AflQaEditorAsync(report, "editor-no-op", editor => { AflQaClick(AflQaFind<Button>(editor, "SaveButton")); return Task.CompletedTask; });
        AflQaScore(report, "editor-no-op", 1, 1, 1, 0, "1.1.7", "1.0.6", "7 \u2013 6");
        AflQaRows(report, "editor-no-op", "+6|GOAL|QA AWAY|Q1  00:00|QAH|1.1.7|\u2013|1.0.6|QAA|Edit",
            "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("editor-no-op-preserves-history", _match.Events.Count == 3 && timestamps.SequenceEqual(_match.Events.Select(ev => ev.Timestamp))
            && JsonSerializer.Serialize(_match.ToState()) == beforeNoOp);
        await AflQaEditorAsync(report, "editor-save", editor =>
        {
            AflQaClick(AflQaFind<System.Windows.Controls.RadioButton>(editor, "HomeTeamBtn"));
            AflQaClick(AflQaFind<Button>(editor, "SaveButton")); return Task.CompletedTask;
        });
        AflQaScore(report, "editor-save", 2, 1, 0, 0, "2.1.13", "0.0.0", "13 \u2013 0");
        AflQaRows(report, "editor-save", "+6|GOAL|QA HOME|Q1  00:00|QAH|2.1.13|\u2013|0.0.0|QAA|Edit",
            "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("edit-last-event-receipt", LastEventText.Text == "Last: QA Home Goal");
        report.observations["edit-last-event"] = LastEventText.Text;
        await AflQaEditorAsync(report, "editor-delete-cancel", async editor =>
        {
            await AflQaDeleteDecisionAsync(report, editor, accept: false);
            report.Check("delete-cancel-keeps-editor-and-model", editor.IsVisible && _match.Events.Count == 3 && _match.HomeTotal == 13 && _match.AwayTotal == 0);
            AflQaClick(AflQaVisuals<Button>(editor).Single(button => Equals(button.Content, "CANCEL")));
        });
        await AflQaEditorAsync(report, "editor-delete", editor => AflQaDeleteDecisionAsync(report, editor, accept: true));
        AflQaScore(report, "editor-delete", 1, 1, 0, 0, "1.1.7", "0.0.0", "7 \u2013 0");
        AflQaRows(report, "editor-delete", "+1|BEHIND|QA HOME|Q1  00:00|QAH|1.1.7|\u2013|0.0.0|QAA|Edit", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("delete-last-event-receipt", LastEventText.Text == "Last: QA Home Behind");
        report.observations["delete-last-event"] = LastEventText.Text;
    }

    internal async Task AflQaLastEventRegressionAsync(AflOperatorFlowQa.Report report)
    {
        int before = _match.Events.Count;
        for (int index = 0; index < before; index++) AflQaClick(UndoButton);
        report.Check("empty-history-clears-receipt", _match.Events.Count == 0 && LastEventText.Text == "" && !UndoButton.IsEnabled);
        AflQaClick(HomeGoalButton); AflQaClick(AwayBehindButton); AflQaClick(UndoButton);
        AflQaScore(report, "stale-receipt-witness", 1, 0, 0, 0, "1.0.6", "0.0.0", "6 \u2013 0");
        AflQaRows(report, "stale-receipt-witness", "+6|GOAL|QA HOME|Q1  00:00|QAH|1.0.6|\u2013|0.0.0|QAA|Edit");
        report.Check("stale-receipt-home-goal-away-behind-undo", LastEventText.Text == "Last: QA Home Goal");
        report.observations["stale-receipt-observed"] = LastEventText.Text;
        await AflQaObserveAsync(report, "stale-receipt");
    }

    internal async Task AflQaClockAsync(AflOperatorFlowQa.Report report, bool countdown)
    {
        string prefix = countdown ? "countdown-clock" : "count-up-clock";
        report.Check(prefix + "-initial-literal", HeaderQuarterClock.Text == (countdown ? "Q1  20:00" : "Q1  00:00") && !_match.ClockRunning);
        var wallClock = Stopwatch.StartNew();
        AflQaClick(StartPauseButton);
        report.Check(prefix + "-button-starts-model", _match.ClockRunning && StatusText.Text == "LIVE");
        await AflOperatorFlowQa.WaitAsync(() => _match.ElapsedInQuarter.TotalSeconds >= 1.2, "Clock did not advance.");
        AflQaClick(StartPauseButton);
        wallClock.Stop();
        await AflOperatorFlowQa.SettleAsync();
        TimeSpan elapsed = _match.ElapsedInQuarter;
        string header = HeaderQuarterClock.Text;
        report.Check(prefix + "-button-pauses-model", !_match.ClockRunning && StatusText.Text == "PAUSED" && elapsed.TotalSeconds is >= 1 and < 5
            && Math.Abs(elapsed.TotalMilliseconds - wallClock.Elapsed.TotalMilliseconds) < 250);
        // An independent integer-seconds oracle, using the literal fixture
        // duration rather than MatchManager.DisplayClock or its formatter.
        long displaySeconds = countdown
            ? Math.Max(0, (20 * TimeSpan.TicksPerMinute - elapsed.Ticks) / TimeSpan.TicksPerSecond)
            : elapsed.Ticks / TimeSpan.TicksPerSecond;
        string expected = $"Q1  {displaySeconds / 60:D2}:{displaySeconds % 60:D2}";
        report.Check(prefix + "-display-direction", header == expected);
        await AflOperatorFlowQa.SettleAsync(650);
        report.Check(prefix + "-no-post-pause-advance", _match.ElapsedInQuarter == elapsed && HeaderQuarterClock.Text == header && !_match.ClockRunning);
        report.observations[prefix + "-paused-header"] = header;
        await AflQaObserveAsync(report, prefix);
    }

    internal async Task AflQaDenialProbesAsync(AflOperatorFlowQa.Report report)
    {
        async Task Probe(string sink, Func<Task> invoke)
        {
            var before = OperatorFlowIsolation.Snapshot().GetValueOrDefault(sink);
            int expectedBefore = before?.expectedProbes ?? 0, violationsBefore = before?.violations ?? 0;
            bool denied = false;
            using (OperatorFlowIsolation.ExpectDenial(sink))
            {
                try { await invoke(); }
                catch (OperatorFlowIsolation.DeniedException error) when (error.Sink == sink) { denied = true; }
            }
            var after = OperatorFlowIsolation.Snapshot().GetValueOrDefault(sink);
            report.Check("denied-" + sink, denied && after is not null && after.expectedProbes == expectedBefore + 1 && after.violations == violationsBefore);
        }
        Task Invoke(Action action) { action(); return Task.CompletedTask; }
        var team = new TeamCatalogEntry("qa-only", "QA Fixture", "QAF", "QA", "QA", "#000000", "#FFFFFF", "");
        await Probe("output-training", () => Invoke(EnsureTrainingDisplayWindow));
        await Probe("output-cricket", () => Invoke(EnsureCricketDisplayWindow));
        await Probe("cloud-session", StartCloudSessionAsync);
        await Probe("cloud-config-onload", () => Invoke(StartCloudRemoteOnLoad));
        await Probe("cloud-config", () => Invoke(() => SupabaseConfig.TryLoad()));
        await Probe("updater-cleanup", () => Invoke(AutoUpdateService.CleanupStaleStagingDirectories));
        await Probe("updater-check", () => new AutoUpdateService().CheckForUpdateAsync());
        await Probe("updater-download", () => new AutoUpdateService().DownloadAndApplyAsync());
        await Probe("external-browser", () => Invoke(() => AutoUpdateService.OpenUrlInBrowser("https://example.invalid/qa-never-open")));
        await Probe("logo-download", () => TeamLogoService.GetTransparentLogoAsync(team));
        await Probe("logo-bundled", () => TeamLogoService.PrepareBundledLogoAsync(team, null));
        await Probe("video-playback", () => Invoke(() => VideoOptimizer.GetPlaybackPath("qa-never-read.mp4")));
        await Probe("video-optimize", () => VideoOptimizer.EnsureOptimisedAsync("qa-never-read.mp4"));
        await Probe("vlc-instance", () => Invoke(() => { _ = VlcRuntime.Instance; }));
        await Probe("vlc-validate", () => Invoke(VlcRuntime.ValidateOrThrow));
        report.Check("denial-probes-created-no-output-or-services", AflQaOutputReferencesNull && _cloudConfig is null && _cloudRemote is null && _weatherService is null
            && !VlcRuntime.IsAvailable && VlcRuntime.InitializationError is null && !VlcRuntime.InitializationAttempted);
        await AflQaObserveAsync(report, "denial-probes");
    }

    internal string[] AflQaActiveTimers()
    {
        return new object[] { this, SetupWizardPanel, CricketSetupWizardPanel, SportSelectionPanel, TrainingControlPanelView, CricketControlPanelView }
            .SelectMany(owner => owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(DispatcherTimer) && field.GetValue(owner) is DispatcherTimer { IsEnabled: true })
                .Select(field => owner.GetType().Name + "." + field.Name)).ToArray();
    }

    internal async Task AflQaRetireAsync(AflOperatorFlowQa.Report report, string name, AflOperatorFlowQa.MemoryPresets memory)
    {
        if (_aflQaRetired) return;
        _aflQaRetired = true;
        foreach (var owned in OwnedWindows.OfType<Window>().ToArray())
        {
            foreach (var nested in owned.OwnedWindows.OfType<Window>().ToArray()) nested.Close();
            owned.Close();
        }
        _match.PauseClock(); _match.RetireLifecycle();
        _setupFlowActive = false; ++_setupFlowGeneration; _setupCompletionAuthorized = false;
        StopAflSetupTransition(); StopCricketSetupTransition();
        InvalidateStoppageEditor(); InvalidatePresentationWork();
        StopBreakRotation(); StopStatsBarTimer(); ResetScoreAnimationQueue();
        _controlPanelCountdownRunning = false;
        _deferredGameStateSaveTimer?.Stop(); StopAutoSaveTimer();
        Close();
        await AflOperatorFlowQa.DrainPresetsAsync();
        await AflOperatorFlowQa.SettleAsync(250);
        string[] active = AflQaActiveTimers();
        report.observations[name + "-active-timers"] = string.Join(",", active);
        report.Check(name + "-timers-stopped", active.Length == 0);
        report.Check(name + "-outputs-null", AflQaOutputReferencesNull);
        report.Check(name + "-preset-work-drained", memory.Pending == 0);
        report.Check(name + "-closed-paused", !_match.ClockRunning && !IsVisible && !Application.Current.Windows.OfType<Window>().Contains(this));
    }
}
