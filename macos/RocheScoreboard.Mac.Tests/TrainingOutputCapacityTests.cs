// Accepted-duration output capacity baseline. Public model-to-view route; no private callbacks.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingOutputCapacityBaselineTests
{
    private static async Task Settle(int milliseconds=180)
    { await Task.Delay(milliseconds);Dispatcher.UIThread.RunJobs(); }
    private static T Named<T>(TrainingScorebugControl view,string name) where T:Control
        =>Assert.IsType<T>(view.FindControl<T>(name));
    private static (TimeSpan Duration,string Expected) Value(string part)=>part switch
    {
        "under-hour"=>(TimeSpan.FromSeconds(3599),"59:59"),
        "hour-boundary"=>(TimeSpan.FromHours(1),"1:00:00"),
        // The actual Add/edit parser permits total seconds <= int.MaxValue;
        // this baseline exercises the accepted value through the public model,
        // not a claim that it has driven the operator's entry fields.
        "manual-parser-maximum"=>(TimeSpan.FromSeconds(int.MaxValue),"596523:14:07"),
        // TrainingTimer accepts any nonnegative TimeSpan. Do not silently
        // lower that domain or clamp the output merely to pass a layout test.
        "model-maximum"=>(TimeSpan.MaxValue,"256204778:48:05"),
        _=>throw new ArgumentOutOfRangeException(nameof(part))
    };
    private static string VisibleDuration(TrainingScorebugControl output,TimeSpan remaining,TimeSpan configured,bool checkLayout=true)
    {
        var primary=Named<TextBlock>(output,"TimerText");var hours=Named<TextBlock>(output,"TimerHoursText");
        var lane=Named<StackPanel>(output,"TimerHoursLane");var root=Named<Grid>(output,"RootGrid");
        bool showHours=configured>=TimeSpan.FromHours(1)||remaining>=TimeSpan.FromHours(1);
        Assert.Equal(showHours,lane.IsVisible);Assert.Equal(420,primary.FontSize);Assert.Equal(88,hours.FontSize);
        Assert.Equal($"{remaining.Minutes:D2}:{remaining.Seconds:D2}",primary.Text);
        Assert.Equal((remaining.Ticks/TimeSpan.TicksPerHour).ToString(System.Globalization.CultureInfo.InvariantCulture),hours.Text);
        if(showHours && checkLayout)
        {
            Assert.True(hours.IsEffectivelyVisible);
            var unit=Assert.Single(lane.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="HOURS");
            foreach(var part in new[]{hours,unit})
            {
                Assert.True(part.TextLayout.WidthIncludingTrailingWhitespace<=part.Bounds.Width,"Visible hours and unit glyphs must fit their actual regions.");
                Assert.DoesNotContain(part.TextLayout.TextLines,line=>line.HasCollapsed);
                foreach(var point in new[]{new Point(),new Point(part.Bounds.Width,0),new Point(0,part.Bounds.Height),new Point(part.Bounds.Width,part.Bounds.Height)})
                {
                    var translated=part.TranslatePoint(point,root);Assert.NotNull(translated);Assert.True(new Rect(root.Bounds.Size).Contains(translated.Value));
                }
            }
            var hourRight=hours.TranslatePoint(new Point(hours.Bounds.Width,0),root);var primaryLeft=primary.TranslatePoint(default,root);
            Assert.NotNull(hourRight);Assert.NotNull(primaryLeft);Assert.True(hourRight.Value.X+16<=primaryLeft.Value.X,"Hours must not overlap the primary clock.");
        }
        return showHours?hours.Text+":"+primary.Text:primary.Text!;
    }

    [AvaloniaTheory]
    [InlineData(386,193,"under-hour")][InlineData(386,193,"hour-boundary")]
    [InlineData(386,193,"manual-parser-maximum")][InlineData(386,193,"model-maximum")]
    [InlineData(960,540,"under-hour")][InlineData(960,540,"hour-boundary")]
    [InlineData(960,540,"manual-parser-maximum")][InlineData(960,540,"model-maximum")]
    [Trait("Evidence","TrainingAcceptedDurationCapacity")]
    public async Task AcceptedPausedDurationsRemainTruthfulAndTheirActualGlyphsFit(int width,int height,string part)
    {
        var session=new TrainingSession();
        var timer=new TrainingTimer("Capacity witness",TimeSpan.FromSeconds(3599));
        session.AddTimer(timer);
        var output=new TrainingScorebugControl();output.Bind(session);
        var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};
        window.Show();
        try
        {
            await Settle();
            Assert.True(output.IsLoaded);Assert.True(output.IsEffectivelyVisible);
            var number=Named<TextBlock>(output,"TimerText");var root=Named<Grid>(output,"RootGrid");
            Assert.True(number.Bounds.Width>0);Assert.True(root.Bounds.Width>0);
            Assert.Equal("59:59",number.Text);
            double originalFont=number.FontSize;
            var (duration,expected)=Value(part);
            session.UpdateTimer(timer,newTitle:null,newDuration:duration);
            // Immediate acceptance remains the model/readout contract. No
            // tweened intermediate value or animation-completion dependency.
            Assert.Same(timer,session.Active);Assert.Equal(0,session.ActiveIndex);
            Assert.Equal(duration,session.ActiveRemaining);Assert.False(session.IsRunning);
            Assert.False(timer.HasFinished);Assert.Single(session.Timers);
            Assert.Equal(expected,VisibleDuration(output,duration,duration,checkLayout:false));
            await Settle();output.UpdateLayout();
            Assert.Equal(originalFont,number.FontSize);
            Assert.Equal(expected,VisibleDuration(output,duration,duration));Assert.Equal(duration,session.ActiveRemaining);
            // Keeping the full underlying Text but drawing an ellipsis is not
            // a truthful visible-duration fix. Inspect actual formatted lines.
            Assert.Equal(TextTrimming.None,number.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap,number.TextWrapping);
            Assert.DoesNotContain(number.TextLayout.TextLines,line=>line.HasCollapsed);
            double glyphWidth=number.TextLayout.WidthIncludingTrailingWhitespace;
            var glyphQuad=new[]{new Point(),new Point(number.Bounds.Width,0),new Point(0,number.Bounds.Height),new Point(number.Bounds.Width,number.Bounds.Height)}
                .Select(point=>number.TranslatePoint(point,root)).ToArray();
            string? directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
            if(!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                string name=$"training-capacity-{width}-{part}";
                using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
                frame.Save(Path.Combine(directory,name+".png"));
                File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new
                {part,width,height,expected,actual=VisibleDuration(output,duration,duration),primary=number.Text,hours=Named<TextBlock>(output,"TimerHoursText").Text,hoursShown=Named<StackPanel>(output,"TimerHoursLane").IsVisible,model=duration.ToString("c"),originalFont,number.FontSize,font=number.FontFamily.ToString(),glyphWidth,number.Bounds,rootBounds=root.Bounds,glyphQuad,lines=number.TextLayout.TextLines.Select(line=>new{line.Length,line.HasCollapsed,line.WidthIncludingTrailingWhitespace}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
            }
            // A clipping diagnosis needs both real glyph metrics and the
            // transformed allocated region. Desired-size-only success is not enough.
            Assert.True(glyphWidth<=number.Bounds.Width,$"{expected} needs {glyphWidth}px but the actual number region has {number.Bounds.Width}px.");
            Assert.All(glyphQuad,point=>
            {
                Assert.NotNull(point);
                Assert.True(new Rect(root.Bounds.Size).Contains(point.Value),$"{expected} extends outside the actual clipped design canvas at {point}.");
            });
            Assert.Same(timer,session.Active);Assert.Equal(0,session.ActiveIndex);
            Assert.False(session.IsRunning);Assert.False(timer.HasFinished);Assert.Single(session.Timers);
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task HoursDigitGrowthKeepsFixedPrimaryAndCompleteHourGlyphs(int width,int height)
    {
        var session=new TrainingSession();var timer=new TrainingTimer("Duration growth",TimeSpan.FromHours(9));session.AddTimer(timer);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle();Point? origin=null;
            foreach(long hours in new long[]{9,10,99,100,99999999,100000000,256204778})
            {
                var duration=TimeSpan.FromTicks(checked(hours*TimeSpan.TicksPerHour+12*TimeSpan.TicksPerMinute+5*TimeSpan.TicksPerSecond));
                session.UpdateTimer(timer,null,duration);await Settle();output.UpdateLayout();
                Assert.Equal(hours+":12:05",VisibleDuration(output,duration,duration));
                var primary=Named<TextBlock>(output,"TimerText");var at=primary.TranslatePoint(default,Named<Grid>(output,"RootGrid"));Assert.NotNull(at);
                if(origin is {} before)Assert.Equal(before,at.Value);origin=at;
                Assert.Same(timer,session.Active);Assert.Equal(duration,session.ActiveRemaining);Assert.False(session.IsRunning);
            }
        }
        finally{window.Content=null;window.Close();await Settle();}
    }

    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task RealTickAcrossOneHourRetainsHoursLaneAndPrimaryOrigin(int width,int height)
    {
        var session=new TrainingSession();var timer=new TrainingTimer("Hour boundary",TimeSpan.FromHours(1));session.AddTimer(timer);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle();Assert.Equal("1:00:00",VisibleDuration(output,session.ActiveRemaining,timer.Duration));
            var primary=Named<TextBlock>(output,"TimerText");var root=Named<Grid>(output,"RootGrid");var before=primary.TranslatePoint(default,root);Assert.NotNull(before);
            session.Start();await Task.Delay(1150);session.Tick();session.Pause();await Settle();
            Assert.True(session.ActiveRemaining<TimeSpan.FromHours(1));Assert.InRange(session.ActiveRemaining.TotalSeconds,3580,3599.9999);
            string visible=VisibleDuration(output,session.ActiveRemaining,timer.Duration);Assert.StartsWith("0:59:",visible);
            Assert.Equal(before,primary.TranslatePoint(default,root));Assert.Same(timer,session.Active);Assert.False(timer.HasFinished);Assert.False(session.IsRunning);
            var elapsed=timer.Duration-session.ActiveRemaining;var corrected=TimeSpan.FromSeconds(3599);
            session.UpdateTimer(timer,null,corrected);await Settle();Assert.Equal(corrected-elapsed,session.ActiveRemaining);
            Assert.False(Named<StackPanel>(output,"TimerHoursLane").IsVisible);
            Assert.Equal($"{session.ActiveRemaining.Minutes:D2}:{session.ActiveRemaining.Seconds:D2}",VisibleDuration(output,session.ActiveRemaining,timer.Duration));
            session.Clear();await Settle();Assert.Null(session.Active);Assert.Equal("00:00",VisibleDuration(output,TimeSpan.Zero,TimeSpan.Zero));
        }
        finally{window.Content=null;window.Close();await Settle();}
    }

    private static void CaptureStrip(Window window,TrainingScorebugControl output,string prefix,int width)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,prefix+"-"+width+".png"));
    }
    private static void AssertStripPart(TrainingScorebugControl output,string name,bool requireFullText)
    {
        var part=Named<TextBlock>(output,name);var root=Named<Grid>(output,"RootGrid");Assert.True(part.IsEffectivelyVisible);
        Assert.True(part.Bounds.Width>0);Assert.True(part.Bounds.Height>0);
        if(requireFullText){Assert.True(part.TextLayout.WidthIncludingTrailingWhitespace<=part.Bounds.Width,name+" glyphs must fit.");Assert.DoesNotContain(part.TextLayout.TextLines,l=>l.HasCollapsed);}
        foreach(var point in new[]{new Point(),new Point(part.Bounds.Width,0),new Point(0,part.Bounds.Height),new Point(part.Bounds.Width,part.Bounds.Height)})
        {var translated=part.TranslatePoint(point,root);Assert.NotNull(translated);Assert.True(new Rect(root.Bounds.Size).Contains(translated.Value),name+" leaves the actual clipped output.");}
    }
    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task LongQueuedDurationSurvivesActualStagingAndPromotion(int width,int height)
    {
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished interval",TimeSpan.Zero);
        var next=new TrainingTimer("Long next interval",TimeSpan.MaxValue);session.AddTimer(first);session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle(600);CaptureStrip(window,output,"capacity-up-next",width);
            Assert.Equal("256204778:48:05",Named<TextBlock>(output,"UpNextDuration").Text);
            foreach(string part in new[]{"UpNextLabel","UpNextDuration","UpNextTitle"})AssertStripPart(output,part,part!="UpNextTitle");
            session.Start();session.Tick();Assert.True(first.HasFinished);Assert.Same(next,session.Active);Assert.False(session.IsRunning);
            await Settle(650);Assert.True(Named<Grid>(output,"StagedTimerHost").IsVisible);CaptureStrip(window,output,"capacity-staged",width);
            Assert.Equal("256204778:48:05",Named<TextBlock>(output,"StagedTimerText").Text);
            foreach(string part in new[]{"StagedLabel","StagedTimerText","StagedTitleText"})AssertStripPart(output,part,part!="StagedTitleText");
            for(int attempt=0;attempt<110&&Named<Grid>(output,"StagedTimerHost").IsVisible;attempt++)await Settle(100);
            Assert.False(Named<Grid>(output,"StagedTimerHost").IsVisible);
            for(int attempt=0;attempt<20 && Named<StackPanel>(output,"ActiveTimerHost").Opacity<.99;attempt++)await Settle(100);
            Assert.InRange(Named<StackPanel>(output,"ActiveTimerHost").Opacity,.99,1);
            Assert.Equal("256204778:48:05",VisibleDuration(output,session.ActiveRemaining,next.Duration));
            Assert.Same(next,session.Active);Assert.Equal(TimeSpan.MaxValue,session.ActiveRemaining);Assert.False(session.IsRunning);
            CaptureStrip(window,output,"capacity-promoted",width);
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task SkipAndRebindCannotLeaveOldHoursInShortReadout(int width,int height)
    {
        var session=new TrainingSession();var longTimer=new TrainingTimer("Long active",TimeSpan.FromHours(100));var shortTimer=new TrainingTimer("Short next",TimeSpan.FromMinutes(2));
        session.AddTimer(longTimer);session.AddTimer(shortTimer);var output=new TrainingScorebugControl();output.Bind(session);
        var window=new NativeWindow{Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle();Assert.Equal("100:00:00",VisibleDuration(output,session.ActiveRemaining,longTimer.Duration));
            session.Skip();await Settle();Assert.Same(shortTimer,session.Active);Assert.Equal("02:00",VisibleDuration(output,session.ActiveRemaining,shortTimer.Duration));
            var replacement=new TrainingSession();var replacementTimer=new TrainingTimer("Replacement",TimeSpan.FromHours(99));replacement.AddTimer(replacementTimer);output.Bind(replacement);await Settle();
            Assert.Equal("99:00:00",VisibleDuration(output,replacement.ActiveRemaining,replacementTimer.Duration));Assert.Same(shortTimer,session.Active);
            replacement.Clear();await Settle();Assert.Equal("00:00",VisibleDuration(output,TimeSpan.Zero,TimeSpan.Zero));
            output.Bind(session);await Settle();Assert.Equal("02:00",VisibleDuration(output,session.ActiveRemaining,shortTimer.Duration));
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaTheory][InlineData(386,193)][InlineData(960,540)]
    public async Task ConfiguredHourAtZeroFinishesThroughPublicTickAndClearsNeutralHours(int width,int height)
    {
        // A valid public model snapshot: configured for an hour, already at
        // zero remaining, but not yet ticked through completion. No private
        // handler/state injection and no claim an hour elapsed in this test.
        var session=new TrainingSession();var timer=new TrainingTimer("Restored final interval",TimeSpan.FromHours(1)){Remaining=TimeSpan.Zero};session.AddTimer(timer);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle();Assert.Equal("0:00:00",VisibleDuration(output,session.ActiveRemaining,timer.Duration));
            session.Start();session.Tick();await Settle(650);Assert.True(timer.HasFinished);Assert.Null(session.Active);
            Assert.True(Named<Border>(output,"SessionCompleteOverlay").IsVisible);
            Assert.InRange(Named<Border>(output,"SessionCompleteOverlay").Opacity,.99,1);CaptureStrip(window,output,"capacity-final-complete",width);
            Assert.Equal("00:00",VisibleDuration(output,TimeSpan.Zero,TimeSpan.Zero));
            Assert.False(session.IsRunning);Assert.Equal(1,session.ActiveIndex);Assert.Single(session.Timers);
        }
        finally{window.Content=null;window.Close();await Settle();}
    }

    [AvaloniaTheory][InlineData(386,193,false)][InlineData(960,540,false)][InlineData(386,193,true)][InlineData(960,540,true)]
    public async Task ReasonableNextTitleKeepsItsIdentityBesideMaximumDuration(int width,int height,bool staged)
    {
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished interval",TimeSpan.Zero);
        var next=new TrainingTimer("Recovery",TimeSpan.MaxValue);session.AddTimer(first);session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle(650);if(staged){session.Start();session.Tick();await Settle(650);Assert.Same(next,session.Active);}
            string titleName=staged?"StagedTitleText":"UpNextTitle",durationName=staged?"StagedTimerText":"UpNextDuration";
            CaptureStrip(window,output,"capacity-readable-title-"+staged,width);
            Assert.Equal(staged?"RECOVERY":"Recovery",Named<TextBlock>(output,titleName).Text);
            AssertStripPart(output,titleName,requireFullText:true);AssertStripPart(output,durationName,requireFullText:true);
            var root=Named<Grid>(output,"RootGrid");var title=Named<TextBlock>(output,titleName);var duration=Named<TextBlock>(output,durationName);
            var titleRight=title.TranslatePoint(new Point(title.Bounds.Width,0),root);var durationLeft=duration.TranslatePoint(default,root);
            Assert.NotNull(titleRight);Assert.NotNull(durationLeft);Assert.True(titleRight.Value.X+16<=durationLeft.Value.X,"Title and duration need a real readable gap.");
            Assert.Equal("256204778:48:05",duration.Text);Assert.False(session.IsRunning);Assert.Equal(TimeSpan.MaxValue,next.Remaining);
        }
        finally{window.Content=null;window.Close();await Settle();}
    }

}
