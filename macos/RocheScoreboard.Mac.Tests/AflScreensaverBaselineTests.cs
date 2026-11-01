// OUTSIDE BEFORE-COMPATIBLE DRAFT — four cases; UNCOMPILED/UNRUN.
// Original card/check/title, actual explicit Open and guarded manual saver routes.
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
    private sealed record SaverOutputViewReceipt(string Stage,bool OutputOpen,bool ViewCreated,bool Inspected,
        bool LogicalSelected,bool? ViewVisible,bool? DefaultVisible,bool? ImageVisible,bool? VideoVisible,bool? ClockVisible,string? ClockText);
    private static SaverOutputViewReceipt SaverInspectActualOutput(AflFixture f,string stage,
        bool requireSelectedVisible=false,bool expectDisposed=false,bool? expectedClockVisible=null)
    {
        bool open=PresWindowOpen(f);var view=PresPlanRead<Control?>(f,"_outputScreensaver");
        bool created=view is not null,selected=PresPlanRead<string>(f,"_activeScreenKey")=="screensaver";
        if(!open)
        {
            // No cached/disposed view is inspected or reconstructed off air.
            // A never-shown hidden view can exist; explicit Close must clear it.
            Assert.False(requireSelectedVisible,"A visible-output inspection cannot be claimed while output is off.");
            if(expectDisposed)Assert.False(created,"The actual output Closed owner clears the screensaver field.");
            return new(stage,false,created,false,selected,null,null,null,null,null,null);
        }
        Assert.False(expectDisposed);Assert.NotNull(view);
        // This diagnostic's isolated fresh fixture uses the real default source.
        Assert.Null(PresPlanRead<string?>(f,"_screensaverMediaPath"));
        var clock=Named<TextBlock>(view,"ClockText");Assert.False(string.IsNullOrWhiteSpace(clock.Text));
        if(expectedClockVisible is bool showClock)Assert.Equal(showClock,clock.IsVisible);
        bool venue=Named<Control>(view,"DefaultScene").IsVisible,image=Named<Control>(view,"CustomImage").IsVisible,video=Named<Control>(view,"CustomVideoHost").IsVisible;
        Assert.True(venue);Assert.False(image);Assert.False(video);
        if(requireSelectedVisible){Assert.True(selected);Assert.True(view.IsEffectivelyVisible);}
        return new(stage,true,true,true,selected,view.IsEffectivelyVisible,venue,image,video,clock.IsVisible,clock.Text);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Dark,1280,850)][InlineData(AppTheme.Dark,420,320)]
    [InlineData(AppTheme.Light,1280,850)][InlineData(AppTheme.Light,420,320)]
    public async Task OriginalScreensaverRespectsExplicitOutputActivationAndTruthfulSelectedCard(AppTheme theme,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var app=(RocheScoreboard.Mac.App)Application.Current!;var priorTheme=app.ThemeService.CurrentTheme;
        bool priorReduce=OperatorAppearance.ReduceMotion;bool? priorOverride=OperatorMotion.AnimationOverride;
        var states=new List<LiveCardBaselineReceipt>();var pixels=new List<object>();var outputEvidence=new List<object>();string? error=null;
        bool expectedClockVisible=OperatorWorkflowPreferences.Load().OutputScreensaverShowClock;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-screensaver-card-baseline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);TextBlock? title=null;double oldOpacity=1;
        try
        {
            app.ThemeService.SetTheme(theme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardScreensaver");var alternate=Named<Control>(f.Window,"PresCardScorebug");
            var windowCard=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,windowCard);
            if(PresWindowOpen(f))PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Establish real explicit output-off state.");
            await PresPlanReveal(f,alternate);PresPlanProvider(alternate).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","Existing scorebug establishes a distinct logical selection while output is closed.");
            Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            await PresPlanReveal(f,card);
            title=Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(),text=>text.IsEffectivelyVisible&&text.Text=="Screensaver");oldOpacity=title.Opacity;
            var border=Assert.IsAssignableFrom<Border>(card);var check=Named<Border>(f.Window,"PresCheckScreensaver");
            Rect fixedCard=FinalsPainted(f.Window,card),fixedTitle=FinalsPainted(f.Window,title);double font=title.FontSize;
            var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            void State(string stage,bool selected,bool open,bool expectDisposed=false)
            {
                LiveCardModelAuthority(f,selected?"screensaver":"scorebug",open,model,changes);
                outputEvidence.Add(SaverInspectActualOutput(f,stage,expectDisposed:expectDisposed,expectedClockVisible:expectedClockVisible));
                Assert.Equal(selected?1d:0d,check.Opacity);
                var cardBounds=FinalsPainted(f.Window,card);var titleBounds=FinalsPainted(f.Window,title);
                Assert.Equal(fixedCard.Size,cardBounds.Size);Assert.Equal(fixedTitle.Size,titleBounds.Size);
                Assert.Equal(fixedTitle.Position-fixedCard.Position,titleBounds.Position-cardBounds.Position);
                Assert.Equal(font,title.FontSize);Assert.True(cardBounds.Contains(titleBounds));
                Assert.All(FinalsProbes(cardBounds),p=>Assert.True(Within(card,f.Window.InputHitTest(p))));
                f.Window.MouseMove(new Point(1,1));string path=Path.Combine(directory,$"screensaver-before-{theme}-{width}-{stage}");
                var ink=Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground);double opacity=Mr5PaletteVisualOpacity(title);
                using var normal=Mr5PaletteCapture(f.Window,path+".png");SkiaSharp.SKBitmap backing;
                try{title.Opacity=0;backing=Mr5PaletteCapture(f.Window,path+"-backing.png");}finally{title.Opacity=oldOpacity;}
                using(backing)
                {
                    var measured=Mr5PalettePixels(f.Window,title,normal,backing,ink,opacity);
                    Assert.True(measured.Count>=12,"Actual original Screensaver title ink is required.");pixels.Add(new{stage,Pixels=measured});
                    var expectedBase=Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,selected?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush"));
                    var expectedInk=Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,selected?"ControlCentre.SelectedTextBrush":"ControlCentre.TextBrush"));
                    states.Add(new(stage,selected,open,peer!.GetName()??"",AutomationProperties.GetItemStatus(card)??"",
                        Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color.ToString(),expectedBase.Color.ToString(),ink.Color.ToString(),expectedInk.Color.ToString(),
                        check.Opacity,font,cardBounds,titleBounds,measured.Count,measured.Min(p=>p.IntendedTextContrast)));
                }
            }
            State("unselected-output-off",false,false);
            // This card intentionally cannot select/open while explicit output
            // activation is required. Do not apply another card's off-air recipe.
            Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"scorebug",false,model,changes);
            Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            State("guarded-space-output-off",false,false);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();
            await Until(()=>PresWindowOpen(f),"Original Window card must explicitly activate output before manual saver selection.");
            await PresPlanReveal(f,card);Rect beforeKeyCard=FinalsPainted(f.Window,card),beforeKeyTitle=FinalsPainted(f.Window,title);
            Key(f.Window,PhysicalKey.Space);
            LiveCardModelAuthority(f,"screensaver",true,model,changes);
            Assert.Equal(beforeKeyCard,FinalsPainted(f.Window,card));Assert.Equal(beforeKeyTitle,FinalsPainted(f.Window,title));
            await Until(()=>PresPlanRead<Control?>(f,"_outputScreensaver") is {IsEffectivelyVisible:true},"Original configured/default saver output must actually become visible.");
            Assert.Null(PresPlanRead<string?>(f,"_screensaverMediaPath")); // Isolated fresh fixture defaults, not a custom-media claim.
            outputEvidence.Add(SaverInspectActualOutput(f,"real-default-saver",requireSelectedVisible:true,expectedClockVisible:expectedClockVisible));
            Capture(PresPlanRead<Window>(f,"_displayWindow"),$"screensaver-original-output-{theme}-{width}");
            State("keyboard-selected-output-on",true,true);
            PresPlanProvider(card).Invoke();await Settle(120);LiveCardModelAuthority(f,"screensaver",true,model,changes);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>!PresWindowOpen(f),"Real output Close must preserve logical screensaver selection.");
            await PresPlanReveal(f,card);State("selected-output-off",true,false,expectDisposed:true);
            Rect beforePointerCard=FinalsPainted(f.Window,card);PresWindowPointer(f,card);
            LiveCardModelAuthority(f,"screensaver",false,model,changes);Assert.Equal(beforePointerCard,FinalsPainted(f.Window,card));
            State("guarded-pointer-still-selected-output-off",true,false,expectDisposed:true);
            await PresPlanReveal(f,windowCard);PresPlanProvider(windowCard).Invoke();await Until(()=>PresWindowOpen(f),"Explicit reopening must restore the chosen saver through the actual output owner.");
            await Until(()=>PresPlanRead<Control?>(f,"_outputScreensaver") is {IsEffectivelyVisible:true},"Reopened output must restore screensaver presentation.");
            await PresPlanReveal(f,card);
            outputEvidence.Add(SaverInspectActualOutput(f,"actual-reopened-saver",requireSelectedVisible:true,expectedClockVisible:expectedClockVisible));
            State("selected-reopened-output-on",true,true);
            PresPlanProvider(alternate).Invoke();await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="scorebug","Original alternate selection must clear saver state without touching match.");
            await PresPlanReveal(f,card);State("unselected-alternate-output-on",false,true);
            Rect beforeAcceptedPointer=FinalsPainted(f.Window,card);PresWindowPointer(f,card);
            LiveCardModelAuthority(f,"screensaver",true,model,changes);Assert.Equal(beforeAcceptedPointer,FinalsPainted(f.Window,card));
            State("pointer-selected-output-on",true,true);
            // All existing public routes precede the new semantic/theme gates.
            Assert.Equal(8,states.Count);
            foreach(var state in states)
            {
                Assert.Equal("Screensaver",state.Name);Assert.DoesNotContain("✓",state.Name);
                Assert.Equal(state.Selected?"Selected":"Not selected",state.Status);Assert.DoesNotContain("on air",state.Status,StringComparison.OrdinalIgnoreCase);
                Assert.Equal(state.ExpectedBackground,state.Background);Assert.Equal(state.ExpectedInk,state.TitleInk);
                Assert.True(state.Font>=13);Assert.True(state.MinimumIntendedContrast>=4.5);
            }
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            if(title is not null)title.Opacity=oldOpacity;
            Mr5FinalsWriteTrace($"screensaver-before-{theme}-{width}",new{Error=error,States=states,Pixels=pixels,Output=outputEvidence});
            app.ThemeService.SetTheme(priorTheme);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,priorReduce);OperatorMotion.AnimationOverride=priorOverride;
        }
    }
}
