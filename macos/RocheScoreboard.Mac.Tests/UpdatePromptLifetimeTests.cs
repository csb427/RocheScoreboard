using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Source-seam baseline: actual public dialog/actions with an in-memory updater
// dependency. The real Windows/Mac service, network, file staging, browser,
// process launcher and App shutdown are never invoked by these cases.
public sealed class UpdatePromptLifetimeTests
{
    private sealed class Operation
    {
        internal readonly TaskCompletionSource<bool> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IProgress<UpdateProgressInfo>? Progress;
        internal CancellationToken Token;
        internal int Calls, CancellationCallbacks;
        internal sealed record Attempt(IProgress<UpdateProgressInfo>? Progress, CancellationToken Token, TaskCompletionSource<bool> Completion);
        internal readonly List<Attempt> Attempts=[];
        internal Task<bool> Run(IProgress<UpdateProgressInfo>? progress, CancellationToken token)
        {
            Calls++; Progress=progress; Token=token;
            token.Register(()=>CancellationCallbacks++);
            var completion=Calls==1?Result:new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Attempts.Add(new(progress,token,completion));return completion.Task;
        }
        internal void Report(string message)=>Progress!.Report(new(UpdateProgressStage.Downloading,.25,25,100,1,3,message));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly bool? oldMotion=OperatorMotion.AnimationOverride;
        internal readonly NativeWindow Owner;
        internal readonly UpdatePromptWindow Dialog;
        internal readonly Operation Op=new();
        internal readonly Task<bool?> Completion;
        internal int Closing, CancelledClosing, Closed;
        internal Fixture()
        {
            OperatorMotion.AnimationOverride=false;
            NativeKeyboard.EnsureInitialized();
            Owner=new NativeWindow { Width=900, Height=700, Content=new Button { Content="Owner" } };
            Owner.Show(); Owner.Activate(); Dispatcher.UIThread.RunJobs();
            Dialog=new UpdatePromptWindow(new AutoUpdateService(),new GitHubRelease { TagName="v99.0.0", Body="In-memory UI regression. No update will run.",Assets=[] },Op.Run) { Owner=Owner };
            Dialog.Closing+=(_,e)=>{Closing++;if(e.Cancel)CancelledClosing++;};
            Dialog.Closed+=(_,_)=>Closed++;
            Completion=((Window)Dialog).ShowDialog<bool?>(Owner);
        }
        internal T Named<T>(string name)where T:Control=>Assert.IsAssignableFrom<T>(Dialog.FindControl<Control>(name));
        public void Dispose()
        {
            foreach(var attempt in Op.Attempts)attempt.Completion.TrySetResult(false);
            Dispatcher.UIThread.RunJobs();
            if(Dialog.IsVisible)Dialog.Close();
            Dispatcher.UIThread.RunJobs();Owner.Close();OperatorMotion.AnimationOverride=oldMotion;
        }
    }
    private static async Task Until(Func<bool> condition,string message)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!condition()&&watch.ElapsedMilliseconds<5000){await Task.Delay(10);Dispatcher.UIThread.RunJobs();}
        Assert.True(condition(),message);
    }
    private static async Task Pointer(Fixture f,string name)
    {
        var button=f.Named<Button>(name);button.BringIntoView();
        bool Ready()
        {
            if(!button.IsLoaded||!button.IsEffectivelyVisible||!button.IsEffectivelyEnabled||!button.IsHitTestVisible||button.Bounds.Width<=0||button.Bounds.Height<=0)return false;
            var top=button.TranslatePoint(default,f.Dialog);if(top is null)return false;
            var bounds=new Rect(top.Value,button.Bounds.Size);if(!new Rect(f.Dialog.ClientSize).Contains(bounds))return false;
            var probes=new[]{bounds.Center,new Point(bounds.Left+4,bounds.Center.Y),new Point(bounds.Right-4,bounds.Center.Y),new Point(bounds.Center.X,bounds.Top+4),new Point(bounds.Center.X,bounds.Bottom-4)};
            return probes.All(point=>f.Dialog.InputHitTest(point) is Visual hit&&(ReferenceEquals(hit,button)||hit.GetVisualAncestors().Contains(button)));
        }
        await Until(Ready,name+" must own five real hit probes in its actual modal viewport before clicking");
        var origin=button.TranslatePoint(default,f.Dialog);Assert.NotNull(origin);
        var box=new Rect(origin.Value,button.Bounds.Size);var point=box.Center;
        f.Dialog.MouseMove(point);f.Dialog.MouseDown(point,MouseButton.Left);f.Dialog.MouseUp(point,MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
    private static object Snapshot(Fixture f)=>new
    {
        visible=f.Dialog.IsVisible,complete=f.Completion.IsCompleted,applied=f.Dialog.UpdateApplied,
        operationComplete=f.Op.Attempts.LastOrDefault()?.Completion.Task.IsCompleted??false,
        f.Closing,f.CancelledClosing,f.Closed,calls=f.Op.Calls,cancelled=f.Op.Token.IsCancellationRequested,
        status=f.Named<TextBlock>("ProgressText").Text,detail=f.Named<TextBlock>("ProgressDetailText").Text,
        percent=f.Named<TextBlock>("ProgressPercentText").Text,updateEnabled=f.Named<Button>("UpdateButton").IsEnabled,
        width=f.Dialog.Bounds.Width,height=f.Dialog.Bounds.Height
    };
    private static void Record(Fixture f,string stage,object value)
    {
        string dir=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Private captures required"),stage);
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"observation.json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        if(f.Dialog.IsVisible){using var frame=f.Dialog.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,"dialog.png"));}
    }
    private static async Task Begin(Fixture f)
    {
        await Pointer(f,"UpdateButton");await Until(()=>f.Op.Calls==1,"Actual Update click must call only fake updater once");
        Assert.False(f.Op.Token.IsCancellationRequested);Assert.False(f.Named<Button>("UpdateButton").IsEnabled);
        Assert.Equal(0,f.Closed);Assert.False(f.Completion.IsCompleted);
    }
    [AvaloniaFact]
    public async Task SuccessfulOperationClosesOnceWithoutRequestingCancellation()
    {
        using var f=new Fixture();await Begin(f);f.Op.Result.SetResult(true);
        await Until(()=>f.Dialog.UpdateApplied,"The fake success must reach actual completion branch");
        var state=Snapshot(f);Record(f,"success-close",state);
        Assert.False(f.Op.Token.IsCancellationRequested);
        Assert.False(f.Dialog.IsVisible);Assert.True(f.Completion.IsCompletedSuccessfully);
        Assert.Equal(1,f.Closing);Assert.Equal(0,f.CancelledClosing);Assert.Equal(1,f.Closed);Assert.Equal(1,f.Op.Calls);
    }
    [AvaloniaFact]
    public async Task CancellationReturnedAsFalseKeepsTruthfulCancellationState()
    {
        using var f=new Fixture();await Begin(f);await Pointer(f,"SkipButton");
        Assert.True(f.Op.Token.IsCancellationRequested);Assert.True(f.Dialog.IsVisible);Assert.False(f.Completion.IsCompleted);
        Assert.Contains("Cancelling",f.Named<TextBlock>("ProgressText").Text);
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Cancelled fake operation must finish its busy state");
        Record(f,"cancel-false",Snapshot(f));
        Assert.Equal("Download cancelled",f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal("STOPPED",f.Named<TextBlock>("ProgressPercentText").Text);
        Assert.False(f.Dialog.UpdateApplied);Assert.Equal(1,f.Op.Calls);Assert.Equal(0,f.Closed);
    }
    [AvaloniaFact]
    public async Task LateProgressCannotReplaceFinishedFailureState()
    {
        using var f=new Fixture();await Begin(f);f.Op.Report("Current operation downloading");
        await Until(()=>f.Named<TextBlock>("ProgressText").Text=="Current operation downloading","Current progress positive control must render");
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Fake failure must settle before retained progress stimulus");
        string? status=f.Named<TextBlock>("ProgressText").Text;string? percent=f.Named<TextBlock>("ProgressPercentText").Text;
        f.Op.Report("Obsolete queued progress");await Task.Delay(30);Dispatcher.UIThread.RunJobs();
        Record(f,"late-failure-progress",Snapshot(f));
        Assert.Equal(status,f.Named<TextBlock>("ProgressText").Text);Assert.Equal(percent,f.Named<TextBlock>("ProgressPercentText").Text);
        Assert.False(f.Dialog.UpdateApplied);Assert.Equal(1,f.Op.Calls);Assert.True(f.Named<Button>("UpdateButton").IsEnabled);
    }
    [AvaloniaFact]
    public async Task InitialCancelClosesWithoutStartingAnyOperation()
    {
        using var f=new Fixture();await Pointer(f,"SkipButton");await Until(()=>f.Completion.IsCompleted,"Initial actual cancel must complete dialog");
        Record(f,"initial-cancel",Snapshot(f));Assert.Equal(0,f.Op.Calls);Assert.False(f.Dialog.UpdateApplied);
        Assert.False(f.Dialog.IsVisible);Assert.Equal(1,f.Closed);Assert.Equal(0,f.CancelledClosing);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosePurposeDescribesItsActualIdleOrBusyAction(bool busy)
    {
        using var f=new Fixture();
        if(busy) await Begin(f);
        else await Until(()=>f.Named<Button>("CloseButton").IsLoaded,"Actual close target must be mounted");
        var close=f.Named<Button>("CloseButton");
        string? name=ControlAutomationPeer.CreatePeerForElement(close)?.GetName();
        string? tip=ToolTip.GetTip(close)?.ToString();
        Record(f,"purpose-"+busy,new{state=Snapshot(f),name,tip});
        Assert.Equal(busy?"Cancel update":"Remind me later",name);
        Assert.Equal(busy?"Cancel update":"Remind me later",tip);
        Assert.Equal(busy?1:0,f.Op.Calls);Assert.False(f.Dialog.UpdateApplied);
    }
    [AvaloniaFact]
    public async Task InitialDefaultAndFocusBelongToSafeDismissAction()
    {
        using var f=new Fixture();
        await Until(()=>f.Named<Button>("SkipButton").IsLoaded,"Actual safe default must be mounted");
        Dispatcher.UIThread.RunJobs();
        var update=f.Named<Button>("UpdateButton");var skip=f.Named<Button>("SkipButton");
        Record(f,"safe-idle-default",new{state=Snapshot(f),updateDefault=update.IsDefault,skipDefault=skip.IsDefault,
            skipFocused=skip.IsFocused,focus=(f.Dialog.FocusManager?.GetFocusedElement() as Control)?.Name});
        Assert.False(update.IsDefault);Assert.True(skip.IsDefault);Assert.True(skip.IsFocused);
        Assert.Equal(0,f.Op.Calls);Assert.False(f.Dialog.UpdateApplied);
    }

    [AvaloniaFact]
    public async Task OldProgressCannotOverwriteAReplacementRetry()
    {
        using var f=new Fixture();await Begin(f);var old=f.Op.Attempts[0].Progress!;
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"First fake failure must finish before retry");
        await Pointer(f,"UpdateButton");await Until(()=>f.Op.Calls==2,"Actual retry starts exactly one new fake operation");
        Assert.False(f.Op.Token.IsCancellationRequested);
        f.Op.Report("Current retry progress");await Until(()=>f.Named<TextBlock>("ProgressText").Text=="Current retry progress","New operation positive progress must render");
        old.Report(new(UpdateProgressStage.Downloading,.9,90,100,1,3,"Obsolete first operation"));
        await Task.Delay(30);Dispatcher.UIThread.RunJobs();Record(f,"retry-progress",Snapshot(f));
        Assert.Equal("Current retry progress",f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal(25,f.Named<ProgressBar>("UpdateProgressBar").Value);
        Assert.False(f.Named<Button>("UpdateButton").IsEnabled);Assert.Equal(2,f.Op.Calls);
        f.Op.Attempts[1].Completion.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Fake retry must settle");
    }
    [AvaloniaFact]
    public async Task OwnerCloseWaitsForCancellationThenRetiresTheClosedWindowProgress()
    {
        using var f=new Fixture();await Begin(f);var progress=f.Op.Progress!;
        f.Owner.Close();await Until(()=>f.Op.Token.IsCancellationRequested,"Actual owner close must request safe cancellation while its owned operation is pending");
        Assert.True(f.Dialog.IsVisible);Assert.False(f.Completion.IsCompleted);
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Owner-close cancellation must settle before a second close");
        string? status=f.Named<TextBlock>("ProgressText").Text;
        f.Owner.Close();await Until(()=>!f.Dialog.IsVisible,"Settled owned window closes with its actual owner");
        progress.Report(new(UpdateProgressStage.Ready,1,100,100,1,3,"Obsolete completed operation"));
        await Task.Delay(50);Dispatcher.UIThread.RunJobs();Record(f,"closed-owner",Snapshot(f));
        Assert.False(f.Dialog.UpdateApplied);Assert.Equal(status,f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal(1,f.Closed);Assert.True(f.Op.Token.IsCancellationRequested);Assert.Equal(1,f.Op.Calls);
    }
    [AvaloniaFact]
    public async Task ThrownCancellationRetainsTheExistingStoppedContract()
    {
        using var f=new Fixture();await Begin(f);await Pointer(f,"SkipButton");
        f.Op.Result.SetException(new OperationCanceledException(f.Op.Token));
        await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Thrown fake cancellation must leave busy state");
        Record(f,"cancel-exception",Snapshot(f));Assert.Equal("Download cancelled",f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal("STOPPED",f.Named<TextBlock>("ProgressPercentText").Text);Assert.True(f.Dialog.IsVisible);
        Assert.False(f.Dialog.UpdateApplied);Assert.Equal(1,f.Op.Calls);Assert.Equal(1,f.Op.CancellationCallbacks);
    }
    [AvaloniaTheory]
    [InlineData("header")]
    [InlineData("escape")]
    [InlineData("window-close")]
    public async Task BusyDismissalWaitsForCancellationAndRejectsFurtherProgress(string route)
    {
        using var f=new Fixture();await Begin(f);
        if(route=="header")await Pointer(f,"CloseButton");
        else if(route=="escape")
        {f.Dialog.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);f.Dialog.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);}
        else f.Dialog.Close();
        await Until(()=>f.Op.Token.IsCancellationRequested,"Actual dismissal route must request fake-operation cancellation");
        Assert.True(f.Dialog.IsVisible);Assert.False(f.Completion.IsCompleted);
        Assert.False(f.Named<Button>("CloseButton").IsEnabled);Assert.False(f.Named<Button>("SkipButton").IsEnabled);
        f.Op.Report("Obsolete progress during cancellation");await Task.Delay(30);Dispatcher.UIThread.RunJobs();
        Record(f,"busy-dismiss-"+route,Snapshot(f));Assert.Equal("Cancelling safely…",f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal(1,f.Op.Calls);Assert.Equal(1,f.Op.CancellationCallbacks);
        f.Op.Result.SetResult(false);await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Cancelled fake work must settle");
        Assert.Equal("Download cancelled",f.Named<TextBlock>("ProgressText").Text);
        await Pointer(f,"SkipButton");await Until(()=>f.Completion.IsCompleted,"Idle cancel remains available after cancellation");
        Assert.Equal(1,f.Closed);Assert.False(f.Dialog.UpdateApplied);
    }
    [AvaloniaFact]
    public async Task InitialRealEnterDismissesWithoutStartingAnOperation()
    {
        using var f=new Fixture();await Until(()=>f.Named<Button>("SkipButton").IsLoaded,"Dialog must mount before actual Enter");
        Dispatcher.UIThread.RunJobs();
        f.Dialog.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);
        (f.Dialog.IsVisible?(Window)f.Dialog:f.Owner).KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);
        await Until(()=>f.Completion.IsCompleted||f.Op.Calls>0,"Actual default Enter must produce a dialog action");
        Record(f,"initial-enter",Snapshot(f));Assert.Equal(0,f.Op.Calls);
        Assert.False(f.Dialog.UpdateApplied);Assert.True(f.Completion.IsCompleted);Assert.False(f.Dialog.IsVisible);Assert.Equal(1,f.Closed);
    }
    [AvaloniaFact]
    public async Task RapidRealUpdateClicksStartOnlyOnePendingOperation()
    {
        using var f=new Fixture();await Begin(f);var button=f.Named<Button>("UpdateButton");
        var origin=button.TranslatePoint(default,f.Dialog);Assert.NotNull(origin);var point=origin.Value+new Vector(button.Bounds.Width/2,button.Bounds.Height/2);
        for(int i=0;i<4;i++){f.Dialog.MouseDown(point,MouseButton.Left);f.Dialog.MouseUp(point,MouseButton.Left);}
        Dispatcher.UIThread.RunJobs();Record(f,"rapid-update",Snapshot(f));Assert.Equal(1,f.Op.Calls);
        Assert.False(f.Dialog.UpdateApplied);Assert.False(f.Completion.IsCompleted);Assert.False(f.Op.Token.IsCancellationRequested);
    }

    [AvaloniaFact]
    public async Task SuccessfulCompletionAfterLateCancelWinsWithoutReportingRollback()
    {
        using var f=new Fixture();await Begin(f);await Pointer(f,"SkipButton");Assert.True(f.Op.Token.IsCancellationRequested);
        f.Op.Result.SetResult(true);await Until(()=>f.Dialog.UpdateApplied,"Authoritative fake success must win a late cancellation request");
        Record(f,"late-cancel-success",Snapshot(f));Assert.True(f.Dialog.UpdateApplied);
        Assert.False(f.Dialog.IsVisible);Assert.True(f.Completion.IsCompletedSuccessfully);Assert.Equal(1,f.Closed);
        Assert.Equal(0,f.CancelledClosing);Assert.Equal(1,f.Op.Calls);Assert.Equal(1,f.Op.CancellationCallbacks);
    }
    [AvaloniaFact]
    public async Task NonCancellationExceptionIsRecoverableAndOldProgressCannotRetargetRetrySuccess()
    {
        using var f=new Fixture();await Begin(f);var old=f.Op.Progress!;
        f.Op.Result.SetException(new InvalidOperationException("Synthetic stage failure"));
        await Until(()=>f.Named<Button>("UpdateButton").IsEnabled,"Fake exception must leave busy state");
        Assert.Equal("Update was not installed",f.Named<TextBlock>("ProgressText").Text);
        Assert.Equal("Synthetic stage failure",f.Named<TextBlock>("ProgressDetailText").Text);
        Assert.Equal("SAFE",f.Named<TextBlock>("ProgressPercentText").Text);Assert.False(f.Dialog.UpdateApplied);
        await Pointer(f,"UpdateButton");await Until(()=>f.Op.Calls==2,"Actual retry follows fake exception");
        f.Op.Report("Current retry after exception");await Until(()=>f.Named<TextBlock>("ProgressText").Text=="Current retry after exception","Current retry progress remains accepted");
        old.Report(new(UpdateProgressStage.Downloading,.9,90,100,1,3,"Obsolete failed operation"));
        await Task.Delay(30);Dispatcher.UIThread.RunJobs();Record(f,"exception-retry",Snapshot(f));
        Assert.Equal("Current retry after exception",f.Named<TextBlock>("ProgressText").Text);
        f.Op.Attempts[1].Completion.SetResult(true);await Until(()=>f.Completion.IsCompleted,"Fake retry success must close actual dialog");
        Assert.True(f.Dialog.UpdateApplied);Assert.False(f.Dialog.IsVisible);Assert.Equal(1,f.Closed);Assert.Equal(2,f.Op.Calls);
        Assert.Equal(0,f.Op.CancellationCallbacks);
    }

    [AvaloniaFact]
    public async Task PendingCancellationDoesNotClaimCleanupBeforeAcknowledgement()
    {
        using var f=new Fixture();await Begin(f);await Pointer(f,"SkipButton");
        Assert.False(f.Op.Result.Task.IsCompleted,"Cancellation request must still be pending acknowledgement by the actual in-memory operation");
        Record(f,"pending-cancel-truth",Snapshot(f));
        Assert.True(f.Op.Token.IsCancellationRequested);Assert.True(f.Dialog.IsVisible);Assert.False(f.Dialog.UpdateApplied);
        Assert.False(f.Completion.IsCompleted);
        Assert.Equal("Cancellation requested. Waiting for the update operation to finish safely.",f.Named<TextBlock>("ProgressDetailText").Text);
        Assert.Equal(1,f.Op.Calls);
    }

}
