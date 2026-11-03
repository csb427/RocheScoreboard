using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class UnifiedSetupTests
{
    private static Border SessionSheet(Fixture f) => Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
        x => Equals(x.Tag,"cricket-decision-sheet") && x.IsEffectivelyVisible);
    private static Button SessionAction(Border sheet,string key) => Assert.Single(sheet.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,key));
    private static MatchManager SeedLiveSession(Fixture f)
    {var match=Field<MatchManager>(f.Window,"_match");match.AddGoal(TeamSide.Home);Set(f.Window,"_aflMatchReady",true);return match;}
    private static void StartTraining(Fixture f) => Click(f.Home.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="TrainingLaunchAction"));

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","NativeFeedbackSession")]
    public async Task NewSessionIsOwnedInAppAndCancelPreservesSession(bool keyboard)
    {
        using var f=new Fixture();await f.Ready(700,450);var match=SeedLiveSession(f);
        string before=JsonSerializer.Serialize(match.ToState());int owned=f.Window.OwnedWindows.Count;
        StartTraining(f);await Settle(120);var sheet=SessionSheet(f);
        Assert.Equal(owned,f.Window.OwnedWindows.Count);Assert.False(f.Home.IsEnabled);
        var cancel=SessionAction(sheet,"stay");Assert.Equal("Cancel",cancel.Content);Assert.True(cancel.IsDefault);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.IsEffectivelyVisible && (x.Text??"").Contains("current session will close"));
        Assert.True(cancel.IsFocused);Capture(f.Window,"new-session-in-app");
        if(keyboard)Key(f.Window,PhysicalKey.Escape);else Click(cancel);
        await Settle(450);Assert.False(Field<bool>(f.Window,"_newSessionDecisionActive"));
        Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.Null(Field<TrainingSession?>(f.Window,"_trainingSession"));Assert.True(f.Home.IsEnabled);
    }

    [AvaloniaFact][Trait("Evidence","NativeFeedbackSession")]
    public async Task ChangedSessionIsNotReplacedAndExplainsRejectedConfirmation()
    {
        using var f=new Fixture();await f.Ready();var match=SeedLiveSession(f);StartTraining(f);await Settle(120);var sheet=SessionSheet(f);
        match.AddBehind(TeamSide.Away);string current=JsonSerializer.Serialize(match.ToState());
        Click(SessionAction(sheet,"apply"));await Settle(100);
        Assert.True(Field<bool>(f.Window,"_newSessionDecisionActive"));Assert.Null(Field<TrainingSession?>(f.Window,"_trainingSession"));
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>x.IsEffectivelyVisible && (x.Text??"").Contains("session changed"));
        Click(SessionAction(sheet,"stay"));await Settle(250);Assert.Equal(current,JsonSerializer.Serialize(match.ToState()));
    }

    [AvaloniaFact][Trait("Evidence","NativeFeedbackSession")]
    public async Task ExplicitAcceptanceStartsOneTrainingSessionAndRetiresDecision()
    {
        using var f=new Fixture();await f.Ready();var match=SeedLiveSession(f);StartTraining(f);await Settle(120);var apply=SessionAction(SessionSheet(f),"apply");
        Click(apply);Click(apply);await Settle(550);
        Assert.NotNull(Field<TrainingSession?>(f.Window,"_trainingSession"));Assert.True(f.Window.FindControl<Control>("TrainingControlPanelView")!.IsVisible);
        Assert.False(Field<bool>(f.Window,"_newSessionDecisionActive"));Assert.Equal(6,match.HomeTotal);
    }
}
