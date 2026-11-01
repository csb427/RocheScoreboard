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
    private static T WormPart<T>(CricketScorewormGlyph glyph,string name) where T:Control
        =>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),x=>x.Name==name);
    private static async Task<(CricketControlPanel Panel,Window Window,Button Button,CricketScorewormGlyph Glyph,Window Output)> WormCardOpen(Scope scope,int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var summary=new CricketSummaryControl();
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetSummaryControl(summary);
        var window=scope.Window(panel,width,height);var output=scope.Window(summary,386,193);output.Hide();
        panel.SetDisplayWindow(output);panel.SetOperatorOutputVisible(false);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();
        await Settle(180);var button=Named<Button>(panel,"Presentationworm");
        await Reveal(window,button);await StableReachable(window,button);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        return(panel,window,button,Named<CricketScorewormGlyph>(panel,"CricketScorewormGlyphView"),output);
    }
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(.5)] [InlineData(1)]
    public async Task ScorewormDiagramKeepsAxesAndSeparateAbstractTrails(double progress)
    {
        using var scope=new Scope(true);var f=await WormCardOpen(scope,1280,850);
        f.Glyph.Progress=progress;await Settle(30);
        Assert.Equal(44,f.Glyph.Width);Assert.Equal(28,f.Glyph.Height);
        Assert.False(f.Glyph.IsHitTestVisible);Assert.False(f.Glyph.Focusable);
        Assert.Empty(f.Glyph.GetVisualDescendants().OfType<TextBlock>());
        var axes=WormPart<Avalonia.Controls.Shapes.Path>(f.Glyph,"CricketWormAxes");
        Assert.Equal(.6,axes.Opacity);Assert.Null(axes.RenderTransform);
        var trails=new double[2];
        for(int i=0;i<2;i++)
        {
            var guide=WormPart<Avalonia.Controls.Shapes.Path>(f.Glyph,"CricketWormGuide"+i);
            var accent=WormPart<Avalonia.Controls.Shapes.Path>(f.Glyph,"CricketWormAccent"+i);
            Assert.Same(guide.Data,accent.Data);
            Assert.False(guide.IsHitTestVisible);Assert.False(accent.IsHitTestVisible);
            var clip=Assert.IsType<RectangleGeometry>(accent.Clip);trails[i]=clip.Rect.Width;
            Assert.Equal(28,clip.Rect.Height);Assert.Equal(0,clip.Rect.X);
            Assert.Equal(progress==0?0:progress==1?44:44*((progress-i*.15)/(i==0?.8:.85)),trails[i],4);
            var end=WormPart<Avalonia.Controls.Shapes.Ellipse>(f.Glyph,"CricketWormEnd"+i);
            Assert.Equal(progress==1?1:0,end.Opacity,4);Assert.Equal(39.5,Canvas.GetLeft(end));
            Assert.Equal(i==0?2.5:8.5,Canvas.GetTop(end));
        }
        if(progress==.5)Assert.True(trails[0]>trails[1]);
        Assert.False(f.Output.IsVisible);PromptCapture(f.Window,$"worm-passive-pose-{progress}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task WormCardKeepsActualSelectionImmediateThroughReversalAndReducedMotion(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var f=await WormCardOpen(scope,width,height);
        var match=Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match");string state=State(match);
        var requests=new List<string>();f.Panel.PresentationChanged+=requests.Add;
        Assert.Equal("Scoreworm",ControlAutomationPeer.CreatePeerForElement(f.Button)?.GetName());
        var bounds=Painted(f.Window,f.Button);TapNow(f.Window,f.Button);
        Assert.Equal("worm",Read<string>(f.Panel,"_currentPresentation"));Assert.Single(requests);
        Assert.Equal(1,Named<TextBlock>(f.Panel,"CricketScorewormCheck").Opacity);Assert.False(f.Output.IsVisible);
        await Settle(100);Assert.Equal(bounds,Painted(f.Window,f.Button));
        if(reduced)Assert.Equal(1,f.Glyph.Progress);else Assert.InRange(f.Glyph.Progress,.001,.999);
        PromptCapture(f.Window,$"worm-arrival-{width}-{reduced}");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Live command must be accepted during Scoreworm arrival.");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(f.Button)).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="worm","Scoreworm must retarget to current selection.");
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Assert.Equal(1,f.Glyph.Progress);
        await Settle(650);Assert.Equal(1,f.Glyph.Progress);Assert.Equal(state,State(match));Assert.False(f.Output.IsVisible);
        Assert.Equal(new[]{"worm","scorebug","worm"},requests);PromptCapture(f.Window,$"worm-final-{width}-{reduced}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task WormKeyboardAndClearRebindKeepOnlyCurrentPresentation(int width,int height)
    {
        using var scope=new Scope();var f=await WormCardOpen(scope,width,height);
        Assert.True(f.Button.Focus(NavigationMethod.Tab));KeyNow(f.Window,PhysicalKey.Enter);
        Assert.Equal("worm",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(1,f.Glyph.Progress);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Return to Live before witnessing a new pointer arrival.");
        await Reveal(f.Window,f.Button);await StableReachable(f.Window,f.Button);TapNow(f.Window,f.Button);
        await Settle(80);Assert.InRange(f.Glyph.Progress,.001,.999);
        f.Panel.ClearMatch();var replacement=CricketReliabilityChecks.CreateMatch(true);string state=State(replacement);f.Panel.SetMatch(replacement);
        await Settle(650);Assert.Equal("scorebug",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(0,f.Glyph.Progress);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(f.Panel,"OutputTab"))).Select();
        await Until(()=>ReferenceEquals(TopLevel.GetTopLevel(f.Glyph),f.Window),"Output re-entry must attach the original Scoreworm glyph.");
        Assert.Equal(0,Named<TextBlock>(f.Panel,"CricketScorewormCheck").Opacity);
        Assert.Equal(state,State(replacement));Assert.False(f.Output.IsVisible);
    }
}
