// OUTSIDE REVIEW DRAFT ONLY — UNCOMPILED / UNRUN. Root owns integration/build.
// Standalone PRODUCTION OperatorSymbol pixels, driven by the ORIGINAL live
// checkbox through a real OneWay binding and native Toggle provider. This is
// neither original-template pixel evidence nor native Windows/macOS evidence.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record Mr7IconPixelFrame(string Stage,string Source,double Milliseconds,
        double Fill,double Pulse,double Turn,bool Active,bool Checked);
    private sealed record Mr7IconPatch(Rect Pixels,byte[] Rgb,long Energy,int InkPixels,int RightmostInkX);
    private sealed record Mr7IconCapture(string Stage,string Path,double FillBefore,double FillAfter,
        Size ClientSize,int BitmapWidth,int BitmapHeight,Rect SymbolBounds,Mr7IconPatch Names,Mr7IconPatch Scores);

    private static Mr7IconCapture Mr7CaptureNameplateIcon(Window host,OperatorSymbol symbol,string path,string stage)
    {
        double before=symbol.FillProgress;
        using var bitmap=Mr5PaletteCapture(host,path);
        double after=symbol.FillProgress;
        double sx=bitmap.Width/host.ClientSize.Width,sy=bitmap.Height/host.ClientSize.Height;
        Assert.InRange(Math.Abs(sx-sy),0,.01);
        // Same optical mapping as the production icon's OnRender: one28-unit
        // square frame, uniform scale, then24-unit glyph placement. These two
        // disjoint masks deliberately isolate nameplates from fixed score wells.
        double scale=Math.Min(symbol.Bounds.Width,symbol.Bounds.Height)/28;
        var offset=new Point((symbol.Bounds.Width-24*scale)/2,(symbol.Bounds.Height-24*scale)/2);
        Mr7IconPatch ReadPatch(Rect glyphRegion)
        {
            Point ToHost(Point p)
            {
                var actual=symbol.TranslatePoint(new Point(offset.X+p.X*scale,offset.Y+p.Y*scale),host);
                Assert.NotNull(actual);return actual.Value;
            }
            var top=ToHost(glyphRegion.TopLeft);var bottom=ToHost(glyphRegion.BottomRight);
            // Select interior pixels only. The left mask ends before the first
            // score-well stroke, avoiding an AA boundary shared between parts.
            int left=Math.Max(0,(int)Math.Ceiling(top.X*sx)),right=Math.Min(bitmap.Width,(int)Math.Floor(bottom.X*sx));
            int first=Math.Max(0,(int)Math.Ceiling(top.Y*sy)),last=Math.Min(bitmap.Height,(int)Math.Floor(bottom.Y*sy));
            Assert.True(right>left&&last>first);
            var rgb=new byte[(right-left)*(last-first)*3];int at=0,ink=0,rightmost=-1;long energy=0;
            for(int y=first;y<last;y++)for(int x=left;x<right;x++)
            {
                SKColor pixel=bitmap.GetPixel(x,y);Assert.Equal((byte)255,pixel.Alpha);
                rgb[at++]=pixel.Red;rgb[at++]=pixel.Green;rgb[at++]=pixel.Blue;
                energy+=pixel.Red+pixel.Green+pixel.Blue;
                if(Math.Max(pixel.Red,Math.Max(pixel.Green,pixel.Blue))>8){ink++;rightmost=Math.Max(rightmost,x);}
            }
            return new(new Rect(left,first,right-left,last-first),rgb,energy,ink,rightmost);
        }
        return new(stage,path,before,after,host.ClientSize,bitmap.Width,bitmap.Height,
            FinalsPainted(host,symbol),ReadPatch(new Rect(.5,2.8,14.5,19.4)),ReadPatch(new Rect(15.2,2.8,8.2,19.4)));
    }

    private static void Mr7SameFixedScorePixels(Mr7IconCapture baseline,Mr7IconCapture next)
    {
        Assert.Equal(baseline.ClientSize,next.ClientSize);Assert.Equal(baseline.SymbolBounds,next.SymbolBounds);
        Assert.Equal(baseline.BitmapWidth,next.BitmapWidth);Assert.Equal(baseline.BitmapHeight,next.BitmapHeight);
        Assert.Equal(baseline.Scores.Pixels,next.Scores.Pixels);
        Assert.True(baseline.Scores.InkPixels>=80,"Fixed score wells must actually contain painted ink before a stable-mask assertion.");
        Assert.Equal(baseline.Scores.Rgb,next.Scores.Rgb);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","MR7ProductionIconPixels")]
    public async Task StandaloneProductionNameplateIconRetractsOnlyNamesWhileFixedScorePixelsStayIdentical(bool reduced)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        Window? host=null;IDisposable? binding=null;EventHandler? observer=null;
        var trace=new List<Mr7IconPixelFrame>();var captures=new List<Mr7IconCapture>();
        var watch=Stopwatch.StartNew();string stage="prepare";bool dispatching=false;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
            ??Path.Combine(Path.GetTempPath(),"roche-mr7-icon-pixels-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string prefix=Path.Combine(directory,"mr7-nameplate-icon-pixels-reduce-"+reduced);
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            await OpenClockSection(f,reduced);
            Assert.Null(OperatorMotion.AnimationOverride);Assert.Equal(!reduced,OperatorMotion.Enabled);
            var original=Named<CheckBox>(f.Window,"SettingsHideTeamNamesToggle");
            await RevealHideTeamNames(f,original);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
            // Current remembered preference is not an assumed starting state.
            // Arrange OFF only through its actual native provider, before trace
            // and event-count observations; never set IsChecked/IsActive/Fill.
            if(original.IsChecked==true)provider.Toggle();
            await Settle(750);
            Assert.False(original.IsChecked);Assert.False(HideTeamNamesValue(f));Assert.False(HideTeamNamesPreference(f));
            string scores=Scores(f.Match);int changes=0;original.IsCheckedChanged+=(_,_)=>changes++;
            var symbol=new OperatorSymbol
            {
                Kind="hide-team-names",Width=84,Height=84,Foreground=Brushes.White,
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center
            };
            binding=symbol.Bind(OperatorSymbol.IsActiveProperty,new Binding(nameof(CheckBox.IsChecked))
                {Source=original,Mode=BindingMode.OneWay});
            host=new Window
            {
                Width=120,Height=120,CanResize=false,Background=Brushes.Black,
                Content=new Grid{Background=Brushes.Black,Children={symbol}}
            };
            Mr7IconPixelFrame Read(string source)=>new(stage,source,watch.Elapsed.TotalMilliseconds,
                symbol.FillProgress,Mr5FinalsDiagnostic(symbol,"Pulse"),Mr5FinalsDiagnostic(symbol,"Turn"),symbol.IsActive,original.IsChecked==true);
            observer=(_,_)=>trace.Add(Read(dispatching?"NativeRendering-during-provider":"NativeRendering"));
            NativeRendering.Rendering+=observer;host.Show();
            await Mr5FinalsObserveUntil(()=>symbol.IsLoaded&&symbol.Bounds.Width==84&&symbol.Bounds.Height==84
                &&symbol.FillProgress==0&&trace.Count(x=>x.Source=="NativeRendering")>=3,
                "Actual bound production symbol must load, measure and reach visible-name endpoint.");
            Assert.Equal(Colors.White,Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground).Color);
            Assert.Equal(1d,symbol.Opacity);Assert.False(symbol.IsHitTestVisible);Assert.False(symbol.Focusable);
            var visible=Mr7CaptureNameplateIcon(host,symbol,prefix+"-01-visible.png","visible");captures.Add(visible);
            Assert.Equal(0d,visible.FillBefore);Assert.Equal(0d,visible.FillAfter);
            Assert.True(visible.Names.InkPixels>=100,"Left region must contain actual nameplate ink, not just empty host pixels.");
            Assert.True(visible.Names.Energy>0);Assert.True(visible.Scores.InkPixels>=80);
            void Authority(bool hidden,int expected)
            {
                Assert.Equal(expected,changes);Assert.Equal(hidden,original.IsChecked==true);
                Assert.Equal(hidden,HideTeamNamesValue(f));Assert.Equal(hidden,HideTeamNamesPreference(f));
                Assert.Equal(hidden,symbol.IsActive);Assert.Equal(scores,Scores(f.Match));
                Assert.Equal(0d,Mr5FinalsDiagnostic(symbol,"Pulse"));Assert.Equal(0d,Mr5FinalsDiagnostic(symbol,"Turn"));
                trace.Add(Read("post-provider-synchronous-authority"));
            }
            void Toggle(){dispatching=true;try{provider.Toggle();}finally{dispatching=false;}}
            stage="hide";Toggle();Authority(true,1);
            if(!reduced)
            {
                // Demand a real production callback and a CURRENT interior
                // pose. No synthetic Frame call, test-time progress setter,
                // pause/freeze or replayed old sample manufactures this image.
                await Mr5FinalsObserveUntil(()=>symbol.FillProgress>.15&&symbol.FillProgress<.75
                    &&trace.Any(x=>x.Stage=="hide"&&x.Source=="NativeRendering"&&x.Fill>.05&&x.Fill<.9),
                    "Need an actual current interior hide pose before capturing icon-part pixels.");
                var middle=Mr7CaptureNameplateIcon(host,symbol,prefix+"-02-interior.png","interior");captures.Add(middle);
                Assert.InRange(middle.FillBefore,.15,.85);Assert.InRange(middle.FillAfter,.15,.85);
                Mr7SameFixedScorePixels(visible,middle);
                Assert.InRange(middle.Names.Energy/(double)visible.Names.Energy,.02,.95);
                Assert.True(middle.Names.InkPixels>0);
                Assert.True(middle.Names.RightmostInkX<visible.Names.RightmostInkX-2,
                    "Nameplate ink must retract spatially; an unchanged plate merely dimming is insufficient.");
                await Mr5FinalsObserveUntil(()=>symbol.FillProgress==1,"Real selected-state icon animation must finish naturally.");
            }
            else Assert.Equal(1d,symbol.FillProgress); // Same input turn; no transition allowed.
            var hidden=Mr7CaptureNameplateIcon(host,symbol,prefix+"-03-hidden.png","hidden");captures.Add(hidden);
            Assert.Equal(1d,hidden.FillBefore);Assert.Equal(1d,hidden.FillAfter);
            Mr7SameFixedScorePixels(visible,hidden);
            Assert.Equal(0,hidden.Names.InkPixels);Assert.Equal(0L,hidden.Names.Energy);
            Authority(true,1);
            stage="restore";Toggle();Authority(false,2);
            if(reduced)Assert.Equal(0d,symbol.FillProgress);
            else await Mr5FinalsObserveUntil(()=>symbol.FillProgress==0&&!symbol.IsAnimating
                &&trace.Count(x=>x.Stage=="restore"&&x.Source=="NativeRendering"&&x.Fill==0)>=2,
                "Original OFF must complete and paint two actual idle endpoint frames before comparing restored pixels.");
            var restored=Mr7CaptureNameplateIcon(host,symbol,prefix+"-04-restored.png","restored");captures.Add(restored);
            Assert.Equal(0d,restored.FillBefore);Assert.Equal(0d,restored.FillAfter);
            Mr7SameFixedScorePixels(visible,restored);
            Assert.Equal(visible.Names.Pixels,restored.Names.Pixels);Assert.Equal(visible.Names.Rgb,restored.Names.Rgb);
            Authority(false,2);
            Assert.All(trace,x=>{Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);});
            if(reduced)Assert.All(trace.Where(x=>x.Source!="NativeRendering-during-provider"),x=>Assert.True(x.Fill==0||x.Fill==1));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            // Capture metadata/masks even on failure. These are actual current
            // frames; durations/gaps are not a uniform-frame-rate claim.
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(new
            {
                scope="Linux headless standalone production OperatorSymbol84px; actual original-checkbox provider/binding source; NOT original-template/native-WPF pixel motion",
                reduced,trace,captures=captures.Select(x=>new{x.Stage,x.Path,x.FillBefore,x.FillAfter,x.ClientSize,x.BitmapWidth,x.BitmapHeight,x.SymbolBounds,
                    names=new{x.Names.Pixels,x.Names.Energy,x.Names.InkPixels,x.Names.RightmostInkX,sha256=Convert.ToHexString(SHA256.HashData(x.Names.Rgb))},
                    scores=new{x.Scores.Pixels,x.Scores.Energy,x.Scores.InkPixels,sha256=Convert.ToHexString(SHA256.HashData(x.Scores.Rgb))}})
            },new JsonSerializerOptions{WriteIndented=true}));
            binding?.Dispose();host?.Close();
            OperatorAppearance.ConfigureMotionStyle(previousStyle);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
