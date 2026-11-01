using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketWizardInterruptionTests
{
    private static async Task SquadReveal(Fixture f,Control control)
    {
        f.Window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);
        try{Assert.True(control.Focus(NavigationMethod.Tab));control.BringIntoView();Dispatcher.UIThread.RunJobs();}
        finally{f.Window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);}
        await Settle(150);
    }
    private static void SquadPointer(Fixture f,Control control,bool activate=true)
    {
        using var rendered=f.Window.CaptureRenderedFrame();var box=new Rect(control.TranslatePoint(default,f.Window)!.Value,control.Bounds.Size);Assert.True(new Rect(f.Window.ClientSize).Contains(box));
        var points=new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)};
        var probes=points.Select(point=>{var hit=f.Window.InputHitTest(point);return new{point,owned=ReferenceEquals(hit,control)||hit is Visual visual&&visual.GetVisualAncestors().Contains(control),type=hit?.GetType().Name};}).ToArray();
        if(!activate && Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"remove-geometry-"+f.Window.ClientSize.Width+".json"),System.Text.Json.JsonSerializer.Serialize(new{box,probes,ancestors=control.GetVisualAncestors().OfType<Control>().Select(x=>new{type=x.GetType().Name,x.Name,x.Bounds,point=x.TranslatePoint(default,f.Window),scroll=x is ScrollViewer scroll?new{scroll.Viewport,scroll.Extent,scroll.Offset}:null})},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        Assert.All(probes,probe=>Assert.True(probe.owned,$"Unowned pointer probe {probe.point} hit {probe.type}."));
        if(activate){f.Window.MouseMove(box.Center);f.Window.MouseDown(box.Center,MouseButton.Left);f.Window.MouseUp(box.Center,MouseButton.Left);}
    }
    [AvaloniaTheory]
    [InlineData(true,1280,850)][InlineData(false,420,320)]
    [Trait("Evidence","CricketSetupSquadRow")]
    public async Task SquadRowIdentifiesItsPlayerRolesAndReflectsAcceptedEdits(bool home,int width,int height)
    {
        using var f=new Fixture(width,height);await Settle(350);
        var player=new CricketPlayer{FirstName="Alexandria Catherine",LastName="Long player surname",IsCaptain=true,IsWicketkeeper=true};f.Players(home).Add(player);f.Step(2);await Settle(300);
        var edit=f.Tagged<Button>(home?"edit-home-player":"edit-away-player");var remove=f.Tagged<Button>(home?"remove-home-player":"remove-away-player");await SquadReveal(f,edit);
        PlayerEditorFrame(f,$"squad-row-{home}-{width}");SquadPointer(f,remove,false);
        Assert.Equal("Edit player "+player.FullName,ControlAutomationPeer.CreatePeerForElement(edit)!.GetName());Assert.Equal("Remove player "+player.FullName,ControlAutomationPeer.CreatePeerForElement(remove)!.GetName());
        var name=Assert.Single(f.Wizard.GetVisualDescendants().OfType<TextBlock>(),x=>ReferenceEquals(x.DataContext,player)&&Equals(x.Tag,"squad-player-name"));
        var row=Assert.Single(name.GetVisualAncestors().OfType<Border>(),x=>Equals(x.Tag,"squad-player-row"));f.AssertWithin(name,row);
        var captain=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"squad-role-captain"));var keeper=Assert.Single(row.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"squad-role-keeper"));Assert.True(captain.IsVisible);Assert.True(keeper.IsVisible);
        player.IsCaptain=false;await Settle(100);Assert.False(captain.IsVisible);Assert.True(keeper.IsVisible);
        SquadPointer(f,edit);await Settle(150);var first=f.Tagged<TextBox>("new-player-first");Assert.True(first.Focus(NavigationMethod.Tab));first.SelectAll();f.Window.KeyTextInput("Updated");Click(f.Tagged<Button>("apply"));await Settle(300);
        Assert.Equal("Updated Long player surname",name.Text);Assert.Equal("Edit player "+player.FullName,ControlAutomationPeer.CreatePeerForElement(edit)!.GetName());Assert.Equal("Remove player "+player.FullName,ControlAutomationPeer.CreatePeerForElement(remove)!.GetName());
        Assert.False(captain.IsVisible);Assert.True(keeper.IsVisible);Assert.Same(player,Assert.Single(f.Players(home)));Assert.Empty(f.Players(!home));Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }
}
