// OUTSIDE BASELINE DRAFT — UNCOMPILED / UNRUN. Root owns integration/execution.
// Four cases. New contract: accepted operator numbers remain stationary in both
// motion policies. This does not claim the old enabled 4% pulse was a rules bug.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record NumericPulseFrame(string Stage,string Source,double Milliseconds,
        string? Header,string? Clock,string? HomeCarrier,string? AwayCarrier,
        Matrix HeaderTransform,Matrix HomeTransform,Matrix AwayTransform,
        Rect HeaderPaintedBounds,int HomeTotal,int AwayTotal,int Events,
        TimeSpan Elapsed,bool ClockRunning);

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","AflNumericReadoutStability")]
    public async Task RealPointerAndRapidKeyboardScoresUpdateImmediatelyWithoutScalingOperatorNumbers(
        int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;
        var trace=new List<NumericPulseFrame>();
        string stage="ready";
        var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduced);
            OperatorMotion.AnimationOverride=null;
            Assert.Equal(!reduced,OperatorMotion.Enabled);
            Key(f.Window,PhysicalKey.Digit1,RawInputModifiers.Control);
            await Settle(650); // Existing tab reveal and450ms navigation guard.
            Assert.True(Named<TabItem>(f.Window,"MatchControlTab").IsSelected);
            var header=Named<TextBlock>(f.Window,"HeaderScore");
            var clock=Named<TextBlock>(f.Window,"HeaderQuarterClock");
            var home=Named<TextBlock>(f.Window,"HomeScoreDisplay");
            var away=Named<TextBlock>(f.Window,"AwayScoreDisplay");
            Assert.True(header.IsEffectivelyVisible);Assert.True(clock.IsEffectivelyVisible);
            Assert.False(home.IsVisible);Assert.False(away.IsVisible);
            Assert.False(home.Focusable);Assert.False(away.Focusable);
            Assert.Equal("6 – 1",header.Text);Assert.Equal("Q1  00:00",clock.Text);
            Assert.Equal("1.0.6",home.Text);Assert.Equal("0.1.1",away.Text);
            Assert.False(f.Match.ClockRunning);Assert.Equal(2,f.Match.Events.Count);
            var elapsed=f.Match.ElapsedInQuarter;
            var duration=f.Match.QuarterDuration;
            int accepted=0;f.Match.ScoreEventAdded+=_=>accepted++;
            Matrix Transform(Control target)=>target.RenderTransform?.Value??Matrix.Identity;
            NumericPulseFrame Read(string source)=>new(stage,source,watch.Elapsed.TotalMilliseconds,
                header.Text,clock.Text,home.Text,away.Text,Transform(header),Transform(home),Transform(away),
                FinalsPainted(f.Window,header),f.Match.HomeTotal,f.Match.AwayTotal,f.Match.Events.Count,
                f.Match.ElapsedInQuarter,f.Match.ClockRunning);
            void Authority(int goals,int behinds,int awayBehinds,int expectedEvents)
            {
                // Check after the real input API returns, before any await.
                Assert.Equal(goals,f.Match.HomeGoals);Assert.Equal(behinds,f.Match.HomeBehinds);
                Assert.Equal(0,f.Match.AwayGoals);Assert.Equal(awayBehinds,f.Match.AwayBehinds);
                Assert.Equal(goals*6+behinds,f.Match.HomeTotal);Assert.Equal(awayBehinds,f.Match.AwayTotal);
                Assert.Equal(expectedEvents,f.Match.Events.Count);Assert.Equal(expectedEvents-2,accepted);
                Assert.Equal($"{goals*6+behinds} – {awayBehinds}",header.Text);
                Assert.Equal($"{goals}.{behinds}.{goals*6+behinds}",home.Text);
                Assert.Equal($"0.{awayBehinds}.{awayBehinds}",away.Text);
                Assert.Equal(1,f.Match.Quarter);Assert.Equal(elapsed,f.Match.ElapsedInQuarter);
                Assert.Equal(duration,f.Match.QuarterDuration);Assert.False(f.Match.ClockRunning);
                Assert.Equal("Q1  00:00",clock.Text);
                Assert.False(home.IsVisible);Assert.False(away.IsVisible);
                trace.Add(Read("post-input-synchronous-authority"));
            }
            observer=(_,_)=>trace.Add(Read("NativeRendering"));
            NativeRendering.Rendering+=observer;

            var button=Named<Button>(f.Window,"HomeGoalButton");
            Assert.True(button.IsEffectivelyVisible);Assert.True(button.IsEffectivelyEnabled);
            // Public reveal plus bounded physical wheel routing only. A failure
            // here is route/readiness evidence, not a pulse-contract failure.
            button.BringIntoView();await Settle(150);
            Point Center()=>button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),f.Window)!.Value;
            bool Ready()=>button.Bounds.Width>10&&button.Bounds.Height>10
                &&new Rect(f.Window.ClientSize).Contains(Center())&&Within(button,f.Window.InputHitTest(Center()));
            for(int step=0;step<35&&!Ready();step++)
            {
                var viewport=button.GetVisualAncestors().OfType<ScrollViewer>()
                    .FirstOrDefault(x=>x.IsEffectivelyVisible&&x.Bounds.Width>20&&x.Bounds.Height>20);
                Assert.NotNull(viewport);
                var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),f.Window);
                Assert.NotNull(wheel);Assert.True(Within(viewport,f.Window.InputHitTest(wheel.Value)));
                double direction=Center().Y<wheel.Value.Y?1:-1;
                f.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);
                await Settle(65);
            }
            Assert.True(Ready(),"Actual home goal input must be physically reachable before observing numeric feedback.");
            Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,header)));
            Authority(1,0,1,2);
            stage="pointer-home-goal";
            f.Window.MouseMove(Center());f.Window.MouseDown(Center(),MouseButton.Left);f.Window.MouseUp(Center(),MouseButton.Left);
            Authority(2,0,1,3);
            // Observe through the production rendering dispatcher. Never call
            // private PulseElement or a synthetic sample/tick callback.
            async Task ObserveFrames(int minimum)
            {
                int first=trace.Count(x=>x.Source=="NativeRendering");
                for(int attempt=0;attempt<150&&trace.Count(x=>x.Source=="NativeRendering")-first<minimum;attempt++)await Settle(12);
                Assert.True(trace.Count(x=>x.Source=="NativeRendering")-first>=minimum,"Missing actual rendering callbacks.");
            }
            await ObserveFrames(18);Authority(2,0,1,3);
            stage="keyboard-away-behind";
            Key(f.Window,PhysicalKey.L);Authority(2,0,2,4);
            stage="rapid-keyboard-home-goal-and-behind";
            Key(f.Window,PhysicalKey.A);Authority(3,0,2,5);
            Key(f.Window,PhysicalKey.S);Authority(3,1,2,6);
            await ObserveFrames(45);Authority(3,1,2,6);
            Assert.All(trace,frame=>
            {
                Assert.Equal(Matrix.Identity,frame.HeaderTransform);
                Assert.Equal(Matrix.Identity,frame.HomeTransform);
                Assert.Equal(Matrix.Identity,frame.AwayTransform);
                Assert.Equal("Q1  00:00",frame.Clock);Assert.Equal(elapsed,frame.Elapsed);Assert.False(frame.ClockRunning);
            });
            // The other two legacy numeric pulses came from real clock-state
            // transitions. Exercise public navigation and native radio/start
            // controls; time may advance, score/history must not change.
            stage="clock-mode-navigation";
            string nonClock=Mr4NonClockState(f.Match);
            await Mr4Navigate(f.Window,PhysicalKey.Digit4);
            await OpenClockSection(f,reduced);
            var down=Named<RadioButton>(f.Window,"ClockModeCountdown");
            Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down)).Select();
            Assert.True(down.IsChecked);
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            stage="public-clock-start";
            await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
            Assert.True(f.Match.ClockRunning);
            await Until(()=>f.Match.ElapsedInQuarter.TotalMilliseconds>=250,"Real Start must advance the stopwatch before checking clock-transition numeric feedback.");
            await ObserveFrames(20);
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(4,accepted);
            stage="public-clock-pause";
            await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
            Assert.False(f.Match.ClockRunning);var paused=f.Match.ElapsedInQuarter;
            await ObserveFrames(22);
            Assert.Equal(paused,f.Match.ElapsedInQuarter);
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(4,accepted);
            Assert.All(trace,frame=>{Assert.Equal(Matrix.Identity,frame.HeaderTransform);Assert.Equal(Matrix.Identity,frame.HomeTransform);Assert.Equal(Matrix.Identity,frame.AwayTransform);});
            Capture(f.Window,$"afl-numeric-stationary-{width}-reduce-{reduced}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,$"afl-numeric-stationary-{width}-reduce-{reduced}.json"),
                    JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
            }
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
