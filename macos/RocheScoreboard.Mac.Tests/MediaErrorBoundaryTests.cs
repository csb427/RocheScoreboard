// Real native-event boundary regressions; readiness failures are fixture failures.
// The backend native event is witnessed directly; no private callback is invoked.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class MediaErrorBoundaryTests
{
    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Diagnostics() => string.Join("\n", VideoDiagnostics.RecentEntries().TakeLast(16)
        .Select(entry => $"{entry.Category}: {entry.Message} {entry.ExceptionText}"));

#if ROCHE_NATIVE_MEDIA
    [AvaloniaTheory]
#else
    [AvaloniaTheory(Skip = "Requires ROCHE_NATIVE_MEDIA=1, synthetic MP4 and real LibVLC native errors.")]
#endif
    [InlineData("close")]
    [InlineData("retarget")]
    [InlineData("stop")]
    [InlineData("current")]
    public async Task AWitnessedQueuedNativeErrorCannotMutateAClosedOrReplacementScope(string boundary)
    {
        string directory = Path.Combine(Path.GetTempPath(), "roche-media-error-boundary-" + Guid.NewGuid().ToString("N"));
        string invalid = Path.Combine(directory, "invalid-source.mp4");
        string valid = Path.Combine(directory, "replacement-pattern.mp4");
        VideoPlayerControl? player = null;
        NativeWindow? window = null;
        Action? detachWitness = null;
        using var nativeErrorSeen = new ManualResetEventSlim();
        int nativeErrors = 0;
        int publicFailures = 0;
        try
        {
            Directory.CreateDirectory(directory);
            // A missing local source reliably produces a decoder I/O error;
            // arbitrary text can be accepted by VLC as a subtitle/playlist.
            Assert.False(File.Exists(invalid));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-pattern.mp4"), valid);
            player = new VideoPlayerControl { Loop = false };
            window = new NativeWindow { Width = 480, Height = 270, Content = player };
            window.Show();
            await Settle(150);
            var surface = Assert.IsType<NativeVideoSurface>(player.FindControl<NativeVideoSurface>("VideoPlayer"));
            var backend = surface.MediaPlayer;
            Assert.NotNull(backend);
            player.MediaLoadFailed += (_, _) => publicFailures++;
            void Witness(object? sender, EventArgs args)
            {
                Interlocked.Increment(ref nativeErrors);
                nativeErrorSeen.Set();
            }
            // Production's OnLoaded subscribed OnVlcError before this observer.
            // Witness only signals after the actual native multicast event has
            // reached the production callback, which queues its UI work.
            backend.EncounteredError += Witness;
            detachWitness = () => backend.EncounteredError -= Witness;
            player.SetVideoSource(invalid);
            player.Play();
            // No await/RunJobs until the old native callback has been witnessed.
            // A timeout is a native-fixture failure, never source-generation proof.
            Assert.True(nativeErrorSeen.Wait(TimeSpan.FromSeconds(3)),
                "Native decoder did not report EncounteredError for invalid fixture.\n" + Diagnostics());
            Assert.True(Volatile.Read(ref nativeErrors) > 0);
            Assert.Equal(0, publicFailures); // Proves its UI notification has not run yet.
            if (boundary == "current")
            {
                await Settle(200);
                Assert.Equal(1, publicFailures);
                Assert.Null(player.CurrentSourcePath);
                Assert.False(player.IsPlaying);
                return;
            }
            if (boundary == "stop")
            {
                player.Stop();
                await Settle(200);
                Assert.Equal(0, publicFailures);
                Assert.Null(player.CurrentSourcePath);
                Assert.False(player.IsPlaying);
                return;
            }
            if (boundary == "close")
            {
                // Remove only the test witness while backend is still alive;
                // removing it after Close could touch a disposed EventManager.
                detachWitness();
                detachWitness = null;
                window.Close();
                Assert.False(window.IsVisible);
                await Settle(200);
                Assert.Equal(0, publicFailures);
                Assert.False(player.IsPlaying);
                return;
            }

            // Invalid A has no valid duration, so an actual playing native B
            // with advancing low position cannot match A's retained state.
            player.SetVideoSource(valid);
            player.Play();
            bool ready = SpinWait.SpinUntil(() => backend.IsPlaying && player.Duration.TotalSeconds >= 2.8 &&
                player.Position.TotalSeconds is >= .08 and < .9, TimeSpan.FromMilliseconds(1500));
            Assert.True(ready, "Native valid B readiness failed; not an obsolete-error baseline.\n" + Diagnostics());
            player.Pause();
            Assert.True(SpinWait.SpinUntil(() => !backend.IsPlaying, TimeSpan.FromMilliseconds(500)),
                "Native B must actually be paused before releasing A's failure callback.");
            Assert.Equal(valid, player.CurrentSourcePath);
            Assert.False(player.SoftwareFallbackAttempted);
            Assert.Equal(0, publicFailures);
            await Settle(250);
            Assert.Equal(valid, player.CurrentSourcePath);
            Assert.Equal(0, publicFailures);
            // Old A's error must not retry valid B in software or resume B
            // behind the operator's explicit pause. This is meaningful even
            // when the fallback succeeds and emits no MediaLoadFailed event.
            Assert.False(player.SoftwareFallbackAttempted);
            Assert.False(player.IsPlaying);
            Assert.False(backend.IsPlaying);
            Assert.InRange(player.Position.TotalSeconds, 0, 1.25);
        }
        finally
        {
            // Stop/dispose backend before disposing the native event signal.
            // Attempt all teardown paths even after partial setup or assertion failure.
            try { detachWitness?.Invoke(); }
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
}
