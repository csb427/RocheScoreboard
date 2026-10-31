// Read-only preparation draft. Root owns copying, compilation and execution.
// Four software-rendered captures of the original, live Settings checkbox.
using System;
using System.Collections.Generic;
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
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,false)][InlineData(AppTheme.Dark,true)]
    [InlineData(AppTheme.Light,false)][InlineData(AppTheme.Light,true)]
    [Trait("Evidence","MR6ActualStoppagePalette")]
    public async Task ActualStoppagePaletteHasReadableClockAndDescriptionOverItsRenderedSelection(AppTheme theme,bool selected)
    {
        await using var f=await AflFixture.Create(420,320);
        var app=(RocheScoreboard.Mac.App)Application.Current!;
        var previousTheme=app.ThemeService.CurrentTheme;
        bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        OperatorSymbol? symbol=null;TextBlock? description=null;
        double symbolOpacity=1,descriptionOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);
            await OpenClockSection(f,true);
            Assert.Equal(theme,app.ThemeService.CurrentTheme);
            Assert.Null(OperatorMotion.AnimationOverride);Assert.False(OperatorMotion.Enabled);
            var toggle=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
            await Mr6Reveal(f,toggle);
            var intendedTheme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.StoppageToggle"));
            Assert.Same(intendedTheme,toggle.Theme);
            T Part<T>(string name) where T:Control=>Assert.Single(toggle.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
            symbol=Part<OperatorSymbol>("StoppageSymbol");
            description=Part<TextBlock>("StoppageDescription");
            symbolOpacity=symbol.Opacity;descriptionOpacity=description.Opacity;
            var basis=Part<Border>("StoppageBase");
            var selection=Part<OperatorChoiceSurface>("StoppageSelection");
            var baseBrush=Assert.IsAssignableFrom<ISolidColorBrush>(basis.Background);
            // Prove actual theme adaptation occurred on the live original view.
            Assert.Equal(Color.Parse(theme==AppTheme.Light?"#E1E1E8":"#29292D"),baseBrush.Color);
            Assert.Equal((byte)255,baseBrush.Color.A);Assert.Equal(1d,baseBrush.Opacity);
            Assert.Equal("clock-up",symbol.Kind);Assert.Equal(13d,description.FontSize);
            Assert.True(symbol.IsEffectivelyVisible);Assert.True(description.IsEffectivelyVisible);
            Assert.True(description.TextLayout.WidthIncludingTrailingWhitespace<=description.Bounds.Width+.01);
            Assert.DoesNotContain(description.TextLayout.TextLines,line=>line.HasCollapsed);
            Assert.True(description.TextLayout.Height<=description.Bounds.Height+.01);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            string scores=Scores(f.Match);
            int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            bool initial=toggle.IsChecked==true;
            if(initial!=selected)provider.Toggle();
            Assert.Equal(initial==selected?0:1,changes);
            Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,Mr6Read<bool>(f,"_showControlPanelCountdown"));
            Assert.Equal(selected?1d:0d,selection.Progress);
            Assert.Equal(selected?1d:0d,symbol.FillProgress);
            Assert.Equal(scores,Scores(f.Match));
            // Keep the pointer away from the measured glyphs; keyboard focus
            // remains the original checkbox's visible focus, never a proxy.
            f.Window.MouseMove(new Point(1,1));
            await Settle(100);
            Rect input=FinalsPainted(f.Window,toggle);
            Assert.All(FinalsProbes(input),point=>Assert.True(Within(toggle,f.Window.InputHitTest(point))));

            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ??Path.Combine(Path.GetTempPath(),"roche-mr6-stoppage-palette-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string prefix=Path.Combine(directory,$"mr6-stoppage-palette-{theme}-{(selected?"on":"off")}-420");
            using var normal=Mr5PaletteCapture(f.Window,prefix+".png");
            var starInk=Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground);
            var textInk=Assert.IsAssignableFrom<ISolidColorBrush>(description.Foreground);
            double starVisualOpacity=Mr5PaletteVisualOpacity(symbol);
            double textVisualOpacity=Mr5PaletteVisualOpacity(description);
            symbolOpacity=symbol.Opacity;descriptionOpacity=description.Opacity;
            SKBitmap backing;
            try
            {
                // Controlled, reversible paint isolation only. Layout, the
                // original checkbox, selected alpha gradient, liquid geometry,
                // theme and opaque base remain exactly as in the normal frame.
                symbol.Opacity=0;description.Opacity=0;
                await Settle(40);
                backing=Mr5PaletteCapture(f.Window,prefix+"-backing-only.png");
            }
            finally
            {
                symbol.Opacity=symbolOpacity;description.Opacity=descriptionOpacity;
            }
            using(backing)
            {
                Assert.Equal(normal.Width,backing.Width);Assert.Equal(normal.Height,backing.Height);
                var starPixels=Mr5PalettePixels(f.Window,symbol,normal,backing,starInk,starVisualOpacity);
                var textPixels=Mr5PalettePixels(f.Window,description,normal,backing,textInk,textVisualOpacity);
                Assert.True(starPixels.Count>=20,"Actual clock ink must be present in the normal/background difference.");
                Assert.True(textPixels.Count>=30,"Actual description glyph ink must be present in the normal/background difference.");
                if(!selected)
                {
                    string actualSolidBase=$"#{baseBrush.Color.R:X2}{baseBrush.Color.G:X2}{baseBrush.Color.B:X2}";
                    Assert.All(starPixels.Concat(textPixels),pixel=>Assert.Equal(actualSolidBase,pixel.Background));
                }
                double strongestCoverage=starPixels.Max(pixel=>pixel.MeasuredCoverage);
                var strongStar=starPixels.Where(pixel=>pixel.MeasuredCoverage>=strongestCoverage-.025).ToArray();
                // Compare fully painted stroke/fill pixels rather than AA edges.
                // There must be a set of such pixels, not one fortunate sample.
                Assert.True(strongStar.Length>=12,"A meaningful set of strongly covered clock pixels is required for a measured icon contrast assertion.");
                Assert.InRange(strongestCoverage,.2,1.04);
                double starMinimum=strongStar.Min(pixel=>pixel.PaintedContrast);
                double textMinimum=textPixels.Min(pixel=>pixel.IntendedTextContrast);
                var report=new
                {
                    scope="Linux headless software rendering; original live Settings Stoppage template",
                    theme=theme.ToString(),selected,viewport=f.Window.ClientSize,
                    baseline=new{baseColor=baseBrush.Color.ToString(),baseBrush.Opacity},
                    star=new{foreground=starInk.Color.ToString(),starInk.Opacity,starVisualOpacity,
                        renderedCoverageMaximum=strongestCoverage,strongPixelCount=strongStar.Length,
                        strongPixelMinimumContrast=starMinimum,
                        sourceFullInkMinimum=starPixels.Min(pixel=>pixel.IntendedTextContrast),
                        sourceGlyphInkMinimum=starPixels.Min(pixel=>pixel.IntendedTextContrast)},
                    description=new{description.FontSize,foreground=textInk.Color.ToString(),textInk.Opacity,
                        textVisualOpacity,glyphPixelCount=textPixels.Count,intendedInkMinimumContrast=textMinimum},
                    backingExplanation="Same rendered view with only clock/description paint temporarily at opacity zero; actual blue alpha gradient/liquid geometry retained.",
                    starPixels,textPixels
                };
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,Mr6Read<bool>(f,"_showControlPanelCountdown"));
                Assert.Equal(initial==selected?0:1,changes);Assert.Equal(scores,Scores(f.Match));
                Assert.Equal(input,FinalsPainted(f.Window,toggle));
                Assert.True(starMinimum>=3,
                    $"{theme} Stoppage {(selected?"ON":"OFF")} actual strongly painted clock contrast {starMinimum:F3}:1 must be >=3:1. Evidence: {prefix}-contrast.json");
                Assert.True(textMinimum>=4.5,
                    $"{theme} Stoppage {(selected?"ON":"OFF")} 13px description intended ink contrast over actual glyph backing {textMinimum:F3}:1 must be >=4.5:1. Evidence: {prefix}-contrast.json");
            }
        }
        finally
        {
            if(symbol is not null)symbol.Opacity=symbolOpacity;
            if(description is not null)description.Opacity=descriptionOpacity;
            app.ThemeService.SetTheme(previousTheme);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
