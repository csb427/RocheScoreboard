using System.Text.Json;
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
public sealed class TrainingTitleVisibilityTests
{
    private static async Task Settle(int ms=120){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static string State(TrainingSession s)=>JsonSerializer.Serialize(new{s.ActiveIndex,s.IsRunning,s.AutoAdvance,s.ActiveRemaining,timers=s.Timers.Select(t=>new{t.Id,t.Title,t.Duration,t.Remaining,t.HasFinished})});
    [AvaloniaTheory] [InlineData(false,386,193)] [InlineData(true,386,193)] [InlineData(false,960,540)] [InlineData(true,960,540)]
    [Trait("Evidence","TrainingTitleVisibility")]
    public async Task HiddenTitleStopsUntilItsCurrentVisibleOwnerReturns(bool hideView,int width,int height)
    {
        const string name="A long current training interval title that exceeds the whole display and needs a visible title marquee";
        var session=new TrainingSession{AutoAdvance=false};session.AddTimer(new TrainingTimer(name,TimeSpan.FromMinutes(20)));
        var output=new TrainingScorebugControl();output.Bind(session);int unloaded=0;output.Unloaded+=(_,_)=>unloaded++;
        var window=new NativeWindow{Title="Training Display",Width=width,Height=height,Content=output};window.Show();
        try
        {
            await Settle(180);var title=output.FindControl<TextBlock>("TitleText")!;var travel=Assert.IsType<TranslateTransform>(title.RenderTransform);
            Assert.True(Motion.IsAnimating(travel));await Settle();Assert.True(travel.X<0,"Positive control must show real title movement before hiding.");
            for(int cycle=0;cycle<2;cycle++)
            {
                string before=State(session);
                if(hideView)output.IsVisible=false;else window.Hide();await Settle();
                Assert.True(output.IsLoaded);Assert.Equal(0,unloaded);
                Assert.False(Motion.IsAnimating(travel),"A hidden output must not retain the repeating title clock.");Assert.Equal(0,travel.X,3);Assert.Equal(before,State(session));
                if(cycle==1)
                {
                    var replacement=new TrainingSession{AutoAdvance=false};replacement.AddTimer(new TrainingTimer("Replacement "+name,TimeSpan.FromMinutes(3)));output.Bind(replacement);
                    Assert.Equal(before,State(session));session=replacement;before=State(session);Assert.False(Motion.IsAnimating(travel));
                }
                if(hideView)output.IsVisible=true;else window.Show();await Settle(180);
                Assert.Equal(session.Active!.Title.ToUpperInvariant(),title.Text);Assert.True(Motion.IsAnimating(travel));await Settle();Assert.True(travel.X<0);
                Assert.Equal(before,State(session));Assert.Equal(0,unloaded);
                if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
                {Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,$"title-{hideView}-{width}-{cycle}.json"),JsonSerializer.Serialize(new{cycle,hideView,width,height,unloaded,title=title.Text,travel.X,animating=Motion.IsAnimating(travel),state=State(session)}));using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(dir,$"title-{hideView}-{width}-{cycle}.png"));}
            }
        }
        finally{window.Content=null;window.Close();await Settle();}
    }
}
