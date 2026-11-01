// OUTSIDE AFTER-ONLY SUMMARY DRAFT: 2 staged/reversal/Reduce + 2 keyboard/lifetime cases.
// Reuses current Live fixture/model/readiness/capture helpers. UNAPPLIED/UNCOMPILED/UNRUN.
// No SampleAt/progress writes/fake clock; direct UI-only refresh seam is explicitly labeled.
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
    private sealed record SummaryAfterParts(AflQuarterSummaryGlyph Glyph,OperatorChoiceSurface Bridge,Border Page,
        Border Header,Border[] Rules,Border Totals,ScaleTransform HeaderScale,ScaleTransform[] RuleScales,ScaleTransform TotalsScale);
    private static SummaryAfterParts SummaryAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflQuarterSummaryGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var header=Part<Border>("SummaryHeaderUnderline");var totals=Part<Border>("SummaryTotalsUnderline");
        var rules=Enumerable.Range(1,3).Select(i=>Part<Border>("SummaryQuarterRule"+i)).ToArray();
        return new(glyph,Part<OperatorChoiceSurface>("QuarterSummarySelectionBridge"),Part<Border>("SummaryPageFrame"),header,rules,totals,
            Assert.IsType<ScaleTransform>(header.RenderTransform),rules.Select(r=>Assert.IsType<ScaleTransform>(r.RenderTransform)).ToArray(),Assert.IsType<ScaleTransform>(totals.RenderTransform));
    }
    private static void SummaryAfterPaintParts(SummaryAfterParts p,double source)
    {
        double header=Math.Clamp(source/.38,0,1),totals=Math.Clamp((source-.60)/.40,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(.55+.45*header,p.HeaderScale.ScaleX,4);Assert.Equal(.55+.45*header,p.Header.Opacity,4);
        Assert.Equal(.35+.65*totals,p.TotalsScale.ScaleX,4);Assert.Equal(.35+.65*totals,p.Totals.Opacity,4);
        for(int i=0;i<3;i++)
        {
            double quarter=Math.Clamp((source-(.16+i*.12))/.50,0,1);
            Assert.Equal(.55+.45*quarter,p.RuleScales[i].ScaleY,4);Assert.Equal(.35+.65*quarter,p.Rules[i].Opacity,4);
            Assert.Equal(10d,p.Rules[i].Bounds.Height);Assert.False(Motion.IsAnimating(p.Rules[i]));Assert.False(Motion.IsAnimating(p.RuleScales[i]));
        }
        Assert.Equal(18d,p.Header.Bounds.Width);Assert.Equal(18d,p.Totals.Bounds.Width);Assert.Equal(25d,p.Page.Bounds.Width);Assert.Equal(26d,p.Page.Bounds.Height);
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));Assert.False(Motion.IsAnimating(p.Header));Assert.False(Motion.IsAnimating(p.Totals));
        Assert.False(Motion.IsAnimating(p.HeaderScale));Assert.False(Motion.IsAnimating(p.TotalsScale));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);
        Assert.All(p.Glyph.GetVisualDescendants().OfType<Control>(),part=>Assert.False(part.Focusable));
    }
    private static async Task<(Control Card,Control Alternate)> SummaryAfterPrepare(AflFixture f)
    {
        // Existing current Live fixture prep already owns storage, output-off,
        // actual original controls and reachable first-row geometry.
        var(alternate,card)=await LiveAfterPrepare(f);
        PresPlanProvider(alternate).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","Actual Scorebug route must establish Summary deselection before observing its own clock.");
        await PresPlanReveal(f,card);
        using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
        Assert.All(FinalsProbes(FinalsPainted(f.Window,alternate)),point=>Assert.True(Within(alternate,f.Window.InputHitTest(point))));
        return(card,alternate);
    }
    private static void SummaryAfterVisibleParts(AflFixture f,SummaryAfterParts p,string directory,string prefix)
    {
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint.png"));
        Rect box=FinalsPainted(f.Window,p.Glyph);var parts=new[]{p.Header}.Concat(p.Rules).Append(p.Totals).ToArray();var opacities=parts.Select(part=>part.Opacity).ToArray();
        try
        {
            foreach(var part in parts)part.Opacity=0;
            using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint-backing.png"));
            double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
            for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                {var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;}
            Assert.True(changed>=20,"Real header/quarter-divider/totals parts must paint beyond the stationary page frame.");
        }
        finally{for(int i=0;i<parts.Length;i++)parts[i].Opacity=opacities[i];}
    }
    private static async Task SummaryDirectVisualRefreshContract(AflFixture f,Control card,SummaryAfterParts parts,string model,PresPlanChanges changes)
    {
        // Explicit DIRECT UI REFRESH SEAM, not public setup/resume. Only the
        // existing paint method is invoked; no scoring/model command privately.
        Assert.False(OperatorMotion.Enabled);Assert.Equal("scorebug",PresPlanRead<string>(f,"_activeScreenKey"));
        var refresh=f.Window.GetType().GetMethod("UpdatePresentationCards",HiddenInstance);Assert.NotNull(refresh);
        foreach(bool requested in new[]{true,false})
        {
            refresh.Invoke(f.Window,new object[]{requested?"summary":"scorebug"});
            Assert.Equal("scorebug",PresPlanRead<string>(f,"_activeScreenKey"));
            Assert.Equal(requested,OperatorChoiceMotion.GetSelected(card));Assert.Equal(requested?1d:0d,OperatorChoiceMotion.GetProgress(card),4);
            Assert.Equal(requested?1d:0d,Named<Border>(f.Window,"PresCheckSummary").Opacity);Assert.False(Motion.IsAnimating(card));
            await Settle(100);using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);SummaryAfterPaintParts(parts,requested?1:0);
            LiveCardModelAuthority(f,"scorebug",false,model,changes);
        }
    }
    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureQuarterSummaryActualSelectionDrivesStagedHeaderQuarterRulesAndTotalsThenReversalAndLiveReduce(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-summary-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var (card,alternate)=await SummaryAfterPrepare(f);var parts=SummaryAfterRead(card);var title=SummaryCardOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Initial logical scorebug selection must leave Summary deselected through the actual attached-progress bridge.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedMonitor=FinalsPainted(f.Window,parts.Page);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),surfaceBefore=parts.Bridge.Progress,boundBefore=parts.Glyph.Progress;
                    bool clock=Motion.IsAnimating(card);
                    if(stage=="reduced")
                    {
                        SummaryAfterPaintParts(parts,1);Assert.False(clock);Assert.Equal(1d,before,4);reducedFrames++;
                        LiveCardModelAuthority(f,"summary",false,model,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card),surfaceAfter=parts.Bridge.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-surfaceBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-surfaceAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,SurfaceBefore=surfaceBefore,BoundBefore=boundBefore,
                        SourceAfter=after,SurfaceAfter=surfaceAfter,BoundAfter=boundAfter,Coherent=coherent,
                        HeaderOpacity=parts.Header.Opacity,QuarterOpacities=parts.Rules.Select(rule=>rule.Opacity).ToArray(),TotalsOpacity=parts.Totals.Opacity,
                        HeaderScale=parts.HeaderScale.ScaleX,QuarterScales=parts.RuleScales.Select(rule=>rule.ScaleY).ToArray(),TotalsScale=parts.TotalsScale.ScaleX,OutputOpen=PresWindowOpen(f)});
                    if(!coherent)return;coherentFrames++;SummaryAfterPaintParts(parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));Assert.Equal(fixedMonitor,FinalsPainted(f.Window,parts.Page));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        LiveCardModelAuthority(f,"summary",false,model,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckSummary").Opacity);
                        if(after<.399)
                        {
                            early=true;Assert.True(parts.Glyph.HeaderPhase>parts.Glyph.QuarterPhase(2));Assert.Equal(.35,parts.Totals.Opacity,4);
                            image.Save(Path.Combine(directory,$"quarter-summary-{policy}-{width}-quarter-rules.png"));
                        }
                        if(after>.601)
                        {
                            late=true;Assert.Equal(1d,parts.Header.Opacity,4);Assert.True(parts.Rules[2].Opacity>.35);Assert.True(parts.Totals.Opacity>.35);
                            image.Save(Path.Combine(directory,$"quarter-summary-{policy}-{width}-totals.png"));
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;stage="closing";PresWindowPointer(f,alternate);
                            LiveCardModelAuthority(f,"scorebug",false,model,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckSummary").Opacity);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;LiveCardModelAuthority(f,"scorebug",false,model,changes);
                        reopenFrom=after;stage="reopening";PresWindowPointer(f,card);LiveCardModelAuthority(f,"summary",false,model,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;
                        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
                        LiveCardModelAuthority(f,"summary",false,model,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            // Our own nested observer can already have reversed the first input.
            LiveCardModelAuthority(f,stage=="closing"?"scorebug":"summary",false,model,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Require coherent naturally rendered early header/divider handoff, late totals handoff, physical reversal/reopen, then stable actual Reduce; no manufactured intermediate pose.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            SummaryAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));Capture(f.Window,$"quarter-summary-{policy}-{width}-reduced");
            // Real pixel difference proves these parts actually paint; bridge is invisible.
            probing=true;
            SummaryAfterVisibleParts(f,parts,directory,$"quarter-summary-{policy}-{width}");
            probing=false;
            // Same selected command is real semantic Invoke, intentionally
            // dispatcher-posted. No selection replay may appear in real frames.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresPlanProvider(card).Invoke();await Settle(180);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.True(error is null,error);LiveCardModelAuthority(f,"summary",false,model,changes);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"quarter-summary-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureQuarterSummaryKeyboardHideDisabledChoiceAndOwnerCloseSettleOnlyCurrentSelection(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,closed=false;string stage="setup";string? error=null;
        var frames=new List<object>();
        try
        {
            var (card,alternate)=await SummaryAfterPrepare(f);var parts=SummaryAfterRead(card);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await SummaryDirectVisualRefreshContract(f,card,parts,model,changes);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);Assert.True(card.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"summary",false,model,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==1,"Actual keyboard endpoint must traverse the ordinary binding.");SummaryAfterPaintParts(parts,1);
            Assert.True(alternate.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            LiveCardModelAuthority(f,"scorebug",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
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
                        LiveCardModelAuthority(f,"summary",false,model,changes);accepted=true;
                        f.Window.Hide();Assert.False(f.Window.IsVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
                        hidden=true;stage="hidden";
                    }
                    else if(stage=="close-flight"&&clock&&source>.01&&source<.99)
                    {
                        LiveCardModelAuthority(f,"summary",false,model,changes);f.Window.Close();closed=true;stage="closed";
                        Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);
            LiveCardModelAuthority(f,"summary",false,model,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual public owner Hide must interrupt a naturally active selection clock.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await Until(()=>parts.Glyph.Progress==1,"Show must render the current selected endpoint rather than replay old arrival.");SummaryAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));
            // Public IsEnabled is an explicit controlled lifecycle stimulus,
            // not a private application action or native accessibility claim.
            card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,alternate);LiveCardModelAuthority(f,"scorebug",false,model,changes);
            Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled original card must show its current deselected state.");
            SummaryAfterPaintParts(parts,0);Assert.False(Motion.IsAnimating(card));
            stage="close-flight";PresWindowPointer(f,card);
            await PresWindowObserve(()=>closed||error is not null,"Actual public owner Close must retire a naturally active new selection flight.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));
            Assert.False(OperatorChoiceMotion.GetEnabled(card));Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"quarter-summary-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Closed=closed,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
