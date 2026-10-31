// Original production toggle; real input and production rendering-frame regression.
// Four cases:1280/420 × enabled/Reduce. Uses real mode/toggle/input/render APIs.
// Positive DP trajectories + source clock-hand mapping are not a claim of native
// WPF pixel animation, or that a static PNG proves an animated glyph trajectory.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record Mr6SelectionFrame(string Stage,string Source,double Milliseconds,
        double ThumbX,double ThumbScaleX,double ThumbScaleY,double Selection,double Fill,double Pulse,
        Rect InputBounds,Rect TitleBounds,Rect DescriptionBounds,Matrix ParentTransform,Matrix ToggleTransform,
        Matrix TitleTransform,Matrix DescriptionTransform,bool Checked);
    private static bool Mr6SelectionEndpoint(Mr6SelectionFrame f,bool on)=>
        Math.Abs(f.ThumbX-(on?20:0))<.00001&&Math.Abs(f.ThumbScaleX-1)<.00001
        &&Math.Abs(f.ThumbScaleY-1)<.00001&&f.Selection==(on?1d:0d)&&f.Fill==(on?1d:0d)&&f.Pulse==0;
    private static void Mr6AssertSelectionEndpoint(Mr6SelectionFrame f,bool on)
    {
        Assert.InRange(Math.Abs(f.ThumbX-(on?20:0)),0,.00001);
        Assert.InRange(Math.Abs(f.ThumbScaleX-1),0,.00001);Assert.InRange(Math.Abs(f.ThumbScaleY-1),0,.00001);
        Assert.Equal(on?1d:0d,f.Selection);Assert.Equal(on?1d:0d,f.Fill);Assert.Equal(0d,f.Pulse);
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR6ObservedStoppageSelection")]
    public async Task OriginalStoppageToggleMovesOnlyItsSelectionThumbAndClockHandAndSettlesCurrentState(
        int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var trace=new List<Mr6SelectionFrame>();var frames=new List<Mr6SelectionFrame>();
        string stage="initial";bool dispatching=false;var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            await OpenClockSection(f,reduced);await Settle(400);
            Assert.Null(OperatorMotion.AnimationOverride);Assert.Equal(!reduced,OperatorMotion.Enabled);
            var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
            var card=Named<Border>(f.Window,"SettingsControlPanelCountdownOptionCard");
            await Mr6Reveal(f,toggle);
            // Neutral pointer pose before fixing the hit/text geometry. Hover
            // may intentionally inflate only the thumb, not the whole row.
            f.Window.MouseMove(new Point(1,1));await Settle(400);
            Assert.False(card.Focusable);Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
            Assert.True(OperatorChoiceMotion.GetEnabled(toggle));Assert.True(OperatorControlMotion.GetEnabled(toggle));
            Assert.False(SettingsControlMotion.GetEnabled(toggle));Assert.False(CricketActionMotion.GetEnabled(toggle));
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            var thumb=Mr5FinalsPart<Avalonia.Controls.Shapes.Ellipse>(toggle,"StoppageThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
            var shift=Assert.Single(group.Children.OfType<TranslateTransform>());
            var scale=Assert.Single(group.Children.OfType<ScaleTransform>());
            var selection=Mr5FinalsPart<OperatorChoiceSurface>(toggle,"StoppageSelection");
            var symbol=Mr5FinalsPart<OperatorSymbol>(toggle,"StoppageSymbol");
            var title=Mr5FinalsPart<TextBlock>(toggle,"StoppageTitle");
            var description=Mr5FinalsPart<TextBlock>(toggle,"StoppageDescription");
            var stateText=Mr5FinalsPart<TextBlock>(toggle,"StoppageStateText");
            var track=Mr5FinalsPart<Border>(toggle,"StoppageTrack");
            Assert.Equal("clock-up",symbol.Kind);Assert.Equal(0d,selection.OriginX);
            Assert.False(selection.IsHitTestVisible);Assert.False(symbol.IsHitTestVisible);
            Assert.Equal("Stoppage clock",title.Text);
            Assert.Equal("Show the local countdown in Match. At zero, it pauses the match clock.",description.Text);
            // Arrange the desired current state through the actual native
            // provider; remembered workflow preferences need not start ON.
            if(toggle.IsChecked!=true){provider.Toggle();await Settle(800);}
            Assert.True(toggle.IsChecked);Assert.True(symbol.IsActive);
            string scores=Scores(f.Match);var stopped=f.Match.ElapsedInQuarter;int changes=0;
            toggle.IsCheckedChanged+=(_,_)=>changes++;
            Rect input=FinalsPainted(f.Window,toggle),titleBounds=FinalsPainted(f.Window,title),descriptionBounds=FinalsPainted(f.Window,description),trackBounds=FinalsPainted(f.Window,track);
            Assert.True(input.Contains(titleBounds));Assert.True(input.Contains(descriptionBounds));Assert.True(input.Contains(trackBounds));
            Assert.True(titleBounds.Right<=trackBounds.Left);Assert.True(descriptionBounds.Right<=trackBounds.Left);
            Matrix Transform(Control c)=>c.RenderTransform?.Value??Matrix.Identity;
            Mr6SelectionFrame Read(string source="snapshot")=>new(stage,source,watch.Elapsed.TotalMilliseconds,
                shift.X,scale.ScaleX,scale.ScaleY,selection.Progress,symbol.FillProgress,Mr5FinalsDiagnostic(symbol,"Pulse"),
                FinalsPainted(f.Window,toggle),FinalsPainted(f.Window,title),FinalsPainted(f.Window,description),
                Transform(card),Transform(toggle),Transform(title),Transform(description),toggle.IsChecked==true);
            void Stable(Mr6SelectionFrame frame)
            {
                Assert.Equal(input,frame.InputBounds);Assert.Equal(titleBounds,frame.TitleBounds);Assert.Equal(descriptionBounds,frame.DescriptionBounds);
                Assert.Equal(Matrix.Identity,frame.ParentTransform);Assert.Equal(Matrix.Identity,frame.ToggleTransform);
                Assert.Equal(Matrix.Identity,frame.TitleTransform);Assert.Equal(Matrix.Identity,frame.DescriptionTransform);
            }
            void Authority(bool on,int expectedChanges)
            {
                Assert.Equal(on,toggle.IsChecked==true);Assert.Equal(on,symbol.IsActive);
                Assert.Equal(on?ToggleState.On:ToggleState.Off,provider.ToggleState);Assert.Equal(on?"ON":"OFF",stateText.Text);
                Assert.Equal(on,Mr6Read<bool>(f,"_showControlPanelCountdown"));
                Assert.Equal(on,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);Assert.Equal(expectedChanges,changes);
                Assert.Equal(scores,Scores(f.Match));Assert.False(f.Match.ClockRunning);Assert.Equal(stopped,f.Match.ElapsedInQuarter);
                Assert.False(Mr6Read<bool>(f,"_controlPanelCountdownRunning"));
                Stable(Read());Assert.All(FinalsProbes(input),p=>Assert.True(Within(toggle,f.Window.InputHitTest(p))));
                trace.Add(Read("post-input-synchronous-authority"));
            }
            void Stage(string next){stage=next;frames.Clear();}
            void Dispatch(Action action){dispatching=true;try{action();}finally{dispatching=false;}}
            // The production rendering dispatcher can fire during input. Keep
            // those frames as evidence, but stage trajectory oracles use only
            // callbacks after the synchronous input API has returned.
            observer=(_,_)=>
            {
                var frame=Read(dispatching?"NativeRendering-during-input-dispatch":"NativeRendering");
                trace.Add(frame);if(!dispatching)frames.Add(frame);
            };
            NativeRendering.Rendering+=observer;
            Authority(true,0);Mr6AssertSelectionEndpoint(Read(),true);
            async Task Observe(Func<bool> condition,string explanation)=>await Mr5FinalsObserveUntil(condition,explanation);
            async Task Finish(bool on)
            {
                await Observe(()=>Mr6SelectionEndpoint(Read(),on),"Current Stoppage ON/OFF parts must finish naturally without forcing Reduce.");
                Mr6AssertSelectionEndpoint(Read(),on);
            }
            bool MovingThumb()
            {
                var moving=frames.Where(x=>x.ThumbX>.01&&x.ThumbX<19.99).ToArray();
                return moving.Length>=2&&moving.Max(x=>x.ThumbX)-moving.Min(x=>x.ThumbX)>.01;
            }
            async Task Arrival(bool on)
            {
                if(!reduced)
                {
                    await Observe(()=>MovingThumb()&&frames.Any(x=>x.Selection>.001&&x.Selection<.999)
                        &&(!on||frames.Any(x=>x.Pulse>.001)),
                        "Actual Stoppage thumb, selection surface and ON-only clock hand accent must progress on production rendering frames.");
                    if(on){trace.Add(Read("mid-arrival-capture-request"));Capture(f.Window,$"mr6-selection-mid-{width}");}
                    await Finish(on);
                }
                else
                {
                    Mr6AssertSelectionEndpoint(Read(),on);
                    await Observe(()=>frames.Count>=4,"Reduced selection must remain canonical across actual frames.");
                    Assert.All(frames,x=>Mr6AssertSelectionEndpoint(x,on));
                }
                Assert.All(frames,Stable);
            }

            Stage("space-off");Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(false,1);await Arrival(false);
            Stage("provider-on");Dispatch(provider.Toggle);Authority(true,2);await Arrival(true);
            // A real held pointer press affects only the actual thumb part.
            Stage("physical-press");Dispatch(()=>{f.Window.MouseMove(input.Center);f.Window.MouseDown(input.Center,MouseButton.Left);});Authority(true,2);
            if(!reduced)await Observe(()=>frames.Any(x=>x.ThumbScaleX>1.001&&x.ThumbScaleY<.999),"Actual Stoppage thumb must show physical press deformation without zooming its text/card.");
            else Mr6AssertSelectionEndpoint(Read(),true);
            Assert.All(frames,Stable);
            Stage("pointer-release-off");Dispatch(()=>f.Window.MouseUp(input.Center,MouseButton.Left));Authority(false,3);
            f.Window.MouseMove(new Point(1,1));await Arrival(false);

            Stage("on-before-reversals");Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,4);
            if(!reduced)await Observe(()=>MovingThumb()&&frames.Any(x=>x.Selection>.001&&x.Selection<.999),"Require witnessed active selection before rapid reversal.");
            Stage("rapid-reversals");Dispatch(provider.Toggle);Authority(false,5);Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,6);
            await Finish(true);Authority(true,6); // Natural completion precedes separate live-Reduce test.
            Stage("second-off-before-live-reduce");Dispatch(provider.Toggle);Authority(false,7);
            if(!reduced)await Observe(()=>MovingThumb()&&frames.Any(x=>x.Selection>.001&&x.Selection<.999),"Live Reduce interrupts an actual new departure, not a settled switch.");
            Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,8);
            Stage("live-reduce-current-on");OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(OperatorMotion.Enabled);Authority(true,8);Mr6AssertSelectionEndpoint(Read(),true);
            await Observe(()=>frames.Count>=60,"Observe current canonical parts after Reduce long enough to expose stale spring/completion work.");
            Assert.All(frames,x=>{Mr6AssertSelectionEndpoint(x,true);Stable(x);});Authority(true,8);
            Stage("reduced-off");Dispatch(provider.Toggle);Authority(false,9);Mr6AssertSelectionEndpoint(Read(),false);
            await Observe(()=>frames.Count>=10,"Reduced subsequent input also keeps endpoint state.");
            Assert.All(frames,x=>{Mr6AssertSelectionEndpoint(x,false);Stable(x);});
            Capture(f.Window,$"mr6-selection-final-{width}-{reduced}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr6-selection-frames-{width}-{reduced}",trace);
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
