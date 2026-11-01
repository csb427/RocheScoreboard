// Original AFL automatic-overlay preference route; Linux headless evidence only.
using System.Text.Json;
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
    private static async Task OpenAutomaticOverlayPreference(AflFixture f, bool reduced)
    {
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, reduced);
        OperatorMotion.AnimationOverride = null;
        Key(f.Window, PhysicalKey.F, RawInputModifiers.Control);
        var workspace = Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(), x => x.IsEffectivelyVisible);
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(), x => x.IsEffectivelyVisible);
        Assert.True(search.IsFocused); f.Window.KeyTextInput("Automatic overlays"); await Settle(150);
    }
    private static string AutomaticOverlayOtherPreferences()
    {
        var data = OperatorWorkflowPreferences.Load(); data.AflAutoOverlaysEnabled = false;
        return JsonSerializer.Serialize(data);
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task AutomaticOverlaysExposesOriginalNativeToggleAndPersistentState(int width,int height,bool reduced)
    {
        await using var f = await AflFixture.Create(width,height);
        await OpenAutomaticOverlayPreference(f,reduced);
        var original = Named<CheckBox>(f.Window,"AutoOverlaysToggle");
        var card = Named<Border>(f.Window,"AutoOverlaysCard");
        await RevealBreakCardActor(f,original.IsEffectivelyVisible ? original : card);
        Capture(f.Window,$"automatic-overlays-state-{width}-{reduced}");
        Assert.True(original.IsEffectivelyVisible,"The existing AFL preference checkbox must be the visible checked-state input.");
        Assert.Equal(typeof(Border),card.GetType()); Assert.False(card.Focusable);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
        var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
        Assert.Equal(original.IsChecked == true ? ToggleState.On : ToggleState.Off,provider.ToggleState);
        Assert.Contains("automatic",(AutomationProperties.GetName(original) ?? "").ToLowerInvariant());
        var state = Assert.Single(original.GetVisualDescendants().OfType<TextBlock>(),x => x.Name == "AutomaticOverlaysStateText");
        Assert.Equal(original.IsChecked == true ? "ON" : "OFF",state.Text);
        // Search focus -> ordinary Tab must reach the sole native input.
        await OpenAutomaticOverlayPreference(f,reduced); bool entered = false;
        for(int i=0;i<100;i++)
        {
            Key(f.Window,PhysicalKey.Tab); await Settle(15);
            Assert.NotSame(card,f.Window.FocusManager?.GetFocusedElement());
            if(Within(original,f.Window.FocusManager?.GetFocusedElement())) { entered = true; break; }
        }
        Assert.True(entered,"Sequential Tab must reach the visible preference without a duplicate action card.");
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task AutomaticOverlaysReversalAndReducedMotionKeepCurrentPreferenceAuthoritative(int width,int height)
    {
        await using var f = await AflFixture.Create(width,height); await OpenAutomaticOverlayPreference(f,false);
        var original = Named<CheckBox>(f.Window,"AutoOverlaysToggle"); await RevealBreakCardActor(f,original);
        var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
        bool initial = original.IsChecked == true; string model = ManualWeatherModel(f);
        Assert.True(OperatorMotion.Enabled);
        provider.Toggle(); await Settle(25);
        using(var frame = f.Window.CaptureRenderedFrame()) { Assert.NotNull(frame); }
        double firstProgress = OperatorChoiceMotion.GetProgress(original);
        Assert.InRange(firstProgress,0.0001,0.9999);
        Capture(f.Window,$"automatic-overlays-mid-selection-{width}");
        provider.Toggle(); await Settle(25); provider.Toggle();
        Assert.Equal(!initial,original.IsChecked == true);
        Assert.Equal(!initial,OperatorWorkflowPreferences.Load().AflAutoOverlaysEnabled);
        // Changing the motion preference mid-flight must settle, not replay a
        // captured selection or delay accepting the newest checkbox state.
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true); await Settle(30);
        Assert.Equal(initial ? 0d : 1d,OperatorChoiceMotion.GetProgress(original),3);
        provider.Toggle(); Assert.Equal(initial,original.IsChecked == true);
        Assert.Equal(initial ? 1d : 0d,OperatorChoiceMotion.GetProgress(original),3);
        await Settle(650); Assert.Equal(initial,Mr6Read<bool>(f,"_autoOverlaysEnabled"));
        Assert.Equal(initial,OperatorWorkflowPreferences.Load().AflAutoOverlaysEnabled);
        Assert.Equal(model,ManualWeatherModel(f));
        Capture(f.Window,$"automatic-overlays-reduced-settled-{width}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task AutomaticOverlaysPointerSpaceAndProviderCommitOnceWithoutScoringOrOutput(int width,int height,bool reduced)
    {
        await using var f = await AflFixture.Create(width,height); await OpenAutomaticOverlayPreference(f,reduced);
        var original = Named<CheckBox>(f.Window,"AutoOverlaysToggle"); await RevealBreakCardActor(f,original);
        var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
        bool initial = original.IsChecked == true; int changes = 0; original.IsCheckedChanged += (_,_) => changes++;
        string model = ManualWeatherModel(f), other = AutomaticOverlayOtherPreferences();
        object?[] presentation = BreakCardPresentation(f);
        var state = Assert.Single(original.GetVisualDescendants().OfType<TextBlock>(),x => x.Name == "AutomaticOverlaysStateText");
        void Authority(bool expected,int count)
        {
            Assert.Equal(expected, original.IsChecked == true); Assert.Equal(expected,Mr6Read<bool>(f,"_autoOverlaysEnabled"));
            Assert.Equal(expected,OperatorWorkflowPreferences.Load().AflAutoOverlaysEnabled);
            Assert.Equal(expected ? ToggleState.On : ToggleState.Off,provider.ToggleState);
            Assert.Equal(expected ? "ON" : "OFF",state.Text); Assert.Equal(count,changes);
            Assert.Equal(model,ManualWeatherModel(f)); Assert.Equal(other,AutomaticOverlayOtherPreferences());
            Assert.Equal(presentation,BreakCardPresentation(f));
        }
        Authority(initial,0); Key(f.Window,PhysicalKey.Space); Authority(!initial,1);
        provider.Toggle(); Authority(initial,2);
        await RevealBreakCardActor(f,original); var box = FinalsPainted(f.Window,original);
        f.Window.MouseMove(box.Center); f.Window.MouseDown(box.Center,MouseButton.Left); f.Window.MouseUp(box.Center,MouseButton.Left);
        Authority(!initial,3); await Settle(700); Authority(!initial,3);
        Capture(f.Window,$"automatic-overlays-commit-{width}-{reduced}");
    }
}
