using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class CricketOverlayShellLifetimeTests
{
    private static T Read<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static CricketMatchManager Match(){var m=CricketReliabilityChecks.CreateMatch(true);m.RecordDelivery(4,CricketDeliveryType.Four);return m;}
    private static void Capture(Window window,CricketOverlayHost host,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,name+".png"));
        File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new{host.Status,host.IsShowing,host.OutputActive,
            shellVisible=Read<Border>(host,"_shell").IsVisible,shellY=Read<TranslateTransform>(host,"_shellMotion").Y,
            shellClock=Motion.IsAnimating(Read<TranslateTransform>(host,"_shellMotion")),handoff=Read<DispatcherTimer>(host,"_handoff").IsEnabled,
            active=Read<CricketOverlayFact?>(host,"_active")?.Kind,pending=Read<CricketOverlayKind?>(host,"_pending")},new JsonSerializerOptions{WriteIndented=true}));
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","CricketOverlayShellLifetime")]
    public async Task LiveBroadcastMotionOffSettlesShellWithoutLosingCurrentOverlayOrPendingChoice(bool exiting)
    {
        var match=Match();string state=JsonSerializer.Serialize(match.ToState());int changes=0;match.MatchChanged+=()=>changes++;
        using var host=new CricketOverlayHost(match){AutomaticEnabled=false};var window=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};
        window.Show();
        try
        {
            await Settle();Assert.True(host.Request(CricketOverlayKind.Partnership));
            if(exiting){await Settle(650);Assert.True(host.Request(CricketOverlayKind.Extras));}
            var move=Read<TranslateTransform>(host,"_shellMotion");Assert.True(Motion.IsAnimating(move),"Actual entry/exit clock must be active before changing broadcast motion.");
            host.MotionEnabled=false;
            Assert.False(Motion.IsAnimating(move));
            Assert.Equal(exiting?-Read<Border>(host,"_shell").Height:0,move.Y,3);
            Assert.Equal(!exiting,Read<Border>(host,"_shell").IsVisible);
            await Settle(650);Capture(window,host,"motion-off-"+exiting);
            Assert.True(host.IsShowing);Assert.Equal(exiting?CricketOverlayKind.Extras:CricketOverlayKind.Partnership,Read<CricketOverlayFact>(host,"_active").Kind);
            Assert.False(Motion.IsAnimating(move));Assert.Equal(0,move.Y,3);Assert.True(Read<Border>(host,"_shell").IsVisible);
            Assert.Equal(state,JsonSerializer.Serialize(match.ToState()));Assert.Equal(0,changes);
            host.Cancel();await Settle(350);Assert.False(host.IsShowing);Assert.False(Read<Border>(host,"_shell").IsVisible);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","CricketOverlayShellLifetime")]
    public async Task RemovingOrHidingOverlayHostDiscardsItsOutgoingAndQueuedWork(bool detach)
    {
        var match=Match();string state=JsonSerializer.Serialize(match.ToState());int changes=0;match.MatchChanged+=()=>changes++;
        using var host=new CricketOverlayHost(match){AutomaticEnabled=false};var window=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};window.Show();
        try
        {
            await Settle();Assert.True(host.Request(CricketOverlayKind.Partnership));await Settle(650);
            Assert.True(host.Request(CricketOverlayKind.Extras));Assert.True(Motion.IsAnimating(Read<TranslateTransform>(host,"_shellMotion")));
            if(detach)window.Content=null;else host.IsVisible=false;
            Assert.False(host.IsShowing);Assert.False(Motion.IsAnimating(Read<TranslateTransform>(host,"_shellMotion")));
            Assert.False(Read<DispatcherTimer>(host,"_handoff").IsEnabled);Assert.Null(Read<CricketOverlayKind?>(host,"_pending"));
            await Settle(650);Capture(window,host,"suspended-"+detach);Assert.False(host.IsShowing);
            if(detach)window.Content=host;else host.IsVisible=true;
            await Settle(150);Assert.False(host.IsShowing);Assert.False(Read<Border>(host,"_shell").IsVisible);
            Assert.True(host.Request(CricketOverlayKind.Extras));await Settle(650);Assert.Equal(CricketOverlayKind.Extras,Read<CricketOverlayFact>(host,"_active").Kind);
            Capture(window,host,"fresh-"+detach);Assert.Equal(state,JsonSerializer.Serialize(match.ToState()));Assert.Equal(0,changes);
        }
        finally{window.Close();}
    }
}
