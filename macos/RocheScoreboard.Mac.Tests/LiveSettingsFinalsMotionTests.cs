// Review draft: add as another partial of the existing real-route fixture.
// The rendering observer reads actual template values. It never invokes a
// private Frame/sample callback or treats Motion.IsAnimating as spring evidence.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    private readonly record struct Mr5FinalsFrame(string Stage,string Source,double ElapsedMilliseconds,double X,double ScaleX,double ScaleY,
        double Selection,double Fill,double Pulse,double Turn,Rect InputBounds);
    private readonly record struct Mr5InitialFinalsFrame(string Stage,double ElapsedMilliseconds,double Fill,double Pulse,double Turn);

    private static void Mr5FinalsWriteTrace(string name,object frames)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
    }

    private static T Mr5FinalsPart<T>(CheckBox toggle,string name) where T:Control =>
        Assert.Single(toggle.GetVisualDescendants().OfType<T>(),part=>part.Name==name);

    private static double Mr5FinalsDiagnostic(OperatorSymbol symbol,string name)
    {
        object diagnostics=symbol.Diagnostics;
        return Assert.IsType<double>(diagnostics.GetType().GetProperty(name)!.GetValue(diagnostics));
    }

    private static async Task Mr5FinalsObserveUntil(Func<bool> observed,string explanation)
    {
        // Observation/readiness bound only. No expected position at a named
        // millisecond; the production NativeRendering dispatcher must tick.
        for(int attempt=0;attempt<250&&!observed();attempt++)await Settle(12);
        Assert.True(observed(),explanation);
    }

    private static void Mr5FinalsEndpoint(Mr5FinalsFrame frame,bool on)
    {
        Assert.InRange(Math.Abs(frame.X-(on?20:0)),0,.00001);
        Assert.InRange(Math.Abs(frame.ScaleX-1),0,.00001);
        Assert.InRange(Math.Abs(frame.ScaleY-1),0,.00001);
        Assert.Equal(on?1d:0d,frame.Selection);
        Assert.Equal(on?1d:0d,frame.Fill);
        Assert.Equal(0d,frame.Pulse);
        Assert.Equal(0d,frame.Turn);
    }

    private static bool Mr5FinalsAtEndpoint(Mr5FinalsFrame frame,bool on) =>
        Math.Abs(frame.X-(on?20:0))<.00001&&Math.Abs(frame.ScaleX-1)<.00001
        &&Math.Abs(frame.ScaleY-1)<.00001&&frame.Selection==(on?1d:0d)
        &&frame.Fill==(on?1d:0d)&&frame.Pulse==0&&frame.Turn==0;

    [AvaloniaTheory]
    [InlineData(1280,850,"Expressive")]
    [InlineData(1280,850,"Calm")]
    [InlineData(1280,850,"Reduce")]
    [InlineData(420,320,"Expressive")]
    [InlineData(420,320,"Calm")]
    [InlineData(420,320,"Reduce")]
    [Trait("Evidence","MR5ObservedFinalsMotion")]
    public async Task RealFinalsPartsProgressOnRenderingFramesThenRapidInputAndReduceKeepCurrentState(
        int width,int height,string policy)
    {
        await using var f=await AflFixture.Create(width,height);
        var previousStyle=OperatorAppearance.MotionStyle;
        bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;
        var trace=new List<Mr5FinalsFrame>();
        try
        {
            bool reduced=policy=="Reduce";
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            await OpenClockSection(f,reduced);
            Assert.Null(OperatorMotion.AnimationOverride);
            Assert.Equal(!reduced,OperatorMotion.Enabled);
            var toggle=Named<CheckBox>(f.Window,"SettingsFinalsToggle");
            await RevealFinals(f,toggle);
            var card=Named<Border>(f.Window,"SettingsFinalsCard");
            var theme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.FinalsToggle"));
            Assert.Same(theme,toggle.Theme);
            Assert.Same(toggle,Assert.Single(card.GetVisualDescendants().OfType<CheckBox>(),x=>x.IsEffectivelyVisible));
            Assert.False(card.Focusable);
            Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
            Assert.True(OperatorControlMotion.GetEnabled(toggle));
            Assert.True(OperatorChoiceMotion.GetEnabled(toggle));
            Assert.False(SettingsControlMotion.GetEnabled(toggle));
            var thumb=Mr5FinalsPart<Border>(toggle,"FinalsThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
            Assert.Equal(2,group.Children.Count);
            var scale=Assert.IsType<ScaleTransform>(group.Children[0]);
            var shift=Assert.IsType<TranslateTransform>(group.Children[1]);
            var selection=Mr5FinalsPart<OperatorChoiceSurface>(toggle,"FinalsSelection");
            var symbol=Mr5FinalsPart<OperatorSymbol>(toggle,"FinalsSymbol");
            var stateText=Mr5FinalsPart<TextBlock>(toggle,"FinalsStateText");
            Assert.Equal("finals",symbol.Kind);
            Assert.Equal(1d,selection.OriginX);
            Assert.Null(new OperatorChoiceSurface().FillBrush);
            Assert.NotNull(selection.FillBrush);
            Assert.False(selection.IsHitTestVisible);Assert.False(symbol.IsHitTestVisible);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            string scores=Scores(f.Match);
            int changes=0;
            toggle.IsCheckedChanged+=(_,_)=>changes++;
            Rect input=FinalsPainted(f.Window,toggle);
            var description=Mr5FinalsPart<TextBlock>(toggle,"FinalsDescription");
            var title=Mr5FinalsPart<TextBlock>(toggle,"FinalsTitle");
            var well=Mr5FinalsPart<Grid>(toggle,"FinalsSwitchWell");
            Rect textBox=FinalsPainted(f.Window,description),titleBox=FinalsPainted(f.Window,title),switchBox=FinalsPainted(f.Window,well);
            Assert.True(input.Contains(textBox));Assert.True(input.Contains(titleBox));Assert.True(input.Contains(switchBox));
            Assert.True(textBox.Right<=switchBox.Left,"Wrapped description must not extend beneath the actual switch well.");
            Assert.True(titleBox.Right<=switchBox.Left,"Title and switch retain independent columns.");
            Assert.Equal("Golden bar styling and time-on after siren in final quarter",description.Text);

            bool dispatchingInput=false;
            string stage="initial-off";
            var observationClock=Stopwatch.StartNew();
            Mr5FinalsFrame Read(string source="snapshot")=>new(stage,source,observationClock.Elapsed.TotalMilliseconds,shift.X,scale.ScaleX,scale.ScaleY,selection.Progress,
                symbol.FillProgress,Mr5FinalsDiagnostic(symbol,"Pulse"),Mr5FinalsDiagnostic(symbol,"Turn"),
                FinalsPainted(f.Window,toggle));
            void Authority(bool on,int expectedChanges)
            {
                // This runs in the same turn as input, before yielding for any
                // animation frame: presentation cannot defer accepted state.
                Assert.Equal(on,toggle.IsChecked==true);
                Assert.Equal(on,FinalsValue(f));
                Assert.Equal(on?ToggleState.On:ToggleState.Off,provider.ToggleState);
                Assert.Equal(on?"ON":"OFF",stateText.Text);
                Assert.Equal(on,symbol.IsActive);
                Assert.Equal(expectedChanges,changes);
                Assert.Equal(scores,Scores(f.Match));
                Assert.Same(toggle,Named<CheckBox>(f.Window,"SettingsFinalsToggle"));
                Assert.Equal(input,FinalsPainted(f.Window,toggle));
                Assert.All(FinalsProbes(input),point=>Assert.True(Within(toggle,f.Window.InputHitTest(point))));
                trace.Add(Read("synchronous-authority"));
            }
            Authority(false,0); // The genuine new AFL setup defaults Finals OFF.
            Mr5FinalsEndpoint(Read(),false);
            var frames=new List<Mr5FinalsFrame>();
            void BeginStage(string next)
            {
                Mr5FinalsWriteTrace($"mr5-finals-frames-{width}-{policy}",trace);
                stage=next;frames.Clear();
            }
            void Dispatch(Action action)
            {
                dispatchingInput=true;
                try{action();}finally{dispatchingInput=false;}
            }
            observer=(_,_)=>{var frame=Read(dispatchingInput?"NativeRendering-during-input-dispatch":"NativeRendering");if(!dispatchingInput)frames.Add(frame);trace.Add(frame);};
            NativeRendering.Rendering+=observer;

            // WPF's preview-left observers must not become every-button
            // observers in the native adapter. Right-click keeps the hover
            // pose isotropic and cannot select or stretch the switch.
            f.Window.MouseMove(input.Center);await Settle(500);
            BeginStage("right-button-no-selection");
            Dispatch(()=>f.Window.MouseDown(input.Center,MouseButton.Right));Authority(false,0);
            await Mr5FinalsObserveUntil(()=>frames.Count>=5,"Observe the real right-button hold without a left-press accent.");
            Assert.All(frames,x=>{Assert.InRange(Math.Abs(x.ScaleX-x.ScaleY),0,.0001);Assert.Equal(0d,x.X);Assert.Equal(0d,x.Fill);Assert.Equal(0d,x.Selection);});
            Dispatch(()=>f.Window.MouseUp(input.Center,MouseButton.Right));Authority(false,0);

            // Hold the physical press long enough to observe the actual thumb
            // stretch. Checkbox authority still changes only on release.
            BeginStage("physical-press-off");
            f.Window.MouseMove(input.Center);
            Dispatch(()=>f.Window.MouseDown(input.Center,MouseButton.Left));
            Authority(false,0);
            if(!reduced)
                await Mr5FinalsObserveUntil(()=>frames.Any(x=>x.ScaleX>1.001&&x.ScaleY<.999),
                    "Physical pointer press must deform the actual FinalsThumb on a real rendering frame.");
            else
            {
                await Mr5FinalsObserveUntil(()=>frames.Count>=3,"Reduced-motion input must also be observed across real frames.");
                Assert.All(frames,x=>Mr5FinalsEndpoint(x,false));
            }
            BeginStage("physical-release-on");
            Dispatch(()=>f.Window.MouseUp(input.Center,MouseButton.Left));
            Authority(true,1);
            f.Window.MouseMove(new Point(1,1)); // Let the thumb return to its neutral scale.
            if(!reduced)
            {
                await Mr5FinalsObserveUntil(()=>
                {
                    var moving=frames.Where(x=>x.X>.01&&x.X<19.99).ToArray();
                    return moving.Length>=2&&moving.Max(x=>x.X)-moving.Min(x=>x.X)>.01
                        &&frames.Any(x=>x.Selection>.001&&x.Selection<.999)
                        &&frames.Any(x=>x.Fill>.001&&x.Fill<.999)
                        &&frames.Any(x=>x.Pulse>.001);
                },"Enabled motion must actually advance the manual thumb spring, selection fill, star fill and finite halo on production rendering frames.");
                Assert.All(frames,x=>Assert.Equal(0d,x.Turn));
                Assert.All(frames,x=>Assert.Equal(input,x.InputBounds));
                Assert.Contains(frames,x=>x.Selection<1&&x.Fill<1);
                Authority(true,1);
                trace.Add(Read("capture-request-mid-arrival"));
                Capture(f.Window,$"mr5-finals-mid-arrival-{width}-{policy}");
                await Mr5FinalsObserveUntil(()=>Mr5FinalsAtEndpoint(Read(),true),
                    "The ordinary ON arrival must naturally finish its thumb/selection/star fill and finite halo without forcing Reduce.");
                Mr5FinalsEndpoint(Read(),true);
            }
            else
            {
                Mr5FinalsEndpoint(Read(),true);
                await Mr5FinalsObserveUntil(()=>frames.Count>=3,"Reduced selected state must stay settled on actual frames.");
                Assert.All(frames,x=>Mr5FinalsEndpoint(x,true));
            }
            Capture(f.Window,$"mr5-finals-observed-{width}-{policy}");

            // The real application remote target repeats the existing ON value.
            // Enabled profiles must retain their natural, settled presentation.
            BeginStage("remote-same-on");
            Dispatch(()=>((IRemoteCommandTarget)f.Window).SetFinalsMode(true));
            Authority(true,1);Mr5FinalsEndpoint(Read(),true);
            await Mr5FinalsObserveUntil(()=>frames.Count>=12,"A same-value remote refresh must not replay the actual settled Finals motion.");
            Assert.All(frames,x=>Mr5FinalsEndpoint(x,true));
            Authority(true,1);

            // Begin an actual departure, then interrupt it with accessibility
            // and native keyboard input, without a settle between reversals.
            BeginStage("provider-off-departure");
            Dispatch(provider.Toggle);Authority(false,2);
            if(!reduced)
            {
                await Mr5FinalsObserveUntil(()=>frames.Any(x=>x.X>.01&&x.X<19.99)
                    &&frames.Any(x=>x.Selection>.001&&x.Selection<.999)
                    &&frames.Any(x=>x.Fill>.001&&x.Fill<.999),
                    "Observe a real OFF departure before claiming that rapid reversals interrupt active motion.");
            }
            BeginStage("rapid-reversals");
            Assert.True(Within(toggle,f.Window.FocusManager?.GetFocusedElement()));
            Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,3);
            Dispatch(provider.Toggle);Authority(false,4);
            Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,5);
            if(!reduced)
            {
                await Mr5FinalsObserveUntil(()=>Mr5FinalsAtEndpoint(Read(),true),
                    "Rapid reversals must finish naturally at the current ON state before Reduce is used.");
                Mr5FinalsEndpoint(Read(),true);Authority(true,5);
            }
            BeginStage("second-off-before-live-reduce");
            Dispatch(provider.Toggle);Authority(false,6);
            if(!reduced)
                await Mr5FinalsObserveUntil(()=>frames.Any(x=>x.X>.01&&x.X<19.99)&&frames.Any(x=>x.Fill>.001&&x.Fill<.999),
                    "Live Reduce must interrupt a witnessed new departure rather than an already settled switch.");
            Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,7);
            BeginStage("live-reduce-current-on");
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(OperatorMotion.Enabled);
            Authority(true,7);
            Mr5FinalsEndpoint(Read(),true);
            await Mr5FinalsObserveUntil(()=>frames.Count>=60,
                "Observe a bounded run of actual rendering frames after live Reduce to catch stale arrival/reversal callbacks.");
            Assert.All(frames,x=>Mr5FinalsEndpoint(x,true));
            Assert.All(frames,x=>Assert.Equal(input,x.InputBounds));
            Authority(true,7);

            // Input remains immediate while Reduce is already active.
            BeginStage("reduced-provider-off");
            Dispatch(provider.Toggle);Authority(false,8);Mr5FinalsEndpoint(Read(),false);
            await Mr5FinalsObserveUntil(()=>frames.Count>=4,"The latest OFF state must remain authoritative across subsequent rendering frames.");
            Assert.All(frames,x=>Mr5FinalsEndpoint(x,false));
            Authority(false,8);
            Capture(f.Window,$"mr5-finals-reduced-current-{width}-{policy}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr5-finals-frames-{width}-{policy}",trace);
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }

    // Small lifecycle supplement: actual production theme with IsChecked=true
    // before initial mount. This isolates restored template state; it does not
    // claim the application's disk-resume route has been exercised.
    [AvaloniaTheory]
    [InlineData("Expressive")][InlineData("Calm")]
    [Trait("Evidence","MR5FinalsInitiallySelectedTemplate")]
    public async Task InitiallySelectedProductionFinalsTemplateMountsWithoutCelebration(string policy)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;
        bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        Window? host=null;EventHandler? observer=null;
        var samples=new List<Mr5InitialFinalsFrame>();
        var observationClock=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
            OperatorMotion.AnimationOverride=null;
            Assert.True(OperatorMotion.Enabled);
            var original=Named<CheckBox>(f.Window,"SettingsFinalsToggle");
            string scores=Scores(f.Match);
            var theme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(original,"ControlCentre.FinalsToggle"));
            var restored=new CheckBox{IsChecked=true,Theme=theme};
            int changes=0;restored.IsCheckedChanged+=(_,_)=>changes++;
            host=new Window{Width=520,Height=200,Content=restored};
            // Register before Show so the first rendering callback after the
            // real Loaded/Attach route is included, not merely a settled tail.
            observer=(_,_)=>
            {
                var star=restored.GetVisualDescendants().OfType<OperatorSymbol>().SingleOrDefault(x=>x.Name=="FinalsSymbol");
                if(star is not null)samples.Add(new("initial-selected-mount",observationClock.Elapsed.TotalMilliseconds,
                    star.FillProgress,Mr5FinalsDiagnostic(star,"Pulse"),Mr5FinalsDiagnostic(star,"Turn")));
            };
            NativeRendering.Rendering+=observer;
            host.Show();
            await Mr5FinalsObserveUntil(()=>samples.Count>=45,"Initially selected production template must mount and be observed on actual rendering frames.");
            Assert.Same(theme,restored.Theme);
            Assert.True(restored.IsChecked);Assert.Equal(0,changes);
            Assert.All(samples,x=>{Assert.Equal(1d,x.Fill);Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);});
            var thumb=Mr5FinalsPart<Border>(restored,"FinalsThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
            Assert.Equal(20d,Assert.Single(group.Children.OfType<TranslateTransform>()).X);
            Assert.Equal(1d,Mr5FinalsPart<OperatorChoiceSurface>(restored,"FinalsSelection").Progress);
            Assert.Equal("ON",Mr5FinalsPart<TextBlock>(restored,"FinalsStateText").Text);
            Assert.Equal(scores,Scores(f.Match));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr5-finals-initial-selected-{policy}",samples);
            host?.Close();
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
