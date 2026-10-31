using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingMotionTests
{
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-training-motion";Directory.CreateDirectory(dir);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData(1000,750)][InlineData(420,320)]
    public async Task TransportChoreographyNeverDelaysOrDuplicatesTheTimerAction(int width,int height)
    {
        var session=new TrainingSession();session.AddTimer(new TrainingTimer("Warm-up and kicking accuracy",TimeSpan.FromMinutes(2)));
        var panel=new TrainingControlPanel();panel.Bind(session);var window=new NativeWindow{Width=width,Height=height,Content=panel};window.Show();
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        try
        {
            OperatorAppearance.Configure(glass,false);await Settle();
            var button=panel.FindControl<Button>("StartPauseButton")!;button.BringIntoView();await Settle();var bounds=button.Bounds;
            Capture(window,"training-transport-before-"+width);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(session.IsRunning);
            var symbol=panel.FindControl<OperatorSymbol>("TrainingTransportSymbol");Assert.NotNull(symbol);Assert.True(symbol.IsActive);
            var label=panel.FindControl<TextBlock>("TrainingTransportLabel")!;Assert.Equal("PAUSE",label.Text);
            for(int i=0;i<6;i++)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(i%2==1,session.IsRunning);Assert.Equal(session.IsRunning,symbol.IsActive);
                Assert.Equal(session.IsRunning?"PAUSE":"START",label.Text);Assert.Equal(bounds,button.Bounds);
                await Settle(25);Capture(window,"training-transport-"+width+"-"+i);
            }
            await Settle(650);Assert.InRange(symbol.FillProgress,.99,1);Capture(window,"training-transport-settled-"+width);
            OperatorAppearance.Configure(glass,true);Assert.False(symbol.IsAnimating);
            button.Focus();window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);
            Assert.False(session.IsRunning);Assert.False(symbol.IsActive);Assert.Equal("START",label.Text);
            var title=panel.FindControl<TextBox>("NewTitleBox")!;title.Focus();window.KeyTextInput("s r drill");Assert.False(session.IsRunning);Assert.Contains("s r drill",title.Text);
            Capture(window,"training-transport-after-"+width);
        }
        finally{session.Pause();window.Close();OperatorAppearance.Configure(glass,reduced);}
    }

    [AvaloniaFact]
    public async Task RebindingAndClosingCannotLeaveTransportMotionOrAnOldSessionStateAttached()
    {
        var previous=new TrainingSession();previous.AddTimer(new TrainingTimer("Old session",TimeSpan.FromMinutes(1)));
        var current=new TrainingSession();var panel=new TrainingControlPanel();panel.Bind(previous);
        var window=new NativeWindow{Width=700,Height=500,Content=panel};window.Show();
        try
        {
            await Settle();previous.Start();var symbol=panel.FindControl<OperatorSymbol>("TrainingTransportSymbol")!;Assert.True(symbol.IsActive);
            panel.Bind(current);Assert.False(symbol.IsActive);Assert.False(panel.FindControl<Button>("StartPauseButton")!.IsEnabled);
            previous.Pause();previous.Start();Assert.False(symbol.IsActive);Assert.Equal("START",panel.FindControl<TextBlock>("TrainingTransportLabel")!.Text);
            current.AddTimer(new TrainingTimer("New session",TimeSpan.FromMinutes(2)));current.Start();Assert.True(symbol.IsActive);
            window.Close();Assert.False(symbol.IsAnimating);
            current.Pause();Assert.False(symbol.IsActive);Assert.False(symbol.IsAnimating);
        }
        finally{previous.Pause();current.Pause();window.Close();}
    }
}
