// OUTSIDE AFTER-ONLY WEATHER DRAFT:2 actual sun-lift/ray/reversal/Reduce +2 keyboard/lifetime.
// Requires separate source candidate. UNCOMPILED/UNRUN. No SampleAt/progress writes.
// Actual original public control input only: no SampleAt, progress writes or private behavior calls.
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
    private sealed record WeatherAfterParts(AflWeatherGlyph Glyph,OperatorChoiceSurface Bridge,Border Sun,
        Avalonia.Controls.Shapes.Path Rays,Avalonia.Controls.Shapes.Path Cloud,TranslateTransform SunLift,TranslateTransform RayLift);
    private static WeatherAfterParts WeatherAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflWeatherGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
        var sun=Part<Border>("WeatherSchematicSun");var rays=Part<Avalonia.Controls.Shapes.Path>("WeatherSchematicRays");
        return new(glyph,Part<OperatorChoiceSurface>("WeatherSelectionBridge"),sun,rays,Part<Avalonia.Controls.Shapes.Path>("WeatherSchematicCloud"),
            Assert.IsType<TranslateTransform>(sun.RenderTransform),Assert.IsType<TranslateTransform>(rays.RenderTransform));
    }
    // Local authored path bounds plus half-stroke, NOT the empty29px Path allocation.
    private static Rect WeatherAfterPaintedLocal(Control part)
    {
        if(part is Avalonia.Controls.Shapes.Path path)
        {
            Assert.NotNull(path.Data);Assert.Equal(Stretch.None,path.Stretch);
            var data=path.Data.Bounds;double half=path.StrokeThickness/2;
            return new Rect(data.X-half,data.Y-half,data.Width+2*half,data.Height+2*half);
        }
        return new Rect(part.Bounds.Size);
    }
    private static Rect WeatherAfterMapBounds(Control part,Rect local,Visual ancestor)
    {
        var points=new[]{local.TopLeft,local.TopRight,local.BottomLeft,local.BottomRight}
            .Select(point=>{var mapped=part.TranslatePoint(point,ancestor);Assert.NotNull(mapped);return mapped.Value;}).ToArray();
        return new Rect(points.Min(point=>point.X),points.Min(point=>point.Y),
            points.Max(point=>point.X)-points.Min(point=>point.X),points.Max(point=>point.Y)-points.Min(point=>point.Y));
    }
    private static void WeatherAfterPaintParts(WeatherAfterParts p,double source)
    {
        double sun=Math.Clamp(source/.65,0,1),rays=Math.Clamp((source-.25)/.75,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(.65+.35*sun,p.Sun.Opacity,4);Assert.Equal(.30+.60*rays,p.Rays.Opacity,4);
        Assert.Equal(2*OperatorMotion.TravelScale*(1-sun),p.SunLift.Y,4);Assert.Equal(p.SunLift.Y,p.RayLift.Y,4);
        Assert.Equal(0d,p.SunLift.X);Assert.Equal(0d,p.RayLift.X);Assert.Equal(1d,p.Cloud.Opacity);
        Assert.Null(p.Cloud.RenderTransform);Assert.Equal(29d,p.Glyph.Bounds.Width);Assert.Equal(29d,p.Glyph.Bounds.Height);
        Assert.Equal(8d,p.Sun.Bounds.Width);Assert.Equal(8d,p.Sun.Bounds.Height);
        Assert.Equal(1.3,p.Rays.StrokeThickness,4);Assert.Equal(1.6,p.Cloud.StrokeThickness,4);
        Assert.Equal(new Rect(12,2,14,12),p.Rays.Data!.Bounds);Assert.Equal(new Rect(3.5,11,22,12),p.Cloud.Data!.Bounds);
        Assert.Equal(15d,Canvas.GetLeft(p.Sun));Assert.Equal(5d,Canvas.GetTop(p.Sun));Assert.Equal(new CornerRadius(4),p.Sun.CornerRadius);
        Assert.True(p.Glyph.ClipToBounds);
        Rect clip=new(0,0,29,29);
        foreach(var part in new Control[]{p.Sun,p.Rays,p.Cloud})
        {
            Assert.True(clip.Contains(WeatherAfterMapBounds(part,WeatherAfterPaintedLocal(part),p.Glyph)),
                "Every actual sun/ray/cloud geometry, including each Path half-stroke, must stay within29px at every coherent observed pose and endpoint.");
            Assert.False(Motion.IsAnimating(part));
        }
        Assert.InRange(p.SunLift.Y,0d,2*OperatorMotion.TravelScale);Assert.InRange(p.RayLift.Y,0d,2*OperatorMotion.TravelScale);
        Assert.False(Motion.IsAnimating(p.SunLift));Assert.False(Motion.IsAnimating(p.RayLift));
        Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);
        Assert.All(p.Glyph.GetVisualDescendants().OfType<Control>(),part=>Assert.False(part.Focusable));
        Assert.Empty(p.Glyph.GetVisualDescendants().OfType<TextBlock>());
    }
    private static WeatherLazySnapshotReceipt WeatherAfterDataAuthority(AflFixture f,bool actuallyVisible=false)
    {
        var lazy=WeatherOriginalLazySnapshot(f);
        var weather=PresPlanRead<Control>(f,"_weatherScreen");
        Assert.False(Named<Control>(weather,"WeatherDataPanel").IsVisible);
        Assert.False(Named<Control>(weather,"WeatherStatsPanel").IsVisible);
        Assert.True(Named<Control>(weather,"WeatherUnavailablePanel").IsVisible);
        Assert.Equal("WEATHER",Named<TextBlock>(weather,"LocationText").Text);
        if(actuallyVisible)
        {
            Assert.True(weather.IsEffectivelyVisible);
            Assert.True(Named<Control>(weather,"WeatherUnavailablePanel").IsEffectivelyVisible);
            var text=weather.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsEffectivelyVisible).Select(x=>x.Text).ToArray();
            Assert.Contains("WEATHER DATA UNAVAILABLE",text);Assert.Contains("SET A LOCATION OR CHECK THE CONNECTION",text);
        }
        Assert.Equal(6,f.Match.HomeTotal);Assert.Equal(1,f.Match.AwayTotal);Assert.Equal(2,f.Match.Events.Count);
        Assert.False(f.Match.ClockRunning);Assert.Equal(TimeSpan.Zero,f.Match.ElapsedInQuarter);
        return lazy;
    }
    private static bool WeatherAfterFullyHittable(AflFixture f,Control target)
    {
        var box=FinalsPainted(f.Window,target);
        return target.IsEffectivelyVisible&&target.IsEffectivelyEnabled&&box.Width>16&&box.Height>16
            &&new Rect(f.Window.ClientSize).Contains(box)
            &&FinalsProbes(box).All(point=>Within(target,f.Window.InputHitTest(point)));
    }
    private static object WeatherAfterInputContext(AflFixture f,Control target)=>new
    {
        target.Name,target.IsEffectivelyVisible,target.IsEffectivelyEnabled,
        Bounds=FinalsPainted(f.Window,target),FullHitSurface=WeatherAfterFullyHittable(f,target)
    };
    private static async Task<(Control Card,Control Alternate)> WeatherAfterPrepare(AflFixture f)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Observe Weather selection separately from actual open output.");
        var card=Named<Control>(f.Window,"PresCardWeather");var alternate=Named<Control>(f.Window,"PresCardQuarterBreak");
        await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="quarterbreak","Actual original Quarter break must establish a different presentation and warm the hidden output before the short Weather clock.");
        await PresPlanReveal(f,card);
        using(var frame=f.Window.CaptureRenderedFrame())Assert.NotNull(frame);
        // XAML order is Weather,Window / QuarterBreak,Screensaver in
        // the two-column420px layout. Only the wide case requires a physical
        // reverse to this alternate. Compact retarget uses its ORIGINAL
        // dispatcher-posted semantic provider, with actual visibility logged.
        Assert.True(WeatherAfterFullyHittable(f,card));
        if(f.Window.ClientSize.Width>420)Assert.True(WeatherAfterFullyHittable(f,alternate));
        return(card,alternate);
    }
    private static bool WeatherAfterIndependentPaint(AflFixture f,Control card,WeatherAfterParts p,string directory,string prefix,List<object> receipts)
    {
        double source=OperatorChoiceMotion.GetProgress(card);
        bool Coherent()=>Math.Abs(source-OperatorChoiceMotion.GetProgress(card))<.00001
            &&Math.Abs(source-p.Bridge.Progress)<.00001&&Math.Abs(source-p.Glyph.Progress)<.00001;
        if(!Coherent())return false;
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-normal.png"));
        if(!Coherent())return false;
        int Count(Rect box,SkiaSharp.SKBitmap backing)
        {
            double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
            for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                {var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;}
            return changed;
        }
        foreach(var(part,name,minimum) in new(Control,string,int)[]{(p.Sun,"sun",6),(p.Rays,"rays",6),(p.Cloud,"cloud",20)})
        {
            double opacity=part.Opacity;
            try
            {
                // Only temporarily conceal this decorative part to observe
                // its actual pixels. No source progress/style/score is written.
                part.Opacity=0;using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-without-"+name+".png"));
                if(!Coherent()){receipts.Add(new{prefix,Part=name,Coherent=false,Source=source});return false;}
                var box=WeatherAfterMapBounds(part,WeatherAfterPaintedLocal(part),f.Window);int count=Count(box,backing);
                receipts.Add(new{prefix,Part=name,Coherent=true,Source=source,Pixels=count,Minimum=minimum,PaintedBounds=box});
                Assert.True(count>=minimum,$"Actual {name} must independently paint inside its own geometry; a combined glyph count cannot prove this part.");
            }
            finally
            {
                // If capture itself allowed a source tick, discard its paint
                // receipt and restore the decorative output for CURRENT bound
                // progress, rather than leaving an old opacity behind. This
                // never changes source/bridge progress and is not an observed
                // pose used as motion evidence.
                double now=p.Glyph.Progress;
                part.Opacity=ReferenceEquals(part,p.Sun) ? .65+.35*Math.Clamp(now/.65,0,1)
                    : ReferenceEquals(part,p.Rays) ? .30+.60*Math.Clamp((now-.25)/.75,0,1):opacity;
            }
            if(!Coherent())return false;
        }
        return true;
    }
    private static async Task WeatherAfterActualOutput(AflFixture f,Control card,string model,PresPlanChanges changes,string directory,int width)
    {
        // Separate actual output verification AFTER motion witnesses; it cannot
        // rescue missing natural phases or masquerade as the source clock.
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(!PresWindowOpen(f))PresPlanProvider(window).Invoke();await Until(()=>PresWindowOpen(f),"Original Window action must actually open the already selected Weather presentation.");
        await Until(()=>PresPlanRead<Control?>(f,"_weatherScreen") is {IsEffectivelyVisible:true},"Current Weather must be actual visible output, not only a selected card.");
        var lazy=WeatherAfterDataAuthority(f,true);LiveCardModelAuthority(f,"weather",true,model,changes);
        Mr5FinalsWriteTrace($"weather-card-actual-unavailable-lazy-{width}",new{Stage="actual-visible-output",LazyWeather=lazy,UnavailablePanelVisible=true,
            Copy=new[]{"WEATHER DATA UNAVAILABLE","SET A LOCATION OR CHECK THE CONNECTION"}});
        using(var frame=PresPlanRead<Window>(f,"_displayWindow").CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"weather-card-actual-output-{width}.png"));}
        PresPlanProvider(window).Invoke();await Until(()=>!PresWindowOpen(f),"Original Close must preserve the selected presentation and match.");
        await PresPlanReveal(f,card);LiveCardModelAuthority(f,"weather",false,model,changes);
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureWeatherActualSelectionLiftsSunThenRaysWithLayoutHonestRetargetRepeatAndLiveReduce(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<object>();var pixels=new List<object>();var watch=Stopwatch.StartNew();
        var phaseCaptures = new List<(string Path, Avalonia.Media.Imaging.WriteableBitmap Frame)>();
        string stage="setup";string? error=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;
        int coherentFrames=0,reducedFrames=0;double reverseFrom=0,reopenFrom=0,reducedAt=0;bool semanticReverse=width<=420,reverseAccepted=false;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-weather-card-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await WeatherAfterPrepare(f);var parts=WeatherAfterRead(card);var title=WeatherCardOriginalTitle(card);
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,"Actual Quarter break selection must propagate to the original Weather bridge before observing its own natural source clock.");
            WeatherAfterPaintParts(parts,0);
            Assert.True(WeatherAfterIndependentPaint(f,card,parts,directory,$"weather-card-{policy}-{width}-unselected-endpoint",pixels));
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title),fixedCloud=WeatherAfterMapBounds(parts.Cloud,WeatherAfterPaintedLocal(parts.Cloud),f.Window);
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                Avalonia.Media.Imaging.WriteableBitmap? capturedFrame = null;
                bool retainedFrame = false;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),bridgeBefore=parts.Bridge.Progress,boundBefore=parts.Glyph.Progress;bool clock=Motion.IsAnimating(card);
                    if(stage=="reduced")
                    {
                        WeatherAfterPaintParts(parts,1);Assert.False(clock);Assert.Equal(1d,before,4);reducedFrames++;
                        LiveCardModelAuthority(f,"weather",false,model,changes);return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    capturedFrame=f.Window.CaptureRenderedFrame();Assert.NotNull(capturedFrame);
                    var image=capturedFrame;
                    double after=OperatorChoiceMotion.GetProgress(card),bridgeAfter=parts.Bridge.Progress,boundAfter=parts.Glyph.Progress;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridgeBefore)<.00001&&Math.Abs(before-boundBefore)<.00001
                        &&Math.Abs(after-bridgeAfter)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,BridgeBefore=bridgeBefore,BoundBefore=boundBefore,
                        SourceAfter=after,BridgeAfter=bridgeAfter,BoundAfter=boundAfter,Coherent=coherent,
                        SunOpacity=parts.Sun.Opacity,SunY=parts.SunLift.Y,RayOpacity=parts.Rays.Opacity,RayY=parts.RayLift.Y,
                        LazyWeather=WeatherOriginalLazySnapshot(f),CloudOpacity=parts.Cloud.Opacity,CloudBounds=WeatherAfterMapBounds(parts.Cloud,WeatherAfterPaintedLocal(parts.Cloud),f.Window),OutputOpen=PresWindowOpen(f),RetargetRoute=semanticReverse?"original-semantic-Invoke":"physical-pointer",Alternate=WeatherAfterInputContext(f,alternate)});
                    if(!coherent)return;coherentFrames++;WeatherAfterPaintParts(parts,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,title));
                    Assert.Equal(fixedCloud,WeatherAfterMapBounds(parts.Cloud,WeatherAfterPaintedLocal(parts.Cloud),f.Window));
                    Assert.All(FinalsProbes(fixedCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                    if(stage=="opening")
                    {
                        LiveCardModelAuthority(f,"weather",false,model,changes);Assert.Equal(1d,Named<Border>(f.Window,"PresCheckWeather").Opacity);
                        WeatherAfterDataAuthority(f);
                        if(!early&&after>.015&&after<.245)
                        {
                            Assert.True(parts.SunLift.Y>0);Assert.True(parts.Sun.Opacity>.65);Assert.Equal(.30,parts.Rays.Opacity,4);
                            // Retain this forced, coherent current render. Encode
                            // it after the live trajectory, not inside its clock.
                            phaseCaptures.Add((Path.Combine(directory,$"weather-card-{policy}-{width}-sun-first.png"),image));
                            retainedFrame=true;early=true;
                        }
                        if(early&&!late&&after>.68)
                        {
                            Assert.Equal(0d,parts.SunLift.Y,4);Assert.Equal(1d,parts.Sun.Opacity,4);Assert.True(parts.Rays.Opacity>.30);
                            // Retain this forced, coherent current render. Encode
                            // it after the live trajectory, not inside its clock.
                            phaseCaptures.Add((Path.Combine(directory,$"weather-card-{policy}-{width}-rays-later.png"),image));
                            retainedFrame=true;late=true;
                        }
                        if(early&&late)
                        {
                            reverseFrom=after;
                            if(semanticReverse)
                            {
                                stage="closing-pending-semantic";
                                frames.Add(new{Stage=stage,Route="original-semantic-Invoke",Weather=WeatherAfterInputContext(f,card),Alternate=WeatherAfterInputContext(f,alternate)});
                                PresPlanProvider(alternate).Invoke();
                                // Provider is dispatcher-posted. No immediate
                                // business acceptance claim or private call.
                            }
                            else
                            {
                                Assert.True(WeatherAfterFullyHittable(f,alternate));stage="closing";PresWindowPointer(f,alternate);reverseAccepted=true;
                                LiveCardModelAuthority(f,"quarterbreak",false,model,changes);Assert.Equal(0d,Named<Border>(f.Window,"PresCheckWeather").Opacity);
                            }
                        }
                    }
                    else if(stage=="closing-pending-semantic"&&PresPlanRead<string>(f,"_activeScreenKey")=="quarterbreak")
                    {
                        reverseAccepted=true;stage="closing";LiveCardModelAuthority(f,"quarterbreak",false,model,changes);
                        Assert.Equal(0d,Named<Border>(f.Window,"PresCheckWeather").Opacity);
                        frames.Add(new{Stage="semantic-retarget-accepted",Source=after,Route="original-semantic-Invoke",Alternate=WeatherAfterInputContext(f,alternate)});
                        if(after<reverseFrom-.01)
                        {
                            // This very coherent frame already witnesses the
                            // accepted reverse; do not require an extra frame
                            // merely to relabel the asynchronous input path.
                            reversed=true;reopenFrom=after;stage="reopening";
                            PresWindowPointer(f,card);LiveCardModelAuthority(f,"weather",false,model,changes);
                        }
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {
                        reversed=true;LiveCardModelAuthority(f,"quarterbreak",false,model,changes);reopenFrom=after;stage="reopening";
                        PresWindowPointer(f,card);LiveCardModelAuthority(f,"weather",false,model,changes);
                    }
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));LiveCardModelAuthority(f,"weather",false,model,changes);
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally
                {
                    if(!retainedFrame) capturedFrame?.Dispose();
                    probing=false;
                }
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            LiveCardModelAuthority(f,stage=="closing"?"quarterbreak":"weather",false,model,changes);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Need coherent actual sun-first/later-ray phases, independently painted parts, desktop physical OR compact original-semantic reverse, physical Weather reopen and stable live Reduce. A missed natural phase is not replaced by a synthetic progress pose or late fallback.");
            Assert.True(error is null,error);Assert.True(early);Assert.True(late);Assert.True(reverseAccepted);Assert.True(reversed);Assert.True(reopened);Assert.True(coherentFrames>=4);
            Assert.Equal(2,phaseCaptures.Count);
            WeatherAfterPaintParts(parts,1);Assert.False(Motion.IsAnimating(card));
            probing=true;try{Assert.True(WeatherAfterIndependentPaint(f,card,parts,directory,$"weather-card-{policy}-{width}-endpoint",pixels));}finally{probing=false;}
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            PresWindowPointer(f,card);LiveCardModelAuthority(f,"weather",false,model,changes);
            Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,card);await Settle(180);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            Assert.True(error is null,error);LiveCardModelAuthority(f,"weather",false,model,changes);WeatherAfterDataAuthority(f);
            NativeRendering.Rendering-=observer;observer=null;
            await WeatherAfterActualOutput(f,card,model,changes,directory,width);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            try
            {
                foreach(var capture in phaseCaptures) capture.Frame.Save(capture.Path);
            }
            finally
            {
                foreach(var capture in phaseCaptures) capture.Frame.Dispose();
                try
                {
                    Mr5FinalsWriteTrace($"weather-card-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,ReverseAccepted=reverseAccepted,RetargetRoute=semanticReverse?"original-semantic-Invoke":"physical-pointer",Reopened=reopened,CoherentFrames=coherentFrames,Frames=frames,PerPartPixels=pixels,PartMaskScope="Settled endpoints; two forced coherent phase renders retained and encoded after the live observer."});
                }
                finally
                {
                    OperatorAppearance.ConfigureMotionStyle(style);
                    OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);
                    OperatorMotion.AnimationOverride=animation;
                }
            }
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureWeatherKeyboardHideDisabledChoiceAndOwnerCloseKeepCurrentAuthority(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var style=OperatorAppearance.MotionStyle;bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,accepted=false,closed=false;int shownFrames=0;string stage="setup";string? error=null;
        var frames=new List<object>();string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-weather-card-lifetime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await WeatherAfterPrepare(f);var parts=WeatherAfterRead(card);using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);await PresPlanReveal(f,card);Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"weather",false,model,changes);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==1,"Actual keyboard endpoint must propagate through the ordinary bridge.");WeatherAfterPaintParts(parts,1);WeatherAfterDataAuthority(f);
            await PresPlanReveal(f,alternate);Assert.True(alternate.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            LiveCardModelAuthority(f,"quarterbreak",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>parts.Glyph.Progress==0,"Keyboard Enter choosing the alternate must settle this card's deselected endpoint immediately.");
            await PresPlanReveal(f,card);
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
                        frames.Add(new{stage,Source=before,Bound=parts.Glyph.Progress,Settled=settled,Clock=clock,LazyWeather=WeatherOriginalLazySnapshot(f)});
                        if(!settled)return;
                        shownFrames++;WeatherAfterPaintParts(parts,1);LiveCardModelAuthority(f,"weather",false,model,changes);return;
                    }
                    if((stage!="hide-flight"&&stage!="close-flight")||!clock||before<=.01||before>=.99)return;
                    double bound=parts.Glyph.Progress,bridge=parts.Bridge.Progress;using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bound)<.00001&&Math.Abs(before-bridge)<.00001
                        &&Math.Abs(after-parts.Glyph.Progress)<.00001&&Math.Abs(after-parts.Bridge.Progress)<.00001;
                    frames.Add(new{stage,SourceBefore=before,SourceAfter=after,BoundBefore=bound,BridgeBefore=bridge,BoundAfter=parts.Glyph.Progress,Coherent=coherent,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled,LazyWeather=WeatherOriginalLazySnapshot(f)});
                    if(!coherent)return;WeatherAfterPaintParts(parts,after);LiveCardModelAuthority(f,"weather",false,model,changes);
                    image.Save(Path.Combine(directory,$"weather-card-{width}-{stage}-actual-frame.png"));
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
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);LiveCardModelAuthority(f,"weather",false,model,changes);
            await PresWindowObserve(()=>hidden||error is not null,"Actual owner Hide must interrupt a coherent captured selection flight.");Assert.True(error is null,error);Assert.True(hidden);Assert.True(accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Show must expose stable current selection without a queued old arrival replay.");Assert.True(error is null,error);
            // IsEnabled is an explicit controlled public lifecycle stimulus.
            stage="disabled";card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            if(width<=420)
            {
                frames.Add(new{Stage="disabled-semantic-choice",Route="original-semantic-Invoke",Alternate=WeatherAfterInputContext(f,alternate)});
                PresPlanProvider(alternate).Invoke();
                await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="quarterbreak","Wait for the original offscreen-capable semantic action to be accepted; this is not a physical click.");
            }
            else
            {
                Assert.True(WeatherAfterFullyHittable(f,alternate));PresWindowPointer(f,alternate);
            }
            LiveCardModelAuthority(f,"quarterbreak",false,model,changes);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>parts.Glyph.Progress==0,"Re-enabled card shows the current deselected endpoint.");WeatherAfterPaintParts(parts,0);Assert.False(Motion.IsAnimating(card));
            stage="close-flight";PresWindowPointer(f,card);await PresWindowObserve(()=>closed||error is not null,"Actual owner Close must retire a newly observed captured selection clock.");
            Assert.True(error is null,error);Assert.True(closed);Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
            Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"weather-card-after-lifetime-{width}",new{Error=error,Accepted=accepted,Hidden=hidden,Closed=closed,ShownFrames=shownFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
