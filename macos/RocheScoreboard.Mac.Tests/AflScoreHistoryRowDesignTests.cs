using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class AflResetScopeTests
{
    private static Border ActualHistoryRow(Fixture f)=>Assert.Single(Named<StackPanel>(f.Window,"EventList").Children.OfType<Border>(),x=>ReferenceEquals(x.Tag,f.Match.Events[^1]));
    private static Button HistoryCancel(ScoreEventEditorWindow editor)=>Assert.Single(editor.GetVisualDescendants().OfType<Button>(),button=>Equals(button.Content,"CANCEL"));
    private static void HistoryFrame(Fixture f,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);using var frame=f.Window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(760,560)]
    [Trait("Evidence","AflScoreHistoryRow")]
    public async Task ActualScoreHistoryRowDoesNotMoveTheListOnHover(int width,int height)
    {
        using var f=await Fixture.Create();bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        try { OperatorAppearance.Configure(glass,false);f.Window.Width=width;f.Window.Height=height;await Settle(550);
        var row=ActualHistoryRow(f);await RevealByKeyboard(f.Window,row);await Settle(250);AssertPhysicalTarget(f.Window,row);
        Assert.True(row.Focusable);Assert.True(row.IsFocused);
        string before=Snapshot(f.Match);double heightBefore=row.Bounds.Height;HistoryFrame(f,$"history-rest-{width}");
        f.Window.MouseMove(PaintedBounds(f.Window,row).Center);await Settle(350);HistoryFrame(f,$"history-hover-{width}");
        Assert.Equal(heightBefore,row.Bounds.Height);Assert.Equal(before,Snapshot(f.Match));
        var hint=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"score-log-edit-hint"));
        Assert.True(hint.Bounds.Height>0);Assert.InRange(row.Bounds.Height,40,80);Assert.Contains(hint.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Edit");
        var ring=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"score-log-keyboard-focus"));Assert.True(ring.IsVisible);
        for(int i=0;i<3;i++){f.Window.MouseMove(new Avalonia.Point(1,1));await Settle(20);f.Window.MouseMove(PaintedBounds(f.Window,row).Center);await Settle(20);}
        OperatorAppearance.Configure(glass,true);await Settle(120);Assert.Equal(heightBefore,row.Bounds.Height);Assert.Equal(before,Snapshot(f.Match));
        } finally {OperatorAppearance.Configure(glass,reduced);}
    }
    [AvaloniaTheory]
    [InlineData("enter")][InlineData("space")][InlineData("invoke")][InlineData("pointer")]
    [Trait("Evidence","AflScoreHistoryRow")]
    public async Task ActualHistoryRowOpensOneOwnedEditorAndCancelPreservesMatch(string input)
    {
        using var f=await Fixture.Create();await Settle(300);var row=Assert.IsType<OperatorActionBorder>(ActualHistoryRow(f));
        await RevealByKeyboard(f.Window,row);AssertPhysicalTarget(f.Window,row);
        var peer=ControlAutomationPeer.CreatePeerForElement(row);Assert.NotNull(peer);Assert.Contains("Edit behind for Reset away",peer.GetName());
        var provider=Assert.IsAssignableFrom<IInvokeProvider>(peer);string before=Snapshot(f.Match);int changes=f.Changes;int opened=0;Exception? failure=null;
        void Trace(string step)
        {
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
            {Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"activation-"+input+".txt"),DateTime.UtcNow.ToString("O")+" "+step+"\n");}
        }
        Trace("dispatch prepared");
        Dispatcher.UIThread.Post(async () =>
        {
            ScoreEventEditorWindow? editor=null;
            try
            {
                Trace("callback entered");
                await Until(()=>
                {
                    editor=f.Window.OwnedWindows.OfType<ScoreEventEditorWindow>().SingleOrDefault();
                    return editor is {IsLoaded:true,IsVisible:true} && HistoryCancel(editor) is {IsLoaded:true,IsEffectivelyVisible:true,IsEffectivelyEnabled:true};
                },"Owned editor and Cancel must be loaded before sending cancellation input.");
                opened++;Trace("editor ready");Assert.Same(f.Window,editor!.Owner);Assert.Equal(before,Snapshot(f.Match));
                Assert.True(HistoryCancel(editor).Focus(NavigationMethod.Tab));
                editor.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);
                (editor.IsVisible ? (Window)editor : f.Window).KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();
                Trace("Escape returned; visible="+editor.IsVisible);
                Assert.False(editor.IsVisible,"Escape must close the ready owned editor.");
            }
            catch(Exception error){failure=error;Trace("callback failed: "+error);if(editor?.IsVisible==true)editor.Close();}
        });
        try
        {
            if(input=="invoke")provider.Invoke();else if(input=="pointer")await Tap(f.Window,row);else Key(f.Window,input=="enter"?PhysicalKey.Enter:PhysicalKey.Space);
            await Until(()=>opened==1 || failure is not null,"Actual row input must open exactly one owned event editor.");
            await Settle(150);Assert.Null(failure);Assert.Empty(f.Window.OwnedWindows.OfType<ScoreEventEditorWindow>());
            Assert.Equal(before,Snapshot(f.Match));Assert.Equal(changes,f.Changes);Assert.False(f.Match.ClockRunning);
            await Until(()=>row.IsFocused,"Cancelling the owned editor must return focus to its actual live history row.");
        }
        finally{foreach(var editor in f.Window.OwnedWindows.OfType<ScoreEventEditorWindow>().ToArray())editor.Close();}
    }

}
