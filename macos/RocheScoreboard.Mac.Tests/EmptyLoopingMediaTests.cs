using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class EmptyLoopingMediaTests
{
#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip="Requires existing native LibVLC.")]
#endif
    [Trait("Evidence","EmptyLoopingMedia")]
    public async Task EmptyLoopingSourceReportsFailureRatherThanReplayingImmediateEndForever()
    {
        SelfTestStorage.Initialize(true);
        string path=Path.Combine(Path.GetTempPath(),"roche-empty-loop-"+Guid.NewGuid().ToString("N")+".mp4");await File.WriteAllBytesAsync(path,new byte[256]);
        var player=new VideoPlayerControl{Loop=true};var window=new NativeWindow{Title="Media Display",Width=480,Height=270,Content=player};
        LibVLCSharp.Shared.MediaPlayer? backend=null;EventHandler<EventArgs>? witness=null;using var ended=new ManualResetEventSlim();int failed=0;
        try
        {
            window.Show();await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            var surface=player.FindControl<NativeVideoSurface>("VideoPlayer")!;backend=surface.MediaPlayer;Assert.NotNull(backend);
            witness=(_,_)=>ended.Set();backend.EndReached+=witness;player.MediaLoadFailed+=(_,_)=>failed++;
            player.SetVideoSource(path);player.Play();
            // Withhold only this test's UI dispatcher until the real native EOF.
            // Production has queued its own handler; it is never called privately.
            Assert.True(ended.Wait(TimeSpan.FromSeconds(5)),"Real native EndReached is required, not a fabricated event.");
            var firstEnd=new{backend.Length,backend.Time,backend.VideoTrackCount,backend.AudioTrackCount,backend.State};
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
            {Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"empty-first-native-end.json"),JsonSerializer.Serialize(firstEnd,new JsonSerializerOptions{WriteIndented=true}));}
            for(int i=0;i<20&&failed==0;i++)await Task.Delay(25);
            Assert.Equal(1,failed);Assert.False(player.HasSource);Assert.False(player.IsPlaying);
            await Task.Delay(150);Assert.Equal(1,failed);
        }
        finally
        {
            if(backend is not null&&witness is not null)backend.EndReached-=witness;
            window.Close();File.Delete(path);
        }
    }

#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip="Requires existing native LibVLC/synthetic MP4.")]
#endif
    [Trait("Evidence","EmptyLoopingMedia")]
    public async Task ValidVideoStillLoopsWithFreshFramesAndNoLoadFailure()
    {
        SelfTestStorage.Initialize(true);string path=Path.Combine(AppContext.BaseDirectory,"Fixtures","test-pattern.mp4");Assert.True(File.Exists(path));
        var player=new VideoPlayerControl{Loop=true};var window=new NativeWindow{Title="Loop Display",Width=480,Height=270,Content=player};
        LibVLCSharp.Shared.MediaPlayer? backend=null;EventHandler<EventArgs>? witness=null;int ends=0,framesAtEnd=0,failed=0;
        try
        {
            window.Show();await Task.Delay(100);Dispatcher.UIThread.RunJobs();var surface=player.FindControl<NativeVideoSurface>("VideoPlayer")!;backend=surface.MediaPlayer;Assert.NotNull(backend);
            witness=(_,_)=>{if(Interlocked.Increment(ref ends)==1)Volatile.Write(ref framesAtEnd,surface.FramesPresented);};backend.EndReached+=witness;player.MediaLoadFailed+=(_,_)=>failed++;
            player.SetVideoSource(path);player.Play();
            for(int i=0;i<300&&(Volatile.Read(ref ends)==0||surface.FramesPresented<=Volatile.Read(ref framesAtEnd)||!player.IsPlaying);i++)await Task.Delay(50);
            Assert.True(Volatile.Read(ref ends)>0,"Actual valid clip must reach native EOF.");Assert.True(Volatile.Read(ref framesAtEnd)>0);
            Assert.True(surface.FramesPresented>Volatile.Read(ref framesAtEnd),"Fresh decoded frames must follow the actual EOF, not just an optimistic state getter.");
            Assert.Equal(0,failed);Assert.True(player.HasSource);Assert.True(player.IsPlaying);Assert.True(player.Duration>TimeSpan.Zero);
        }
        finally{if(backend is not null&&witness is not null)backend.EndReached-=witness;window.Close();}
    }
}
