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
    private readonly record struct Mr5PalettePixel(int X,int Y,string Foreground,string Background,
        double MeasuredCoverage,double PaintedContrast,double IntendedTextContrast,double OffStarSourceContrast);

    private static double Mr5PaletteLuminance(double red,double green,double blue)
    {
        static double Linear(double channel)=>channel<=.04045?channel/12.92:Math.Pow((channel+.055)/1.055,2.4);
        return .2126*Linear(red)+.7152*Linear(green)+.0722*Linear(blue);
    }

    private static double Mr5PaletteContrast(double red,double green,double blue,SKColor background)
    {
        double first=Mr5PaletteLuminance(red,green,blue);
        double second=Mr5PaletteLuminance(background.Red/255d,background.Green/255d,background.Blue/255d);
        return (Math.Max(first,second)+.05)/(Math.Min(first,second)+.05);
    }

    private static double Mr5PaletteCompositeContrast(Color foreground,double opacity,SKColor background) =>
        Mr5PaletteContrast((foreground.R*opacity+background.Red*(1-opacity))/255d,
            (foreground.G*opacity+background.Green*(1-opacity))/255d,
            (foreground.B*opacity+background.Blue*(1-opacity))/255d,background);

    private static double Mr5PaletteVisualOpacity(Control target) =>
        target.GetVisualAncestors().Prepend(target).OfType<Visual>().Aggregate(1d,(opacity,visual)=>opacity*visual.Opacity);

    private static SKBitmap Mr5PaletteCapture(Window window,string path)
    {
        using var frame=window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(path);
        using var stream=new MemoryStream();frame.Save(stream);
        return SKBitmap.Decode(stream.ToArray())??throw new InvalidDataException("Actual rendered frame was not a decodable PNG.");
    }

    private static List<Mr5PalettePixel> Mr5PalettePixels(Window window,Control target,
        SKBitmap normal,SKBitmap backing,ISolidColorBrush foreground,double visualOpacity)
    {
        Rect bounds=FinalsPainted(window,target);
        double sx=normal.Width/window.ClientSize.Width,sy=normal.Height/window.ClientSize.Height;
        Assert.InRange(Math.Abs(sx-sy),0,.01);
        int left=Math.Max(0,(int)Math.Floor(bounds.Left*sx));
        int top=Math.Max(0,(int)Math.Floor(bounds.Top*sy));
        int right=Math.Min(normal.Width,(int)Math.Ceiling(bounds.Right*sx));
        int bottom=Math.Min(normal.Height,(int)Math.Ceiling(bounds.Bottom*sy));
        var result=new List<Mr5PalettePixel>();
        Color ink=foreground.Color;
        double authoredOpacity=visualOpacity*foreground.Opacity*ink.A/255d;
        for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
        {
            SKColor actual=normal.GetPixel(x,y),background=backing.GetPixel(x,y);
            Assert.Equal((byte)255,actual.Alpha);Assert.Equal((byte)255,background.Alpha);
            double dr=actual.Red-background.Red,dg=actual.Green-background.Green,db=actual.Blue-background.Blue;
            // Ignore sub-byte/rounding differences and antialiasing dust. The
            // remaining set still includes partially covered glyph edges for
            // checking intended text ink against every sampled glyph backing.
            if(Math.Max(Math.Abs(dr),Math.Max(Math.Abs(dg),Math.Abs(db)))<3)continue;
            double fr=ink.R-background.Red,fg=ink.G-background.Green,fb=ink.B-background.Blue;
            double denominator=fr*fr+fg*fg+fb*fb;
            if(denominator<1)continue;
            double coverage=(dr*fr+dg*fg+db*fb)/denominator;
            result.Add(new(x,y,$"#{actual.Red:X2}{actual.Green:X2}{actual.Blue:X2}",
                $"#{background.Red:X2}{background.Green:X2}{background.Blue:X2}",coverage,
                Mr5PaletteContrast(actual.Red/255d,actual.Green/255d,actual.Blue/255d,background),
                Mr5PaletteCompositeContrast(ink,authoredOpacity,background),
                Mr5PaletteCompositeContrast(ink,authoredOpacity*.68,background)));
        }
        return result;
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,false)][InlineData(AppTheme.Dark,true)]
    [InlineData(AppTheme.Light,false)][InlineData(AppTheme.Light,true)]
    [Trait("Evidence","MR5ActualFinalsPalette")]
    public async Task ActualFinalsPaletteHasReadableStarAndDescriptionOverItsRenderedSelection(AppTheme theme,bool selected)
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
            var toggle=Named<CheckBox>(f.Window,"SettingsFinalsToggle");
            await RevealFinals(f,toggle);
            var intendedTheme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.FinalsToggle"));
            Assert.Same(intendedTheme,toggle.Theme);
            T Part<T>(string name) where T:Control=>Assert.Single(toggle.GetVisualDescendants().OfType<T>(),part=>part.Name==name);
            symbol=Part<OperatorSymbol>("FinalsSymbol");
            description=Part<TextBlock>("FinalsDescription");
            symbolOpacity=symbol.Opacity;descriptionOpacity=description.Opacity;
            var basis=Part<Border>("FinalsBase");
            var selection=Part<OperatorChoiceSurface>("FinalsSelection");
            var baseBrush=Assert.IsAssignableFrom<ISolidColorBrush>(basis.Background);
            // Prove actual theme adaptation occurred on the live original view.
            Assert.Equal(Color.Parse(theme==AppTheme.Light?"#E1E1E8":"#29292D"),baseBrush.Color);
            Assert.Equal((byte)255,baseBrush.Color.A);Assert.Equal(1d,baseBrush.Opacity);
            Assert.Equal("finals",symbol.Kind);Assert.Equal(13d,description.FontSize);
            Assert.True(symbol.IsEffectivelyVisible);Assert.True(description.IsEffectivelyVisible);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
            string scores=Scores(f.Match);
            int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
            bool initial=toggle.IsChecked==true;
            if(initial!=selected)provider.Toggle();
            Assert.Equal(initial==selected?0:1,changes);
            Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,FinalsValue(f));
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
                ??Path.Combine(Path.GetTempPath(),"roche-mr5-finals-palette-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string prefix=Path.Combine(directory,$"mr5-finals-palette-{theme}-{(selected?"on":"off")}-420");
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
                Assert.True(starPixels.Count>=20,"Actual star ink must be present in the normal/background difference.");
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
                Assert.True(strongStar.Length>=12,"A meaningful set of strongly covered star pixels is required for a measured icon contrast assertion.");
                Assert.InRange(strongestCoverage,.2,1.04);
                double starMinimum=strongStar.Min(pixel=>pixel.PaintedContrast);
                double textMinimum=textPixels.Min(pixel=>pixel.IntendedTextContrast);
                var report=new
                {
                    scope="Linux headless software rendering; original live Settings Finals template",
                    theme=theme.ToString(),selected,viewport=f.Window.ClientSize,
                    baseline=new{baseColor=baseBrush.Color.ToString(),baseBrush.Opacity},
                    star=new{foreground=starInk.Color.ToString(),starInk.Opacity,starVisualOpacity,
                        renderedCoverageMaximum=strongestCoverage,strongPixelCount=strongStar.Length,
                        strongPixelMinimumContrast=starMinimum,
                        sourceFullInkMinimum=starPixels.Min(pixel=>pixel.IntendedTextContrast),
                        sourceOffOutlineAt68PercentMinimum=starPixels.Min(pixel=>pixel.OffStarSourceContrast)},
                    description=new{description.FontSize,foreground=textInk.Color.ToString(),textInk.Opacity,
                        textVisualOpacity,glyphPixelCount=textPixels.Count,intendedInkMinimumContrast=textMinimum},
                    backingExplanation="Same rendered view with only star/description paint temporarily at opacity zero; actual gold alpha gradient/liquid geometry retained.",
                    starPixels,textPixels
                };
                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(selected,FinalsValue(f));
                Assert.Equal(initial==selected?0:1,changes);Assert.Equal(scores,Scores(f.Match));
                Assert.Equal(input,FinalsPainted(f.Window,toggle));
                Assert.True(starMinimum>=3,
                    $"{theme} Finals {(selected?"ON":"OFF")} actual strongly painted star contrast {starMinimum:F3}:1 must be >=3:1. Evidence: {prefix}-contrast.json");
                Assert.True(textMinimum>=4.5,
                    $"{theme} Finals {(selected?"ON":"OFF")} 13px description intended ink contrast over actual glyph backing {textMinimum:F3}:1 must be >=4.5:1. Evidence: {prefix}-contrast.json");
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
