// OUTSIDE COMPONENT BASELINE ONLY:13 cases, UNCOMPILED/UNRUN.
// Public mutable/immutable brushes and actual rendered pixels. No private tint
// invocation, MainWindow/data fixtures, preference writes, material shader substitution or OS-HC emulation.
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
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class OperatorGlassBrushCompatibilityTests
{
    private static readonly Color Authored=Color.Parse("#F3F3F7");
    private static readonly Color Matte=Color.Parse("#136F54");
    private sealed record PixelPair(double U,double V,string Mutable,string Immutable,int PairDelta,int MutableToMatte);
    private sealed class TwinHost : IDisposable
    {
        internal readonly NativeWindow Window;
        internal readonly OperatorGlassSurface Left,Right;
        internal TwinHost(IBrush? left,IBrush? right,GlassMaterial material,IBrush? leftBorder=null,IBrush? rightBorder=null,
            double thickness=0,IBrush? leftResource=null,IBrush? rightResource=null)
        {
            Window=new NativeWindow{Width=320,Height=150,Background=new SolidColorBrush(Matte)};
            try
            {
                OperatorGlassSurface Face(IBrush? brush,IBrush? border,double x,IBrush? resource)
                {
                    var surface=new OperatorGlassSurface
                    {
                        Width=110,Height=90,Margin=new Thickness(x,20,0,0),
                        HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top,
                        Background=brush,BorderBrush=border,BorderThickness=new Thickness(thickness),CornerRadius=new CornerRadius(8),Material=material
                    };
                    surface.Resources["ControlCentre.SurfaceBrush"]=resource??new SolidColorBrush(Authored);
                    return surface;
                }
                Left=Face(left,leftBorder,20,leftResource);Right=Face(right,rightBorder,180,rightResource);
                var stage=new Grid{Background=new SolidColorBrush(Matte)};stage.Children.Add(Left);stage.Children.Add(Right);
                Window.Content=stage;Window.Show();
            }
            catch {Window.Close();throw;}
        }
        internal async Task Ready()
        {
            for(int i=0;i<50&&(!Left.IsEffectivelyVisible||!Right.IsEffectivelyVisible||Left.Bounds.Width<100||Right.Bounds.Width<100);i++)
            {await Task.Delay(20);Dispatcher.UIThread.RunJobs();}
            Assert.True(Left.IsEffectivelyVisible&&Right.IsEffectivelyVisible);
            Assert.Equal(Left.Bounds.Size,Right.Bounds.Size);Assert.Equal(new Size(110,90),Left.Bounds.Size);
            // Static pixel capture readiness only; no expected animation phase.
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();
        }
        public void Dispose()=>Window.Close();
    }
    private static IBrush Brush(string family,bool immutable)
    {
        IBrush brush=family switch
        {
            "Solid"=>new SolidColorBrush(Authored),
            "Linear"=>new LinearGradientBrush{GradientStops=new GradientStops{new GradientStop(Authored,0),new GradientStop(Color.Parse("#D1DAE7"),1)}},
            "Radial"=>new RadialGradientBrush{GradientStops=new GradientStops{new GradientStop(Authored,0),new GradientStop(Color.Parse("#D1DAE7"),1)}},
            "EmptyLinear"=>new LinearGradientBrush(),
            _=>throw new ArgumentException(family)
        };
        return immutable?brush.ToImmutable():brush;
    }
    private static int Delta(SKColor a,SKColor b)=>Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)));
    private static SKColor PixelAt(SKBitmap frame,TwinHost host,Control control,double u,double v)
    {
        var point=control.TranslatePoint(new Point(control.Bounds.Width*u,control.Bounds.Height*v),host.Window);Assert.NotNull(point);
        Assert.True(new Rect(host.Window.ClientSize).Contains(point.Value));
        double sx=frame.Width/host.Window.ClientSize.Width,sy=frame.Height/host.Window.ClientSize.Height;
        return frame.GetPixel(Math.Clamp((int)Math.Round(point.Value.X*sx),0,frame.Width-1),Math.Clamp((int)Math.Round(point.Value.Y*sy),0,frame.Height-1));
    }
    private static async Task Observe(string name,IBrush? left,IBrush? right,GlassMaterial material,
        Color? expected,IBrush? leftBorder=null,IBrush? rightBorder=null,double thickness=0,IBrush? leftResource=null,IBrush? rightResource=null,bool materialExpected=true)
    {
        bool? animation=OperatorMotion.AnimationOverride;
        TwinHost? host=null;
        try
        {
            // Preconditions do not change persisted preferences. This environment
            // does NOT emulate actual macOS Increased Contrast or Windows HC.
            Assert.False(NativeSystemPreferences.HighContrast,"This baseline is the normal native-adapter rendering branch, not OS-HC acceptance.");
            Assert.True(OperatorAppearance.GlassEnabled,"This component baseline requires the current glass-enabled preference; it does not configure or save preferences.");
            OperatorMotion.AnimationOverride=false;
            host=new TwinHost(left,right,material,leftBorder,rightBorder,thickness,leftResource,rightResource);await host.Ready();
            Assert.Same(left,host.Left.Background);Assert.Same(right,host.Right.Background);
            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-glass-brush-baseline");Directory.CreateDirectory(directory);
            using var rendered=host.Window.CaptureRenderedFrame();Assert.NotNull(rendered);rendered.Save(Path.Combine(directory,name+".png"));
            using var bytes=new MemoryStream();rendered.Save(bytes);using var frame=SKBitmap.Decode(bytes.ToArray());Assert.NotNull(frame);
            var receipts=new List<PixelPair>();var actuals=new List<(SKColor Left,SKColor Right)>();
            SKColor matte=new(Matte.R,Matte.G,Matte.B,255);
            foreach(double v in new[]{.32,.5,.68})foreach(double u in new[]{.32,.5,.68})
            {
                SKColor a=PixelAt(frame,host,host.Left,u,v),b=PixelAt(frame,host,host.Right,u,v);
                Assert.Equal((byte)255,a.Alpha);Assert.Equal((byte)255,b.Alpha);
                receipts.Add(new(u,v,a.ToString(),b.ToString(),Delta(a,b),Delta(a,matte)));actuals.Add((a,b));
            }
            File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new
            {
                Scope="Actual static component pixels, Linux headless native adapter. No private BaseTint or OS-HC invocation.",
                Material=material.ToString(),LeftType=left?.GetType().FullName,RightType=right?.GetType().FullName,
                LeftBorder=leftBorder?.GetType().FullName,RightBorder=rightBorder?.GetType().FullName,
                LeftResource=leftResource?.GetType().FullName,RightResource=rightResource?.GetType().FullName,
                Expected=expected?.ToString(),host.Window.ClientSize,LeftBounds=host.Left.Bounds,RightBounds=host.Right.Bounds,Pixels=receipts
            },new JsonSerializerOptions{WriteIndented=true}));
            // Exact semantic-color reference for opaque mode prevents two wrong
            // fallback paints from satisfying equality. Regular mode additionally
            // requires a real material contribution above the matte.
            if(expected is Color colour)
            {
                SKColor target=new(colour.R,colour.G,colour.B,255);
                Assert.All(actuals,pair=>Assert.InRange(Delta(pair.Left,target),0,1));
                Assert.All(actuals,pair=>Assert.InRange(Delta(pair.Right,target),0,1));
            }
            Assert.All(receipts,pixel=>Assert.InRange(pixel.PairDelta,0,1));
            if(materialExpected)Assert.Contains(receipts,pixel=>pixel.MutableToMatte>=10);
            else Assert.All(receipts,pixel=>Assert.InRange(pixel.MutableToMatte,0,1));
        }
        finally
        {
            try{host?.Dispose();}
            finally{OperatorMotion.AnimationOverride=animation;}
        }
    }

    [AvaloniaTheory]
    [InlineData("Solid",GlassMaterial.Opaque)][InlineData("Linear",GlassMaterial.Opaque)]
    [InlineData("Radial",GlassMaterial.Opaque)][InlineData("EmptyLinear",GlassMaterial.Opaque)]
    [InlineData("Solid",GlassMaterial.Regular)][InlineData("Linear",GlassMaterial.Regular)]
    [InlineData("Radial",GlassMaterial.Regular)][InlineData("EmptyLinear",GlassMaterial.Regular)]
    public async Task MutableAndImmutableBrushesPreserveEquivalentActualMaterialPixels(string family,GlassMaterial material)
    {
        IBrush mutable=Brush(family,false),immutable=Brush(family,true);Assert.NotSame(mutable,immutable);
        Assert.False(immutable is SolidColorBrush or GradientBrush);
        Color? expected=material==GlassMaterial.Opaque?(family=="EmptyLinear"?Color.FromRgb(242,246,252):Authored):null;
        await Observe("glass-brush-"+family+"-"+material,mutable,immutable,material,expected);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task PublicImmutableSurfaceResourceMatchesMutableLightAndLowAlphaFallback(bool lowAlphaWhite)
    {
        IBrush? background=lowAlphaWhite?new SolidColorBrush(Color.FromArgb(30,255,255,255)):null;
        Color expected=lowAlphaWhite?Authored:Color.FromRgb(242,246,252);
        await Observe("glass-resource-"+(lowAlphaWhite?"white-fallback":"light-detection"),background,background,GlassMaterial.Opaque,expected,
            leftResource:new SolidColorBrush(Authored),rightResource:new SolidColorBrush(Authored).ToImmutable());
    }

    [AvaloniaFact]
    public async Task AlphaZeroMutableAndImmutableBackgroundsBothExposeTheActualMatte()
    {
        var transparent=new SolidColorBrush(Colors.Transparent);
        await Observe("glass-transparent-background",transparent,transparent.ToImmutable(),GlassMaterial.Opaque,Matte,materialExpected:false);
    }

    [AvaloniaFact]
    public async Task AlphaZeroBorderReadDoesNotInventMaterialForAnImmutableTransparentBorder()
    {
        var transparent=new SolidColorBrush(Colors.Transparent);
        await Observe("glass-transparent-border",transparent,transparent,GlassMaterial.Opaque,Matte,
            leftBorder:transparent,rightBorder:transparent.ToImmutable(),thickness:2,materialExpected:false);
    }

    [AvaloniaFact]
    public async Task TransparentBackgroundWithARealColoredBorderStillRetainsItsMaterial()
    {
        var transparent=new SolidColorBrush(Colors.Transparent);var border=new SolidColorBrush(Color.Parse("#245AB8"));
        await Observe("glass-transparent-colored-border-control",transparent,transparent.ToImmutable(),GlassMaterial.Opaque,
            Color.FromRgb(242,246,252),leftBorder:border,rightBorder:border.ToImmutable(),thickness:2);
    }
}
