// OUTSIDE TEST DRAFT ONLY: four cases; UNCOMPILED/UNRUN. No repo mutation.
// Reuses actual AFL setup, original PresCardWindow and existing named helpers.
// Real managed rendering witnesses, no SampleAt/private behavior invocation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record PresWindowMotionFrame(string Stage,double Milliseconds,bool Open,string? Label,
        double X,bool Clock,bool FocusWithin,bool KeyboardMode,Rect Card,Rect Track,Rect Thumb);
    private static bool PresWindowOpen(AflFixture f)=>PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true};
    private static async Task PresWindowObserve(Func<bool> reached,string reason)
    {
        // Diagnostic readiness bound, not an expected pose at a fixed timestamp.
        for(int attempt=0;attempt<400&&!reached();attempt++)await Settle(12);
        Assert.True(reached(),reason);
    }
    private static void PresWindowPointer(AflFixture f,Control card)
    {
        // Caller establishes a rendered scene before starting any short flight.
        // Cards/text do not move during the thumb motion. No hot-path PNG/pump.
        Rect box=FinalsPainted(f.Window,card);
        Assert.True(new Rect(f.Window.ClientSize).Contains(box));
        Assert.All(FinalsProbes(box),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
        f.Window.MouseMove(box.Center);
        f.Window.MouseDown(box.Center,MouseButton.Left);f.Window.MouseUp(box.Center,MouseButton.Left);
    }
    private static void PresWindowAuthority(AflFixture f,bool open,string state,string screen,PresPlanChanges changes)
    {
        Assert.Equal(open,PresWindowOpen(f));
        Assert.Equal(open?"Close":"Open",Named<TextBlock>(f.Window,"WindowToggleLabel").Text);
        Assert.Equal(screen,PresPlanRead<string>(f,"_activeScreenKey"));
        Assert.Equal(state,Scores(f.Match));Assert.Equal(0,changes.Count);
    }

    [AvaloniaTheory]
    [InlineData(1280,850,"Expressive")]
    [InlineData(420,320,"Calm")]
    public async Task OriginalWindowPointerRetainsFocusReversesAnObservedFlightAndLiveReduceSettlesCurrentTruth(
        int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;
        bool? oldOverride=OperatorMotion.AnimationOverride;EventHandler? observer=null;
        var trace=new List<PresWindowMotionFrame>();var watch=Stopwatch.StartNew();
        string stage="setup",state="",screen="";string? error=null;
        bool firstFlight=false,reverseFlight=false,lastFlight=false,dispatching=false;
        double reverseStart=0,lastStart=0,reducedAt=0;int reducedFrames=0;
        using var changes=new PresPlanChanges(f.Match);
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            if(!PresWindowOpen(f))PresPlanProvider(card).Invoke();
            await Until(()=>PresWindowOpen(f)&&Named<TextBlock>(f.Window,"WindowToggleLabel").Text=="Close","Setup requires the actual output to be open.");
            await Settle(500);Assert.Equal(20d,PresPlanThumb(f).X,4);
            Assert.True(card.Focus(NavigationMethod.Tab));
            using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
            Rect fixedCard=FinalsPainted(f.Window,card);
            var track=Named<Border>(f.Window,"WindowToggleTrack");var thumbPaint=Named<Border>(f.Window,"WindowToggleThumb");
            var thumb=PresPlanThumb(f);var label=Named<TextBlock>(f.Window,"WindowToggleLabel");
            double labelSize=label.FontSize;Point labelCenter=FinalsPainted(f.Window,label).Center;
            state=Scores(f.Match);screen=PresPlanRead<string>(f,"_activeScreenKey");
            Assert.Equal(0,changes.Count);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(dispatching||error is not null)return;
                var frame=new PresWindowMotionFrame(stage,watch.Elapsed.TotalMilliseconds,PresWindowOpen(f),label.Text,
                    thumb.X,Motion.IsAnimating(thumb),card.IsKeyboardFocusWithin,OperatorInputFocus.GetIsKeyboardMode(card),
                    FinalsPainted(f.Window,card),FinalsPainted(f.Window,track),FinalsPainted(f.Window,thumbPaint));
                trace.Add(frame);
                try
                {
                    Assert.Equal(fixedCard,frame.Card);Assert.True(frame.Track.Contains(frame.Thumb));
                    Assert.Equal(labelSize,label.FontSize);
                    var center=FinalsPainted(f.Window,label).Center;
                    Assert.Equal(labelCenter.X,center.X,3);Assert.Equal(labelCenter.Y,center.Y,3);
                    bool mid=frame.Clock&&frame.X>.01&&frame.X<19.99;
                    if(stage=="pointer-close"&&mid&&!frame.Open)
                    {
                        // This positive retained-focus witness would fail V1's
                        // focus-only keyboard suppression, despite real pointer input.
                        Assert.True(frame.FocusWithin);Assert.False(frame.KeyboardMode);
                        PresWindowAuthority(f,false,state,screen,changes);
                        firstFlight=true;reverseStart=frame.X;dispatching=true;stage="reverse-open";
                        PresWindowPointer(f,card);PresWindowAuthority(f,true,state,screen,changes);dispatching=false;
                    }
                    else if(stage=="reverse-open"&&mid&&frame.Open&&frame.X>reverseStart+.02)
                    {
                        reverseFlight=true;lastStart=frame.X;dispatching=true;stage="rapid-close";
                        PresWindowPointer(f,card);PresWindowAuthority(f,false,state,screen,changes);dispatching=false;
                    }
                    else if(stage=="rapid-close"&&mid&&!frame.Open&&frame.X<lastStart-.02)
                    {
                        lastFlight=true;stage="live-reduce";reducedAt=watch.Elapsed.TotalMilliseconds;
                        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.False(OperatorMotion.Enabled);Assert.Equal(0d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
                        PresWindowAuthority(f,false,state,screen,changes);
                    }
                    else if(stage=="live-reduce")
                    {
                        reducedFrames++;Assert.Equal(0d,frame.X,4);Assert.False(frame.Clock);
                        PresWindowAuthority(f,false,state,screen,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();dispatching=false;}
            };
            NativeRendering.Rendering+=observer;
            stage="pointer-close";PresWindowPointer(f,card);
            // A real nested frame may already have performed our deliberate
            // reversal before the outer pointer event returns. Check the latest
            // authority; the accepted departure is separately pinned above.
            PresWindowAuthority(f,stage=="reverse-open",state,screen,changes);
            await PresWindowObserve(()=>error is not null||(lastFlight&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Need actual retained-focus departure, reverse arrival, second departure and stable post-Reduce frames; do not replace missing witnesses with a forced endpoint.");
            Assert.Null(error);Assert.True(firstFlight);Assert.True(reverseFlight);Assert.True(lastFlight);
            PresWindowAuthority(f,false,state,screen,changes);
            Capture(f.Window,$"pres-window-reversal-reduce-{width}-{policy}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"pres-window-reversal-reduce-{width}-{policy}",new{Error=error,FirstFlight=firstFlight,ReverseFlight=reverseFlight,LastFlight=lastFlight,Frames=trace});
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);
            OperatorMotion.AnimationOverride=oldOverride;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task OriginalWindowHideExternalCloseShowKeyboardAndCancelledCloseRetainOnlyActualState(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;
        bool? oldOverride=OperatorMotion.AnimationOverride;EventHandler? observer=null;
        Window? cancelOwner=null;EventHandler<WindowClosingEventArgs>? cancelHandler=null;
        var trace=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup",state="",screen="";string? error=null;
        bool hidden=false,ownerClosed=false,acceptedOpen=false,dispatching=false;int shownFrames=0;
        using var changes=new PresPlanChanges(f.Match);
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            if(PresWindowOpen(f))PresPlanProvider(card).Invoke();
            await Until(()=>!PresWindowOpen(f)&&Named<TextBlock>(f.Window,"WindowToggleLabel").Text=="Open","Start with a genuinely closed output.");
            await Settle(500);
            var thumb=PresPlanThumb(f);var label=Named<TextBlock>(f.Window,"WindowToggleLabel");
            state=Scores(f.Match);screen=PresPlanRead<string>(f,"_activeScreenKey");Assert.Equal(0,changes.Count);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            observer=(_,_)=>
            {
                if(dispatching||error is not null)return;
                trace.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,Open=PresWindowOpen(f),Label=label.Text,
                    X=thumb.X,Clock=Motion.IsAnimating(thumb),Visible=f.Window.IsVisible});
                try
                {
                    if(stage=="open-before-hide"&&PresWindowOpen(f)&&Motion.IsAnimating(thumb)&&thumb.X>.01&&thumb.X<19.99)
                    {
                        PresWindowAuthority(f,true,state,screen,changes);acceptedOpen=true;
                        dispatching=true;
                        var actual=PresPlanRead<Window>(f,"_displayWindow");
                        f.Window.Hide();Assert.False(f.Window.IsVisible);
                        Assert.True(actual.IsVisible);Assert.Equal(20d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
                        actual.Close();Assert.False(PresWindowOpen(f));
                        Assert.Equal(0d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
                        Assert.Equal("Open",label.Text);hidden=true;stage="hidden-current-closed";dispatching=false;
                        PresWindowAuthority(f,false,state,screen,changes);
                    }
                    else if(stage=="reshown-current-closed")
                    {
                        shownFrames++;Assert.Equal(0d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
                        PresWindowAuthority(f,false,state,screen,changes);
                    }
                    else if(stage=="close-before-owner-close"&&!PresWindowOpen(f)&&Motion.IsAnimating(thumb)&&thumb.X>.01&&thumb.X<19.99)
                    {
                        dispatching=true;f.Window.Close();ownerClosed=true;stage="owner-closed";dispatching=false;
                        Assert.False(Motion.IsAnimating(thumb));
                    }
                }
                catch(Exception failure){error=failure.ToString();dispatching=false;}
            };
            NativeRendering.Rendering+=observer;
            stage="open-before-hide";PresWindowPointer(f,card);
            // Output.Show may pump a nested real rendering callback. If our
            // own observer already closed it, its closed state is authoritative.
            Assert.Null(error);PresWindowAuthority(f,!hidden,state,screen,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Hide/external close must interrupt an actually observed open flight.");
            Assert.Null(error);Assert.True(hidden);Assert.True(acceptedOpen);Assert.False(PresWindowOpen(f));
            stage="reshown-current-closed";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Showing the operator must expose only current closed state, without old arrival replay.");
            Assert.Null(error);PresWindowAuthority(f,false,state,screen,changes);
            // Separate original keyboard action: immediate endpoint and no clock.
            stage="keyboard-open";f.Window.Activate();Key(f.Window,PhysicalKey.Tab);Assert.True(card.Focus(NavigationMethod.Tab));
            Assert.True(OperatorInputFocus.GetIsKeyboardMode(card));Key(f.Window,PhysicalKey.Space);
            PresWindowAuthority(f,true,state,screen,changes);Assert.Equal(20d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
            int cancellations=0;cancelOwner=PresPlanRead<Window>(f,"_displayWindow");
            cancelHandler=(_,args)=>{cancellations++;args.Cancel=true;};cancelOwner.Closing+=cancelHandler;
            stage="cancelled-close";f.Window.Activate();Assert.True(card.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Space);
            Assert.Equal(1,cancellations);Assert.Same(cancelOwner,PresPlanRead<Window>(f,"_displayWindow"));
            PresWindowAuthority(f,true,state,screen,changes);Assert.False(PresPlanRead<bool>(f,"_outputActivationRequired"));
            Assert.Equal(20d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
            cancelOwner.Closing-=cancelHandler;cancelHandler=null;
            // The close action starts a final real departure; owner close must
            // retire its clock. No private cleanup or preference stop is used.
            stage="close-before-owner-close";f.Window.Activate();Assert.True(card.Focus(NavigationMethod.Tab));
            using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
            PresWindowPointer(f,card);PresWindowAuthority(f,false,state,screen,changes);
            await PresWindowObserve(()=>ownerClosed||error is not null,"Need actual owner-close cleanup during a current departure.");
            Assert.Null(error);Assert.True(ownerClosed);Assert.False(Motion.IsAnimating(thumb));
        }
        finally
        {
            if(cancelOwner is not null&&cancelHandler is not null)cancelOwner.Closing-=cancelHandler;
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"pres-window-lifetime-{width}",new{Error=error,AcceptedOpen=acceptedOpen,Hidden=hidden,OwnerClosed=ownerClosed,Frames=trace});
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);
            OperatorMotion.AnimationOverride=oldOverride;
        }
    }
}
