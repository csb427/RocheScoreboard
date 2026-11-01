using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    [AvaloniaTheory]
    [InlineData("code")][InlineData("same-code-host")][InlineData("hide")]
    [Trait("Evidence","TrainingCloudEnd")]
    public async Task TrainingCloudEndDoesNotDispatchAfterPopupClosureReplacesItsSession(string boundary)
    {
        await using var f=await Fixture.Create();string before=ClearFullState(f.Session);var launcher=await OpenTrainingCloudDisclosure(f);
        if(boundary=="same-code-host")launcher.ApplyState(true,true,"ABC123","https://remote.example.invalid/join/ABC123",1);
        var popup=Named<Popup>(launcher,"DetailsPopup");int calls=0,closes=0;
        launcher.EndRequested+=(_,_)=>calls++;
        EventHandler<EventArgs> closed=(_,_)=> {
            closes++;
            if(boundary=="hide")f.Window.Hide();
            else if(boundary=="same-code-host")launcher.ApplyState(true,true,"ABC123","https://remote.example.invalid/join/ABC123",2);
            else launcher.ApplyState(true,true,"REPLACED","https://remote.example.invalid/new");
        };
        popup.Closed+=closed;
        try {
            var end=Named<Button>(launcher,"EndButton");await TabTo(f.Window,end);Key(f.Window,PhysicalKey.Enter);await Settle(120);
            Assert.Equal(1,closes);Assert.Equal(0,calls);Assert.False(launcher.IsExpanded);Assert.Equal(before,ClearFullState(f.Session));
        }
        finally{popup.Closed-=closed;}
    }
}
