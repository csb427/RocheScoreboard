// Public completion-cue baseline. Real model Start/Tick; no private callback injection.
// The chosen steady cue preserves existing broadcast hold timing.
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[CollectionDefinition("Training output steady cue",DisableParallelization=true)]
public sealed class TrainingOutputSteadyCueCollection{}

[Collection("Training output steady cue")]
public sealed class TrainingOutputSteadyCueBaselineTests
{
    private static async Task Settle(int milliseconds=100)
    {await Task.Delay(milliseconds);Dispatcher.UIThread.RunJobs();}
    private static T Named<T>(TrainingScorebugControl view,string name) where T:Control
        =>Assert.IsType<T>(view.FindControl<T>(name));
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    [Trait("Evidence","TrainingPublicSteadyCompletionCue")]
    public async Task ARealIntervalFinishKeepsASteadyPublicCueWithoutDelayingModelAdvance(bool final,bool autoAdvance)
    {
        var session=new TrainingSession{AutoAdvance=autoAdvance};
        var first=new TrainingTimer("Finished interval",TimeSpan.Zero);
        var next=new TrainingTimer("Current next timer",TimeSpan.FromMinutes(2));
        session.AddTimer(first);if(!final)session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);
        var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};
        window.Show();
        try
        {
            await Settle(180);Assert.True(output.IsLoaded);
            var title=Named<TextBlock>(output,"TitleText");var time=Named<TextBlock>(output,"TimerText");
            var staged=Named<Grid>(output,"StagedTimerHost");var overlay=Named<Border>(output,"SessionCompleteOverlay");
            var root=Named<Grid>(output,"RootGrid");var gradient=Named<Border>(output,"GradientBg");
            var phaseClock=Stopwatch.StartNew();
            session.Start();session.Tick();
            Assert.True(first.HasFinished);Assert.Equal(1,session.ActiveIndex);
            if(final)
            {Assert.Null(session.Active);Assert.True(session.IsExhausted);Assert.False(session.IsRunning);Assert.Equal("SESSION COMPLETE",title.Text);}
            else
            {Assert.Same(next,session.Active);Assert.Equal(autoAdvance,session.IsRunning);Assert.True(staged.IsVisible);Assert.Equal("FINISHED INTERVAL",title.Text);}
            Assert.Equal("00:00",time.Text);
            // Allow only the initial finite arrival to settle. The remainder
            // sampled here lies well inside the existing7.5s/10s hold contract.
            await Settle(750);
            var samples=new List<object>();Color? color=null;double? opacity=null;
            for(int frameIndex=0;frameIndex<13;frameIndex++)
            {
                var current=Assert.IsType<SolidColorBrush>(root.Background).Color;
                color??=current;opacity??=gradient.Opacity;
                samples.Add(new{frameIndex,elapsedMilliseconds=phaseClock.Elapsed.TotalMilliseconds,color=current.ToString(),gradientOpacity=gradient.Opacity,overlayOpacity=overlay.Opacity,title=title.Text,time=time.Text,session.ActiveIndex,session.IsRunning});
                if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
                {
                    Directory.CreateDirectory(directory);string name=$"training-steady-final-{final}-auto-{autoAdvance}";
                    if(frameIndex%3==0){using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+$"-{frameIndex:D2}.png"));}
                    File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
                }
                // Current code is expected to fail these exact presentation
                // observations as its420ms full-background strobe changes them.
                Assert.Equal(color.Value,current);Assert.Equal(opacity.Value,gradient.Opacity);
                Assert.Equal(1,session.ActiveIndex);Assert.True(first.HasFinished);
                Assert.Equal("00:00",time.Text);
                if(final)
                {Assert.Null(session.Active);Assert.True(session.IsExhausted);Assert.False(session.IsRunning);Assert.Equal("SESSION COMPLETE",title.Text);Assert.Equal(1,overlay.Opacity);}
                else
                {Assert.Same(next,session.Active);Assert.Equal(autoAdvance,session.IsRunning);Assert.True(staged.IsVisible);Assert.Equal("FINISHED INTERVAL",title.Text);}
                await Settle(100);
            }
            // The relative stability check above rejects the current repeating
            // strobe first. These independent literals also reject deleting it
            // without supplying the chosen purposeful success treatment.
            Assert.Equal(Color.Parse("#102A21"),Assert.IsType<SolidColorBrush>(root.Background).Color);
            Assert.InRange(gradient.Opacity,.349,.351);
            if(!final)
            {
                var mark=output.FindControl<Control>("CompletionMark");Assert.NotNull(mark);
                Assert.True(mark.IsEffectivelyVisible);Assert.InRange(mark.Opacity,.99,1);
                Assert.True(mark.Bounds.Width>0);Assert.True(mark.Bounds.Height>0);
                Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Shape>(mark);
                var clip=Named<Canvas>(output,"TitleClip");
                var right=mark.TranslatePoint(new Point(mark.Bounds.Width,0),root);
                var clipLeft=clip.TranslatePoint(default,root);Assert.NotNull(right);Assert.NotNull(clipLeft);
                Assert.True(right.Value.X+2<=clipLeft.Value.X,"Completion mark belongs in existing title padding, without reducing or covering the name clip.");
                foreach(var point in new[]{new Point(),new Point(mark.Bounds.Width,0),new Point(0,mark.Bounds.Height),new Point(mark.Bounds.Width,mark.Bounds.Height)})
                {
                    var at=mark.TranslatePoint(point,root);Assert.NotNull(at);Assert.True(new Rect(root.Bounds.Size).Contains(at.Value));
                }
                Assert.Equal(1380,clip.Width);Assert.Equal(140,clip.Height);
            }
        }
        finally{window.Content=null;window.Close();await Settle(180);}
    }
    private static object? Field(TrainingScorebugControl output,string name)=>typeof(TrainingScorebugControl).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(output);
    private static void CueRetired(TrainingScorebugControl output)
    {
        var mark=Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Path>(output.FindControl<Control>("CompletionMark"));
        Assert.Equal(0,mark.Opacity);Assert.False(Motion.IsAnimating(mark));
        Assert.Equal(Color.Parse("#080C12"),Assert.IsType<SolidColorBrush>(Named<Grid>(output,"RootGrid").Background).Color);
        Assert.Equal(1,Named<Border>(output,"GradientBg").Opacity);
        foreach(string name in new[]{"_holdTimer","_sessionCompleteTimer","_promotionTimer"})Assert.Null(Field(output,name));
    }
    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task CompletionCheckDrawsOneFiniteStrokeAndResetRetiresIt(int width,int height)
    {
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished interval",TimeSpan.Zero);var next=new TrainingTimer("Next",TimeSpan.FromMinutes(2));session.AddTimer(first);session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};window.Show();
        var trace=new List<object>();var clock=Stopwatch.StartNew();
        void Capture(string phase,Avalonia.Controls.Shapes.Path mark)
        {
            trace.Add(new{phase,elapsedMilliseconds=clock.Elapsed.TotalMilliseconds,mark.StrokeDashOffset,mark.Opacity,mark.IsVisible,animating=Motion.IsAnimating(mark)});
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
            Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,$"completion-check-{width}-{phase}.png"));
            File.WriteAllText(Path.Combine(directory,$"completion-check-{width}.json"),JsonSerializer.Serialize(trace,new JsonSerializerOptions{WriteIndented=true}));
        }
        try
        {
            await Settle(180);var mark=Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Path>(output.FindControl<Control>("CompletionMark"));
            Assert.Equal(0,mark.Opacity);Assert.NotNull(mark.Data);Assert.True(mark.Data.Bounds.Width>0);Assert.True(mark.Data.Bounds.Height>0);
            session.Start();clock.Restart();session.Tick();
            Assert.Same(next,session.Active);Assert.Equal(1,session.ActiveIndex);Assert.False(session.IsRunning);
            double initial=mark.StrokeDashOffset;Assert.InRange(initial,5.5,6);Assert.True(Motion.IsAnimating(mark));Capture("initial",mark);
            await Settle(90);double mid=mark.StrokeDashOffset;Capture("mid",mark);Assert.InRange(mid,.001,initial-.001);
            await Settle(350);Capture("settled",mark);Assert.InRange(mark.StrokeDashOffset,-.001,.001);Assert.False(Motion.IsAnimating(mark));Assert.Equal(1,mark.Opacity);
            Assert.Same(next,session.Active);Assert.Equal(TimeSpan.FromMinutes(2),session.ActiveRemaining);
            session.Clear();CueRetired(output);Capture("reset",mark);await Settle(350);CueRetired(output);Assert.Empty(session.Timers);
        }
        finally{window.Content=null;window.Close();await Settle(180);}
    }
    [AvaloniaTheory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task CompletionPropertyObserverCannotDecorateOrPromoteAReplacement(bool onExit,bool rebind)
    {
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished interval",TimeSpan.Zero);var next=new TrainingTimer("Old next",TimeSpan.FromMinutes(2));session.AddTimer(first);session.AddTimer(next);
        var replacement=new TrainingSession();replacement.AddTimer(new TrainingTimer("Replacement",TimeSpan.FromMinutes(3)));
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};window.Show();
        int changes=0;Avalonia.Controls.Shapes.Path? mark=null;
        void Observe(object? sender,AvaloniaPropertyChangedEventArgs args)
        {
            if(args.Property!=Visual.OpacityProperty || mark is null || changes!=0)return;
            if(onExit ? mark.Opacity>=.99 : mark.Opacity<.99)return;
            changes++;if(rebind)output.Bind(replacement);else session.Clear();
        }
        try
        {
            await Settle(180);mark=Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Path>(output.FindControl<Control>("CompletionMark"));
            if(!onExit)mark.PropertyChanged+=Observe;
            session.Start();session.Tick();
            if(onExit)
            {
                Assert.True(mark.IsVisible);Assert.Same(next,session.Active);mark.PropertyChanged+=Observe;
                // Wait for the actual7.5-second hold callback to retire the cue;
                // never invoke Stop/Complete or force a timer/private handler.
                for(int attempt=0;attempt<110&&changes==0;attempt++)await Settle(100);
            }
            Assert.Equal(1,changes);CueRetired(output);
            if(rebind)
            {Assert.Same(replacement,Field(output,"_session"));Assert.Equal("REPLACEMENT",Named<TextBlock>(output,"TitleText").Text);Assert.Equal("03:00",Named<TextBlock>(output,"TimerText").Text);Assert.Same(next,session.Active);}
            else{Assert.Same(session,Field(output,"_session"));Assert.Empty(session.Timers);Assert.Equal("NO TIMERS",Named<TextBlock>(output,"TitleText").Text);Assert.Equal("00:00",Named<TextBlock>(output,"TimerText").Text);}
            await Settle(650);CueRetired(output);Assert.False(Named<Grid>(output,"StagedTimerHost").IsVisible);
            if(rebind){Assert.Equal("REPLACEMENT",Named<TextBlock>(output,"TitleText").Text);Assert.Equal("03:00",Named<TextBlock>(output,"TimerText").Text);Assert.False(replacement.IsRunning);}
        }
        finally{if(mark is not null)mark.PropertyChanged-=Observe;window.Content=null;window.Close();await Settle(180);}
    }

    [AvaloniaTheory]
    [InlineData("interval-hold")][InlineData("interval-promotion")][InlineData("session-hold")]
    public async Task ActualHiddenWindowRetiresItsOwnedCompletionBeforeReopening(string phase)
    {
        bool final=phase=="session-hold";
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished interval",TimeSpan.Zero);var next=new TrainingTimer("Next timer",TimeSpan.FromMinutes(2));
        session.AddTimer(first);if(!final)session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);
        var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};window.Show();
        try
        {
            await Settle(180);session.Start();session.Tick();
            Assert.True(first.HasFinished);Assert.Equal(1,session.ActiveIndex);
            if(phase=="interval-promotion")
            {
                for(int attempt=0;attempt<200&&Field(output,"_promotionTimer") is null;attempt++)await Settle(50);
                Assert.NotNull(Field(output,"_promotionTimer"));Assert.Null(Field(output,"_holdTimer"));
            }
            else await Settle(750);
            Assert.True((bool)Field(output,"_flashing")!);
            window.Hide();Assert.False(window.IsVisible);
            // Real host visibility change, not content removal/Unloaded injection.
            // Wait through the actual7.5s hold or10s final window (plus return).
            await Settle(phase=="interval-promotion"?800:final?10300:8000);
            Assert.False((bool)Field(output,"_flashing")!);CueRetired(output);
            window.Show();await Settle(650);
            Assert.True(window.IsVisible);Assert.Same(session,Field(output,"_session"));
            Assert.Equal(1,session.ActiveIndex);Assert.False(session.IsRunning);
            Assert.False((bool)Field(output,"_flashing")!);CueRetired(output);
            if(final){Assert.Null(session.Active);Assert.True(session.IsExhausted);}
            else{Assert.Same(next,session.Active);Assert.Equal("NEXT TIMER",Named<TextBlock>(output,"TitleText").Text);Assert.Equal("02:00",Named<TextBlock>(output,"TimerText").Text);}
        }
        finally{window.Content=null;window.Close();await Settle(180);}
    }

// OUTSIDE-REPOSITORY DRAFT: NOT compiled, run, or applied.
// Append to existing TrainingOutputSteadyCueBaselineTests, whose Field/Named/Settle/
// CueRetired helpers and imports are reused. No private callback/state injection.
// session-hold is intentionally absent: exhausted sessions have no long next title.
[AvaloniaTheory]
[InlineData("interval-hold")]
[InlineData("interval-promotion")]
[InlineData("ordinary-active")]
public async Task ALongCurrentTitleResumesAfterActualWindowHideWithoutRequiringUnload(string phase)
{
    const string longTitle = "A very long current training interval title that exceeds the entire display and must scroll so every word can be read";
    bool ordinary = phase == "ordinary-active";
    var session = new TrainingSession { AutoAdvance = false };
    var first = new TrainingTimer("Finished interval", TimeSpan.Zero);
    var current = new TrainingTimer(longTitle, TimeSpan.FromMinutes(20));
    if (!ordinary) session.AddTimer(first);
    session.AddTimer(current);
    var output = new TrainingScorebugControl();
    int loaded = 0, unloaded = 0;
    output.Loaded += (_, _) => loaded++;
    output.Unloaded += (_, _) => unloaded++;
    output.Bind(session);
    var window = new NativeWindow { Title = "Training Display", Width = 386, Height = 193, Content = output };
    var lifecycle = new List<object>();
    var elapsed = Stopwatch.StartNew();
    var title = Named<TextBlock>(output, "TitleText");
    var clip = Named<Canvas>(output, "TitleClip");
    var travel = Assert.IsType<TranslateTransform>(title.RenderTransform);
    void Record(string point) => lifecycle.Add(new
    {
        point,
        elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
        loaded,
        unloaded,
        windowVisible = window.IsVisible,
        outputLoaded = output.IsLoaded,
        outputVisible = output.IsVisible,
        title = title.Text,
        desiredWidth = title.DesiredSize.Width,
        clipWidth = clip.Bounds.Width,
        titleAnimating = Motion.IsAnimating(travel),
        titleTravel = travel.X,
        flashing = (bool)Field(output, "_flashing")!,
        holdExists = Field(output, "_holdTimer") is not null,
        promotionExists = Field(output, "_promotionTimer") is not null,
        finalExists = Field(output, "_sessionCompleteTimer") is not null,
        session.ActiveIndex,
        session.IsRunning,
        remaining = session.ActiveRemaining.ToString()
    });
    window.Show();
    try
    {
        await Settle(180);
        Assert.True(output.IsLoaded);
        Assert.True(loaded > 0);
        if (ordinary)
        {
            title.Measure(Size.Infinity);
            Assert.Equal(longTitle.ToUpperInvariant(), title.Text);
            Assert.True(title.DesiredSize.Width > clip.Bounds.Width - 8);
            Assert.True(Motion.IsAnimating(travel));
        }
        else
        {
            session.Start();
            session.Tick();
            Assert.True(first.HasFinished);
            Assert.Same(current, session.Active);
            Assert.Equal(1, session.ActiveIndex);
            Assert.False(session.IsRunning);
            Assert.True((bool)Field(output, "_flashing")!);
            if (phase == "interval-promotion")
            {
                for (int attempt = 0; attempt < 200 && Field(output, "_promotionTimer") is null; attempt++)
                    await Settle(50);
                Assert.NotNull(Field(output, "_promotionTimer"));
                Assert.Null(Field(output, "_holdTimer"));
            }
            else
            {
                await Settle(750);
                Assert.NotNull(Field(output, "_holdTimer"));
                Assert.Null(Field(output, "_promotionTimer"));
            }
        }
        int beforeHideLoaded = loaded, beforeHideUnloaded = unloaded;
        Record("before-hide");
        window.Hide();
        Assert.False(window.IsVisible);
        Record("after-hide");
        // Actual wall-time expiry; no forced Tick/Complete/handler call.
        await Settle(ordinary ? 450 : phase == "interval-promotion" ? 800 : 8000);
        Record("hidden-after-expiry");
        Assert.False((bool)Field(output, "_flashing")!);
        CueRetired(output);
        Assert.Same(current, session.Active);
        Assert.Equal(ordinary ? 0 : 1, session.ActiveIndex);
        Assert.False(session.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(20), session.ActiveRemaining);
        window.Show();
        await Settle(650);
        title.Measure(Size.Infinity);
        Record("reopened");
        Assert.True(window.IsVisible);
        Assert.True(output.IsLoaded);
        // We record rather than assume whether this platform unloads on Hide.
        Assert.True(loaded >= beforeHideLoaded);
        Assert.True(unloaded >= beforeHideUnloaded);
        Assert.Equal(longTitle.ToUpperInvariant(), title.Text);
        Assert.True(title.DesiredSize.Width > clip.Bounds.Width - 8);
        Assert.True(Motion.IsAnimating(travel));
        Assert.Same(current, session.Active);
        Assert.Equal(ordinary ? 0 : 1, session.ActiveIndex);
        Assert.False(session.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(20), session.ActiveRemaining);
        CueRetired(output);
        await Settle(400);
        Record("reopened-motion-frame");
        Assert.True(travel.X < -.001, "The restored overflow clock must actually move the visible title; presence alone is insufficient.");
        Assert.Equal(longTitle.ToUpperInvariant(), title.Text);
        Assert.False(session.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(20), session.ActiveRemaining);
    }
    finally
    {
        try
        {
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "training-title-hide-" + phase + ".json"),
                    JsonSerializer.Serialize(lifecycle, new JsonSerializerOptions { WriteIndented = true }));
                if (window.IsVisible)
                {
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(directory, "training-title-hide-" + phase + ".png"));
                }
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
            await Settle(180);
        }
    }
}

}
