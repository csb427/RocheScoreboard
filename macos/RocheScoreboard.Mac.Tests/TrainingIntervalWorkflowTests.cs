using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingIntervalWorkflowTests
{
    private static void Set(TrainingControlPanel panel,string name,string value)=>panel.FindControl<TextBox>(name)!.Text=value;
    private static void Build(TrainingControlPanel panel)=>panel.FindControl<Button>("BuildIntervalButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    [AvaloniaTheory]
    [InlineData("0","20","10","IntervalRoundsBox")]
    [InlineData("1","0","10","IntervalWorkBox")]
    [InlineData("1","garbage","10","IntervalWorkBox")]
    [InlineData("1","20","-1","IntervalRestBox")]
    public async Task RejectedIntervalsLeaveTheLiveQueueUntouchedAndExplainTheBadField(string rounds,string work,string rest,string field)
    {
        var session=new TrainingSession();var active=new TrainingTimer("Current drill",TimeSpan.FromMinutes(2));session.AddTimer(active);session.Start();
        var panel=new TrainingControlPanel();panel.Bind(session);var window=new NativeWindow{Width=420,Height=320,Content=panel};window.Show();
        try
        {
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            Set(panel,"IntervalRoundsBox",rounds);Set(panel,"IntervalWorkBox",work);Set(panel,"IntervalRestBox",rest);
            int changed=0,structural=0;session.Changed+=()=>changed++;session.StructuralChanged+=()=>structural++;
            Build(panel);Assert.Single(session.Timers);Assert.Same(active,session.Active);Assert.True(session.IsRunning);Assert.Equal(0,changed);Assert.Equal(0,structural);
            var error=panel.FindControl<TextBlock>("TrainingIntervalError");Assert.NotNull(error);Assert.True(error.IsVisible);Assert.False(string.IsNullOrWhiteSpace(error.Text));
            await Task.Delay(50);Dispatcher.UIThread.RunJobs();
            Assert.True(panel.FindControl<TextBox>(field)!.IsFocused);Assert.Equal(rounds,panel.FindControl<TextBox>("IntervalRoundsBox")!.Text);
            var point=error.TranslatePoint(default,window)!.Value;
            Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(point,error.Bounds.Size)),"Rejected-interval explanation must be visible at compact size");
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-training-interval";Directory.CreateDirectory(dir);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(dir,"interval-error-"+field+"-"+work+".png"));}
            Set(panel,"IntervalRoundsBox","1");Set(panel,"IntervalWorkBox","20");Set(panel,"IntervalRestBox","0");Build(panel);
            Assert.Equal(2,session.Timers.Count);Assert.Equal(1,structural);Assert.Equal(1,changed);Assert.Same(active,session.Active);Assert.True(session.IsRunning);Assert.False(error.IsVisible);
        }
        finally{session.Pause();window.Close();}
    }

    [AvaloniaFact]
    public void BuildingMultipleRoundsNotifiesTheUiOnceAndKeepsTheCurrentDrillRunning()
    {
        var session=new TrainingSession();var active=new TrainingTimer("Current drill",TimeSpan.FromMinutes(2));session.AddTimer(active);session.Start();
        var panel=new TrainingControlPanel();panel.Bind(session);
        Set(panel,"IntervalRoundsBox","10");Set(panel,"IntervalWorkBox","20");Set(panel,"IntervalRestBox","10");
        int changed=0,structural=0;session.Changed+=()=>changed++;session.StructuralChanged+=()=>structural++;
        try
        {
            Build(panel);Assert.Equal(20,session.Timers.Count);Assert.Equal(1,changed);Assert.Equal(1,structural);
            Assert.Same(active,session.Active);Assert.True(session.IsRunning);
            Assert.Equal("WORK 1/10",session.Timers[1].Title);Assert.Equal("REST",session.Timers[2].Title);Assert.Equal("WORK 10/10",session.Timers[^1].Title);
            Assert.Equal(TimeSpan.FromSeconds(20),session.Timers[1].Duration);Assert.Equal(TimeSpan.FromSeconds(10),session.Timers[2].Duration);
        }
        finally{session.Pause();}
    }

    [AvaloniaTheory]
    [InlineData("100","1","1",199)]
    [InlineData("3","20","0",3)]
    public void ValidRoundLimitsAndNoRestHaveOneOrderedBatchAndNoDeferredMassRowReveal(string rounds,string work,string rest,int expected)
    {
        var session=new TrainingSession();var panel=new TrainingControlPanel();panel.Bind(session);
        Set(panel,"IntervalRoundsBox",rounds);Set(panel,"IntervalWorkBox",work);Set(panel,"IntervalRestBox",rest);
        int structural=0;session.StructuralChanged+=()=>structural++;Build(panel);
        Assert.Equal(expected,session.Timers.Count);Assert.Equal(1,structural);Assert.Equal("WORK "+rounds+"/"+rounds,session.Timers[^1].Title);
        var revealed=(HashSet<Guid>)typeof(TrainingControlPanel).GetField("_revealedTimerIds",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(panel)!;
        Assert.Equal(expected,revealed.Count);
        Assert.False(panel.FindControl<TextBlock>("TrainingIntervalError")!.IsVisible);
    }

    [Fact]
    public void InvalidBulkAppendsAndOverflowDoNotMutateOrNotify()
    {
        var session=new TrainingSession();var original=new TrainingTimer("Long session",TimeSpan.MaxValue);session.AddTimer(original);
        int changes=0;session.StructuralChanged+=()=>changes++;session.Changed+=()=>changes++;
        Assert.False(session.TryAppendTimers(new[]{new TrainingTimer("Overflow",TimeSpan.FromSeconds(1))},out string overflow));Assert.Contains("long",overflow);
        Assert.False(session.TryAppendTimers(new[]{original},out _));Assert.False(session.TryAppendTimers(null,out _));
        Assert.False(session.TryAppendTimers(new TrainingTimer[]{null!},out _));
        Assert.True(session.TryAppendTimers(Array.Empty<TrainingTimer>(),out _));
        Assert.Single(session.Timers);Assert.Same(original,session.Active);Assert.Equal(TimeSpan.MaxValue,session.TotalDuration);Assert.Equal(0,changes);
    }

    [AvaloniaFact]
    public async Task BuildingIntervalsCannotSilentlyDiscardAnOpenTimerCorrection()
    {
        var session=new TrainingSession();session.AddTimer(new TrainingTimer("Existing drill",TimeSpan.FromMinutes(2)));
        var panel=new TrainingControlPanel();panel.Bind(session);var window=new NativeWindow{Width=700,Height=500,Content=panel};window.Show();
        try
        {
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            var edit=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),button=>Equals(ToolTip.GetTip(button),"Edit title and duration"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            var input=panel.GetVisualDescendants().OfType<TextBox>().Single(field=>field.Text=="Existing drill");input.Text="Unsaved correction";
            int changes=0;session.StructuralChanged+=()=>changes++;Build(panel);
            Assert.Single(session.Timers);Assert.Equal(0,changes);Assert.Equal("Unsaved correction",input.Text);
            Assert.Contains(input,panel.GetVisualDescendants());Assert.Equal("Existing drill",session.Active!.Title);
            Assert.Contains("cancel",panel.FindControl<TextBlock>("TrainingIntervalError")!.Text,StringComparison.OrdinalIgnoreCase);
            panel.GetVisualDescendants().OfType<Button>().Single(button=>Equals(ToolTip.GetTip(button),"Cancel (Esc)")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0,changes);Assert.Equal("Existing drill",session.Active.Title);
            Build(panel);Assert.Equal(16,session.Timers.Count);Assert.Equal(1,changes);Assert.False(panel.FindControl<TextBlock>("TrainingIntervalError")!.IsVisible);
        }
        finally{window.Close();}
    }
}
