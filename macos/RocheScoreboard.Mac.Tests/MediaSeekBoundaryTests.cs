// Real decoder regressions. A readiness failure is not a product-behavior baseline.
// Tests use the existing real LibVLC synthetic fixture when ROCHE_NATIVE_MEDIA=1.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class MediaSeekBoundaryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Pattern = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-pattern.mp4");

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Field<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, Private)?.GetValue(target)
            ?? throw new InvalidOperationException("Fixture field unavailable: " + name));

    private static void FixtureSetupCall(object target, string method, params object[] arguments) =>
        (target.GetType().GetMethod(method, Private)
            ?? throw new InvalidOperationException("Fixture setup method unavailable: " + method))
            .Invoke(target, arguments);

    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(root.FindControl<Control>(name));

    private static void Click(Button action) =>
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Diagnostics() => string.Join("\n", VideoDiagnostics.RecentEntries().TakeLast(16)
        .Select(entry => $"{entry.Category}: {entry.Message} {entry.ExceptionText}"));

    private static async Task DecodeAndPause(VideoPlayerControl player, string path)
    {
        Assert.True(File.Exists(path), "Existing synthetic fixture must be copied to test output.");
        player.SetVideoSource(path);
        player.Play();
        var surface = Named<NativeVideoSurface>(player, "VideoPlayer");
        for (int attempt = 0; attempt < 40 &&
            (surface.FramesPresented == 0 || player.Duration.TotalSeconds < 2.8); attempt++)
            await Settle(100);
        Assert.True(surface.FramesPresented > 0, "Fixture did not decode a real frame.\n" + Diagnostics());
        Assert.True(player.Duration.TotalSeconds >= 2.8,
            "Fixture needs enough real duration to distinguish a stale >2s seek.\n" + Diagnostics());
        player.Pause();
        await Settle(180);
        Assert.False(player.IsPlaying);
        player.Seek(TimeSpan.FromSeconds(.25));
        await Settle(180);
        Assert.InRange(player.Position.TotalSeconds, 0, .9);
    }

    // Mirrors existing test isolation, including SelfTestStorage's separate process directory.
    // Reflection here is fixture setup/data inspection, never private media-handler invocation.
    private sealed class OperatorFixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool selfTest;
        private readonly bool? motion = OperatorMotion.AnimationOverride;
        private readonly string? dataHomeBefore = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-media-keyboard-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> files = new();
        private readonly HashSet<string> oldDirectories;
        private Roche_Scoreboard.MainWindow? window;
        private bool disposed;
        internal Roche_Scoreboard.MainWindow Window => window ?? throw new InvalidOperationException("Fixture window unavailable.");
        internal VideoPlayerControl Player => Field<VideoPlayerControl>(Window, "_videoPlayer");
        internal MatchManager Match => Field<MatchManager>(Window, "_match");

        internal OperatorFixture()
        {
            selfTest = app.IsSelfTestMode;
            // Headless startup does not run WPF's OnStartup test-mode latch.
            // Use that same process-local latch before resolving any save paths.
            SelfTestStorage.Initialize(true);
            Assert.True(SelfTestStorage.IsEnabled, "Media fixture must use process-isolated self-test storage.");
            // GetStoragePath can create parent directories: record their preexisting
            // state before invoking either getter or constructing any window.
            oldDirectories = Directory.Exists(SelfTestStorage.RootPath)
                ? Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                    .Append(SelfTestStorage.RootPath).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            try
            {
                app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", dataHome);
                Directory.CreateDirectory(dataHome);
                string workflow = (string)typeof(OperatorWorkflowPreferences)
                    .GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
                string display = (string)typeof(DisplaySettings)
                    .GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
                foreach (string path in new[] { workflow, display, GameStateService.RecoveryStatePath,
                    SelfTestStorage.PathFor("legacy", "game_state.json"), OperatorAppearance.StoragePath,
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json"),
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "ux_preferences.json") })
                    foreach (string suffix in new[] { "", ".bak", ".tmp" })
                        files.TryAdd(path + suffix, File.Exists(path + suffix) ? File.ReadAllBytes(path + suffix) : null);
                DisplaySettings.LastVideoPath = null;
                GameStateService.Clear();
                window = new Roche_Scoreboard.MainWindow();
                window.Show();
            }
            catch (Exception setup)
            {
                var failures = Cleanup();
                if (failures.Count > 0) throw new AggregateException("Fixture setup and rollback failed.", new[] { setup }.Concat(failures));
                throw;
            }
        }

        internal async Task ReadyMedia()
        {
            await Settle(180);
            Named<SportSelectionScreen>(Window, "SportSelectionPanel").IsVisible = false;
            FixtureSetupCall(Window, "FinalizeAflSetup", new SetupResult
            { HomeName = "Media home", AwayName = "Media away", WeatherLocation = "", ClockMode = ClockMode.CountUp });
            FixtureSetupCall(Window, "EnableOperatorOutput");
            await Settle(300);
            var media = Named<TabItem>(Window, "MediaDeskTab");
            if (!media.IsVisible) Click(Named<Button>(Window, "GuidedModeButton"));
            Assert.True(media.IsVisible);
            // Decoder visibility/source are fixture prerequisites; picker and
            // takeover behavior are not claimed by this keyboard regression.
            Player.IsVisible = true;
            await DecodeAndPause(Player, Pattern);
            Named<TabControl>(Window, "MainTabControl").SelectedItem = media;
            await Settle(250);
            Window.Activate();
            await Settle(80);
            Assert.Same(media, Named<TabControl>(Window, "MainTabControl").SelectedItem);
            Assert.False(Match.ClockRunning);
        }

        private List<Exception> Cleanup()
        {
            var failures = new List<Exception>();
            if (disposed) return failures;
            disposed = true;
            void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
            if (window is { } owner)
            {
                // Partial setup can leave no output field. Cleanup must still
                // close owner and restore every captured value if Close throws.
                Attempt(() => (owner.GetType().GetField("_displayWindow", Private)?.GetValue(owner) as NativeWindow)?.Close());
                Attempt(owner.Close);
                window = null;
            }
            foreach (var (path, bytes) in files)
                Attempt(() =>
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
                });
            Attempt(() => app.IsSelfTestMode = selfTest);
            Attempt(() => OperatorMotion.AnimationOverride = motion);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", dataHomeBefore));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            Attempt(() =>
            {
                if (!Directory.Exists(SelfTestStorage.RootPath)) return;
                foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                    .Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length))
                    if (!oldDirectories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
            });
            return failures;
        }

        public void Dispose()
        {
            var failures = Cleanup();
            if (failures.Count > 0) throw new AggregateException("Media fixture cleanup failed.", failures);
        }
    }

#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip = "Requires ROCHE_NATIVE_MEDIA=1, existing synthetic MP4 and real LibVLC runtime.")]
#endif
    public async Task ActualMediaSliderArrowInputSeeksThePausedDecoderWithoutChangingTheMatch()
    {
        using var fixture = new OperatorFixture();
        await fixture.ReadyMedia();
        var slider = Named<Slider>(fixture.Window, "VideoTabSeekSlider");
        Assert.True(slider.Maximum >= 2.8, "Real position sync must expose decoded duration before input.");
        slider.BringIntoView();
        await Settle(80);
        Assert.True(slider.Focus(NavigationMethod.Tab));
        Assert.True(slider.IsFocused);
        Assert.True(slider.IsEffectivelyVisible);
        Assert.True(slider.Bounds.Width > 20 && slider.Bounds.Height > 0);
        double before = slider.Value;
        double expected = before + slider.SmallChange;
        Assert.InRange(expected, .9, slider.Maximum - .2);
        string matchBefore = JsonSerializer.Serialize(fixture.Match.ToState());
        fixture.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        // This condition proves actual native slider input reached its owning control.
        Assert.InRange(slider.Value, expected - .05, expected + .05);
        await Settle(300); // Flush background seek and allow real paused decoder seek.
        Assert.InRange(fixture.Player.Position.TotalSeconds, expected - .35, expected + .35);
        Assert.False(fixture.Player.IsPlaying);
        Assert.Equal(matchBefore, JsonSerializer.Serialize(fixture.Match.ToState()));
    }

#if ROCHE_NATIVE_MEDIA
    [AvaloniaTheory]
#else
    [AvaloniaTheory(Skip = "Requires ROCHE_NATIVE_MEDIA=1, existing synthetic MP4 and real LibVLC runtime.")]
#endif
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQueuedSeekCannotMoveAReplacementSourceAfterThatSourceIsReady(bool newSourceSeek)
    {
        string directory = Path.Combine(Path.GetTempPath(), "roche-media-source-boundary-" + Guid.NewGuid().ToString("N"));
        string replacement = Path.Combine(directory, "replacement-pattern.mp4");
        VideoPlayerControl? player = null;
        NativeWindow? window = null;
        try
        {
            Directory.CreateDirectory(directory);
            File.Copy(Pattern, replacement);
            player = new VideoPlayerControl { Loop = false };
            window = new NativeWindow { Width = 480, Height = 270, Content = player };
            window.Show();
            await Settle(150);
            await DecodeAndPause(player, Pattern);
            // Do not let B's readiness predicate match A's retained paused.25s.
            player.Seek(TimeSpan.FromSeconds(1.25));
            await Settle(200);
            Assert.InRange(player.Position.TotalSeconds, 1.1, 1.7);
            var backend = Named<NativeVideoSurface>(player, "VideoPlayer").MediaPlayer;
            Assert.NotNull(backend);
            Assert.False(backend.IsPlaying);
            double oldTarget = Math.Min(2.5, player.Duration.TotalSeconds - .3);
            Assert.True(oldTarget >= 2, "Need distinguishable old source seek target.");
            int ended = 0, failed = 0;
            player.PlaybackEnded += (_, _) => ended++;
            player.MediaLoadFailed += (_, _) => failed++;
            // No await/RunJobs: A's Background seek stays queued while native
            // LibVLC loads B. The backend/time gate is bounded and test-only.
            player.Seek(TimeSpan.FromSeconds(oldTarget));
            player.SetVideoSource(replacement);
            player.Play();
            bool ready = SpinWait.SpinUntil(() => backend.IsPlaying && player.Duration.TotalSeconds >= 2.8 &&
                player.Position.TotalSeconds is >= .08 and < .9, TimeSpan.FromMilliseconds(1500));
            Assert.True(ready, "Native B readiness failed; not an obsolete-seek baseline.\n" + Diagnostics());
            player.Pause();
            Assert.True(SpinWait.SpinUntil(() => !backend.IsPlaying, TimeSpan.FromMilliseconds(500)),
                "Native B must actually be paused before releasing obsolete work.");
            Assert.InRange(player.Position.TotalSeconds, .05, 1.1);
            Assert.Equal(replacement, player.CurrentSourcePath);
            if (newSourceSeek)
            {
                // Retiring A's callback must not consume/reset B's new queue.
                player.Seek(TimeSpan.FromSeconds(.5));
                player.Seek(TimeSpan.FromSeconds(1.5));
            }
            await Settle(250);
            Assert.Equal(replacement, player.CurrentSourcePath);
            if (newSourceSeek) Assert.InRange(player.Position.TotalSeconds, 1.25, 1.75);
            else Assert.InRange(player.Position.TotalSeconds, 0, 1.25);
            Assert.False(player.IsPlaying);
            Assert.False(backend.IsPlaying);
            Assert.Equal(0, ended);
            Assert.Equal(0, failed);
        }
        finally
        {
            try { player?.Stop(); }
            finally
            {
                try { window?.Close(); }
                finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            }
        }
    }
}
