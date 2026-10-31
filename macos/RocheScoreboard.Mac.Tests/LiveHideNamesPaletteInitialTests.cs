// OUTSIDE MR7 DRAFT: eight cases, uncompiled/unrun. Root owns integration.
// Four actual live420palette cases plus four real initial production-template mounts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using System.Diagnostics;
using Avalonia.Automation;
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
    [Trait("Evidence","MR7ActualNamesPalette")]
    public async Task ActualNamesPaletteHasReadableGlyphAndDescriptionOverItsRenderedSelection(AppTheme theme,bool selected)
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
            var toggle=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");
            await RevealHideTeamNames(f,toggle);
            var intendedTheme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.NamesToggle"));
            Assert.Same(intendedTheme,toggle.Theme);
            T Part<T>(string name) where T:Control=>Assert.Single(toggle.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
            symbol=Part<OperatorSymbol>("NamesSymbol");
            description=Part<TextBlock>("NamesDescription");
            symbolOpacity=symbol.Opacity;descriptionOpacity=description.Opacity;
            var basis=Part<Border>("NamesBase");
            var selection=Part<OperatorChoiceSurface>("NamesSelection");
            var baseBrush=Assert.IsAssignableFrom<ISolidColorBrush>(basis.Background);
            // Prove actual theme adaptation occurred on the live original view.
            Assert.Equal(Color.Parse(theme==AppTheme.Light?"#E1E1E8":"#29292D"),baseBrush.Color);
            Assert.Equal((byte)255,baseBrush.Color.A);Assert.Equal(1d,baseBrush.Opacity);
            Assert.Equal("hide-team-names",symbol.Kind);Assert.Equal(13d,description.FontSize);
            Assert.Equal("Removes name panels while keeping professional, digit-safe score sizing",description.Text);
            Assert.True(symbol.IsEffectivelyVisible);Assert.True(description.IsEffectivelyVisible);
            string fitDirectory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.GetTempPath();Directory.CreateDirectory(fitDirectory);
            string fitPrefix=Path.Combine(fitDirectory,$"mr7-names-fit-{theme}-{selected}");
            Capture(f.Window,$"mr7-names-fit-{theme}-{selected}");
            File.WriteAllText(fitPrefix+".json",JsonSerializer.Serialize(new{theme,requestedSelected=selected,actualSelected=toggle.IsChecked,description.Text,description.FontSize,description.Bounds,description.TextLayout.Width,description.TextLayout.WidthIncludingTrailingWhitespace,description.TextLayout.Height,lines=description.TextLayout.TextLines.Select(line=>new{line.Width,line.WidthIncludingTrailingWhitespace,line.HasCollapsed})},new JsonSerializerOptions{WriteIndented=true}));
            // Wrapped line-ending blanks may extend past the layout box but
            // paint no glyphs. Measure each visible run, without relaxing the
            // fit tolerance, font size, no-collapse or vertical-fit contract.
            Assert.True(description.TextLayout.Width<=description.Bounds.Width+.01);
            Assert.All(description.TextLayout.TextLines,line=>Assert.True(line.Width<=description.Bounds.Width+.01));
            Assert.DoesNotContain(description.TextLayout.TextLines,line=>line.HasCollapsed);
            Assert.True(description.TextLayout.Height<=description.Bounds.Height+.01);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            string scores=Scores(f.Match);
            int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            bool initial=toggle.IsChecked==true;
            if(initial!=selected)provider.Toggle();
            Assert.Equal(initial==selected?0:1,changes);
            Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,HideTeamNamesValue(f));Assert.Equal(selected,HideTeamNamesPreference(f));
            Assert.Equal(selected?1d:0d,selection.Progress);
            Assert.Equal(selected?1d:0d,symbol.FillProgress);
            Assert.Equal(1d,selection.OriginX);
            Assert.Equal(selected?"HIDDEN":"ALLOWED",Part<TextBlock>("NamesStateText").Text);
            Assert.Equal(scores,Scores(f.Match));
            // Keep the pointer away from the measured glyphs; keyboard focus
            // remains the original checkbox's visible focus, never a proxy.
            f.Window.MouseMove(new Point(1,1));
            await Settle(100);
            Rect input=FinalsPainted(f.Window,toggle);
            Assert.True(new Rect(f.Window.ClientSize).Contains(input));
            Assert.True(input.Contains(FinalsPainted(f.Window,description)));
            Assert.All(FinalsProbes(input),point=>Assert.True(Within(toggle,f.Window.InputHitTest(point))));

            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ??Path.Combine(Path.GetTempPath(),"roche-mr7-names-palette-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string prefix=Path.Combine(directory,$"mr7-names-palette-{theme}-{(selected?"on":"off")}-420");
            using var normal=Mr5PaletteCapture(f.Window,prefix+".png");
            var iconInk=Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground);
            var textInk=Assert.IsAssignableFrom<ISolidColorBrush>(description.Foreground);
            double iconVisualOpacity=Mr5PaletteVisualOpacity(symbol);
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
                var iconPixels=Mr5PalettePixels(f.Window,symbol,normal,backing,iconInk,iconVisualOpacity);
                var textPixels=Mr5PalettePixels(f.Window,description,normal,backing,textInk,textVisualOpacity);
                Assert.True(iconPixels.Count>=20,"Actual name/score glyph ink must be present in the normal/background difference.");
                Assert.True(textPixels.Count>=30,"Actual description glyph ink must be present in the normal/background difference.");
                if(!selected)
                {
                    string actualSolidBase=$"#{baseBrush.Color.R:X2}{baseBrush.Color.G:X2}{baseBrush.Color.B:X2}";
                    Assert.All(iconPixels.Concat(textPixels),pixel=>Assert.Equal(actualSolidBase,pixel.Background));
                }
                double strongestCoverage=iconPixels.Max(pixel=>pixel.MeasuredCoverage);
                var strongIcon=iconPixels.Where(pixel=>pixel.MeasuredCoverage>=strongestCoverage-.025).ToArray();
                // Compare fully painted stroke/fill pixels rather than AA edges.
                // There must be a set of such pixels, not one fortunate sample.
                Assert.True(strongIcon.Length>=12,"A meaningful set of strongly covered name/score glyph pixels is required for a measured icon contrast assertion.");
                Assert.InRange(strongestCoverage,.2,1.04);
                double iconMinimum=strongIcon.Min(pixel=>pixel.PaintedContrast);
                double textMinimum=textPixels.Min(pixel=>pixel.IntendedTextContrast);
                var report=new
                {
                    scope="Linux headless software rendering; original live Settings HideTeamNames template",
                    theme=theme.ToString(),selected,viewport=f.Window.ClientSize,
                    baseline=new{baseColor=baseBrush.Color.ToString(),baseBrush.Opacity},
                    symbol=new{foreground=iconInk.Color.ToString(),sourceAlpha=iconInk.Color.A,iconInk.Opacity,iconVisualOpacity,
                        renderedCoverageMaximum=strongestCoverage,strongPixelCount=strongIcon.Length,
                        strongPixelMinimumContrast=iconMinimum,
                        sourceFullInkMinimum=iconPixels.Min(pixel=>pixel.IntendedTextContrast),
                        sourceGlyphInkMinimum=iconPixels.Min(pixel=>pixel.IntendedTextContrast)},
                    description=new{description.FontSize,foreground=textInk.Color.ToString(),sourceAlpha=textInk.Color.A,textInk.Opacity,
                        textVisualOpacity,glyphPixelCount=textPixels.Count,intendedInkMinimumContrast=textMinimum},
                    backingExplanation="Same rendered view with only name/score glyph and description paint temporarily at opacity zero; actual blue alpha gradient/liquid geometry retained.",
                    iconPixels,textPixels
                };
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,HideTeamNamesValue(f));Assert.Equal(selected,HideTeamNamesPreference(f));
                Assert.Equal(initial==selected?0:1,changes);Assert.Equal(scores,Scores(f.Match));
                Assert.Equal(input,FinalsPainted(f.Window,toggle));
                Assert.True(iconMinimum>=3,
                    $"{theme} HideTeamNames {(selected?"ON":"OFF")} actual strongly painted icon contrast {iconMinimum:F3}:1 must be >=3:1. Evidence: {prefix}-contrast.json");
                Assert.True(textMinimum>=4.5,
                    $"{theme} HideTeamNames {(selected?"ON":"OFF")} 13px description intended ink contrast over actual glyph backing {textMinimum:F3}:1 must be >=4.5:1. Evidence: {prefix}-contrast.json");
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

    private sealed record Mr7NamesInitialFrame(double Milliseconds,bool? Checked,double Fill,double Pulse,double Turn,
        double ThumbX,double ThumbScaleX,double ThumbScaleY,double Selection,string? Label);

    [AvaloniaTheory]
    [InlineData(false,"Expressive")][InlineData(true,"Expressive")]
    [InlineData(false,"Calm")][InlineData(true,"Calm")]
    [Trait("Evidence","MR7InitialNamesTemplate")]
    public async Task InitiallyAuthoredProductionNamesTemplateMountsAtItsCurrentEndpointWithoutCelebration(bool selected,string policy)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;
        bool previousReduce=OperatorAppearance.ReduceMotion;bool? previousOverride=OperatorMotion.AnimationOverride;
        Window? host=null;EventHandler? observer=null;
        var samples=new List<Mr7NamesInitialFrame>();var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);OperatorMotion.AnimationOverride=null;
            Assert.True(OperatorMotion.Enabled);
            string scores=Scores(f.Match);bool oldLive=HideTeamNamesValue(f),oldPreference=HideTeamNamesPreference(f);
            var original=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");
            var theme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(original,"ControlCentre.NamesToggle"));
            // This is a real production-template mount, not a saved-file/load
            // simulation. IsChecked is authored BEFORE the first Show/Loaded.
            var restored=new CheckBox{IsChecked=selected,Theme=theme};
            Assert.False(restored.IsLoaded);Assert.Empty(restored.GetVisualChildren());
            int changes=0;restored.IsCheckedChanged+=(_,_)=>changes++;
            host=new Window{Width=520,Height=220,Content=restored};
            observer=(_,_)=>
            {
                var parts=restored.GetVisualDescendants().OfType<Control>().ToArray();
                var symbol=parts.OfType<OperatorSymbol>().SingleOrDefault(x=>x.Name=="NamesSymbol");
                var thumb=parts.OfType<Border>().SingleOrDefault(x=>x.Name=="NamesThumb");
                var selection=parts.OfType<OperatorChoiceSurface>().SingleOrDefault(x=>x.Name=="NamesSelection");
                var label=parts.OfType<TextBlock>().SingleOrDefault(x=>x.Name=="NamesStateText");
                if(symbol is null||thumb is null||selection is null||label is null)return;
                var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
                var shift=Assert.Single(group.Children.OfType<TranslateTransform>());
                var scale=Assert.Single(group.Children.OfType<ScaleTransform>());
                samples.Add(new(watch.Elapsed.TotalMilliseconds,restored.IsChecked,symbol.FillProgress,
                    Mr5FinalsDiagnostic(symbol,"Pulse"),Mr5FinalsDiagnostic(symbol,"Turn"),
                    shift.X,scale.ScaleX,scale.ScaleY,selection.Progress,label.Text));
            };
            NativeRendering.Rendering+=observer;
            host.Show();
            await Mr5FinalsObserveUntil(()=>samples.Count>=45,"The actual production Names template must mount and expose at least45real rendering callbacks.");
            Assert.True(restored.IsLoaded);Assert.Same(theme,restored.Theme);Assert.Equal(selected,restored.IsChecked==true);
            Assert.Equal(0,changes);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(restored));
            Assert.Equal(selected?ToggleState.On:ToggleState.Off,provider.ToggleState);
            Assert.All(samples,x=>
            {
                Assert.Equal(selected,x.Checked==true);Assert.Equal(selected?1d:0d,x.Fill);
                Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);Assert.Equal(selected?20d:0d,x.ThumbX);
                Assert.Equal(1d,x.ThumbScaleX);Assert.Equal(1d,x.ThumbScaleY);Assert.Equal(selected?1d:0d,x.Selection);
                Assert.Equal(selected?"HIDDEN":"ALLOWED",x.Label);
            });
            Assert.Equal(scores,Scores(f.Match));Assert.Equal(oldLive,HideTeamNamesValue(f));Assert.Equal(oldPreference,HideTeamNamesPreference(f));
            Capture(host,$"mr7-names-initial-{selected}-{policy}");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace($"mr7-names-initial-{selected}-{policy}",samples);host?.Close();
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
