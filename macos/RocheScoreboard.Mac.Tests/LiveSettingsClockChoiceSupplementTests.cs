// Outside draft only. Root marks the existing LiveSettingsClockChoiceTests
// declaration partial before adding this file. No new or private setup routes.
using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    private static string Mr4NonClockState(MatchManager match)
    {
        // ToState returns a detached copy. Normalize only the three fields this
        // real running-clock test intentionally changes; never write the model.
        var state=match.ToState();
        state.ElapsedInQuarterTicks=0; state.ClockMode=0; state.ClockRunning=false;
        return JsonSerializer.Serialize(state);
    }

    private static async Task Mr4Navigate(Window owner,PhysicalKey tab)
    {
        Key(owner,tab,RawInputModifiers.Control);
        await Settle(480); // Existing navigation trailing-pointer guard, not application delay.
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    [Trait("Evidence","MR4RunningContinuity")]
    public async Task PublicRunningClockChangesModeWithoutLosingElapsedOrScoreHistory(bool reduced)
    {
        await using var f=await AflFixture.Create(1280,850);
        await OpenClockSection(f,reduced);
        var up=VisibleChoice(f,"SettingsCountUpCard");
        var down=VisibleChoice(f,"SettingsCountdownCard");
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountUp"),up);
        Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountdown"),down);
        // Start in Countdown through the public choice. This avoids assigning a
        // different meaning to StartPause when count-up stoppage mode is enabled.
        await Tap(f.Window,down);
        Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
        string nonClock=Mr4NonClockState(f.Match);
        await Mr4Navigate(f.Window,PhysicalKey.Digit1);
        await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
        Assert.True(f.Match.ClockRunning);
        await Until(()=>f.Match.ElapsedInQuarter.TotalMilliseconds>=250,"The real public Start must run the stopwatch before a continuity claim.");
        await Mr4Navigate(f.Window,PhysicalKey.Digit4);
        await OpenClockSection(f,reduced);
        foreach(var (choice,mode) in new[]{(up,ClockMode.CountUp),(down,ClockMode.Countdown)})
        {
            TimeSpan before=f.Match.ElapsedInQuarter;
            var wall=Stopwatch.StartNew();
            await Tap(f.Window,choice);
            TimeSpan after=f.Match.ElapsedInQuarter;
            Assert.Equal(mode,f.Match.ClockMode); Assert.True(f.Match.ClockRunning);
            Assert.Equal(choice==up,up.IsChecked); Assert.Equal(choice==down,down.IsChecked);
            Assert.True(after>=before,$"Clock mode change cannot reset or rewind elapsed: before={before}, after={after}.");
            Assert.True(after-before<=wall.Elapsed+TimeSpan.FromMilliseconds(500),"Elapsed must not jump beyond actual elapsed wall time plus bounded observation tolerance.");
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));
        }
        // End in Countdown so the actual primary button pauses the match, not
        // its optional separate count-up stoppage timer.
        await Mr4Navigate(f.Window,PhysicalKey.Digit1);
        await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
        Assert.False(f.Match.ClockRunning);
        TimeSpan paused=f.Match.ElapsedInQuarter;
        await Settle(300);
        Assert.Equal(paused,f.Match.ElapsedInQuarter);
        Assert.Equal(nonClock,Mr4NonClockState(f.Match));
    }

    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","MR4ObservedChoiceMotion")]
    public async Task RealChoiceReversalAndLiveReduceSettleOnlyCurrentSelectionThenSurviveTabReturn(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var previousStyle=OperatorAppearance.MotionStyle;
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            await OpenClockSection(f,false);
            Assert.True(OperatorMotion.Enabled,"This observed-motion case requires an actual enabled normal-motion host.");
            var up=VisibleChoice(f,"SettingsCountUpCard"); var down=VisibleChoice(f,"SettingsCountdownCard");
            Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountUp"),up);
            Assert.Same(Named<RadioButton>(f.Window,"ClockModeCountdown"),down);
            var upIcon=Assert.Single(up.GetVisualDescendants().OfType<OperatorSymbol>());
            var downIcon=Assert.Single(down.GetVisualDescendants().OfType<OperatorSymbol>());
            Assert.Equal("clock-up",upIcon.Kind); Assert.Equal("clock-down",downIcon.Kind);
            Assert.False(OperatorControlMotion.GetEnabled(up));Assert.False(OperatorControlMotion.GetEnabled(down));
            Assert.False(CricketActionMotion.GetEnabled(up));Assert.False(CricketActionMotion.GetEnabled(down));
            string nonClock=Mr4NonClockState(f.Match);
            var downIconOrigin=downIcon.TranslatePoint(default,down);
            var upIconOrigin=upIcon.TranslatePoint(default,up);
            var downSize=down.Bounds.Size; var upSize=up.Bounds.Size;
            object Geometry(OperatorSymbol icon,RadioButton choice)=>new
            {
                iconOrigin=icon.TranslatePoint(default,choice),choice.Bounds,
                ancestors=icon.GetVisualAncestors().Prepend(icon).OfType<Visual>().Select(v=>new
                {type=v.GetType().Name,name=(v as Control)?.Name,v.Bounds,transform=v.RenderTransform?.Value.ToString(),origin=v.RenderTransformOrigin.ToString()}).ToArray(),
                cricket=CricketActionMotion.GetEnabled(choice),control=OperatorControlMotion.GetEnabled(choice),settings=SettingsControlMotion.GetEnabled(choice)
            };
            var geometryBefore=new{up=Geometry(upIcon,up),down=Geometry(downIcon,down)};
            await Tap(f.Window,down);
            Assert.Equal(ClockMode.Countdown,f.Match.ClockMode); Assert.True(down.IsChecked); Assert.False(up.IsChecked);
            Assert.True(downIcon.IsActive); Assert.True(downIcon.IsAnimating,
                "Observe a real choice-owned clock before claiming interruption/Reduce settlement; do not infer motion from the style alone.");
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));
            Capture(f.Window,$"mr4-observed-active-{width}");
            // Pointer activation must leave the actual native radio focused;
            // subsequent arrows are real keyboard input, not state assignments.
            Assert.Same(down,f.Window.FocusManager?.GetFocusedElement());
            Key(f.Window,PhysicalKey.ArrowLeft);
            Assert.True(up.IsChecked); Assert.False(down.IsChecked); Assert.Equal(ClockMode.CountUp,f.Match.ClockMode);
            Assert.True(upIcon.IsActive); Assert.True(upIcon.IsAnimating);
            Key(f.Window,PhysicalKey.ArrowRight);
            Assert.True(down.IsChecked); Assert.False(up.IsChecked); Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
            Assert.True(downIcon.IsAnimating);
            Assert.Equal(upSize,up.Bounds.Size); Assert.Equal(downSize,down.Bounds.Size);
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} geometryDirectory)
            {
                Directory.CreateDirectory(geometryDirectory);
                File.WriteAllText(Path.Combine(geometryDirectory,$"mr4-geometry-{width}.json"),JsonSerializer.Serialize(new{before=geometryBefore,after=new{up=Geometry(upIcon,up),down=Geometry(downIcon,down)}},new JsonSerializerOptions{WriteIndented=true}));
            }
            Assert.Equal(upIconOrigin,upIcon.TranslatePoint(default,up));
            Assert.Equal(downIconOrigin,downIcon.TranslatePoint(default,down));
            // Existing preference API is a controlled policy change, not a claim
            // of physically operating the preferences UI.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(upIcon.IsAnimating); Assert.False(downIcon.IsAnimating);
            Assert.Equal(0,OperatorChoiceMotion.GetProgress(up)); Assert.Equal(1,OperatorChoiceMotion.GetProgress(down));
            string selected=JsonSerializer.Serialize(f.Match.ToState());
            await Settle(800);
            Assert.False(upIcon.IsAnimating); Assert.False(downIcon.IsAnimating);
            Assert.Equal(selected,JsonSerializer.Serialize(f.Match.ToState()));
            Capture(f.Window,$"mr4-reduced-settled-{width}");
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            await Mr4Navigate(f.Window,PhysicalKey.Digit4);
            await OpenClockSection(f,true);
            Assert.Same(up,VisibleChoice(f,"SettingsCountUpCard")); Assert.Same(down,VisibleChoice(f,"SettingsCountdownCard"));
            Assert.True(down.IsChecked); Assert.False(up.IsChecked);
            Assert.False(upIcon.IsAnimating); Assert.False(downIcon.IsAnimating);
            Assert.Equal(selected,JsonSerializer.Serialize(f.Match.ToState()));
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));
        }
        finally{OperatorAppearance.ConfigureMotionStyle(previousStyle);}
    }
}
