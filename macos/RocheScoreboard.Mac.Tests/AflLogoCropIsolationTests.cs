// OUTSIDE DRAFT ONLY — UNCOMPILED/UNRUN. Two theory rows, no product edits.
// Reuses current safe StorageScope and awaited public UI helpers from the
// existing partial BreakRotationModeIsolationTests class. No private app
// business method or mutable app field is invoked/seeded by this fixture.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private sealed record LogoCropSnapshot(string? Path,double Zoom,double X,double Y,
        double SliderZoom,double SliderX,double SliderY,double PreviewZoomX,double PreviewZoomY,
        double PreviewX,double PreviewY,bool PreviewHasSource);
    private sealed record LogoCropFixture(Roche_Scoreboard.MainWindow Main,MatchManager Match,
        Window Output,string HomePath,string AwayPath);

    private static void LogoCropCreateOwnedPng(string path,bool home)
    {
        // Generated fixture pixels only: no uploaded/user asset or network.
        // The Scope's own GUID home is proved before this call.
        var image=new Grid{Width=96,Height=64,Background=new SolidColorBrush(home?Colors.DarkRed:Colors.Navy)};
        image.Children.Add(new Border{Width=28,Height=40,Background=Brushes.White,
            HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(6)});
        image.Children.Add(new Border{Width=22,Height=22,Background=new SolidColorBrush(home?Colors.Gold:Colors.Cyan),
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(6)});
        image.Measure(new Size(96,64));image.Arrange(new Rect(0,0,96,64));
        using var bitmap=new RenderTargetBitmap(new PixelSize(96,64),new Vector(96,96));
        bitmap.Render(image);bitmap.Save(path);
        Assert.True(File.Exists(path));Assert.True(new FileInfo(path).Length>0);
    }
    private static async Task<LogoCropFixture> LogoCropOpenFromOwnedRecovery(StorageScope scope)
    {
        scope.AssertOwnedPaths();
        // Read only the TEST scope's private owned directory, not app state.
        string directory=Read<string>(scope,"privateHome");
        Assert.Equal(Path.GetFullPath(directory),Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
        string homePath=Path.Combine(directory,"home-crop-fixture.png"),awayPath=Path.Combine(directory,"away-crop-fixture.png");
        Assert.False(File.Exists(homePath));Assert.False(File.Exists(awayPath));
        LogoCropCreateOwnedPng(homePath,true);LogoCropCreateOwnedPng(awayPath,false);
        var seed=new MatchManager();seed.AddGoal(TeamSide.Home);seed.AddBehind(TeamSide.Away);
        Assert.False(seed.ClockRunning);Assert.Equal(2,seed.Events.Count);
        // Public persistence format/Save route. Initial crops intentionally
        // default; actual public sliders supply all non-default framing later.
        GameStateService.Save(new GameState{Sport="afl",MatchState=seed.ToState(),HomeLogoPath=homePath,
            AwayLogoPath=awayPath,WeatherLocation=""});
        var saved=GameStateService.Load();Assert.NotNull(saved);
        Assert.Equal(homePath,saved.HomeLogoPath);Assert.Equal(awayPath,saved.AwayLogoPath);
        scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};var main=scope.Main;main.Show();
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible,"Actual initial Home must display the private recovery slot.");
        home.PrepareForDisplay();
        var resume=Named<Button>(home,"HomeResumeButton");await RotatorInvoke(resume);
        var recovery=Named<Grid>(main,"RecoveryOverlay");
        await Until(()=>recovery.IsEffectivelyVisible&&recovery.Children.OfType<ResumeMatchPrompt>().Any(),"Home Resume opens actual recovery choice.");
        var prompt=Assert.Single(recovery.Children.OfType<ResumeMatchPrompt>());
        await RotatorInvoke(Named<Button>(prompt,"ResumeButton"));
        await Until(()=>Named<Control>(main,"MainContent").IsEffectivelyVisible&&!recovery.IsVisible&&!home.IsVisible,
            "Actual Continue Match restores AFL operator controls.");
        var match=Read<MatchManager>(main,"_match");var output=Read<Window>(main,"_displayWindow");
        Assert.False(match.ClockRunning);Assert.Equal(6,match.HomeTotal);Assert.Equal(1,match.AwayTotal);Assert.Equal(2,match.Events.Count);
        Assert.Equal(homePath,Read<string?>(main,"_homeLogoPath"));Assert.Equal(awayPath,Read<string?>(main,"_awayLogoPath"));
        Assert.False(output.IsVisible);Assert.True(Read<bool>(main,"_outputActivationRequired"));
        var tools=Named<Button>(main,"GuidedModeButton");
        if(Equals(tools.Content,"ADVANCED TOOLS"))await RotatorInvoke(tools);
        Assert.Equal("RETURN TO ESSENTIALS",tools.Content);
        var setup=Named<TabItem>(main,"MatchSetupTab");
        await Until(()=>setup.IsLoaded&&setup.IsEffectivelyVisible&&setup.IsEffectivelyEnabled,"Actual advanced Setup tab must be available.");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(setup)).Select();
        await Until(()=>setup.IsSelected&&main.GetVisualDescendants().OfType<SettingsWorkspace>().Any(value=>value.IsEffectivelyVisible),
            "Original Setup tab must mount its SettingsWorkspace.");
        scope.AssertOwnedPaths();return new(main,match,output,homePath,awayPath);
    }
    private static async Task LogoCropSearch(Roche_Scoreboard.MainWindow main,string query,string expectedSection)
    {
        RotationKey(main,Avalonia.Input.PhysicalKey.F,Avalonia.Input.RawInputModifiers.Control);
        var workspace=Assert.Single(main.GetVisualDescendants().OfType<SettingsWorkspace>(),value=>value.IsEffectivelyVisible);
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),value=>value.IsEffectivelyVisible);
        await Until(()=>search.IsFocused,"Original CtrlF must focus the real settings search.");
        RotationKey(main,Avalonia.Input.PhysicalKey.A,Avalonia.Input.RawInputModifiers.Control);
        main.KeyTextInput(query);await Pump(150);
        Assert.True(Named<Border>(main,expectedSection).IsEffectivelyVisible);
    }
    private static async Task LogoCropSetSlider(Roche_Scoreboard.MainWindow main,string name,double value)
    {
        var slider=Named<Slider>(main,name);slider.BringIntoView();
        await Until(()=>slider.IsLoaded&&slider.IsEffectivelyVisible&&slider.IsEffectivelyEnabled,"Original crop slider must actually be available.");
        var provider=Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider));
        Assert.False(provider.IsReadOnly);provider.SetValue(value);await Pump(30);
        Assert.True(Math.Abs(slider.Value-value)<.000001,name+" must accept the original public range value.");
    }
    private static async Task LogoCropSetBoth(LogoCropFixture f)
    {
        await LogoCropSearch(f.Main,"logo crop","SettingsSection_Media");
        foreach(var change in new[]{("HomeLogoZoomSlider",2.15),("HomeLogoXSlider",19d),("HomeLogoYSlider",-13d),
                                    ("AwayLogoZoomSlider",1.65),("AwayLogoXSlider",-24d),("AwayLogoYSlider",17d)})
            await LogoCropSetSlider(f.Main,change.Item1,change.Item2);
    }
    private static LogoCropSnapshot LogoCropRead(Roche_Scoreboard.MainWindow main,bool home)
    {
        string side=home?"Home":"Away",field=home?"_home":"_away";
        var preview=Named<Image>(main,side+"LogoCropPreviewImage");
        var transform=Assert.IsType<TransformGroup>(preview.RenderTransform);
        var scale=Assert.Single(transform.Children.OfType<ScaleTransform>());
        var shift=Assert.Single(transform.Children.OfType<TranslateTransform>());
        return new(Read<string?>(main,field+"LogoPath"),Read<double>(main,field+"LogoZoom"),
            Read<double>(main,field+"LogoOffsetX"),Read<double>(main,field+"LogoOffsetY"),
            Named<Slider>(main,side+"LogoZoomSlider").Value,Named<Slider>(main,side+"LogoXSlider").Value,
            Named<Slider>(main,side+"LogoYSlider").Value,scale.ScaleX,scale.ScaleY,shift.X,shift.Y,preview.Source is not null);
    }
    private static async Task<Button> LogoCropOriginalClearButton(Roche_Scoreboard.MainWindow main,bool home)
    {
        await LogoCropSearch(main,"teams","SettingsSection_Teams");
        // Exact original source row: Browse Logo, Clear ×, named status text.
        // No synthetic clear handler/event or replacement button is created.
        var status=Named<TextBlock>(main,home?"HomeLogoStatusText":"AwayLogoStatusText");
        var row=Assert.IsType<StackPanel>(status.Parent);
        var clear=Assert.Single(row.Children.OfType<Button>(),button=>Equals(button.Content,"✕"));
        clear.BringIntoView();await Until(()=>clear.IsLoaded&&clear.IsEffectivelyVisible&&clear.IsEffectivelyEnabled,
            "Original selected-team Clear button must be mounted and ready.");
        return clear;
    }
    private static void LogoCropAssertRequested(LogoCropSnapshot value,string path,double zoom,double x,double y)
    {
        Assert.Equal(path,value.Path);Assert.True(value.PreviewHasSource);
        foreach(double current in new[]{value.Zoom,value.SliderZoom,value.PreviewZoomX,value.PreviewZoomY})Assert.Equal(zoom,current,6);
        foreach(double current in new[]{value.X,value.SliderX,value.PreviewX})Assert.Equal(x,current,6);
        foreach(double current in new[]{value.Y,value.SliderY,value.PreviewY})Assert.Equal(y,current,6);
    }
    private static void LogoCropCapture(Window window,string name)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private capture directory required.");
        Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();
        Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
    }
    private static void LogoCropAssertDefault(LogoCropSnapshot value)
    {
        Assert.Null(value.Path);Assert.False(value.PreviewHasSource);
        foreach(double zoom in new[]{value.Zoom,value.SliderZoom,value.PreviewZoomX,value.PreviewZoomY})Assert.Equal(1d,zoom,6);
        foreach(double offset in new[]{value.X,value.Y,value.SliderX,value.SliderY,value.PreviewX,value.PreviewY})Assert.Equal(0d,offset,6);
    }
    [AvaloniaTheory][InlineData(true)][InlineData(false)]
    [Trait("Evidence","AflLogoClearOppositeCropIsolation")]
    public async Task ActualTeamLogoClearResetsOnlySelectedFramingAndPreservesOppositeOwnedLogo(bool clearHome)
    {
        await using var scope=new StorageScope(true,true);var f=await LogoCropOpenFromOwnedRecovery(scope);
        await LogoCropSetBoth(f);
        var homeBefore=LogoCropRead(f.Main,true);var awayBefore=LogoCropRead(f.Main,false);
        LogoCropAssertRequested(homeBefore,f.HomePath,2.15,19,-13);
        LogoCropAssertRequested(awayBefore,f.AwayPath,1.65,-24,17);
        Assert.NotEqual(homeBefore.Zoom,awayBefore.Zoom);Assert.NotEqual(homeBefore.X,awayBefore.X);Assert.NotEqual(homeBefore.Y,awayBefore.Y);
        LogoCropCapture(f.Main,"afl-logo-crop-before-"+(clearHome?"home":"away"));
        string before=JsonSerializer.Serialize(f.Match.ToState());int matchChanges=0;f.Match.MatchChanged+=()=>matchChanges++;
        string homeHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.HomePath)));
        string awayHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.AwayPath)));
        var clear=await LogoCropOriginalClearButton(f.Main,clearHome);
        await RotatorInvoke(clear);await Pump(100);
        await LogoCropSearch(f.Main,"logo crop","SettingsSection_Media");
        var selectedAfter=LogoCropRead(f.Main,clearHome);var oppositeAfter=LogoCropRead(f.Main,!clearHome);
        LogoCropCapture(f.Main,"afl-logo-crop-after-"+(clearHome?"home":"away"));
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private capture directory required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"afl-logo-clear-"+(clearHome?"home":"away")+".json"),
            JsonSerializer.Serialize(new{clearHome,homeBefore,awayBefore,selectedAfter,oppositeAfter,matchChanges,
                outputVisible=f.Output.IsVisible,activationRequired=Read<bool>(f.Main,"_outputActivationRequired")},new JsonSerializerOptions{WriteIndented=true}));
        // Baseline is expected to fail this exact opposite-side assertion:
        // the original Clear path calls the six-field global crop reset.
        Assert.Equal(clearHome?awayBefore:homeBefore,oppositeAfter);
        LogoCropAssertDefault(selectedAfter);
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,matchChanges);
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));
        Assert.Equal(homeHash,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.HomePath))));
        Assert.Equal(awayHash,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.AwayPath))));
        // Repeating Clear on the now-empty selected path must not erase the
        // opposite framing or enable output; same original UI action only.
        clear=await LogoCropOriginalClearButton(f.Main,clearHome);
        await RotatorInvoke(clear);await Pump(100);
        Assert.Equal(oppositeAfter,LogoCropRead(f.Main,!clearHome));LogoCropAssertDefault(LogoCropRead(f.Main,clearHome));
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(0,matchChanges);
        Assert.False(f.Output.IsVisible);Assert.True(Read<bool>(f.Main,"_outputActivationRequired"));scope.AssertOwnedPaths();
    }
}
