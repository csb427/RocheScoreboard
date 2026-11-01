using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class QueuedInvokeCallbackIdentityTests
{
    [AvaloniaFact]
    public void ObsoleteQueuedInvokeCannotRetargetCallbackOrFallThroughToLegacyRelease()
    {
        int oldCalls=0,newCalls=0,releases=0,activated=0;
        var tile=new OperatorActionBorder{Width=120,Height=80,Background=Brushes.Blue};
        var sink=new TextBox{Text="Focus owner"};var panel=new StackPanel();panel.Children.Add(tile);panel.Children.Add(sink);
        var window=new NativeWindow{Width=420,Height=300,Content=panel};
        tile.PointerReleased+=(_,_)=>releases++;tile.Activated+=(_,_)=>activated++;
        Action oldAction=()=>oldCalls++,newAction=()=>{newCalls++;sink.Focus(NavigationMethod.Tab);};
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();Assert.True(tile.Focus(NavigationMethod.Tab));
            var peer=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(tile));
            tile.ActivationAction=oldAction;peer.Invoke();Assert.Equal(0,oldCalls);Assert.Equal(0,newCalls);
            tile.ActivationAction=newAction;Dispatcher.UIThread.RunJobs();
            Evidence("replace",oldCalls,newCalls,releases,activated,tile.IsFocused,sink.IsFocused);
            Assert.Equal(0,newCalls);Assert.Equal(0,oldCalls);Assert.Equal(0,releases);Assert.Equal(0,activated);Assert.True(tile.IsFocused);

            tile.ActivationAction=oldAction;peer.Invoke();tile.ActivationAction=null;Dispatcher.UIThread.RunJobs();
            Evidence("remove",oldCalls,newCalls,releases,activated,tile.IsFocused,sink.IsFocused);
            Assert.Equal(0,newCalls);Assert.Equal(0,oldCalls);Assert.Equal(0,releases);Assert.Equal(0,activated);Assert.True(tile.IsFocused);

            // Current unchanged identity must still execute once and retain its
            // legitimate focus effect; rejection must not disable future Invoke.
            tile.ActivationAction=newAction;peer.Invoke();Assert.Equal(0,newCalls);Dispatcher.UIThread.RunJobs();
            Assert.Equal(1,newCalls);Assert.Equal(0,oldCalls);Assert.True(sink.IsFocused);Assert.Equal(0,releases);Assert.Equal(0,activated);

            // A null callback from enqueue through dispatch keeps the original
            // legacy release + native Activated route exactly once.
            tile.ActivationAction=null;peer.Invoke();Dispatcher.UIThread.RunJobs();
            Assert.Equal(1,newCalls);Assert.Equal(0,oldCalls);Assert.Equal(1,releases);Assert.Equal(1,activated);
        }
        finally{window.Close();}
    }
    private static void Evidence(string stage,int oldCalls,int newCalls,int releases,int activated,bool tileFocused,bool sinkFocused)
    {
        string path=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!;Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"queued-invoke-"+stage+".json"),System.Text.Json.JsonSerializer.Serialize(new{stage,oldCalls,newCalls,releases,activated,tileFocused,sinkFocused,appAssembly=typeof(OperatorActionBorder).Assembly.Location,appSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(OperatorActionBorder).Assembly.Location)))},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
