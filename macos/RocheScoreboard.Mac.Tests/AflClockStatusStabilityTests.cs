// Regression contract for the original production status badge and real clock input.
// Four cases: three current motion policies, plus live policy change and Hide/Show.
// Actual visible1280 header; at420 production intentionally hides StatusBadge.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record AflStatusFrame(string Stage,string Source,double Milliseconds,
        string? Text,Matrix BadgeTransform,double BadgeOpacity,Matrix TextTransform,double TextOpacity,
        Matrix DotTransform,double DotOpacity,bool BadgeVisible,bool ClockRunning,TimeSpan Elapsed,
        int HomeTotal,int AwayTotal,int Events);

    private static void AflStatusWrite(string name,object trace)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void AflStatusSteady(AflStatusFrame frame)
    {
        Assert.Equal(Matrix.Identity,frame.BadgeTransform);
        Assert.Equal(1d,frame.BadgeOpacity);
        Assert.Equal(Matrix.Identity,frame.TextTransform);
        Assert.Equal(1d,frame.TextOpacity);
        // Dot is recorded independently. This contract does not mandate either
        // static or finite dot feedback, nor permit fading/scaling the label.
    }
    private static void AflStatusPress(Window owner,Button button)
    {
        // The real route has already revealed the control. No reflection/fake
        // click, and no await between input return and synchronous assertions.
        Assert.True(button.IsEffectivelyVisible);Assert.True(button.IsEffectivelyEnabled);
        var p=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),owner);
        Assert.NotNull(p);Assert.True(new Rect(owner.ClientSize).Contains(p.Value));
        Assert.True(Within(button,owner.InputHitTest(p.Value)),"Actual Start/Pause centre must hit its own template.");
        owner.MouseMove(p.Value);owner.MouseDown(p.Value,MouseButton.Left);owner.MouseUp(p.Value,MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData("Expressive")][InlineData("Calm")][InlineData("Reduce")]
    [Trait("Evidence","AflSteadyClockStatus")]
    public async Task RealClockChangesKeepStatusWordsOpaqueAndStationaryWithoutDelayingClock(string policy)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var trace=new List<AflStatusFrame>();string stage="ready";var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            await OpenClockSection(f,policy=="Reduce");
            var down=Named<RadioButton>(f.Window,"ClockModeCountdown");
            Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down)).Select();
            Assert.True(down.IsChecked);Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
            Assert.Null(OperatorMotion.AnimationOverride);Assert.Equal(policy!="Reduce",OperatorMotion.Enabled);
            string nonClock=Mr4NonClockState(f.Match);var duration=f.Match.QuarterDuration;
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            var badge=Named<Border>(f.Window,"StatusBadge");var text=Named<TextBlock>(f.Window,"StatusText");
            var dot=Named<Control>(f.Window,"StatusDot");var start=Named<Button>(f.Window,"StartPauseButton");
            Assert.True(badge.IsEffectivelyVisible);Assert.True(text.IsEffectivelyVisible);Assert.Equal("PAUSED",text.Text);
            Matrix Transform(Control c)=>c.RenderTransform?.Value??Matrix.Identity;
            AflStatusFrame Read(string source)=>new(stage,source,watch.Elapsed.TotalMilliseconds,text.Text,
                Transform(badge),badge.Opacity,Transform(text),text.Opacity,Transform(dot),dot.Opacity,
                badge.IsEffectivelyVisible,f.Match.ClockRunning,f.Match.ElapsedInQuarter,
                f.Match.HomeTotal,f.Match.AwayTotal,f.Match.Events.Count);
            observer=(_,_)=>trace.Add(Read("NativeRendering"));NativeRendering.Rendering+=observer;
            async Task Observe(int count)
            {
                int before=trace.Count(x=>x.Source=="NativeRendering");
                for(int attempt=0;attempt<160&&trace.Count(x=>x.Source=="NativeRendering")-before<count;attempt++)await Settle(12);
                Assert.True(trace.Count(x=>x.Source=="NativeRendering")-before>=count,"Must observe actual production rendering callbacks.");
            }
            void State(bool running)
            {
                Assert.Equal(running,f.Match.ClockRunning);Assert.Equal(running?"LIVE":"PAUSED",text.Text);
                Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(duration,f.Match.QuarterDuration);
                trace.Add(Read("post-input-synchronous-authority"));
            }
            stage="start";AflStatusPress(f.Window,start);State(true);
            await Until(()=>f.Match.ElapsedInQuarter.TotalMilliseconds>=150,"Physical Start must advance the real stopwatch.");
            await Observe(24);State(true);
            stage="pause";AflStatusPress(f.Window,start);State(false);var paused=f.Match.ElapsedInQuarter;
            await Observe(24);Assert.Equal(paused,f.Match.ElapsedInQuarter);State(false);
            // Genuine rapid reversals: each accepted clock state is observed
            // before another input. No blanket debounce is allowed here.
            stage="rapid-start";AflStatusPress(f.Window,start);State(true);
            stage="rapid-pause";AflStatusPress(f.Window,start);State(false);
            stage="rapid-start-again";AflStatusPress(f.Window,start);State(true);
            await Observe(24);State(true);
            stage="final-pause";AflStatusPress(f.Window,start);State(false);paused=f.Match.ElapsedInQuarter;
            await Observe(24);Assert.Equal(paused,f.Match.ElapsedInQuarter);State(false);
            Capture(f.Window,"afl-status-steady-"+policy);
            Assert.All(trace,AflStatusSteady);
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            AflStatusWrite("afl-status-steady-"+policy,trace);
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }

    [AvaloniaFact][Trait("Evidence","AflSteadyClockStatus")]
    public async Task LiveReduceAndWindowHideShowKeepCurrentStatusReadableAndDoNotReplayOldFeedback()
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var trace=new List<AflStatusFrame>();string stage="ready";var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);await OpenClockSection(f,false);
            var down=Named<RadioButton>(f.Window,"ClockModeCountdown");
            Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down)).Select();
            Assert.True(down.IsChecked);Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            var badge=Named<Border>(f.Window,"StatusBadge");var text=Named<TextBlock>(f.Window,"StatusText");
            var dot=Named<Control>(f.Window,"StatusDot");var start=Named<Button>(f.Window,"StartPauseButton");
            Assert.True(badge.IsEffectivelyVisible);Assert.True(text.IsEffectivelyVisible);
            string nonClock=Mr4NonClockState(f.Match);var duration=f.Match.QuarterDuration;
            Matrix Transform(Control c)=>c.RenderTransform?.Value??Matrix.Identity;
            AflStatusFrame Read(string source)=>new(stage,source,watch.Elapsed.TotalMilliseconds,text.Text,
                Transform(badge),badge.Opacity,Transform(text),text.Opacity,Transform(dot),dot.Opacity,
                badge.IsEffectivelyVisible,f.Match.ClockRunning,f.Match.ElapsedInQuarter,
                f.Match.HomeTotal,f.Match.AwayTotal,f.Match.Events.Count);
            observer=(_,_)=>trace.Add(Read("NativeRendering"));NativeRendering.Rendering+=observer;
            async Task Observe(int count)
            {
                int before=trace.Count(x=>x.Source=="NativeRendering");
                for(int attempt=0;attempt<160&&trace.Count(x=>x.Source=="NativeRendering")-before<count;attempt++)await Settle(12);
                Assert.True(trace.Count(x=>x.Source=="NativeRendering")-before>=count);
            }
            stage="enabled-start";AflStatusPress(f.Window,start);
            Assert.True(f.Match.ClockRunning);Assert.Equal("LIVE",text.Text);
            trace.Add(Read("post-input-before-live-reduce"));
            // This is a controlled existing preference API change, not a claim
            // of physically operating the preferences screen. First snapshot
            // records whether the OLD implementation had active body feedback.
            stage="live-reduce";OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(OperatorMotion.Enabled);trace.Add(Read("post-policy-synchronous"));
            await Observe(24);Assert.True(f.Match.ClockRunning);Assert.Equal("LIVE",text.Text);
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(duration,f.Match.QuarterDuration);
            stage="pause-reduced";AflStatusPress(f.Window,start);Assert.False(f.Match.ClockRunning);Assert.Equal("PAUSED",text.Text);
            var paused=f.Match.ElapsedInQuarter;trace.Add(Read("post-input-synchronous-authority"));
            stage="hide";f.Window.Hide();Assert.False(f.Window.IsVisible);await Settle(40);
            stage="reshow";f.Window.Show();await Until(()=>badge.IsEffectivelyVisible,"Real existing window must restore its current visible status.");
            Assert.Equal("PAUSED",text.Text);await Observe(35);
            Assert.False(f.Match.ClockRunning);Assert.Equal(paused,f.Match.ElapsedInQuarter);
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(duration,f.Match.QuarterDuration);
            Capture(f.Window,"afl-status-live-reduce-reshow");
            // The initial enabled-start frame documents the original baseline;
            // every frame from Reduce onward must satisfy the steady contract.
            Assert.All(trace.Where(x=>x.Stage!="enabled-start"),AflStatusSteady);
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            AflStatusWrite("afl-status-live-reduce-reshow",trace);
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
