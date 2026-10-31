// Regression for the original production Names control and actual rendering frames.
// Four actual-route cases:1280/420 × enabled/Reduce. Positive part trajectories
// are not native WPF pixel evidence; source maps FillProgress to retracting name
// panels, leaving the two score wells fixed. Pulse/Turn must remain zero.
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
    private sealed record Mr7SelectionFrame(string Stage,string Source,double Milliseconds,
        double ThumbX,double ThumbScaleX,double ThumbScaleY,double Selection,double Fill,double Pulse,double Turn,
        Rect InputBounds,Rect TitleBounds,Rect DescriptionBounds,Matrix ParentTransform,Matrix ToggleTransform,
        Matrix TitleTransform,Matrix DescriptionTransform,bool Checked,bool IconLoaded,bool ToggleLoaded,bool AttachedToOwner);
    private static bool Mr7SelectionEndpoint(Mr7SelectionFrame frame,bool hidden)=>
        Math.Abs(frame.ThumbX-(hidden?20:0))<.00001&&Math.Abs(frame.ThumbScaleX-1)<.00001
        &&Math.Abs(frame.ThumbScaleY-1)<.00001&&frame.Selection==(hidden?1d:0d)
        &&frame.Fill==(hidden?1d:0d)&&frame.Pulse==0&&frame.Turn==0;
    private static void Mr7AssertSelectionEndpoint(Mr7SelectionFrame frame,bool hidden)
    {
        Assert.InRange(Math.Abs(frame.ThumbX-(hidden?20:0)),0,.00001);
        Assert.InRange(Math.Abs(frame.ThumbScaleX-1),0,.00001);Assert.InRange(Math.Abs(frame.ThumbScaleY-1),0,.00001);
        Assert.Equal(hidden?1d:0d,frame.Selection);Assert.Equal(hidden?1d:0d,frame.Fill);
        Assert.Equal(0d,frame.Pulse);Assert.Equal(0d,frame.Turn);
    }

    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(1280,850,true)]
    [InlineData(420,320,false)][InlineData(420,320,true)]
    [Trait("Evidence","MR7ObservedHideNamesSelection")]
    public async Task HideNamesSelectionRetractsOnlyItsNameplatesAndKeepsOriginalInputAndCurrentPreferenceAuthoritative(
        int width,int height,bool reduced)
    {
        await using var f=await AflFixture.Create(width,height);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var trace=new List<Mr7SelectionFrame>();var frames=new List<Mr7SelectionFrame>();
        string stage="arrange-initial-allowed";bool dispatching=false;var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            await OpenClockSection(f,reduced);
            Assert.Null(OperatorMotion.AnimationOverride);Assert.Equal(!reduced,OperatorMotion.Enabled);
            var toggle=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");
            var card=Named<Border>(f.Window,"SettingsHideTeamNamesCard");
            string scores=Scores(f.Match);var stopped=f.Match.ElapsedInQuarter;
            await RevealHideTeamNames(f,toggle);
            f.Window.MouseMove(new Point(1,1));await Settle(400);
            Assert.True(toggle.IsLoaded);Assert.Same(f.Window,toggle.GetVisualRoot());
            Assert.False(card.Focusable);Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);
            Assert.True(OperatorChoiceMotion.GetEnabled(toggle));Assert.True(OperatorControlMotion.GetEnabled(toggle));
            Assert.False(SettingsControlMotion.GetEnabled(toggle));Assert.False(CricketActionMotion.GetEnabled(toggle));
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            var thumb=Mr5FinalsPart<Border>(toggle,"NamesThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
            var shift=Assert.Single(group.Children.OfType<TranslateTransform>());
            var scale=Assert.Single(group.Children.OfType<ScaleTransform>());
            var selection=Mr5FinalsPart<OperatorChoiceSurface>(toggle,"NamesSelection");
            var symbol=Mr5FinalsPart<OperatorSymbol>(toggle,"NamesSymbol");
            var title=Mr5FinalsPart<TextBlock>(toggle,"NamesTitle");
            var description=Mr5FinalsPart<TextBlock>(toggle,"NamesDescription");
            var stateText=Mr5FinalsPart<TextBlock>(toggle,"NamesStateText");
            var track=Mr5FinalsPart<Border>(toggle,"NamesTrack");
            Assert.Equal("hide-team-names",symbol.Kind);Assert.Equal(1d,selection.OriginX);
            Assert.False(selection.IsHitTestVisible);Assert.False(symbol.IsHitTestVisible);
            Assert.True(symbol.IsLoaded);Assert.Same(f.Window,symbol.GetVisualRoot());
            Assert.Equal("Hide team names",title.Text);
            Assert.Equal("Removes name panels while keeping professional, digit-safe score sizing",description.Text);
            Matrix Transform(Control c)=>c.RenderTransform?.Value??Matrix.Identity;
            Mr7SelectionFrame Read(string source="snapshot")=>new(stage,source,watch.Elapsed.TotalMilliseconds,
                shift.X,scale.ScaleX,scale.ScaleY,selection.Progress,symbol.FillProgress,
                Mr5FinalsDiagnostic(symbol,"Pulse"),Mr5FinalsDiagnostic(symbol,"Turn"),
                HideTeamNamesPainted(f.Window,toggle),HideTeamNamesPainted(f.Window,title),HideTeamNamesPainted(f.Window,description),
                Transform(card),Transform(toggle),Transform(title),Transform(description),toggle.IsChecked==true,
                symbol.IsLoaded,toggle.IsLoaded,ReferenceEquals(f.Window,symbol.GetVisualRoot()));
            observer=(_,_)=>
            {
                var frame=Read(dispatching?"NativeRendering-during-input-dispatch":"NativeRendering");
                trace.Add(frame);if(!dispatching)frames.Add(frame);
            };
            NativeRendering.Rendering+=observer;
            // Arrange ALLOWED through the ORIGINAL public semantic provider,
            // before counters. Do not assume a persisted/default bool is false.
            if(toggle.IsChecked==true)provider.Toggle();
            await Mr5FinalsObserveUntil(()=>Mr7SelectionEndpoint(Read(),false),"Public initial ALLOWED state must finish naturally before measuring gestures.");
            Assert.False(toggle.IsChecked);Assert.False(HideTeamNamesValue(f));Assert.False(HideTeamNamesPreference(f));
            Assert.Equal(scores,Scores(f.Match));
            int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            Rect input=HideTeamNamesPainted(f.Window,toggle),titleBounds=HideTeamNamesPainted(f.Window,title),descriptionBounds=HideTeamNamesPainted(f.Window,description),trackBounds=HideTeamNamesPainted(f.Window,track);
            Assert.True(input.Contains(titleBounds));Assert.True(input.Contains(descriptionBounds));Assert.True(input.Contains(trackBounds));
            Assert.True(titleBounds.Right<=trackBounds.Left);Assert.True(descriptionBounds.Right<=trackBounds.Left);
            void Stable(Mr7SelectionFrame frame)
            {
                Assert.Equal(input,frame.InputBounds);Assert.Equal(titleBounds,frame.TitleBounds);Assert.Equal(descriptionBounds,frame.DescriptionBounds);
                Assert.Equal(Matrix.Identity,frame.ParentTransform);Assert.Equal(Matrix.Identity,frame.ToggleTransform);
                Assert.Equal(Matrix.Identity,frame.TitleTransform);Assert.Equal(Matrix.Identity,frame.DescriptionTransform);
                Assert.True(frame.IconLoaded);Assert.True(frame.ToggleLoaded);Assert.True(frame.AttachedToOwner);
                Assert.Equal(0d,frame.Pulse);Assert.Equal(0d,frame.Turn);
            }
            void Authority(bool hidden,int expectedChanges)
            {
                Assert.Equal(hidden,toggle.IsChecked==true);Assert.Equal(hidden,symbol.IsActive);
                Assert.Equal(hidden?ToggleState.On:ToggleState.Off,provider.ToggleState);
                Assert.Equal(hidden?"HIDDEN":"ALLOWED",stateText.Text);
                Assert.Equal(hidden,HideTeamNamesValue(f));Assert.Equal(hidden,HideTeamNamesPreference(f));
                Assert.Equal(expectedChanges,changes);Assert.Equal(scores,Scores(f.Match));
                Assert.False(f.Match.ClockRunning);Assert.Equal(stopped,f.Match.ElapsedInQuarter);
                Stable(Read());Assert.All(HideTeamNamesProbes(input),p=>Assert.True(Within(toggle,f.Window.InputHitTest(p))));
                trace.Add(Read("post-input-synchronous-authority"));
            }
            void Stage(string next){stage=next;frames.Clear();}
            void Dispatch(Action action){dispatching=true;try{action();}finally{dispatching=false;}}
            async Task Observe(Func<bool> condition,string explanation)=>await Mr5FinalsObserveUntil(condition,explanation);
            async Task Finish(bool hidden)
            {
                await Observe(()=>Mr7SelectionEndpoint(Read(),hidden),"Current Names selection/slider/nameplates must finish naturally without forcing Reduce.");
                Mr7AssertSelectionEndpoint(Read(),hidden);
            }
            bool PartsMove()
            {
                var moving=frames.Where(x=>x.ThumbX>.01&&x.ThumbX<19.99).ToArray();
                var fill=frames.Where(x=>x.Fill>.001&&x.Fill<.999).ToArray();
                return moving.Length>=2&&moving.Max(x=>x.ThumbX)-moving.Min(x=>x.ThumbX)>.01
                    &&fill.Length>=2&&fill.Max(x=>x.Fill)-fill.Min(x=>x.Fill)>.001
                    &&frames.Any(x=>x.Selection>.001&&x.Selection<.999);
            }
            async Task Arrival(bool hidden)
            {
                if(!reduced)
                {
                    await Observe(PartsMove,"Production frames must show actual Names thumb/selection/fill progress, not a phantom click Pulse/Turn.");
                    trace.Add(Read("mid-nameplate-capture-request"));Capture(f.Window,$"mr7-names-mid-{width}-{hidden}");
                    await Finish(hidden);
                }
                else
                {
                    Mr7AssertSelectionEndpoint(Read(),hidden);
                    await Observe(()=>frames.Count>=4,"Reduced Names state must remain canonical on actual frames.");
                    Assert.All(frames,x=>Mr7AssertSelectionEndpoint(x,hidden));
                }
                Assert.All(frames,Stable);
            }
            Stage("initial-allowed");Authority(false,0);Mr7AssertSelectionEndpoint(Read(),false);
            Assert.True(Within(toggle,f.Window.FocusManager?.GetFocusedElement()));
            Stage("space-hidden");Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,1);await Arrival(true);
            Stage("provider-allowed");Dispatch(provider.Toggle);Authority(false,2);await Arrival(false);
            Stage("physical-press");Dispatch(()=>{f.Window.MouseMove(input.Center);f.Window.MouseDown(input.Center,MouseButton.Left);});Authority(false,2);
            if(!reduced)await Observe(()=>frames.Any(x=>x.ThumbScaleX>1.001&&x.ThumbScaleY<.999),"Physical Names press deforms only the thumb, never the text or full card.");
            else Mr7AssertSelectionEndpoint(Read(),false);
            Assert.All(frames,Stable);
            Stage("pointer-release-hidden");Dispatch(()=>f.Window.MouseUp(input.Center,MouseButton.Left));Authority(true,3);
            f.Window.MouseMove(new Point(1,1));await Arrival(true);
            Stage("allowed-before-reversals");Dispatch(provider.Toggle);Authority(false,4);
            if(!reduced)await Observe(PartsMove,"Observe a real current departure before reversal.");
            Stage("rapid-reversals");Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,5);
            Dispatch(provider.Toggle);Authority(false,6);Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,7);
            await Finish(true);Authority(true,7); // Natural current-state completion, before live Reduce.
            Stage("second-allowed-before-live-reduce");Dispatch(provider.Toggle);Authority(false,8);
            if(!reduced)await Observe(PartsMove,"Live Reduce must interrupt a witnessed new departure, not an already settled pose.");
            Dispatch(()=>Key(f.Window,PhysicalKey.Space));Authority(true,9);
            Stage("live-reduce-current-hidden");OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(OperatorMotion.Enabled);Authority(true,9);Mr7AssertSelectionEndpoint(Read(),true);
            await Observe(()=>frames.Count>=60,"Observe canonical current Names state after Reduce to catch obsolete spring/completion work.");
            Assert.All(frames,x=>{Mr7AssertSelectionEndpoint(x,true);Stable(x);});Authority(true,9);
            Stage("reduced-allowed");Dispatch(provider.Toggle);Authority(false,10);Mr7AssertSelectionEndpoint(Read(),false);
            await Observe(()=>frames.Count>=10,"Subsequent reduced Names input stays immediate and canonical.");
            Assert.All(frames,x=>{Mr7AssertSelectionEndpoint(x,false);Stable(x);});
            Assert.All(trace,x=>{Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);});
            Capture(f.Window,$"mr7-names-final-{width}-{reduced}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr7-names-frames-{width}-{reduced}",trace);
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
