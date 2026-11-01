using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingControllerLifetimeTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,Private)!.GetValue(instance)!;
    private static object? Invoke(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,Private)!.Invoke(instance,args);
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}

    [AvaloniaTheory]
    [InlineData(true)][InlineData(false)]
    public async Task TrainingEntryKeepsTheLiveConsoleReadableAndHonorsReducedMotion(bool reducedAtEntry)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        bool? animation=OperatorMotion.AnimationOverride;
        byte[]? appearance=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        string layout=Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json");
        byte[]? saved=File.Exists(layout)?File.ReadAllBytes(layout):null;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle(180);OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,reducedAtEntry);
            Invoke(window,"EnterTrainingMode");
            var panel=window.FindControl<TrainingControlPanel>("TrainingControlPanelView")!;
            Assert.True(panel.IsVisible);Assert.NotNull(Field<TrainingSession>(window,"_trainingSession"));
            Assert.Equal(1,panel.Opacity,3);Assert.False(Motion.IsAnimating(panel));
            Dispatcher.UIThread.RunJobs();
            var symbol=panel.FindControl<OperatorSymbol>("TrainingEntrySymbol")!;
            Assert.Equal("training-entry",symbol.Kind);Assert.Null(symbol.RenderTransform);
            var bounds=symbol.Bounds;
            if(reducedAtEntry)Assert.False(symbol.IsAnimating);
            else
            {
                Assert.True(symbol.IsAnimating);
                Motion.SampleAt(symbol,OperatorMotion.SymbolAccent);
                Assert.Equal(1,(double)symbol.Diagnostics.GetType().GetProperty("Pulse")!.GetValue(symbol.Diagnostics)!,3);
            }
            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-training-entry";Directory.CreateDirectory(directory);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"training-entry-{reducedAtEntry}.png"));}
            OperatorAppearance.Configure(true,true);Dispatcher.UIThread.RunJobs();
            Assert.Equal(1,panel.Opacity,3);Assert.False(Motion.IsAnimating(panel));
            Assert.False(symbol.IsAnimating);Assert.Equal(bounds,symbol.Bounds);
        }
        finally
        {
            window.Close();app.IsSelfTestMode=previous;OperatorMotion.AnimationOverride=animation;OperatorAppearance.Configure(glass,reduced);
            if(appearance is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,appearance);
            if(saved is null)File.Delete(layout);else{Directory.CreateDirectory(Path.GetDirectoryName(layout)!);File.WriteAllBytes(layout,saved);}
        }
    }

    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task QueueProgressesOnceWithOrWithoutItsOutput(bool closeOutput,bool autoAdvance)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        string layout=Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json");
        byte[]? saved=File.Exists(layout)?File.ReadAllBytes(layout):null;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle(180);window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;
            Invoke(window,"EnterTrainingMode");await Settle();
            var session=Field<TrainingSession>(window,"_trainingSession");session.Clear();session.AutoAdvance=autoAdvance;
            var first=new TrainingTimer("Finished drill",TimeSpan.Zero);var next=new TrainingTimer("Next drill",TimeSpan.FromMinutes(1));
            session.AddTimer(first);session.AddTimer(next);
            var output=Field<NativeWindow>(window,"_trainingDisplayWindow");
            if(closeOutput){output.Close();await Settle();Assert.False(output.IsVisible);}
            int completions=0;session.IntervalFinished+=_=>completions++;
            session.Start();await Settle(180);
            Assert.True(first.HasFinished);Assert.Equal(1,completions);Assert.Same(next,session.Active);Assert.Equal(1,session.ActiveIndex);
            Assert.Equal(autoAdvance,session.IsRunning);Assert.False(next.HasFinished);
            await Settle(100);Assert.Same(next,session.Active);Assert.Equal(1,completions);
            if(closeOutput)
            {
                // Closing output intentionally leaves it off-air. Reopening
                // uses the operator's explicit display command, not Ensure.
                Invoke(window,"EnableOperatorOutput");await Settle();
                Assert.True(Field<NativeWindow>(window,"_trainingDisplayWindow").IsVisible);Assert.Same(next,session.Active);Assert.Equal(1,completions);
            }
        }
        finally
        {
            window.Close();app.IsSelfTestMode=previous;
            if(saved is null)File.Delete(layout);else{Directory.CreateDirectory(Path.GetDirectoryName(layout)!);File.WriteAllBytes(layout,saved);}
        }
    }
}
