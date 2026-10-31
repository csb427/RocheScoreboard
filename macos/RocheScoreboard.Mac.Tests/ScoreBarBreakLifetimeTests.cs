// OUTSIDE DRAFT ONLY. UNCOMPILED/UNRUN. Root owns integration and baseline.
// Three cases: one positive cadence, two public-API interruption cases.
// Component routes: public Populate + actual attached/detached visual lifecycle.
// No private Tick/Transition call, timer shortening, SampleAt, preference Configure,
// file picker substitute, or claim of actual desktop compositor acceptance.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record BreakFadeObservation(string Stage,double Milliseconds,string? Text,
        double Opacity,bool Clock,bool Loaded,bool Attached,bool BreakVisible);
    private static MatchManager BreakFadeMatch(int endedQuarter)
    {
        var match=new MatchManager();
        for(int quarter=1;quarter<=endedQuarter;quarter++)
        {
            if(quarter>1)match.StartClock();
            Assert.True(match.EndQuarter());
        }
        Assert.False(match.ClockRunning);return match;
    }
    private static void BreakFadePopulate(PresentationScoreBar bar,MatchManager match,int endedQuarter)
        =>bar.Populate(match,endedQuarter,"#C9483B","#B68F12","#FFFFFF","#FFFFFF",null,null,showTimer:false);

    [AvaloniaTheory]
    [InlineData("unload")][InlineData("rebind")]
    [Trait("Evidence","ScoreBarNaturalBreakFadeOwnershipBaseline")]
    public async Task ActualNaturalBreakFadeCannotCompleteIntoRetiredOrReboundPublicBar(string interruption)
    {
        // Reuse the already-owned/rollback-safe storage fixture because the
        // quarter-label service can lazily create its storage directory on read.
        // No OperatorAppearance.Configure or preference value is changed here.
        await using var storage=await AflFixture.Create(1280,850);
        var oldMatch=BreakFadeMatch(1);var newMatch=BreakFadeMatch(2);
        string oldState=System.Text.Json.JsonSerializer.Serialize(oldMatch.ToState());
        string newState=System.Text.Json.JsonSerializer.Serialize(newMatch.ToState());
        var bar=new PresentationScoreBar{Height=64};
        var slot=new Grid();slot.Children.Add(bar);
        var window=new Window{Width=707,Height=100,Content=slot};
        EventHandler? observer=null;Exception? error=null;bool probing=false,interrupted=false;
        bool? clockImmediatelyAfter=null;string? labelImmediatelyAfter=null;
        var watch=Stopwatch.StartNew();var trace=new List<BreakFadeObservation>();
        TextBlock? text=null;Control? breakPanel=null;
        void Note(string stage)=>trace.Add(new(stage,watch.Elapsed.TotalMilliseconds,text?.Text,
            text?.Opacity??-1,text is not null&&Motion.IsAnimating(text),bar.IsLoaded,
            TopLevel.GetTopLevel(bar) is not null,breakPanel?.IsEffectivelyVisible??false));
        try
        {
            BreakFadePopulate(bar,oldMatch,1);window.Show();await Settle(100);
            text=Named<TextBlock>(bar,"BreakCenterText");breakPanel=Named<Control>(bar,"BreakCenterPanel");
            string originalLabel=QuarterLabelService.GetBreakLabel(1);
            string replacementLabel=QuarterLabelService.GetBreakLabel(2);
            Assert.True(bar.IsLoaded);Assert.True(breakPanel.IsEffectivelyVisible);
            Assert.Equal(originalLabel,text.Text);Assert.Equal(1d,text.Opacity,6);
            using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
            Capture(window,"scorebar-break-"+interruption+"-before-natural-tick");Note("ready");
            observer=(_,_)=>
            {
                if(probing||interrupted||error is not null)return;
                // Actual authored17s timer, actual source clock and actual
                // opacity before/after a real headless rendered frame.
                if(!Motion.IsAnimating(text)||text.Opacity<=.10||text.Opacity>=.96||text.Text!=originalLabel)return;
                probing=true;
                try
                {
                    using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
                    if(!Motion.IsAnimating(text)||text.Opacity<=.10||text.Opacity>=.96||text.Text!=originalLabel)return;
                    Note("actual-painted-fade-out");Capture(window,"scorebar-break-"+interruption+"-actual-fade");
                    interrupted=true;
                    if(interruption=="unload")slot.Children.Remove(bar);
                    else BreakFadePopulate(bar,newMatch,2); // Existing break mode remains true.
                    clockImmediatelyAfter=Motion.IsAnimating(text);labelImmediatelyAfter=text.Text;Note("immediately-after-public-interruption");
                }
                catch(Exception caught){error=caught;}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;
            // Bound is readiness for real cadence, not an exact duration claim.
            for(int attempt=0;attempt<1250&&!interrupted&&error is null;attempt++)await Settle(20);
            Assert.Null(error);Assert.True(interrupted,"No actual painted fade was witnessed after the natural17s timer; do not substitute a private Tick or synthetic timeline sample.");
            NativeRendering.Rendering-=observer;observer=null;
            await Settle(1200);Note("after-old-completion-budget");
            string expected=interruption=="unload"?originalLabel:replacementLabel;
            // Record all old-callback results before the first failure so the
            // baseline distinguishes an active clock from actual stale text.
            Assert.False(clockImmediatelyAfter,"Public unload/rebind must retire the previous fade immediately.");
            Assert.Equal(expected,labelImmediatelyAfter);Assert.Equal(expected,text.Text);
            Assert.False(Motion.IsAnimating(text));Assert.Equal(1d,text.Opacity,6);
            Assert.Equal(oldState,System.Text.Json.JsonSerializer.Serialize(oldMatch.ToState()));
            Assert.Equal(newState,System.Text.Json.JsonSerializer.Serialize(newMatch.ToState()));
            if(interruption=="unload")
            {
                Assert.False(bar.IsLoaded);Assert.Null(TopLevel.GetTopLevel(bar));
                slot.Children.Add(bar);await Settle(120);Note("reattached-before-new-cadence");
                Assert.True(bar.IsLoaded);Assert.Equal(originalLabel,text.Text);Assert.False(Motion.IsAnimating(text));
            }
            else Assert.True(bar.IsLoaded);
            Capture(window,"scorebar-break-"+interruption+"-settled");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            try{Mr5FinalsWriteTrace("scorebar-break-"+interruption+"-natural-baseline",new{interruption,interrupted,clockImmediatelyAfter,labelImmediatelyAfter,Error=error?.ToString(),Frames=trace});}
            finally{window.Close();}
        }
    }

    [AvaloniaFact]
    [Trait("Evidence","ScoreBarNaturalBreakFadePositiveControl")]
    public async Task ActualNaturalBreakCadenceStillRevealsWallClockAndSettlesWhenItsOwnerRemainsCurrent()
    {
        await using var storage=await AflFixture.Create(1280,850);
        var match=BreakFadeMatch(1);string state=System.Text.Json.JsonSerializer.Serialize(match.ToState());
        var bar=new PresentationScoreBar{Height=64};var window=new Window{Width=707,Height=100,Content=bar};
        bool witnessed=false,probing=false;Exception? error=null;EventHandler? observer=null;
        var trace=new List<BreakFadeObservation>();var watch=Stopwatch.StartNew();
        try
        {
            BreakFadePopulate(bar,match,1);window.Show();await Settle(100);
            var text=Named<TextBlock>(bar,"BreakCenterText");var panel=Named<Control>(bar,"BreakCenterPanel");
            string label=QuarterLabelService.GetBreakLabel(1);Assert.Equal(label,text.Text);
            observer=(_,_)=>
            {
                if(probing||witnessed||error is not null||!Motion.IsAnimating(text)||text.Opacity<=.10||text.Opacity>=.96||text.Text!=label)return;
                probing=true;try
                {
                    using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
                    if(!Motion.IsAnimating(text)||text.Opacity<=.10||text.Opacity>=.96||text.Text!=label)return;
                    witnessed=true;trace.Add(new("actual-painted-fade",watch.Elapsed.TotalMilliseconds,text.Text,text.Opacity,true,bar.IsLoaded,true,panel.IsEffectivelyVisible));
                    Capture(window,"scorebar-break-positive-actual-fade");
                }
                catch(Exception caught){error=caught;}finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;
            for(int attempt=0;attempt<1250&&!witnessed&&error is null;attempt++)await Settle(20);
            Assert.Null(error);Assert.True(witnessed,"Actual natural cadence must start a painted fade, preventing a disabled-animation fix from passing.");
            NativeRendering.Rendering-=observer;observer=null;
            for(int attempt=0;attempt<100&&(Motion.IsAnimating(text)||text.Text==label);attempt++)await Settle(20);
            Assert.NotEqual(label,text.Text);Assert.True(DateTime.TryParse(text.Text,CultureInfo.CurrentCulture,DateTimeStyles.None,out _));
            Assert.False(Motion.IsAnimating(text));Assert.Equal(1d,text.Opacity,6);Assert.True(panel.IsEffectivelyVisible);
            Assert.Equal(state,System.Text.Json.JsonSerializer.Serialize(match.ToState()));
            trace.Add(new("actual-current-owner-completed",watch.Elapsed.TotalMilliseconds,text.Text,text.Opacity,false,bar.IsLoaded,true,panel.IsEffectivelyVisible));
            Capture(window,"scorebar-break-positive-wall-clock");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            try{Mr5FinalsWriteTrace("scorebar-break-positive-natural-baseline",new{witnessed,Error=error?.ToString(),Frames=trace});}
            finally{window.Close();}
        }
    }
}
