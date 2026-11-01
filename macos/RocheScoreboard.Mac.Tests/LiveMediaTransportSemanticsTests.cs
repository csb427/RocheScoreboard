using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task OpenOriginalMediaTransport(AflFixture f)
    {
        var tab=Named<TabItem>(f.Window,"MediaDeskTab"); Assert.True(tab.IsEffectivelyVisible);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(tab)).Select();
        await Until(()=>ReferenceEquals(tab,Named<TabControl>(f.Window,"MainTabControl").SelectedItem)
            &&Named<Slider>(f.Window,"VideoTabSeekSlider").Bounds.Width>0,"Original Media tab must attach its transport controls.");
        await Settle(180);
    }
    private static async Task RevealOriginalMediaTarget(AflFixture f,Control target)
    {
        Assert.True(target.IsEffectivelyVisible); Assert.True(target.Focus(NavigationMethod.Tab)); target.BringIntoView(); await Settle(80);
        var viewport=Named<ScrollViewer>(f.Window,"AflMediaPageViewport");
        bool Ready()
        {
            using var frame=f.Window.CaptureRenderedFrame(); var box=FinalsPainted(f.Window,target);
            var input=MediaTransportInputBox(f,target);
            return new Rect(f.Window.ClientSize).Intersect(FinalsPainted(f.Window,viewport)).Contains(box)
                &&box.Contains(input)&&input.Height>=24
                &&MediaTransportProbes(input).All(p=>Within(target,f.Window.InputHitTest(p)));
        }
        for(int i=0;i<45&&!Ready();i++)
        {
            var visible=FinalsPainted(f.Window,viewport).Intersect(new Rect(f.Window.ClientSize));
            var box=FinalsPainted(f.Window,target); var input=MediaTransportInputBox(f,target);
            bool top=Within(target,f.Window.InputHitTest(MediaTransportProbes(input)[3])),bottom=Within(target,f.Window.InputHitTest(MediaTransportProbes(input)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<visible.Center.Y?1:-1;
            f.Window.MouseWheel(new Point(visible.Left+3,visible.Center.Y),new Vector(0,direction),RawInputModifiers.None); await Settle(60);
        }
        if(!Ready())
        {
            Capture(f.Window,$"media-transport-unreachable-{target.Name}-{f.Window.Width}");
            var box=FinalsPainted(f.Window,target);
            DeltaOwnerRecord($"media-transport-unreachable-{target.Name}-{f.Window.Width}",new { box,
                viewport=FinalsPainted(f.Window,viewport), client=f.Window.ClientSize,
                probes=MediaTransportProbes(box).Select(p=>new { p,owned=Within(target,f.Window.InputHitTest(p)),
                    hit=f.Window.InputHitTest(p)?.GetType().Name }) });
        }
        Assert.True(Ready(),"Original media target must fit the visible scroller and own all five pointer probes.");
    }
    // The shared slider template deliberately has a five-pixel horizontal
    // decorative inset. Probe inside its actual track, preserving full control
    // containment and both endpoint/vertical ownership checks.
    private static Point[] MediaTransportProbes(Rect box) =>
        [box.Center,new(box.Left+8,box.Center.Y),new(box.Right-8,box.Center.Y),new(box.Center.X,box.Top+4),new(box.Center.X,box.Bottom-4)];
    private static Rect MediaTransportInputBox(AflFixture f,Control target)
    {
        // The native ComboBox's layout bounds include vertical decorative
        // padding. geometry1 proves its top/bottom are backing-card pixels,
        // while the actual glass field is fully visible and owns its input.
        // Keep full layout containment above and measure the painted field;
        // do not claim a40px native clickable surface from42px layout bounds.
        return target is ComboBox
            ? FinalsPainted(f.Window,Assert.Single(target.GetVisualDescendants().OfType<OperatorGlassSurface>()))
            : FinalsPainted(f.Window,target);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalMediaTransportTargetsAreNamedAndReachableWithoutChangingPlayback(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height); await OpenOriginalMediaTransport(f);
        var seek=Named<Slider>(f.Window,"VideoTabSeekSlider"); var volume=Named<Slider>(f.Window,"VideoTabVolumeSlider");
        var speed=Named<ComboBox>(f.Window,"VideoTabSpeedCombo"); var mute=Named<Button>(f.Window,"VideoTabMuteButton");
        string model=ManualWeatherModel(f); bool output=PresWindowOpen(f);
        var player=Mr6Read<VideoPlayerControl>(f,"_videoPlayer"); double level=player.Volume,rate=player.SpeedRatio; bool muted=player.IsMuted;
        Assert.False(muted); Assert.Equal(1,level);
        Control[] targets=[seek,speed,mute,volume]; string[] names=["Video playback position","Video playback speed","Mute scoreboard audio · volume 100%","Scoreboard video volume"];
        for(int i=0;i<targets.Length;i++)
        {
            var target=targets[i]; await RevealOriginalMediaTarget(f,target); Capture(f.Window,$"media-transport-{i}-{width}");
            var box=FinalsPainted(f.Window,target); Assert.True(box.Height>=39.5&&box.Width>=39.5,"Media controls must reserve40px layout height.");
            DeltaOwnerRecord($"media-transport-geometry-{i}-{width}",new { layout=box,input=MediaTransportInputBox(f,target) });
            Assert.Equal(names[i],ControlAutomationPeer.CreatePeerForElement(target)?.GetName());
            Assert.Equal(level,player.Volume); Assert.Equal(rate,player.SpeedRatio); Assert.Equal(muted,player.IsMuted);
            Assert.Equal(model,ManualWeatherModel(f)); Assert.Equal(output,PresWindowOpen(f)); Assert.False(player.HasSource);
        }
        Assert.Equal(0,seek.Minimum); Assert.Equal(1,volume.Maximum); Assert.Equal(0,volume.Minimum);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalMediaVolumeNativeRangeAndKeysPreserveExistingQuantization(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height); await OpenOriginalMediaTransport(f);
        var volume=Named<Slider>(f.Window,"VideoTabVolumeSlider"); await RevealOriginalMediaTarget(f,volume);
        var range=Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(volume));
        var player=Mr6Read<VideoPlayerControl>(f,"_videoPlayer"); string model=ManualWeatherModel(f); bool output=PresWindowOpen(f);
        range.SetValue(.25); Assert.Equal(.25,volume.Value,4); Assert.Equal(.25,player.Volume,4);
        range.SetValue(.255); Assert.Equal(.255,volume.Value,4); Assert.Equal(.25,player.Volume,4);
        range.SetValue(.5); Assert.Equal(.5,player.Volume,4);
        Key(f.Window,PhysicalKey.End); Assert.Equal(1,volume.Value); Assert.Equal(1,player.Volume);
        Assert.Equal(model,ManualWeatherModel(f)); Assert.Equal(output,PresWindowOpen(f)); Assert.False(player.HasSource);
    }
}
