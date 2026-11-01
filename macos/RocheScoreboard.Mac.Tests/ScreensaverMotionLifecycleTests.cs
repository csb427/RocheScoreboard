using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class ScreensaverMotionLifecycleTests
{
    private static DispatcherTimer Clock(OutputScreensaverControl view)=>(DispatcherTimer)view.GetType().GetField("_clockTimer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!;
    private static async Task Pump(int ms=120){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name){if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} dir)return;Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));}
    [AvaloniaTheory] [InlineData(386,193)] [InlineData(960,540)]
    [Trait("Evidence","ScreensaverMotionLifecycle")]
    public async Task HidingRetainedOutputStopsItsClockAndReopeningResumesCurrentDisplay(int width,int height)
    {
        var view=new OutputScreensaverControl();var host=new Grid();host.Children.Add(view);var owner=new NativeWindow{Width=width,Height=height,Content=host};owner.Show();
        try
        {
            await Pump();Assert.True(view.IsLoaded);Assert.True(Clock(view).IsEnabled);Assert.Equal(OutputScreensaverMediaState.BuiltIn,view.CurrentMediaState);Capture(owner,"saver-visible-"+width);
            owner.Hide();await Pump();Assert.True(view.IsLoaded);Assert.False(Clock(view).IsEnabled,"Hidden retained output must not keep its wall-clock dispatcher timer running.");
            owner.Show();await Pump();Assert.True(Clock(view).IsEnabled);Assert.Equal(OutputScreensaverMediaState.BuiltIn,view.CurrentMediaState);Assert.False(string.IsNullOrWhiteSpace(view.FindControl<TextBlock>("ClockText")!.Text));
            host.Children.Remove(view);await Pump();Assert.False(Clock(view).IsEnabled);
            host.Children.Add(view);await Pump();Assert.True(Clock(view).IsEnabled);
        }
        finally{owner.Close();}
    }
    private static Avalonia.Media.TranslateTransform Beam(OutputScreensaverControl view) =>
        (Avalonia.Media.TranslateTransform)view.GetType().GetProperty("BeamOneTranslate",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(view)!;
    [AvaloniaTheory] [InlineData(386,193)] [InlineData(960,540)]
    [Trait("Evidence","ScreensaverMotionLifecycle")]
    public async Task ReducedOutputIsStaticAndLivePolicyHideAndReparentRetireItsAmbientClocks(int width,int height)
    {
        var view=new OutputScreensaverControl{ReduceDisplayMotion=true};var host=new Grid();host.Children.Add(view);var owner=new NativeWindow{Width=width,Height=height,Content=host};owner.Show();
        try
        {
            await Pump();var beam=Beam(view);Assert.True(Clock(view).IsEnabled);Assert.False(Motion.IsAnimating(beam));Assert.Equal(0,beam.X);
            Assert.Equal(OutputScreensaverMediaState.BuiltIn,view.CurrentMediaState);Capture(owner,"saver-reduced-"+width);
            view.ReduceDisplayMotion=false;await Pump(160);Assert.True(Motion.IsAnimating(beam));double first=beam.X;await Pump(200);Assert.NotEqual(first,beam.X);Capture(owner,"saver-ambient-"+width);
            view.ReduceDisplayMotion=true;Assert.False(Motion.IsAnimating(beam));Assert.Equal(0,beam.X);Assert.True(Clock(view).IsEnabled);
            view.ReduceDisplayMotion=false;await Pump(80);Assert.True(Motion.IsAnimating(beam));owner.Hide();Assert.False(Motion.IsAnimating(beam));Assert.False(Clock(view).IsEnabled);
            view.ReduceDisplayMotion=true;owner.Show();await Pump();Assert.False(Motion.IsAnimating(beam));Assert.True(Clock(view).IsEnabled);
            view.ReduceDisplayMotion=false;await Pump();Assert.True(Motion.IsAnimating(beam));view.IsVisible=false;Assert.False(Motion.IsAnimating(beam));Assert.False(Clock(view).IsEnabled);
            view.IsVisible=true;await Pump();Assert.True(Motion.IsAnimating(beam));host.Children.Remove(view);await Pump();Assert.False(Motion.IsAnimating(beam));Assert.False(Clock(view).IsEnabled);
            view.ReduceDisplayMotion=true;host.Children.Add(view);await Pump();Assert.False(Motion.IsAnimating(beam));Assert.True(Clock(view).IsEnabled);
            owner.Close();Assert.False(Motion.IsAnimating(beam));Assert.False(Clock(view).IsEnabled);
        }
        finally{owner.Close();}
    }
}
