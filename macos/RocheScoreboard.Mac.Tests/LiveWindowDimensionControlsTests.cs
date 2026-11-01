using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task<Expander> OpenOriginalWindowDimensions(AflFixture f)
    {
        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);
        var workspace=Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(),x=>x.IsEffectivelyVisible);
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
        Assert.True(search.IsFocused); f.Window.KeyTextInput("Window dimensions"); await Settle(150);
        var input=Named<TextBox>(f.Window,"WindowWidthBox");
        var expander=Assert.Single(input.GetLogicalAncestors().OfType<Expander>());
        expander.ApplyTemplate(); await Settle(50);
        var header=Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
        if(!expander.IsExpanded) Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(header)).Toggle();
        await Until(()=>expander.IsExpanded&&ReferenceEquals(TopLevel.GetTopLevel(input),f.Window)&&input.Bounds.Height>0,"Original dimension expander must actually attach and lay out its field.");
        return expander;
    }
    private static OperatorActionBorder[] DimensionSteppers(TextBox input)
        => Assert.IsAssignableFrom<Panel>(Assert.IsType<Border>(input.Parent).Parent).Children.OfType<OperatorActionBorder>().ToArray();
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalWindowDimensionTargetsHaveContextAndRemainReachable(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height); var expander=await OpenOriginalWindowDimensions(f);
        var w=Named<TextBox>(f.Window,"WindowWidthBox"); var h=Named<TextBox>(f.Window,"WindowHeightBox");
        var wx=DimensionSteppers(w); var hx=DimensionSteppers(h); Assert.Equal(2,wx.Length); Assert.Equal(2,hx.Length);
        var apply=Assert.Single(expander.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"APPLY SIZE"));
        var reset=Assert.Single(expander.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"RESET TO 386 × 193"));
        Control[] targets=[wx[0],w,wx[1],hx[0],h,hx[1],apply,reset];
        string[] names=["Decrease output width","Output width in pixels","Increase output width","Decrease output height","Output height in pixels","Increase output height","Apply output dimensions","Reset output dimensions to 386 by 193"];
        string model=ManualWeatherModel(f), prefs=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        object? output=Mr6Read<object?>(f,"_sharedOutputPixels");
        string wd=w.Text??"",hd=h.Text??"";
        for(int i=0;i<targets.Length;i++)
        {
            var target=targets[i]; await RevealBreakCardActor(f,target);
            var box=FinalsPainted(f.Window,target); Capture(f.Window,$"output-dimension-{i}-{width}");
            Assert.True(box.Width>=39.5&&box.Height>=39.5,"Every original dimension input/action must have40px usable target.");
            Assert.Equal(names[i],ControlAutomationPeer.CreatePeerForElement(target)?.GetName());
            Assert.Equal(wd,w.Text); Assert.Equal(hd,h.Text); Assert.Equal(output,Mr6Read<object?>(f,"_sharedOutputPixels"));
            Assert.Equal(model,ManualWeatherModel(f)); Assert.Equal(prefs,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
        }
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task OriginalWindowDimensionSteppersEditOnlyDraftAndRetainBounds(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height); await OpenOriginalWindowDimensions(f);
        var w=Named<TextBox>(f.Window,"WindowWidthBox"); var h=Named<TextBox>(f.Window,"WindowHeightBox");
        string model=ManualWeatherModel(f), prefs=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        object? output=Mr6Read<object?>(f,"_sharedOutputPixels");
        foreach(var entry in new[]{(input:w,other:h,max:7680),(input:h,other:w,max:4320)})
        {
            var controls=DimensionSteppers(entry.input); Assert.Equal(2,controls.Length);
            string other=entry.other.Text??"";
            entry.input.Text="50"; await RevealBreakCardActor(f,controls[0]); Key(f.Window,PhysicalKey.Space); Assert.Equal("50",entry.input.Text);
            await RevealBreakCardActor(f,controls[1]); Key(f.Window,PhysicalKey.Space); Assert.Equal("51",entry.input.Text);
            entry.input.Text=entry.max.ToString();
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(controls[1])).Invoke(); await Settle(40);
            Assert.Equal(entry.max.ToString(),entry.input.Text);
            await RevealBreakCardActor(f,controls[0]); var box=FinalsPainted(f.Window,controls[0]);
            f.Window.MouseMove(box.Center); f.Window.MouseDown(box.Center,MouseButton.Left); f.Window.MouseUp(box.Center,MouseButton.Left);
            Assert.Equal((entry.max-1).ToString(),entry.input.Text);
            entry.input.Text="bad"; Key(f.Window,PhysicalKey.Space); Assert.Equal("bad",entry.input.Text);
            Assert.Equal(other,entry.other.Text);
            Assert.Equal(output,Mr6Read<object?>(f,"_sharedOutputPixels")); Assert.Equal(model,ManualWeatherModel(f));
            Assert.Equal(prefs,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
        }
    }
}
