using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
#if ROCHE_NATIVE_MEDIA
    [AvaloniaTheory]
#else
    [AvaloniaTheory(Skip="Requires existing LibVLC source cue fixture.")]
#endif
    [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","MediaPreviewTruthfulness")]
    public async Task UncapturableMediaShowsAnHonestOutputNoticeWithoutAnotherDecoder(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);string? saved=DisplaySettings.LastVideoPath;
        try
        {
            // Simulate WPF's known HWND mirror capability, not native GUI proof.
            f.Window.NativeVideoMirrorSupported=false;var browse=await MediaBrowseActor(f);
            f.Window.MediaPickerSelectionOverride=(picker,owner)=>{Assert.Same(f.Window,owner);picker.FileName=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","test-pattern.mp4");return true;};
            await Tap(f.Window,browse);var player=PresPlanRead<VideoPlayerControl>(f,"_videoPlayer");Assert.True(player.HasSource);Assert.False(player.IsPlaying);
            string model=ManualWeatherModel(f);var show=Named<Button>(f.Window,"VideoTabShowOnScoreboardButton");await MediaBrowseReveal(f,show);await Tap(f.Window,show);
            Assert.True(PresPlanRead<bool>(f,"_videoTakeoverActive"));var card=Named<Control>(f.Window,"AflMediaPreviewCard");card.BringIntoView();await Settle(100);Capture(f.Window,"uncapturable-preview-"+width);
            Assert.True(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").IsVisible,"Uncapturable HWND media needs a truthful visible notice, not an empty mirror.");
            Assert.Null(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill);
            var detail=Assert.Single(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("cannot preview")==true);
            detail.BringIntoView();await Settle(100);
            var detailBox=FinalsPainted(f.Window,detail);
            Assert.True(new Rect(f.Window.ClientSize).Contains(detailBox));
            Assert.True(FinalsPainted(f.Window,Named<Control>(f.Window,"VideoTabPreviewPlaceholder")).Contains(detailBox),"Notice must fit its own preview region, not overlap the transport strip.");
            var heading=Named<TextBlock>(f.Window,"VideoTabPreviewHeading");Assert.True(FinalsPainted(f.Window,heading).Bottom<=detailBox.Top);
            Capture(f.Window,"uncapturable-notice-"+width);
            Assert.Same(player,PresPlanRead<VideoPlayerControl>(f,"_videoPlayer"));Assert.False(player.IsPlaying);Assert.Equal(model,ManualWeatherModel(f));
            var unload=Named<Button>(f.Window,"VideoTabUnloadButton");await MediaBrowseReveal(f,unload);await Tap(f.Window,unload);
            Assert.False(PresPlanRead<bool>(f,"_videoTakeoverActive"));Assert.False(player.HasSource);
            Assert.False(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").IsVisible);
            Assert.Equal("OUTPUT PREVIEW",Named<TextBlock>(f.Window,"VideoTabPreviewBadge").Text);
            Assert.Same(PresPlanRead<object>(f,"_displayContainer"),Assert.IsType<VisualBrush>(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill).Visual);
        }
        finally{DisplaySettings.LastVideoPath=saved;}
    }
    [AvaloniaFact]
    [Trait("Evidence","MediaPreviewTruthfulness")]
    public async Task ClosedOutputPreviewStaysOffAndExplicitReopenBindsTheCurrentOwner()
    {
        await using var f=await AflFixture.Create(420,320);await MediaBrowseActor(f);string model=ManualWeatherModel(f);
        var old=PresPlanRead<object>(f,"_displayContainer");PresPlanRead<Window>(f,"_displayWindow").Close();await Settle(100);
        Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
        Assert.Null(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill);Assert.True(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").IsVisible);
        Capture(f.Window,"closed-preview-420");Assert.DoesNotContain("LIVE",Named<TextBlock>(f.Window,"VideoTabPreviewBadge").Text,StringComparison.OrdinalIgnoreCase);
        Assert.True(f.Window.OpenCommandBar());Assert.True(f.Window.ExecuteCommand("enable-output"));await Settle(150);Assert.True(PresWindowOpen(f));
        var current=PresPlanRead<object>(f,"_displayContainer");Assert.NotSame(old,current);
        Assert.Same(current,Assert.IsType<VisualBrush>(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill).Visual);
        Assert.False(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").IsVisible);Assert.Equal(model,ManualWeatherModel(f));
        var owner=PresPlanRead<Window>(f,"_displayWindow");owner.Hide();await Settle(50);
        Assert.Null(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill);Assert.Equal("OUTPUT NOT VISIBLE",Named<TextBlock>(f.Window,"VideoTabPreviewBadge").Text);
        owner.Show();await Settle(50);Assert.Same(current,Assert.IsType<VisualBrush>(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill).Visual);
        Assert.Equal(model,ManualWeatherModel(f));
    }
#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip="Requires existing LibVLC source cue fixture.")]
#endif
    [Trait("Evidence","MediaPreviewTruthfulness")]
    public async Task PortableMediaPreviewKeepsOneCurrentOutputBrushAndDoesNotStartPlayback()
    {
        await using var f=await AflFixture.Create(1280,850);string? saved=DisplaySettings.LastVideoPath;
        try
        {
            f.Window.NativeVideoMirrorSupported=true;var browse=await MediaBrowseActor(f);
            f.Window.MediaPickerSelectionOverride=(picker,_)=>{picker.FileName=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","test-pattern.mp4");return true;};
            await Tap(f.Window,browse);var player=PresPlanRead<VideoPlayerControl>(f,"_videoPlayer");
            var show=Named<Button>(f.Window,"VideoTabShowOnScoreboardButton");await MediaBrowseReveal(f,show);await Tap(f.Window,show);
            Assert.Same(player,PresPlanRead<VideoPlayerControl>(f,"_videoPlayer"));Assert.False(player.IsPlaying);
            Assert.False(Named<Control>(f.Window,"VideoTabPreviewPlaceholder").IsVisible);
            Assert.Equal("MEDIA OUTPUT PREVIEW",Named<TextBlock>(f.Window,"VideoTabPreviewBadge").Text);
            Assert.Same(PresPlanRead<object>(f,"_displayContainer"),Assert.IsType<VisualBrush>(Named<Rectangle>(f.Window,"VideoTabLiveMirror").Fill).Visual);
            // This proves binding/cued state only, not decoded-video fidelity.
        }
        finally{DisplaySettings.LastVideoPath=saved;}
    }
}
