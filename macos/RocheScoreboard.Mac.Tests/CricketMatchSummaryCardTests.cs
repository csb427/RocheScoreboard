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
    private static T SummaryPart<T>(CricketMatchSummaryGlyph glyph,string name) where T:Control
        =>Assert.Single(glyph.GetVisualDescendants().OfType<T>(),x=>x.Name==name);
    private static async Task<(CricketControlPanel Panel,Window Window,Button Button,CricketMatchSummaryGlyph Glyph,Window Output)> SummaryCardOpen(Scope scope,int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var summary=new CricketSummaryControl();
        var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetSummaryControl(summary);
        var window=scope.Window(panel,width,height);var output=scope.Window(summary,386,193);output.Hide();
        panel.SetDisplayWindow(output);panel.SetOperatorOutputVisible(false);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();
        await Settle(180);var button=Named<Button>(panel,"Presentationsummary");
        await Reveal(window,button);await StableReachable(window,button);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        return(panel,window,button,Named<CricketMatchSummaryGlyph>(panel,"CricketMatchSummaryGlyphView"),output);
    }
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(.5)] [InlineData(1)]
    public async Task MatchSummaryDiagramHasTwoIndependentPanelsWithoutInventedFigures(double progress)
    {
        using var scope=new Scope(true);var f=await SummaryCardOpen(scope,1280,850);
        f.Glyph.Progress=progress;await Settle(30);
        Assert.Equal(44,f.Glyph.Width);Assert.Equal(28,f.Glyph.Height);Assert.False(f.Glyph.IsHitTestVisible);Assert.False(f.Glyph.Focusable);
        Assert.Empty(f.Glyph.GetVisualDescendants().OfType<TextBlock>());
        var top=SummaryPart<Canvas>(f.Glyph,"CricketSummaryTeamPanel0");var bottom=SummaryPart<Canvas>(f.Glyph,"CricketSummaryTeamPanel1");
        Assert.Equal(new RelativePoint(0,.5,RelativeUnit.Relative),top.RenderTransformOrigin);
        Assert.Equal(new RelativePoint(1,.5,RelativeUnit.Relative),bottom.RenderTransformOrigin);
        Assert.Equal(40,top.Width);Assert.Equal(40,bottom.Width);Assert.Equal(11,top.Height);Assert.Equal(11,bottom.Height);
        var topScale=Assert.IsType<ScaleTransform>(top.RenderTransform);var bottomScale=Assert.IsType<ScaleTransform>(bottom.RenderTransform);
        var name0=Assert.IsType<ScaleTransform>(SummaryPart<Border>(f.Glyph,"CricketSummaryName0").RenderTransform);
        var name1=Assert.IsType<ScaleTransform>(SummaryPart<Border>(f.Glyph,"CricketSummaryName1").RenderTransform);
        var score0=SummaryPart<Border>(f.Glyph,"CricketSummaryScoreMark0");var score1=SummaryPart<Border>(f.Glyph,"CricketSummaryScoreMark1");
        if(progress==.5){Assert.True(topScale.ScaleX>bottomScale.ScaleX);Assert.True(name0.ScaleX>name1.ScaleX);Assert.True(score0.Opacity>score1.Opacity);}
        else
        {
            Assert.Equal(progress==0?.88:1,topScale.ScaleX,4);Assert.Equal(topScale.ScaleX,bottomScale.ScaleX,4);
            Assert.Equal(progress==0?.45:1,name0.ScaleX,4);Assert.Equal(name0.ScaleX,name1.ScaleX,4);
            Assert.Equal(progress==0?.25:.9,score0.Opacity,4);Assert.Equal(score0.Opacity,score1.Opacity,4);
        }
        Assert.False(f.Output.IsVisible);PromptCapture(f.Window,$"summary-passive-pose-{progress}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task SummaryCardKeepsActualSelectionImmediateThroughReversalAndReducedMotion(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var f=await SummaryCardOpen(scope,width,height);
        var match=Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match");string state=State(match);
        var requests=new List<string>();f.Panel.PresentationChanged+=requests.Add;
        Assert.Equal("Match summary",ControlAutomationPeer.CreatePeerForElement(f.Button)?.GetName());
        var bounds=Painted(f.Window,f.Button);TapNow(f.Window,f.Button);
        Assert.Equal("summary",Read<string>(f.Panel,"_currentPresentation"));Assert.Single(requests);
        Assert.Equal(1,Named<TextBlock>(f.Panel,"CricketMatchSummaryCheck").Opacity);Assert.False(f.Output.IsVisible);
        await Settle(100);Assert.Equal(bounds,Painted(f.Window,f.Button));
        if(reduced)Assert.Equal(1,f.Glyph.Progress);else Assert.InRange(f.Glyph.Progress,.001,.999);
        PromptCapture(f.Window,$"summary-arrival-{width}-{reduced}");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Live command must be accepted during summary arrival.");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(f.Button)).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="summary","Summary must retarget to current selection.");
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Assert.Equal(1,f.Glyph.Progress);
        await Settle(650);Assert.Equal(1,f.Glyph.Progress);Assert.Equal(state,State(match));Assert.False(f.Output.IsVisible);
        Assert.Equal(new[]{"summary","scorebug","summary"},requests);PromptCapture(f.Window,$"summary-final-{width}-{reduced}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task SummaryKeyboardAndClearRebindKeepOnlyCurrentPresentation(int width,int height)
    {
        using var scope=new Scope();var f=await SummaryCardOpen(scope,width,height);
        Assert.True(f.Button.Focus(NavigationMethod.Tab));KeyNow(f.Window,PhysicalKey.Enter);
        Assert.Equal("summary",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(1,f.Glyph.Progress);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Return to Live before witnessing a new pointer arrival.");
        await Reveal(f.Window,f.Button);await StableReachable(f.Window,f.Button);TapNow(f.Window,f.Button);
        await Settle(80);Assert.InRange(f.Glyph.Progress,.001,.999);
        f.Panel.ClearMatch();var replacement=CricketReliabilityChecks.CreateMatch(true);string state=State(replacement);f.Panel.SetMatch(replacement);
        await Settle(650);Assert.Equal("scorebug",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(0,f.Glyph.Progress);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(f.Panel,"OutputTab"))).Select();
        await Until(()=>ReferenceEquals(TopLevel.GetTopLevel(f.Glyph),f.Window),"Output re-entry must attach the original summary glyph.");
        Assert.Equal(0,Named<TextBlock>(f.Panel,"CricketMatchSummaryCheck").Opacity);
        Assert.Equal(state,State(replacement));Assert.False(f.Output.IsVisible);
    }
}
