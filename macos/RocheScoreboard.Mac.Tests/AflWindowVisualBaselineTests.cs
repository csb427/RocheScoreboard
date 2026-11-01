// OUTSIDE BEFORE-COMPATIBLE DRAFT: exactly4cases, UNCOMPILED/UNRUN.
// Current original control/labels only. No future glyph/type/part lookup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record PresWindowVisualReceipt(string Stage,bool Open,string Name,string Status,string Label,
        Rect Card,Rect Track,Rect Title,Rect Verb,double TitleFont,double VerbFont,double Thumb,
        string CardBrush,string IntendedBrush,int TitlePixels,int VerbPixels,double MinimumTextContrast,
        string ThumbFace,string TrackPaint,double ThumbContrast);

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,1280,850)][InlineData(AppTheme.Light,420,320)]
    public async Task OriginalWindowCardCommandsHaveReadableThemePaletteStableSlotsAndTruthfulAccessibleNames(
        AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;
        var previousTheme=app.ThemeService.CurrentTheme;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        var receipts=new List<PresWindowVisualReceipt>();
        var pixelReceipts=new List<object>();
        var pixelAssertions=new List<Action>();
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
            ??Path.Combine(Path.GetTempPath(),"roche-window-visual-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        TextBlock? title=null,label=null;double titleOpacity=1,labelOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);Assert.Equal(theme,app.ThemeService.CurrentTheme);
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            var backing=Assert.IsAssignableFrom<Border>(card);
            title=Named<TextBlock>(f.Window,"WindowToggleTitle");label=Named<TextBlock>(f.Window,"WindowToggleLabel");
            titleOpacity=title.Opacity;labelOpacity=label.Opacity;
            var track=Named<Border>(f.Window,"WindowToggleTrack");var thumb=PresPlanThumb(f);
            var thumbPaint=Named<Border>(f.Window,"WindowToggleThumb");
            var provider=PresPlanProvider(card);var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            if(PresWindowOpen(f))provider.Invoke();
            await Until(()=>!PresWindowOpen(f)&&label.Text=="Open","Initial actual output must be closed before the three state captures.");
            await PresPlanReveal(f,card);
            string before=Scores(f.Match),screen=PresPlanRead<string>(f,"_activeScreenKey");
            using var changes=new PresPlanChanges(f.Match);
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTrack=FinalsPainted(f.Window,track),fixedTitle=FinalsPainted(f.Window,title),fixedVerb=FinalsPainted(f.Window,label);
            double titleFont=title.FontSize,verbFont=label.FontSize;
            Assert.True(label.MinWidth>=40);
            var expectedBasis=Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,"ControlCentre.RaisedBrush"));

            void CaptureState(string stage,bool open)
            {
                Assert.Equal(open,PresWindowOpen(f));Assert.Equal(open?"Close":"Open",label.Text);
                Assert.Equal(open?20d:0d,thumb.X,4);Assert.Equal(before,Scores(f.Match));Assert.Equal(0,changes.Count);
                Assert.Equal(screen,PresPlanRead<string>(f,"_activeScreenKey"));
                // Keep the pointer off the sampled glyphs. Capture establishes
                // an actual rendered scene; this is not a layout-only oracle.
                f.Window.MouseMove(new Point(1,1));
                Rect input=FinalsPainted(f.Window,card),pill=FinalsPainted(f.Window,track),caption=FinalsPainted(f.Window,title),verb=FinalsPainted(f.Window,label);
                Assert.Equal(fixedCard,input);Assert.Equal(fixedTrack,pill);Assert.Equal(fixedTitle,caption);Assert.Equal(fixedVerb,verb);
                Assert.True(input.Contains(caption)&&input.Contains(verb)&&input.Contains(pill));
                Assert.Equal(titleFont,title.FontSize);Assert.Equal(verbFont,label.FontSize);
                Assert.All(FinalsProbes(input),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                var titleInk=Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground);
                var verbInk=Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground);
                double titleVisualOpacity=Mr5PaletteVisualOpacity(title),verbVisualOpacity=Mr5PaletteVisualOpacity(label);
                string prefix=Path.Combine(directory,$"pres-window-visual-{theme}-{width}-{stage}");
                using SKBitmap normal=Mr5PaletteCapture(f.Window,prefix+".png");
                // Actual opaque thumb core versus the unoccupied track well.
                // Positions are measured from original controls in this frame.
                Rect thumbBox=FinalsPainted(f.Window,thumbPaint);
                Assert.True(pill.Contains(thumbBox));
                double sx=normal.Width/f.Window.ClientSize.Width,sy=normal.Height/f.Window.ClientSize.Height;
                SKColor Pixel(Point point)=>normal.GetPixel(Math.Clamp((int)Math.Round(point.X*sx),0,normal.Width-1),Math.Clamp((int)Math.Round(point.Y*sy),0,normal.Height-1));
                SKColor face=Pixel(thumbBox.Center),well=Pixel(new Point(open?pill.Left+8:pill.Right-8,pill.Center.Y));
                double thumbContrast=Mr5PaletteContrast(face.Red/255d,face.Green/255d,face.Blue/255d,well);
                Assert.Equal((byte)255,face.Alpha);Assert.Equal((byte)255,well.Alpha);
                Assert.True(thumbContrast>=3,"The actual thumb face must remain distinguishable from its actual track.");
                SKBitmap baseFrame;
                try
                {
                    title.Opacity=0;label.Opacity=0;
                    baseFrame=Mr5PaletteCapture(f.Window,prefix+"-backing.png");
                }
                finally {title.Opacity=titleOpacity;label.Opacity=labelOpacity;}
                using(baseFrame)
                {
                    var titlePixels=Mr5PalettePixels(f.Window,title,normal,baseFrame,titleInk,titleVisualOpacity);
                    var verbPixels=Mr5PalettePixels(f.Window,label,normal,baseFrame,verbInk,verbVisualOpacity);
                    Assert.True(titlePixels.Count>=12&&verbPixels.Count>=12,"Real rendered title and verb glyphs are required before a contrast claim.");
                    pixelReceipts.Add(new{stage,Title=titlePixels,Verb=verbPixels});
                    var all=titlePixels.Concat(verbPixels).ToArray();
                    // Core painted pixels and the intended ink over each actual
                    // glyph backing; do not require AA edge pixels to reach4.5.
                    var cores=all.Where(pixel=>pixel.MeasuredCoverage>=.82&&pixel.MeasuredCoverage<=1.15).ToArray();
                    // Capture every actual command state before validating the
                    // captured data. Keep every original threshold/assertion.
                    pixelAssertions.Add(()=>
                    {
                        Assert.All(all,pixel=>Assert.True(pixel.IntendedTextContrast>=4.5));
                        Assert.True(cores.Length>=12);Assert.All(cores,pixel=>Assert.True(pixel.PaintedContrast>=4.5));
                    });
                    var baseBrush=Assert.IsAssignableFrom<ISolidColorBrush>(backing.Background);
                    receipts.Add(new(stage,open,peer!.GetName()??"",AutomationProperties.GetItemStatus(card)??"",label.Text??"",
                        input,pill,caption,verb,title.FontSize,label.FontSize,thumb.X,baseBrush.Color.ToString(),expectedBasis.Color.ToString(),
                        titlePixels.Count,verbPixels.Count,all.Min(pixel=>pixel.IntendedTextContrast),
                        face.ToString(),well.ToString(),thumbContrast));
                }
            }
            CaptureState("closed-before",false);
            provider.Invoke();await Until(()=>PresWindowOpen(f)&&label.Text=="Close","The real original action must open the actual output.");
            CaptureState("open",true);
            provider.Invoke();await Until(()=>!PresWindowOpen(f)&&label.Text=="Open","The real original action must close that actual output.");
            CaptureState("closed-after",false);
            // New metadata/palette contract is checked only after both commands,
            // all state/geometry/pixel witnesses and captures were reached.
            Assert.Equal(3,receipts.Count);
            Assert.True(titleFont>=13&&verbFont>=13,"Typography threshold is checked after both commands and all captures, preserving before evidence.");
            foreach(var assertion in pixelAssertions)assertion();
            foreach(var receipt in receipts)
            {
                Assert.Equal(receipt.Open?"Close output window":"Open output window",receipt.Name);
                Assert.Equal(receipt.Open?"Window open":"Window closed",receipt.Status);
                Assert.DoesNotContain("OUT",receipt.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries));
                Assert.DoesNotContain("✓",receipt.Name);
                Assert.Equal(receipt.IntendedBrush,receipt.CardBrush);
            }
            Assert.Equal(before,Scores(f.Match));Assert.Equal(0,changes.Count);
        }
        finally
        {
            if(title is not null)title.Opacity=titleOpacity;if(label is not null)label.Opacity=labelOpacity;
            File.WriteAllText(Path.Combine(directory,$"pres-window-visual-{theme}-{width}.json"),
                System.Text.Json.JsonSerializer.Serialize(new{States=receipts,Pixels=pixelReceipts},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            app.ThemeService.SetTheme(previousTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);
            OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
