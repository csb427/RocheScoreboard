// AFTER-ONLY FUTURE-WINDOW-GLYPH DRAFT:2cases, UNCOMPILED/UNRUN.
// Requires the separately reviewed Window visual patch. Never part of before baseline.
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
    [AvaloniaTheory][InlineData("Expressive")][InlineData("Calm")]
    public async Task FutureWindowGlyphUsesActualThumbBindingAndDistinctCoherentRenderedPhasesThenReverseAndReduce(string policy)
    {
        await using var f=await AflFixture.Create(420,320);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;EventHandler? observer=null;
        var trace=new List<object>();var watch=Stopwatch.StartNew();
        bool probing=false,early=false,late=false,departure=false,acceptedOpen=false;int reducedFrames=0,coherentFrames=0;
        double reducedAt=0;string stage="setup";string? error=null;
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            var provider=PresPlanProvider(card);
            if(!PresWindowOpen(f))provider.Invoke();await Until(()=>PresWindowOpen(f),"Warm the real output before observing short natural glyph phases.");
            using(var image=f.Window.CaptureRenderedFrame())Assert.NotNull(image);
            using(var image=PresPlanRead<Window>(f,"_displayWindow").CaptureRenderedFrame())Assert.NotNull(image);
            provider.Invoke();await Until(()=>!PresWindowOpen(f),"Start observed input from actual closed state.");await PresPlanReveal(f,card);
            var glyph=Assert.Single(card.GetVisualDescendants().OfType<AflOutputWindowGlyph>());
            T Part<T>(string name) where T:Control=>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
            var pane=Part<Border>("OutputWindowOpenPane");var edge=Part<Border>("OutputWindowPaneEdge");
            var monitor=Part<Avalonia.Controls.Shapes.Path>("OutputWindowMonitorFrame");
            var paneTransforms=Assert.IsType<TransformGroup>(pane.RenderTransform);
            var paneScale=Assert.Single(paneTransforms.Children.OfType<ScaleTransform>());
            var paneTravel=Assert.Single(paneTransforms.Children.OfType<TranslateTransform>());
            var thumb=PresPlanThumb(f);Rect fixedCard=FinalsPainted(f.Window,card),fixedMonitor=FinalsPainted(f.Window,monitor);
            string model=Scores(f.Match),screen=PresPlanRead<string>(f,"_activeScreenKey");using var changes=new PresPlanChanges(f.Match);
            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-window-glyph-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            void Parts(double source)
            {
                double normalized=Math.Clamp(source/20,0,1),panePhase=Math.Clamp(normalized/.72,0,1),edgePhase=Math.Clamp((normalized-.72)/.28,0,1);
                Assert.Equal(source,glyph.ThumbPosition,4);
                Assert.Equal(.72*panePhase,pane.Opacity,4);Assert.Equal(.45+.55*panePhase,paneScale.ScaleY,4);
                Assert.Equal(2*(1-panePhase)*OperatorMotion.TravelScale,paneTravel.Y,4);
                Assert.Equal(.72*edgePhase,edge.Opacity,4);Assert.Equal(14d,edge.Bounds.Width);
                Assert.Equal(fixedCard,FinalsPainted(f.Window,card));Assert.Equal(fixedMonitor,FinalsPainted(f.Window,monitor));
                Assert.False(Motion.IsAnimating(glyph));Assert.False(Motion.IsAnimating(paneScale));Assert.False(Motion.IsAnimating(paneTravel));
            }
            // Endpoint paint proof supplements the later live binding/phase
            // witnesses: real pane pixels must disappear when only pane/edge
            // are hidden, with clocks disabled and actual output still open.
            provider.Invoke();await Until(()=>PresWindowOpen(f)&&glyph.ThumbPosition==20,"The actual open endpoint must propagate through its binding.");
            using(var normal=Mr5PaletteCapture(f.Window,Path.Combine(directory,$"window-glyph-{policy}-endpoint.png")))
            {
                var box=FinalsPainted(f.Window,pane);double savedPane=pane.Opacity,savedEdge=edge.Opacity;
                try
                {
                    pane.Opacity=edge.Opacity=0;
                    using var backing=Mr5PaletteCapture(f.Window,Path.Combine(directory,$"window-glyph-{policy}-endpoint-backing.png"));
                    double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;
                    int changed=0;
                    for(int y=Math.Max(0,(int)Math.Floor(box.Top*sy));y<Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));y++)
                        for(int x=Math.Max(0,(int)Math.Floor(box.Left*sx));x<Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));x++)
                        {
                            var a=normal.GetPixel(x,y);var b=backing.GetPixel(x,y);
                            if(Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)))>10)changed++;
                        }
                    Assert.True(changed>=40,"The actual pane must paint at least40 changed pixels inside its rendered bounds.");
                }
                finally{pane.Opacity=savedPane;edge.Opacity=savedEdge;}
            }
            provider.Invoke();await Until(()=>!PresWindowOpen(f)&&glyph.ThumbPosition==0,"Restore the actual closed endpoint before live motion.");
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            observer=(_,_)=>
            {
                if(probing||error is not null)return;
                probing=true;
                try
                {
                    double before=thumb.X,boundBefore=glyph.ThumbPosition;bool active=Motion.IsAnimating(thumb);
                    if(stage=="reduced")
                    {
                        Parts(0);Assert.False(active);reducedFrames++;
                        PresWindowAuthority(f,false,model,screen,changes);return;
                    }
                    if(!active||before<=.01||before>=19.99)return;
                    using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);
                    double after=thumb.X,boundAfter=glyph.ThumbPosition;
                    bool coherent=Math.Abs(before-after)<.00001&&Math.Abs(before-boundBefore)<.00001&&Math.Abs(after-boundAfter)<.00001;
                    trace.Add(new{stage,Milliseconds=watch.Elapsed.TotalMilliseconds,SourceBefore=before,BoundBefore=boundBefore,SourceAfter=after,BoundAfter=boundAfter,Coherent=coherent,Open=PresWindowOpen(f)});
                    // A binding/render phase that advanced during capture is
                    // diagnostic, not accepted as a coherent image witness.
                    if(!coherent)return;
                    coherentFrames++;Parts(after);
                    if(stage=="opening")
                    {
                        PresWindowAuthority(f,true,model,screen,changes);acceptedOpen=true;
                        if(after<14.39){early=true;Assert.Equal(0d,edge.Opacity);image.Save(Path.Combine(directory,$"window-glyph-{policy}-pane.png"));}
                        if(after>14.41){late=true;Assert.Equal(.72,pane.Opacity,4);Assert.Equal(1d,paneScale.ScaleY,4);Assert.True(edge.Opacity>0);image.Save(Path.Combine(directory,$"window-glyph-{policy}-edge.png"));}
                        if(early&&late)
                        {
                            stage="closing";PresWindowPointer(f,card);PresWindowAuthority(f,false,model,screen,changes);
                        }
                    }
                    else if(stage=="closing")
                    {
                        departure=true;PresWindowAuthority(f,false,model,screen,changes);
                        stage="reduced";reducedAt=watch.Elapsed.TotalMilliseconds;
                        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
                        Assert.Equal(0d,thumb.X,4);Assert.False(Motion.IsAnimating(thumb));
                        using var reduced=f.Window.CaptureRenderedFrame();Assert.NotNull(reduced);Parts(0);
                        reduced.Save(Path.Combine(directory,$"window-glyph-{policy}-reduce.png"));
                    }
                }
                catch(Exception failure){error=failure.ToString();}
                finally{probing=false;}
            };
            NativeRendering.Rendering+=observer;
            stage="opening";PresWindowPointer(f,card);PresWindowAuthority(f,stage=="opening",model,screen,changes);
            await PresWindowObserve(()=>error is not null||(departure&&reducedFrames>=12&&watch.Elapsed.TotalMilliseconds-reducedAt>=350),
                "Need coherent actual source-binding/render witnesses in both pane/edge phases, departure and live Reduce; no synthetic sampling or forced propagation.");
            Assert.True(error is null,error);Assert.True(acceptedOpen);Assert.True(early);Assert.True(late);Assert.True(departure);Assert.True(coherentFrames>=3);
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"window-glyph-after-only-{policy}",new{Error=error,AcceptedOpen=acceptedOpen,Early=early,Late=late,Departure=departure,CoherentFrames=coherentFrames,Frames=trace});
            OperatorAppearance.ConfigureMotionStyle(previousStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
