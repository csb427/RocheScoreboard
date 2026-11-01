using System.Reflection;
using System.Text.Json;
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

public sealed class EditorInterruptionTests
{
    [AvaloniaTheory]
    [InlineData("save")][InlineData("delete")][InlineData("cancel")][InlineData("close")]
    public void AStaleAflEditorCannotChangeTheReplacementScoreAtItsOldIndex(string action)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        var match=(MatchManager)typeof(Roche_Scoreboard.MainWindow).GetField("_match",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        match.AddGoal(TeamSide.Home);var original=match.Events[0];string? state=null;Exception? callbackError=null;
        int changesAfterConfirmation=0;
        var waiting=System.Diagnostics.Stopwatch.StartNew();
        var noticeCloser=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        noticeCloser.Tick+=(_,_)=>
        {
            if(waiting.Elapsed>TimeSpan.FromSeconds(15))
            {
                callbackError=new TimeoutException("The modal rejection notice did not close.");noticeCloser.Stop();
                foreach(var owned in window.OwnedWindows.ToArray())owned.Close();return;
            }
            foreach(var notice in window.OwnedWindows.Where(w=>w is not ScoreEventEditorWindow).ToArray())
                notice.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>Equals(b.Content,"OK"))?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            Dispatcher.UIThread.Post(()=>
            {
                var editor=window.OwnedWindows.OfType<ScoreEventEditorWindow>().Single();
                try
                {
                    Assert.True(match.UndoLastScore());match.AddGoal(TeamSide.Away);state=JsonSerializer.Serialize(match.ToState());
                    match.MatchChanged+=()=>changesAfterConfirmation++;
                    // A rejection message is owned by the operator window; allow
                    // the synchronous source handler to finish without user input.
                    noticeCloser.Start();
                    if(action=="save")editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else if(action=="cancel")editor.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"CANCEL")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else if(action=="close")editor.Close();
                    else
                    {
                        // Exercise the owner's post-confirmation Delete path.
                        typeof(ScoreEventEditorWindow).GetProperty("Result")!.SetValue(editor,ScoreEventEditorWindow.EditorResult.Deleted);editor.Close();
                    }
                }
                catch(Exception error){callbackError=error;editor.Close();}
            });
            typeof(Roche_Scoreboard.MainWindow).GetMethod("ScoreLogBar_Click",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[new Border{Tag=original},null]);
            Assert.Null(callbackError);Assert.NotNull(state);Assert.Equal(state,JsonSerializer.Serialize(match.ToState()));
            Assert.Equal(0,match.HomeTotal);Assert.Equal(6,match.AwayTotal);Assert.Single(match.Events);
            Assert.Equal(0,changesAfterConfirmation);
        }
        finally{noticeCloser.Stop();foreach(var child in window.OwnedWindows.ToArray())child.Close();window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData("append")][InlineData("shift")][InlineData("corrected")][InlineData("cancel")]
    public void ScoreEditorRevalidatesIdentityWithoutBlockingUnrelatedScoring(string operation)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();
        var match=(MatchManager)typeof(Roche_Scoreboard.MainWindow).GetField("_match",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        try
        {
            match.AddGoal(TeamSide.Home);match.AddGoal(TeamSide.Away);
            var target=match.Events[operation=="shift"?1:0];var snapshot=target.Clone();
            if(operation=="append")match.AddBehind(TeamSide.Away);
            if(operation=="shift")Assert.True(match.RemoveEvent(0));
            if(operation=="corrected")Assert.True(match.ModifyEvent(0,TeamSide.Away,ScoreType.Goal,TimeSpan.FromSeconds(5)));
            string before=JsonSerializer.Serialize(match.ToState());int notifications=0;match.MatchChanged+=()=>notifications++;
            bool changed=window.TryApplyScoreLogEdit(target,snapshot,operation=="cancel"?ScoreEventEditorWindow.EditorResult.Cancelled:ScoreEventEditorWindow.EditorResult.Saved,
                snapshot.Team,ScoreType.Behind,snapshot.GameTime,snapshot.Quarter,out string error);
            if(operation is "cancel" or "corrected")
            {Assert.False(changed);Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.Equal(0,notifications);Assert.Equal(operation=="cancel",error.Length==0);}
            else
            {
                Assert.True(changed);Assert.Empty(error);Assert.Equal(1,notifications);
                Assert.Equal(operation=="append"?1:0,match.HomeTotal);Assert.Equal(operation=="append"?7:1,match.AwayTotal);
                Assert.Equal(operation=="append"?3:1,match.Events.Count);
            }
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public void ObsoleteScoreAnimationCompletionAndTimeoutCannotFinishTheNewAnimation()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();
        object? Invoke(string name,params object[] arguments)=>typeof(Roche_Scoreboard.MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,arguments);
        try
        {
            int old=(int)Invoke("BeginScoreAnimation",TimeSpan.FromSeconds(1))!;Invoke("ResetScoreAnimationQueue");
            int current=(int)Invoke("BeginScoreAnimation",TimeSpan.FromSeconds(1))!;
            Invoke("EndScoreAnimation",old);Invoke("RecoverTimedOutScoreAnimation",old);
            Assert.Equal(current,typeof(Roche_Scoreboard.MainWindow).GetField("_activeScoreAnimationToken",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window));
            Assert.Equal(1,typeof(Roche_Scoreboard.MainWindow).GetField("_activeScoreAnimations",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window));
        }
        finally{Invoke("ResetScoreAnimationQueue");window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData("cancel")][InlineData("escape")][InlineData("switch")]
    public async Task CancelledOrObsoleteCricketEntryCannotBeAppliedLater(string operation)
    {
        var match=Roche_Scoreboard.Services.CricketReliabilityChecks.CreateMatch(true);var next=Roche_Scoreboard.Services.CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=new NativeWindow{Width=700,Height=500,Content=panel};window.Show();
        async Task Pump(){await Task.Delay(150);Dispatcher.UIThread.RunJobs();}
        try
        {
            await Pump();panel.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Wide")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            var apply=panel.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ApplyButton");
            string original=JsonSerializer.Serialize(match.ToState()),replacement=JsonSerializer.Serialize(next.ToState());int changes=0;match.MatchChanged+=()=>changes++;next.MatchChanged+=()=>changes++;
            if(operation=="switch")panel.SetMatch(next);
            else if(operation=="cancel")panel.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else
            {Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(window,Avalonia.Input.PhysicalKey.Escape,Avalonia.Input.RawInputModifiers.None);Avalonia.Headless.HeadlessWindowExtensions.KeyReleaseQwerty(window,Avalonia.Input.PhysicalKey.Escape,Avalonia.Input.RawInputModifiers.None);}
            await Pump();apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Pump();
            Assert.Equal(original,JsonSerializer.Serialize(match.ToState()));Assert.Equal(replacement,JsonSerializer.Serialize(next.ToState()));Assert.Equal(0,changes);
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaFact]
    public async Task RapidOrdinaryRunClicksRemainDistinctWhileARepeatedExtraConfirmationIsIdempotent()
    {
        var match=Roche_Scoreboard.Services.CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=850,Height=650,Content=panel};window.Show();
        try
        {
            await Task.Delay(400);Dispatcher.UIThread.RunJobs();
            var run=panel.GetVisualDescendants().OfType<Button>().Single(b=>Equals(Avalonia.Controls.ToolTip.GetTip(b),"1 run"));
            run.BringIntoView();await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            for(int i=0;i<5;i++)
            {
                var point=run.TranslatePoint(new Point(run.Bounds.Width/2,run.Bounds.Height/2),window)!.Value;
                window.MouseDown(point,Avalonia.Input.MouseButton.Left);window.MouseUp(point,Avalonia.Input.MouseButton.Left);Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(5,match.TotalRuns);Assert.Equal(5,match.CurrentInnings!.Deliveries.Count);
            panel.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Wide")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);Dispatcher.UIThread.RunJobs();var apply=panel.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ApplyButton");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(6,match.TotalRuns);Assert.Equal(1,match.CurrentInnings.Wides);Assert.Equal(6,match.CurrentInnings.Deliveries.Count);Assert.Equal(5,match.CurrentInnings.LegalBallsBowled);
        }
        finally{panel.ClearMatch();window.Close();}
    }
}
