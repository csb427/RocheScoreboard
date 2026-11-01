// OUTSIDE REVIEW DRAFT ONLY: UNCOMPILED / UNRUN. Root owns integration/build.
// Two cases: actual Close during independently witnessed reveal, Expressive/Calm.
// Uses existing partial fixture and Mr6 helpers; never invokes a private callback.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record Mr6CloseFrame(string Stage,string Source,double Milliseconds,
        double Opacity,double Y,double ScaleX,double ScaleY,bool WindowVisible,bool CardVisible,
        bool ClockRunning,TimeSpan Elapsed,int HomeTotal,int AwayTotal,int Events);

    [AvaloniaTheory][InlineData("Expressive")][InlineData("Calm")]
    [Trait("Evidence","MR6ActualCloseLifetime")]
    public async Task ActualCloseDuringWitnessedStoppageRevealSettlesNamedPartsAndCannotReplayOldMotion(string policy)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;
        bool previousReduce=OperatorAppearance.ReduceMotion;bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var trace=new List<Mr6CloseFrame>();
        string stage="ready";var watch=Stopwatch.StartNew();bool closed=false;
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            await OpenClockSection(f,false);Assert.Null(OperatorMotion.AnimationOverride);Assert.True(OperatorMotion.Enabled);
            // Actual native original mode provider establishes a genuinely new
            // CountUp entry. A same-value selection must not be called a reveal.
            Mr6Mode(f,false);await Settle(400);
            var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");
            var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
            Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);Assert.False(card.IsEffectivelyVisible);
            Assert.False(f.Match.ClockRunning);var elapsed=f.Match.ElapsedInQuarter;
            Mr6CloseFrame Read(string source)
            {
                var scale=Mr6Part<Avalonia.Media.ScaleTransform>(f,"SettingsControlPanelCountdownCardScale");
                return new(stage,source,watch.Elapsed.TotalMilliseconds,card.Opacity,
                    Mr6Part<Avalonia.Media.TranslateTransform>(f,"SettingsControlPanelCountdownCardTranslate").Y,
                    scale.ScaleX,scale.ScaleY,f.Window.IsVisible,card.IsEffectivelyVisible,
                    f.Match.ClockRunning,f.Match.ElapsedInQuarter,f.Match.HomeTotal,f.Match.AwayTotal,f.Match.Events.Count);
            }
            observer=(_,_)=>trace.Add(Read("NativeRendering"));NativeRendering.Rendering+=observer;
            stage="reveal";Mr6Mode(f,true);Assert.Equal(ClockMode.CountUp,f.Match.ClockMode);
            // Do not assume a named35ms sample is in-flight. Require an actual
            // production rendering callback with independently interior geometry.
            bool Partial(Mr6CloseFrame frame)=>frame.Stage=="reveal"&&frame.Source=="NativeRendering"
                &&frame.CardVisible&&frame.Opacity>0&&frame.Opacity<1&&frame.Y>0
                &&frame.ScaleX>0&&frame.ScaleX<1&&frame.ScaleY>0&&frame.ScaleY<1;
            for(int attempt=0;attempt<100&&!trace.Any(Partial);attempt++)await Settle(12);
            Assert.Contains(trace,Partial);
            var beforeClose=Read("before-close");trace.Add(beforeClose);
            Assert.True(beforeClose.Opacity>0&&beforeClose.Opacity<1&&beforeClose.Y>0,
                "Close must still occur during the actual reveal, not after an old interior frame has settled.");
            bool preference=OperatorWorkflowPreferences.Load().AflControlPanelCountdown;
            Assert.Equal(preference,toggle.IsChecked==true);Assert.Equal(preference,Mr6Read<bool>(f,"_showControlPanelCountdown"));
            // Full stopped MatchManager snapshot is stable; no normalised clock
            // fields are needed. No save-file/no-IO assertion is made: Closing
            // intentionally flushes preferences/layout/recovery (SelfTest mode
            // suppresses SaveGameState). Close must not alter this match model.
            string before=JsonSerializer.Serialize(f.Match.ToState());
            f.Window.Closed+=(_,_)=>closed=true;
            stage="close";f.Window.Close();
            Assert.True(closed,"Actual public Close must complete, not leave a confirmation/dialog pending.");
            Assert.False(f.Window.IsVisible);Mr6Canonical(f);trace.Add(Read("post-close-synchronous"));
            Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
            Assert.Equal(preference,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
            // Let real dispatcher work run beyond original entry/exit/fallback
            // lifetimes. No private timer.Tick, synthetic completion, Dispose or
            // stale private callback is invoked by this test.
            stage="closed-wait";await Settle(700);Mr6Canonical(f);trace.Add(Read("post-close-wait"));
            Assert.True(closed);Assert.False(f.Window.IsVisible);
            Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
            Assert.False(f.Match.ClockRunning);Assert.Equal(elapsed,f.Match.ElapsedInQuarter);
            Assert.Equal(preference,toggle.IsChecked==true);
            Assert.Equal(preference,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
            Assert.Equal(preference,Mr6Read<bool>(f,"_showControlPanelCountdown"));
            Assert.All(trace.Where(x=>x.Stage is "close" or "closed-wait"),frame=>
            {
                Assert.Equal(1d,frame.Opacity,3);Assert.Equal(0d,frame.Y,3);
                Assert.Equal(1d,frame.ScaleX,3);Assert.Equal(1d,frame.ScaleY,3);
                Assert.False(frame.WindowVisible);Assert.False(frame.ClockRunning);Assert.Equal(elapsed,frame.Elapsed);
            });
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"mr6-actual-close-"+policy+".json"),
                    JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
            }
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
