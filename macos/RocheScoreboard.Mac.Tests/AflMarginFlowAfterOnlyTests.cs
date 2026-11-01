// OUTSIDE AFTER-ONLY MARGIN FLOW DRAFT:2 phase/reversal/Reduce +2 keyboard/lifetime.
// Requires separate source candidate. UNCOMPILED/UNRUN. No SampleAt/progress writes.
// Actual public control input; one explicit DIRECT_UI_REFRESH seam is labeled.
// AflFixture owns/rolls back preference storage, including live Reduce/style changes.
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
    private sealed record MarginAfterParts(AflMarginFlowGlyph Glyph,OperatorChoiceSurface Bridge,Control Reference,Control Zero,
        Border[] Holds,Border[] Jumps,ScaleTransform[] HoldScales,ScaleTransform[] JumpScales);
    private static MarginAfterParts MarginAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflMarginFlowGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var holds=Enumerable.Range(1,4).Select(i=>Part<Border>("MarginHoldShelf"+i)).ToArray();
        var jumps=Enumerable.Range(1,3).Select(i=>Part<Border>("MarginJumpConnector"+i)).ToArray();
        return new(glyph,Part<OperatorChoiceSurface>("MarginFlowSelectionBridge"),Part<Avalonia.Controls.Shapes.Path>("MarginStepReference"),
            Part<Avalonia.Controls.Shapes.Path>("MarginZeroRail"),holds,jumps,
            holds.Select(part=>Assert.IsType<ScaleTransform>(part.RenderTransform)).ToArray(),jumps.Select(part=>Assert.IsType<ScaleTransform>(part.RenderTransform)).ToArray());
    }
    private static void MarginAfterPaintParts(MarginAfterParts p,double source)
    {
        double hold=Math.Clamp(source/.55,0,1),jump=Math.Clamp((source-.30)/.70,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(.72,p.Reference.Opacity);Assert.Equal(.45,p.Zero.Opacity);
        Assert.Equal(29d,p.Glyph.Bounds.Width);Assert.Equal(29d,p.Glyph.Bounds.Height);
        for(int i=0;i<p.Holds.Length;i++)
        {
            Assert.Equal(.15+.70*hold,p.Holds[i].Opacity,4);Assert.Equal(1-.40*OperatorMotion.TravelScale*(1-hold),p.HoldScales[i].ScaleX,4);
            Assert.Equal(i==2?6d:5d,p.Holds[i].Bounds.Width);Assert.Equal(1.7,p.Holds[i].Height,3);Assert.InRange(p.Holds[i].Bounds.Height,1d,2.5d);
            Assert.False(Motion.IsAnimating(p.Holds[i]));Assert.False(Motion.IsAnimating(p.HoldScales[i]));
        }
        double[] heights={9,11,6};
        for(int i=0;i<p.Jumps.Length;i++)
        {
            Assert.Equal(.10+.75*jump,p.Jumps[i].Opacity,4);Assert.Equal(1-.45*OperatorMotion.TravelScale*(1-jump),p.JumpScales[i].ScaleY,4);
            Assert.Equal(1.7,p.Jumps[i].Width,3);Assert.InRange(p.Jumps[i].Bounds.Width,1d,2.5d);Assert.Equal(heights[i],p.Jumps[i].Bounds.Height);
            Assert.False(Motion.IsAnimating(p.Jumps[i]));Assert.False(Motion.IsAnimating(p.JumpScales[i]));
        }
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));Assert.False(Motion.IsAnimating(p.Reference));Assert.False(Motion.IsAnimating(p.Zero));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);
        Assert.All(p.Glyph.GetVisualDescendants().OfType<Control>(),part=>Assert.False(part.Focusable));
        Assert.Empty(p.Glyph.GetVisualDescendants().OfType<TextBlock>());
    }
    private static async Task<(Control Card,Control Alternate)> MarginAfterPrepare(AflFixture f)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Observe Margin flow selection separately from actual open output.");
        var card=Named<Control>(f.Window,"PresCardWorm");var alternate=Named<Control>(f.Window,"PresCardStats");
        await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="stats","Actual original Stats must establish a different presentation and warm the hidden output before the short Margin flow clock.");
        await PresPlanReveal(f,card);
        using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);
        // Worm/Stats share their actual responsive row; prove this rather than
        // treating an offscreen semantic activation as a physical reverse.
        Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,alternate)));
        Assert.All(FinalsProbes(FinalsPainted(f.Window,alternate)),point=>Assert.True(Within(alternate,f.Window.InputHitTest(point))));
        return(card,alternate);
    }
    private static void MarginAfterPerPartPaint(AflFixture f,MarginAfterParts p,string directory,string prefix,List<object> receipts)
    {
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint.png"));
        foreach(var part in p.Holds.Concat(p.Jumps))
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
                int minimum=part.Name!.StartsWith("MarginHoldShelf",StringComparison.Ordinal)?2:4;
                receipts.Add(new{Part=part.Name,Bounds=box,ChangedPixels=changed,Minimum=minimum});
                Assert.True(changed>=minimum,$"Actual {part.Name} must independently paint above the stationary full reference. A combined glyph count cannot prove each contribution.");
            }
            finally{part.Opacity=opacity;}
        }
    }
    private static async Task MarginDirectVisualRefreshContract(AflFixture f,Control card,MarginAfterParts parts,string model,PresPlanChanges changes)
    {
        // DIRECT UI REFRESH SEAM ONLY. No private setup/model/scoring call.
        Assert.False(OperatorMotion.Enabled);Assert.Equal("stats",PresPlanRead<string>(f,"_activeScreenKey"));
        var refresh=f.Window.GetType().GetMethod("UpdatePresentationCards",HiddenInstance);Assert.NotNull(refresh);
        foreach(bool requested in new[]{true,false})
        {
            refresh.Invoke(f.Window,new object[]{requested?"worm":"stats"});
            Assert.Equal("stats",PresPlanRead<string>(f,"_activeScreenKey"));
            Assert.Equal(requested,OperatorChoiceMotion.GetSelected(card));Assert.Equal(requested?1d:0d,OperatorChoiceMotion.GetProgress(card),4);
            Assert.Equal(requested?1d:0d,Named<Border>(f.Window,"PresCheckWorm").Opacity);Assert.False(Motion.IsAnimating(card));
            Assert.Equal(requested?"Selected":"Not selected",Avalonia.Automation.AutomationProperties.GetItemStatus(card));
            await Settle(100);using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);MarginAfterPaintParts(parts,requested?1:0);
            LiveCardModelAuthority(f,"stats",false,model,changes);
        }
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureMarginFlowActualSelectionHandsOffLocalHoldsToJumpsThenReversesRepeatsAndLiveReduceSettles(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var pixels=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-margin-flow-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await MarginAfterPrepare(f);var parts=MarginAfterRead(card);var title=MarginCardOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Actual Stats selection must propagate to the original Margin flow bridge before observing its own natural source clock.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedReference=FinalsPainted(f.Window,parts.Reference),fixedZero=FinalsPainted(f.Window,parts.Zero);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),bridgeBefore=parts.Bridge.Progress,boundBefore=parts.Glyph.Progress;bool clock=Motion.IsAnimating(card);
                    if(stage=="reduced")
                    {
                        MarginAfterPaintParts(parts,1);Assert.False(clock);Assert.Equal(1d,before,4);reducedFrames++;
                        LiveCardModelAuthority(f,"worm",false,model,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card),bridgeAfter=parts.Bridge.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridgeBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-bridgeAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,BridgeBefore=bridgeBefore,BoundBefore=boundBefore,
                        SourceAfter=after,BridgeAfter=bridgeAfter,BoundAfter=boundAfter,Coherent=coherent,
                        HoldOpacities=parts.Holds.Select(part=>part.Opacity).ToArray(),JumpOpacities=parts.Jumps.Select(part=>part.Opacity).ToArray(),
                        HoldScales=parts.HoldScales.Select(part=>part.ScaleX).ToArray(),JumpScales=parts.JumpScales.Select(part=>part.ScaleY).ToArray(),OutputOpen=PresWindowOpen(f)});
                    if(!coherent)return;coherentFrames++;MarginAfterPaintParts(parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));
                    Assert.Equal(fixedReference,FinalsPainted(f.Window,parts.Reference));Assert.Equal(fixedZero,FinalsPainted(f.Window,parts.Zero));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        LiveCardModelAuthority(f,"worm",false,model,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckWorm").Opacity);
                        if(after<.299)
                        {
                            early=true;Assert.All(parts.Holds,part=>Assert.True(part.Opacity>.15));Assert.All(parts.Jumps,part=>Assert.Equal(.10,part.Opacity,4));
                            image.Save(Path.Combine(directory,$"margin-flow-{policy}-{width}-holds.png"));
                        }
                        if(after>.601)
                        {
                            late=true;Assert.All(parts.Holds,part=>Assert.Equal(.85,part.Opacity,4));Assert.All(parts.Jumps,part=>Assert.True(part.Opacity>.10));
                            image.Save(Path.Combine(directory,$"margin-flow-{policy}-{width}-jumps.png"));
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;stage="closing";PresWindowPointer(f,alternate);
                            LiveCardModelAuthority(f,"stats",false,model,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckWorm").Opacity);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;LiveCardModelAuthority(f,"stats",false,model,changes);reopenFrom=after;stage="reopening";
                        PresWindowPointer(f,card);LiveCardModelAuthority(f,"worm",false,model,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));LiveCardModelAuthority(f,"worm",false,model,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            LiveCardModelAuthority(f,stage=="closing"?"stats":"worm",false,model,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Need coherent actual early hold/later jump phases, physical reverse/reopen and stable live Reduce. A missed natural phase is not replaced by a synthetic progress pose or late fallback.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            MarginAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));
            probing=true;try{MarginAfterPerPartPaint(f,parts,directory,$"margin-flow-{policy}-{width}",pixels);}finally{probing=false;}
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresPlanProvider(card).Invoke();await Settle(180);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.True(error is null,error);LiveCardModelAuthority(f,"worm",false,model,changes);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"margin-flow-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,Frames=frames,PerPartPixels=pixels});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureMarginFlowKeyboardDirectRefreshHideDisabledChoiceAndOwnerCloseKeepCurrentAuthority(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,closed=false;int shownFrames=0;string stage="setup";string? error=null;
        var frames=new List<object>();string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-margin-flow-lifetime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await MarginAfterPrepare(f);var parts=MarginAfterRead(card);using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await MarginDirectVisualRefreshContract(f,card,parts,model,changes);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);await PresPlanReveal(f,card);Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"worm",false,model,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==1,"Actual keyboard endpoint must propagate through the ordinary bridge.");MarginAfterPaintParts(parts,1);
            Assert.True(alternate.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            LiveCardModelAuthority(f,"stats",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==0,"Keyboard Enter choosing the alternate must settle this card's deselected endpoint immediately.");
            using(var image=f.Window.CaptureRenderedFrame())Assert.NotNull(image);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card);bool clock=Motion.IsAnimating(card);
                    if(stage=="shown")
                    {
                        Assert.Equal(1d,before,4);Assert.False(clock);
                        using var shown=f.Window.CaptureRenderedFrame();Assert.NotNull(shown);
                        bool settled=Math.Abs(OperatorChoiceMotion.GetProgress(card)-1)<.00001&&Math.Abs(parts.Bridge.Progress-1)<.00001&&Math.Abs(parts.Glyph.Progress-1)<.00001;
                        frames.Add(new{stage,Source=before,Bound=parts.Glyph.Progress,Settled=settled,Clock=clock});
                        if(!settled)return;
                        shownFrames++;MarginAfterPaintParts(parts,1);LiveCardModelAuthority(f,"worm",false,model,changes);return;
                    }
                    if((stage!="hide-flight"&&stage!="close-flight")||!clock||before<=.01||before>=.99)return;
                    double bound=parts.Glyph.Progress,bridge=parts.Bridge.Progress;using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bound)<.00001&&Math.Abs(before-bridge)<.00001
                        &&Math.Abs(after-parts.Glyph.Progress)<.00001&&Math.Abs(after-parts.Bridge.Progress)<.00001;
                    frames.Add(new{stage,SourceBefore=before,SourceAfter=after,BoundBefore=bound,BridgeBefore=bridge,BoundAfter=parts.Glyph.Progress,Coherent=coherent,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled});
                    if(!coherent)return;MarginAfterPaintParts(parts,after);LiveCardModelAuthority(f,"worm",false,model,changes);
                    image.Save(Path.Combine(directory,$"margin-flow-{width}-{stage}-actual-frame.png"));
                    if(stage=="hide-flight")
                    {
                        accepted=true;f.Window.Hide();Assert.False(f.Window.IsVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
                        hidden=true;stage="hidden";
                    }
                    else
                    {
                        f.Window.Close();closed=true;stage="closed";Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);LiveCardModelAuthority(f,"worm",false,model,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual owner Hide must interrupt a coherent captured selection flight.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Show must expose stable current selection without a queued old arrival replay.");Assert.True(error is null,error);
            // IsEnabled is an explicit controlled public lifecycle stimulus.
            stage="disabled";card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,alternate);LiveCardModelAuthority(f,"stats",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled card shows the current deselected endpoint.");MarginAfterPaintParts(parts,0);Assert.False(Motion.IsAnimating(card));
            stage="close-flight";PresWindowPointer(f,card);await PresWindowObserve(()=>closed||error is not null,"Actual owner Close must retire a newly observed captured selection clock.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
            Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"margin-flow-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Closed=closed,ShownFrames=shownFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
