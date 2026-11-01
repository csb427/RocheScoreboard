using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketWizardInterruptionTests
{
    // Exercise the production Finish route. Retire the old modal during the
    // before run so a failed inline-validation expectation cannot hang the host.
    private static bool FinishSquad(Fixture f)
    {
        bool modal=false;
        var retire=Dispatcher.UIThread.InvokeAsync(()=>
        {
            foreach(var window in f.Window.OwnedWindows.ToArray())
            {modal=true;window.Close(NativeMessageBoxResult.OK);}
        },DispatcherPriority.Background);
        try{Click(f.Named<Button>("NextButton"));}
        catch(Exception error) when(error.GetBaseException() is InvalidOperationException {Message:"A message dialog requires an open window."})
        {
            // The old global MessageBox path cannot find a desktop owner in
            // this headless host. Record that exact attempted route; do not
            // suppress any other error or pretend a native modal was viewed.
            modal=true;
        }
        finally{retire.Abort();}
        return modal;
    }

    [AvaloniaTheory]
    [InlineData(true,1280,850)][InlineData(false,420,320)]
    [Trait("Evidence","CricketSquadValidation")]
    public async Task RejectedSinglePlayerSquadExplainsAndRevealsItsOwnCorrection(bool home,int width,int height)
    {
        using var f=new Fixture(width,height);await Settle(300);f.Window.Activate();
        f.Named<TextBox>("SetupHomeNameBox").Text="Retained home draft";f.Named<TextBox>("SetupAwayNameBox").Text="Retained away draft";
        f.Named<CheckBox>("FreeHitsToggle").IsChecked=true;
        var player=new CricketPlayer{FirstName="Alexandria",LastName="Long squad name",IsCaptain=true,IsWicketkeeper=true};f.Players(home).Add(player);
        string before=System.Text.Json.JsonSerializer.Serialize(f.Players(home));int confirmations=0;
        f.Wizard.GetType().GetProperty("ConfirmCompletion",Private)!.SetValue(f.Wizard,(Func<bool>)(()=>{++confirmations;return true;}));
        f.Step(4);await Settle(200);bool modal=FinishSquad(f);await Settle(250);PlayerEditorFrame(f,$"squad-validation-{home}-{width}");
        Assert.False(modal);Assert.True(f.Named<Control>("Step3Panel").IsVisible);Assert.Null(f.Wizard.Result);Assert.Equal(0,f.Completions);Assert.Equal(0,confirmations);
        var message=f.Tagged<TextBlock>(home?"home-squad-validation":"away-squad-validation");Assert.True(message.IsEffectivelyVisible);Assert.Contains("second player",message.Text);Assert.Contains("11 numbered",message.Text);
        var add=f.Tagged<Button>(home?"add-home-player":"add-away-player");Assert.True(add.IsKeyboardFocusWithin,SquadValidationFocusDetails(f,add));
        var viewport=Assert.Single(add.GetVisualAncestors().OfType<Roche_Scoreboard.Views.WizardViewport>());f.AssertWithin(message,viewport);f.AssertWithin(add,viewport);SquadPointer(f,add,false);
        Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(f.Players(home)));Assert.Empty(f.Players(!home));Assert.Equal(0,f.Cancellations);
        SquadPointer(f,add);await Settle(150);f.Tagged<TextBox>("new-player-first").Text="Second";Click(f.Tagged<Button>("apply"));await Settle(250);
        Assert.False(message.IsVisible);Assert.Equal(2,f.Players(home).Count);Assert.Same(player,f.Players(home)[0]);Assert.True(player.IsCaptain);Assert.True(player.IsWicketkeeper);
        f.Step(4);await Settle(100);Assert.False(FinishSquad(f));await Settle(150);Assert.Equal(1,confirmations);Assert.Equal(1,f.Completions);
        var result=Assert.IsType<CricketSetupResult>(f.Wizard.Result);Assert.Equal(2,(home?result.TeamAPlayers:result.TeamBPlayers).Count);Assert.Equal(11,(home?result.TeamBPlayers:result.TeamAPlayers).Count);
        Assert.Equal("Retained home draft",result.TeamAName);Assert.Equal("Retained away draft",result.TeamBName);Assert.True(result.FreeHitsEnabled);
    }

    private static string SquadValidationFocusDetails(Fixture f,Button add)=>System.Text.Json.JsonSerializer.Serialize(new
    {
        active=f.Window.IsActive,focus=f.Window.FocusManager?.GetFocusedElement()?.ToString(),add.IsLoaded,add.IsEffectivelyVisible,add.IsEffectivelyEnabled,add.Focusable,
        version=f.Wizard.GetType().GetField("_squadValidationVersion",Private)?.GetValue(f.Wizard),generation=f.Wizard.GetType().GetField("_playerEntryGeneration",Private)?.GetValue(f.Wizard),
        requested=f.Wizard.GetType().GetField("_squadValidationRequested",Private)?.GetValue(f.Wizard),adding=f.Wizard.GetType().GetField("_addingPlayer",Private)?.GetValue(f.Wizard),
        ancestors=add.GetVisualAncestors().OfType<Control>().Select(x=>new{type=x.GetType().Name,x.Name,x.IsVisible,x.IsEnabled,x.IsHitTestVisible})
    });

    [AvaloniaFact]
    [Trait("Evidence","CricketSquadValidation")]
    public async Task BothIncompleteSquadsRemainSpecificAndCorrectionRevealsTheOtherSquad()
    {
        using var f=new Fixture(420,320);await Settle(300);f.Window.Activate();
        f.Players(true).Add(new CricketPlayer{FirstName="Home only"});f.Players(false).Add(new CricketPlayer{FirstName="Away only"});f.Step(4);await Settle(100);
        Assert.False(FinishSquad(f));await Settle(150);Assert.True(f.Tagged<Button>("add-home-player").IsKeyboardFocusWithin);
        Assert.True(f.Tagged<TextBlock>("home-squad-validation").IsVisible);Assert.True(f.Tagged<TextBlock>("away-squad-validation").IsVisible);
        f.Players(true).Clear();await Settle(150);Assert.False(f.Tagged<TextBlock>("home-squad-validation").IsVisible);Assert.True(f.Tagged<TextBlock>("away-squad-validation").IsVisible);
        f.Step(4);await Settle(100);Assert.False(FinishSquad(f));await Settle(150);var add=f.Tagged<Button>("add-away-player");Assert.True(add.IsKeyboardFocusWithin);SquadPointer(f,add,false);
        Assert.Empty(f.Players(true));Assert.Single(f.Players(false));Assert.Null(f.Wizard.Result);Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketSquadValidation")]
    public async Task QueuedSquadValidationCannotSurviveResetOrHide(bool hide)
    {
        using var f=new Fixture();await Settle(300);f.Window.Activate();f.Players(true).Add(new CricketPlayer{FirstName="Old"});f.Step(4);await Settle(100);
        Assert.False(FinishSquad(f));await Settle(100);f.Players(true).Clear();f.Players(false).Add(new CricketPlayer{FirstName="Next"});
        if(hide){f.Wizard.IsVisible=false;f.Wizard.IsVisible=true;f.Step(1);}else f.Wizard.Reset();await Settle(200);
        Assert.False(f.Tagged<TextBlock>("home-squad-validation").IsVisible);Assert.False(f.Tagged<TextBlock>("away-squad-validation").IsVisible);
        Assert.True(f.Named<Control>("TeamsStepContainer").IsVisible);Assert.Null(f.Wizard.Result);Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }

    [AvaloniaFact]
    [Trait("Evidence","CricketSquadValidation")]
    public async Task ResetDuringValidationRetirementCannotFinishANewDraft()
    {
        using var f=new Fixture();await Settle(300);f.Window.Activate();f.Players(true).Add(new CricketPlayer{FirstName="Old"});f.Step(4);await Settle(100);
        Assert.False(FinishSquad(f));await Settle(100);var message=f.Tagged<TextBlock>("home-squad-validation");bool reset=false;
        message.PropertyChanged+=(_,args)=>{if(reset || args.Property!=Visual.IsVisibleProperty || args.NewValue is not false)return;reset=true;f.Wizard.Reset();};
        f.Players(true).Add(new CricketPlayer{FirstName="Second"});f.Step(4);Assert.False(FinishSquad(f));await Settle(150);
        Assert.True(reset);Assert.Null(f.Wizard.Result);Assert.Equal(0,f.Completions);Assert.Empty(f.Players(true));Assert.Empty(f.Players(false));Assert.True(f.Named<Control>("TeamsStepContainer").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketSquadValidation")]
    public async Task ValidOrEmptySquadsKeepCompletionConsentAndPlaceholderSemantics(bool named)
    {
        using var f=new Fixture();await Settle(300);f.Window.Activate();
        if(named)foreach(bool home in new[]{true,false}){f.Players(home).Add(new CricketPlayer{FirstName="Captain",IsCaptain=true});f.Players(home).Add(new CricketPlayer{LastName="Keeper",IsWicketkeeper=true});}
        string before=System.Text.Json.JsonSerializer.Serialize(new[]{f.Players(true),f.Players(false)});int confirmations=0;bool accept=false;
        f.Wizard.GetType().GetProperty("ConfirmCompletion",Private)!.SetValue(f.Wizard,(Func<bool>)(()=>{++confirmations;return accept;}));f.Step(4);await Settle(100);
        Assert.False(FinishSquad(f));await Settle(100);Assert.Null(f.Wizard.Result);Assert.Equal(1,confirmations);Assert.Equal(0,f.Completions);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(new[]{f.Players(true),f.Players(false)}));
        accept=true;Assert.False(FinishSquad(f));await Settle(150);var result=Assert.IsType<CricketSetupResult>(f.Wizard.Result);Assert.Equal(2,confirmations);Assert.Equal(1,f.Completions);
        Assert.Equal(named?2:11,result.TeamAPlayers.Count);Assert.Equal(named?2:11,result.TeamBPlayers.Count);
        if(named){Assert.Same(f.Players(true)[0],result.TeamAPlayers[0]);Assert.True(result.TeamAPlayers[0].IsCaptain);Assert.True(result.TeamBPlayers[1].IsWicketkeeper);}else{Assert.Empty(f.Players(true));Assert.Empty(f.Players(false));}
        Assert.False(FinishSquad(f));Assert.Equal(1,f.Completions);Assert.Equal(2,confirmations);
    }
}
