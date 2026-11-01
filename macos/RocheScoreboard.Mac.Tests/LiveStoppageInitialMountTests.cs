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
    [AvaloniaTheory]
    [InlineData("Expressive")][InlineData("Calm")]
    [Trait("Evidence","MR6StoppageInitiallySelectedTemplate")]
    public async Task InitiallySelectedProductionStoppageTemplateMountsWithoutCelebration(string policy)
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
            var original=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
            string scores=Scores(f.Match);
            var theme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(original,"ControlCentre.StoppageToggle"));
            var restored=new CheckBox{IsChecked=true,Theme=theme};
            int changes=0;restored.IsCheckedChanged+=(_,_)=>changes++;
            host=new Window{Width=520,Height=200,Content=restored};
            // Register before Show so the first rendering callback after the
            // real Loaded/Attach route is included, not merely a settled tail.
            observer=(_,_)=>
            {
                var star=restored.GetVisualDescendants().OfType<OperatorSymbol>().SingleOrDefault(x=>x.Name=="StoppageSymbol");
                if(star is not null)samples.Add(new("initial-selected-mount",observationClock.Elapsed.TotalMilliseconds,
                    star.FillProgress,Mr5FinalsDiagnostic(star,"Pulse"),Mr5FinalsDiagnostic(star,"Turn")));
            };
            NativeRendering.Rendering+=observer;
            host.Show();
            await Mr5FinalsObserveUntil(()=>samples.Count>=45,"Initially selected production template must mount and be observed on actual rendering frames.");
            Assert.Same(theme,restored.Theme);
            Assert.True(restored.IsChecked);Assert.Equal(0,changes);
            Assert.All(samples,x=>{Assert.Equal(1d,x.Fill);Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);});
            var thumb=Mr5FinalsPart<Avalonia.Controls.Shapes.Ellipse>(restored,"StoppageThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
            Assert.Equal(20d,Assert.Single(group.Children.OfType<TranslateTransform>()).X);
            Assert.Equal(1d,Mr5FinalsPart<OperatorChoiceSurface>(restored,"StoppageSelection").Progress);
            Assert.Equal("ON",Mr5FinalsPart<TextBlock>(restored,"StoppageStateText").Text);
            Assert.Equal(scores,Scores(f.Match));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr6-stoppage-initial-selected-{policy}",samples);
            host?.Close();
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
