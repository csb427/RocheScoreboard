using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class OperatorActionBorderSemanticTests
{
    [AvaloniaTheory]
    [InlineData(false,PhysicalKey.Enter)][InlineData(false,PhysicalKey.Space)]
    [InlineData(true,PhysicalKey.Enter)][InlineData(true,PhysicalKey.Space)]
    public void ActionKeyboardUsesOneOptInCallbackOrOriginalReleaseAndActivated(bool semantic,PhysicalKey key)
        =>Exercise(semantic,key);

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public void ActionInvokeUsesOneOptInCallbackOrOriginalReleaseAndActivated(bool semantic)
        =>Exercise(semantic,null);

    private static void Exercise(bool semantic,PhysicalKey? key)
    {
        int releases=0,activated=0,chosen=0,presses=0;
        var tile=new OperatorActionBorder{Width=120,Height=80,Background=Brushes.Blue};
        tile.PointerPressed+=(_,_)=>presses++;
        tile.PointerReleased+=(_,e)=>{Assert.Equal(MouseButton.Left,e.InitialPressMouseButton);releases++;};
        tile.Activated+=(_,_)=>activated++;
        if(semantic)tile.ActivationAction=()=>chosen++;
        var window=new NativeWindow{Width=420,Height=300,Content=tile};window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();Assert.True(tile.Focus());Assert.True(tile.IsFocused);
            if(key is { } physical){window.KeyPressQwerty(physical,RawInputModifiers.None);window.KeyReleaseQwerty(physical,RawInputModifiers.None);}
            else Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(tile)).Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(semantic?1:0,chosen);Assert.Equal(semantic?0:1,releases);Assert.Equal(semantic?0:1,activated);Assert.Equal(0,presses);
            var provider=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(tile));
            tile.IsEnabled=false;provider.Invoke();Dispatcher.UIThread.RunJobs();
            Assert.Equal(semantic?1:0,chosen);Assert.Equal(semantic?0:1,releases);Assert.Equal(semantic?0:1,activated);
            tile.IsEnabled=true;provider.Invoke();tile.IsVisible=false;Dispatcher.UIThread.RunJobs();
            Assert.Equal(semantic?1:0,chosen);Assert.Equal(semantic?0:1,releases);Assert.Equal(semantic?0:1,activated);Assert.Equal(0,presses);
        }
        finally{window.Close();}
    }
}
