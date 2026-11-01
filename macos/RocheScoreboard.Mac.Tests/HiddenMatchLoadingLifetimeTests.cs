using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using RocheScoreboard.Mac.Production;
using Xunit;
namespace RocheScoreboard.Mac.Tests;

// Actual product startup: never invokes the dormant loader API or synthesizes Loaded.
public sealed class HiddenMatchLoadingLifetimeTests
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static async Task Settle(int milliseconds=150){await Task.Delay(milliseconds);Dispatcher.UIThread.RunJobs();}
    private static T Named<T>(Control owner,string name) where T:Control => Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));
    private static async Task Until(Func<bool> ready,string message){for(int i=0;i<50&&!ready();i++)await Settle(100);Assert.True(ready(),message);}
    private static async Task Tap(Window window,Control target)
    {
        target.BringIntoView();await Settle();Assert.True(target.IsEffectivelyVisible);Assert.True(target.IsEnabled);
        var point=target.TranslatePoint(new Point(target.Bounds.Width/2,target.Bounds.Height/2),window);Assert.NotNull(point);
        var hit=window.InputHitTest(point.Value);
        Assert.True(ReferenceEquals(hit,target)||hit is Visual child&&child.GetVisualAncestors().Contains(target),target.Name+" pointer must reach its template.");
        window.MouseMove(point.Value);window.MouseDown(point.Value,MouseButton.Left);window.MouseUp(point.Value,MouseButton.Left);await Settle(250);
    }
    private static string EvidenceDirectory=>Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-hidden-loader-evidence");
    private static void Capture(Window owner,string name){Directory.CreateDirectory(EvidenceDirectory);using var frame=owner.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(EvidenceDirectory,name+".png"));}
    private static AvaloniaObject[] NamedTargets(MatchLoadingScreen loader)=>new[]{"OuterOrbitRotate","InnerOrbitRotate","VsChipScale","BrandPulse","HomeRingGlow","AwayRingGlow","ShineTranslate"}.Select(name=>ProductionNames.RegistryFor(loader).TryGetValue(name,out var value)?value:Assert.IsAssignableFrom<AvaloniaObject>(loader.FindControl<Control>(name))).ToArray();
    private static Control[] DynamicTargets(MatchLoadingScreen loader)=>Named<Canvas>(loader,"BeamCanvas").Children.Concat(Named<Canvas>(loader,"ParticleCanvas").Children).ToArray();
    private static string MatchState(HomeFixture fixture)=>JsonSerializer.Serialize(fixture.Match.ToState());
    private static byte[]? Recovery()=>File.Exists(GameStateService.RecoveryStatePath)?File.ReadAllBytes(GameStateService.RecoveryStatePath):null;
    private static void OutputOff(Window owner)
    {
        foreach(string name in new[]{"_displayWindow","_cricketDisplayWindow","_trainingDisplayWindow"})
            Assert.False((owner.GetType().GetField(name,HiddenInstance)?.GetValue(owner) as Window)?.IsVisible??false);
    }
    private static void NoAmbient(MatchLoadingScreen loader)
    {
        Assert.All(NamedTargets(loader),target=>Assert.False(Motion.IsAnimating(target),"Hidden loader owns an ambient named clock: "+target.GetType().Name));
        Assert.Empty(DynamicTargets(loader));
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCollapsedMainWindowStartupKeepsOwnedAmbientIdle(bool reduced)
    {
        await using var fixture=await HomeFixture.Create(reduced);
        var window=fixture.Window;var loader=Named<MatchLoadingScreen>(window,"MatchLoadingPanel");
        int loaded=0,unloaded=0,finished=0,added=0;var removed=new List<Control>();
        loader.Loaded+=(_,_)=>loaded++;loader.Unloaded+=(_,_)=>unloaded++;loader.LoadingFinished+=()=>finished++;
        foreach(var canvas in new[]{Named<Canvas>(loader,"BeamCanvas"),Named<Canvas>(loader,"ParticleCanvas")})
            canvas.Children.CollectionChanged+=(_,e)=>{added+=e.NewItems?.Count??0;if(e.OldItems is not null)removed.AddRange(e.OldItems.Cast<Control>());};
        string state=MatchState(fixture);byte[]? recovery=Recovery();
        window.Width=1280;window.Height=850;window.Show();
        var home=Named<SportSelectionScreen>(window,"SportSelectionPanel");
        await Until(()=>home.IsEffectivelyVisible&&home.GetVisualDescendants().OfType<Button>().Any(x=>x.Name=="AflLaunchAction"&&x.Bounds.Width>0),"Actual Home and Start Game must mount.");
        await Settle(350);
        object Snapshot(string phase)=>new{phase,reduced,loaded,unloaded,finished,loader.IsLoaded,loader.IsVisible,loader.IsEffectivelyVisible,
            visualRoot=loader.GetVisualRoot()?.GetType().Name,parent=loader.GetVisualParent()?.GetType().Name,window.Bounds,
            named=NamedTargets(loader).Select(x=>new{type=x.GetType().Name,animating=Motion.IsAnimating(x)}).ToArray(),
            dynamic=DynamicTargets(loader).Length,dynamicAnimating=DynamicTargets(loader).Count(Motion.IsAnimating),added,removed=removed.Count,
            removedAnimating=removed.Count(Motion.IsAnimating),motion=OperatorMotion.Enabled,state=MatchState(fixture),recovery=Recovery()};
        Directory.CreateDirectory(EvidenceDirectory);var snapshots=new List<object>{Snapshot("startup")};
        Capture(window,"hidden-home-"+reduced);
        await Tap(window,Assert.Single(home.GetVisualDescendants().OfType<Button>(),x=>x.Name=="AflLaunchAction"));
        var wizard=Named<SetupWizard>(window,"SetupWizardPanel");
        await Until(()=>Named<Control>(wizard,"SportStepContainer").IsEffectivelyVisible,"Start Game must open unified sport step.");
        Capture(window,"hidden-sport-step-"+reduced);
        await Tap(window,Named<Button>(wizard,"CancelSetupButton"));
        await Until(()=>home.IsEffectivelyVisible&&!wizard.IsEffectivelyVisible,"Cancel must return Home.");
        snapshots.Add(Snapshot("home-after-cancel"));
        File.WriteAllText(Path.Combine(EvidenceDirectory,"actual-hidden-"+reduced+".json"),JsonSerializer.Serialize(snapshots,new JsonSerializerOptions{WriteIndented=true}));
        Assert.False(loader.IsVisible);Assert.False(loader.IsEffectivelyVisible);Assert.Equal(0,finished);
        Assert.Equal(state,MatchState(fixture));Assert.Equal(recovery,Recovery());OutputOff(window);
        // IsLoaded is recorded even if this platform supplies no Loaded witness.
        // A baseline with no owned work stays a positive result.
        NoAmbient(loader);
        window.Hide();await Settle();NoAmbient(loader);window.Show();await Settle();NoAmbient(loader);
    }
    // Explicit component harness, separate from the actual collapsed product route.
    // No progress/completion API activation is needed for ambient lifecycle ownership.
    [AvaloniaTheory]
    [InlineData("reduce")]
    [InlineData("local-hide")]
    [InlineData("ancestor-hide")]
    [InlineData("ancestor-disable")]
    [InlineData("disable")]
    [InlineData("owner-hide")]
    [InlineData("detach")]
    public async Task VisibleComponentRetiresAndRestoresOwnedAmbient(string action)
    {
        await using var guard=await HomeFixture.Create(false,false);
        var loader=new MatchLoadingScreen();var host=new Grid();host.Children.Add(loader);
        var owner=new Window{Width=1280,Height=850,Content=host};var removed=new List<Control>();
        foreach(var canvas in new[]{Named<Canvas>(loader,"BeamCanvas"),Named<Canvas>(loader,"ParticleCanvas")})
            canvas.Children.CollectionChanged+=(_,e)=>{if(e.OldItems is not null)removed.AddRange(e.OldItems.Cast<Control>());};
        try
        {
            owner.Show();await Until(()=>loader.IsLoaded&&loader.Bounds.Width>0,"Component must naturally load visibly.");await Settle();
            Assert.True(OperatorMotion.Enabled);Assert.All(NamedTargets(loader),x=>Assert.True(Motion.IsAnimating(x)));
            int activeBefore=DynamicTargets(loader).Count(Motion.IsAnimating);
            if(action=="reduce")OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            if(action=="local-hide")loader.IsVisible=false;
            if(action=="ancestor-hide")host.IsVisible=false;
            if(action=="ancestor-disable")host.IsEnabled=false;
            if(action=="disable")loader.IsEnabled=false;
            if(action=="owner-hide")owner.Hide();
            if(action=="detach")owner.Content=null;
            await Settle(250);
            Capture(owner,"component-"+action);
            File.WriteAllText(Path.Combine(EvidenceDirectory,"component-"+action+".json"),JsonSerializer.Serialize(new{action,loader.IsLoaded,loader.IsVisible,loader.IsEffectivelyVisible,activeBefore,
                namedAnimating=NamedTargets(loader).Count(Motion.IsAnimating),dynamic=DynamicTargets(loader).Length,removed=removed.Count,removedAnimating=removed.Count(Motion.IsAnimating)},new JsonSerializerOptions{WriteIndented=true}));
            NoAmbient(loader);Assert.All(removed,x=>Assert.False(Motion.IsAnimating(x),"Removed dynamic node still has a clock."));
            if(action=="reduce")OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            if(action=="local-hide")loader.IsVisible=true;
            if(action=="ancestor-hide")host.IsVisible=true;
            if(action=="ancestor-disable")host.IsEnabled=true;
            if(action=="disable")loader.IsEnabled=true;
            if(action=="owner-hide")owner.Show();
            if(action=="detach")owner.Content=host;
            await Settle(250);
            Assert.True(loader.IsLoaded);Assert.True(loader.IsEffectivelyVisible);
            Assert.All(NamedTargets(loader),x=>Assert.True(Motion.IsAnimating(x)));Assert.Equal(42,DynamicTargets(loader).Length);
            owner.Close();await Settle();NoAmbient(loader);Assert.All(removed,x=>Assert.False(Motion.IsAnimating(x)));
        }
        finally { owner.Close();await Settle(); }
    }
    [AvaloniaFact]
    public async Task VisibleComponentResizeStopsOldDynamicClocksBeforeRemoval()
    {
        await using var guard=await HomeFixture.Create(false,false);
        var loader=new MatchLoadingScreen();var owner=new Window{Width=1280,Height=850,Content=loader};
        var removed=new List<Control>();var animatedAtRemoval=new List<bool>();
        foreach(var canvas in new[]{Named<Canvas>(loader,"BeamCanvas"),Named<Canvas>(loader,"ParticleCanvas")})
            canvas.Children.CollectionChanged+=(_,e)=>{if(e.OldItems is not null)foreach(Control node in e.OldItems){removed.Add(node);animatedAtRemoval.Add(Motion.IsAnimating(node));}};
        try
        {
            owner.Show();await Until(()=>loader.IsLoaded&&loader.Bounds.Width>0,"Component must naturally load.");await Settle(200);
            Assert.NotEmpty(DynamicTargets(loader));owner.Width=1100;owner.Height=760;await Settle(300);
            Capture(owner,"component-resized");
            File.WriteAllText(Path.Combine(EvidenceDirectory,"component-resize.json"),JsonSerializer.Serialize(new{removed=removed.Count,animatedAtRemoval,removedAnimating=removed.Count(Motion.IsAnimating),currentDynamic=DynamicTargets(loader).Length},new JsonSerializerOptions{WriteIndented=true}));
            Assert.NotEmpty(removed);Assert.All(animatedAtRemoval,x=>Assert.False(x,"Dynamic animation must stop before the collection removes its target."));
            Assert.All(removed,x=>Assert.False(Motion.IsAnimating(x)));Assert.Equal(42,DynamicTargets(loader).Length);
            owner.Close();await Settle();Assert.All(removed,x=>Assert.False(Motion.IsAnimating(x)));NoAmbient(loader);
        }
        finally{owner.Close();await Settle();}
    }
    [AvaloniaFact]
    public async Task ReducedMotionRetiresStepAndProgressDecorationWithoutCompletingWorkEarly()
    {
        await using var guard=await HomeFixture.Create(false,false);
        var loader=new MatchLoadingScreen();var owner=new Window{Width=1280,Height=850,Content=loader};
        int finished=0;loader.LoadingFinished+=()=>finished++;
        try
        {
            owner.Show();await Until(()=>loader.IsLoaded,"Component must naturally load.");
            loader.StartLoading(new[]{"Read teams","Prepare match"},TimeSpan.FromMilliseconds(700));
            await Settle(100);
            var dots=Named<StackPanel>(loader,"StepStack").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().ToArray();
            Assert.Equal(2,dots.Length);Assert.Contains(dots,Motion.IsAnimating);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await Settle(40);
            Assert.Equal(0,finished);NoAmbient(loader);
            Assert.All(dots,dot=>Assert.False(Motion.IsAnimating(dot)));
            Assert.False(Motion.IsAnimating(Named<Border>(loader,"ProgressFill")));
            await Until(()=>finished==1,"Logical progress must still finish with reduced motion.");
            Assert.Equal("100%",Named<TextBlock>(loader,"PercentText").Text);
            Assert.Equal("READY",Named<TextBlock>(loader,"StatusText").Text);
            await Settle(250);Assert.Equal(1,finished);NoAmbient(loader);
        }
        finally { owner.Close();await Settle(); }
    }

    // Dormant public API compatibility harness, not an active setup route.
    [AvaloniaTheory]
    [InlineData("empty-replace")]
    [InlineData("finish-replace")]
    [InlineData("finish-unload")]
    public async Task RetiredRunCannotCompleteAReplacementOrDetachedOwner(string action)
    {
        await using var guard=await HomeFixture.Create(true,false);
        var loader=new MatchLoadingScreen();var owner=new Window{Width=1280,Height=850,Content=loader};
        int finished=0;loader.LoadingFinished+=()=>finished++;
        try
        {
            owner.Show();await Until(()=>loader.IsLoaded,"Component must naturally load.");
            loader.StartLoading(action=="empty-replace"?Array.Empty<string>():new[]{"Old work"},TimeSpan.FromMilliseconds(action=="empty-replace"?150:2000));
            if(action!="empty-replace")loader.Finish();
            if(action=="finish-unload")owner.Content=null;
            else loader.StartLoading(new[]{"Replacement work"},TimeSpan.FromSeconds(2));
            await Settle(350);
            Assert.Equal(0,finished);
            if(action!="finish-unload")
            {
                Assert.NotEqual("READY",Named<TextBlock>(loader,"StatusText").Text);
                loader.Finish();await Until(()=>finished==1,"Current run must retain exactly-once completion.");
                loader.Finish();await Settle(250);Assert.Equal(1,finished);
            }
        }
        finally { owner.Close();await Settle(); }
    }

    private sealed class HomeFixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousAnimation = OperatorMotion.AnimationOverride;
        private readonly bool previousGlass = OperatorAppearance.GlassEnabled;
        private readonly bool previousReduce = OperatorAppearance.ReduceMotion;
        private readonly string? previousQuarterOverride = QuarterLabelService.CurrentOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-live-settings-search-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> files = new(StringComparer.Ordinal);
        private string? documents;
        private Roche_Scoreboard.MainWindow? window;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal MatchManager Match => Assert.IsType<MatchManager>(Window.GetType().GetField("_match", HiddenInstance)!.GetValue(Window));

        private void Preserve(string path)
        {
            foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
                if (!files.ContainsKey(candidate)) files[candidate] = File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;
        }

        internal static async Task<HomeFixture> Create(bool reduced, bool constructWindow=true)
        {
            var fixture = new HomeFixture();
            try
            {
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                fixture.documents = SelfTestStorage.DocumentsDirectory;
                Directory.CreateDirectory(fixture.documents);
                // SelfTestStorage may be process-latched rather than use the
                // new environment directory. Preserve its actual files too.
                foreach (string file in Directory.EnumerateFiles(fixture.documents, "*", SearchOption.AllDirectories))
                    fixture.files[file] = File.ReadAllBytes(file);
                // Resolve conditional storage paths in the SAME mode used by
                // the actual constructor and interaction. The native App does
                // not initialize the global storage latch for this fixture.
                fixture.app.IsSelfTestMode = true;
                foreach (string path in new[]
                {
                    GameStateService.RecoveryStatePath,
                    (string)typeof(GameStateService).GetProperty("LegacyStateFilePath", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!,
                    UxPreferences.GetStoragePath(),
                    (string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false])!,
                    (string)typeof(AflKeybindSettings).GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!
                }) fixture.Preserve(path);
                foreach (string path in fixture.files.Keys) if (File.Exists(path)) File.Delete(path);

                OperatorMotion.AnimationOverride = null;
                OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, reduced);
                if(constructWindow) fixture.window = new Roche_Scoreboard.MainWindow();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (window is not null)
                {
                    window.Close();
                    // This existing static subscription lacks production
                    // teardown. Detach only this fixture window's own handler;
                    // do not replace or clear anyone else's event callbacks.
                    var handler = window.GetType().GetMethod("OnQuarterLabelServiceChanged", HiddenInstance);
                    if (handler is not null) QuarterLabelService.LabelsChanged -= handler.CreateDelegate<Action>(window);
                    await Settle(180);
                }
                OperatorAppearance.Configure(previousGlass, previousReduce);
                app.ThemeService.SetTheme(previousTheme);
                app.ThemeService.SetDensity(previousDensity);
                QuarterLabelService.SetCurrentOverride(previousQuarterOverride);
                if (documents is not null && Directory.Exists(documents))
                    foreach (string path in Directory.EnumerateFiles(documents, "*", SearchOption.AllDirectories))
                        if (!files.ContainsKey(path)) File.Delete(path);
                foreach (var (path, bytes) in files)
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }
            finally
            {
                OperatorMotion.AnimationOverride = previousAnimation;
                app.IsSelfTestMode = previousSelfTest;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome);
                if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true);
            }
        }
    }
}
