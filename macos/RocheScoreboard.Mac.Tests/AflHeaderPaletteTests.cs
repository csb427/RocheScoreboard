// OUTSIDE DRAFT: UNCOMPILED/UNRUN. Four cases only. Root integrates after full freeze.
// Extends real existing AFL6–1 fixture and actual rendered palette helpers in
// LiveSettingsFinalsPaletteTests.cs. No nominal background colour substitute.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Light,1280,850)]
    [InlineData(AppTheme.Dark,420,320)][InlineData(AppTheme.Light,420,320)]
    [Trait("Evidence","AflHeaderActualPalette")]
    public async Task ActualOperatorHeaderRemainsReadableInBothPalettesAndPreservesLiveScoreFormat(AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var priorTheme=app.ThemeService.CurrentTheme;
        var priorReduce=OperatorAppearance.ReduceMotion;var priorOverride=OperatorMotion.AnimationOverride;
        var savedOpacity=new Dictionary<TextBlock,double>();
        try
        {
            app.ThemeService.SetTheme(theme);await OpenClockSection(f,true);await Settle(600);
            Assert.Equal(theme,app.ThemeService.CurrentTheme);Assert.False(f.Match.ClockRunning);
            Assert.Equal(6,f.Match.HomeTotal);Assert.Equal(1,f.Match.AwayTotal);Assert.Equal(2,f.Match.Events.Count);
            var strip=Named<Border>(f.Window,"MatchStripBorder");Assert.True(strip.IsEffectivelyVisible);
            var primaryNames=new[]{"HeaderQuarterClock","HeaderScore","HeaderHomeAbbr","HeaderAwayAbbr"};
            var primary=primaryNames.Select(name=>Named<TextBlock>(f.Window,name)).ToArray();
            var clock=primary[0];var score=primary[1];
            // Exact existing operational format, including two spaces/en dash.
            Assert.Equal("Q1  00:00",clock.Text);Assert.Equal("6 – 1",score.Text);
            Assert.Equal(f.Match.HomeAbbr.ToUpper(),primary[2].Text);Assert.Equal(f.Match.AwayAbbr.ToUpper(),primary[3].Text);
            Assert.Equal("1.0.6",Named<TextBlock>(f.Window,"HomeScoreDisplay").Text);
            Assert.Equal("0.1.1",Named<TextBlock>(f.Window,"AwayScoreDisplay").Text);
            string state=JsonSerializer.Serialize(f.Match.ToState());
            foreach(var value in primary)
            {
                Assert.True(value.IsEffectivelyVisible);Assert.True(new Rect(f.Window.ClientSize).Contains(FinalsPainted(f.Window,value)));
                Assert.True(value.TextLayout.WidthIncludingTrailingWhitespace<=value.Bounds.Width+.01,value.Name+" full glyph run must fit unchanged font.");
                Assert.DoesNotContain(value.TextLayout.TextLines,line=>line.HasCollapsed);
            }
            for(int i=0;i<primary.Length;i++)for(int j=i+1;j<primary.Length;j++)
                Assert.False(FinalsPainted(f.Window,primary[i]).Intersects(FinalsPainted(f.Window,primary[j])),"Primary header glyph regions must not overlap.");
            // Optional currently visible stationary readouts remain in header scope.
            // Deliberately exclude child Button templates and unrelated scorecards.
            var prefixLabels=Named<Border>(f.Window,"HeaderOutputBadge").GetVisualDescendants().OfType<TextBlock>()
                .Where(text=>text.Text=="Output · "&&text.IsEffectivelyVisible).ToArray();
            if(Named<Border>(f.Window,"HeaderOutputBadge").IsEffectivelyVisible)Assert.Single(prefixLabels);
            var targets=primary.Concat(prefixLabels).Concat(new[]{"HeaderOutputText","HeaderMarginText","LastEventText","OperatorModeBadgeText","StatusText"}
                .Select(name=>Named<TextBlock>(f.Window,name)).Where(text=>text.IsEffectivelyVisible&&!string.IsNullOrWhiteSpace(text.Text))).ToArray();
            foreach(var text in targets)savedOpacity[text]=text.Opacity;
            var inks=targets.ToDictionary(text=>text,text=>Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground));
            var visualOpacities=targets.ToDictionary(text=>text,Mr5PaletteVisualOpacity);
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-afl-header-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string prefix=Path.Combine(dir,$"afl-header-palette-{theme}-{width}");
            f.Window.MouseMove(new Point(3,Math.Max(1,f.Window.ClientSize.Height-3)));await Settle(50);
            using var normal=Mr5PaletteCapture(f.Window,prefix+".png");
            SKBitmap backing;
            try
            {
                // Reversible PAINT isolation only: same material, backdrop,
                // opacity, text layout, header geometry and current match.
                foreach(var text in targets)text.Opacity=0;
                await Settle(40);backing=Mr5PaletteCapture(f.Window,prefix+"-backing-only.png");
            }
            finally{foreach(var pair in savedOpacity)pair.Key.Opacity=pair.Value;}
            using(backing)
            {
                Assert.Equal(normal.Width,backing.Width);Assert.Equal(normal.Height,backing.Height);
                var observations=new List<object>();var failures=new List<string>();
                foreach(var text in targets)
                {
                    var pixels=Mr5PalettePixels(f.Window,text,normal,backing,inks[text],visualOpacities[text]);
                    // WCAG large text:24px regular OR>=18.667px bold. Semibold
                    //23px compact clock is held conservatively to4.5 here.
                    double required=text.FontSize>=24||(text.FontSize>=18.667&&text.FontWeight==FontWeight.Bold)?3:4.5;
                    if(pixels.Count<12){failures.Add(text.Name+" lacks a witnessed glyph-pixel set");continue;}
                    double minimum=pixels.Min(p=>p.IntendedTextContrast);
                    double strongest=pixels.Max(p=>p.MeasuredCoverage);
                    var strong=pixels.Where(p=>p.MeasuredCoverage>=strongest-.025).ToArray();
                    observations.Add(new{text.Name,text.Text,text.FontSize,font=text.FontFamily.ToString(),fontWeight=text.FontWeight.ToString(),bounds=FinalsPainted(f.Window,text),sourceInk=inks[text].Color.ToString(),brushOpacity=inks[text].Opacity,visualOpacity=visualOpacities[text],required,intendedInkMinimumContrast=minimum,glyphPixelCount=pixels.Count,strongestCoverage=strongest,strongPixelCount=strong.Length,strongPixelMinimumContrast=strong.Min(p=>p.PaintedContrast),pixels});
                    if(minimum<required)failures.Add($"{text.Name} actual backing contrast {minimum:F3}:1 < {required}:1");
                }
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(new{scope="Linux headless software rendered operator header; actual normal/backing frame pair",theme=theme.ToString(),width,height,f.Window.ClientSize,observations,failures},new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(state,JsonSerializer.Serialize(f.Match.ToState()));
                Assert.Equal("Q1  00:00",clock.Text);Assert.Equal("6 – 1",score.Text);
                Assert.True(failures.Count==0,string.Join("; ",failures)+". Evidence: "+prefix+"-contrast.json");
                // Mounted readouts follow live theme changes without rebuilding
                // the view or changing accepted score/text state.
                var adaptive=primary.Concat(prefixLabels).Concat(new[]{Named<TextBlock>(f.Window,"HeaderOutputText"),Named<TextBlock>(f.Window,"LastEventText")}
                    .Where(value=>value.IsEffectivelyVisible)).ToArray();
                var accepted=adaptive.ToDictionary(value=>value,value=>Assert.IsAssignableFrom<ISolidColorBrush>(value.Foreground).Color);
                app.ThemeService.SetTheme(theme==AppTheme.Dark?AppTheme.Light:AppTheme.Dark);await Settle(80);
                foreach(var value in adaptive)Assert.NotEqual(accepted[value],Assert.IsAssignableFrom<ISolidColorBrush>(value.Foreground).Color);
                app.ThemeService.SetTheme(theme);await Settle(80);
                foreach(var value in adaptive)Assert.Equal(accepted[value],Assert.IsAssignableFrom<ISolidColorBrush>(value.Foreground).Color);
                Assert.Equal(state,JsonSerializer.Serialize(f.Match.ToState()));
                Assert.Equal("Q1  00:00",clock.Text);Assert.Equal("6 – 1",score.Text);

            }
        }
        finally
        {
            foreach(var pair in savedOpacity)pair.Key.Opacity=pair.Value;
            app.ThemeService.SetTheme(priorTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,priorReduce);OperatorMotion.AnimationOverride=priorOverride;
        }
    }
}
