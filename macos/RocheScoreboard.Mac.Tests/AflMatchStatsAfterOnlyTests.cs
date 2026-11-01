// OUTSIDE AFTER-ONLY MATCH STATS DRAFT:2 coordinated-pair/reversal/Reduce +2 keyboard/lifetime.
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
    private sealed record MatchStatsAfterParts(AflMatchStatsGlyph Glyph,OperatorChoiceSurface Bridge,Control Spine,Control Brace,
        Border[] Keys,Border[] Left,Border[] Right,TranslateTransform BraceArrival,TranslateTransform[] LeftArrival,TranslateTransform[] RightArrival);
    private static MatchStatsAfterParts MatchStatsAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflMatchStatsGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var keys=Enumerable.Range(1,3).Select(i=>Part<Border>("StatsMetricKey"+i)).ToArray();
        var left=Enumerable.Range(1,3).Select(i=>Part<Border>("StatsLeftMetricRail"+i)).ToArray();
        var right=Enumerable.Range(1,3).Select(i=>Part<Border>("StatsRightMetricRail"+i)).ToArray();
        var brace=Part<Avalonia.Controls.Shapes.Path>("StatsComparisonBrace");
        return new(glyph,Part<OperatorChoiceSurface>("MatchStatsSelectionBridge"),Part<Avalonia.Controls.Shapes.Path>("StatsMetricSpine"),brace,keys,left,right,
            Assert.IsType<TranslateTransform>(brace.RenderTransform),left.Select(part=>Assert.IsType<TranslateTransform>(part.RenderTransform)).ToArray(),
            right.Select(part=>Assert.IsType<TranslateTransform>(part.RenderTransform)).ToArray());
    }
    private static void MatchStatsAfterPaintParts(MatchStatsAfterParts p,double source)
    {
        double header=Math.Clamp(source/.38,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(.65,p.Spine.Opacity);Assert.Equal(.65+.35*header,p.Brace.Opacity,4);
        Assert.Equal(-2.4*OperatorMotion.TravelScale*(1-header),p.BraceArrival.Y,4);
        Assert.Equal(29d,p.Glyph.Bounds.Width);Assert.Equal(29d,p.Glyph.Bounds.Height);
        for(int i=0;i<3;i++)
        {
            double phase=Math.Clamp((source-(.10+i*.14))/.62,0,1);
            Assert.Equal(.85,p.Keys[i].Opacity);Assert.Equal(2d,p.Keys[i].Width);Assert.Equal(1.4,p.Keys[i].Height,3);
            Assert.Equal(.60+.35*phase,p.Left[i].Opacity,4);Assert.Equal(p.Left[i].Opacity,p.Right[i].Opacity,4);
            Assert.Equal(-3.0*OperatorMotion.TravelScale*(1-phase),p.LeftArrival[i].X,4);
            Assert.Equal(3.0*OperatorMotion.TravelScale*(1-phase),p.RightArrival[i].X,4);
            Assert.Equal(-p.LeftArrival[i].X,p.RightArrival[i].X,4);
            Assert.Equal(6d,p.Left[i].Bounds.Width);Assert.Equal(6d,p.Right[i].Bounds.Width);
            Assert.Equal(2d,p.Left[i].Bounds.Height);Assert.Equal(2d,p.Right[i].Bounds.Height);
            Assert.False(Motion.IsAnimating(p.Left[i]));Assert.False(Motion.IsAnimating(p.Right[i]));
            Assert.False(Motion.IsAnimating(p.LeftArrival[i]));Assert.False(Motion.IsAnimating(p.RightArrival[i]));
        }
        // Actual local painted bounds, not the empty29x29 allocation of the
        // translated Path. Stretch.None preserves the authored brace geometry;
        // include its stroke when checking its transformed containing clip.
        Rect LocalBounds(Control part,Rect local)
        {
            var points=new[]{local.TopLeft,local.TopRight,local.BottomLeft,local.BottomRight}
                .Select(point=>{var mapped=part.TranslatePoint(point,p.Glyph);Assert.NotNull(mapped);return mapped.Value;}).ToArray();
            return new Rect(points.Min(point=>point.X),points.Min(point=>point.Y),
                points.Max(point=>point.X)-points.Min(point=>point.X),points.Max(point=>point.Y)-points.Min(point=>point.Y));
        }
        Rect clip=new(0,0,p.Glyph.Bounds.Width,p.Glyph.Bounds.Height);
        var brace=Assert.IsType<Avalonia.Controls.Shapes.Path>(p.Brace);Assert.NotNull(brace.Data);
        Rect data=brace.Data.Bounds;double halfStroke=brace.StrokeThickness/2;
        Rect paintedBrace=new(data.X-halfStroke,data.Y-halfStroke,data.Width+2*halfStroke,data.Height+2*halfStroke);
        Assert.True(clip.Contains(LocalBounds(brace,paintedBrace)),"The actual header geometry including its stroke must remain inside the glyph at every observed pose and both endpoints.");
        Assert.InRange(p.BraceArrival.Y,-2.4*OperatorMotion.TravelScale,0d);
        for(int i=0;i<3;i++)
        {
            Assert.True(clip.Contains(LocalBounds(p.Left[i],new Rect(p.Left[i].Bounds.Size))));
            Assert.True(clip.Contains(LocalBounds(p.Right[i],new Rect(p.Right[i].Bounds.Size))));
            Assert.InRange(p.LeftArrival[i].X,-3.0*OperatorMotion.TravelScale,0d);
            Assert.InRange(p.RightArrival[i].X,0d,3.0*OperatorMotion.TravelScale);
        }
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));Assert.False(Motion.IsAnimating(p.Spine));Assert.False(Motion.IsAnimating(p.Brace));Assert.False(Motion.IsAnimating(p.BraceArrival));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);
        Assert.All(p.Glyph.GetVisualDescendants().OfType<Control>(),part=>Assert.False(part.Focusable));Assert.Empty(p.Glyph.GetVisualDescendants().OfType<TextBlock>());
    }
    private static Dictionary<string,string> MatchStatsExpectedMetrics()=>new()
    {
        ["StatsScrHomeShots"]="1",["StatsScrAwayShots"]="1",["StatsScrHomeAcc"]="100%",["StatsScrAwayAcc"]="0%",
        ["StatsScrHomeTime"]="0%",["StatsScrAwayTime"]="0%",["StatsScrHomeLead"]="6",["StatsScrAwayLead"]="–",
        ["ShotsBarLabel"]="SCORING SHOTS",["AccuracyBarLabel"]="ACCURACY",["TimeBarLabel"]="TIME IN FRONT",["LeadBarLabel"]="LARGEST LEAD"
    };
    private static void MatchStatsAfterDataAuthority(AflFixture f)
    {
        // Public named output field values only, no private Populate/Calculate.
        // When output is hidden this is cached-data, not actual visible-output proof.
        var stats=PresPlanRead<Control>(f,"_statsScreen");
        foreach(var metric in MatchStatsExpectedMetrics())Assert.Equal(metric.Value,Named<TextBlock>(stats,metric.Key).Text);
    }
    private static async Task<(Control Card,Control Alternate)> MatchStatsAfterPrepare(AflFixture f)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Observe Match stats selection separately from actual open output.");
        var card=Named<Control>(f.Window,"PresCardStats");var alternate=Named<Control>(f.Window,"PresCardWorm");
        await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="worm","Actual original Margin flow must establish a different presentation and warm the hidden output before the short Match stats clock.");
        await PresPlanReveal(f,card);
        using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);
        // Original Stats/Margin flow share their actual responsive row; prove this rather than
        // treating an offscreen semantic activation as a physical reverse.
        Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,alternate)));
        Assert.All(FinalsProbes(FinalsPainted(f.Window,alternate)),point=>Assert.True(Within(alternate,f.Window.InputHitTest(point))));
        return(card,alternate);
    }
    private static void MatchStatsAfterPerPairPaint(AflFixture f,MatchStatsAfterParts p,string directory,string prefix,List<object> receipts)
    {
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint.png"));
        int Count(Rect box,SkiaSharp.SKBitmap backing)
        {
            double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
            for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                {var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;}
            return changed;
        }
        double braceOpacity=p.Brace.Opacity;
        try
        {
            p.Brace.Opacity=0;using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-without-header.png"));
            int pixels=Count(FinalsPainted(f.Window,p.Brace),backing);receipts.Add(new{Part="header-brace",Pixels=pixels});Assert.True(pixels>=10);
        }
        finally{p.Brace.Opacity=braceOpacity;}
        for(int i=0;i<3;i++)
        {
            double leftOpacity=p.Left[i].Opacity,rightOpacity=p.Right[i].Opacity;
            try
            {
                p.Left[i].Opacity=p.Right[i].Opacity=0;
                using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-without-pair-"+(i+1)+".png"));
                int left=Count(FinalsPainted(f.Window,p.Left[i]),backing),right=Count(FinalsPainted(f.Window,p.Right[i]),backing);
                receipts.Add(new{Pair=i+1,LeftPixels=left,RightPixels=right,Combined=left+right});
                Assert.True(left>=4&&right>=4,"Each actual paired row must independently paint BOTH fixed-width sides; one visible side cannot satisfy a combined glyph count.");
            }
            finally{p.Left[i].Opacity=leftOpacity;p.Right[i].Opacity=rightOpacity;}
        }
    }
    private static async Task MatchStatsAfterActualOutput(AflFixture f,Control card,string model,PresPlanChanges changes,string directory,int width)
    {
        // Separate actual output verification AFTER motion witnesses; it cannot
        // rescue missing natural phases or masquerade as the source clock.
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(!PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>PresWindowOpen(f),"Original Window action must actually open the already selected Stats presentation.");
        await Until(()=>PresPlanRead<Control?>(f,"_statsScreen") is {IsEffectivelyVisible:true},"Current Stats must be actual visible output, not only a selected card.");
        MatchStatsAfterDataAuthority(f);LiveCardModelAuthority(f,"stats",true,model,changes);
        using(var frame=PresPlanRead<Window>(f,"_displayWindow").CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"match-stats-actual-output-{width}.png"));}
        PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Original Close must preserve the selected presentation and match.");
        await PresPlanReveal(f,card);LiveCardModelAuthority(f,"stats",false,model,changes);
    }
    private static async Task MatchStatsDirectVisualRefreshContract(AflFixture f,Control card,MatchStatsAfterParts parts,string model,PresPlanChanges changes)
    {
        // DIRECT UI REFRESH SEAM ONLY. No private setup/model/scoring call.
        Assert.False(OperatorMotion.Enabled);Assert.Equal("worm",PresPlanRead<string>(f,"_activeScreenKey"));
        var refresh=f.Window.GetType().GetMethod("UpdatePresentationCards",HiddenInstance);Assert.NotNull(refresh);
        foreach(bool requested in new[]{true,false})
        {
            refresh.Invoke(f.Window,new object[]{requested?"stats":"worm"});
            Assert.Equal("worm",PresPlanRead<string>(f,"_activeScreenKey"));
            Assert.Equal(requested,OperatorChoiceMotion.GetSelected(card));Assert.Equal(requested?1d:0d,OperatorChoiceMotion.GetProgress(card),4);
            Assert.Equal(requested?1d:0d,Named<Border>(f.Window,"PresCheckStats").Opacity);Assert.False(Motion.IsAnimating(card));
            Assert.Equal(requested?"Selected":"Not selected",Avalonia.Automation.AutomationProperties.GetItemStatus(card));
            await Settle(100);using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);MatchStatsAfterPaintParts(parts,requested?1:0);
            LiveCardModelAuthority(f,"worm",false,model,changes);
        }
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureMatchStatsActualSelectionCoordinatesHeaderAndPairedMetricRowsThenReversesRepeatsAndLiveReduceSettles(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var pixels=new List<object>();var watch=Stopwatch.StartNew();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-match-stats-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await MatchStatsAfterPrepare(f);var parts=MatchStatsAfterRead(card);var title=MatchStatsCardOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Actual Margin flow selection must propagate to the original Match stats bridge before observing its own natural source clock.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedSpine=FinalsPainted(f.Window,parts.Spine);
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
                        MatchStatsAfterPaintParts(parts,1);Assert.False(clock);Assert.Equal(1d,before,4);reducedFrames++;
                        LiveCardModelAuthority(f,"stats",false,model,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card),bridgeAfter=parts.Bridge.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridgeBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-bridgeAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,BridgeBefore=bridgeBefore,BoundBefore=boundBefore,
                        SourceAfter=after,BridgeAfter=bridgeAfter,BoundAfter=boundAfter,Coherent=coherent,
                        HeaderOpacity=parts.Brace.Opacity,HeaderY=parts.BraceArrival.Y,LeftOpacities=parts.Left.Select(part=>part.Opacity).ToArray(),RightOpacities=parts.Right.Select(part=>part.Opacity).ToArray(),
                        LeftX=parts.LeftArrival.Select(part=>part.X).ToArray(),RightX=parts.RightArrival.Select(part=>part.X).ToArray(),OutputOpen=PresWindowOpen(f)});
                    if(!coherent)return;coherentFrames++;MatchStatsAfterPaintParts(parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));
                    Assert.Equal(fixedSpine,FinalsPainted(f.Window,parts.Spine));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        LiveCardModelAuthority(f,"stats",false,model,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckStats").Opacity);
                        MatchStatsAfterDataAuthority(f);
                        if(after<.379)
                        {
                            early=true;Assert.True(parts.Brace.Opacity>.65);Assert.Equal(.60,parts.Left[2].Opacity,4);Assert.Equal(.60,parts.Right[2].Opacity,4);
                            image.Save(Path.Combine(directory,$"match-stats-{policy}-{width}-header-first.png"));
                        }
                        if(after>.721)
                        {
                            late=true;Assert.Equal(1d,parts.Brace.Opacity,4);Assert.Equal(.95,parts.Left[0].Opacity,4);Assert.Equal(.95,parts.Right[0].Opacity,4);
                            Assert.True(parts.Left[2].Opacity>.60);Assert.Equal(parts.Left[2].Opacity,parts.Right[2].Opacity,4);
                            image.Save(Path.Combine(directory,$"match-stats-{policy}-{width}-paired-rows.png"));
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;stage="closing";PresWindowPointer(f,alternate);
                            LiveCardModelAuthority(f,"worm",false,model,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckStats").Opacity);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;LiveCardModelAuthority(f,"worm",false,model,changes);reopenFrom=after;stage="reopening";
                        PresWindowPointer(f,card);LiveCardModelAuthority(f,"stats",false,model,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));LiveCardModelAuthority(f,"stats",false,model,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            LiveCardModelAuthority(f,stage=="closing"?"worm":"stats",false,model,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Need coherent actual header-first/later paired-row phases, physical reverse/reopen and stable live Reduce. A missed natural phase is not replaced by a synthetic progress pose or late fallback.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            MatchStatsAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));
            probing=true;try{MatchStatsAfterPerPairPaint(f,parts,directory,$"match-stats-{policy}-{width}",pixels);}finally{probing=false;}
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresPlanProvider(card).Invoke();await Settle(180);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.True(error is null,error);LiveCardModelAuthority(f,"stats",false,model,changes);MatchStatsAfterDataAuthority(f);
            NativeRendering.Rendering-=observer;observer=null;
            await MatchStatsAfterActualOutput(f,card,model,changes,directory,width);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"match-stats-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,Frames=frames,PerPairPixels=pixels});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureMatchStatsKeyboardDirectRefreshHideDisabledChoiceAndOwnerCloseKeepCurrentAuthority(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,closed=false;int shownFrames=0;string stage="setup";string? error=null;
        var frames=new List<object>();string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-match-stats-lifetime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await MatchStatsAfterPrepare(f);var parts=MatchStatsAfterRead(card);using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await MatchStatsDirectVisualRefreshContract(f,card,parts,model,changes);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);await PresPlanReveal(f,card);Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"stats",false,model,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==1,"Actual keyboard endpoint must propagate through the ordinary bridge.");MatchStatsAfterPaintParts(parts,1);MatchStatsAfterDataAuthority(f);
            Assert.True(alternate.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            LiveCardModelAuthority(f,"worm",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
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
                        shownFrames++;MatchStatsAfterPaintParts(parts,1);LiveCardModelAuthority(f,"stats",false,model,changes);return;
                    }
                    if((stage!="hide-flight"&&stage!="close-flight")||!clock||before<=.01||before>=.99)return;
                    double bound=parts.Glyph.Progress,bridge=parts.Bridge.Progress;using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bound)<.00001&&Math.Abs(before-bridge)<.00001
                        &&Math.Abs(after-parts.Glyph.Progress)<.00001&&Math.Abs(after-parts.Bridge.Progress)<.00001;
                    frames.Add(new{stage,SourceBefore=before,SourceAfter=after,BoundBefore=bound,BridgeBefore=bridge,BoundAfter=parts.Glyph.Progress,Coherent=coherent,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled});
                    if(!coherent)return;MatchStatsAfterPaintParts(parts,after);LiveCardModelAuthority(f,"stats",false,model,changes);
                    image.Save(Path.Combine(directory,$"match-stats-{width}-{stage}-actual-frame.png"));
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
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);LiveCardModelAuthority(f,"stats",false,model,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual owner Hide must interrupt a coherent captured selection flight.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Show must expose stable current selection without a queued old arrival replay.");Assert.True(error is null,error);
            // IsEnabled is an explicit controlled public lifecycle stimulus.
            stage="disabled";card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,alternate);LiveCardModelAuthority(f,"worm",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled card shows the current deselected endpoint.");MatchStatsAfterPaintParts(parts,0);Assert.False(Motion.IsAnimating(card));
            stage="close-flight";PresWindowPointer(f,card);await PresWindowObserve(()=>closed||error is not null,"Actual owner Close must retire a newly observed captured selection clock.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
            Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"match-stats-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Closed=closed,ShownFrames=shownFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
