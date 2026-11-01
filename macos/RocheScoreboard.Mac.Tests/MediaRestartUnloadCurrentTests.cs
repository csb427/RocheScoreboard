using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
#if ROCHE_NATIVE_MEDIA
    [AvaloniaTheory]
#else
    [AvaloniaTheory(Skip="Requires existing real LibVLC and synthetic MP4 fixture.")]
#endif
    [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","MediaRestartUnloadCurrent")]
    public async Task RestartAndUnloadStayCurrentThroughRealMediaControlsAndReturnPreviousScreen(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        string? saved=DisplaySettings.LastVideoPath;
        try
        {
            string path=Path.Combine(AppContext.BaseDirectory,"Fixtures","test-pattern.mp4");Assert.True(File.Exists(path));
            var browse=await MediaBrowseActor(f);
            f.Window.MediaPickerSelectionOverride=(picker,owner)=>{Assert.Same(f.Window,owner);picker.FileName=path;return true;};
            await Tap(f.Window,browse);
            var player=Mr6Read<VideoPlayerControl>(f,"_videoPlayer");
            var surface=Assert.IsType<NativeVideoSurface>(player.FindControl<NativeVideoSurface>("VideoPlayer"));
            Assert.True(player.HasSource);Assert.False(player.IsPlaying); // Browse cues; decoding starts only on explicit playback.
            string state=ManualWeatherModel(f),previous=PresPlanRead<string>(f,"_activeScreenKey");
            var show=Named<Button>(f.Window,"VideoTabShowOnScoreboardButton");await MediaBrowseReveal(f,show);await Tap(f.Window,show);
            Assert.True(PresPlanRead<bool>(f,"_videoTakeoverActive"));Assert.True(player.IsVisible);Assert.False(player.IsPlaying);
            // Takeover stores the actual prior view; it deliberately does not
            // replace the navigation screen key with a fabricated video key.
            var previousView=Assert.IsAssignableFrom<Control>(PresPlanRead<object?>(f,"_preTakeoverScreen"));Assert.False(previousView.IsVisible);
            var restart=Named<Button>(f.Window,"VideoTabRestartButton");await MediaBrowseReveal(f,restart);Assert.True(restart.IsEnabled);
            Capture(f.Window,"media-restart-target-"+width);Assert.True(restart.Bounds.Height>=39.5,"Restart must reserve a40px target,like the adjacentPlay control.");Assert.True(restart.FontSize>=12);
            await Tap(f.Window,restart);
            await Until(()=>surface.FramesPresented>0&&player.Duration.TotalSeconds>=2.8,"Explicit first Restart must decode a real clip before testing a nonzero restart.");
            player.Pause();await Settle(180);Assert.False(player.IsPlaying);
            player.Seek(TimeSpan.FromSeconds(2));await Until(()=>player.Position.TotalSeconds>=1.5&&!player.IsPlaying,"Public seek establishes a nonzero paused restart position.");
            int frames=surface.FramesPresented;await Tap(f.Window,restart);
            await Until(()=>player.IsPlaying&&surface.FramesPresented>frames&&player.Position.TotalSeconds<1.5,"Restart must produce a fresh decoded frame near beginning, not merely update requested UI text.");
            Assert.Equal(state,ManualWeatherModel(f));Assert.True(PresPlanRead<bool>(f,"_videoTakeoverActive"));
            Capture(f.Window,"media-restarted-"+width);
            // Freeze the now-proven restart frame before scrolling to Unload,
            // so a short fixture's natural EndReached cannot restore it first.
            player.Pause();await Settle(100);Assert.False(player.IsPlaying);
            var unload=Named<Button>(f.Window,"VideoTabUnloadButton");await MediaBrowseReveal(f,unload);Assert.True(unload.Bounds.Height>=39.5);Assert.True(unload.FontSize>=12);
            Assert.True(PresPlanRead<bool>(f,"_videoTakeoverActive"));Assert.False(previousView.IsVisible);await Tap(f.Window,unload);
            await Settle(250);
            Assert.False(player.HasSource);Assert.False(player.IsPlaying);Assert.Null(PresPlanRead<string?>(f,"_customVideoPath"));Assert.Null(DisplaySettings.LastVideoPath);
            Assert.False(PresPlanRead<bool>(f,"_videoTakeoverActive"));Assert.Equal(previous,PresPlanRead<string>(f,"_activeScreenKey"));Assert.True(previousView.IsVisible);Assert.False(player.IsVisible);
            Assert.False(restart.IsEnabled);Assert.False(unload.IsEnabled);Assert.False(Named<Button>(f.Window,"VideoTabPlayPauseButton").IsEnabled);
            Assert.Equal(0,Named<Slider>(f.Window,"VideoTabSeekSlider").Value);Assert.Equal("No file selected",Named<TextBlock>(f.Window,"VideoTabFilePathText").Text);
            Assert.Contains("unloaded",Named<TextBlock>(f.Window,"VideoTabStatusText").Text,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(state,ManualWeatherModel(f));Assert.True(PresWindowOpen(f));Capture(f.Window,"media-unloaded-"+width);
        }
        finally{DisplaySettings.LastVideoPath=saved;}
    }
}
