// OUTSIDE DRAFT ONLY:2cases, UNCOMPILED/UNRUN. Integrate after full freeze.
// Uses actual countdown public route to avoid count-up stoppage semantics.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory][InlineData(AppTheme.Dark)][InlineData(AppTheme.Light)]
    [Trait("Evidence","AflLiveStatusActualPalette")]
    public async Task ActualPublicRunningClockShowsReadableLiveStatusWithoutChangingScoreOrClockContract(AppTheme theme)
    {
        await using var f=await AflFixture.Create(1280,850);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var previousTheme=app.ThemeService.CurrentTheme;
        var previousReduce=OperatorAppearance.ReduceMotion;var previousOverride=OperatorMotion.AnimationOverride;
        TextBlock? text=null;double priorOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);await OpenClockSection(f,true);
            var down=Named<RadioButton>(f.Window,"ClockModeCountdown");
            Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(down)).Select();
            Assert.True(down.IsChecked);Assert.Equal(ClockMode.Countdown,f.Match.ClockMode);
            Assert.False(f.Match.ClockRunning);Assert.Equal(6,f.Match.HomeTotal);Assert.Equal(1,f.Match.AwayTotal);Assert.Equal(2,f.Match.Events.Count);
            string nonClock=Mr4NonClockState(f.Match);var duration=f.Match.QuarterDuration;
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));
            Assert.True(f.Match.ClockRunning,"Physical public Start must accept immediately.");
            await Until(()=>f.Match.ElapsedInQuarter.TotalMilliseconds>=250,"Actual stopwatch must advance before LIVE readability is measured.");
            text=Named<TextBlock>(f.Window,"StatusText");var badge=Named<Border>(f.Window,"StatusBadge");
            await Until(()=>text.IsEffectivelyVisible&&text.Text=="LIVE","LIVE must come from actual public running state, never seeded text.");
            await Settle(600);Assert.True(badge.IsEffectivelyVisible);Assert.Equal("LIVE",text.Text);
            Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.Equal(duration,f.Match.QuarterDuration);
            var ink=Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);var baseBrush=Assert.IsAssignableFrom<ISolidColorBrush>(badge.Background);
            Assert.Equal((byte)255,baseBrush.Color.A);Assert.Equal(1d,baseBrush.Opacity);
            double visualOpacity=Mr5PaletteVisualOpacity(text);Assert.Equal(1d,visualOpacity,3);
            priorOpacity=text.Opacity;var before=f.Match.ElapsedInQuarter;
            f.Window.MouseMove(new Point(3,f.Window.ClientSize.Height-3));
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-afl-live-status-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string prefix=Path.Combine(dir,$"afl-live-status-{theme}-1280");
            using var normal=Mr5PaletteCapture(f.Window,prefix+".png");
            SKBitmap backing;
            try{text.Opacity=0;await Settle(40);backing=Mr5PaletteCapture(f.Window,prefix+"-backing-only.png");}
            finally{text.Opacity=priorOpacity;}
            using(backing)
            {
                var pixels=Mr5PalettePixels(f.Window,text,normal,backing,ink,visualOpacity);
                Assert.True(pixels.Count>=12,"Actual LIVE glyphs must be witnessed in normal/backing difference.");
                string actualBase=$"#{baseBrush.Color.R:X2}{baseBrush.Color.G:X2}{baseBrush.Color.B:X2}";
                Assert.All(pixels,p=>Assert.Equal(actualBase,p.Background));
                double minimum=pixels.Min(p=>p.IntendedTextContrast);
                double strongest=pixels.Max(p=>p.MeasuredCoverage);var strong=pixels.Where(p=>p.MeasuredCoverage>=strongest-.025).ToArray();
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(new{scope="Actual public Countdown Start; Linux headless software rendering of 11px LIVE status",theme=theme.ToString(),text.Text,text.FontSize,text.Bounds,foreground=ink.Color.ToString(),ink.Opacity,visualOpacity,backing=actualBase,glyphPixelCount=pixels.Count,strongestCoverage=strongest,strongPixelCount=strong.Length,strongPaintedMinimum=strong.Min(p=>p.PaintedContrast),intendedInkMinimum=minimum,elapsedBefore=before,elapsedAfter=f.Match.ElapsedInQuarter,pixels},new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(nonClock,Mr4NonClockState(f.Match));Assert.True(f.Match.ClockRunning);Assert.True(f.Match.ElapsedInQuarter>=before);
                // Publicprimarybutton still pauses this real Countdown clock.
                await Tap(f.Window,Named<Button>(f.Window,"StartPauseButton"));Assert.False(f.Match.ClockRunning);
                var paused=f.Match.ElapsedInQuarter;await Settle(100);Assert.Equal(paused,f.Match.ElapsedInQuarter);Assert.Equal(nonClock,Mr4NonClockState(f.Match));
                Assert.True(minimum>=4.5,$"{theme} 11px LIVE intended ink contrast on actual opaque badge {minimum:F3}:1 must be >=4.5. Evidence: {prefix}-contrast.json");
            }
        }
        finally
        {
            if(text is not null)text.Opacity=priorOpacity;
            app.ThemeService.SetTheme(previousTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
