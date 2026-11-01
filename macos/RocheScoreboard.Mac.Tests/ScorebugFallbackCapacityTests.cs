// Actual output capacity regression; fixture setup limitation documented below.
// Existing Fixture/SBReadReady uses scoped private setup finalization, not a
// physically completed Home wizard. Actual output/controller and original live
// team TextChanged route are used; no private fit/animation handler is called.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class PresentationLifetimeTests
{
    private static Rect FallbackQuad(Window output,Control source,double width,double height)
    {
        var corners=new[]{new Point(),new Point(width,0),new Point(0,height),new Point(width,height)}
            .Select(point=>source.TranslatePoint(point,output)).ToArray();
        Assert.All(corners,point=>Assert.NotNull(point));
        double x=corners.Min(point=>point!.Value.X),y=corners.Min(point=>point!.Value.Y);
        return new Rect(x,y,corners.Max(point=>point!.Value.X)-x,corners.Max(point=>point!.Value.Y)-y);
    }
    private sealed record FallbackObservation(string Side,string? Text,double FontSize,string FontFamily,
        double NaturalWidth,double NaturalHeight,Rect GlyphQuad,Rect HostQuad,Rect CellQuad,
        double WidthScale,bool ImageVisible,bool FallbackVisible,int TextLines,bool CollapsedLine);

    [AvaloniaTheory]
    [InlineData(ScorebugLayout.Expanded,"HOM","AWA")]
    [InlineData(ScorebugLayout.Expanded,"WWWW","MMMM")]
    [InlineData(ScorebugLayout.Distance,"HOM","AWA")]
    [InlineData(ScorebugLayout.Distance,"WWWW","MMMM")]
    public async Task CompleteSupportedFallbackAbbreviationsFitTheirLogoCellsAtCompactAndNormalOutputSizes(
        ScorebugLayout layout,string home,string away)
    {
        using var motion=new SBReadMotionScope(true);using var f=new Fixture();await SBReadReady(f,layout);
        var match=SBReadMatch(f);var bug=SBReadBug(f);var output=f.Output();
        // Public fields' TextChanged invokes the existing live team styling
        // path. This is fixture data input, not physical typing coverage.
        SBReadNamed<TextBox>(f.Window,"HomeAbbrBox").Text=home;
        SBReadNamed<TextBox>(f.Window,"AwayAbbrBox").Text=away;
        await SBReadPump(250);
        Assert.Equal(home,match.HomeAbbr);Assert.Equal(away,match.AwayAbbr);
        Assert.False(match.ClockRunning);Assert.Empty(match.Events);
        string before=JsonSerializer.Serialize(match.ToState());
        string directory=SBReadDirectory($"fallback-{layout}-{home}-{away}");
        var evidence=new List<object>();
        foreach(var size in new[]{new PixelSize(386,193),new PixelSize(1280,720),new PixelSize(386,193)})
        {
            output.Width=size.Width;output.Height=size.Height;
            // Pump real layout/dispatcher work; no forced clipping/visibility,
            // hand-written Measure/Arrange, or fake private fit call.
            await SBReadPump(350);
            var rows=new List<FallbackObservation>();
            foreach(var (side,expected) in new[]{("Home",home),("Away",away)})
            {
                var text=SBReadNamed<TextBlock>(bug,"Exp"+side+"LogoFallback");
                var host=SBReadNamed<Grid>(bug,"Exp"+side+"LogoHost");
                var cell=SBReadNamed<Border>(bug,"Exp"+side+"LogoCell");
                var image=SBReadNamed<Image>(bug,"Exp"+side+"LogoImage");
                Assert.True(text.IsEffectivelyVisible);Assert.False(image.IsVisible);
                Assert.Equal(expected,text.Text);Assert.Equal(32d,text.FontSize);
                // Full natural glyph-run rect: control Bounds alone may be
                // clipped/constrained and cannot establish complete text fit.
                double naturalWidth=text.TextLayout.WidthIncludingTrailingWhitespace;
                double naturalHeight=text.TextLayout.Height;
                Assert.True(naturalWidth>0&&naturalHeight>0);
                var glyph=FallbackQuad(output,text,naturalWidth,naturalHeight);
                var hostQuad=FallbackQuad(output,host,host.Bounds.Width,host.Bounds.Height);
                var cellQuad=FallbackQuad(output,cell,cell.Bounds.Width,cell.Bounds.Height);
                rows.Add(new(side,text.Text,text.FontSize,text.FontFamily.ToString(),naturalWidth,naturalHeight,
                    glyph,hostQuad,cellQuad,glyph.Width/naturalWidth,image.IsVisible,text.IsEffectivelyVisible,
                    text.TextLayout.TextLines.Count(),text.TextLayout.TextLines.Any(line=>line.HasCollapsed)));
            }
            // Persist actual pre-assertion evidence, including genuine baseline
            // failures. Normal-size assertions may be reached only after fix.
            evidence.Add(new{size,rows});SBReadFrame(output,directory,$"{evidence.Count:00}-{size.Width}x{size.Height}");
            File.WriteAllText(Path.Combine(directory,"fallback-fit.json"),JsonSerializer.Serialize(
                new{scope="Linux headless original production scorebug; native desktop/font/hardware unrun",layout,home,away,evidence},
                new JsonSerializerOptions{WriteIndented=true}));
            foreach(var row in rows)
            {
                Assert.Equal(1,row.TextLines);Assert.False(row.CollapsedLine);
                Assert.True(row.HostQuad.Inflate(.3).Contains(row.GlyphQuad),
                    row.Side+" complete fallback glyph run must fit actual inner logo host; inspect fallback-fit.json.");
                Assert.True(row.CellQuad.Inflate(.3).Contains(row.GlyphQuad));
                Assert.True(new Rect(output.ClientSize).Inflate(.3).Contains(row.GlyphQuad));
                Assert.True(row.WidthScale<=1.001,"DownOnly fitting must never enlarge beyond design size.");
                if(size.Width==1280)Assert.InRange(row.WidthScale,.999,1.001);
            }
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
            Assert.False(bug.IsScoreAnimating);
        }
    }
}
