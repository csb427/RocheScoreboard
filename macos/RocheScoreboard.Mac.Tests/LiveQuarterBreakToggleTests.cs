// Actual quarter-break original-owner/commit regressions. Reuses the real Home/setup/settings fixture.
// Linux headless evidence; native WPF desktop acceptance remains separate.
using System;
using System.Linq;
using System.Text.Json;
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
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    private static bool BreakCardValue(AflFixture f)=>(bool)f.Window.GetType()
        .GetField("_breakScreenEnabled",HiddenInstance)!.GetValue(f.Window)!;

    private static string BreakCardOtherPreferences()
    {
        // Detached loaded data only: normalize the one intentionally changed
        // preference without saving it or editing the production model.
        var data=OperatorWorkflowPreferences.Load();
        data.AflBreakScreenEnabled=false;
        return JsonSerializer.Serialize(data);
    }

    private static object?[] BreakCardPresentation(AflFixture f)=>new[]
    {
        "_breakScreenDelayTimer","_rotationTimer","_isBreakRotating","_showingBreakScreen",
        "_activeScreenKey","_videoTakeoverActive","_scoreboardSuspendedForPlayback"
    }.Select(name=>f.Window.GetType().GetField(name,HiddenInstance)!.GetValue(f.Window)).ToArray();

    private static async Task OpenBreakCardSection(AflFixture f,bool reduced)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduced);
        OperatorMotion.AnimationOverride=null;
        Assert.Equal(!reduced,OperatorMotion.Enabled);
        var workspace=Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(),x=>x.IsEffectivelyVisible);
        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        f.Window.KeyTextInput("Auto-show summary screen at quarter breaks");
        await Settle(150);
        Assert.True(Named<Border>(f.Window,"SettingsSection_Overlays").IsEffectivelyVisible);
        Assert.False(Named<Border>(f.Window,"SettingsSection_Match").IsVisible);
    }

    private static async Task RevealBreakCardActor(AflFixture f,Control actor)
    {
        Assert.True(actor.IsEffectivelyVisible);Assert.True(actor.IsEffectivelyEnabled);
        // Explicit focus is a fixture reveal and a starting point for Space;
        // this does not claim sequential Tab traversal has been accepted.
        Key(f.Window,PhysicalKey.Tab);Assert.True(actor.Focus(NavigationMethod.Tab));
        actor.BringIntoView();await Settle();
        var viewport=Named<ScrollViewer>(f.Window,"AflSettingsPageViewport");
        bool Ready()
        {
            using var rendered=f.Window.CaptureRenderedFrame();
            Rect box=FinalsPainted(f.Window,actor);
            return box.Width>16&&box.Height>16&&new Rect(f.Window.ClientSize).Contains(box)
                &&FinalsProbes(box).All(point=>Within(actor,f.Window.InputHitTest(point)));
        }
        for(int step=0;step<40&&!Ready();step++)
        {
            var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),f.Window);
            Assert.NotNull(wheel);Assert.True(Within(viewport,f.Window.InputHitTest(wheel.Value)));
            Rect box=FinalsPainted(f.Window,actor);
            bool top=Within(actor,f.Window.InputHitTest(FinalsProbes(box)[3]));
            bool bottom=Within(actor,f.Window.InputHitTest(FinalsProbes(box)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<wheel.Value.Y?1:-1;
            f.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);
            await Settle(80);
        }
        Assert.True(Ready(),"The actual quarter-break preference actor must fit and own all five painted input probes after bounded physical scrolling.");
        Assert.True(Within(actor,f.Window.FocusManager?.GetFocusedElement()));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","QuarterBreakOriginalToggleBaseline")]
    public async Task QuarterBreakCardExposesItsOriginalNativeCheckedInput(int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        await OpenBreakCardSection(f,reduced);
        var toggle=Named<CheckBox>(f.Window,"SettingsBreakScreenToggle");
        var card=Named<Border>(f.Window,"SettingsBreakScreenCard");
        string match=JsonSerializer.Serialize(f.Match.ToState());
        bool value=BreakCardValue(f);
        await RevealBreakCardActor(f,toggle.IsEffectivelyVisible?toggle:card);
        Capture(f.Window,$"quarter-break-original-baseline-{width}-{reduced}");
        // Intended acceptance assertion. Source predicts the original is
        // collapsed; root must obtain actual runtime evidence before claiming it.
        Assert.True(toggle.IsEffectivelyVisible,"Quarter-break preference must expose its ORIGINAL checkbox as the visible checked-state input.");
        Assert.Same(toggle,Assert.Single(card.GetVisualDescendants().OfType<CheckBox>(),x=>x.IsEffectivelyVisible));
        Assert.Equal(typeof(Border),card.GetType());Assert.False(card.Focusable);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        Assert.Equal(value?ToggleState.On:ToggleState.Off,provider.ToggleState);
        string name=(AutomationProperties.GetName(toggle)??"").ToLowerInvariant();
        Assert.Contains("quarter",name);Assert.Contains("break",name);
        Assert.Equal(value,toggle.IsChecked==true);
        Assert.Equal(value,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
        Assert.Equal(match,JsonSerializer.Serialize(f.Match.ToState()));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","QuarterBreakExistingCommitBaseline")]
    public async Task QuarterBreakVisibleActivationCommitsOnlyItsExistingPreference(int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        await OpenBreakCardSection(f,reduced);
        var toggle=Named<CheckBox>(f.Window,"SettingsBreakScreenToggle");
        var card=Named<Border>(f.Window,"SettingsBreakScreenCard");
        Control actor=toggle.IsEffectivelyVisible?toggle:card;
        await RevealBreakCardActor(f,actor);
        bool initial=BreakCardValue(f);int changes=0;
        string match=JsonSerializer.Serialize(f.Match.ToState());
        string unrelated=BreakCardOtherPreferences();
        object?[] presentation=BreakCardPresentation(f);
        toggle.IsCheckedChanged+=(_,_)=>changes++;
        void Authority(bool expected,int count)
        {
            Assert.Same(toggle,Named<CheckBox>(f.Window,"SettingsBreakScreenToggle"));
            Assert.Equal(expected,toggle.IsChecked==true);Assert.Equal(expected,BreakCardValue(f));
            Assert.Equal(expected,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
            Assert.Equal(count,changes);Assert.Equal(unrelated,BreakCardOtherPreferences());
            Assert.Equal(match,JsonSerializer.Serialize(f.Match.ToState()));
            Assert.Equal(presentation,BreakCardPresentation(f));
        }
        Authority(initial,0);
        Rect box=FinalsPainted(f.Window,actor);
        f.Window.MouseMove(box.Center);f.Window.MouseDown(box.Center,MouseButton.Left);f.Window.MouseUp(box.Center,MouseButton.Left);
        Authority(!initial,1); // Existing click handler commits synchronously.
        await Settle();Authority(!initial,1);
        Assert.True(Within(actor,f.Window.FocusManager?.GetFocusedElement()));
        Key(f.Window,PhysicalKey.Space);Authority(initial,2);
        await Settle();Authority(initial,2);
        if(ReferenceEquals(actor,toggle))
        {
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            provider.Toggle();Authority(!initial,3);
        }
        else
        {
            // Existing action-border accessibility route is queued. Characterize
            // it honestly rather than representing Invoke as a Toggle provider.
            var provider=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(card));
            provider.Invoke();await Settle();Authority(!initial,3);
        }
        await Settle(700);Authority(!initial,3);
        Capture(f.Window,$"quarter-break-commit-baseline-{width}-{reduced}");
    }
}
