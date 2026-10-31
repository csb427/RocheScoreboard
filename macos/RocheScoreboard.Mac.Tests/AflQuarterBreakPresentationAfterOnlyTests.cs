// OUTSIDE AFTER-ONLY v2 DRAFT (depends on v2 baseline observation helper): 2 paired-panel phase/reversal/pixel + 2 keyboard/lifetime cases.
// UNAPPLIED/UNCOMPILED/UNRUN. Requires the separately pinned Quarter break source candidate.
// Reuses accepted current AflFixture/Live/Presentation/Finals helpers; no private command,
// SampleAt, fake timer Tick, Progress write, field/flag write or manufactured intermediate frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record QuarterBreakAfterParts(AflQuarterBreakGlyph Glyph,OperatorChoiceSurface Bridge,
        Border Frame,Border Left,Border Right,Border Centre,ScaleTransform LeftFold,ScaleTransform RightFold,ScaleTransform CentreUnfold);
    private static QuarterBreakAfterParts QuarterBreakAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflQuarterBreakGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var left=Part<Border>("QuarterBreakPresentationLeftPanel");var right=Part<Border>("QuarterBreakPresentationRightPanel");
        var centre=Part<Border>("QuarterBreakPresentationCentreBand");
        return new(glyph,Part<OperatorChoiceSurface>("QuarterBreakPresentationSelectionBridge"),Part<Border>("QuarterBreakPresentationFrame"),left,right,centre,
            Assert.IsType<ScaleTransform>(left.RenderTransform),Assert.IsType<ScaleTransform>(right.RenderTransform),Assert.IsType<ScaleTransform>(centre.RenderTransform));
    }
    private static void QuarterBreakAfterPaintParts(AflFixture f,QuarterBreakAfterParts p,double source)
    {
        double panels=Math.Clamp(source/.64,0,1),centre=Math.Clamp((source-.48)/.52,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(panels,p.Glyph.PanelsPhase,4);Assert.Equal(centre,p.Glyph.CentrePhase,4);
        Assert.Equal(.68+.32*panels,p.LeftFold.ScaleX,4);Assert.Equal(p.LeftFold.ScaleX,p.RightFold.ScaleX,4);
        Assert.Equal(.60+.40*panels,p.Left.Opacity,4);Assert.Equal(p.Left.Opacity,p.Right.Opacity,4);
        Assert.Equal(.50+.50*centre,p.CentreUnfold.ScaleY,4);Assert.Equal(.45+.55*centre,p.Centre.Opacity,4);
        Assert.Equal(1d,p.LeftFold.ScaleY);Assert.Equal(1d,p.RightFold.ScaleY);Assert.Equal(1d,p.CentreUnfold.ScaleX);
        Assert.Equal(29d,p.Glyph.Bounds.Width);Assert.Equal(29d,p.Glyph.Bounds.Height);
        Assert.Equal(27d,p.Frame.Bounds.Width);Assert.Equal(19d,p.Frame.Bounds.Height);Assert.Equal(1d,p.Frame.Opacity);
        Assert.Equal(8d,p.Left.Bounds.Width);Assert.Equal(13d,p.Left.Bounds.Height);
        Assert.Equal(8d,p.Right.Bounds.Width);Assert.Equal(13d,p.Right.Bounds.Height);
        Assert.Equal(5d,p.Centre.Bounds.Width);Assert.Equal(3d,p.Centre.Bounds.Height);
        Assert.Equal(4d,Canvas.GetLeft(p.Left));Assert.Equal(8d,Canvas.GetTop(p.Left));
        Assert.Equal(17d,Canvas.GetLeft(p.Right));Assert.Equal(8d,Canvas.GetTop(p.Right));
        Assert.Equal(12d,Canvas.GetLeft(p.Centre));Assert.Equal(13d,Canvas.GetTop(p.Centre));
        Rect glyph=FinalsPainted(f.Window,p.Glyph),left=FinalsPainted(f.Window,p.Left),right=FinalsPainted(f.Window,p.Right),band=FinalsPainted(f.Window,p.Centre);
        Assert.Equal(8*p.LeftFold.ScaleX,left.Width,3);Assert.Equal(8*p.RightFold.ScaleX,right.Width,3);
        Assert.Equal(3*p.CentreUnfold.ScaleY,band.Height,3);
        Assert.Equal(glyph.Left+4,left.Left,3);Assert.Equal(glyph.Left+25,right.Right,3);Assert.Equal(glyph.Left+14.5,band.Center.X,3);
        foreach(var part in new[]{p.Frame,p.Left,p.Right,p.Centre})
        {Assert.True(glyph.Contains(FinalsPainted(f.Window,part)));Assert.False(Motion.IsAnimating(part));Assert.False(part.IsHitTestVisible);Assert.False(part.Focusable);}
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));
        Assert.False(Motion.IsAnimating(p.LeftFold));Assert.False(Motion.IsAnimating(p.RightFold));Assert.False(Motion.IsAnimating(p.CentreUnfold));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);
        Assert.All(p.Glyph.GetVisualDescendants().OfType<Control>(),part=>Assert.False(part.Focusable));
        Assert.Empty(p.Glyph.GetVisualDescendants().OfType<TextBlock>()); // no invented clock/quarter/score text
    }
    private static void QuarterBreakAfterAuthority(AflFixture f,string key,bool open,string scores,string full,PresPlanChanges changes)
    {
        LiveCardModelAuthority(f,key,open,scores,changes);Assert.Equal(full,JsonSerializer.Serialize(f.Match.ToState()));
        Assert.False(f.Match.ClockRunning);Assert.Equal(!open,PresPlanRead<bool>(f,"_outputActivationRequired"));
        var card=Named<Control>(f.Window,"PresCardQuarterBreak");bool selected=key=="quarterbreak";
        Assert.Equal("Quarter break",AutomationProperties.GetName(card));Assert.Equal(selected?"Selected":"Not selected",AutomationProperties.GetItemStatus(card));
        Assert.Equal(selected?1d:0d,Named<Border>(f.Window,"PresCheckQuarterBreak").Opacity);
        Assert.Null(PresPlanRead<object?>(f,"_rotationTimer"));Assert.False(PresPlanRead<bool>(f,"_isBreakRotating"));Assert.Null(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));
    }
    private static async Task<(Control Card,Control Alternate)> QuarterBreakAfterPrepare(AflFixture f,bool pairedPhysicalRow)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        // PUBLIC MODEL FIXTURE SEAM, matching before4: paused positive-elapsed Q1, not a break/clock-edit interaction proof.
        QuarterLabelService.SetCurrentOverride(null);f.Match.SetElapsed(TimeSpan.FromSeconds(12));Assert.False(f.Match.ClockRunning);
        await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Observe explicit output activation hold before any Quarter break motion.");
        Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
        var card=Named<Control>(f.Window,"PresCardQuarterBreak");var alternate=Named<Control>(f.Window,pairedPhysicalRow?"PresCardScreensaver":"PresCardWeather");
        if(pairedPhysicalRow)
        {
            // Only Screensaver shares Quarter break's actual responsive row at420.
            // Its actual original off-air action is a no-op, unlike Quarter break.
            string key=PresPlanRead<string>(f,"_activeScreenKey"),full=JsonSerializer.Serialize(f.Match.ToState());
            using var changes=new PresPlanChanges(f.Match);
            await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();await Settle(180);
            Assert.Equal(key,PresPlanRead<string>(f,"_activeScreenKey"));Assert.False(PresWindowOpen(f));
            Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));Assert.Equal(full,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,changes.Count);
            await PresPlanReveal(f,window);PresPlanProvider(window).Invoke();await Until(()=>PresWindowOpen(f),"The real Window action must explicitly activate output before Screensaver may be a physical alternate.");
            Assert.False(PresPlanRead<bool>(f,"_outputActivationRequired"));
            // Warm the real paired output through its original semantic route
            // before measuring the much shorter operator-card clock. This is
            // accepted public setup, not a sampled/progress-written glyph pose.
            await PresPlanReveal(f,card);PresPlanProvider(card).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="quarterbreak"&&PresPlanRead<Control?>(f,"_quarterBreakScorebug") is {IsVisible:true},
                "Warm the actual Quarter break output before natural source-frame observations.");
            var output=Assert.IsAssignableFrom<Window>(PresPlanRead<Window?>(f,"_displayWindow"));
            using(var actualOutputFrame=output.CaptureRenderedFrame())Assert.NotNull(actualOutputFrame);
        }
        await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
        string alternateKey=pairedPhysicalRow?"screensaver":"weather";
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")==alternateKey,"Warm the actual original alternate, with its real output activation rules, before the short Quarter break source clock.");
        await PresPlanReveal(f,card);using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);
        if(pairedPhysicalRow)
        {
            Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,alternate)));
            Assert.All(FinalsProbes(FinalsPainted(f.Window,alternate)),point=>Assert.True(Within(alternate,f.Window.InputHitTest(point))));
        }
        return(card,alternate);
    }
    private static void QuarterBreakAfterPerPartPaint(AflFixture f,QuarterBreakAfterParts p,string directory,string prefix,List<object> receipts)
    {
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint.png"));
        foreach(var part in new[]{p.Frame,p.Left,p.Right,p.Centre})
        {
            double opacity=part.Opacity;Rect box=FinalsPainted(f.Window,part);
            try
            {
                part.Opacity=0;
                using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-without-"+part.Name+".png"));
                double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
                for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                    for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                    {var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;}
                // Four nontrivial actual pixels from EACH part, not one combined glyph count.
                receipts.Add(new{Part=part.Name,Bounds=box,ChangedPixels=changed,Minimum=4});
                Assert.True(changed>=4,$"Actual {part.Name} must independently paint. A source property or another visible part cannot satisfy this oracle.");
            }
            finally{part.Opacity=opacity;}
        }
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    [Trait("Evidence","AflQuarterBreakPresentationActualParts")]
    public async Task FutureQuarterBreakActualPairedPanelsHandOffToCentreThenPhysicalReverseRepeatAndLiveReduceKeepAuthority(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var pixels=new List<object>();var views=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0,repeatFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-break-presentation-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await QuarterBreakAfterPrepare(f,true);var parts=QuarterBreakAfterRead(card);var title=QuarterBreakPresentationOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Actual enabled Screensaver selection must reach the Quarter break bridge before observing its own clock.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedFrame=FinalsPainted(f.Window,parts.Frame);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match),full=JsonSerializer.Serialize(f.Match.ToState());
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),bridgeBefore=parts.Bridge.Progress,boundBefore=parts.Glyph.Progress;bool clock=Motion.IsAnimating(card);
                    if(stage is "reduced" or "repeat")
                    {
                        QuarterBreakAfterPaintParts(f,parts,1);Assert.False(clock);Assert.Equal(1d,before,4);
                        if(stage=="reduced")reducedFrames++;else repeatFrames++;
                        QuarterBreakAfterAuthority(f,"quarterbreak",true,model,full,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card),bridgeAfter=parts.Bridge.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridgeBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-bridgeAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,BridgeBefore=bridgeBefore,BoundBefore=boundBefore,
                        SourceAfter=after,BridgeAfter=bridgeAfter,BoundAfter=boundAfter,Coherent=coherent,LeftFold=parts.LeftFold.ScaleX,RightFold=parts.RightFold.ScaleX,
                        PanelsOpacity=parts.Left.Opacity,CentreScale=parts.CentreUnfold.ScaleY,CentreOpacity=parts.Centre.Opacity,OutputOpen=PresWindowOpen(f)});
                    if(!coherent)return;coherentFrames++;QuarterBreakAfterPaintParts(f,parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));Assert.Equal(fixedFrame,FinalsPainted(f.Window,parts.Frame));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        QuarterBreakAfterAuthority(f,"quarterbreak",true,model,full,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckQuarterBreak").Opacity);
                        if(!early&&after>=.15&&after<=.45)
                        {
                            early=true;Assert.InRange(parts.Glyph.PanelsPhase,.01,.99);Assert.Equal(0d,parts.Glyph.CentrePhase);
                            Assert.Equal(.45,parts.Centre.Opacity,4);image.Save(Path.Combine(directory,$"quarter-break-presentation-{policy}-{width}-paired-panels.png"));
                        }
                        if(!late&&after>=.70&&after<=.95)
                        {
                            late=true;Assert.Equal(1d,parts.Glyph.PanelsPhase);Assert.True(parts.Glyph.CentrePhase>0);
                            Assert.Equal(1d,parts.Left.Opacity);Assert.Equal(1d,parts.Right.Opacity);Assert.True(parts.Centre.Opacity>.45);
                            image.Save(Path.Combine(directory,$"quarter-break-presentation-{policy}-{width}-centre.png"));
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;stage="closing";PresWindowPointer(f,alternate);
                            QuarterBreakAfterAuthority(f,"screensaver",true,model,full,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckQuarterBreak").Opacity);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;QuarterBreakAfterAuthority(f,"screensaver",true,model,full,changes);reopenFrom=after;stage="reopening";
                        PresWindowPointer(f,card);QuarterBreakAfterAuthority(f,"quarterbreak",true,model,full,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));QuarterBreakAfterAuthority(f,"quarterbreak",true,model,full,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            QuarterBreakAfterAuthority(f,stage=="closing"?"screensaver":"quarterbreak",true,model,full,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Require coherent naturally rendered paired-panel then centre phases, same-row physical reverse/reopen and stable live Reduce; never manufacture a missed pose.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            QuarterBreakAfterPaintParts(f,parts,1);Assert.False(Motion.IsAnimating(card));
            probing=true;try{QuarterBreakAfterPerPartPaint(f,parts,directory,$"quarter-break-presentation-{policy}-{width}",pixels);}finally{probing=false;}
            stage="repeat";OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresWindowPointer(f,card);QuarterBreakAfterAuthority(f,"quarterbreak",true,model,full,changes);
            await PresWindowObserve(()=>repeatFrames>=12||error is not null,"Physical repeat of the selected original card must keep the source and every part settled without replay.");Assert.True(error is null,error);
            stage="off-air";var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);PresPlanProvider(window).Invoke();
            await Until(()=>!PresWindowOpen(f),"Close the actual output owner after enabled-output physical reversal, never fake the activation flag.");
            await PresPlanReveal(f,card);QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);QuarterBreakAfterPaintParts(f,parts,1);Assert.False(Motion.IsAnimating(card));
            var closedView=QuarterBreakInspectExistingOutput(f,true,false,QuarterLabelService.GetQuarterLabel(1));
            views.Add(new{Stage="after-actual-output-Close",closedView.ViewCreated,closedView.LabelInspected,closedView.Label,closedView.ViewVisible});
            Capture(f.Window,$"quarter-break-presentation-{policy}-{width}-retained-off-air");
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"quarter-break-presentation-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,RepeatFrames=repeatFrames,Frames=frames,ExistingViews=views,PerPartPixels=pixels});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","AflQuarterBreakPresentationKeyboardLifetime")]
    public async Task FutureQuarterBreakRealEnterCancelsPendingOwnerThenKeyboardHideDisableAndCloseSettleCurrentLogicalSelection(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,disabled=false,closed=false;int shownFrames=0;string stage="setup";string? error=null;
        var frames=new List<object>();var owners=new List<object>();
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-break-presentation-lifetime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await QuarterBreakAfterPrepare(f,false);var parts=QuarterBreakAfterRead(card);
            // Actual accepted End Quarter creates the45s owner. Automatic output
            // selection is NOT treated as a new write to the manual logical key.
            string logical=PresPlanRead<string>(f,"_activeScreenKey");Assert.Equal("weather",logical);
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);Assert.True(Named<CheckBox>(f.Window,"SettingsBreakScreenToggle").IsChecked);
            var end=Named<Button>(f.Window,"EndQtrButton");end.BringIntoView();
            await Until(()=>
            {
                using var frame=f.Window.CaptureRenderedFrame();
                if(!end.IsLoaded||!end.IsEffectivelyVisible||!end.IsEffectivelyEnabled||!end.GetVisualAncestors().Contains(f.Window))return false;
                var box=FinalsPainted(f.Window,end);return box.Width>8&&box.Height>8&&new Rect(f.Window.ClientSize).Contains(box)&&FinalsProbes(box).All(point=>Within(end,f.Window.InputHitTest(point)));
            },"Actual End Quarter must own all real pointer probes before the pending-owner witness.");
            int home=f.Match.HomeTotal,away=f.Match.AwayTotal,eventCount=f.Match.Events.Count;
            await Tap(f.Window,end);Assert.Equal(2,f.Match.Quarter);Assert.False(f.Match.ClockRunning);Assert.Equal(TimeSpan.Zero,f.Match.ElapsedInQuarter);
            Assert.NotNull(f.Match.GetQuarterSnapshot(1));Assert.Equal(home,f.Match.HomeTotal);Assert.Equal(away,f.Match.AwayTotal);Assert.Equal(eventCount,f.Match.Events.Count);
            Assert.Equal(logical,PresPlanRead<string>(f,"_activeScreenKey"));Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            var pending=Assert.IsType<Avalonia.Threading.DispatcherTimer>(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));Assert.True(pending.IsEnabled);Assert.Equal(TimeSpan.FromSeconds(45),pending.Interval);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match),full=JsonSerializer.Serialize(f.Match.ToState());
            owners.Add(new{Stage="before-one-Enter",f.Match.Quarter,f.Match.ClockRunning,f.Match.ElapsedInQuarter,Key=logical,OutputOpen=PresWindowOpen(f),PendingEnabled=pending.IsEnabled,PendingInterval=pending.Interval,ModelEvents=changes.Count});
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);await PresPlanReveal(f,card);f.Window.Activate();Key(f.Window,PhysicalKey.Tab);await PresPlanReveal(f,card);
            Assert.Same(pending,PresPlanRead<object?>(f,"_breakScreenDelayTimer"));Assert.True(pending.IsEnabled);Assert.Equal(full,JsonSerializer.Serialize(f.Match.ToState()));
            Key(f.Window,PhysicalKey.Enter); // ONE original key activation; no private timer/business invocation
            Assert.True(Within(card,f.Window.FocusManager?.GetFocusedElement()));QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);
            Assert.False(pending.IsEnabled);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.Equal("Selected",AutomationProperties.GetItemStatus(card));Assert.Equal(1d,Named<Border>(f.Window,"PresCheckQuarterBreak").Opacity);
            await Until(()=>parts.Glyph.Progress==1&&parts.Bridge.Progress==1,"Real reduced Enter endpoint must traverse the ordinary binding.");QuarterBreakAfterPaintParts(f,parts,1);
            var enterView=QuarterBreakInspectExistingOutput(f,true,PresWindowOpen(f),QuarterLabelService.GetBreakLabel(1));
            owners.Add(new{Stage="after-one-Enter",f.Match.Quarter,f.Match.ClockRunning,f.Match.ElapsedInQuarter,Key=PresPlanRead<string>(f,"_activeScreenKey"),OutputOpen=PresWindowOpen(f),PendingEnabled=pending.IsEnabled,CurrentPendingIsNull=PresPlanRead<object?>(f,"_breakScreenDelayTimer") is null,enterView.ViewCreated,enterView.LabelInspected,enterView.Label,enterView.ViewVisible,ModelEvents=changes.Count,ModelUnchanged=full==JsonSerializer.Serialize(f.Match.ToState())});
            Capture(f.Window,"quarter-break-presentation-one-Enter-pending-owner-"+width);
            // Weather may be offscreen at420: reveal and use actual keyboard;
            // this is never represented as a same-row physical reverse.
            await PresPlanReveal(f,alternate);Key(f.Window,PhysicalKey.Enter);QuarterBreakAfterAuthority(f,"weather",false,model,full,changes);
            await PresPlanReveal(f,card);Key(f.Window,PhysicalKey.Space);Assert.True(Within(card,f.Window.FocusManager?.GetFocusedElement()));
            QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Capture(f.Window,"quarter-break-presentation-reduced-Space-focus-"+width);
            await PresPlanReveal(f,alternate);Key(f.Window,PhysicalKey.Enter);QuarterBreakAfterAuthority(f,"weather",false,model,full,changes);
            await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Actual keyboard alternate must reach the current deselected bridge before natural lifetime flights.");
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card);bool clock=Motion.IsAnimating(card);
                    if(stage=="shown")
                    {
                        Assert.Equal(1d,before,4);Assert.False(clock);using var shown=f.Window.CaptureRenderedFrame();Assert.NotNull(shown);
                        bool settled=Math.Abs(OperatorChoiceMotion.GetProgress(card)-1)<.00001&&Math.Abs(parts.Bridge.Progress-1)<.00001&&Math.Abs(parts.Glyph.Progress-1)<.00001;
                        frames.Add(new{stage,Source=before,Bound=parts.Glyph.Progress,Settled=settled,Clock=clock});
                        if(!settled)return;shownFrames++;QuarterBreakAfterPaintParts(f,parts,1);QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);return;
                    }
                    if((stage is not ("hide-flight" or "disable-flight" or "close-flight"))||!clock||before<=.01||before>=.99)return;
                    double bound=parts.Glyph.Progress,bridge=parts.Bridge.Progress;using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bound)<.00001&&Math.Abs(before-bridge)<.00001
                        &&Math.Abs(after-parts.Glyph.Progress)<.00001&&Math.Abs(after-parts.Bridge.Progress)<.00001;
                    frames.Add(new{stage,SourceBefore=before,SourceAfter=after,BoundBefore=bound,BridgeBefore=bridge,BoundAfter=parts.Glyph.Progress,Coherent=coherent,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled});
                    if(!coherent)return;QuarterBreakAfterPaintParts(f,parts,after);QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);
                    Assert.All(FinalsProbes(FinalsPainted(f.Window,card)),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    image.Save(Path.Combine(directory,$"quarter-break-presentation-{width}-{stage}-actual-frame.png"));
                    if(stage=="hide-flight")
                    {
                        accepted=true;f.Window.Hide();Assert.False(f.Window.IsVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));hidden=true;stage="hidden";
                    }
                    else if(stage=="disable-flight")
                    {
                        card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));disabled=true;stage="disabled";
                    }
                    else
                    {
                        f.Window.Close();closed=true;stage="closed";Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);QuarterBreakAfterAuthority(f,"quarterbreak",false,model,full,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual owner Hide must interrupt a coherent captured source selection flight.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Actual Show must expose stable latest selection without old arrival replay.");Assert.True(error is null,error);
            stage="between";await PresPlanReveal(f,alternate);PresWindowPointer(f,alternate);QuarterBreakAfterAuthority(f,"weather",false,model,full,changes);
            await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0&&!Motion.IsAnimating(card),"Retire actual deselection before observing disable in a new flight.");
            stage="disable-flight";PresWindowPointer(f,card);
            await PresWindowObserve(()=>disabled||error is not null,"Public IsEnabled=false must settle an actual coherent new source flight.");Assert.True(error is null,error);Assert.True(disabled);
            // Controlled PUBLIC lifecycle stimulus, not a private action or a claim of disabled-card interaction.
            await PresPlanReveal(f,alternate);PresWindowPointer(f,alternate);QuarterBreakAfterAuthority(f,"weather",false,model,full,changes);
            Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled card shows the actual current deselected endpoint.");QuarterBreakAfterPaintParts(f,parts,0);
            stage="close-flight";PresWindowPointer(f,card);await PresWindowObserve(()=>closed||error is not null,"Actual owner Close must retire a new captured card-owned source clock.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
            Assert.Equal(model,Scores(f.Match));Assert.Equal(full,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,changes.Count);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await Settle(180);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);await Settle(180);
            Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);
            Assert.Equal(full,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,changes.Count);
            var ownerClosedView=QuarterBreakInspectExistingOutput(f,true,false,QuarterLabelService.GetBreakLabel(1));
            owners.Add(new{Stage="after-owner-Close",ownerClosedView.ViewCreated,ownerClosedView.LabelInspected,ownerClosedView.Label,ownerClosedView.ViewVisible});
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"quarter-break-presentation-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Disabled=disabled,Closed=closed,ShownFrames=shownFrames,PendingOwners=owners,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
