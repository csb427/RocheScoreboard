using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class SmallSurfaceTests
{
    private static void Capture(Window window,string name)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-mac-captures";Directory.CreateDirectory(directory);
        using var image=window.CaptureRenderedFrame();Assert.NotNull(image);image.Save(Path.Combine(directory,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData("afl-setup",700,450)][InlineData("cricket-setup",700,450)][InlineData("cricket-console",700,450)]
    [InlineData("delivery",700,450)][InlineData("settings",700,450)][InlineData("resume",700,450)]
    [InlineData("afl-setup",480,320)][InlineData("cricket-console",480,320)]
    [InlineData("settings",480,320)][InlineData("resume",420,280)]
    [InlineData("delivery",420,280)]
    [InlineData("training-console",700,450)][InlineData("training-console",420,280)]
    public async Task CurrentProductionSurfacesRenderAndKeepTheirActionsReachableOnShortWindows(string surface,int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(ready:true);match.SetTeams(new string('A',90),"AAA",new string('B',90),"BBB");
        Control content=surface switch
        {
            "afl-setup"=>new SetupWizard(),"cricket-setup"=>new CricketSetupWizard(),
            "cricket-console"=>new CricketControlPanel(),"delivery"=>new CricketDeliveryDialog(match),
            "training-console"=>new TrainingControlPanel(),
            "settings"=>new GlobalSettingsScreen(),"resume"=>new ResumeMatchPrompt(new GameState()),
            _=>throw new ArgumentOutOfRangeException(nameof(surface))
        };
        var window=new NativeWindow{Width=width,Height=height,Content=content,Title="Small "+surface};
        if(content is CricketControlPanel panel)panel.SetMatch(match);
        if(content is TrainingControlPanel training)
        {
            var session = new TrainingSession(); session.AddTimer(new TrainingTimer(new string('T',90),TimeSpan.FromMinutes(2)));
            training.Bind(session);
        }
        try
        {
            window.Show();await Task.Delay(1000);Dispatcher.UIThread.RunJobs();
            Assert.InRange(window.Bounds.Width,1,width);Assert.InRange(window.Bounds.Height,1,height);
            Capture(window,"small-"+surface+(width==700?"":$"-{width}x{height}"));
            if(surface=="resume")
            {
                Assert.True(content.FindControl<TextBlock>("PromptHeading")!.Bounds.Height>15);
                Assert.True(content.FindControl<TextBlock>("HomeNameText")!.Bounds.Height>15);
            }
            var actions=content.GetVisualDescendants().OfType<Button>().Where(x=>x.IsEffectivelyVisible&&x.IsEffectivelyEnabled&&x.Bounds.Width>0&&x.Bounds.Height>0).ToArray();
            Assert.NotEmpty(actions);
            foreach(var action in actions.TakeLast(8))
            {
                action.BringIntoView();Dispatcher.UIThread.RunJobs();
                // Use an actual Tab input: source viewports deliberately suppress
                // pointer-triggered/programmatic scrolling to avoid moving click targets.
                window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);
                if(action.Focusable)Assert.True(action.Focus(NavigationMethod.Tab));
                action.BringIntoView();Dispatcher.UIThread.RunJobs();
                window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);
                var location=action.TranslatePoint(default,window);Assert.NotNull(location);
                Assert.True(new Rect(window.Bounds.Size).Intersects(new Rect(location.Value,action.Bounds.Size)),surface+": "+action.Name+" / "+action.Content+" at "+location+" size "+action.Bounds.Size+" ancestors "+string.Join("/",action.GetVisualAncestors().Select(parent=>parent.GetType().Name))+" must be reachable after scrolling");
            }
            if(content is CricketDeliveryDialog delivery)
            {bool? completed=null;delivery.Completed+=result=>completed=result;delivery.Cancel();Assert.False(completed);}
        }
        finally{if(content is CricketControlPanel controls)controls.ClearMatch();window.Close();}
    }
    [AvaloniaFact]
    public async Task SearchRetainsEditingClearButtonAndEscapeWithoutLeavingTheSheet()
    {
        var launcher=new SportSelectionScreen();var window=new NativeWindow{Width=800,Height=500,Content=launcher};
        window.Show();
        try
        {
            await Task.Delay(500);Dispatcher.UIThread.RunJobs();
            launcher.FindControl<Button>("ChangelogToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(400);Dispatcher.UIThread.RunJobs();
            var search=launcher.FindControl<OperatorSearchBox>("ChangelogSearchBox")!;
            search.Text="cricket";search.Focus();Dispatcher.UIThread.RunJobs();Assert.True(search.HasQuery);
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();Assert.Equal("",search.Text);Assert.False(search.HasQuery);
            Assert.True(launcher.FindControl<Grid>("ChangelogOverlay")!.IsVisible);
        }
        finally{window.Close();}
    }
}
