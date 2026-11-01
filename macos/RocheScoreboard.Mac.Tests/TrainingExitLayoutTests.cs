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
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class TrainingQueueCommandRegressionTests
{
    private sealed record ExitPaint(Matrix Shell,Matrix Content,Matrix Heading,double Opacity);
    [AvaloniaTheory]
    [InlineData(420,320,"Expressive")][InlineData(420,320,"Calm")][InlineData(420,320,"Reduce")]
    [InlineData(980,780,"Expressive")][InlineData(980,780,"Calm")][InlineData(980,780,"Reduce")]
    public async Task ExitReviewKeepsItsOwnHeadingStatusContextAndActionsReadableAndItsMotionInterruptible(int width,int height,string policy)
    {
        await using var f=await Fixture.Create(width,height);
        var host=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),g=>g.Children.Contains(f.Panel)&&g.Children.Contains(f.OtherScope));
        host.Height=f.Window.ClientSize.Height;await Settle(70); // Same finite operator-host contract as Clear's reviewed fixture.
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;bool? oldOverride=OperatorMotion.AnimationOverride;
        EventHandler? observer=null;var frames=new List<ExitPaint>();
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-exit-layout-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string tag=$"exit-{width}-{policy}";
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,policy=="Reduce");OperatorMotion.AnimationOverride=null;
            f.Active.Title=string.Concat(Enumerable.Repeat("Current drill with meaningful long operator context — ",10));f.Panel.Bind(f.Session);await Settle(70);
            string state=ClearFullState(f.Session);int structural=0;f.Session.StructuralChanged+=()=>structural++;
            var exit=Named<Button>(f.Panel,"ExitButton");await TabTo(f.Window,exit);
            using(var before=f.Window.CaptureRenderedFrame()){Assert.NotNull(before);before.Save(Path.Combine(dir,tag+"-before-request.png"));}
            var overlay=Named<Border>(f.Panel,"TrainingExitReviewOverlay");var shell=Named<Control>(f.Panel,"TrainingExitReviewShell");
            var content=Named<Grid>(f.Panel,"TrainingExitReviewContent");var heading=Named<TextBlock>(f.Panel,"TrainingExitReviewHeading");var status=Named<TextBlock>(f.Panel,"TrainingExitReviewStatus");
            observer=(_,_)=>{if(overlay.IsVisible)frames.Add(new(shell.RenderTransform?.Value??Matrix.Identity,content.RenderTransform?.Value??Matrix.Identity,heading.RenderTransform?.Value??Matrix.Identity,content.Opacity));};
            NativeRendering.Rendering+=observer;
            Key(f.Window,PhysicalKey.Space);Assert.True(overlay.IsVisible);await Settle(50);
            var cancel=ExitReviewCommand(f.Panel,"training-exit-cancel");var confirm=ExitReviewCommand(f.Panel,"training-exit-confirm");
            var details=Named<ScrollViewer>(f.Panel,"TrainingExitReviewScroll");var context=Named<TextBlock>(f.Panel,"TrainingExitReviewContext");
            var footer=Assert.Single(overlay.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.StartsWith("This Training session cannot be resumed") is true);
            Rect Box(Control c){var origin=c.TranslatePoint(default,f.Window);Assert.NotNull(origin);return new Rect(origin.Value,c.Bounds.Size);}
            File.WriteAllText(Path.Combine(dir,tag+"-geometry.json"),JsonSerializer.Serialize(new{window=f.Window.ClientSize,heading=Box(heading),status=Box(status),contextViewport=Box(details),details.Viewport,details.Extent,footer=Box(footer),cancel=Box(cancel),confirm=Box(confirm)},new JsonSerializerOptions{WriteIndented=true}));
            using(var first=f.Window.CaptureRenderedFrame()){Assert.NotNull(first);first.Save(Path.Combine(dir,tag+"-first.png"));}
            foreach(var text in new[]{heading,status,footer})
            {
                Assert.True(new Rect(f.Window.ClientSize).Contains(Box(text)),"Complete heading/status/consequence must remain inside the owner: "+text.Text);
                Assert.DoesNotContain(text.TextLayout.TextLines,line=>line.HasCollapsed);Assert.True(text.TextLayout.Width<=text.Bounds.Width+.01);
            }
            Assert.True(details.Viewport.Height>=20,"The Exit review must retain at least one complete readable context line, not a zero-height scroll region.");
            Assert.True(Box(status).Bottom<=Box(details).Top+.01);Assert.True(Box(details).Bottom<=Box(footer).Top+.01);
            ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);Assert.True(cancel.IsFocused);Assert.True(cancel.IsDefault);Assert.False(confirm.IsDefault);
            Assert.Contains(f.Active.Title,context.Text);Assert.Equal(state,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(0,f.Exits);
            if(width==420)
            {
                Assert.True(details.Extent.Height>details.Viewport.Height);
                Key(f.Window,PhysicalKey.End);await Settle(70);Assert.Equal(details.Extent.Height-details.Viewport.Height,details.Offset.Y,3);
                Assert.True(cancel.IsFocused);ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);
                Key(f.Window,PhysicalKey.Home);await Settle(70);Assert.Equal(0,details.Offset.Y);Assert.True(cancel.IsFocused);
            }
            await Settle(650);Assert.NotEmpty(frames);Assert.All(frames,p=>{Assert.Equal(Matrix.Identity,p.Content);Assert.Equal(Matrix.Identity,p.Heading);});
            if(policy=="Reduce")Assert.All(frames,p=>{Assert.Equal(Matrix.Identity,p.Shell);Assert.Equal(1d,p.Opacity);});else Assert.Contains(frames,p=>p.Shell!=Matrix.Identity);
            Assert.Equal(Matrix.Identity,shell.RenderTransform?.Value??Matrix.Identity);Assert.Equal(1d,content.Opacity);
            using(var after=f.Window.CaptureRenderedFrame()){Assert.NotNull(after);after.Save(Path.Combine(dir,tag+"-settled.png"));}
            Key(f.Window,PhysicalKey.Escape);Assert.False(overlay.IsVisible);Assert.Equal(state,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(0,f.Exits);
            await TabTo(f.Window,exit);Key(f.Window,PhysicalKey.Space);Assert.True(overlay.IsVisible);Key(f.Window,PhysicalKey.Escape);await Settle(650);
            Assert.False(overlay.IsVisible);Assert.Equal(state,ClearFullState(f.Session));Assert.Equal(0,f.Exits);
            File.WriteAllText(Path.Combine(dir,tag+"-frames.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;
        }
    }
}
