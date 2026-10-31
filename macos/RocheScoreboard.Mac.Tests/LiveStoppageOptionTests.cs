// Initial regression draft ONLY. Uncompiled/unrun. Root integrates after verified MR5.
// Extends the existing real-route fixture. No private handler invocation,
// synthetic Checked, forced visibility, or application writes are used.
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static T Mr6Read<T>(AflFixture f,string name) => (T)f.Window.GetType().GetField(name,HiddenInstance)!.GetValue(f.Window)!;
    private static T Mr6Part<T>(AflFixture f,string name) => (T)f.Window.GetType().GetProperty(name,HiddenInstance)!.GetValue(f.Window)!;
    private static void Mr6Mode(AflFixture f,bool up) =>
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<RadioButton>(f.Window,up?"ClockModeCountUp":"ClockModeCountdown"))).Select();
    private static Rect Mr6Box(Window window,Control target)
    {
        var points=new[]{new Point(),new Point(target.Bounds.Width,0),new Point(0,target.Bounds.Height),new Point(target.Bounds.Width,target.Bounds.Height)}.Select(p=>target.TranslatePoint(p,window)).ToArray();
        Assert.All(points,p=>Assert.NotNull(p));double x=points.Min(p=>p!.Value.X),y=points.Min(p=>p!.Value.Y);
        return new Rect(x,y,points.Max(p=>p!.Value.X)-x,points.Max(p=>p!.Value.Y)-y);
    }
    private static Point[] Mr6Probes(Rect r)=>new[]{r.Center,new Point(r.Left+4,r.Center.Y),new Point(r.Right-4,r.Center.Y),new Point(r.Center.X,r.Top+4),new Point(r.Center.X,r.Bottom-4)};
    private static async Task Mr6Reveal(AflFixture f,Control target)
    {
        Assert.True(target.IsEffectivelyVisible);Assert.True(target.IsEffectivelyEnabled);Assert.True(target.Focusable);
        // Fixture reveal only; a separate sequential traversal assertion below
        // establishes actual Tab reachability.
        Assert.True(target.Focus(NavigationMethod.Tab));target.BringIntoView();await Settle(100);
        var viewport=Named<ScrollViewer>(f.Window,"AflSettingsPageViewport");
        bool Ready(){using var frame=f.Window.CaptureRenderedFrame();var r=Mr6Box(f.Window,target);return r.Width>16&&r.Height>16&&new Rect(f.Window.ClientSize).Contains(r)&&Mr6Probes(r).All(p=>Within(target,f.Window.InputHitTest(p)));}
        for(int i=0;i<40&&!Ready();i++)
        {
            var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),f.Window);Assert.NotNull(wheel);Assert.True(Within(viewport,f.Window.InputHitTest(wheel.Value)));
            var r=Mr6Box(f.Window,target);bool top=Within(target,f.Window.InputHitTest(Mr6Probes(r)[3])),bottom=Within(target,f.Window.InputHitTest(Mr6Probes(r)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:r.Center.Y<wheel.Value.Y?1:-1;
            f.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Settle(80);
        }
        Assert.True(Ready(),"Stoppage option must own all five real input probes after bounded physical scrolling.");
    }
    private static void Mr6Canonical(AflFixture f)
    {
        var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");Assert.Equal(1,card.Opacity,3);
        // These are current named transforms, not newly assumed part names.
        Assert.Equal(0,Mr6Part<TranslateTransform>(f,"SettingsControlPanelCountdownCardTranslate").Y,3);
        var scale=Mr6Part<ScaleTransform>(f,"SettingsControlPanelCountdownCardScale");Assert.Equal(1,scale.ScaleX,3);Assert.Equal(1,scale.ScaleY,3);
        Assert.Null(f.Window.GetType().GetField("_settingsCountdownOptionHideTimer",HiddenInstance)!.GetValue(f.Window));
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)][InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR6OriginalStoppageToggle")]
    public async Task StoppageOptionExposesOriginalToggleWithOneInputOwner(int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);await OpenClockSection(f,reduced);await Settle(400);
        var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");
        await Mr6Reveal(f,toggle.IsEffectivelyVisible?toggle:card);Capture(f.Window,$"mr6-before-{width}-{reduced}");
        Assert.True(toggle.IsEffectivelyVisible,"Original checked-state carrier must become the actual visible semantic toggle.");
        Assert.Same(toggle,Assert.Single(card.GetVisualDescendants().OfType<CheckBox>(),x=>x.IsEffectivelyVisible));
        Assert.Equal(typeof(Border),card.GetType());Assert.False(card.Focusable);Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        Assert.Equal(toggle.IsChecked==true?ToggleState.On:ToggleState.Off,provider.ToggleState);
        Assert.Contains("stoppage",(AutomationProperties.GetName(toggle)??"").ToLowerInvariant());
        Assert.Equal(toggle.IsChecked==true,Mr6Read<bool>(f,"_showControlPanelCountdown"));
        await OpenClockSection(f,reduced);bool found=false;
        for(int i=0;i<100;i++){Key(f.Window,PhysicalKey.Tab);await Settle(15);Assert.NotSame(card,f.Window.FocusManager?.GetFocusedElement());if(Within(toggle,f.Window.FocusManager?.GetFocusedElement())){found=true;break;}}
        Assert.True(found,"Sequential Tab from real search focus must reach original stoppage toggle.");
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)][InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR6OriginalStoppageToggle")]
    public async Task StoppageSpaceProviderAndPointerCommitOnceAndPreserveStoppedMatch(int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);await OpenClockSection(f,reduced);
        var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");await Mr6Reveal(f,toggle);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        string before=Scores(f.Match);bool initial=toggle.IsChecked==true;int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
        void AssertState(bool value,int count){Assert.Equal(count,changes);Assert.Equal(value,toggle.IsChecked==true);Assert.Equal(value,Mr6Read<bool>(f,"_showControlPanelCountdown"));Assert.Equal(value,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);Assert.Equal(value?ToggleState.On:ToggleState.Off,provider.ToggleState);Assert.Equal(before,Scores(f.Match));Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));}
        Key(f.Window,PhysicalKey.Space);AssertState(!initial,1);await Settle(30);
        provider.Toggle();AssertState(initial,2);await Settle(30);
        await Mr6Reveal(f,toggle);var r=Mr6Box(f.Window,toggle);f.Window.MouseMove(r.Center);f.Window.MouseDown(r.Center,MouseButton.Left);f.Window.MouseUp(r.Center,MouseButton.Left);AssertState(!initial,3);
        await Settle(600);AssertState(!initial,3);Capture(f.Window,$"mr6-after-{width}-{reduced}");
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","MR6RevealLifetime")]
    public async Task ReducedStoppageRevealIsImmediatelyCanonical(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);await OpenClockSection(f,true);Mr6Mode(f,false);await Settle(300);
        bool pref=OperatorWorkflowPreferences.Load().AflControlPanelCountdown;Mr6Mode(f,true);
        using var frame=f.Window.CaptureRenderedFrame();Mr6Canonical(f);
        Assert.Equal(pref,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Assert.Equal(pref,Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown").IsChecked==true);
        await Settle(600);Mr6Canonical(f);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","MR6RevealLifetime")]
    public async Task UnavailableStoppageOptionRetiresHitAndFocusAtStartOfExit(bool reduced)
    {
        await using var f=await AflFixture.Create(1280,850);await OpenClockSection(f,reduced);await Settle(400);
        var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");Control current=toggle.IsEffectivelyVisible?toggle:card;
        await Mr6Reveal(f,current);var before=Mr6Box(f.Window,current);bool preference=toggle.IsChecked==true;string score=Scores(f.Match);
        // Accessibility selection changes the actual native radio without
        // moving focus away in the fixture. Product must retire obsolete focus.
        Mr6Mode(f,false);using var frame=f.Window.CaptureRenderedFrame();
        Assert.False(Within(card,f.Window.FocusManager?.GetFocusedElement()));
        Assert.All(Mr6Probes(before),p=>Assert.False(Within(card,f.Window.InputHitTest(p))));
        Assert.Equal(preference,toggle.IsChecked==true);Assert.Equal(preference,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);Assert.False(Mr6Read<bool>(f,"_showControlPanelCountdown"));Assert.Equal(score,Scores(f.Match));
        await Settle(600);Assert.False(card.IsEffectivelyVisible);
    }

    [AvaloniaTheory][InlineData("tab")][InlineData("search")][InlineData("window")]
    [Trait("Evidence","MR6RevealLifetime")]
    public async Task RealHostHideRetiresActiveStoppageRevealWithoutChangingPreference(string route)
    {
        await using var f=await AflFixture.Create(1280,850);await OpenClockSection(f,false);Mr6Mode(f,false);await Settle(300);Mr6Mode(f,true);await Settle(35);
        var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");using(var frame=f.Window.CaptureRenderedFrame()){}
        Assert.True(card.Opacity>0&&card.Opacity<1,"Require actual active reveal before hiding host.");bool preference=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown").IsChecked==true;
        if(route=="tab")
        {
            Key(f.Window,PhysicalKey.Digit1,RawInputModifiers.Control);
            Assert.True(Named<TabItem>(f.Window,"MatchControlTab").IsSelected);
            // Native tab content detaches on its next layout pass. Assert the
            // real selected tab synchronously, then observe actual host hide
            // before testing that hide's cleanup; do not force visibility.
            await Until(()=>!card.IsLoaded && TopLevel.GetTopLevel(card) is null,
                "Selected Match tab must actually detach the prior settings content.");
            Assert.Null(card.GetVisualRoot());
        }
        else if(route=="search"){Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);f.Window.KeyTextInput("no-stoppage-section-5839");}
        else f.Window.Hide();
        if(route=="tab") { Assert.False(card.IsLoaded);Assert.Null(TopLevel.GetTopLevel(card)); }
        else Assert.False(card.IsEffectivelyVisible);
        // Proposed ownership contract: settle current count-up visibility pose
        // synchronously when an ancestor hides, with no pending collapse work.
        Mr6Canonical(f);await Settle(500);Mr6Canonical(f);
        if(route=="tab"){Key(f.Window,PhysicalKey.Digit4,RawInputModifiers.Control);await Settle(480);await OpenClockSection(f,false);}
        else if(route=="search"){Key(f.Window,PhysicalKey.Escape);await Settle(100);}
        else {f.Window.Show();await Settle(100);}
        Assert.True(card.IsEffectivelyVisible);Mr6Canonical(f);await Settle(500);Mr6Canonical(f);
        Assert.Equal(preference,Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown").IsChecked==true);Assert.Equal(preference,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","MR6RevealLifetime")]
    public async Task ActualRepeatedModeChangesAndLiveReduceKeepOnlyCurrentStoppageReveal(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);var previousStyle=OperatorAppearance.MotionStyle;
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);await OpenClockSection(f,false);await Settle(400);
            var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");bool preference=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown").IsChecked==true;
            // Real native selection sequence. No stale callback is injected.
            for(int i=0;i<3;i++){Mr6Mode(f,false);await Settle(25);Mr6Mode(f,true);await Settle(25);}
            using(var frame=f.Window.CaptureRenderedFrame()){}Assert.True(card.IsEffectivelyVisible);Assert.True(card.Opacity>0&&card.Opacity<1,"Witness actual in-flight reveal before live Reduce assertion.");
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Mr6Canonical(f);await Settle(800);Mr6Canonical(f);Assert.True(card.IsEffectivelyVisible);
            Assert.Equal(preference,Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown").IsChecked==true);Assert.Equal(preference,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);Assert.Equal(preference,Mr6Read<bool>(f,"_showControlPanelCountdown"));
        }
        finally{OperatorAppearance.ConfigureMotionStyle(previousStyle);}
    }
}
