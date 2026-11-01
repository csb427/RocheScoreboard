using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class AflResetScopeTests
{
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","TrainingCloudEnd")]
    public async Task ActualTrainingRemoteEndDeclinesByDefaultAndCannotEndReplacement(bool replace)
    {
        await using var scope=await CloudOwnerScope.Create("ORIGINAL","REPLACEMENT");var window=scope.F.Window;
        var home=Named<SportSelectionScreen>(window,"SportSelectionPanel");
        Button? training=null;
        await Until(()=> {
            var current=home.GetVisualDescendants().OfType<Button>().SingleOrDefault(x=>x.Name=="TrainingLaunchAction");
            if(current is not {IsLoaded:true,IsEffectivelyVisible:true,IsEffectivelyEnabled:true} || current.Bounds.Width<=0 || current.Bounds.Height<=0)return false;
            var point=current.TranslatePoint(new Avalonia.Point(current.Bounds.Width/2,current.Bounds.Height/2),window);
            if(point is null || !Within(current,window.InputHitTest(point.Value)))return false;
            training=current;return true;
        },"Current Home Training action must be attached and hit-testable; retired startup actions are not a product baseline.");
        await Tap(window,training!);
        var panel=Named<TrainingControlPanel>(window,"TrainingControlPanelView");await Until(()=>panel.IsEffectivelyVisible,"Home Training must mount the real panel.");
        var session=Assert.IsType<TrainingSession>(window.GetType().GetField("_trainingSession",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window));
        string TrainingState()=>JsonSerializer.Serialize(new {session.ActiveIndex,session.IsRunning,timers=session.Timers.Select(t=>new{t.Duration,t.Remaining}).ToArray()});
        string before=TrainingState();
        Task started=scope.Start();scope.Services[0].StartSucceeds();await CloudOwnerScope.Terminal(started);Assert.Equal("training",scope.Services[0].StartedSport);
        var launcher=Named<CloudRemoteLauncher>(panel,"TrainingCloudLauncher");var header=Named<Button>(launcher,"CollapsedButton");
        await RevealByKeyboard(window,header);await Tap(window,header);await Settle(100);Assert.True(launcher.IsExpanded);
        var end=Named<Button>(launcher,"EndButton");Assert.True(end.Focus(NavigationMethod.Tab));
        bool seen=false,noDefault=false;Exception? callbackError=null;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};var elapsed=System.Diagnostics.Stopwatch.StartNew();
        timer.Tick+=async(_,_)=> {
            var dialog=window.OwnedWindows.SingleOrDefault(x=>x.Title=="Cloud Remote");
            if(dialog is null){if(elapsed.Elapsed>TimeSpan.FromSeconds(10)){callbackError=new TimeoutException("Training End confirmation did not appear.");timer.Stop();}return;}
            timer.Stop();seen=true;
            try {
                var buttons=dialog.GetVisualDescendants().OfType<Button>().ToArray();var no=Assert.Single(buttons,x=>Equals(x.Content,"No"));noDefault=no.IsDefault;
                if(replace) {
                    Task oldEnd=scope.End();scope.Services[0].FinishDisposal();await CloudOwnerScope.Terminal(oldEnd);
                    Task next=scope.Start();scope.Services[1].StartSucceeds();await CloudOwnerScope.Terminal(next);
                }
                var action=replace?Assert.Single(buttons,x=>Equals(x.Content,"Yes")):no;
                action.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                if(replace)scope.Services[1].FinishDisposal();
            }
            catch(Exception error){callbackError=error;dialog.Close();}
        };
        try {
            timer.Start();window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);await Settle(200);
            Assert.Null(callbackError);Assert.True(seen);Assert.True(window.IsCloudSessionActive,"An old End confirmation cannot disconnect its replacement.");
            Assert.Equal(replace?"REPLACEMENT":"ORIGINAL",window.CloudJoinCode);Assert.True(noDefault,"No must be the explicit confirmation default.");
            Assert.Equal(before,TrainingState());Assert.Same(session,window.GetType().GetField("_trainingSession",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window));Assert.Empty(scope.Failures);
        }
        finally{timer.Stop();foreach(var child in window.OwnedWindows.Where(x=>x.Title=="Cloud Remote").ToArray())child.Close();}
    }
}
