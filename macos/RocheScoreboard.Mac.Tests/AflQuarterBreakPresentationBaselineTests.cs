// OUTSIDE REVISED v2 BEFORE-COMPATIBLE QUARTER-BREAK DRAFT: original failure was a disposed-view fixture assumption, not an app defect.
// Reuses current Live baseline receipt/model/readiness/pixel helpers; no future glyph references.
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
    private static TextBlock QuarterBreakPresentationOriginalTitle(Control card)=>Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(),
        text=>text.IsEffectivelyVisible&&text.Text=="Quarter break");
    // Observe only the actual current output instance. Close legitimately clears
    // it; logical selection survives without retaining/recreating disposed views.
    private sealed record QuarterBreakExistingOutputReceipt(bool ViewCreated,bool LabelInspected,string? Label,bool? ViewVisible);
    private static QuarterBreakExistingOutputReceipt QuarterBreakInspectExistingOutput(AflFixture f,bool selected,bool outputOpen,string expectedLabel)
    {
        var surface=PresPlanRead<Control?>(f,"_quarterBreakScorebug");
        if(outputOpen)Assert.NotNull(surface); // open selected output must have its actual view
        if(surface is null)
        {
            Assert.False(outputOpen,"An open Quarter break output may not omit its actual view.");
            return new(false,false,null,null);
        }
        if(!selected)return new(true,false,null,surface.IsEffectivelyVisible); // inactive, possibly unpopulated view is not a label witness
        string? label=Named<TextBlock>(surface,"BreakLabelText").Text;
        Assert.Equal(expectedLabel,label); // preserve exact current-label oracle whenever the selected view exists
        if(outputOpen)Assert.True(surface.IsEffectivelyVisible,"The selected actual output must be visible while its owner is open.");
        return new(true,true,label,surface.IsEffectivelyVisible);
    }
    [AvaloniaTheory]
    [Trait("Evidence","AflQuarterBreakPresentationOriginalBaseline")]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,1280,850)][InlineData(AppTheme.Light,420,320)]
    public async Task OriginalQuarterBreakPresentationSelectionPreservesRealPeriodStateAndOutputOwnership(AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var previousTheme=app.ThemeService.CurrentTheme;
        bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        var states=new List<LiveCardBaselineReceipt>();var pixels=new List<object>();var views=new List<object>();string? error=null;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-break-presentation-baseline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);TextBlock? title=null;double oldOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);
            // PUBLIC MODEL FIXTURE SEAM: a paused in-progress Q1, not a break.
            // No private controller command, manufactured preview or clock tick.
            QuarterLabelService.SetCurrentOverride(null);f.Match.SetElapsed(TimeSpan.FromSeconds(12));
            Assert.False(f.Match.ClockRunning);Assert.Equal(1,f.Match.Quarter);Assert.True(f.Match.ElapsedInQuarter>TimeSpan.Zero);
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardQuarterBreak");var alternate=Named<Control>(f.Window,"PresCardScorebug");
            var windowCard=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,windowCard);
            if(PresWindowOpen(f))PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Actual output must be off before selection/output authority is distinguished.");
            await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","Original scorebug must establish a distinct logical presentation while off.");
            Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            string fullModel=System.Text.Json.JsonSerializer.Serialize(f.Match.ToState());
            var rotation=PresPlanRead<object?>(f,"_rotationTimer");
            Assert.Null(rotation);Assert.False(PresPlanRead<bool>(f,"_isBreakRotating"));Assert.Null(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));
            await PresPlanReveal(f,card);title=QuarterBreakPresentationOriginalTitle(card);oldOpacity=title.Opacity;
            var border=Assert.IsAssignableFrom<Border>(card);var check=Named<Border>(f.Window,"PresCheckQuarterBreak");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title);double font=title.FontSize;
            var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            void State(string stage,bool selected,bool outputOpen)
            {
                LiveCardModelAuthority(f,selected?"quarterbreak":"scorebug",outputOpen,model,changes);
                Assert.Equal(selected?1d:0d,check.Opacity);Assert.True(check.IsVisible);Assert.Equal(new Thickness(2),border.BorderThickness);
                Assert.Equal(fullModel,System.Text.Json.JsonSerializer.Serialize(f.Match.ToState()));
                Assert.False(f.Match.ClockRunning);Assert.Equal(1,f.Match.Quarter);Assert.True(f.Match.ElapsedInQuarter>TimeSpan.Zero);
                Assert.Null(PresPlanRead<object?>(f,"_rotationTimer"));Assert.False(PresPlanRead<bool>(f,"_isBreakRotating"));Assert.Null(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));
                var view=QuarterBreakInspectExistingOutput(f,selected,outputOpen,QuarterLabelService.GetQuarterLabel(1));
                views.Add(new{Stage=stage,Selected=selected,OutputOpen=outputOpen,view.ViewCreated,view.LabelInspected,view.Label,view.ViewVisible});
                Rect currentCard=FinalsPainted(f.Window,card),currentTitle=FinalsPainted(f.Window,title);
                // Deliberate reveal of a different row may change scroll offset.
                // Preserve measured sizes/local caption placement across those
                // routes, plus absolute input geometry within each action below.
                Assert.Equal(fixedCard.Size,currentCard.Size);Assert.Equal(fixedTitle.Size,currentTitle.Size);
                Assert.Equal(fixedTitle.Position-fixedCard.Position,currentTitle.Position-currentCard.Position);
                Assert.Equal(font,title.FontSize);Assert.True(currentCard.Contains(currentTitle));
                Assert.All(FinalsProbes(currentCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                f.Window.MouseMove(new Point(1,1));string path=Path.Combine(directory,$"quarter-break-presentation-before-{theme}-{width}-{stage}");
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
            Assert.True(Within(card,f.Window.FocusManager?.GetFocusedElement()),"Original Space must retain this card keyboard ownership.");
            Assert.Equal(beforeKeyCard,FinalsPainted(f.Window,card));Assert.Equal(beforeKeyTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"quarterbreak",false,model,changes);State("keyboard-selected-output-off",true,false);
            PresPlanProvider(alternate).Invoke();await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","The original semantic Invoke provider is deliberately dispatcher-posted; observe its accepted scorebug before the next raw pointer action.");
            LiveCardModelAuthority(f,"scorebug",false,model,changes);
            await PresPlanReveal(f,card);
            Rect beforePointerCard=FinalsPainted(f.Window,card),beforePointerTitle=FinalsPainted(f.Window,title);
            PresWindowPointer(f,card);
            Assert.Equal(beforePointerCard,FinalsPainted(f.Window,card));Assert.Equal(beforePointerTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"quarterbreak",false,model,changes);State("pointer-selected-output-off",true,false);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>PresWindowOpen(f),"Original Window card must actually open the already chosen Quarter break presentation.");
            await PresPlanReveal(f,card);State("selected-output-on",true,true);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Closing actual output must preserve the chosen presentation.");
            await PresPlanReveal(f,card);State("still-selected-output-off",true,false);
            // Real End Quarter is separate from selecting a presentation.
            // Its accepted state/pending45s owner is observed, never ticked or shortened.
            await Mr4Navigate(f.Window,PhysicalKey.Digit1);
            Assert.True(Named<CheckBox>(f.Window,"SettingsBreakScreenToggle").IsChecked);
            int eventsBeforeEnd=f.Match.Events.Count,home=f.Match.HomeTotal,away=f.Match.AwayTotal;
            var end=Named<Button>(f.Window,"EndQtrButton");end.BringIntoView();
            await Until(()=>
            {
                using var frame=f.Window.CaptureRenderedFrame();
                if(!end.IsLoaded||!end.IsEffectivelyVisible||!end.IsEffectivelyEnabled||!end.GetVisualAncestors().Contains(f.Window))return false;
                var box=FinalsPainted(f.Window,end);
                return box.Width>8&&box.Height>8&&new Rect(f.Window.ClientSize).Contains(box)&&FinalsProbes(box).All(point=>Within(end,f.Window.InputHitTest(point)));
            },"Real End Quarter must own every pointer probe at the actual current compact/full geometry.");
            await Tap(f.Window,end);
            Assert.Equal(2,f.Match.Quarter);Assert.False(f.Match.ClockRunning);Assert.Equal(TimeSpan.Zero,f.Match.ElapsedInQuarter);
            Assert.NotNull(f.Match.GetQuarterSnapshot(1));Assert.Equal(eventsBeforeEnd,f.Match.Events.Count);
            Assert.Equal(home,f.Match.HomeTotal);Assert.Equal(away,f.Match.AwayTotal);
            var pending=Assert.IsType<Avalonia.Threading.DispatcherTimer>(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));
            Assert.True(pending.IsEnabled);Assert.Equal(TimeSpan.FromSeconds(45),pending.Interval);
            string ended=System.Text.Json.JsonSerializer.Serialize(f.Match.ToState());int events=changes.Count;
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);await PresPlanReveal(f,card);
            Assert.Same(pending,PresPlanRead<object?>(f,"_breakScreenDelayTimer"));
            Assert.True(pending.IsEnabled,"The same captured owner must still be pending immediately before the one Enter cancellation.");
            Key(f.Window,PhysicalKey.Enter); // original native action retires actual pending break rotation
            Assert.True(Within(card,f.Window.FocusManager?.GetFocusedElement()),"Original Enter must retain this card keyboard ownership.");
            Assert.Equal("quarterbreak",PresPlanRead<string>(f,"_activeScreenKey"));Assert.False(PresWindowOpen(f));
            Assert.Null(PresPlanRead<object?>(f,"_breakScreenDelayTimer"));Assert.False(pending.IsEnabled);
            Assert.Null(PresPlanRead<object?>(f,"_rotationTimer"));Assert.False(PresPlanRead<bool>(f,"_isBreakRotating"));
            Assert.Equal(ended,System.Text.Json.JsonSerializer.Serialize(f.Match.ToState()));Assert.Equal(events,changes.Count);
            var breakView=QuarterBreakInspectExistingOutput(f,true,PresWindowOpen(f),QuarterLabelService.GetBreakLabel(1));
            views.Add(new{Stage="after-actual-end-and-one-Enter",Selected=true,OutputOpen=PresWindowOpen(f),breakView.ViewCreated,breakView.LabelInspected,breakView.Label,breakView.ViewVisible});
            Capture(f.Window,"quarter-break-presentation-after-actual-end-"+theme+"-"+width);
            Mr5FinalsWriteTrace("quarter-break-presentation-ended-owner-"+theme+"-"+width,new
            {f.Match.Quarter,f.Match.ClockRunning,f.Match.ElapsedInQuarter,PendingInterval=pending.Interval,PendingEnabled=pending.IsEnabled,breakView.ViewCreated,breakView.LabelInspected,breakView.Label,breakView.ViewVisible,
                ModelUnchanged=ended==System.Text.Json.JsonSerializer.Serialize(f.Match.ToState()),OutputOpen=PresWindowOpen(f),ModelEventsAfterEnd=changes.Count-events});
            // All actual routes/captures precede new semantic/theme assertions.
            Assert.Equal(5,states.Count);
            foreach(var state in states)
            {
                Assert.Equal("Quarter break",state.Name);Assert.DoesNotContain("✓",state.Name);
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
            Mr5FinalsWriteTrace($"quarter-break-presentation-before-{theme}-{width}",new{Error=error,States=states,Views=views,Pixels=pixels});
            app.ThemeService.SetTheme(previousTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
