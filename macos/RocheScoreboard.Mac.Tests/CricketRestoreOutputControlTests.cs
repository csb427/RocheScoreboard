using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class PresentationLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalCricketRestoreWindowActionHasANameAndReopensOnlyOnExplicitInput(int width,int height)
    {
        using var f=new Fixture();await f.Ready("cricket");f.Window.Width=width;f.Window.Height=height;await Settle(180);
        var panel=f.Window.FindControl<CricketControlPanel>("CricketControlPanelView")!;
        var match=Field<CricketMatchManager>(f.Window,"_cricketMatch");
        // Public model operations establish ready data; this test does not
        // claim the Home/setup/toss wizard route was exercised.
        if(match.TossPending)Assert.True(match.CompleteToss(true,true));
        if(match.CurrentInnings?.Striker is null)match.SetStriker(0);
        if(match.CurrentInnings?.NonStriker is null)match.SetNonStriker(1);
        if(match.CurrentInnings?.NeedsBowlerSelection==true)match.ConfirmBowler(match.SuggestedNextBowler());
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(panel.FindControl<TabItem>("OutputTab")!)).Select();
        await Settle(150);
        var worm=panel.FindControl<Button>("Presentationworm")!;Assert.True(worm.IsEnabled);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(worm)).Invoke();await Settle(650);
        Assert.Equal("worm",Field<string>(panel,"_currentPresentation"));
        var button=panel.FindControl<Button>("Presentationwindow")!;
        Assert.True(button.Focus(NavigationMethod.Tab));button.BringIntoView();await Settle(150);
        var point=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),f.Window);Assert.NotNull(point);
        var box=new Rect(button.TranslatePoint(default,f.Window)!.Value,button.Bounds.Size);
        Assert.True(new Rect(f.Window.ClientSize).Contains(box));
        var hit=f.Window.InputHitTest(point.Value);Assert.True(ReferenceEquals(hit,button)||hit is Visual v&&v.GetVisualAncestors().Contains(button));
        Capture(f.Window,$"cricket-restore-action-{width}");
        Assert.Equal("Show scoreboard output",ControlAutomationPeer.CreatePeerForElement(button)?.GetName());
        string state=System.Text.Json.JsonSerializer.Serialize(match.ToState());
        var old=f.Output("cricket");old.Close();await Settle(100);Assert.False(old.IsVisible);
        Assert.True(Field<bool>(f.Window,"_outputActivationRequired"));
        f.Window.MouseMove(point.Value);f.Window.MouseDown(point.Value,MouseButton.Left);f.Window.MouseUp(point.Value,MouseButton.Left);await Settle(650);
        Assert.True(f.Output("cricket").IsVisible);Assert.False(Field<bool>(f.Window,"_outputActivationRequired"));
        Assert.Equal("worm",Field<string>(panel,"_currentPresentation"));
        Assert.Equal(state,System.Text.Json.JsonSerializer.Serialize(match.ToState()));
        var summary=Field<CricketSummaryControl>(f.Window,"_cricketSummary");Assert.True(summary.IsVisible);
        Assert.Equal("SCOREWORM",summary.FindControl<TextBlock>("HeaderText")!.Text);
        var host=Field<CricketOverlayHost>(panel,"_overlayHost");Assert.False(host.AllowsOverlays);
        Assert.False(host.Request(CricketOverlayKind.Extras));
        f.Output("cricket").Close();await Settle(100);Assert.True(button.Focus(NavigationMethod.Tab));
        f.Window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);await Settle(650);
        Assert.True(f.Output("cricket").IsVisible);Assert.Equal("worm",Field<string>(panel,"_currentPresentation"));
        Assert.True(summary.IsVisible);Assert.Equal("SCOREWORM",summary.FindControl<TextBlock>("HeaderText")!.Text);
        Assert.Equal(state,System.Text.Json.JsonSerializer.Serialize(match.ToState()));
    }
}
