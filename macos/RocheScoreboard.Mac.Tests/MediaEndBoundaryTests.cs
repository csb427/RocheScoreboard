// Real decoder lifecycle checks; distinguish fixture readiness from behavior failures.
// Real LibVLC EndReached witness, public source/play paths, no private handler calls.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
using System.Diagnostics;
using System.Text.Json;
using System.Reflection;

namespace RocheScoreboard.Mac.Tests;

public sealed class MediaEndBoundaryTests
{
    private static readonly string Pattern = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-pattern.mp4");

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Diagnostics() => string.Join("\n", VideoDiagnostics.RecentEntries().Take(16)
        .Select(entry => $"{entry.Category}: {entry.Message} {entry.ExceptionText}"));

    private static bool IsEarlyPlayingSource(VideoPlayerControl player, MediaPlayer backend) =>
        backend.IsPlaying && player.Duration.TotalSeconds is >= 4.8 and <= 5.4 &&
        player.Position.TotalSeconds is >= .08 and < .9;

#if ROCHE_NATIVE_MEDIA
    [AvaloniaTheory]
#else
    [AvaloniaTheory(Skip = "Requires ROCHE_NATIVE_MEDIA=1, existing five-second synthetic MP4 and real LibVLC.")]
#endif
    [InlineData("current")]
    [InlineData("retarget-before-dispatch")]
    [InlineData("retarget-in-state-observer")]
    public async Task AWitnessedNativeEndBelongsOnlyToItsCurrentSourceAndObserverScope(string route)
    {
        // This fixture creates no operator window or preference/recovery files.
        // Diagnostics use the application's existing process-local test storage latch.
        SelfTestStorage.Initialize(true);
        string directory = Path.Combine(Path.GetTempPath(), "roche-media-end-boundary-" + Guid.NewGuid().ToString("N"));
        string original = Path.Combine(directory, "original-pattern.mp4");
        string replacement = Path.Combine(directory, "replacement-pattern.mp4");
        VideoPlayerControl? player = null;
        NativeWindow? window = null;
        Action? detachNativeWitness = null;
        Action? detachPauseWitness = null;
        Action? detachObservers = null;
        using var nativeEndSeen = new ManualResetEventSlim();
        int nativeEnds = 0;
        int publicEnds = 0;
        int failures = 0;
        int replacementFalseStates = 0;
        int observerRetargets = 0;
        bool endHandlerLogged = false;
        bool falseBeforeEndHandler = false;
        bool observeReplacementFalse = false;
        var readinessSamples=new List<object>();var readinessClock=Stopwatch.StartNew();
        void RecordReadiness(string phase,NativeVideoSurface surface,MediaPlayer backend)
        {
            readinessSamples.Add(new{phase,elapsedMilliseconds=readinessClock.ElapsedMilliseconds,
                frames=surface.FramesPresented,nativePlaying=backend.IsPlaying,nativeState=backend.State.ToString(),
                nativeLength=backend.Length,nativeTime=backend.Time,nativeSeekable=backend.IsSeekable,
                publicPlaying=player!.IsPlaying,publicDuration=player.Duration.TotalSeconds,publicPosition=player.Position.TotalSeconds,
                currentOriginal=player.CurrentSourcePath==original,nativeEnds=Volatile.Read(ref nativeEnds),publicEnds,failures});
        }
        string ReadySummary()=>JsonSerializer.Serialize(readinessSamples.TakeLast(3));
        Exception? observerError = null;
        try
        {
            Assert.True(File.Exists(Pattern), "Existing five-second synthetic fixture must be copied to test output.");
            Directory.CreateDirectory(directory);
            File.Copy(Pattern, original);
            File.Copy(Pattern, replacement);
            player = new VideoPlayerControl { Loop = false };
            window = new NativeWindow { Width = 480, Height = 270, Content = player };
            window.Show();
            await Settle(150);
            var surface = Assert.IsType<NativeVideoSurface>(player.FindControl<NativeVideoSurface>("VideoPlayer"));
            var backend = surface.MediaPlayer;
            Assert.NotNull(backend);
            player.PlaybackEnded += (_, _) => publicEnds++;
            player.MediaLoadFailed += (_, _) => failures++;
            void WitnessNativeEnd(object? sender, EventArgs args)
            {
                Interlocked.Increment(ref nativeEnds);
                nativeEndSeen.Set();
            }
            // OnLoaded attached production OnVlcEndReached first. This witness
            // therefore observes the real multicast event after production has
            // posted its UI work, not a simulated invocation of that handler.
            backend.EndReached += WitnessNativeEnd;
            detachNativeWitness = () => backend.EndReached -= WitnessNativeEnd;
            player.SetVideoSource(original);
            player.Play();
            // Prepare one explicit start through public media controls. A real
            // first frame/metadata is independent of the narrow early-position
            // witness: a loaded renderer must not silently miss that window.
            // This is one setup sequence, not retrying or resetting end counts.
            RecordReadiness("A-before-poll",surface,backend);
            for(int attempt=0;attempt<60 && (surface.FramesPresented==0 || !backend.IsPlaying
                || player.Duration.TotalSeconds is <4.8 or >5.4);attempt++)
            { await Settle(50);RecordReadiness("A-metadata-"+attempt,surface,backend); }
            Assert.True(surface.FramesPresented>0,"Native A must present a real decoded frame.\n"+ReadySummary());
            Assert.True(backend.IsPlaying);Assert.InRange(player.Duration.TotalSeconds,4.8,5.4);
            Assert.Equal(original,player.CurrentSourcePath);Assert.Equal(0,Volatile.Read(ref nativeEnds));Assert.Equal(0,publicEnds);Assert.Equal(0,failures);

            int pauseWitnesses=0;
            void WitnessPaused(object? sender,EventArgs args)=>Interlocked.Increment(ref pauseWitnesses);
            backend.Paused+=WitnessPaused;detachPauseWitness=()=>backend.Paused-=WitnessPaused;
            player.Pause();
            for(int attempt=0;attempt<60 && (Volatile.Read(ref pauseWitnesses)==0 || backend.IsPlaying
                || backend.State!=VLCState.Paused || player.IsPlaying);attempt++)
            { await Settle(50);RecordReadiness("A-pause-"+attempt,surface,backend); }
            Assert.True(Volatile.Read(ref pauseWitnesses)>0);Assert.Equal(VLCState.Paused,backend.State);
            Assert.False(backend.IsPlaying);Assert.False(player.IsPlaying);
            Dispatcher.UIThread.RunJobs(); // Drain the genuine paused callback before the next Play.
            player.Seek(TimeSpan.FromMilliseconds(200));
            // Position may already be inside the early window. Drain the real
            // coalesced request unconditionally, rather than letting that old
            // position bypass its dispatcher work. Private reads observe only.
            int PendingSeek() => (int)typeof(VideoPlayerControl).GetField("_seekDispatchPending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(player)!;
            Assert.Equal(1,PendingSeek());Dispatcher.UIThread.RunJobs();Assert.Equal(0,PendingSeek());
            RecordReadiness("A-seek-dispatch-drained",surface,backend);
            for(int attempt=0;attempt<60 && (player.Position.TotalSeconds is <.08 or >=.9);attempt++)
            { await Settle(50);RecordReadiness("A-seek-"+attempt,surface,backend); }
            Assert.InRange(player.Position.TotalSeconds,.08,.899999);Assert.Equal(VLCState.Paused,backend.State);
            Assert.False(player.IsPlaying);Assert.Equal(original,player.CurrentSourcePath);
            Assert.Equal(0,Volatile.Read(ref nativeEnds));Assert.Equal(0,publicEnds);Assert.Equal(0,failures);
            RecordReadiness("A-controlled-paused-start",surface,backend);
            player.Play();
            Assert.True(SpinWait.SpinUntil(()=>IsEarlyPlayingSource(player,backend),TimeSpan.FromMilliseconds(1500)),
                "Controlled A must genuinely play inside the unchanged early-position and duration bounds.\n"+ReadySummary());
            RecordReadiness("A-before-withheld-ui",surface,backend);
            Assert.True(IsEarlyPlayingSource(player,backend));Assert.True(player.IsPlaying);
            Assert.Equal(original,player.CurrentSourcePath);Assert.Equal(0,Volatile.Read(ref nativeEnds));Assert.Equal(0,publicEnds);Assert.Equal(0,failures);

            // Deliberately no await/RunJobs: decoder runs to its real end while
            // its production EndReached and state callbacks remain UI-queued.
            // The watchdog also cannot tick in this section.
            Assert.True(nativeEndSeen.Wait(TimeSpan.FromSeconds(6)),
                "Native EndReached was not witnessed with UI withheld; fixture failure.\n" + Diagnostics());
            Assert.Equal(1, Volatile.Read(ref nativeEnds));
            Assert.False(backend.IsPlaying);
            Assert.True(player.IsPlaying); // Production's false-state UI work has not run.
            Assert.Equal(0, publicEnds);
            Assert.Equal(0, failures);

            void ObserveDiagnostic(object? sender, VideoDiagnosticEntry entry)
            {
                // Evidence only. This never drives a production transition.
                if (Dispatcher.UIThread.CheckAccess() && entry.Category == "VideoPlayer" &&
                    entry.Message == "EndReached (loop=False).")
                    endHandlerLogged = true;
            }
            void ObserveState(object? sender, EventArgs args)
            {
                if (observeReplacementFalse && player.CurrentSourcePath == replacement && !player.IsPlaying)
                    replacementFalseStates++;
                if (route != "retarget-in-state-observer" || player.IsPlaying ||
                    player.CurrentSourcePath != original || observerRetargets != 0) return;
                if (!endHandlerLogged)
                {
                    // A preceding native Stopped callback would otherwise make
                    // this appear to test the post-EndReached observer guard.
                    falseBeforeEndHandler = true;
                    return;
                }
                observerRetargets++;
                try
                {
                    // Actual public reentry while EndReached is notifying its
                    // false-state observer; the outer end callback then resumes.
                    player.SetVideoSource(replacement);
                    player.Play();
                    observeReplacementFalse = true;
                }
                catch (Exception error) { observerError = error; }
            }
            VideoDiagnostics.EntryAdded += ObserveDiagnostic;
            player.PlaybackStateChanged += ObserveState;
            detachObservers = () =>
            {
                VideoDiagnostics.EntryAdded -= ObserveDiagnostic;
                player.PlaybackStateChanged -= ObserveState;
            };

            if (route == "current")
            {
                Dispatcher.UIThread.RunJobs();
                Assert.True(endHandlerLogged);
                Assert.Equal(1, publicEnds);
                Assert.Equal(original, player.CurrentSourcePath);
                Assert.False(player.IsPlaying);
                Assert.False(backend.IsPlaying);
                Assert.Equal(0, failures);
                await Settle(150);
                Assert.Equal(1, publicEnds); // Current, non-looping end emits once.
                Assert.Equal(1, Volatile.Read(ref nativeEnds));
                Assert.False(player.IsPlaying);
                Assert.Equal(0, failures);
                return;
            }

            if (route == "retarget-before-dispatch")
            {
                player.SetVideoSource(replacement);
                player.Play();
                // A is genuinely ended near 3s. Native B must reset into an
                // advancing low position before any old UI work is released.
                Assert.True(SpinWait.SpinUntil(() => IsEarlyPlayingSource(player, backend),
                    TimeSpan.FromMilliseconds(1500)),
                    "Native B readiness failed while UI withheld; not a stale-end baseline.\n" + Diagnostics());
                Assert.Equal(replacement, player.CurrentSourcePath);
                Assert.True(player.IsPlaying);
                Assert.Equal(0, publicEnds);
                Assert.Equal(1, Volatile.Read(ref nativeEnds));
                // SetVideoSource legitimately notified false before Play.
                // Count only state changes after B is already playing.
                observeReplacementFalse = true;
            }

            Dispatcher.UIThread.RunJobs(); // Release the witnessed A end/state queue.
            if (route == "retarget-in-state-observer")
            {
                Assert.False(falseBeforeEndHandler,
                    "A Stopped callback changed state before EndReached; fixture did not reach the intended observer reentry.");
                Assert.True(endHandlerLogged);
                Assert.Equal(1, observerRetargets);
                Assert.Null(observerError);
                // The observer runs synchronously during the old end callback;
                // real B readiness is then checked without pumping extra UI jobs.
                Assert.True(SpinWait.SpinUntil(() => IsEarlyPlayingSource(player, backend),
                    TimeSpan.FromMilliseconds(1500)),
                    "Native observer-selected B did not become ready; fixture failure.\n" + Diagnostics());
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(replacement, player.CurrentSourcePath);
            Assert.True(player.IsPlaying);
            Assert.True(backend.IsPlaying);
            Assert.Equal(0, publicEnds); // Neither stale dispatch nor observer reentry may end B.
            Assert.Equal(0, replacementFalseStates); // Also reject a transient false state later overwritten by B's Playing event.
            Assert.Equal(0, failures);
            double positionBefore = player.Position.TotalSeconds;
            Assert.InRange(positionBefore, .05, 1.4);
            // Still withhold UI while requiring actual decoder progress; a
            // retained bool/time alone is insufficient evidence B kept playing.
            Assert.True(SpinWait.SpinUntil(() => player.Position.TotalSeconds >= positionBefore + .08,
                TimeSpan.FromMilliseconds(650)),
                "Replacement decoder did not continue advancing after obsolete work was released.\n" + Diagnostics());
            Assert.Equal(1, Volatile.Read(ref nativeEnds)); // B must not have reached its own natural end during this observation.
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(replacement, player.CurrentSourcePath);
            Assert.True(player.IsPlaying);
            Assert.True(backend.IsPlaying);
            Assert.Equal(0, publicEnds);
            Assert.Equal(0, replacementFalseStates);
            Assert.Equal(0, failures);
        }
        finally
        {
            try
            {
                if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} evidence)
                {
                    Directory.CreateDirectory(evidence);
                    File.WriteAllText(Path.Combine(evidence,"media-end-readiness-"+route+".json"),JsonSerializer.Serialize(new
                    {route,samples=readinessSamples,nativeEnds,publicEnds,failures},new JsonSerializerOptions{WriteIndented=true}));
                }
            }
            finally
            {
                // Detach witnesses before backend disposal; cleanup attempts every
                // owned resource even after partial setup or a readiness assertion.
                try { detachObservers?.Invoke(); }
                finally
                {
                    try { try { detachPauseWitness?.Invoke(); } finally { detachNativeWitness?.Invoke(); } }
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
    }
}
