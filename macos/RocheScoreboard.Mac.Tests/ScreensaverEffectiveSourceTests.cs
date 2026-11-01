using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task<GlobalSettingsScreen> OpenSaverSourceSettings(AflFixture f)
    {
        Assert.True(f.Window.OpenCommandBar());Assert.True(f.Window.ExecuteCommand("settings"));await Settle(200);
        var settings=Named<GlobalSettingsScreen>(f.Window,"GlobalSettingsPanel");Assert.True(settings.IsEffectivelyVisible);
        var workspace=Named<SettingsWorkspace>(settings,"SettingsWorkspace");workspace.SelectScope("general");
        Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>()).Text="screensaver";await Settle(150);
        return settings;
    }
    private static async Task ChooseSaverSource(AflFixture f,GlobalSettingsScreen settings,string path)
    {
        var custom=Named<Button>(settings,"CustomScreensaverButton");Assert.True(custom.IsEffectivelyVisible&&custom.IsEffectivelyEnabled);
        int choices=0;
        f.Window.MediaPickerSelectionOverride=(picker,owner)=>{++choices;Assert.Same(f.Window,owner);Assert.True(picker.CheckFileExists);Assert.False(picker.Multiselect);picker.FileName=path;return true;};
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(custom)).Invoke();await Settle(150);Assert.Equal(1,choices);
    }
    private static async Task LeaveSaverSettings(AflFixture f,GlobalSettingsScreen settings)
    {
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(settings,"BackButton"))).Invoke();await Settle(100);
        Assert.True(Named<Control>(f.Window,"MainContent").IsEffectivelyVisible);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","ScreensaverEffectiveSource")]
    public async Task ExistingUnreadableScreensaverFileCannotBeReportedActiveWhenActualRendererUsesBuiltIn(int width,int height)
    {
        string path=Path.Combine(Path.GetTempPath(),"roche-saver-unreadable-"+Guid.NewGuid().ToString("N")+".png");await File.WriteAllBytesAsync(path,new byte[256]);
        try
        {
            await using var f=await AflFixture.Create(width,height);string model=ManualWeatherModel(f);bool output=PresWindowOpen(f);
            var settings=await OpenSaverSourceSettings(f);await ChooseSaverSource(f,settings,path);
            var saver=PresPlanRead<OutputScreensaverControl>(f,"_outputScreensaver");Assert.True(Named<Control>(saver,"DefaultScene").IsVisible);Assert.False(Named<Control>(saver,"CustomImage").IsVisible);
            Assert.Equal(path,PresPlanRead<string?>(f,"_screensaverMediaPath"));Assert.True(File.Exists(path));
            var status=Named<TextBlock>(settings,"GlobalScreensaverStatus");status.BringIntoView();await Settle(100);Capture(f.Window,"screensaver-unreadable-"+width);
            Assert.DoesNotContain("ACTIVE",Named<TextBlock>(settings,"CustomScreensaverSelected").Text,StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unavailable",status.Text,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(model,ManualWeatherModel(f));Assert.Equal(output,PresWindowOpen(f));
        }
        finally{File.Delete(path);}
    }

    [AvaloniaFact]
    [Trait("Evidence","ScreensaverEffectiveSource")]
    public async Task OffAirSelectionStaysOffThenReopenedOwnerIgnoresAnOldRenderersFailureReceipt()
    {
        using var image=new MediaBrowseImage();await using var f=await AflFixture.Create(420,320);
        var oldSaver=PresPlanRead<OutputScreensaverControl>(f,"_outputScreensaver");
        PresPlanRead<Window>(f,"_displayWindow").Close();await Settle(100);Assert.False(PresWindowOpen(f));
        string model=ManualWeatherModel(f);
        var settings=await OpenSaverSourceSettings(f);await ChooseSaverSource(f,settings,image.Path);
        Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
        Assert.Null(PresPlanRead<OutputScreensaverControl?>(f,"_outputScreensaver"));
        Assert.Contains("SELECTED",Named<TextBlock>(settings,"CustomScreensaverSelected").Text);Assert.DoesNotContain("ACTIVE",Named<TextBlock>(settings,"CustomScreensaverSelected").Text);
        await LeaveSaverSettings(f,settings);Assert.True(f.Window.OpenCommandBar());Assert.True(f.Window.ExecuteCommand("enable-output"));await Settle(150);
        Assert.True(PresWindowOpen(f));var current=PresPlanRead<OutputScreensaverControl>(f,"_outputScreensaver");Assert.NotSame(oldSaver,current);
        Assert.Equal(OutputScreensaverMediaState.CustomImage,current.CurrentMediaState);Assert.NotNull(Named<Image>(current,"CustomImage").Source);
        settings=await OpenSaverSourceSettings(f);var status=Named<TextBlock>(settings,"GlobalScreensaverStatus");Assert.Contains("image loaded",status.Text);string prepared=status.Text!;
        // Public old-view Configure is a deterministic obsolete receipt, not
        // an invented private decoder callback. The path deliberately matches
        // the current preference; only the owner-instance guard may accept it.
        await File.WriteAllBytesAsync(image.Path,new byte[256]);oldSaver.Configure(image.Path);
        Assert.Equal(OutputScreensaverMediaState.CustomUnavailable,oldSaver.CurrentMediaState);
        Assert.Equal(prepared,status.Text);Assert.Equal(OutputScreensaverMediaState.CustomImage,current.CurrentMediaState);Assert.NotNull(Named<Image>(current,"CustomImage").Source);
        current.Configure(image.Path);Assert.Equal(OutputScreensaverMediaState.CustomUnavailable,current.CurrentMediaState);Assert.Contains("unavailable",status.Text);
        Assert.True(Named<Control>(current,"DefaultScene").IsVisible);Assert.Equal(model,ManualWeatherModel(f));
        status.BringIntoView();await Settle(100);Capture(f.Window,"screensaver-current-owner-fallback");
    }
#if ROCHE_NATIVE_MEDIA
    [AvaloniaFact]
#else
    [AvaloniaFact(Skip="Requires the existing native LibVLC backend for a real invalid-media error.")]
#endif
    [Trait("Evidence","ScreensaverEffectiveSource")]
    public async Task RealCurrentVideoFailureReturnsToBuiltInAndUpdatesThePreparedSourceStatus()
    {
        string path=Path.Combine(Path.GetTempPath(),"roche-saver-invalid-"+Guid.NewGuid().ToString("N")+".mp4");await File.WriteAllBytesAsync(path,new byte[256]);
        try
        {
            await using var f=await AflFixture.Create(1280,850);string model=ManualWeatherModel(f);
            var settings=await OpenSaverSourceSettings(f);await ChooseSaverSource(f,settings,path);
            var saver=PresPlanRead<OutputScreensaverControl>(f,"_outputScreensaver");
            Assert.Equal(OutputScreensaverMediaState.CustomMediaRequested,saver.CurrentMediaState);
            Assert.DoesNotContain("ACTIVE",Named<TextBlock>(settings,"CustomScreensaverSelected").Text);
            await LeaveSaverSettings(f,settings);await Mr4Navigate(f.Window,Avalonia.Input.PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardScreensaver");await PresPlanReveal(f,card);PresPlanProvider(card).Invoke();
            try
            {
                await Until(()=>saver.CurrentMediaState==OutputScreensaverMediaState.CustomUnavailable,"Actual current LibVLC error must publish fallback; no private failure/timer invocation.");
            }
            finally
            {
                var video=saver.GetVisualDescendants().OfType<VideoPlayerControl>().SingleOrDefault();
                object? ReadField(object? target,string name)=>target?.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
                string Safe(Func<object?> read){try{return read()?.ToString()??"null";}catch(Exception e){return e.GetType().Name;}}
                var surface=video?.GetVisualDescendants().OfType<RocheScoreboard.Mac.Native.NativeVideoSurface>().SingleOrDefault();
                DeltaOwnerRecord("screensaver-video-diagnostic",new {saver.CurrentMediaState,saver.ConfiguredMediaPath,saver.IsVisible,saver.IsEffectivelyVisible,
                    active=PresPlanRead<string>(f,"_activeScreenKey"),generation=ReadField(saver,"_configurationGeneration"),requested=ReadField(saver,"_requestedVideoGeneration"),
                    videoLoaded=video?.IsLoaded,videoVisible=video?.IsVisible,videoEffective=video?.IsEffectivelyVisible,video?.HasSource,video?.CurrentSourcePath,video?.IsPlaying,
                    disposed=ReadField(video,"_isDisposed"),suspended=ReadField(video,"_suspended"),sourceGeneration=ReadField(video,"_sourceGeneration"),
                    backendState=Safe(()=>surface?.MediaPlayer?.State),backendLength=Safe(()=>surface?.MediaPlayer?.Length),backendTime=Safe(()=>surface?.MediaPlayer?.Time),videoTracks=Safe(()=>surface?.MediaPlayer?.VideoTrackCount),observedTime=ReadField(video,"_lastObservedTimeMs"),frames=surface?.FramesPresented,
                    logs=VideoDiagnostics.RecentEntries().TakeLast(20).Select(e=>new{e.Category,e.Message,e.ExceptionText}).ToArray()});
            }
            Assert.True(Named<Control>(saver,"DefaultScene").IsVisible);Assert.False(Named<Control>(saver,"CustomVideoHost").IsVisible);
            settings=await OpenSaverSourceSettings(f);Assert.Contains("unavailable",Named<TextBlock>(settings,"GlobalScreensaverStatus").Text);
            Assert.DoesNotContain("ACTIVE",Named<TextBlock>(settings,"CustomScreensaverSelected").Text);Assert.Equal(model,ManualWeatherModel(f));
            Capture(f.Window,"screensaver-real-video-fallback");
        }
        finally{File.Delete(path);}
    }
}
