using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class UnifiedSetupTests
{
    private static Rect FeedbackBox(Control target, Control ancestor)
        => new(target.TranslatePoint(default,ancestor)!.Value,target.Bounds.Size);
    private static void FeedbackContained(Control target, Control host)
    {
        Rect box=FeedbackBox(target,host);
        Assert.True(box.Width>0 && box.Height>0 && box.Left>=-1 && box.Top>=-1 &&
            box.Right<=host.Bounds.Width+1 && box.Bottom<=host.Bounds.Height+1,$"{target.Name}: {box} in {host.Bounds.Size}");
    }

    [AvaloniaTheory]
    [InlineData(1280,800)][InlineData(800,500)][InlineData(640,480)][InlineData(420,320)]
    [Trait("Evidence","NativeFeedbackLayout")]
    public async Task ReleaseBrowserFitsViewportAndAllReleasesRemainReachable(int width,int height)
    {
        using var f=new Fixture();await f.Ready(width,height);
        Click(f.Home.FindControl<Button>("ChangelogToggle")!);await Settle(500);
        var panel=f.Home.FindControl<Control>("ChangelogPanel")!;FeedbackContained(panel,f.Window);
        Assert.DoesNotContain(f.Home.FindControl<ScrollViewer>("ResponsiveScrollHost")!,panel.GetVisualAncestors());
        var rail=f.Home.FindControl<Control>("ReleaseVersionRail")!;
        var buttons=f.Home.FindControl<StackPanel>("ReleaseVersionButtons")!.Children.OfType<Button>().ToArray();
        Assert.True(buttons.Length>5);
        if(rail.IsVisible)
        {
            var viewport=f.Home.FindControl<ScrollViewer>("ReleaseHistoryScroller")!;
            foreach(var button in new[]{buttons[0],buttons[^1],buttons[0]})
            {
                button.Focus();button.BringIntoView();await Settle(100);
                FeedbackContained(button,viewport);
                Assert.DoesNotContain(button.GetVisualDescendants(),x=>x is OperatorGlassSurface);
                Assert.Contains(button.GetVisualDescendants(),x=>x.Name=="ReleaseKeyboardFocus");
            }
            Key(f.Window,PhysicalKey.End);await Settle(100);Assert.True(buttons[^1].IsFocused);
            Click(buttons[^1]);await Settle(150);
            Assert.Equal(buttons[^1].Tag,f.Home.FindControl<ComboBox>("ReleaseVersionChoice")!.SelectedValue);
        }
        else
        {
            var choice=f.Home.FindControl<ComboBox>("ReleaseVersionChoice")!;FeedbackContained(choice,f.Window);
            choice.SelectedIndex=choice.ItemCount-1;await Settle(100);
            Assert.Equal(buttons[^1].Tag,choice.SelectedValue);
        }
        FeedbackContained(f.Home.FindControl<ScrollViewer>("ReleaseScroller")!,f.Window);
        Capture(f.Window,$"release-after-{width}-{height}");
        Key(f.Window,PhysicalKey.Escape);await Settle(350);Assert.False(f.Home.HasOpenSheet);
    }

    [AvaloniaTheory]
    [InlineData(false,true)][InlineData(true,true)][InlineData(false,false)][InlineData(true,false)]
    [Trait("Evidence","NativeFeedbackWizard")]
    public async Task BothSportsRevealActualTeamFormsWithoutChangingDraftOrBlockingInput(bool cricket,bool animate)
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=animate;
        try
        {
            using var f=new Fixture();await f.Ready();f.Start();await Settle(350);f.Continue(cricket);
            UserControl wizard=cricket?f.Cricket:f.Afl;
            for(int i=0;i<30 && !wizard.GetVisualDescendants().OfType<Control>().Any(x=>x.Name=="HomeSetupSurface");i++)
            {using var frame=f.Window.CaptureRenderedFrame();await Settle(10);}
            var forms=new[]{wizard.GetVisualDescendants().OfType<Control>().Single(x=>x.Name=="HomeSetupSurface"),wizard.GetVisualDescendants().OfType<Control>().Single(x=>x.Name=="AwaySetupSurface")};
            var samples=new List<double[]>();
            var diagnostics=new List<object>();
            diagnostics.Add(new{wizard.IsLoaded,wizard.IsVisible,forms=forms.Select(x=>new{x.Name,x.IsLoaded,x.IsVisible,x.Bounds}).ToArray()});
            for(int i=0;i<15;i++){using var frame=f.Window.CaptureRenderedFrame();await Settle(20);samples.Add(forms.Select(x=>x.Opacity).ToArray());}
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,$"wizard-motion-{cricket}-{animate}.json"),JsonSerializer.Serialize(new{diagnostics,samples,forms=forms.Select(x=>new{x.Name,x.IsLoaded,x.Bounds}).ToArray(),enabled=OperatorMotion.Enabled},new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}
            if(animate)Assert.Contains(samples,x=>x.Any(value=>value>0 && value<.99));
            else Assert.All(samples,x=>Assert.All(x,value=>Assert.Equal(1,value)));
            Assert.All(forms,x=>Assert.True(x.IsEffectivelyEnabled));
            var name=wizard.FindControl<TextBox>("SetupHomeNameBox")!;name.Text="Draft retained during motion";
            await Settle(220);Assert.All(forms,x=>Assert.Equal(1,x.Opacity));
            Capture(f.Window,$"teams-after-{cricket}-{animate}");
            Click(wizard.FindControl<Button>("BackButton")!);f.Continue(cricket);
            Assert.Equal("Draft retained during motion",name.Text);
            Click(wizard.FindControl<Button>("BackButton")!);Click(f.Afl.FindControl<Button>("CancelSetupButton")!);
            f.Start();await Settle(450);Assert.True(f.Afl.FindControl<Control>("SportStepContainer")!.IsVisible);
            Assert.False(f.Cricket.IsVisible);Assert.False(f.Home.IsVisible);
            f.Continue(cricket);await Settle(450);Assert.All(forms,x=>Assert.Equal(1,x.Opacity));
        }
        finally{OperatorMotion.AnimationOverride=previous;}
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","NativeFeedbackSession")]
    public async Task DeferredSetupApprovalCannotCompleteAResetWizard(bool cricket)
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(cricket);await Settle(350);
        UserControl wizard=cricket?f.Cricket:f.Afl;
        var approval=new TaskCompletionSource<bool>();int confirmations=0;
        Func<Task<bool>> confirm=()=>{confirmations++;return approval.Task;};
        if(cricket)f.Cricket.ConfirmCompletionAsync=confirm;else f.Afl.ConfirmCompletionAsync=confirm;
        string before=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        Invoke(wizard,"FinishWizard");Invoke(wizard,"FinishWizard");Assert.Equal(1,confirmations);
        if(cricket)f.Cricket.Reset();else f.Afl.Reset();
        approval.SetResult(true);await Settle(150);
        Assert.Null(cricket?(object?)f.Cricket.Result:f.Afl.Result);
        Assert.Equal(before,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
    }
}

public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1550,1050)][InlineData(1280,800)][InlineData(1150,600)][InlineData(800,500)][InlineData(420,320)]
    [Trait("Evidence","NativeFeedbackLayout")]
    public async Task CricketMatchActionsAlignAndBottomDockStaysInsideThePanel(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(600);string before=State(match);
        var root=Named<Grid>(panel,"CricketRoot");Assert.NotNull(root.Background);
        var outer=Named<ScrollViewer>(panel,"ConsoleViewport");Assert.Equal(ScrollBarVisibility.Disabled,outer.VerticalScrollBarVisibility);
        var actions=Named<UniformGrid>(panel,"MatchActionBar");Assert.Equal(width<760?2:4,actions.Columns);
        var reset=Named<Button>(panel,"ResetMatchButton");Assert.Contains(actions,reset.GetVisualAncestors());
        Assert.Equal(4,actions.Children.OfType<Button>().Count());
        var buttons=actions.Children.OfType<Button>().ToArray();
        Assert.All(buttons,x=>Assert.InRange(Math.Abs(x.Bounds.Width-buttons[0].Bounds.Width),0,1));
        await Reveal(window,reset);
        var resetBox=Painted(window,reset);
        Record("feedback-reset-"+width,new{resetBox,root.Bounds,window.ClientSize,probes=DockEdgePoints(resetBox).Select(point=>new{point,owned=Within(reset,window.InputHitTest(point)),type=window.InputHitTest(point)?.GetType().Name,name=(window.InputHitTest(point)as Control)?.Name})});
        PromptCapture(window,$"cricket-after-{width}-{height}");
        await StableReachable(window,reset);
        foreach(var tab in Named<TabControl>(panel,"MainTabControl").GetVisualDescendants().OfType<TabItem>().Where(x=>x.IsEffectivelyVisible))
        {
            var box=Painted(window,tab);Assert.True(box.Top>=0 && box.Bottom<=window.ClientSize.Height+1,$"{tab.Name}: {box}");
        }
        if(width>=1150)
        {
            var columns=Named<Grid>(panel,"ScoringColumns");Assert.True(double.IsNaN(columns.Height));
            Assert.True(columns.Bounds.Height<panel.Bounds.Height-100,"Fresh match must not stretch its empty log through the entire console.");
            Assert.True(Named<Border>(panel,"BowlingZone").Bounds.Height<=Named<Border>(panel,"BattingZone").Bounds.Height);
        }
        PromptCapture(window,$"cricket-after-{width}-{height}");Assert.Equal(before,State(match));
    }
}
