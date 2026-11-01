using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketWizardInterruptionTests
{
    private static Button SquadAction(Fixture f,bool home,bool edit,CricketPlayer player)=>Assert.Single(f.Wizard.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,(edit?"edit-":"remove-")+(home?"home-player":"away-player"))&&ReferenceEquals(x.DataContext,player));
    [AvaloniaTheory]
    [InlineData(true,1280,850)][InlineData(false,420,320)]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task LatestSquadRemovalRestoresTheSamePlayerAtItsOriginalPosition(bool home,int width,int height)
    {
        using var f=new Fixture(width,height);await Settle(300);f.Window.Activate();Assert.True(f.Window.IsActive);
        var first=new CricketPlayer{FirstName="Alpha",LastName="One",IsCaptain=true,IsWicketkeeper=true};var second=new CricketPlayer{FirstName="Beta",LastName="Two"};var third=new CricketPlayer{FirstName="Gamma",LastName="Three"};
        f.Players(home).Add(first);f.Players(home).Add(second);f.Players(home).Add(third);string before=System.Text.Json.JsonSerializer.Serialize(f.Players(home));f.Step(2);await Settle(300);
        var remove=SquadAction(f,home,false,first);await SquadReveal(f,remove);SquadPointer(f,remove);await Settle(200);PlayerEditorFrame(f,$"squad-removal-{home}-{width}");
        Assert.Equal(new[]{second,third},f.Players(home));Assert.True(SquadAction(f,home,true,second).IsKeyboardFocusWithin);
        var undo=f.Tagged<Button>(home?"undo-home-player-removal":"undo-away-player-removal");await SquadReveal(f,undo);SquadPointer(f,undo);await Settle(250);
        Assert.Equal(new[]{first,second,third},f.Players(home));Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(f.Players(home)));Assert.True(SquadAction(f,home,true,first).IsKeyboardFocusWithin);
        Assert.Empty(f.Players(!home));Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);Assert.Null(f.Wizard.Result);
    }
    private static async Task<Button> RemoveForRecovery(Fixture f,bool home,CricketPlayer player)
    {
        f.Step(2);await Settle(250);var remove=SquadAction(f,home,false,player);await SquadReveal(f,remove);SquadPointer(f,remove);await Settle(200);
        return f.Tagged<Button>(home?"undo-home-player-removal":"undo-away-player-removal");
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task RemovingTheOnlyPlayerReturnsToAddAndUndoReturnsToEdit()
    {
        using var f=new Fixture(420,320);await Settle(300);f.Window.Activate();var player=new CricketPlayer{FirstName="Only",LastName="Player"};f.Players(true).Add(player);
        var undo=await RemoveForRecovery(f,true,player);Assert.Empty(f.Players(true));Assert.True(f.Tagged<Button>("add-home-player").IsKeyboardFocusWithin);
        await SquadReveal(f,undo);SquadPointer(f,undo);await Settle(200);Assert.Same(player,Assert.Single(f.Players(true)));Assert.True(SquadAction(f,true,true,player).IsKeyboardFocusWithin);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task ResetOrHideRetiresCachedRemovalActions(bool hide)
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Alpha"};var beta=new CricketPlayer{FirstName="Beta"};f.Players(true).Add(alpha);f.Players(true).Add(beta);
        var undo=await RemoveForRecovery(f,true,alpha);
        if(hide){f.Wizard.IsVisible=false;await Settle(50);f.Wizard.IsVisible=true;}else f.Wizard.Reset();await Settle(150);
        string before=System.Text.Json.JsonSerializer.Serialize(f.Players(true));Click(undo);await Settle(150);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(f.Players(true)));Assert.DoesNotContain(alpha,f.Players(true));
        Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"undo-home-player-removal"));Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task PlayerDraftBlocksUndoWhileOpenAndCancelPreservesTheRemovalRecovery()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Alpha"};var beta=new CricketPlayer{FirstName="Beta"};f.Players(true).Add(alpha);f.Players(true).Add(beta);
        var undo=await RemoveForRecovery(f,true,alpha);await f.OpenPlayer(true);Click(undo);await Settle(100);Assert.Equal(new[]{beta},f.Players(true));
        Assert.Single(f.Wizard.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));f.Escape();await Settle(250);
        await SquadReveal(f,undo);SquadPointer(f,undo);await Settle(200);Assert.Equal(new[]{alpha,beta},f.Players(true));Assert.Equal(0,f.Cancellations);Assert.Equal(0,f.Completions);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task ObsoleteUndoCannotOverrideANewerCaptainSelection()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Alpha",IsCaptain=true};var beta=new CricketPlayer{FirstName="Beta"};f.Players(true).Add(alpha);f.Players(true).Add(beta);
        var undo=await RemoveForRecovery(f,true,alpha);beta.IsCaptain=true;string before=System.Text.Json.JsonSerializer.Serialize(f.Players(true));Click(undo);await Settle(200);
        Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(f.Players(true)));Assert.Same(beta,Assert.Single(f.Players(true)));Assert.True(beta.IsCaptain);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task APreviousRemovalButtonCannotRestoreTheLatestRemovalInstead()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Alpha"};var beta=new CricketPlayer{FirstName="Beta"};var gamma=new CricketPlayer{FirstName="Gamma"};f.Players(true).Add(alpha);f.Players(true).Add(beta);f.Players(true).Add(gamma);
        var old=await RemoveForRecovery(f,true,alpha);var current=await RemoveForRecovery(f,true,beta);Assert.NotSame(old,current);Click(old);await Settle(100);Assert.Equal(new[]{gamma},f.Players(true));
        await SquadReveal(f,current);SquadPointer(f,current);await Settle(200);Assert.Equal(new[]{beta,gamma},f.Players(true));Assert.DoesNotContain(alpha,f.Players(true));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecovery")]
    public async Task AcceptedPlayerEditRetiresThePreviousRemovalWithoutChangingItsNewCaptain()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Alpha",IsCaptain=true};var beta=new CricketPlayer{FirstName="Beta"};f.Players(true).Add(alpha);f.Players(true).Add(beta);
        var undo=await RemoveForRecovery(f,true,alpha);var edit=SquadAction(f,true,true,beta);await SquadReveal(f,edit);SquadPointer(f,edit);await Settle(150);
        var captain=f.Tagged<CheckBox>("new-player-captain");Assert.True(captain.Focus(NavigationMethod.Tab));f.Window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);Assert.True(captain.IsChecked);
        Click(f.Tagged<Button>("apply"));await Settle(300);Assert.True(beta.IsCaptain);Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"undo-home-player-removal"));
        string before=System.Text.Json.JsonSerializer.Serialize(f.Players(true));Click(undo);await Settle(100);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(f.Players(true)));Assert.Same(beta,Assert.Single(f.Players(true)));Assert.True(beta.IsCaptain);Assert.Equal(0,f.Completions);
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecoveryBoundary")]
    public async Task ResetDuringUndoCardRetirementCannotRestoreAnOldPlayerIntoNewSetup()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Old Alpha"};var beta=new CricketPlayer{FirstName="Old Beta"};var current=new CricketPlayer{FirstName="New setup"};f.Players(true).Add(alpha);f.Players(true).Add(beta);
        var undo=await RemoveForRecovery(f,true,alpha);var card=Assert.Single(f.Wizard.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"squad-removal-recovery"));bool reset=false;Exception? failure=null;
        card.DetachedFromVisualTree+=(_,_)=>{if(reset)return;reset=true;try{f.Wizard.Reset();f.Players(true).Add(current);f.Step(2);}catch(Exception error){failure=error;}};
        await SquadReveal(f,undo);SquadPointer(f,undo);await Settle(250);Assert.True(reset);Assert.Null(failure);Assert.Same(current,Assert.Single(f.Players(true)));Assert.DoesNotContain(alpha,f.Players(true));
        Assert.DoesNotContain(f.Wizard.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"undo-home-player-removal"));
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSquadRemovalRecoveryBoundary")]
    public async Task ResetDuringRecoveryPublicationCannotShowAnObsoleteRecoveryHost()
    {
        using var f=new Fixture();await Settle(300);var alpha=new CricketPlayer{FirstName="Old Alpha"};var beta=new CricketPlayer{FirstName="Old Beta"};var current=new CricketPlayer{FirstName="New setup"};f.Players(true).Add(alpha);f.Players(true).Add(beta);f.Step(2);await Settle(250);
        var host=f.Tagged<ContentControl>("home-player-removal-recovery");bool reset=false;
        host.PropertyChanged+=(_,args)=>{if(reset || args.Property!=ContentControl.ContentProperty || args.NewValue is null)return;reset=true;f.Wizard.Reset();f.Players(true).Add(current);f.Step(2);};
        var remove=SquadAction(f,true,false,alpha);await SquadReveal(f,remove);SquadPointer(f,remove);await Settle(250);
        Assert.True(reset);Assert.Same(current,Assert.Single(f.Players(true)));Assert.Null(host.Content);Assert.False(host.IsVisible);
    }

}
