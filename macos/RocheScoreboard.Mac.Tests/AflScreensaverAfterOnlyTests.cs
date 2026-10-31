// OUTSIDE AFTER-ONLY DRAFT — 4cases, UNCOMPILED/UNRUN.
// Requires the separate Screensaver helper/glyph candidate. Original public
// activation guard is respected: actual output is explicitly opened first.
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
    private sealed record SaverAfterParts(AflScreensaverGlyph Glyph,OperatorChoiceSurface Bridge,
        Border Frame,Avalonia.Controls.Shapes.Ellipse Field,Border Center,Border Horizon,
        Border[] Ambient,ScaleTransform HorizonScale,TranslateTransform[] AmbientShift);
    private static SaverAfterParts SaverAfterRead(Control card)
    {
        var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflScreensaverGlyph>());
        T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),p=>p.Name==name);
        var horizon=Part<Border>("ScreensaverHorizonAccent");
        var ambient=Enumerable.Range(1,2).Select(i=>Part<Border>("ScreensaverAmbientMark"+i)).ToArray();
        return new(glyph,Part<OperatorChoiceSurface>("ScreensaverSelectionBridge"),Part<Border>("ScreensaverSceneFrame"),
            Part<Avalonia.Controls.Shapes.Ellipse>("ScreensaverVenueOval"),Part<Border>("ScreensaverVenueCenter"),horizon,ambient,
            Assert.IsType<ScaleTransform>(horizon.RenderTransform),ambient.Select(p=>Assert.IsType<TranslateTransform>(p.RenderTransform)).ToArray());
    }
    private static TextBlock SaverAfterTitle(Control card)=>Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(),text=>text.Name=="ScreensaverCardTitle");
    private static Rect SaverAfterLocalBounds(Control part,Control glyph)
    {
        var corners=new[]{new Point(),new Point(part.Bounds.Width,0),new Point(0,part.Bounds.Height),new Point(part.Bounds.Width,part.Bounds.Height)}
            .Select(p=>{var q=part.TranslatePoint(p,glyph);Assert.NotNull(q);return q.Value;}).ToArray();
        return new(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Max(p=>p.X)-corners.Min(p=>p.X),corners.Max(p=>p.Y)-corners.Min(p=>p.Y));
    }
    private static void SaverAfterPaint(SaverAfterParts p,double source)
    {
        double horizon=Math.Clamp(source/.65,0,1),ambient=Math.Clamp((source-.30)/.70,0,1);
        Assert.Equal(source,p.Bridge.Progress,4);Assert.Equal(source,p.Glyph.Progress,4);Assert.Equal(0d,p.Bridge.Opacity);
        Assert.Equal(29d,p.Glyph.Bounds.Width);Assert.Equal(29d,p.Glyph.Bounds.Height);Assert.True(p.Glyph.ClipToBounds);Assert.Equal(1d,p.Frame.Opacity);
        Assert.Equal(25d,p.Frame.Bounds.Width);Assert.Equal(23d,p.Frame.Bounds.Height);
        Assert.Equal(17d,p.Field.Bounds.Width);Assert.Equal(8d,p.Field.Bounds.Height);Assert.Equal(.72,p.Field.Opacity);
        Assert.Equal(1d,p.Center.Bounds.Width);Assert.Equal(5d,p.Center.Bounds.Height);Assert.Equal(.60,p.Center.Opacity);
        Assert.Equal(1-.25*OperatorMotion.TravelScale*(1-horizon),p.HorizonScale.ScaleX,4);Assert.Equal(1d,p.HorizonScale.ScaleY);
        Assert.Equal(.28+.62*horizon,p.Horizon.Opacity,4);
        Assert.Equal(17d,p.Horizon.Bounds.Width);
        Assert.Equal(1.4,p.Horizon.Height,3); // Authored DIP height is distinct from rounded allocation.
        double scale=TopLevel.GetTopLevel(p.Glyph)?.RenderScaling??1d;
        Assert.True(scale>0);
        Assert.Equal(p.Horizon.DesiredSize.Height,p.Horizon.Bounds.Height,3);
        Assert.InRange(p.Horizon.Bounds.Height,p.Horizon.Height,p.Horizon.Height+1d/scale);
        var horizonBounds=SaverAfterLocalBounds(p.Horizon,p.Glyph);
        Assert.Equal(17*p.HorizonScale.ScaleX,horizonBounds.Width,3);Assert.Equal(14.5,horizonBounds.Center.X,3);
        Rect clip=new(p.Glyph.Bounds.Size);
        foreach(var part in new Control[]{p.Frame,p.Field,p.Center,p.Horizon}.Concat(p.Ambient))
        {
            Assert.True(clip.Contains(SaverAfterLocalBounds(part,p.Glyph)),part.Name+" actual transformed paint allocation must fit the fixed29px glyph clip.");
            Assert.False(Motion.IsAnimating(part));Assert.False(part.Focusable);Assert.False(part.IsHitTestVisible);
        }
        for(int i=0;i<2;i++)
        {
            Assert.Equal(2d,p.Ambient[i].Bounds.Width);Assert.Equal(i==0?4d:5d,p.Ambient[i].Bounds.Height);
            Assert.Equal(.18+.67*ambient,p.Ambient[i].Opacity,4);
            Assert.Equal((i==0?-.9:.9)*OperatorMotion.TravelScale*(1-ambient),p.AmbientShift[i].Y,4);
            Assert.Equal(0d,p.AmbientShift[i].X);Assert.False(Motion.IsAnimating(p.AmbientShift[i]));
        }
        Assert.False(Motion.IsAnimating(p.HorizonScale));Assert.False(Motion.IsAnimating(p.Glyph));Assert.False(Motion.IsAnimating(p.Bridge));
        Assert.False(p.Glyph.Focusable);Assert.False(p.Glyph.IsHitTestVisible);Assert.Empty(p.Glyph.GetVisualDescendants().OfType<TextBlock>());
    }
    private static async Task<(Control Card,Control Alternate)> SaverAfterPrepare(AflFixture f)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
        Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var window=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,window);
        if(!PresWindowOpen(f))PresPlanProvider(window).Invoke();
        await Until(()=>PresWindowOpen(f)&&!PresPlanRead<bool>(f,"_outputActivationRequired"),"Screensaver flight requires original explicit output Open; never bypass its manual-activation guard.");
        var card=Named<Control>(f.Window,"PresCardScreensaver");var alternate=Named<Control>(f.Window,"PresCardQuarterBreak");
        await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="quarterbreak","The actual Quarter break action establishes distinct selection without a match-quarter change.");
        await PresPlanReveal(f,card);using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
        // Last-row neighbors at both current compact2/full4column layouts.
        Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,alternate)));
        Assert.All(FinalsProbes(FinalsPainted(f.Window,alternate)),p=>Assert.True(Within(alternate,f.Window.InputHitTest(p))));
        Assert.True(PresWindowOpen(f));return(card,alternate);
    }
    private static void SaverAfterAuthority(AflFixture f,string key,bool open,string model,PresPlanChanges changes,bool expectedClockVisible)
    {
        LiveCardModelAuthority(f,key,open,model,changes);
        // Actual open owner must retain its created view/clock. Off-air output
        // view absence is not an error and must never create another view.
        SaverInspectActualOutput(f,"authority-"+key,expectedClockVisible:expectedClockVisible);
        Assert.Equal(key=="screensaver"?1d:0d,Named<Border>(f.Window,"PresCheckScreensaver").Opacity);
        Assert.Equal(key=="screensaver"?"Selected":"Not selected",Avalonia.Automation.AutomationProperties.GetItemStatus(Named<Control>(f.Window,"PresCardScreensaver")));
    }
    private static void SaverAfterPerPartPaint(AflFixture f,SaverAfterParts p,string directory,string prefix,List<object> receipts)
    {
        // Isolate each authored part separately. A combined count must not hide
        // a missing ambient mark. Only paint opacity is temporarily changed.
        using var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-endpoint.png"));
        foreach(var (part,minimum) in new (Control Part,int Minimum)[]{(p.Frame,20),(p.Field,10),(p.Center,2),(p.Horizon,8),(p.Ambient[0],2),(p.Ambient[1],3)})
        {
            double opacity=part.Opacity;Rect box=FinalsPainted(f.Window,part);
            try
            {
                part.Opacity=0;using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,prefix+"-without-"+part.Name+".png"));
                Assert.Equal(normal.Width,backing.Width);Assert.Equal(normal.Height,backing.Height);
                double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;int changed=0;
                for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                    for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                    {var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;}
                receipts.Add(new{part.Name,Box=box,Pixels=changed,Minimum=minimum});Assert.True(changed>=minimum,part.Name+" must independently paint actual endpoint pixels.");
            }
            finally{part.Opacity=opacity;}
        }
    }

    [AvaloniaTheory][InlineData(420,320,"Expressive")][InlineData(1280,850,"Calm")]
    public async Task FutureScreensaverHorizonThenAmbientPartsFollowActualAcceptedSelectionReversalRepeatAndLiveReduce(int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;bool? oldOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,early=false,late=false,reversed=false,reopened=false;int coherentFrames=0,reducedFrames=0,repeatFrames=0;
        string stage="setup";string? error=null;double reverseFrom=0,reopenFrom=0,reducedAt=0;var watch=Stopwatch.StartNew();
        var frames=new List<object>();var pixels=new List<object>();
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-screensaver-after-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await SaverAfterPrepare(f);var p=SaverAfterRead(card);using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            bool expectedClockVisible=OperatorWorkflowPreferences.Load().OutputScreensaverShowClock;
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&p.Glyph.Progress==0,"Initial deselection must traverse the ordinary binding before a real flight.");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,SaverAfterTitle(card)),fixedFrame=FinalsPainted(f.Window,p.Frame),fixedField=FinalsPainted(f.Window,p.Field);
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.Null(OperatorMotion.AnimationOverride);Assert.True(OperatorMotion.Enabled);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card),bridge=p.Bridge.Progress,bound=p.Glyph.Progress;bool clock=Motion.IsAnimating(card);
                    if(stage=="reduced"||stage=="repeat")
                    {
                        Assert.Equal(1d,before,4);Assert.False(clock);using var settled=f.Window.CaptureRenderedFrame();Assert.NotNull(settled);
                        if(Math.Abs(p.Bridge.Progress-1)>.00001||Math.Abs(p.Glyph.Progress-1)>.00001)return;
                        SaverAfterPaint(p,1);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
                        frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,Source=before,Bridge=p.Bridge.Progress,Bound=p.Glyph.Progress,Clock=clock,HorizonScale=p.HorizonScale.ScaleX,HorizonOpacity=p.Horizon.Opacity,Ambient=p.Ambient.Select(x=>x.Opacity).ToArray(),OutputView=SaverInspectActualOutput(f,stage,expectedClockVisible:expectedClockVisible)});
                        if(stage=="reduced")reducedFrames++;else repeatFrames++;return;
                    }
                    if(!clock||before<=.001||before>=.999)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridge)<.00001&&Math.Abs(before-bound)<.00001
                        &&Math.Abs(after-p.Bridge.Progress)<.00001&&Math.Abs(after-p.Glyph.Progress)<.00001;
                    frames.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,SourceAfter=after,Bridge=bridge,Bound=bound,Coherent=coherent,
                        HorizonScale=p.HorizonScale.ScaleX,HorizonOpacity=p.Horizon.Opacity,AmbientOpacity=p.Ambient.Select(x=>x.Opacity).ToArray(),AmbientY=p.AmbientShift.Select(x=>x.Y).ToArray(),Open=PresWindowOpen(f),OutputView=SaverInspectActualOutput(f,stage,expectedClockVisible:expectedClockVisible)});
                    if(!coherent)return;coherentFrames++;SaverAfterPaint(p,after);
                    Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedTitle,FinalsPainted(f.Window,SaverAfterTitle(card)));
                    Assert.Equal(fixedFrame,FinalsPainted(f.Window,p.Frame));Assert.Equal(fixedField,FinalsPainted(f.Window,p.Field));
                    Assert.All(FinalsProbes(fixedCard),x=>Assert.True(Within(card,f.Window.InputHitTest(x))));
                    if(stage=="opening")
                    {
                        SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
                        if(after>.02&&after<.295)
                        {
                            early=true;Assert.True(p.Horizon.Opacity>.28);Assert.Equal(.18,p.Ambient[0].Opacity,4);Assert.Equal(.18,p.Ambient[1].Opacity,4);
                            image.Save(Path.Combine(directory,$"screensaver-{policy}-{width}-horizon-first.png"));
                        }
                        if(after>.67&&after<.99)
                        {
                            late=true;Assert.Equal(.90,p.Horizon.Opacity,4);Assert.Equal(1d,p.HorizonScale.ScaleX,4);Assert.True(p.Ambient[0].Opacity>.18);Assert.Equal(p.Ambient[0].Opacity,p.Ambient[1].Opacity,4);
                            image.Save(Path.Combine(directory,$"screensaver-{policy}-{width}-ambient-later.png"));
                        }
                        if(early&&late){reverseFrom=after;stage="closing";PresWindowPointer(f,alternate);SaverAfterAuthority(f,"quarterbreak",true,model,changes,expectedClockVisible);}
                    }
                    else if(stage=="closing"&&after<reverseFrom-.01)
                    {reversed=true;reopenFrom=after;stage="reopening";PresWindowPointer(f,card);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);}
                    else if(stage=="reopening"&&after>reopenFrom+.01)
                    {
                        reopened=true;stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.False(OperatorMotion.Enabled);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
                    }
                }
                catch(Exception failure){error=failure.ToString();}finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="opening";PresWindowPointer(f,card);
            SaverAfterAuthority(f,stage=="closing"?"quarterbreak":"screensaver",true,model,changes,expectedClockVisible);
            await PresWindowObserve(()=>error is not null||(reopened&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),"Need natural coherent early horizon/later ambient frames, actual neighbor-pointer reverse/reopen, then stable real live Reduce. No synthetic pose can replace a missed phase.");
            Assert.True(error is null,error);Assert.True(early&&late&&reversed&&reopened);Assert.True(coherentFrames>=4);
            await Until(()=>PresPlanRead<Control?>(f,"_outputScreensaver") is {IsEffectivelyVisible:true},"After accepted retargeting, observe the actual existing output transition separately from the short operator glyph clock.");
            pixels.Add(new{Stage="actual-output-after-motion",OutputView=SaverInspectActualOutput(f,"actual-output-after-motion",requireSelectedVisible:true,expectedClockVisible:expectedClockVisible)});
            probing=true;try{SaverAfterPaint(p,1);SaverAfterPerPartPaint(f,p,directory,$"screensaver-{policy}-{width}",pixels);}finally{probing=false;}
            // Current selection remains finite/settled when motion is reenabled
            // and the SAME original command is actually repeated by pointer.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);
            stage="repeat";PresWindowPointer(f,card);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
            await PresWindowObserve(()=>repeatFrames>=12||error is not null,"Same accepted pointer choice must not restart operator glyph choreography.");Assert.True(error is null,error);
            SaverAfterPaint(p,1);Assert.False(Motion.IsAnimating(card));
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"screensaver-after-phases-{policy}-{width}",new{Error=error,Early=early,Late=late,Reversed=reversed,Reopened=reopened,CoherentFrames=coherentFrames,ReducedFrames=reducedFrames,RepeatFrames=repeatFrames,Frames=frames,PerPartPixels=pixels});
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    public async Task FutureScreensaverKeyboardHideDisableAndOwnerCloseRetireOnlyCurrentAcceptedSelection(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;bool? oldOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;bool probing=false,hidden=false,closed=false,accepted=false;int shownFrames=0;string stage="setup";string? error=null;
        var frames=new List<object>();string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-screensaver-life-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var(card,alternate)=await SaverAfterPrepare(f);var p=SaverAfterRead(card);using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            bool expectedClockVisible=OperatorWorkflowPreferences.Load().OutputScreensaverShowClock;
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);Assert.True(OperatorMotion.Enabled);Assert.Null(OperatorMotion.AnimationOverride);
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);await PresPlanReveal(f,card);Key(f.Window,PhysicalKey.Space);
            SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>p.Glyph.Progress==1&&p.Bridge.Progress==1,"Keyboard selected endpoint follows the real binding.");SaverAfterPaint(p,1);
            await Until(()=>PresPlanRead<Control?>(f,"_outputScreensaver") is {IsEffectivelyVisible:true},"Keyboard acceptance and actual visible output are observed separately.");
            frames.Add(new{Stage="actual-output-keyboard-selected",OutputView=SaverInspectActualOutput(f,"actual-output-keyboard-selected",requireSelectedVisible:true,expectedClockVisible:expectedClockVisible)});
            Assert.True(alternate.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Enter);
            SaverAfterAuthority(f,"quarterbreak",true,model,changes,expectedClockVisible);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            await Until(()=>p.Glyph.Progress==0&&p.Bridge.Progress==0,"Keyboard alternate settles actual deselection, without closing output.");
            using(var rendered=f.Window.CaptureRenderedFrame())Assert.NotNull(rendered);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;probing=true;
                try
                {
                    double before=OperatorChoiceMotion.GetProgress(card);bool clock=Motion.IsAnimating(card);
                    if(stage=="shown")
                    {
                        Assert.Equal(1d,before,4);Assert.False(clock);using var shown=f.Window.CaptureRenderedFrame();Assert.NotNull(shown);
                        bool settled=Math.Abs(p.Bridge.Progress-1)<.00001&&Math.Abs(p.Glyph.Progress-1)<.00001;
                        frames.Add(new{stage,Source=before,Bridge=p.Bridge.Progress,Bound=p.Glyph.Progress,Settled=settled,Clock=clock,OutputView=SaverInspectActualOutput(f,stage,expectedClockVisible:expectedClockVisible)});
                        if(!settled)return;shownFrames++;SaverAfterPaint(p,1);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);return;
                    }
                    if((stage!="hide-flight"&&stage!="close-flight")||!clock||before<=.01||before>=.99)return;
                    double bridge=p.Bridge.Progress,bound=p.Glyph.Progress;using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=OperatorChoiceMotion.GetProgress(card);bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-bridge)<.00001&&Math.Abs(before-bound)<.00001
                        &&Math.Abs(after-p.Bridge.Progress)<.00001&&Math.Abs(after-p.Glyph.Progress)<.00001;
                    frames.Add(new{stage,SourceBefore=before,SourceAfter=after,Bridge=bridge,Bound=bound,Coherent=coherent,Clock=clock,OwnerVisible=f.Window.IsVisible,CardEnabled=card.IsEnabled,Open=PresWindowOpen(f),OutputView=SaverInspectActualOutput(f,stage,expectedClockVisible:expectedClockVisible)});
                    if(!coherent)return;SaverAfterPaint(p,after);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
                    image.Save(Path.Combine(directory,$"screensaver-{width}-{stage}-actual-frame.png"));
                    if(stage=="hide-flight")
                    {
                        accepted=true;f.Window.Hide();Assert.False(f.Window.IsVisible);Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));hidden=true;stage="hidden";
                        frames.Add(new{stage,OutputView=SaverInspectActualOutput(f,stage,expectedClockVisible:expectedClockVisible)});
                    }
                    else
                    {f.Window.Close();closed=true;stage="closed";Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));
                        frames.Add(new{stage,OutputView=SaverInspectActualOutput(f,stage,expectDisposed:true,expectedClockVisible:expectedClockVisible)});}
                }
                catch(Exception failure){error=failure.ToString();}finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;stage="hide-flight";PresWindowPointer(f,card);SaverAfterAuthority(f,"screensaver",true,model,changes,expectedClockVisible);
            await PresWindowObserve(()=>hidden||error is not null,"Public owner Hide must interrupt a captured active accepted Saver selection.");Assert.True(error is null,error);Assert.True(hidden&&accepted);
            stage="shown";f.Window.Show();await PresPlanReveal(f,card);
            await PresWindowObserve(()=>shownFrames>=12||error is not null,"Show must retain current selection and no stale replay.");Assert.True(error is null,error);
            // Explicit public lifecycle stimulus; it does not invoke private UI
            // code, change selection authority or bypass output activation.
            stage="disabled";card.IsEnabled=false;Assert.Equal(1d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            PresWindowPointer(f,alternate);SaverAfterAuthority(f,"quarterbreak",true,model,changes,expectedClockVisible);Assert.Equal(0d,OperatorChoiceMotion.GetProgress(card),4);Assert.False(Motion.IsAnimating(card));
            card.IsEnabled=true;await PresPlanReveal(f,card);await Until(()=>p.Bridge.Progress==0&&p.Glyph.Progress==0,"Reenabled original card must show current deselection.");SaverAfterPaint(p,0);
            stage="close-flight";PresWindowPointer(f,card);
            await PresWindowObserve(()=>closed||error is not null,"Actual public owner Close must retire a new captured natural selection clock.");Assert.True(error is null,error);Assert.True(closed);
            Assert.False(Motion.IsAnimating(card));Assert.False(OperatorChoiceMotion.GetEnabled(card));Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"screensaver-after-lifetime-{width}",new{Error=error,Hidden=hidden,Accepted=accepted,Closed=closed,ShownFrames=shownFrames,Frames=frames});
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;
        }
    }
}
