using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static T ManhattanPart<T>(CricketManhattanGlyph glyph,string name) where T:Control
        =>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),x=>x.Name==name);
    private static async Task<(CricketControlPanel Panel,Window Window,Button Button,CricketManhattanGlyph Glyph,Window Output)> ManhattanCardOpen(Scope scope,int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var summary=new CricketSummaryControl();
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetSummaryControl(summary);
        var window=scope.Window(panel,width,height);var output=scope.Window(summary,386,193);output.Hide();
        panel.SetDisplayWindow(output);panel.SetOperatorOutputVisible(false);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();
        await Settle(180);var button=Named<Button>(panel,"Presentationmanhattan");
        await Reveal(window,button);await StableReachable(window,button);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        return(panel,window,button,Named<CricketManhattanGlyph>(panel,"CricketManhattanGlyphView"),output);
    }
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(.5)] [InlineData(1)]
    public async Task ManhattanDiagramKeepsBaselineAndIndependentPairedBars(double progress)
    {
        using var scope=new Scope(true);var f=await ManhattanCardOpen(scope,1280,850);
        f.Glyph.Progress=progress;await Settle(30);
        Assert.Equal(44,f.Glyph.Width);Assert.Equal(28,f.Glyph.Height);
        Assert.False(f.Glyph.IsHitTestVisible);Assert.False(f.Glyph.Focusable);
        Assert.Empty(f.Glyph.GetVisualDescendants().OfType<TextBlock>());
        var baseline=ManhattanPart<Border>(f.Glyph,"CricketManhattanBaseline");
        Assert.Equal(.6,baseline.Opacity);Assert.Equal(25,Canvas.GetTop(baseline));Assert.Null(baseline.RenderTransform);
        double[] heights={12,17,20,13,16,22};var scales=new List<double>();
        for(int i=0;i<6;i++)
        {
            var bar=ManhattanPart<Border>(f.Glyph,"CricketManhattanBar"+i);
            Assert.Equal(heights[i],bar.Height);Assert.Equal(4,bar.Width);
            Assert.Equal(24,Canvas.GetTop(bar)+bar.Height);Assert.Equal(4+i*6,Canvas.GetLeft(bar));
            Assert.Equal(new RelativePoint(.5,1,RelativeUnit.Relative),bar.RenderTransformOrigin);
            var scale=Assert.IsType<ScaleTransform>(bar.RenderTransform);Assert.Equal(1,scale.ScaleX);
            Assert.False(bar.IsHitTestVisible);Assert.False(bar.Focusable);scales.Add(scale.ScaleY);
            if(progress==0)Assert.Equal(.45,scale.ScaleY,4);
            else if(progress==1)Assert.Equal(1,scale.ScaleY,4);
            else Assert.InRange(scale.ScaleY,.4501,.9999);
        }
        if(progress==.5){Assert.Equal(6,scales.Distinct().Count());for(int i=1;i<6;i++)Assert.True(scales[i-1]>scales[i]);}
        for(int i=0;i<2;i++)
        {
            var mark=ManhattanPart<Border>(f.Glyph,"CricketManhattanMarker"+i);
            Assert.Equal(progress==1?1:0,mark.Opacity,4);Assert.Equal(2,mark.Width);Assert.Equal(2,mark.Height);
        }
        Assert.False(f.Output.IsVisible);PromptCapture(f.Window,$"manhattan-passive-pose-{progress}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task ManhattanCardKeepsActualSelectionImmediateThroughReversalAndReducedMotion(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var f=await ManhattanCardOpen(scope,width,height);
        var match=Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match");string state=State(match);
        var requests=new List<string>();f.Panel.PresentationChanged+=requests.Add;
        Assert.Equal("Manhattan",ControlAutomationPeer.CreatePeerForElement(f.Button)?.GetName());
        var bounds=Painted(f.Window,f.Button);TapNow(f.Window,f.Button);
        Assert.Equal("manhattan",Read<string>(f.Panel,"_currentPresentation"));Assert.Single(requests);
        Assert.Equal(1,Named<TextBlock>(f.Panel,"CricketManhattanCheck").Opacity);Assert.False(f.Output.IsVisible);
        await Settle(100);Assert.Equal(bounds,Painted(f.Window,f.Button));
        if(reduced)Assert.Equal(1,f.Glyph.Progress);else Assert.InRange(f.Glyph.Progress,.001,.999);
        PromptCapture(f.Window,$"manhattan-arrival-{width}-{reduced}");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Live command must be accepted during Manhattan arrival.");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(f.Button)).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="manhattan","Manhattan must retarget to current selection.");
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Assert.Equal(1,f.Glyph.Progress);
        await Settle(650);Assert.Equal(1,f.Glyph.Progress);Assert.Equal(state,State(match));Assert.False(f.Output.IsVisible);
        Assert.Equal(new[]{"manhattan","scorebug","manhattan"},requests);PromptCapture(f.Window,$"manhattan-final-{width}-{reduced}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task ManhattanKeyboardAndClearRebindKeepOnlyCurrentPresentation(int width,int height)
    {
        using var scope=new Scope();var f=await ManhattanCardOpen(scope,width,height);
        Assert.True(f.Button.Focus(NavigationMethod.Tab));KeyNow(f.Window,PhysicalKey.Enter);
        Assert.Equal("manhattan",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(1,f.Glyph.Progress);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Return to Live before witnessing a new pointer arrival.");
        await Reveal(f.Window,f.Button);await StableReachable(f.Window,f.Button);TapNow(f.Window,f.Button);
        await Settle(80);Assert.InRange(f.Glyph.Progress,.001,.999);
        f.Panel.ClearMatch();var replacement=CricketReliabilityChecks.CreateMatch(true);string state=State(replacement);f.Panel.SetMatch(replacement);
        await Settle(650);Assert.Equal("scorebug",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(0,f.Glyph.Progress);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(f.Panel,"OutputTab"))).Select();
        await Until(()=>ReferenceEquals(TopLevel.GetTopLevel(f.Glyph),f.Window),"Output re-entry must attach the original Manhattan glyph.");
        Assert.Equal(0,Named<TextBlock>(f.Panel,"CricketManhattanCheck").Opacity);
        Assert.Equal(state,State(replacement));Assert.False(f.Output.IsVisible);
    }
}
