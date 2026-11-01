// OUTSIDE ONLY. UNCOMPILED / UNRUN. Root integrates after reviewing.
// Integration prerequisite: make the existing ColorPickerValidationCurrentDraftTests
// declaration partial (no helper copy/replacement). Reuses its public TryPick +
// original owner, actual original button, proven storage scope, and cleanup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class ColorPickerValidationCurrentDraftTests
{
    private static SKBitmap PickerContrastCapture(ColorPickerWindow window,string path)
    {
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(path);
        using var stream=new MemoryStream();frame.Save(stream);
        return SKBitmap.Decode(stream.ToArray())??throw new InvalidDataException("Actual modal frame is not a decodable PNG.");
    }
    private static Rect PickerPainted(Window window,Control control)
    {
        Point origin=Assert.IsType<Point>(control.TranslatePoint(default,window));
        return new Rect(origin,control.Bounds.Size);
    }
    private static double PickerContrast(double r,double g,double b,SKColor background)
    {
        static double Linear(double c)=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);
        static double Lum(double r,double g,double b)=>.2126*Linear(r)+.7152*Linear(g)+.0722*Linear(b);
        double a=Lum(r,g,b),z=Lum(background.Red/255d,background.Green/255d,background.Blue/255d);
        return(Math.Max(a,z)+.05)/(Math.Min(a,z)+.05);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,500,760)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,500,760)][InlineData(AppTheme.Light,420,320)]
    [Trait("Evidence","PickerOriginalPrimaryContrast")]
    public async Task OriginalUseColourLabelHasReadableInkOverActualMaterial(AppTheme theme,int width,int height)
    {
        using var storage=new PickerStorage();
        var app=(RocheScoreboard.Mac.App)Application.Current!;AppTheme oldTheme=app.ThemeService.CurrentTheme;
        // Material is deliberately not replaced/configured in this BASELINE.
        // A disabled glass precondition is reported as such, never as a contrast failure.
        Assert.True(OperatorAppearance.GlassEnabled,"This glass-baseline requires the current enabled glass material; no preference is changed to manufacture it.");
        var owner=new NativeWindow{Width=900,Height=700};var previews=new List<Color>();
        try
        {
            app.ThemeService.SetTheme(theme);owner.Show();
            var result=await Pick(owner,Colors.CornflowerBlue,previews,async picker=>
            {
                picker.Width=width;picker.MaxHeight=height;picker.Height=height;await Pump();
                Button accept=Action(picker,"USE COLOUR");accept.BringIntoView();
                picker.MouseMove(new Point(1,1));await Pump(160);Onscreen(accept,picker);
                TextBlock label=Assert.Single(accept.GetVisualDescendants().OfType<TextBlock>(),text=>text.Text=="USE COLOUR");
                var face=Assert.Single(accept.GetVisualDescendants().OfType<OperatorGlassSurface>(),part=>part.Name=="Bd");
                Assert.True(accept.IsEnabled);Onscreen(label,picker);Assert.Equal(theme,app.ThemeService.CurrentTheme);
                Assert.False(OperatorMotion.Enabled);Assert.True(label.FontSize<18);
                Assert.DoesNotContain(label.TextLayout.TextLines,line=>line.HasCollapsed);
                Assert.All(label.TextLayout.TextLines,line=>Assert.True(line.Width<=label.Bounds.Width+.01));
                Assert.True(label.TextLayout.Height<=label.Bounds.Height+.01);
                var ink=Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground);IBrush originalBrush=label.Foreground!;
                double originalOpacity=label.Opacity;
                double visualOpacity=label.GetVisualAncestors().Prepend(label).OfType<Visual>().Aggregate(1d,(p,v)=>p*v.Opacity);
                double authoredAlpha=visualOpacity*ink.Opacity*ink.Color.A/255d;
                Rect buttonBounds=PickerPainted(picker,accept),labelBounds=PickerPainted(picker,label);
                Assert.True(buttonBounds.Contains(labelBounds));
                Color selected=picker.SelectedColor;var previewSnapshot=previews.ToArray();byte[]?[] history=storage.Bytes();
                string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-picker-primary-contrast");Directory.CreateDirectory(directory);
                string prefix=Path.Combine(directory,$"picker-primary-{theme}-{width}x{height}");
                void Authority()
                {
                    Assert.True(picker.IsVisible);Assert.Null(picker.DialogResult);Assert.Equal(selected,picker.SelectedColor);
                    Assert.Equal(previewSnapshot,previews.ToArray());storage.AssertBytes(history);
                    Assert.Equal(buttonBounds,PickerPainted(picker,accept));Assert.Equal(labelBounds,PickerPainted(picker,label));
                    Assert.Equal("USE COLOUR",label.Text);Assert.True(accept.IsEnabled);
                }
                Authority();
                using var normal=PickerContrastCapture(picker,prefix+"-normal.png");
                SKBitmap backing,black,white,backingAgain;
                // Animation-priority leases leave the existing local/inherited/
                // template binding completely intact; disposal is guaranteed on
                // every capture failure. No opacity or foreground base writes.
                using(label.SetValue((AvaloniaProperty)Visual.OpacityProperty,0d,BindingPriority.Animation))
                {await Pump(40);Authority();backing=PickerContrastCapture(picker,prefix+"-backing.png");}
                using(backing)
                {
                    using(label.SetValue((AvaloniaProperty)TextBlock.ForegroundProperty,Brushes.Black,BindingPriority.Animation))
                    {await Pump(40);Authority();black=PickerContrastCapture(picker,prefix+"-sentinel-black.png");}
                    using(black)
                    {
                        using(label.SetValue((AvaloniaProperty)TextBlock.ForegroundProperty,Brushes.White,BindingPriority.Animation))
                        {await Pump(40);Authority();white=PickerContrastCapture(picker,prefix+"-sentinel-white.png");}
                        using(white)
                        {
                            using(label.SetValue((AvaloniaProperty)Visual.OpacityProperty,0d,BindingPriority.Animation))
                            {await Pump(40);Authority();backingAgain=PickerContrastCapture(picker,prefix+"-backing-recheck.png");}
                            using(backingAgain)
                            using(var restored=PickerContrastCapture(picker,prefix+"-restored-normal.png"))
                            {
                                Assert.Same(originalBrush,label.Foreground);Assert.Equal(originalOpacity,label.Opacity);Authority();
                                foreach(var image in new[]{backing,black,white,backingAgain,restored})
                                {Assert.Equal(normal.Width,image.Width);Assert.Equal(normal.Height,image.Height);}
                                double sx=normal.Width/picker.ClientSize.Width,sy=normal.Height/picker.ClientSize.Height;
                                Assert.InRange(Math.Abs(sx-sy),0,.01);
                                int left=Math.Max(0,(int)Math.Floor(labelBounds.Left*sx)),right=Math.Min(normal.Width,(int)Math.Ceiling(labelBounds.Right*sx));
                                int top=Math.Max(0,(int)Math.Floor(labelBounds.Top*sy)),bottom=Math.Min(normal.Height,(int)Math.Ceiling(labelBounds.Bottom*sy));
                                var pixels=new List<object>();var intended=new List<double>();var corePainted=new List<double>();
                                int glyphs=0,changed=0,maxNormalDelta=0,maxBackdropDrift=0,maxRestoreDrift=0;double maxCoverage=0;
                                for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
                                {
                                    SKColor bg=backing.GetPixel(x,y),b2=backingAgain.GetPixel(x,y),n=normal.GetPixel(x,y),r=restored.GetPixel(x,y);
                                    SKColor lo=black.GetPixel(x,y),hi=white.GetPixel(x,y);
                                    static int Delta(SKColor a,SKColor b)=>Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)));
                                    maxBackdropDrift=Math.Max(maxBackdropDrift,Delta(bg,b2));maxRestoreDrift=Math.Max(maxRestoreDrift,Delta(n,r));
                                    // Two extreme glyph paints identify location independently
                                    // of whether ORIGINAL dark text resembles its dark backing.
                                    double coverage=(hi.Red-lo.Red+hi.Green-lo.Green+hi.Blue-lo.Blue)/(3d*255d);
                                    if(coverage<.15)continue;
                                    foreach(var value in new[]{bg,n,lo,hi})Assert.Equal((byte)255,value.Alpha);
                                    glyphs++;maxCoverage=Math.Max(maxCoverage,coverage);int normalDelta=Delta(n,bg);
                                    maxNormalDelta=Math.Max(maxNormalDelta,normalDelta);if(normalDelta>0)changed++;
                                    double nominal=PickerContrast((ink.Color.R*authoredAlpha+bg.Red*(1-authoredAlpha))/255d,
                                        (ink.Color.G*authoredAlpha+bg.Green*(1-authoredAlpha))/255d,
                                        (ink.Color.B*authoredAlpha+bg.Blue*(1-authoredAlpha))/255d,bg);
                                    double painted=PickerContrast(n.Red/255d,n.Green/255d,n.Blue/255d,bg);
                                    intended.Add(nominal);if(coverage>=.8)corePainted.Add(painted);
                                    pixels.Add(new{x,y,sentinelCoverage=coverage,normal=n.ToString(),backing=bg.ToString(),normalDelta,nominal,painted});
                                }
                                var diagnostics=new{scope="Linux headless public owned picker, original primary template; not native WPF/HC/compositor proof",
                                    theme=theme.ToString(),viewport=picker.ClientSize,buttonBounds,labelBounds,label.FontSize,fontFamily=label.FontFamily.ToString(),
                                    foreground=ink.Color.ToString(),ink.Opacity,originalOpacity,visualOpacity,authoredAlpha,
                                    material=face.Material.ToString(),
                                    buttonBackground=accept.Background?.ToString(),buttonBackgroundType=accept.Background?.GetType().Name,
                                    faceBackground=face.Background?.ToString(),faceBackgroundType=face.Background?.GetType().Name,
                                    buttonGradient=accept.Background is GradientBrush buttonGradient?string.Join(";",buttonGradient.GradientStops.Select(stop=>stop.Color+"@"+stop.Offset)):null,
                                    faceGradient=face.Background is GradientBrush faceGradient?string.Join(";",faceGradient.GradientStops.Select(stop=>stop.Color+"@"+stop.Offset)):null,
                                    OperatorAppearance.GlassEnabled,glassIntensity=OperatorAppearance.GlassIntensity.ToString(),
                                    glyphs,changed,maxNormalDelta,maxCoverage,maxBackdropDrift,maxRestoreDrift,
                                    nominalMinimum=intended.Count==0?(double?)null:intended.Min(),
                                    coreCount=corePainted.Count,corePaintedMinimum=corePainted.Count==0?(double?)null:corePainted.Min(),pixels};
                                File.WriteAllText(prefix+"-contrast.json",JsonSerializer.Serialize(diagnostics,new JsonSerializerOptions{WriteIndented=true}));
                                // Mask/stability assertions are prerequisite failures, not
                                // an assumed contrast result. Evidence is saved FIRST.
                                Assert.InRange(maxBackdropDrift,0,1);Assert.InRange(maxRestoreDrift,0,1);
                                Assert.True(glyphs>=30,"Actual same-geometry black/white label masks must contain real glyphs.");
                                Assert.True(corePainted.Count>=12&&maxCoverage>=.8,"The normal label requires witnessed glyph cores.");
                                Assert.True(changed>0&&maxNormalDelta>0,"Original normal ink must actually paint; no sentinel-only text proof.");
                                Assert.True(intended.Min()>=4.5,$"{theme} {width} original USE COLOUR intended ink minimum {intended.Min():F3}:1 <4.5:1 over actual glyph backing; {prefix}-contrast.json");
                            }
                        }
                    }
                }
            });
            Assert.False(result.accepted);Assert.Equal(Colors.CornflowerBlue,result.selected);Assert.NotEmpty(previews);Assert.Equal(Colors.CornflowerBlue,previews[^1]);
        }
        finally
        {
            foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();app.ThemeService.SetTheme(oldTheme);
        }
    }
}
