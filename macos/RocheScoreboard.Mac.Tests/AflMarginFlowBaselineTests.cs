// OUTSIDE BEFORE-COMPATIBLE MARGIN FLOW BASELINE: four theme/size cases.
// Original public controls and real output event-step geometry; no future glyph references.
// AflFixture owns/rolls back latched storage and theme data; only motion override is changed here.
// UNAPPLIED, UNCOMPILED, UNRUN. Root owns all integration/execution.
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
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static TextBlock MarginCardOriginalTitle(Control card)=>Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(),
        text=>text.IsEffectivelyVisible&&text.Text=="Margin flow");
    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,1280,850)][InlineData(AppTheme.Light,420,320)]
    public async Task OriginalMarginFlowIsImmediateReadableAndKeepsActualEventHistorySeparateFromOutputOpen(AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var previousTheme=app.ThemeService.CurrentTheme;
        bool? animation=OperatorMotion.AnimationOverride;
        var states=new List<LiveCardBaselineReceipt>();var pixels=new List<object>();var graphReceipts=new List<object>();string? error=null;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-margin-flow-baseline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);TextBlock? title=null;double oldOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);OperatorMotion.AnimationOverride=false;
            Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWorm");var alternate=Named<Control>(f.Window,"PresCardScorebug");
            var windowCard=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,windowCard);
            if(PresWindowOpen(f))PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Actual output must be off before selection/output authority is distinguished.");
            await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","Original scorebug must establish a distinct logical presentation while off.");
            Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await PresPlanReveal(f,card);title=MarginCardOriginalTitle(card);oldOpacity=title.Opacity;
            var border=Assert.IsAssignableFrom<Border>(card);var check=Named<Border>(f.Window,"PresCheckWorm");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title);double font=title.FontSize;
            var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            void State(string stage,bool selected,bool outputOpen)
            {
                LiveCardModelAuthority(f,selected?"worm":"scorebug",outputOpen,model,changes);
                Assert.Equal(selected?1d:0d,check.Opacity);
                Rect currentCard=FinalsPainted(f.Window,card),currentTitle=FinalsPainted(f.Window,title);
                // Deliberate reveal of a different row may change scroll offset.
                // Preserve measured sizes/local caption placement across those
                // routes, plus absolute input geometry within each action below.
                Assert.Equal(fixedCard.Size,currentCard.Size);Assert.Equal(fixedTitle.Size,currentTitle.Size);
                Assert.Equal(fixedTitle.Position-fixedCard.Position,currentTitle.Position-currentCard.Position);
                Assert.Equal(font,title.FontSize);Assert.True(currentCard.Contains(currentTitle));
                Assert.All(FinalsProbes(currentCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                f.Window.MouseMove(new Point(1,1));string path=Path.Combine(directory,$"margin-flow-before-{theme}-{width}-{stage}");
                var ink=Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground);double opacity=Mr5PaletteVisualOpacity(title);
                using var normal=Mr5PaletteCapture(f.Window,path+".png");
                SkiaSharp.SKBitmap backing;
                try{title.Opacity=0;backing=Mr5PaletteCapture(f.Window,path+"-backing.png");}
                finally{title.Opacity=oldOpacity;}
                using(backing)
                {
                    var measured=Mr5PalettePixels(f.Window,title,normal,backing,ink,opacity);
                    Assert.True(measured.Count>=12,"A real rendered original title is required before a readability claim.");
                    pixels.Add(new{stage,Pixels=measured});
                    var expectedBase=Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush"));
                    var expectedInk=Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush"));
                    states.Add(new(stage,selected,outputOpen,peer!.GetName()??"",AutomationProperties.GetItemStatus(card)??"",
                        Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color.ToString(),expectedBase.Color.ToString(),ink.Color.ToString(),expectedInk.Color.ToString(),
                        check.Opacity,font,currentCard,currentTitle,measured.Count,measured.Min(pixel=>pixel.IntendedTextContrast)));
                }
            }
            State("unselected-output-off",false,false);
            // Native semantic keyboard path on the ORIGINAL action control.
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);
            // Tab can scroll to a different child. Reveal/focus the original
            // action and prove the settled full input surface before measuring
            // the subsequent raw Space action; keep its absolute geometry gate.
            await PresPlanReveal(f,card);
            Rect beforeKeyCard=FinalsPainted(f.Window,card),beforeKeyTitle=FinalsPainted(f.Window,title);
            Key(f.Window,PhysicalKey.Space);
            Assert.Equal(beforeKeyCard,FinalsPainted(f.Window,card));Assert.Equal(beforeKeyTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"worm",false,model,changes);State("keyboard-selected-output-off",true,false);
            PresPlanProvider(alternate).Invoke();await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","The original semantic Invoke provider is deliberately dispatcher-posted; observe its accepted scorebug before the next raw pointer action.");
            LiveCardModelAuthority(f,"scorebug",false,model,changes);
            await PresPlanReveal(f,card);
            Rect beforePointerCard=FinalsPainted(f.Window,card),beforePointerTitle=FinalsPainted(f.Window,title);
            PresWindowPointer(f,card);
            Assert.Equal(beforePointerCard,FinalsPainted(f.Window,card));Assert.Equal(beforePointerTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"worm",false,model,changes);State("pointer-selected-output-off",true,false);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>PresWindowOpen(f),"Original Window card must actually open the already chosen margin-flow presentation.");
            await Until(()=>PresPlanRead<Control?>(f,"_scoreworm") is {IsEffectivelyVisible:true},"Actual output must show the real existing margin-history control after explicit activation.");
            var worm=PresPlanRead<Control>(f,"_scoreworm");var graph=Named<Canvas>(worm,"GraphCanvas");
            await Until(()=>graph.Bounds.Width>0&&graph.Bounds.Height>0&&graph.Children.OfType<Avalonia.Controls.Shapes.Polyline>().Any(line=>line.Points.Count==2*f.Match.Events.Count+1),
                "Actual populated graph must be measured and contain the current event-step history before any output-data claim.");
            var line=Assert.Single(graph.Children.OfType<Avalonia.Controls.Shapes.Polyline>());var points=line.Points.ToArray();
            Assert.Equal(2,f.Match.Events.Count);Assert.Equal(6,f.Match.Events[0].Margin);Assert.Equal(5,f.Match.Events[1].Margin);
            Assert.Equal(f.Match.Events[0].GameTime,f.Match.Events[1].GameTime);
            Assert.Equal(5,points.Length);Assert.Equal(0d,points[0].X,3);
            // Horizontal holds and vertical score jumps are event-indexed,
            // despite both real fixture events having the same game time.
            Assert.Equal(points[0].Y,points[1].Y,3);Assert.Equal(points[1].X,points[2].X,3);
            Assert.Equal(points[2].Y,points[3].Y,3);Assert.Equal(points[3].X,points[4].X,3);
            Assert.Equal(graph.Bounds.Width/2,points[1].X,3);Assert.Equal(graph.Bounds.Width,points[4].X,3);
            Assert.True(points[2].Y<points[0].Y);Assert.True(points[4].Y>points[2].Y&&points[4].Y<points[0].Y);
            graphReceipts.Add(new{Stage="actual-output-current-events",graph.Bounds,Points=points,EventMargins=f.Match.Events.Select(item=>item.Margin).ToArray(),EventTimes=f.Match.Events.Select(item=>item.GameTime).ToArray()});
            PresPlanProvider(card).Invoke();await Settle(120);
            LiveCardModelAuthority(f,"worm",true,model,changes);
            var repeated=Assert.Single(graph.Children.OfType<Avalonia.Controls.Shapes.Polyline>());Assert.Equal(points,repeated.Points.ToArray());
            graphReceipts.Add(new{Stage="same-original-selected-command",graph.Bounds,Points=repeated.Points.ToArray()});
            Capture(PresPlanRead<Window>(f,"_displayWindow"),$"margin-flow-actual-output-{theme}-{width}");
            await PresPlanReveal(f,card);State("selected-output-on",true,true);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Closing actual output must preserve the chosen presentation.");
            await PresPlanReveal(f,card);State("still-selected-output-off",true,false);
            // All actual routes/captures precede new semantic/theme assertions.
            Assert.Equal(5,states.Count);
            foreach(var state in states)
            {
                Assert.Equal("Margin flow",state.Name);Assert.DoesNotContain("✓",state.Name);
                Assert.Equal(state.Selected?"Selected":"Not selected",state.Status);
                Assert.DoesNotContain("on air",state.Status,StringComparison.OrdinalIgnoreCase);
                Assert.Equal(state.ExpectedBackground,state.Background);Assert.Equal(state.ExpectedInk,state.TitleInk);
                Assert.True(state.Font>=13);Assert.True(state.MinimumIntendedContrast>=4.5);
            }
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(title is not null)title.Opacity=oldOpacity;
            Mr5FinalsWriteTrace($"margin-flow-before-{theme}-{width}",new{Error=error,States=states,Pixels=pixels,Graph=graphReceipts});
            app.ThemeService.SetTheme(previousTheme);OperatorMotion.AnimationOverride=animation;
        }
    }
}
