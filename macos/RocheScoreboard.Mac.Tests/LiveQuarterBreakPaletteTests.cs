// Four actual original-template palette/fit regressions; native desktop acceptance remains separate.
// Root owns all integration/build/test/source/ledger writes. This extends existing
// actual normal/backing helpers; Names/Finals results are not Quarter evidence.
using System;
using System.Collections.Generic;
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
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,false)][InlineData(AppTheme.Dark,true)]
    [InlineData(AppTheme.Light,false)][InlineData(AppTheme.Light,true)]
    [Trait("Evidence","QuarterBreakActualPalette")]
    public async Task ActualQuarterBreakTemplateHasReadableIconAndCopyAtCompactSelectedAndUnselectedStates(AppTheme theme,bool selected)
    {
        await using var f=await AflFixture.Create(420,320);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var oldTheme=app.ThemeService.CurrentTheme;
        bool oldReduce=OperatorAppearance.ReduceMotion;bool? oldOverride=OperatorMotion.AnimationOverride;
        OperatorSymbol? icon=null;double iconOpacity=1;var originalOpacity=new Dictionary<TextBlock,double>();
        try
        {
            app.ThemeService.SetTheme(theme);await OpenBreakCardSection(f,true);
            Assert.Equal(theme,app.ThemeService.CurrentTheme);Assert.Null(OperatorMotion.AnimationOverride);Assert.False(OperatorMotion.Enabled);
            var toggle=Named<CheckBox>(f.Window,"SettingsBreakScreenToggle");await RevealBreakCardActor(f,toggle);
            var actualTheme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.QuarterBreakToggle"));Assert.Same(actualTheme,toggle.Theme);
            T Part<T>(string name) where T:Control=>Assert.Single(toggle.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
            icon=Part<OperatorSymbol>("QuarterBreakSymbol");iconOpacity=icon.Opacity;
            var title=Part<TextBlock>("QuarterBreakTitle");var description=Part<TextBlock>("QuarterBreakDescription");var label=Part<TextBlock>("QuarterBreakStateText");
            var texts=new[]{title,description,label};foreach(var text in texts)originalOpacity[text]=text.Opacity;
            var basis=Part<Border>("QuarterBreakBase");var selection=Part<OperatorChoiceSurface>("QuarterBreakSelection");
            var actualBase=Assert.IsAssignableFrom<ISolidColorBrush>(basis.Background);
            // This only proves requested theme adaptation. All contrast below
            // uses ACTUAL normal/backing pixels, including selected alpha layers.
            Assert.Equal(Color.Parse(theme==AppTheme.Light?"#E1E1E8":"#29292D"),actualBase.Color);
            Assert.Equal((byte)255,actualBase.Color.A);Assert.Equal(1d,actualBase.Opacity);
            Assert.Equal("quarter-break",icon.Kind);Assert.Equal(15d,title.FontSize);Assert.Equal(13d,description.FontSize);
            Assert.Equal("Quarter-break screens",title.Text);
            Assert.Equal("At quarter end, show the score first, then cycle match analysis.",description.Text);
            Assert.True(icon.IsEffectivelyVisible);Assert.All(texts,text=>Assert.True(text.IsEffectivelyVisible));
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            string model=Scores(f.Match),otherPreferences=BreakCardOtherPreferences();object?[] presentation=BreakCardPresentation(f);
            bool initial=toggle.IsChecked==true;int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            if(initial!=selected)provider.Toggle();
            void Authority()
            {
                Assert.Equal(initial==selected?0:1,changes);Assert.Equal(selected,toggle.IsChecked==true);
                Assert.Equal(selected?ToggleState.On:ToggleState.Off,provider.ToggleState);
                Assert.Equal(selected,BreakCardValue(f));Assert.Equal(selected,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
                Assert.Equal(selected?1d:0d,selection.Progress);Assert.Equal(1d,selection.OriginX);Assert.Equal(selected?1d:0d,icon.FillProgress);
                Assert.Equal(selected?"ON":"OFF",label.Text);Assert.Equal(model,Scores(f.Match));
                Assert.Equal(otherPreferences,BreakCardOtherPreferences());Assert.Equal(presentation,BreakCardPresentation(f));
            }
            Authority();f.Window.MouseMove(new Point(1,1));await Settle(100);Authority();
            var input=FinalsPainted(f.Window,toggle);Assert.True(new Rect(f.Window.ClientSize).Contains(input));
            Assert.All(FinalsProbes(input),point=>Assert.True(Within(toggle,f.Window.InputHitTest(point))));
            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-palette-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string prefix=Path.Combine(directory,$"quarter-break-palette-{theme}-{(selected?"on":"off")}-420");
            Capture(f.Window,$"quarter-break-palette-fit-{theme}-{selected}-420");
            File.WriteAllText(prefix+"-fit.json",JsonSerializer.Serialize(new{theme=theme.ToString(),selected,input,viewport=f.Window.ClientSize,
                texts=texts.Select(text=>new{text.Name,text.Text,text.FontSize,text.Bounds,text.TextLayout.Width,text.TextLayout.WidthIncludingTrailingWhitespace,text.TextLayout.Height,
                    lines=text.TextLayout.TextLines.Select(line=>new{line.Width,line.WidthIncludingTrailingWhitespace,line.HasCollapsed})})},new JsonSerializerOptions{WriteIndented=true}));
            foreach(var text in texts)
            {
                Assert.True(input.Contains(FinalsPainted(f.Window,text)),text.Name+" full allocated text region must remain inside its original checkbox.");
                // Invisible trailing-space advances remain diagnostic, not a
                // false glyph-clipping oracle. No fit tolerance/font relaxation.
                Assert.True(text.TextLayout.Width<=text.Bounds.Width+.01,text.Name+" visible layout width");
                Assert.All(text.TextLayout.TextLines,line=>Assert.True(line.Width<=text.Bounds.Width+.01,text.Name+" visible line width"));
                Assert.DoesNotContain(text.TextLayout.TextLines,line=>line.HasCollapsed);
                Assert.True(text.TextLayout.Height<=text.Bounds.Height+.01,text.Name+" visible layout height");
            }
            var iconInk=Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground);double iconVisualOpacity=Mr5PaletteVisualOpacity(icon);
            var textInks=texts.ToDictionary(text=>text,text=>Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground));
            var textOpacities=texts.ToDictionary(text=>text,Mr5PaletteVisualOpacity);
            using var normal=Mr5PaletteCapture(f.Window,prefix+".png");SKBitmap backing;
            try
            {
                // Reversible PAINT isolation only, not visibility/layout/state.
                // Retain the actual quarter card, material and blue alpha surface.
                icon.Opacity=0;foreach(var text in texts)text.Opacity=0;
                await Settle(40);backing=Mr5PaletteCapture(f.Window,prefix+"-backing-only.png");
            }
            finally {icon.Opacity=iconOpacity;foreach(var pair in originalOpacity)pair.Key.Opacity=pair.Value;}
            using(backing)
            {
                Assert.Equal(normal.Width,backing.Width);Assert.Equal(normal.Height,backing.Height);
                var iconPixels=Mr5PalettePixels(f.Window,icon,normal,backing,iconInk,iconVisualOpacity);
                Assert.True(iconPixels.Count>=20,"Actual upright screen icon must be witnessed in normal/backing paint difference.");
                double maximum=iconPixels.Max(pixel=>pixel.MeasuredCoverage);
                var strong=iconPixels.Where(pixel=>pixel.MeasuredCoverage>=maximum-.025).ToArray();
                Assert.True(strong.Length>=12,"Require multiple strongly painted screen pixels, not one convenient point.");Assert.InRange(maximum,.2,1.04);
                double iconMinimum=strong.Min(pixel=>pixel.PaintedContrast);
                var reports=new List<object>();var failures=new List<string>();
                if(iconMinimum<3)failures.Add($"essential screen icon actual strong-pixel contrast {iconMinimum:F3}:1 <3:1");
                foreach(var text in texts)
                {
                    var ink=textInks[text];var pixels=Mr5PalettePixels(f.Window,text,normal,backing,ink,textOpacities[text]);
                    Assert.True(pixels.Count>=12,text.Name+" needs an actually witnessed glyph-pixel set.");
                    if(!selected)
                    {
                        string baseHex=$"#{actualBase.Color.R:X2}{actualBase.Color.G:X2}{actualBase.Color.B:X2}";
                        Assert.All(pixels,pixel=>Assert.Equal(baseHex,pixel.Background));
                    }
                    double minimum=pixels.Min(pixel=>pixel.IntendedTextContrast);
                    reports.Add(new{text.Name,text.Text,text.FontSize,text.Bounds,text.TextLayout.Width,text.TextLayout.WidthIncludingTrailingWhitespace,text.TextLayout.Height,
                        lines=text.TextLayout.TextLines.Select(line=>new{line.Width,line.WidthIncludingTrailingWhitespace,line.HasCollapsed}),
                        foreground=ink.Color.ToString(),sourceAlpha=ink.Color.A,ink.Opacity,visualOpacity=textOpacities[text],glyphPixels=pixels.Count,intendedInkMinimum=minimum,pixels});
                    if(minimum<4.5)failures.Add($"{text.Name} small text intended ink over actual backing {minimum:F3}:1 <4.5:1");
                }
                if(!selected)
                {
                    string baseHex=$"#{actualBase.Color.R:X2}{actualBase.Color.G:X2}{actualBase.Color.B:X2}";
                    Assert.All(iconPixels,pixel=>Assert.Equal(baseHex,pixel.Background));
                }
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(new{scope="Linux headless actual original QuarterBreakToggle at420; normal/backing capture; NOTnativeWPF/HC/compositor proof",
                    theme=theme.ToString(),selected,viewport=f.Window.ClientSize,input,actualBase=new{color=actualBase.Color.ToString(),actualBase.Opacity},
                    icon=new{foreground=iconInk.Color.ToString(),sourceAlpha=iconInk.Color.A,iconInk.Opacity,iconVisualOpacity,strongestCoverage=maximum,strongPixels=strong.Length,strongMinimum=iconMinimum,sourceInkMinimum=iconPixels.Min(pixel=>pixel.IntendedTextContrast)},
                    texts=reports,failures,iconPixels},new JsonSerializerOptions{WriteIndented=true}));
                Authority();Assert.Equal(input,FinalsPainted(f.Window,toggle));
                Assert.True(failures.Count==0,string.Join("; ",failures)+". Actual evidence: "+prefix+"-contrast.json");
            }
        }
        finally
        {
            if(icon is not null)icon.Opacity=iconOpacity;foreach(var pair in originalOpacity)pair.Key.Opacity=pair.Value;
            app.ThemeService.SetTheme(oldTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;
        }
    }
}
