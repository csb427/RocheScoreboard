using System.Reflection;
using System.Text.Json;
using Avalonia;
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
public sealed class TrainingDisplayMotionPolicyTests
{
    private const string LongName="A long training interval title that exceeds the whole output and must remain readable without spatial motion";
    private static async Task Settle(int ms=120){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static T Read<T>(object o,string n)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        if(o.GetType().GetField(n,flags) is {} field)return (T)field.GetValue(o)!;
        var property=o.GetType().GetProperty(n,flags);Assert.NotNull(property);return (T)property.GetValue(o)!;
    }
    private static string State(TrainingSession s)=>JsonSerializer.Serialize(new{s.ActiveIndex,s.IsRunning,s.AutoAdvance,s.ActiveRemaining,timers=s.Timers.Select(t=>new{t.Id,t.Title,t.Duration,t.Remaining,t.HasFinished})});
    [AvaloniaTheory] [InlineData(false,386,193)] [InlineData(true,386,193)] [InlineData(false,960,540)] [InlineData(true,960,540)]
    [Trait("Evidence","TrainingDisplayMotionPolicy")]
    public async Task ReducedOutputUsesStaticEllipsisAndNormalOutputKeepsItsCurrentMarquee(bool reduce,int width,int height)
    {
        var session=new TrainingSession{AutoAdvance=false};session.AddTimer(new TrainingTimer(LongName,TimeSpan.FromMinutes(2)));
        var output=new TrainingScorebugControl{ReduceDisplayMotion=reduce};output.Bind(session);
        var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle(180);string before=State(session);var title=output.FindControl<TextBlock>("TitleText")!;var travel=Assert.IsType<TranslateTransform>(title.RenderTransform);
            Assert.Equal(LongName.ToUpperInvariant(),title.Text);Assert.Equal(110,title.FontSize);
            if(reduce){Assert.False(Motion.IsAnimating(travel));Assert.Equal(0,travel.X);Assert.Equal(TextTrimming.CharacterEllipsis,title.TextTrimming);Assert.InRange(title.Width,1371,1373);}
            else{Assert.True(Motion.IsAnimating(travel));Assert.True(travel.X<0);Assert.Equal(TextTrimming.None,title.TextTrimming);Assert.True(double.IsNaN(title.Width));}
            Assert.Equal(before,State(session));
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir){Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(dir,$"output-policy-{reduce}-{width}.png"));}
            output.ReduceDisplayMotion=!reduce;await Settle(180);Assert.Equal(reduce,Motion.IsAnimating(travel));Assert.Equal(before,State(session));
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaFact]
    [Trait("Evidence","TrainingDisplayMotionPolicy")]
    public async Task LiveSpatialReductionKeepsTheExistingHoldPromotionAndModelOwner()
    {
        var session=new TrainingSession{AutoAdvance=false};var first=new TrainingTimer("Finished",TimeSpan.Zero);var next=new TrainingTimer(LongName,TimeSpan.FromMinutes(2));session.AddTimer(first);session.AddTimer(next);
        var output=new TrainingScorebugControl();output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};window.Show();
        try
        {
            await Settle(180);session.Start();session.Tick();Assert.Same(next,session.Active);Assert.False(session.IsRunning);
            var staged=Read<TranslateTransform>(output,"StagedTranslate");Assert.True(Motion.IsAnimating(staged));Assert.True(staged.Y>0);
            var hold=Read<DispatcherTimer>(output,"_holdTimer");Assert.Equal(TimeSpan.FromMilliseconds(7500),hold.Interval);
            string before=State(session);int changes=0;session.Changed+=()=>changes++;
            output.ReduceDisplayMotion=true;Assert.False(Motion.IsAnimating(staged));Assert.Equal(0,staged.Y);Assert.Same(hold,Read<DispatcherTimer>(output,"_holdTimer"));Assert.True(hold.IsEnabled);
            output.ReduceDisplayMotion=false;Assert.False(Motion.IsAnimating(staged));Assert.Same(hold,Read<DispatcherTimer>(output,"_holdTimer"));
            output.ReduceDisplayMotion=true;
            for(int i=0;i<150&&Read<bool>(output,"_flashing");i++)
            {
                Assert.False(Motion.IsAnimating(Read<ScaleTransform>(output,"TimerScale")));Assert.Equal(1,Read<ScaleTransform>(output,"TimerScale").ScaleX);
                if(Read<DispatcherTimer?>(output,"_promotionTimer") is { } promotion)Assert.Equal(TimeSpan.FromMilliseconds(470),promotion.Interval);
                await Settle(80);
            }
            Assert.False(Read<bool>(output,"_flashing"));Assert.Null(Read<DispatcherTimer?>(output,"_promotionTimer"));
            Assert.Equal(LongName.ToUpperInvariant(),output.FindControl<TextBlock>("TitleText")!.Text);Assert.Equal(TextTrimming.CharacterEllipsis,output.FindControl<TextBlock>("TitleText")!.TextTrimming);
            Assert.False(Motion.IsAnimating(Read<ScaleTransform>(output,"TitleScale")));Assert.Equal(before,State(session));Assert.Equal(0,changes);
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","TrainingDisplayMotionPolicy")]
    public async Task ReducedDisplayDoesNotDelayModelAdvanceOrChangeAutoAdvance(bool auto)
    {
        var session=new TrainingSession{AutoAdvance=auto};var first=new TrainingTimer("Finished",TimeSpan.Zero);var next=new TrainingTimer("Next",TimeSpan.FromMinutes(2));session.AddTimer(first);session.AddTimer(next);
        var output=new TrainingScorebugControl{ReduceDisplayMotion=true};output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};window.Show();
        try{await Settle();session.Start();session.Tick();Assert.True(first.HasFinished);Assert.Same(next,session.Active);Assert.Equal(auto,session.IsRunning);Assert.Equal(1,session.ActiveIndex);Assert.Equal(TimeSpan.FromMilliseconds(7500),Read<DispatcherTimer>(output,"_holdTimer").Interval);Assert.False(Motion.IsAnimating(Read<TranslateTransform>(output,"StagedTranslate")));}
        finally{window.Content=null;window.Close();await Settle();}
    }
    [AvaloniaFact]
    [Trait("Evidence","TrainingDisplayMotionPolicy")]
    public async Task ReducedFinalCompletionKeepsItsTenSecondWindow()
    {
        var session=new TrainingSession();session.AddTimer(new TrainingTimer("Finished",TimeSpan.Zero));var output=new TrainingScorebugControl{ReduceDisplayMotion=true};output.Bind(session);var window=new NativeWindow{Title="Training Display",Width=386,Height=193,Content=output};window.Show();
        try{await Settle();session.Start();session.Tick();Assert.True(session.IsExhausted);Assert.False(session.IsRunning);Assert.Equal(TimeSpan.FromSeconds(10),Read<DispatcherTimer>(output,"_sessionCompleteTimer").Interval);Assert.Equal("SESSION COMPLETE",output.FindControl<TextBlock>("TitleText")!.Text);}
        finally{window.Content=null;window.Close();await Settle();}
    }
}
