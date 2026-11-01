// OUTSIDE ONLY: two compact Exit palette cases (Dark/Light), UNCOMPILED/UNRUN.
// Root owns integration/execution after the current max-two-host full freeze.
// Uses existing Training Fixture/public inputs; no production/translator edits.
// Pixel math adapted from LiveSettingsFinalsPaletteTests/LiveQuarterBreakPaletteTests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
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
public sealed partial class TrainingQueueCommandRegressionTests
{
    private sealed record TxpFile(byte[]? Bytes,DateTime? LastWriteUtc);
    private readonly record struct TxpPixel(int X,int Y,string Actual,string Backing,double Coverage,double PaintedContrast,double IntendedInkContrast);
    private static Rect TxpBox(Window window,Control target)
    {var point=target.TranslatePoint(default,window);Assert.NotNull(point);return new Rect(point.Value,target.Bounds.Size);}
    private static double TxpVisualOpacity(Control target)=>target.GetVisualAncestors().Prepend(target).OfType<Visual>().Aggregate(1d,(value,visual)=>value*visual.Opacity);
    private static double TxpLuminance(double r,double g,double b)
    {static double Linear(double v)=>v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);return .2126*Linear(r)+.7152*Linear(g)+.0722*Linear(b);}
    private static double TxpContrast(double r,double g,double b,SKColor backing)
    {double x=TxpLuminance(r,g,b),y=TxpLuminance(backing.Red/255d,backing.Green/255d,backing.Blue/255d);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    private static SKBitmap TxpCapture(Window window,string path)
    {using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(path);using var stream=new MemoryStream();frame.Save(stream);return SKBitmap.Decode(stream.ToArray())??throw new InvalidDataException("Rendered Exit frame must decode as PNG.");}
    private static List<TxpPixel> TxpPixels(Window window,Control target,Rect actualVisibleMask,SKBitmap normal,SKBitmap backing,ISolidColorBrush foreground,double visualOpacity)
    {
        Rect box=TxpBox(window,target).Intersect(actualVisibleMask);double sx=normal.Width/window.ClientSize.Width,sy=normal.Height/window.ClientSize.Height;
        Assert.InRange(Math.Abs(sx-sy),0,.01);Assert.True(box.Width>0&&box.Height>0);
        int left=Math.Max(0,(int)Math.Floor(box.Left*sx)),right=Math.Min(normal.Width,(int)Math.Ceiling(box.Right*sx));
        int top=Math.Max(0,(int)Math.Floor(box.Top*sy)),bottom=Math.Min(normal.Height,(int)Math.Ceiling(box.Bottom*sy));
        Color ink=foreground.Color;double alpha=visualOpacity*foreground.Opacity*ink.A/255d;var result=new List<TxpPixel>();
        for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
        {
            SKColor pixel=normal.GetPixel(x,y),bg=backing.GetPixel(x,y);Assert.Equal((byte)255,pixel.Alpha);Assert.Equal((byte)255,bg.Alpha);
            double dr=pixel.Red-bg.Red,dg=pixel.Green-bg.Green,db=pixel.Blue-bg.Blue;
            if(Math.Max(Math.Abs(dr),Math.Max(Math.Abs(dg),Math.Abs(db)))<3)continue;
            double fr=ink.R-bg.Red,fg=ink.G-bg.Green,fb=ink.B-bg.Blue,denominator=fr*fr+fg*fg+fb*fb;if(denominator<1)continue;
            double coverage=(dr*fr+dg*fg+db*fb)/denominator;
            result.Add(new(x,y,$"#{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}",$"#{bg.Red:X2}{bg.Green:X2}{bg.Blue:X2}",coverage,
                TxpContrast(pixel.Red/255d,pixel.Green/255d,pixel.Blue/255d,bg),
                TxpContrast((ink.R*alpha+bg.Red*(1-alpha))/255d,(ink.G*alpha+bg.Green*(1-alpha))/255d,(ink.B*alpha+bg.Blue*(1-alpha))/255d,bg)));
        }
        return result;
    }
    private static T TxpOperatorState<T>(string name)=>Assert.IsType<T>(typeof(OperatorDesign).GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));

    [AvaloniaTheory][InlineData(AppTheme.Dark)][InlineData(AppTheme.Light)]
    [Trait("Evidence","TrainingExitActualCompactPalette")]
    public async Task CompactExitUsesReadableActualThemeInkOverItsRenderedGlassAndKeepsPreferencesUnwritten(AppTheme theme)
    {
        await using var f=await Fixture.Create(420,320);
        var app=Assert.IsType<RocheScoreboard.Mac.App>(Application.Current);var oldTheme=app.ThemeService.CurrentTheme;var oldVariant=app.RequestedThemeVariant;
        var oldOperatorTheme=TxpOperatorState<AppTheme>("_theme");bool? oldOverride=OperatorMotion.AnimationOverride;
        // Existing Fixture has already established the latched PID-private scope.
        // Prove exact preference paths BEFORE any extra file read/write/delete.
        Assert.True(SelfTestStorage.IsEnabled);string root=Path.GetFullPath(SelfTestStorage.RootPath),documents=Path.GetFullPath(SelfTestStorage.DocumentsDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))),root);
        Assert.Equal(Path.Combine(root,"preferences"),documents);
        var files=new Dictionary<string,TxpFile>(StringComparer.Ordinal);
        foreach(string name in new[]{"ux_preferences.json","operator_appearance.json"})foreach(string suffix in new[]{"",".bak",".tmp"})
        {
            string path=Path.GetFullPath(Path.Combine(documents,name+suffix));Assert.Equal(documents,Path.GetDirectoryName(path));
            files.Add(path,File.Exists(path)?new(File.ReadAllBytes(path),File.GetLastWriteTimeUtc(path)):new(null,null));
        }
        void PreferencesUnwritten()
        {
            foreach(var (path,saved)in files)
            {
                Assert.Equal(saved.Bytes is not null,File.Exists(path));
                if(saved.Bytes is not null){Assert.Equal(saved.Bytes,File.ReadAllBytes(path));Assert.Equal(saved.LastWriteUtc!.Value,File.GetLastWriteTimeUtc(path));}
            }
        }
        var appearance=new{OperatorAppearance.GlassEnabled,OperatorAppearance.ReduceMotion,OperatorAppearance.MotionStyle,OperatorAppearance.GlassIntensity,OperatorAppearance.Backdrop};
        string appearanceBefore=JsonSerializer.Serialize(appearance);
        var paint=new Dictionary<Control,double>();OperatorSymbol? backIcon=null;
        try
        {
            // SetTheme itself is the real theme route and does not save UX files.
            // Do NOT call appearance Configure*: those intentionally save preferences.
            app.ThemeService.SetTheme(theme);OperatorMotion.AnimationOverride=false;await Settle(100);PreferencesUnwritten();
            Assert.Equal(theme,app.ThemeService.CurrentTheme);Assert.Equal(theme==AppTheme.Dark?ThemeVariant.Dark:ThemeVariant.Light,app.RequestedThemeVariant);
            var host=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),g=>g.Children.Contains(f.Panel)&&g.Children.Contains(f.OtherScope));host.Height=f.Window.ClientSize.Height;await Settle(70);
            f.Active.Title=string.Concat(Enumerable.Repeat("Meaningful long current drill context — ",10));f.Panel.Bind(f.Session);await Settle(60);
            string state=ClearFullState(f.Session);int structural=0;f.Session.StructuralChanged+=()=>structural++;
            var exit=Named<Button>(f.Panel,"ExitButton");await TabTo(f.Window,exit);Key(f.Window,PhysicalKey.Space);await Settle(100);
            var overlay=Named<Border>(f.Panel,"TrainingExitReviewOverlay");Assert.True(overlay.IsEffectivelyVisible);Assert.True(f.Panel.HasActiveExitDecision);
            var heading=Named<TextBlock>(f.Panel,"TrainingExitReviewHeading");var status=Named<TextBlock>(f.Panel,"TrainingExitReviewStatus");var context=Named<TextBlock>(f.Panel,"TrainingExitReviewContext");var scroll=Named<ScrollViewer>(f.Panel,"TrainingExitReviewScroll");
            var footer=Assert.Single(overlay.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.StartsWith("This Training session cannot be resumed")is true);
            var cancel=ExitReviewCommand(f.Panel,"training-exit-cancel");var confirm=ExitReviewCommand(f.Panel,"training-exit-confirm");
            var cancelText=Assert.Single(cancel.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==cancel.Content?.ToString());
            var confirmText=Assert.Single(confirm.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==confirm.Content?.ToString());
            backIcon=Assert.Single(overlay.GetVisualDescendants().OfType<OperatorSymbol>(),icon=>Equals(icon.Kind,"back"));
            var texts=new[]{heading,status,context,footer,cancelText,confirmText};var viewport=new Rect(f.Window.ClientSize);
            // Theme-adaptation precondition, NOT a nominal-color contrast oracle.
            Assert.Equal(Color.Parse(theme==AppTheme.Dark?"#FAFAFC":"#19191E"),Assert.IsAssignableFrom<ISolidColorBrush>(heading.Foreground).Color);
            Assert.Equal(24d,heading.FontSize);Assert.Equal(13d,status.FontSize);Assert.Equal(13d,footer.FontSize);
            Assert.True(scroll.Viewport.Height>=20);Assert.True(scroll.Extent.Height>scroll.Viewport.Height);Assert.Equal(0,scroll.Offset.Y);
            Assert.Contains(f.Active.Title,context.Text);Assert.True(cancel.IsFocused);Assert.True(cancel.IsDefault);Assert.False(confirm.IsDefault);
            ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);f.Window.MouseMove(new Point(1,1));await Settle(60);
            var fits=new List<object>();
            foreach(var text in texts)
            {
                // Context is deliberately taller than its clip; all other entire
                // allocated text regions must fit in the actual compact owner.
                if(!ReferenceEquals(text,context))Assert.True(viewport.Contains(TxpBox(f.Window,text)),text.Name+" full text region must fit.");
                Assert.True(text.TextLayout.Width<=text.Bounds.Width+.01);Assert.All(text.TextLayout.TextLines,line=>Assert.True(line.Width<=text.Bounds.Width+.01));
                Assert.DoesNotContain(text.TextLayout.TextLines,line=>line.HasCollapsed);Assert.True(text.TextLayout.Height<=text.Bounds.Height+.01);
                fits.Add(new{text.Name,text.Text,text.FontSize,text.Bounds,text.TextLayout.Width,text.TextLayout.WidthIncludingTrailingWhitespace,text.TextLayout.Height,lines=text.TextLayout.TextLines.Select(line=>new{line.Width,line.WidthIncludingTrailingWhitespace,line.HasCollapsed})});
            }
            Assert.True(viewport.Contains(TxpBox(f.Window,backIcon)));
            var inks=texts.ToDictionary(t=>t,t=>Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground));var opacities=texts.ToDictionary(t=>t,TxpVisualOpacity);
            var iconInk=Assert.IsAssignableFrom<ISolidColorBrush>(backIcon.Foreground);double iconVisualOpacity=TxpVisualOpacity(backIcon);
            foreach(Control target in texts.Cast<Control>().Append(backIcon))paint[target]=target.Opacity;
            string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-exit-palette-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string prefix=Path.Combine(dir,"exit-palette-"+theme+"-420");
            using var normal=TxpCapture(f.Window,prefix+"-normal.png");SKBitmap backing;
            try
            {
                // Reversible PAINT-ONLY masking; no Visibility/layout/brush/model
                // change. SetCurrentValue retains an existing opacity binding.
                foreach(var target in paint.Keys)target.SetCurrentValue(Visual.OpacityProperty,0d);
                await Settle(40);backing=TxpCapture(f.Window,prefix+"-backing-only.png");
            }
            finally{foreach(var (target,opacity)in paint)target.SetCurrentValue(Visual.OpacityProperty,opacity);}
            using(backing)
            {
                Assert.Equal(normal.Width,backing.Width);Assert.Equal(normal.Height,backing.Height);
                var reports=new List<object>();var failures=new List<string>();
                foreach(var text in texts)
                {
                    Rect mask=ReferenceEquals(text,context)?TxpBox(f.Window,scroll):viewport;
                    var pixels=TxpPixels(f.Window,text,mask,normal,backing,inks[text],opacities[text]);Assert.True(pixels.Count>=12,"Actually painted glyph pixels are required for "+text.Name+" / "+text.Text);
                    double minimum=pixels.Min(p=>p.IntendedInkContrast),threshold=ReferenceEquals(text,heading)?3:4.5; // 24px semibold heading is large text; others normal text.
                    if(minimum<threshold)failures.Add($"{text.Name??text.Text} actual-backing intended ink {minimum:F3}:1 < {threshold}:1");
                    reports.Add(new{text.Name,text.Text,text.FontSize,foreground=inks[text].Color.ToString(),sourceAlpha=inks[text].Color.A,brushOpacity=inks[text].Opacity,visualOpacity=opacities[text],glyphPixels=pixels.Count,minimum,threshold,pixels});
                }
                var iconPixels=TxpPixels(f.Window,backIcon,viewport,normal,backing,iconInk,iconVisualOpacity);Assert.True(iconPixels.Count>=12);
                double maximum=iconPixels.Max(p=>p.Coverage);Assert.InRange(maximum,.2,1.04);var strong=iconPixels.Where(p=>p.Coverage>=maximum-.025).ToArray();Assert.True(strong.Length>=6,"Several strongly painted essential back-chevron pixels must be witnessed.");
                double iconMinimum=strong.Min(p=>p.PaintedContrast);if(iconMinimum<3)failures.Add($"back-chevron actual strongly painted ink {iconMinimum:F3}:1 <3:1");
                File.WriteAllText(prefix+"-evidence.json",JsonSerializer.Serialize(new{scope="Bounded actual Linux software-rendered Exit at420; normal/backing DIFFERENCE, no nominal-backdrop assumption; NOTnativeWPF/HC/DPI",
                    theme=theme.ToString(),appearance,window=f.Window.ClientSize,scroll.Viewport,scroll.Extent,fits,texts=reports,
                    icon=new{foreground=iconInk.Color.ToString(),sourceAlpha=iconInk.Color.A,iconInk.Opacity,iconVisualOpacity,maximum,strongPixels=strong.Length,minimum=iconMinimum,iconPixels},failures},new JsonSerializerOptions{WriteIndented=true}));
                Assert.Equal(state,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(0,f.Exits);Assert.True(cancel.IsFocused);Assert.True(overlay.IsEffectivelyVisible);PreferencesUnwritten();
                Assert.Equal(appearanceBefore,JsonSerializer.Serialize(new{OperatorAppearance.GlassEnabled,OperatorAppearance.ReduceMotion,OperatorAppearance.MotionStyle,OperatorAppearance.GlassIntensity,OperatorAppearance.Backdrop}));
                Assert.True(failures.Count==0,string.Join("; ",failures)+". Actual evidence: "+prefix+"-evidence.json");
            }
            Key(f.Window,PhysicalKey.Escape);Assert.False(overlay.IsVisible);Assert.Equal(state,ClearFullState(f.Session));Assert.Equal(0,f.Exits);PreferencesUnwritten();
        }
        finally
        {
            var cleanup=new List<Exception>();void Attempt(Action action){try{action();}catch(Exception error){cleanup.Add(error);}}
            foreach(var (target,opacity)in paint)Attempt(()=>target.SetCurrentValue(Visual.OpacityProperty,opacity));
            Attempt(()=>app.ThemeService.SetTheme(oldTheme));Attempt(()=>app.RequestedThemeVariant=oldVariant);
            Attempt(()=>OperatorDesign.UpdateTheme(oldOperatorTheme));Attempt(()=>OperatorMotion.AnimationOverride=oldOverride);
            // No deliberate file writes occurred. If a failing loading path did
            // write, restore only these proven private snapshots exactly.
            foreach(var (path,saved)in files)Attempt(()=>
            {
                if(saved.Bytes is null){if(File.Exists(path))File.Delete(path);}
                else if(!File.Exists(path)||!saved.Bytes.SequenceEqual(File.ReadAllBytes(path))||File.GetLastWriteTimeUtc(path)!=saved.LastWriteUtc!.Value)
                {Directory.CreateDirectory(documents);File.WriteAllBytes(path,saved.Bytes);File.SetLastWriteTimeUtc(path,saved.LastWriteUtc!.Value);}
            });
            if(cleanup.Count>0)throw new AggregateException("Exit palette fixture restoration failed.",cleanup);
        }
    }
}
