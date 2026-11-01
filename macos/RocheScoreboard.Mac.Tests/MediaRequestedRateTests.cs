using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class MediaRequestedRateTests
{
#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip="Requires ROCHE_NATIVE_MEDIA=1 and existing LibVLC/synthetic MP4.")]
#endif
    [Trait("Evidence","MediaRequestedRate")]
    public async Task RateChosenBeforeLoadingReachesNativePlaybackAndTheNextSource()
    {
        SelfTestStorage.Initialize(true);
        string pattern=Path.Combine(AppContext.BaseDirectory,"Fixtures","test-pattern.mp4");
        Assert.True(File.Exists(pattern));
        var player=new VideoPlayerControl { SpeedRatio=1.75,Loop=false };
        var window=new NativeWindow { Width=480,Height=270,Content=player };
        try
        {
            window.Show(); await Pump(120);
            var surface=Assert.IsType<NativeVideoSurface>(player.FindControl<NativeVideoSurface>("VideoPlayer"));
            var backend=surface.MediaPlayer; Assert.NotNull(backend);
            int failures=0; player.MediaLoadFailed+=(_,_)=>failures++;
            for(int pass=0;pass<2;pass++)
            {
                int framesBefore=surface.FramesPresented;
                player.SetVideoSource(pattern); player.Play();
                for(int i=0;i<50&&(surface.FramesPresented<=framesBefore||!backend.IsPlaying||Math.Abs(backend.Rate-1.75)>.001);i++) await Pump(50);
                Assert.True(surface.FramesPresented>framesBefore,"Each reload must present a fresh decoded frame, not reuse the old frame count or requested-rate getter alone.");
                Assert.True(backend.IsPlaying); Assert.Equal(1.75,player.SpeedRatio);
                Assert.Equal(1.75,backend.Rate,3);
                Assert.Equal(0,failures);
                player.Pause(); await Pump(100); Assert.False(player.IsPlaying);
            }
            // This is the native backend's reported accepted rate, not a
            // measured physical-display frame cadence or native WPF proof.
        }
        finally { window.Close(); await Pump(100); }
    }

    private static async Task Pump(int milliseconds)
    {
        await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs();
    }
}
