// OUTSIDE BEFORE-COMPATIBLE DRAFT. Four cases, UNCOMPILED/UNRUN.
// Original card/title/check only; no future glyph lookup. Actual public input.
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
    private sealed record LiveCardBaselineReceipt(string Stage,bool Selected,bool OutputOpen,string Name,string Status,
        string Background,string ExpectedBackground,string TitleInk,string ExpectedInk,double Check,double Font,
        Rect Card,Rect Title,int PaintedTextPixels,double MinimumIntendedContrast);
    private static TextBlock LiveCardOriginalTitle(Control card)=>Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(),
        text=>text.IsEffectivelyVisible&&text.Text=="Live scorebug");
    private static void LiveCardModelAuthority(AflFixture f,string key,bool outputOpen,string model,PresPlanChanges changes)
    {
        // The logical selection must be committed at input return, not delayed
        // until a glyph or external screen transition completes.
        Assert.Equal(key,PresPlanRead<string>(f,"_activeScreenKey"));Assert.Equal(outputOpen,PresWindowOpen(f));
        Assert.Equal(model,Scores(f.Match));Assert.Equal(0,changes.Count);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,1280,850)][InlineData(AppTheme.Light,420,320)]
    public async Task OriginalLiveScorebugSelectionIsImmediateReadableAndDistinctFromOutputOpen(AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var previousTheme=app.ThemeService.CurrentTheme;
        bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        var states=new List<LiveCardBaselineReceipt>();var pixels=new List<object>();string? error=null;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-live-scorebug-baseline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);TextBlock? title=null;double oldOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardScorebug");var summary=Named<Control>(f.Window,"PresCardSummary");
            var windowCard=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,windowCard);
            if(PresWindowOpen(f))PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Actual output must be off before selection/output authority is distinguished.");
            await PresPlanReveal(f,summary);PresPlanProvider(summary).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="summary","Original summary must establish a distinct logical presentation while off.");
            Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await PresPlanReveal(f,card);title=LiveCardOriginalTitle(card);oldOpacity=title.Opacity;
            var border=Assert.IsAssignableFrom<Border>(card);var check=Named<Border>(f.Window,"PresCheckScorebug");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title);double font=title.FontSize;
            var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            void State(string stage,bool selected,bool outputOpen)
            {
                LiveCardModelAuthority(f,selected?"scorebug":"summary",outputOpen,model,changes);
                Assert.Equal(selected?1d:0d,check.Opacity);
                Rect currentCard=FinalsPainted(f.Window,card),currentTitle=FinalsPainted(f.Window,title);
                // Deliberate reveal of a different row may change scroll offset.
                // Preserve measured sizes/local caption placement across those
                // routes, plus absolute input geometry within each action below.
                Assert.Equal(fixedCard.Size,currentCard.Size);Assert.Equal(fixedTitle.Size,currentTitle.Size);
                Assert.Equal(fixedTitle.Position-fixedCard.Position,currentTitle.Position-currentCard.Position);
                Assert.Equal(font,title.FontSize);Assert.True(currentCard.Contains(currentTitle));
                Assert.All(FinalsProbes(currentCard),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                f.Window.MouseMove(new Point(1,1));string path=Path.Combine(directory,$"live-scorebug-before-{theme}-{width}-{stage}");
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
            f.Window.Activate();Key(f.Window,PhysicalKey.Tab);Assert.True(card.Focus(NavigationMethod.Tab));
            Rect beforeKeyCard=FinalsPainted(f.Window,card),beforeKeyTitle=FinalsPainted(f.Window,title);
            Key(f.Window,PhysicalKey.Space);
            Assert.Equal(beforeKeyCard,FinalsPainted(f.Window,card));Assert.Equal(beforeKeyTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"scorebug",false,model,changes);State("keyboard-selected-output-off",true,false);
            PresPlanProvider(summary).Invoke();await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="summary","The original semantic Invoke provider is deliberately dispatcher-posted; observe its accepted summary before the next raw pointer action.");
            LiveCardModelAuthority(f,"summary",false,model,changes);
            await PresPlanReveal(f,card);
            Rect beforePointerCard=FinalsPainted(f.Window,card),beforePointerTitle=FinalsPainted(f.Window,title);
            PresWindowPointer(f,card);
            Assert.Equal(beforePointerCard,FinalsPainted(f.Window,card));Assert.Equal(beforePointerTitle,FinalsPainted(f.Window,title));
            LiveCardModelAuthority(f,"scorebug",false,model,changes);State("pointer-selected-output-off",true,false);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>PresWindowOpen(f),"Original Window card must actually open the already chosen scorebug presentation.");
            await PresPlanReveal(f,card);State("selected-output-on",true,true);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Closing actual output must preserve the chosen presentation.");
            await PresPlanReveal(f,card);State("still-selected-output-off",true,false);
            // All actual routes/captures precede new semantic/theme assertions.
            Assert.Equal(5,states.Count);
            foreach(var state in states)
            {
                Assert.Equal("Live scorebug",state.Name);Assert.DoesNotContain("✓",state.Name);
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
            Mr5FinalsWriteTrace($"live-scorebug-before-{theme}-{width}",new{Error=error,States=states,Pixels=pixels});
            app.ThemeService.SetTheme(previousTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
