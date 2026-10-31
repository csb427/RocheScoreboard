using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>Capture-only historical comparison; not a functional acceptance suite.</summary>
public sealed class OperatorEntryCaptureTests
{
    [AvaloniaTheory]
    [InlineData("settings",900,700)][InlineData("settings",420,320)]
    [InlineData("training",1280,850)][InlineData("training",420,320)]
    [Trait("Evidence","CaptureOnly")]
    public async Task CaptureOriginalNamedSurfaces(string area,int width,int height)
    {
        string? directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if(string.IsNullOrEmpty(directory))return;
        var app=(App)Application.Current!;bool selfTest=app.IsSelfTestMode;app.IsSelfTestMode=true;
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        byte[]? appearance=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        string layout=Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json");byte[]? saved=File.Exists(layout)?File.ReadAllBytes(layout):null;
        Window? window=null;
        async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
        void Capture(string phase)
        {
            Directory.CreateDirectory(directory);using var frame=window!.CaptureRenderedFrame();Assert.NotNull(frame);
            frame.Save(Path.Combine(directory,$"{area}-{width}-{phase}.png"));
        }
        try
        {
            OperatorAppearance.Configure(true,true);
            if(area=="settings")
            {
                var view=new GlobalSettingsScreen();window=new NativeWindow{Width=width,Height=height,Content=view};window.Show();await Settle(200);
                var input=view.FindControl<TextBox>("OutputWidthBox")!;input.Focus(Avalonia.Input.NavigationMethod.Tab);input.BringIntoView();await Settle();
                input.Text="49";Capture("draft");
                view.GetType().GetMethod("ApplyOutputDimensions_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[view,new Avalonia.Interactivity.RoutedEventArgs()]);await Settle(160);Capture("rejected");
            }
            else
            {
                var main=new Roche_Scoreboard.MainWindow();window=main;main.Show();await Settle(200);
                main.Width=width;main.Height=height;await Settle();
                main.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;
                OperatorAppearance.Configure(true,false);
                main.GetType().GetMethod("EnterTrainingMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null);
                await Settle(25);var panel=main.FindControl<TrainingControlPanel>("TrainingControlPanelView")!;Motion.SampleAt(panel,TimeSpan.FromMilliseconds(80));Capture("arrival");
                await Settle(650);Capture("settled");
            }
        }
        finally
        {
            window?.Close();app.IsSelfTestMode=selfTest;OperatorAppearance.Configure(glass,reduced);
            if(appearance is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,appearance);
            if(saved is null)File.Delete(layout);else{Directory.CreateDirectory(Path.GetDirectoryName(layout)!);File.WriteAllBytes(layout,saved);}
        }
    }
}
