// AFTER-ONLY OUTSIDE DRAFT. Four cases, UNCOMPILED/UNRUN.
// Actual original controls, real input and coherent rendered source/binding/parts.
// No SampleAt, target-progress writes, fake clock, or private behavior invocation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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
    private sealed record LiveAfterParts(AflLiveScorebugGlyph Glyph,OperatorChoiceSurface Selection,
        Border Home,Border Away,Border Timing,Border HomeWell,Border AwayWell,Border Rail,
        TranslateTransform HomeTravel,TranslateTransform AwayTravel,TranslateTransform TimingTravel,Control Monitor);
    private static LiveAfterParts LiveAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflLiveScorebugGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var home=Part<Border>("LiveScorebugHomeFill");var away=Part<Border>("LiveScorebugAwayFill");var timing=Part<Border>("LiveScorebugTimingStrip");
        return new(glyph,Part<OperatorChoiceSurface>("LiveScorebugSelectionWell"),home,away,timing,
            Part<Border>("LiveScorebugHomeWell"),Part<Border>("LiveScorebugAwayWell"),Part<Border>("LiveScorebugTimingRail"),
            Assert.IsType<TranslateTransform>(home.RenderTransform),Assert.IsType<TranslateTransform>(away.RenderTransform),
            Assert.IsType<TranslateTransform>(timing.RenderTransform),Part<Avalonia.Controls.Shapes.Path>("LiveScorebugMonitorFrame"));
    }
    private static void LiveAfterPaintParts(LiveAfterParts p,double source)
    {
        double home=Math.Clamp(source/.45,0,1),away=Math.Clamp((source-.12)/.45,0,1),timing=Math.Clamp((source-.40)/.60,0,1);
        Assert.Equal(source,p.Selection.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);
        Assert.Equal(.50*home,p.Home.Opacity,4);Assert.Equal(.50*away,p.Away.Opacity,4);Assert.Equal(.85*timing,p.Timing.Opacity,4);
        Assert.Equal(-1.2*(1-home)*OperatorMotion.TravelScale,p.HomeTravel.X,4);
        Assert.Equal(1.2*(1-away)*OperatorMotion.TravelScale,p.AwayTravel.X,4);
        Assert.Equal(1.4*(1-timing)*OperatorMotion.TravelScale,p.TimingTravel.Y,4);
        Assert.Equal(16d,p.Timing.Bounds.Width);Assert.Equal(18d,p.Rail.Bounds.Width);
        Assert.Equal(8d,p.HomeWell.Bounds.Width);Assert.Equal(8d,p.AwayWell.Bounds.Width);
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Home));Assert.False(Motion.IsAnimating(p.Away));Assert.False(Motion.IsAnimating(p.Timing));
        Assert.False(Motion.IsAnimating(p.HomeTravel));Assert.False(Motion.IsAnimating(p.AwayTravel));Assert.False(Motion.IsAnimating(p.TimingTravel));
    }
    private static async Task<(Control Card,Control Summary)> LiveAfterPrepare(AflFixture f)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Observe glyph selection independently of an actual open output.");
        var card=Named<Control>(f.Window,"PresCardScorebug");var summary=Named<Control>(f.Window,"PresCardSummary");
        await PresPlanReveal(f,summary);PresPlanProvider(summary).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="summary","Warm the original alternate presentation before observing the short live-scorebug selection clock.");
        await PresPlanReveal(f,card);
        // First two cards share their actual responsive row. Both physical
        // targets must be fully reachable before a mid-flight reverse is used.
        using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
        Assert.All(FinalsProbes(FinalsPainted(f.Window,summary)),point=>Assert.True(Within(summary,f.Window.InputHitTest(point))));
        return(card,summary);
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureLiveScorebugActualSelectionDrivesStagedTeamWellsTimingThenReversalAndLiveReduce(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-live-scorebug-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var (card,summary)=await LiveAfterPrepare(f);var parts=LiveAfterRead(card);var title=LiveCardOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Initial logical summary must propagate through the actual attached-progress bridge.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedMonitor=FinalsPainted(f.Window,parts.Monitor);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),surfaceBefore=parts.Selection.Progress,boundBefore=parts.Glyph.Progress;
                    bool clock=Motion.IsAnimating(card);
                    if(stage=="reduced")
                    {
                        LiveAfterPaintParts(parts,1);Assert.False(clock);Assert.Equal(1d,before,4);reducedFrames++;
                        LiveCardModelAuthority(f,"scorebug",false,model,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card),surfaceAfter=parts.Selection.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-surfaceBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-surfaceAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,SurfaceBefore=surfaceBefore,BoundBefore=boundBefore,
                        SourceAfter=after,SurfaceAfter=surfaceAfter,BoundAfter=boundAfter,Coherent=coherent,
                        HomeOpacity=parts.Home.Opacity,AwayOpacity=parts.Away.Opacity,TimingOpacity=parts.Timing.Opacity,
                        HomeX=parts.HomeTravel.X,AwayX=parts.AwayTravel.X,TimingY=parts.TimingTravel.Y,OutputOpen=PresWindowOpen(f)});
                    if(!coherent)return;coherentFrames++;LiveAfterPaintParts(parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));Assert.Equal(fixedMonitor,FinalsPainted(f.Window,parts.Monitor));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        LiveCardModelAuthority(f,"scorebug",false,model,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckScorebug").Opacity);
                        if(after<.399)
                        {
                            early=true;Assert.True(parts.Home.Opacity>parts.Away.Opacity);Assert.Equal(0d,parts.Timing.Opacity);
                            image.Save(Path.Combine(directory,$"live-scorebug-{policy}-{width}-teams.png"));
                        }
                        if(after>.601)
                        {
                            late=true;Assert.Equal(.5,parts.Home.Opacity,4);Assert.Equal(.5,parts.Away.Opacity,4);Assert.True(parts.Timing.Opacity>0);
                            image.Save(Path.Combine(directory,$"live-scorebug-{policy}-{width}-timing.png"));
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;stage="closing";PresWindowPointer(f,summary);
                            LiveCardModelAuthority(f,"summary",false,model,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckScorebug").Opacity);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;LiveCardModelAuthority(f,"summary",false,model,changes);
                        reopenFrom=after;stage="reopening";PresWindowPointer(f,card);LiveCardModelAuthority(f,"scorebug",false,model,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;
                        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
                        LiveCardModelAuthority(f,"scorebug",false,model,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            // Our own nested observer can already have reversed the first input.
            LiveCardModelAuthority(f,stage=="closing"?"summary":"scorebug",false,model,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Require coherent naturally rendered early team handoff, late timing handoff, physical reversal/reopen, then stable actual Reduce; no manufactured intermediate pose.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            LiveAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));Capture(f.Window,$"live-scorebug-{policy}-{width}-reduced");
            // Real endpoint paint supplements the live property/binding frames.
            // Hold the source endpoint, hide only the authored fills for backing,
            // and restore in finally. The steady frame/wells remain untouched.
            probing=true;
            using(var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,$"live-scorebug-{policy}-{width}-endpoint.png")))
            {
                Rect box=FinalsPainted(f.Window,parts.Glyph);
                double homeOpacity=parts.Home.Opacity,awayOpacity=parts.Away.Opacity,timingOpacity=parts.Timing.Opacity;
                try
                {
                    parts.Home.Opacity=parts.Away.Opacity=parts.Timing.Opacity=0;
                    using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,$"live-scorebug-{policy}-{width}-endpoint-backing.png"));
                    double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
                    for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                        for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                        {
                            var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);
                            if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;
                        }
                    Assert.True(changed>=40,"The authored team wells/timing fill must visibly paint inside the actual glyph, beyond its stationary monitor frame.");
                }
                finally{parts.Home.Opacity=homeOpacity;parts.Away.Opacity=awayOpacity;parts.Timing.Opacity=timingOpacity;}
            }
            probing=false;
            // Same selected command is real semantic Invoke, intentionally
            // dispatcher-posted. No selection replay may appear in real frames.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresPlanProvider(card).Invoke();await Settle(180);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.True(error is null,error);LiveCardModelAuthority(f,"scorebug",false,model,changes);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"live-scorebug-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureLiveScorebugKeyboardHideDisabledChoiceAndOwnerCloseSettleOnlyCurrentSelection(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,closed=false;string stage="setup";string? error=null;
        var frames=new List<object>();
        try
        {
            var (card,summary)=await LiveAfterPrepare(f);var parts=LiveAfterRead(card);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);Assert.True(card.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"scorebug",false,model,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==1,"Actual keyboard endpoint must traverse the ordinary binding.");LiveAfterPaintParts(parts,1);
            Assert.True(summary.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            LiveCardModelAuthority(f,"summary",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==0,"Actual Enter deselection endpoint must traverse the ordinary binding.");
            using(var image=f.Window.CaptureRenderedFrame())Assert.NotNull(image);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double source=OperatorChoiceMotion.GetProgress(card);bool clock=Motion.IsAnimating(card);
                    frames.Add(new{stage,Source=source,Bound=parts.Glyph.Progress,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled});
                    if(stage=="hide-flight"&&clock&&source>.01&&source<.99)
                    {
                        LiveCardModelAuthority(f,"scorebug",false,model,changes);accepted=true;
                        f.Window.Hide();Assert.False(f.Window.IsVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
                        hidden=true;stage="hidden";
                    }
                    else if(stage=="close-flight"&&clock&&source>.01&&source<.99)
                    {
                        LiveCardModelAuthority(f,"scorebug",false,model,changes);f.Window.Close();closed=true;stage="closed";
                        Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);
            LiveCardModelAuthority(f,"scorebug",false,model,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual public owner Hide must interrupt a naturally active selection clock.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await Until(()=>parts.Glyph.Progress==1,"Show must render the current selected endpoint rather than replay old arrival.");LiveAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));
            // Public IsEnabled is an explicit controlled lifecycle stimulus,
            // not a private application action or native accessibility claim.
            card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,summary);LiveCardModelAuthority(f,"summary",false,model,changes);
            Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled original card must show its current deselected state.");
            LiveAfterPaintParts(parts,0);Assert.False(Motion.IsAnimating(card));
            stage="close-flight";PresWindowPointer(f,card);
            await PresWindowObserve(()=>closed||error is not null,"Actual public owner Close must retire a naturally active new selection flight.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));
            Assert.False(OperatorChoiceMotion.GetEnabled(card));Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"live-scorebug-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Closed=closed,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
