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
    private sealed record ClearPaintFrame(string Stage,Matrix Shell,Matrix Content,Matrix Title,double ContentOpacity);

    [AvaloniaTheory][InlineData("Expressive")][InlineData("Calm")][InlineData("Reduce")]
    public async Task CompactClearReviewKeepsLongContextAndActionsReachableWhileOnlyItsShellMorphs(string policy)
    {
        await using var f=await Fixture.Create(420,320);
        // Match MainWindow's finite client-height host. A generic NativeWindow
        // wraps arbitrary Grid content in an unbounded recovery ScrollViewer;
        // that is not the operator-window layout contract under review.
        var fixtureHost=Assert.Single(f.Panel.GetVisualAncestors().OfType<Grid>(),grid=>grid.Children.Contains(f.Panel)&&grid.Children.Contains(f.OtherScope));
        fixtureHost.Height=f.Window.ClientSize.Height;await Settle(80);
        var oldStyle=OperatorAppearance.MotionStyle;bool oldReduce=OperatorAppearance.ReduceMotion;
        bool? oldOverride=OperatorMotion.AnimationOverride;EventHandler? observer=null;
        var frames=new List<ClearPaintFrame>();string stage="initial";
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-clear-layout-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,policy=="Reduce");OperatorMotion.AnimationOverride=null;
            f.Active.Title=string.Concat(Enumerable.Repeat("Long current drill with additional operator context — ",8));
            f.Panel.Bind(f.Session);await Settle(80);
            string before=ClearFullState(f.Session);int structural=0;f.Session.StructuralChanged+=()=>structural++;
            var overlay=ClearOverlay(f.Panel);
            var shell=Named<Control>(f.Panel,"TrainingClearReviewShell");
            var content=Named<Control>(f.Panel,"TrainingClearReviewContent");
            var title=Assert.Single(content.GetVisualDescendants().OfType<TextBlock>(),text=>text.Text=="Clear all timers?");
            observer=(_,_)=>
            {
                if(overlay.IsVisible)frames.Add(new(stage,shell.RenderTransform?.Value??Matrix.Identity,
                    content.RenderTransform?.Value??Matrix.Identity,title.RenderTransform?.Value??Matrix.Identity,content.Opacity));
            };
            NativeRendering.Rendering+=observer;
            await OpenStoppedClearReview(f,f.Session);
            using(var image=f.Window.CaptureRenderedFrame()){Assert.NotNull(image);image.Save(Path.Combine(directory,"clear-long-first-"+policy+".png"));}
            var cancel=ClearReviewAction(f.Panel,"training-clear-cancel");var confirm=ClearReviewAction(f.Panel,"training-clear-confirm");
            ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);
            File.WriteAllText(Path.Combine(directory,"clear-geometry-"+policy+".json"),JsonSerializer.Serialize(
                title.GetVisualAncestors().OfType<Control>().Prepend(title).Select(control=>new{control.Name,Type=control.GetType().Name,control.Bounds,control.DesiredSize,Origin=control.TranslatePoint(default,f.Window),MaxHeight=double.IsFinite(control.MaxHeight)?control.MaxHeight:(double?)null,control.VerticalAlignment}),new JsonSerializerOptions{WriteIndented=true}));
            var titleOrigin=title.TranslatePoint(default,f.Window);Assert.NotNull(titleOrigin);
            Assert.True(titleOrigin.Value.Y>=0 && titleOrigin.Value.Y+title.Bounds.Height<=f.Window.ClientSize.Height,
                $"The review heading must remain visible: top={titleOrigin.Value.Y}, height={title.Bounds.Height}, window={f.Window.ClientSize.Height}.");
            Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);
            var context=Named<TextBlock>(f.Panel,"TrainingClearReviewContext");
            Assert.Contains(f.Active.Title,context.Text);
            Assert.DoesNotContain(context.TextLayout.TextLines,line=>line.HasCollapsed);
            Assert.True(context.TextLayout.Width<=context.Bounds.Width+.01);
            var details=Named<ScrollViewer>(f.Panel,"TrainingClearReviewScroll");
            Assert.True(details.Extent.Height>details.Viewport.Height,"The fixture must really need context scrolling.");
            double beforeScroll=details.Offset.Y;
            Key(f.Window,PhysicalKey.PageDown);await Settle(80);
            Assert.True(details.Offset.Y>beforeScroll,"Keyboard PageDown must reveal long review context without moving its actions.");
            Assert.True(cancel.IsFocused);ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);
            Key(f.Window,PhysicalKey.PageUp);await Settle(80);
            Assert.Equal(beforeScroll,details.Offset.Y);Assert.True(cancel.IsFocused);
            Key(f.Window,PhysicalKey.End);await Settle(80);
            Assert.Equal(details.Extent.Height-details.Viewport.Height,details.Offset.Y,3);
            Assert.True(cancel.IsFocused);ActualPoint(f.Window,cancel);ActualPoint(f.Window,confirm);
            Key(f.Window,PhysicalKey.Home);await Settle(80);
            Assert.Equal(0,details.Offset.Y);Assert.True(cancel.IsFocused);
            Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);


            await Settle(650);
            Assert.NotEmpty(frames);
            Assert.All(frames,frame=>{Assert.Equal(Matrix.Identity,frame.Content);Assert.Equal(Matrix.Identity,frame.Title);});
            if(policy=="Reduce")Assert.All(frames,frame=>{Assert.Equal(Matrix.Identity,frame.Shell);Assert.Equal(1d,frame.ContentOpacity);});
            else Assert.Contains(frames,frame=>frame.Shell!=Matrix.Identity);
            Assert.Equal(Matrix.Identity,shell.RenderTransform?.Value??Matrix.Identity);Assert.Equal(1d,content.Opacity);
            using(var image=f.Window.CaptureRenderedFrame()){Assert.NotNull(image);image.Save(Path.Combine(directory,"clear-long-settled-"+policy+".png"));}
            Key(f.Window,PhysicalKey.End);await Settle(60);Assert.True(details.Offset.Y>0);
            var point=ActualPoint(f.Window,cancel);f.Window.MouseMove(point);f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);
            Assert.False(overlay.IsVisible);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);

            // A second request is immediately cancelled before its entry can
            // finish; no animation completion may reopen it or clear the queue.
            stage="interrupt";var clear=Named<Button>(f.Panel,"ClearAllButton");await TabTo(f.Window,clear);
            Key(f.Window,PhysicalKey.Space);Assert.True(overlay.IsVisible);await Settle(40);
            Assert.Equal(0,details.Offset.Y); // A new review starts at its own current context, not a previous scroll position.
            Key(f.Window,PhysicalKey.Escape);
            Assert.False(overlay.IsVisible);await Settle(650);
            Assert.False(overlay.IsVisible);Assert.Equal(before,ClearFullState(f.Session));Assert.Equal(0,structural);Assert.Equal(0,f.Exits);
            File.WriteAllText(Path.Combine(directory,"clear-layout-motion-"+policy+".json"),JsonSerializer.Serialize(new{policy,frames},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;
        }
    }
}
