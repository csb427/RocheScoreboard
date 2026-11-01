using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using System.Text.Json;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class CalibrationCompactTests
{
    private static Rect Rendered(Control c,Visual target)
    {
        var first=c.TranslatePoint(default,target);var last=c.TranslatePoint(new Point(c.Bounds.Width,c.Bounds.Height),target);
        Assert.NotNull(first);Assert.NotNull(last);return new Rect(first.Value,last.Value);
    }
    [AvaloniaTheory] [InlineData(386,193)] [InlineData(772,386)]
    [Trait("Evidence","CalibrationCompact")]
    public async Task StaticCalibrationKeepsSampleReadoutAndExitHintSeparate(int width,int height)
    {
        SelfTestStorage.Initialize(true);var overlay=new CalibrationOverlay();
        var window=new NativeWindow{Title="Calibration Display",Width=width,Height=height,Content=overlay};
        try
        {
            window.Show();await Task.Delay(60);Dispatcher.UIThread.RunJobs();
            var proof=Assert.Single(overlay.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text is "DISTANCE READABILITY TEST" or "NUMERAL FIT SAMPLE").GetVisualAncestors().OfType<Border>().First();
            var hint=Assert.Single(overlay.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.Contains("Ctrl+Shift+C")==true).GetVisualAncestors().OfType<Border>().First();
            var readout=overlay.FindControl<TextBlock>("SizeReadout")!.GetVisualAncestors().OfType<Border>().First();
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
            {Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,"calibration-"+width+".png"));
                File.WriteAllText(Path.Combine(dir,"geometry-"+width+".json"),JsonSerializer.Serialize(new{component=overlay.Bounds,window=window.ClientSize,proof=Rendered(proof,window),hint=Rendered(hint,window),readout=Rendered(readout,window)},new JsonSerializerOptions{WriteIndented=true}));}
            var p=Rendered(proof,window);var h=Rendered(hint,window);var r=Rendered(readout,window);var client=new Rect(window.ClientSize);
            Assert.True(client.Contains(p),"Numeral sample must fit the actual compact client.");Assert.True(client.Contains(h));Assert.True(client.Contains(r));
            Assert.False(p.Intersects(h),"Numeral proof must not cover the exit hint.");Assert.False(p.Intersects(r),"Numeral proof must not cover the size readout.");
            Assert.Contains(width+" x "+height,overlay.FindControl<TextBlock>("SizeReadout")!.Text);
            Assert.False(overlay.IsHitTestVisible); // Metadata only; native hit-through remains manual.
            var old=p;await Task.Delay(100);Assert.Equal(old,Rendered(proof,window));
        }
        finally{window.Close();}
    }
}
