// Original production Hide Team Names owner, pointer/provider/Space regression.
// Eight baseline cases using the existing real-route fixture.
// Explicit Focus(Tab) is fixture reveal, not proof of sequential Tab order.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    private static bool HideTeamNamesValue(AflFixture fixture) =>
        (bool)fixture.Window.GetType().GetField("_hideTeamNames",HiddenInstance)!.GetValue(fixture.Window)!;

    private static bool HideTeamNamesPreference(AflFixture fixture)
    {
        object preferences=fixture.Window.GetType().GetField("_uxPreferences",HiddenInstance)!.GetValue(fixture.Window)!;
        return (bool)preferences.GetType().GetProperty("HideTeamNames")!.GetValue(preferences)!;
    }

    // A stable input region is independent from animated content inside it.
    private static Rect HideTeamNamesPainted(Window window,Control target)
    {
        var corners=new[]{new Point(),new Point(target.Bounds.Width,0),new Point(0,target.Bounds.Height),new Point(target.Bounds.Width,target.Bounds.Height)}
            .Select(point=>target.TranslatePoint(point,window)).ToArray();
        Assert.All(corners,point=>Assert.NotNull(point));
        double x=corners.Min(point=>point!.Value.X),y=corners.Min(point=>point!.Value.Y);
        return new Rect(x,y,corners.Max(point=>point!.Value.X)-x,corners.Max(point=>point!.Value.Y)-y);
    }
    private static Point[] HideTeamNamesProbes(Rect box)=>new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)};
    private static async Task RevealHideTeamNames(AflFixture fixture,CheckBox toggle)
    {
        Assert.True(toggle.IsEffectivelyVisible,"The ORIGINAL HideTeamNames checkbox must be visible; a hidden backing value behind an Invoke tile is not a semantic toggle.");
        Assert.True(toggle.IsEffectivelyEnabled);Assert.True(toggle.Focusable);
        Key(fixture.Window,PhysicalKey.Tab);
        Assert.True(toggle.Focus(NavigationMethod.Tab));toggle.BringIntoView();
        await Settle();
        var outer=Named<ScrollViewer>(fixture.Window,"AflSettingsPageViewport");
        bool Ready()
        {
            using var frame=fixture.Window.CaptureRenderedFrame();
            var box=HideTeamNamesPainted(fixture.Window,toggle);
            return box.Width>16&&box.Height>16&&new Rect(fixture.Window.ClientSize).Contains(box)
                &&HideTeamNamesProbes(box).All(point=>Within(toggle,fixture.Window.InputHitTest(point)));
        }
        for(int step=0;step<30&&!Ready();step++)
        {
            var wheel=outer.TranslatePoint(new Point(3,outer.Bounds.Height/2),fixture.Window);
            Assert.NotNull(wheel);Assert.True(Within(outer,fixture.Window.InputHitTest(wheel.Value)),"Physical scroll must reach the actual settings viewport.");
            var box=HideTeamNamesPainted(fixture.Window,toggle);
            bool top=Within(toggle,fixture.Window.InputHitTest(HideTeamNamesProbes(box)[3]));
            bool bottom=Within(toggle,fixture.Window.InputHitTest(HideTeamNamesProbes(box)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<wheel.Value.Y?1:-1;
            fixture.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Settle(80);
        }
        Assert.True(Ready(),"Original HideTeamNames toggle must fit and own all five input probes after bounded physical scrolling.");
        Assert.True(Within(toggle,fixture.Window.FocusManager?.GetFocusedElement()));
    }


    private static async Task RevealHideTeamNamesCard(AflFixture fixture,Border card)
    {
        Assert.True(card.IsEffectivelyVisible);Assert.True(card.IsEffectivelyEnabled);Assert.True(card.Focusable);
        Key(fixture.Window,PhysicalKey.Tab);Assert.True(card.Focus(NavigationMethod.Tab));card.BringIntoView();await Settle();
        var viewport=Named<ScrollViewer>(fixture.Window,"AflSettingsPageViewport");
        bool Ready()
        {
            using var frame=fixture.Window.CaptureRenderedFrame();var box=HideTeamNamesPainted(fixture.Window,card);
            return box.Width>16&&box.Height>16&&new Rect(fixture.Window.ClientSize).Contains(box)
                &&HideTeamNamesProbes(box).All(point=>Within(card,fixture.Window.InputHitTest(point)));
        }
        for(int step=0;step<40&&!Ready();step++)
        {
            var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),fixture.Window);Assert.NotNull(wheel);
            Assert.True(Within(viewport,fixture.Window.InputHitTest(wheel.Value)),"Physical wheel must reach the actual old card viewport.");
            var box=HideTeamNamesPainted(fixture.Window,card);bool top=Within(card,fixture.Window.InputHitTest(HideTeamNamesProbes(box)[3]));bool bottom=Within(card,fixture.Window.InputHitTest(HideTeamNamesProbes(box)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<wheel.Value.Y?1:-1;
            fixture.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Settle(80);
        }
        Assert.True(Ready(),"Actual old visible HideTeamNames card must own5painted probes before a meaningful before capture.");
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR7OriginalHideTeamNamesToggle")]
    public async Task HideTeamNamesCardExposesItsOriginalVisibleToggleStateWithoutAnotherInvokeOwner(int width,int height,bool reduced)
    {
        await using var fixture=await AflFixture.Create(width,height);await OpenClockSection(fixture,reduced);
        var original=Named<CheckBox>(fixture.Window,"SettingsHideTeamNamesToggle");var card=Named<Border>(fixture.Window,"SettingsHideTeamNamesCard");
        string before=Scores(fixture.Match);bool value=HideTeamNamesValue(fixture);
        if(original.IsEffectivelyVisible)await RevealHideTeamNames(fixture,original);
        else await RevealHideTeamNamesCard(fixture,card);
        Capture(fixture.Window,$"mr5-hide-team-names-before-{width}-{reduced}");
        Assert.True(original.IsEffectivelyVisible,"Baseline: original checked-state owner is currently collapsed.");
        Assert.Same(original,Assert.Single(card.GetVisualDescendants().OfType<CheckBox>(),item=>item.IsEffectivelyVisible));
        Assert.Equal(typeof(Border),card.GetType());Assert.False(card.Focusable);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
        Assert.Equal(value?ToggleState.On:ToggleState.Off,provider.ToggleState);
        Assert.Contains("hide team names",(AutomationProperties.GetName(original)??"").ToLowerInvariant());
        Assert.Equal(value,original.IsChecked==true);Assert.Equal(value,HideTeamNamesPreference(fixture));Assert.Equal(before,Scores(fixture.Match));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR7OriginalHideTeamNamesToggle")]
    public async Task HideTeamNamesSpaceProviderAndPhysicalPointerShareOneStateWithoutEditingScoresOrClock(int width,int height,bool reduced)
    {
        await using var fixture=await AflFixture.Create(width,height);await OpenClockSection(fixture,reduced);
        var original=Named<CheckBox>(fixture.Window,"SettingsHideTeamNamesToggle");await RevealHideTeamNames(fixture,original);
        string scores=Scores(fixture.Match);bool initial=HideTeamNamesValue(fixture);int changes=0;
        original.IsCheckedChanged+=(_,_)=>changes++;
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
        Key(fixture.Window,PhysicalKey.Space);await Settle();
        Assert.Equal(1,changes);Assert.Equal(!initial,HideTeamNamesValue(fixture));Assert.Equal(!initial,original.IsChecked==true);Assert.Equal(!initial,HideTeamNamesPreference(fixture));
        Assert.Equal(!initial?ToggleState.On:ToggleState.Off,provider.ToggleState);Assert.Equal(scores,Scores(fixture.Match));
        provider.Toggle();await Settle();
        Assert.Equal(2,changes);Assert.Equal(initial,HideTeamNamesValue(fixture));Assert.Equal(initial,original.IsChecked==true);Assert.Equal(initial,HideTeamNamesPreference(fixture));
        Assert.Equal(initial?ToggleState.On:ToggleState.Off,provider.ToggleState);Assert.Equal(scores,Scores(fixture.Match));
        await RevealHideTeamNames(fixture,original);Rect box=HideTeamNamesPainted(fixture.Window,original);
        Assert.All(HideTeamNamesProbes(box),point=>Assert.True(Within(original,fixture.Window.InputHitTest(point))));
        fixture.Window.MouseMove(box.Center);fixture.Window.MouseDown(box.Center,MouseButton.Left);fixture.Window.MouseUp(box.Center,MouseButton.Left);await Settle();
        Assert.Equal(3,changes);Assert.Equal(!initial,HideTeamNamesValue(fixture));Assert.Equal(!initial,original.IsChecked==true);Assert.Equal(!initial,HideTeamNamesPreference(fixture));
        Assert.Equal(!initial?ToggleState.On:ToggleState.Off,provider.ToggleState);Assert.Equal(scores,Scores(fixture.Match));
        Capture(fixture.Window,$"mr5-hide-team-names-after-{width}-{reduced}");
    }
}
