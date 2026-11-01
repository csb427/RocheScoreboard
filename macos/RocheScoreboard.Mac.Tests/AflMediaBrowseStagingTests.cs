using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static string MediaBrowseCue(AflFixture f)
    {
        var player=PresPlanRead<VideoPlayerControl?>(f,"_videoPlayer");
        return JsonSerializer.Serialize(new
        {
            path=PresPlanRead<string?>(f,"_customVideoPath"),image=PresPlanRead<bool>(f,"_customMediaIsImage"),
            file=Named<TextBlock>(f.Window,"VideoTabFilePathText").Text,meta=Named<TextBlock>(f.Window,"VideoTabFileMetaText").Text,status=Named<TextBlock>(f.Window,"VideoTabStatusText").Text,
            saved=DisplaySettings.LastVideoPath,source=player?.GetType().GetField("_sourcePath",HiddenInstance)?.GetValue(player),generation=player?.GetType().GetField("_sourceGeneration",HiddenInstance)?.GetValue(player),playing=player?.IsPlaying
        });
    }
    private static async Task MediaBrowseReveal(AflFixture f,Control target)
    {
        Key(f.Window,PhysicalKey.Tab);Assert.True(target.Focus(NavigationMethod.Tab));target.BringIntoView();await Settle(100);
        bool Ready(){using var frame=f.Window.CaptureRenderedFrame();var box=FinalsPainted(f.Window,target);return new Rect(f.Window.ClientSize).Contains(box)&&FinalsProbes(box).All(point=>Within(target,f.Window.InputHitTest(point)));}
        for(int i=0;i<35&&!Ready();i++)
        {
            var viewport=target.GetVisualAncestors().OfType<ScrollViewer>().First(x=>x.IsEffectivelyVisible&&x.Bounds.Height>0);var area=FinalsPainted(f.Window,viewport).Intersect(new Rect(f.Window.ClientSize));var box=FinalsPainted(f.Window,target);
            f.Window.MouseWheel(new Point(area.Left+3,area.Center.Y),new Vector(0,box.Center.Y<area.Center.Y?1:-1),RawInputModifiers.None);await Settle(60);
        }
        Assert.True(Ready(),"Media Browse must own all five physical probes in the current window.");
    }
    private static async Task<Button> MediaBrowseActor(AflFixture f)
    {
        await Mr4Navigate(f.Window,PhysicalKey.Digit3);Assert.Same(Named<TabItem>(f.Window,"MediaDeskTab"),Named<TabControl>(f.Window,"MainTabControl").SelectedItem);
        var browse=Assert.Single(Named<Border>(f.Window,"AflMediaControlsCard").GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"📁 Browse"));await MediaBrowseReveal(f,browse);return browse;
    }
    private static async Task<DispatcherTimer> MediaBrowsePendingBreak(AflFixture f)
    {
        await OpenBreakCardSection(f,true);var toggle=Named<CheckBox>(f.Window,"SettingsBreakScreenToggle");await RevealBreakCardActor(f,toggle);
        if(toggle.IsChecked!=true)Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).Toggle();
        await Mr4Navigate(f.Window,PhysicalKey.Digit1);var start=Named<Button>(f.Window,"StartPauseButton");await MediaBrowseReveal(f,start);await Tap(f.Window,start);Assert.True(f.Match.ClockRunning);
        var end=Named<Button>(f.Window,"EndQtrButton");await MediaBrowseReveal(f,end);await Tap(f.Window,end);Assert.False(f.Match.ClockRunning);Assert.Equal(2,f.Match.Quarter);
        var pending=Assert.IsType<DispatcherTimer>(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));Assert.True(pending.IsEnabled);Assert.Equal(TimeSpan.FromSeconds(45),pending.Interval);return pending;
    }
    private sealed class MediaBrowseImage : IDisposable
    {
        internal readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"roche-media-cue-"+Guid.NewGuid().ToString("N")+".png");
        internal MediaBrowseImage()
        {
            var directory=new DirectoryInfo(AppContext.BaseDirectory);string? source=null;
            while(directory is not null){string candidate=System.IO.Path.Combine(directory.FullName,"Roche_Scoreboard","Resources","ClubLogos","cricket-vsdca-box-hill-official.png");if(File.Exists(candidate)){source=candidate;break;}directory=directory.Parent;}
            Assert.NotNull(source);File.Copy(source,Path);
        }
        public void Dispose(){if(File.Exists(Path))File.Delete(Path);}
    }
    [AvaloniaTheory]
    [InlineData(false,1280,850)][InlineData(true,420,320)]
    [Trait("Evidence","AflMediaBrowseStaging")]
    public async Task CancellingMediaBrowsePreservesTheRealPendingBreakOwnerAndCue(bool dismissed,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);var pending=await MediaBrowsePendingBreak(f);var browse=await MediaBrowseActor(f);
        string model=JsonSerializer.Serialize(f.Match.ToState()),cue=MediaBrowseCue(f);var output=PresPlanRead<Window?>(f,"_displayWindow");bool visible=output?.IsVisible==true;string screen=PresPlanRead<string>(f,"_activeScreenKey");int choices=0;
        f.Window.MediaPickerSelectionOverride=(picker,owner)=>{++choices;Assert.Same(f.Window,owner);Assert.True(picker.CheckFileExists);Assert.False(picker.Multiselect);return dismissed?null:false;};
        await Tap(f.Window,browse);Assert.Equal(1,choices);Assert.Equal(model,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(cue,MediaBrowseCue(f));
        Assert.Same(pending,PresPlanRead<object?>(f,"_breakScreenDelayTimer"));Assert.True(pending.IsEnabled);Assert.Same(output,PresPlanRead<Window?>(f,"_displayWindow"));Assert.Equal(visible,output?.IsVisible==true);Assert.Equal(screen,PresPlanRead<string>(f,"_activeScreenKey"));
        Capture(f.Window,"media-browse-cancel-"+width);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","AflMediaBrowseStaging")]
    public async Task AcceptedMediaStillLoadsAndASecondCancelledBrowsePreservesThatCue(bool scoreDuringPicker)
    {
        using var image=new MediaBrowseImage();await using var f=await AflFixture.Create(1280,850);var browse=await MediaBrowseActor(f);string? previous=DisplaySettings.LastVideoPath;string model="";long lifetime=f.Match.LifecycleVersion;
        try
        {
            f.Window.MediaPickerSelectionOverride=(picker,owner)=>{Assert.Same(f.Window,owner);if(scoreDuringPicker)f.Match.AddGoal(TeamSide.Home);model=JsonSerializer.Serialize(f.Match.ToState());picker.FileName=image.Path;return true;};
            await Tap(f.Window,browse);Assert.Equal(lifetime,f.Match.LifecycleVersion);Assert.Equal(model,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(image.Path,PresPlanRead<string?>(f,"_customVideoPath"));Assert.True(PresPlanRead<bool>(f,"_customMediaIsImage"));Assert.Equal(image.Path,DisplaySettings.LastVideoPath);
            Assert.Contains("Image loaded",Named<TextBlock>(f.Window,"VideoTabStatusText").Text);string cue=MediaBrowseCue(f);
            f.Window.MediaPickerSelectionOverride=(_,_)=>false;await Tap(f.Window,browse);Assert.Equal(cue,MediaBrowseCue(f));Assert.Equal(model,JsonSerializer.Serialize(f.Match.ToState()));
        }
        finally{DisplaySettings.LastVideoPath=previous;}
    }
    [AvaloniaFact]
    [Trait("Evidence","AflMediaBrowseStaging")]
    public async Task AFileChosenForAnObsoleteMatchCannotReplaceTheCurrentCue()
    {
        using var image=new MediaBrowseImage();await using var f=await AflFixture.Create(1280,850);var browse=await MediaBrowseActor(f);string cue=MediaBrowseCue(f);string reset="";
        f.Window.MediaPickerSelectionOverride=(picker,_)=>{f.Match.ResetForNewGame();reset=JsonSerializer.Serialize(f.Match.ToState());picker.FileName=image.Path;return true;};
        await Tap(f.Window,browse);Assert.Equal(reset,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(cue,MediaBrowseCue(f));
    }
}
