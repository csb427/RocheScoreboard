using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaFact]
    public async Task BowlingCardRecordedArrivalUsesRealFramesAndKeepsTheInputFixed()
    {
        using var scope=new Scope();var f=await BowlingOpen(scope,1280,850);
        string state=State(Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match"));
        var bounds=Painted(f.Window,f.Button);
        string directory=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.GetTempPath(),"bowling-motion");
        Directory.CreateDirectory(directory);
        var samples=new List<object>();var clock=System.Diagnostics.Stopwatch.StartNew();TapNow(f.Window,f.Button);
        bool witnessed=false;
        foreach(int time in new[]{0,30,70,110,170,240,330,480})
        {
            // Saving a full frame can outrun the requested capture interval.
            // A zero-delay await would then keep the recorder on the UI
            // thread and starve its own animation timer between captures.
            int delay=(int)Math.Max(16,time-clock.Elapsed.TotalMilliseconds);await Settle(delay);
            using var frame=f.Window.CaptureRenderedFrame();Assert.NotNull(frame);
            double progress=f.Glyph.Progress;witnessed|=progress>0&&progress<1;
            string file=$"frame-{samples.Count:D2}.png";frame.Save(Path.Combine(directory,file));
            samples.Add(new{file,milliseconds=clock.Elapsed.TotalMilliseconds,progress});
            Assert.Equal(bounds,Painted(f.Window,f.Button));Assert.False(f.Output.IsVisible);
        }
        File.WriteAllText(Path.Combine(directory,"timeline.json"),System.Text.Json.JsonSerializer.Serialize(new{bounds,samples},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Assert.True(witnessed);Assert.Equal(1,f.Glyph.Progress);
        Assert.Equal(state,State(Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match")));
    }
    private static Border BowlingPart(CricketBowlingFiguresGlyph glyph,string name)
        =>Assert.Single(glyph.GetVisualDescendants().OfType<Border>(),x=>x.Name==name);
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task BowlingCardKeyboardEnterAndSpaceSettleWithoutADeferredSelection(int width,int height)
    {
        using var scope=new Scope();var f=await BowlingOpen(scope,width,height);
        string state=State(Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match"));
        var requests=new List<string>();f.Panel.PresentationChanged+=requests.Add;
        Assert.True(f.Button.Focus(Avalonia.Input.NavigationMethod.Tab));
        KeyNow(f.Window,Avalonia.Input.PhysicalKey.Enter);
        Assert.Equal("bowling",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(1,f.Glyph.Progress);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(Named<Button>(f.Panel,"Presentationscorebug"))).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Live command must be accepted between keyboard bowling choices.");
        Assert.True(f.Button.Focus(Avalonia.Input.NavigationMethod.Tab));KeyNow(f.Window,Avalonia.Input.PhysicalKey.Space);
        Assert.Equal("bowling",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(1,f.Glyph.Progress);
        Assert.Equal(new[]{"bowling","scorebug","bowling"},requests);await Settle(500);Assert.Equal(1,f.Glyph.Progress);
        Assert.Equal(state,State(Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match")));Assert.False(f.Output.IsVisible);
    }
    private static async Task<(CricketControlPanel Panel,Window Window,Button Button,CricketBowlingFiguresGlyph Glyph,Window Output)> BowlingOpen(Scope scope,int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true); var summary=new CricketSummaryControl();
        var panel=new CricketControlPanel(); panel.SetMatch(match); panel.SetSummaryControl(summary);
        var window=scope.Window(panel,width,height); var output=scope.Window(summary,386,193); output.Hide();
        panel.SetDisplayWindow(output); panel.SetOperatorOutputVisible(false);
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(panel,"OutputTab"))).Select();
        await Settle(180); var button=Named<Button>(panel,"Presentationbowling");
        await Reveal(window,button); await StableReachable(window,button);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)Directory.CreateDirectory(directory);
        return(panel,window,button,Named<CricketBowlingFiguresGlyph>(panel,"CricketBowlingGlyphView"),output);
    }
    [AvaloniaTheory]
    [InlineData(0)] [InlineData(.5)] [InlineData(1)]
    public async Task BowlingDiagramHasIndependentStatColumnsAndFixedBallAndFrame(double progress)
    {
        using var scope=new Scope(true); var f=await BowlingOpen(scope,1280,850);
        // Explicit passive render poses, not user selection or simulated data.
        f.Glyph.Progress=progress; await Settle(30);
        Assert.Equal(44,f.Glyph.Width); Assert.Equal(28,f.Glyph.Height);
        Assert.False(f.Glyph.IsHitTestVisible); Assert.False(f.Glyph.Focusable);
        var frame=BowlingPart(f.Glyph,"CricketBowlingFrame"); var ball=BowlingPart(f.Glyph,"CricketBowlingBall");
        Assert.Equal(34,frame.Width); Assert.Equal(28,frame.Height); Assert.Equal(10,Canvas.GetLeft(frame));
        Assert.Equal(7,ball.Width); Assert.Equal(7,ball.Height); Assert.Equal(0,Canvas.GetLeft(ball));
        Assert.Null(frame.RenderTransform); Assert.Null(ball.RenderTransform);
        var first=BowlingPart(f.Glyph,"CricketBowlingFigure0");var last=BowlingPart(f.Glyph,"CricketBowlingFigure11");
        Assert.InRange(first.Opacity,.25,.9); Assert.InRange(last.Opacity,.25,.9);
        var figures=Enumerable.Range(0,12).Select(i=>BowlingPart(f.Glyph,"CricketBowlingFigure"+i)).ToArray();
        Assert.All(figures,part=>{Assert.False(part.IsHitTestVisible);Assert.False(part.Focusable);Assert.Equal(3,part.Width);Assert.Equal(2,part.Height);});
        var seam=Assert.IsType<RotateTransform>(BowlingPart(f.Glyph,"CricketBowlingSeam").RenderTransform);
        if(progress==.5)
        {
            Assert.True(first.Opacity>last.Opacity);Assert.Equal(12,seam.Angle,4);
            // Every leading statistic column has a distinct actual pose.
            for(int column=1;column<4;column++)Assert.True(figures[(column-1)*3].Opacity>figures[column*3].Opacity);
        }
        else
        {
            Assert.All(figures,part=>{Assert.Equal(progress == 0 ? .25 : .9,part.Opacity,4);
                Assert.Equal(progress == 0 ? .35 : 1,Assert.IsType<ScaleTransform>(part.RenderTransform).ScaleY,4);});
            Assert.Equal(0,seam.Angle,4);
        }
        Assert.False(f.Output.IsVisible); PromptCapture(f.Window,$"bowling-passive-pose-{progress}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)] [InlineData(420,320,false)]
    [InlineData(1280,850,true)] [InlineData(420,320,true)]
    public async Task BowlingCardRetargetsItsPartsAndKeepsSelectionImmediateAndOutputOff(int width,int height,bool reduced)
    {
        using var scope=new Scope(reduced);var f=await BowlingOpen(scope,width,height);
        var match=Read<Roche_Scoreboard.Models.CricketMatchManager>(f.Panel,"_match");string state=State(match);
        var requests=new List<string>();f.Panel.PresentationChanged+=requests.Add;
        Assert.Equal("Bowling figures",ControlAutomationPeer.CreatePeerForElement(f.Button)?.GetName());
        var bounds=Painted(f.Window,f.Button); TapNow(f.Window,f.Button);
        Assert.Equal("bowling",Read<string>(f.Panel,"_currentPresentation")); Assert.Single(requests);
        Assert.Equal(1,Named<TextBlock>(f.Panel,"CricketBowlingCheck").Opacity);Assert.False(f.Output.IsVisible);
        await Settle(100); Assert.Equal(bounds,Painted(f.Window,f.Button));
        if(reduced)Assert.Equal(1,f.Glyph.Progress);else Assert.InRange(f.Glyph.Progress,.001,.999);
        PromptCapture(f.Window,$"bowling-arrival-{width}-{reduced}");
        var live=Named<Button>(f.Panel,"Presentationscorebug");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(live)).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="scorebug","Live source command must be accepted during bowling arrival.");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(f.Button)).Invoke();
        await Until(()=>Read<string>(f.Panel,"_currentPresentation")=="bowling","Bowling must retarget to latest selection.");
        Assert.Equal(new[]{"bowling","scorebug","bowling"},requests);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true); await Settle(30);
        Assert.Equal(1,f.Glyph.Progress);await Settle(650);Assert.Equal(1,f.Glyph.Progress);
        Assert.Equal(state,State(match));Assert.False(f.Output.IsVisible);Assert.Equal(3,requests.Count);
        PromptCapture(f.Window,$"bowling-final-{width}-{reduced}");
    }
    [AvaloniaTheory]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task BowlingArrivalCannotSurviveClearingAndRebindingTheMatch(int width,int height)
    {
        using var scope=new Scope();var f=await BowlingOpen(scope,width,height);
        TapNow(f.Window,f.Button);await Settle(80);Assert.InRange(f.Glyph.Progress,.001,.999);
        f.Panel.ClearMatch();var replacement=CricketReliabilityChecks.CreateMatch(true);string state=State(replacement);
        f.Panel.SetMatch(replacement);await Settle(650);
        Assert.Equal("scorebug",Read<string>(f.Panel,"_currentPresentation"));Assert.Equal(0,f.Glyph.Progress);
        // ClearMatch deliberately returns to Match. Re-enter Output through
        // its actual tab before querying newly attached visual descendants.
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(Named<TabItem>(f.Panel,"OutputTab"))).Select();
        await Until(()=>ReferenceEquals(TopLevel.GetTopLevel(f.Glyph),f.Window),"Rebound Output tab must attach its original passive glyph.");
        Assert.Same(f.Glyph,Named<CricketBowlingFiguresGlyph>(f.Panel,"CricketBowlingGlyphView"));Assert.Equal(0,f.Glyph.Progress);
        Assert.Equal(0,Named<TextBlock>(f.Panel,"CricketBowlingCheck").Opacity);
        Assert.Equal(state,State(replacement));Assert.False(f.Output.IsVisible);
    }
}
