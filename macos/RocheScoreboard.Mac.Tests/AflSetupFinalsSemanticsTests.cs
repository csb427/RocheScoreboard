using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Original Windows-derived setup route; all actions use physical pointer/keys or
// the public Toggle provider. Reflection below observes state and storage only.
// Linux headless evidence cannot certify native Windows UIA/touch/compositor.
[Collection("Club preset persistence draft")]
public sealed partial class AflSetupFinalsSemanticsTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Read<T>(object owner,string name) => (T)owner.GetType().GetField(name,Hidden)!.GetValue(owner)!;
    private static T Named<T>(Control owner,string name) where T:Control => Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static async Task Until(Func<bool> state,string reason)
    {
        for(int i=0;i<50&&!state();i++)await Settle();
        Assert.True(state(),reason);
    }
    private static bool Within(Control owner,object? hit)=>ReferenceEquals(owner,hit)||hit is Visual child&&child.GetVisualAncestors().Contains(owner);
    private static Rect Painted(Window window,Control target)
    {
        // Ancestor entry/layout transforms scale the painted region. A mapped
        // origin combined with raw Bounds.Size is not its actual input box.
        var corners=new[]{new Point(),new Point(target.Bounds.Width,0),new Point(0,target.Bounds.Height),new Point(target.Bounds.Width,target.Bounds.Height)}
            .Select(point=>target.TranslatePoint(point,window)).ToArray();
        Assert.All(corners,point=>Assert.NotNull(point));
        double left=corners.Min(point=>point!.Value.X),top=corners.Min(point=>point!.Value.Y);
        return new Rect(left,top,corners.Max(point=>point!.Value.X)-left,corners.Max(point=>point!.Value.Y)-top);
    }
    private static Point[] Probes(Rect b)=>new[]{b.Center,new Point(b.Left+4,b.Center.Y),new Point(b.Right-4,b.Center.Y),new Point(b.Center.X,b.Top+4),new Point(b.Center.X,b.Bottom-4)};
    private static void Key(Window window,PhysicalKey key)
    {window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();}
    private static async Task Reveal(Window window,Control target)
    {
        Assert.True(target.IsEffectivelyVisible,target.Name+" must be visible in the actual mounted route.");
        target.BringIntoView();await Settle();
        var scroll=target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        var presenter=scroll?.GetVisualDescendants().OfType<ScrollContentPresenter>()
            .FirstOrDefault(item=>ReferenceEquals(item.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(),scroll));
        Rect? previousTarget=null,previousViewport=null;int wheelSteps=0;double previousDirection=0,wheelMagnitude=1;
        bool Ready(Rect box)=>target.Bounds.Width>=20&&target.Bounds.Height>=20&&new Rect(window.ClientSize).Contains(box)
            &&Probes(box).All(point=>Within(target,window.InputHitTest(point)));
        bool OwnsWheel(Point point)
        {
            if(scroll is null)return false;
            var hit=window.InputHitTest(point);
            if(!Within(scroll,hit))return false;
            return hit is Visual visual&&ReferenceEquals(visual.GetVisualAncestors().Prepend(visual).OfType<ScrollViewer>().FirstOrDefault(),scroll);
        }
        Point? WheelPoint(Rect viewport)
        {
            if(scroll is null||viewport.Width<=8||viewport.Height<=8)return null;
            // Validate the visible viewport/window intersection against real
            // input hit testing. Entry chrome or clipping may cover its center.
            foreach(double y in new[]{.5,.25,.75,.1,.9})
                foreach(double x in new[]{.03,.97,.5,.25,.75,.1,.9})
                {
                    var point=new Point(viewport.Left+viewport.Width*x,viewport.Top+viewport.Height*y);
                    if(OwnsWheel(point))return point;
                }
            return null;
        }
        // A finite entry animation may still be changing ancestor scale or
        // covering the wheel region. Render and wait for actual geometry;
        // never substitute forced focus, a stopped clock or disabled motion.
        for(int attempt=0;attempt<50;attempt++)
        {
            using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
            var box=Painted(window,target);if(Ready(box))return;
            var viewport=scroll is null?default:Painted(window,(Control?)presenter??scroll).Intersect(new Rect(window.ClientSize));
            bool stable=previousTarget==box&&previousViewport==viewport;
            previousTarget=box;previousViewport=viewport;
            var wheel=WheelPoint(viewport);
            if(stable&&wheel is Point point&&wheelSteps<30)
            {
                // Keep the original top/bottom ownership tests and all five
                // target probes. Scroll only through this mounted viewport.
                Assert.True(OwnsWheel(point),"Physical wheel must reach its actual nearest mounted setup viewport.");
                var probes=Probes(box);bool top=Within(target,window.InputHitTest(probes[3])),bottom=Within(target,window.InputHitTest(probes[4]));
                double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<point.Y?1:-1;
                // Preserve all five hit probes; refine physical wheel steps after overshoot.
                if(previousDirection!=0&&previousDirection!=direction)wheelMagnitude=Math.Max(.0625,wheelMagnitude/2);
                previousDirection=direction;
                window.MouseWheel(point,new Vector(0,direction*wheelMagnitude),RawInputModifiers.None);wheelSteps++;
                previousTarget=null;previousViewport=null;
            }
            await Settle(80);
        }
        using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
        Assert.True(Ready(Painted(window,target)),target.Name+" must own five probes and fit the current window after bounded real-frame readiness and physical scrolling.");
    }
    private static async Task Tap(Window window,Control target)
    {
        await Reveal(window,target);var p=Painted(window,target).Center;
        window.MouseMove(p);window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);await Settle(180);
    }
    private static async Task TabTo(Window window,Control target)
    {
        // No Focus(), forced focus scope or private handler substitutes for Tab.
        for(int i=0;i<100&&!Within(target,window.FocusManager?.GetFocusedElement());i++){Key(window,PhysicalKey.Tab);await Settle(15);}
        Assert.True(Within(target,window.FocusManager?.GetFocusedElement()),"Original setup checkbox must be reachable by sequential physical Tab.");
        await Reveal(window,target);
    }
    private static void Capture(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
    }

    private sealed class Fixture:IAsyncDisposable
    {
        // A failed close must not redirect callbacks into restored operator state.
        // Retain that fixture and stop later cases from borrowing its environment.
        private static readonly List<Fixture> RetainedUnclosed=[];
        private readonly App app=Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest=((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme=((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity previousDensity=((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly bool? previousAnimation=OperatorMotion.AnimationOverride;
        private readonly bool previousGlass=OperatorAppearance.GlassEnabled,previousReduce=OperatorAppearance.ReduceMotion;
        private readonly string? previousQuarter=QuarterLabelService.CurrentOverride;
        private readonly string? previousData=Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string data=Path.Combine(Path.GetTempPath(),"roche-setup-finals-"+Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string,byte[]> files=new(StringComparer.Ordinal);
        private readonly HashSet<string> directories=new(StringComparer.Ordinal);
        private readonly Dictionary<Window,bool> closedWindows=[];
        private readonly HashSet<Window> closeRequested=[];
        private bool storageCaptured,fixtureMutated,disposed;
        private Roche_Scoreboard.MainWindow? window;
        internal Roche_Scoreboard.MainWindow Window=>window!;
        internal SetupWizard Wizard=>Named<SetupWizard>(Window,"SetupWizardPanel");
        internal SportSelectionScreen Home=>Named<SportSelectionScreen>(Window,"SportSelectionPanel");
        internal CheckBox Toggle=>Named<CheckBox>(Wizard,"SetupFinalsMode");
        internal MatchManager Match=>Read<MatchManager>(Window,"_match");
        private void TrackWindow(Window owner)
        {
            if(closedWindows.TryAdd(owner,false))owner.Closed+=(_,_)=>closedWindows[owner]=true;
            foreach(var owned in owner.OwnedWindows.ToArray())TrackWindow(owned);
        }
        internal static async Task Run(int width,int height,bool reduced,bool light,Func<Fixture,Task> body)
        {
            Fixture? fixture=null;Exception? failure=null;
            try{fixture=await Create(width,height,reduced,light);await body(fixture);}
            catch(Exception error){failure=error;}
            if(fixture is not null)
            {
                try{await fixture.DisposeAsync();}
                catch(Exception cleanup)
                {
                    if(failure is null)failure=cleanup;
                    else failure.Data["AflSetupFinalsCleanupFailure"]=cleanup;
                }
            }
            if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private static async Task<Fixture> Create(int width,int height,bool reduced,bool light)
        {
            if(RetainedUnclosed.Count>0)throw new InvalidOperationException("An earlier Finals fixture has an unproven Window.Closed boundary; its isolated environment is retained. Stop this test process before starting another fixture.");
            var f=new Fixture();
            try
            {
                SelfTestStorage.Initialize(true);Assert.True(SelfTestStorage.IsEnabled);
                // Snapshot the entire process-latched root before creating or
                // deleting anything; ROCHE_DATA_HOME alone does not redirect it.
                if(Directory.Exists(SelfTestStorage.RootPath))
                {
                    foreach(string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories).Append(SelfTestStorage.RootPath))f.directories.Add(directory);
                    foreach(string path in Directory.EnumerateFiles(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories))f.files.Add(path,File.ReadAllBytes(path));
                }
                f.storageCaptured=true;f.fixtureMutated=true;
                f.app.IsSelfTestMode=true;Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",f.data);Directory.CreateDirectory(f.data);
                foreach(string path in f.files.Keys)File.Delete(path);
                OperatorMotion.AnimationOverride=!reduced;OperatorAppearance.Configure(true,reduced);f.app.ThemeService.SetTheme(light?AppTheme.Light:AppTheme.Dark);
                f.window=new();f.TrackWindow(f.window);f.Window.Show();
                await Until(()=>f.Home.IsEffectivelyVisible&&f.Home.GetVisualDescendants().OfType<Button>().Any(b=>b.Name=="AflLaunchAction"&&b.Bounds.Width>0),"Actual Home launch must be mounted.");
                // Loaded may restore remembered bounds. Apply the requested
                // geometry afterward and require its actual measured client size.
                f.Window.Width=width;f.Window.Height=height;await Settle();
                await Until(()=>f.Window.ClientSize==new Size(width,height),"Loaded window must measure the exact requested client size.");
                Assert.Equal(new Size(width,height),f.Window.ClientSize);
                await f.Start();Assert.Equal(new Size(width,height),f.Window.ClientSize);return f;
            }
            catch(Exception setup)
            {
                try{await f.DisposeAsync();}
                catch(Exception cleanup){setup.Data["AflSetupFinalsSetupCleanupFailure"]=cleanup;}
                throw;
            }
        }
        internal async Task Start()
        {
            await Tap(Window,Assert.Single(Home.GetVisualDescendants().OfType<Button>(),b=>b.Name=="AflLaunchAction"));
            await Until(()=>Named<Control>(Wizard,"SportStepContainer").IsEffectivelyVisible,"Home Start must show unified sport setup.");
            await Tap(Window,Named<RadioButton>(Wizard,"AflSportChoice"));await Next();
            await Until(()=>Named<Control>(Wizard,"TeamsStepContainer").IsEffectivelyVisible,"AFL teams must mount.");
            Named<TextBox>(Wizard,"SetupHomeNameBox").Text="Finals Draft Home";Named<TextBox>(Wizard,"SetupAwayNameBox").Text="Finals Draft Away";
            await Next();await Next();
            await Until(()=>Named<Control>(Wizard,"Step4Panel").IsEffectivelyVisible,"Actual options step must mount.");
            Named<ComboBox>(Wizard,"SetupWeatherLocation").Text="";await Settle();
        }
        internal Task Next()=>Tap(Window,Named<Button>(Wizard,"NextButton"));
        internal Task Back()=>Tap(Window,Named<Button>(Wizard,"BackButton"));
        internal Task Cancel()=>Tap(Window,Named<Button>(Wizard,"CancelSetupButton"));
        public async ValueTask DisposeAsync()
        {
            if(disposed)return;disposed=true;
            if(!fixtureMutated&&window is null)return;
            var errors=new List<Exception>();
            void Attempt(string stage,Action action)
            {try{action();}catch(Exception error){errors.Add(new InvalidOperationException(stage,error));}}
            async Task Drain(string stage)
            {try{await Settle(100);}catch(Exception error){errors.Add(new InvalidOperationException(stage,error));}}
            bool Terminal()=>window is null||closedWindows.TryGetValue(window,out bool closed)&&closed&&closedWindows.Values.All(value=>value);
            // Observe actual Closed events for this owner and every owned
            // window discovered while closing. A delay or IsVisible=false
            // does not establish terminal controller/window lifetime.
            if(window is not null)Attempt("discover initial owned windows",()=>TrackWindow(window));
            for(int pass=0;pass<50&&!Terminal();pass++)
            {
                Attempt("discover owned windows",()=>TrackWindow(Window));
                foreach(var owned in closedWindows.Keys.Where(owner=>!ReferenceEquals(owner,window)).ToArray())
                    if(!closedWindows[owned]&&closeRequested.Add(owned))Attempt("close owned window "+owned.Title,owned.Close);
                if(window is not null&&(!closedWindows.TryGetValue(window,out bool mainClosed)||!mainClosed)&&closeRequested.Add(window))Attempt("close main window",window.Close);
                await Drain("drain close callbacks "+pass);
            }
            if(!Terminal())
            {
                RetainedUnclosed.Add(this);
                errors.Add(new InvalidOperationException("Finals cleanup could not observe Window.Closed for the owner and every tracked owned window. Self-test mode, isolated data environment, files and controlled preferences are retained; restoration was not attempted. Fixture directory: "+data));
                throw new AggregateException("Finals fixture terminal close failed; environment retained.",errors);
            }
            // Root production lifetime fixes own subscription teardown. Do not
            // manually invoke or detach private controller event handlers here.
            Attempt("clear pressed keys",NativeKeyboard.ClearPressed);
            Attempt("restore glass and reduced motion",()=>OperatorAppearance.Configure(previousGlass,previousReduce));
            Attempt("restore theme",()=>app.ThemeService.SetTheme(previousTheme));
            Attempt("restore density",()=>app.ThemeService.SetDensity(previousDensity));
            Attempt("restore quarter-label preference",()=>QuarterLabelService.SetCurrentOverride(previousQuarter));
            Attempt("restore animation preference",()=>OperatorMotion.AnimationOverride=previousAnimation);
            await Drain("drain terminal preference callbacks before restoring files");
            if(storageCaptured)
            {
                string[] currentFiles=[];
                Attempt("enumerate current self-test files",()=>currentFiles=Directory.Exists(SelfTestStorage.RootPath)?Directory.GetFiles(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories):[]);
                foreach(string path in currentFiles.Where(path=>!files.ContainsKey(path)))Attempt("remove created file "+path,()=>File.Delete(path));
                foreach(var(path,bytes) in files)Attempt("restore file "+path,()=>{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);});
                string[] currentDirectories=[];
                Attempt("enumerate current self-test directories",()=>currentDirectories=Directory.Exists(SelfTestStorage.RootPath)?Directory.GetDirectories(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories).Append(SelfTestStorage.RootPath).OrderByDescending(path=>path.Length).ToArray():[]);
                foreach(string directory in currentDirectories.Where(directory=>!directories.Contains(directory)))Attempt("remove created empty directory "+directory,()=>{if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);});
            }
            // Keep self-test mode and its data redirect through final callbacks
            // and every storage restoration attempt, even if one stage failed.
            Attempt("restore data environment",()=>Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",previousData));
            Attempt("restore self-test mode",()=>app.IsSelfTestMode=previousSelfTest);
            Attempt("remove isolated fixture directory",()=>{if(Directory.Exists(data))Directory.Delete(data,true);});
            if(errors.Count>0)throw new AggregateException("Finals fixture cleanup failed; all independent restoration stages were attempted.",errors);
        }
    }
    private static void State(Fixture f,IToggleProvider provider,bool on)
    {
        Assert.Equal(on,f.Toggle.IsChecked==true);Assert.Equal(on?ToggleState.On:ToggleState.Off,provider.ToggleState);
        Assert.Equal(on?1d:0d,Named<Border>(f.Wizard,"FinalsSelectedBadge").Opacity);
        Assert.Equal(on,Assert.Single(f.Toggle.GetVisualDescendants().OfType<OperatorSymbol>()).IsActive);
        Assert.Equal(on?"On":"Off",AutomationProperties.GetItemStatus(f.Toggle));
    }
    private static string MatchState(Fixture f)=>JsonSerializer.Serialize(f.Match.ToState());
    private static string Recovery()=>JsonSerializer.Serialize(GameStateService.Load());
    private static string Preferences()=>JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());

    [AvaloniaTheory]
    [InlineData(1280,850,false,false)][InlineData(1280,850,true,true)]
    [InlineData(420,320,false,true)][InlineData(420,320,true,false)]
    [Trait("Evidence","AflSetupOriginalFinals")]
    public async Task OriginalSetupCheckboxOwnsCardAndExposesSequentialKeyboardFocus(int width,int height,bool reduced,bool light)
    {
        await Fixture.Run(width,height,reduced,light,async f =>
        {
            var original=f.Toggle;var card=Named<OperatorGlassSurface>(f.Wizard,"FinalsCard");
            Assert.True(original.IsEffectivelyVisible,"Original IsChecked owner must be mounted in the visible card.");
            Assert.Same(original,card.Child);Assert.False(card.Focusable);Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
            Assert.Equal("Finals mode",AutomationProperties.GetName(original));Assert.Equal("Use the gold match presentation accents.",AutomationProperties.GetHelpText(original));
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));State(f,provider,false);
            await TabTo(f.Window,original);
            var focus=Assert.Single(original.GetVisualDescendants().OfType<Border>(),b=>b.Name=="FinalsFocusFrame");
            Assert.True(focus.IsEffectivelyVisible);Assert.Equal(new Thickness(2),focus.BorderThickness);
            Assert.True(original.Bounds.Height>=44);Assert.All(Probes(Painted(f.Window,original)),p=>Assert.True(Within(original,f.Window.InputHitTest(p))));
            Capture(f.Window,$"setup-finals-focus-{width}-{reduced}-{light}");
        });
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","AflSetupOriginalFinals")]
    public async Task SpaceEnterRepeatPointerProviderAndProgrammaticChangesShareOneDraftState(int width,int height,bool reduced)
    {
        await Fixture.Run(width,height,reduced,false,async f =>
        {
            var toggle=f.Toggle;
            string match=MatchState(f),saved=Recovery(),preferences=Preferences();int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));await TabTo(f.Window,toggle);
            Key(f.Window,PhysicalKey.Space);await Settle();Assert.Equal(1,changes);State(f,provider,true);
            f.Window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);await Settle();Assert.Equal(2,changes);State(f,provider,false);
            f.Window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);await Settle();Assert.Equal(2,changes);State(f,provider,false);
            f.Window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Key(f.Window,PhysicalKey.Enter);await Settle();Assert.Equal(3,changes);State(f,provider,true);
            Assert.Null(f.Wizard.Result);Assert.True(Named<Control>(f.Wizard,"Step4Panel").IsEffectivelyVisible);
            await Tap(f.Window,toggle);Assert.Equal(4,changes);State(f,provider,false);
            provider.Toggle();await Settle();Assert.Equal(5,changes);State(f,provider,true);
            toggle.IsChecked=false;await Settle();Assert.Equal(6,changes);State(f,provider,false);
            toggle.IsChecked=true;await Settle();Assert.Equal(7,changes);State(f,provider,true);
            Assert.Equal(match,MatchState(f));Assert.Equal(saved,Recovery());Assert.Equal(preferences,Preferences());Assert.Null(f.Wizard.Result);
            Capture(f.Window,$"setup-finals-selected-{width}-{reduced}");
        });
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(420,320,true)]
    [Trait("Evidence","AflSetupOriginalFinals")]
    public async Task BackRetainsDraftCancelPreservesMatchAndNewSetupResetsFinals(int width,int height,bool reduced)
    {
        await Fixture.Run(width,height,reduced,false,async f =>
        {
            var original=f.Toggle;
            string match=MatchState(f),saved=Recovery(),preferences=Preferences();
            await TabTo(f.Window,original);Key(f.Window,PhysicalKey.Space);await Settle();
            await f.Back();await f.Next();Assert.Same(original,f.Toggle);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(f.Toggle));State(f,provider,true);
            Assert.Equal("Finals Draft Home",Named<TextBox>(f.Wizard,"SetupHomeNameBox").Text);Assert.Null(f.Wizard.Result);
            await f.Cancel();await Until(()=>f.Home.IsEffectivelyVisible,"Cancel must return to Home.");
            Assert.False(f.Wizard.IsEffectivelyVisible);Assert.Null(f.Wizard.Result);Assert.Equal(match,MatchState(f));Assert.Equal(saved,Recovery());Assert.Equal(preferences,Preferences());
            await f.Start();Assert.Same(original,f.Toggle);State(f,provider,false);Assert.Null(f.Wizard.Result);
            Assert.Equal(match,MatchState(f));Assert.Equal(saved,Recovery());Assert.Equal(preferences,Preferences());
            await TabTo(f.Window,f.Toggle);Key(f.Window,PhysicalKey.Escape);await Until(()=>f.Home.IsEffectivelyVisible,"Escape must cancel actual setup.");
            Assert.Equal(match,MatchState(f));Assert.Equal(saved,Recovery());Assert.Equal(preferences,Preferences());Assert.Null(f.Wizard.Result);
        });
    }

    [AvaloniaTheory]
    [InlineData(1280,850,true)][InlineData(420,320,false)]
    [Trait("Evidence","AflSetupOriginalFinals")]
    public async Task FinishReadsTheOriginalCheckboxAfterRealInputReversals(int width,int height,bool finalValue)
    {
        await Fixture.Run(width,height,true,false,async f =>
        {
            int completed=0;SetupResult? delivered=null;
            f.Wizard.SetupCompleted+=result=>{completed++;delivered=result;};
            await TabTo(f.Window,f.Toggle);Key(f.Window,PhysicalKey.Space);await Settle();
            if(!finalValue){Key(f.Window,PhysicalKey.Enter);await Settle();}
            Assert.Equal(finalValue,f.Toggle.IsChecked==true);await f.Next();
            await Until(()=>f.Wizard.Result is not null,"Public Start match must create setup result.");
            Assert.Equal(1,completed);Assert.Same(f.Wizard.Result,delivered);Assert.Equal(finalValue,delivered!.FinalsMode);
            Assert.Equal("Finals Draft Home",delivered.HomeName);Assert.Equal("Finals Draft Away",delivered.AwayName);
            Assert.Equal(finalValue,Read<bool>(f.Window,"_finalsMode"));
        });
    }
}
