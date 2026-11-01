// OUTSIDE REVISED DRAFT ONLY — four cases; UNCOMPILED/UNRUN. No app writes.
// Per-card planned names/status are NEW contracts; public routes are observed first.
// Root owns later integration after current work. Source review does NOT prove
// these contracts fail; preserve actual readiness/action/failure distinctions.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed class PresPlanChanges : IDisposable
    {
        private readonly Roche_Scoreboard.Models.MatchManager match;
        internal int Count { get; private set; }
        internal PresPlanChanges(Roche_Scoreboard.Models.MatchManager value) { match=value; match.MatchChanged+=Changed; }
        private void Changed()=>Count++;
        public void Dispose()=>match.MatchChanged-=Changed;
    }
    private sealed record PresPlanAcceptedReceipt(string Stage,bool Open,double X);
    private sealed record PresPlanSemanticReceipt(string Card,string Name,string Status,bool Selected);
    private static readonly (string Name,string Key,string OutputField,string Check)[] PresPlanCards=
    [
        ("PresCardScorebug","scorebug","_scorebug","PresCheckScorebug"),
        ("PresCardSummary","summary","_breakScreen","PresCheckSummary"),
        ("PresCardWorm","worm","_scoreworm","PresCheckWorm"),
        ("PresCardStats","stats","_statsScreen","PresCheckStats"),
        ("PresCardWeather","weather","_weatherScreen","PresCheckWeather"),
        ("PresCardQuarterBreak","quarterbreak","_quarterBreakScorebug","PresCheckQuarterBreak"),
        ("PresCardScreensaver","screensaver","_outputScreensaver","PresCheckScreensaver")
    ];
    private static T PresPlanRead<T>(AflFixture f,string name)=>(T)f.Window.GetType().GetField(name,HiddenInstance)!.GetValue(f.Window)!;
    private static TranslateTransform PresPlanThumb(AflFixture f)=>Assert.IsType<TranslateTransform>(
        f.Window.GetType().GetProperty("WindowToggleThumbTranslate",HiddenInstance)!.GetValue(f.Window));
    private static IInvokeProvider PresPlanProvider(Control target)=>
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(target));
    private static async Task PresPlanReveal(AflFixture f,Control target)
    {
        Assert.True(target.IsEffectivelyVisible&&target.IsEffectivelyEnabled&&target.Focusable);
        Assert.True(target.Focus(NavigationMethod.Tab));target.BringIntoView();await Settle(100);
        var viewport=Named<ScrollViewer>(f.Window,"AflOutputPageViewport");
        bool Ready(){using var frame=f.Window.CaptureRenderedFrame();var box=FinalsPainted(f.Window,target);return box.Width>16&&box.Height>16&&new Rect(f.Window.ClientSize).Contains(box)&&FinalsProbes(box).All(p=>Within(target,f.Window.InputHitTest(p)));}
        for(int i=0;i<50&&!Ready();i++)
        {
            var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),f.Window);Assert.NotNull(wheel);
            Assert.True(Within(viewport,f.Window.InputHitTest(wheel.Value)));
            var box=FinalsPainted(f.Window,target);bool top=Within(target,f.Window.InputHitTest(FinalsProbes(box)[3])),bottom=Within(target,f.Window.InputHitTest(FinalsProbes(box)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<wheel.Value.Y?1:-1;
            f.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Settle(80);
        }
        Assert.True(Ready(),target.Name+" needs its full actual five-point input surface before a control behavior claim.");
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","AflOutputWindowReducedMotion")]
    public async Task ActualOutputWindowOpenCloseKeepsThumbCanonicalUnderReduce(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);bool previousReduce=Roche_Scoreboard.Services.OperatorAppearance.ReduceMotion;
        bool? previousOverride=Roche_Scoreboard.Services.OperatorMotion.AnimationOverride;
        EventHandler? observer=null;EventHandler<AvaloniaPropertyChangedEventArgs>? labelObserver=null;var accepted=new List<PresPlanAcceptedReceipt>();var trace=new List<object>();var poses=new List<(string Stage,string Label,bool Open,double X)>();string stage="open";var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Roche_Scoreboard.Services.OperatorAppearance.Configure(Roche_Scoreboard.Services.OperatorAppearance.GlassEnabled,true);
            Roche_Scoreboard.Services.OperatorMotion.AnimationOverride=null;Assert.False(Roche_Scoreboard.Services.OperatorMotion.Enabled);
            await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            var provider=PresPlanProvider(card);var label=Named<TextBlock>(f.Window,"WindowToggleLabel");var thumb=PresPlanThumb(f);
            if(PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true})provider.Invoke();
            await Until(()=>PresPlanRead<Window?>(f,"_displayWindow") is not {IsVisible:true}&&label.Text=="Open","Real initial output must be closed before a fresh Open transition.");
            await Settle(500);Assert.Equal(0d,thumb.X,4);string before=Scores(f.Match);
            observer=(_,_)=>
            {
                bool open=PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true};string text=label.Text??"";
                if((open&&text=="Close")||(!open&&text=="Open"))poses.Add((stage,text,open,thumb.X));
                trace.Add(new{stage,source="NativeRendering",milliseconds=elapsed.Elapsed.TotalMilliseconds,open,label=label.Text,thumb=thumb.X});
            };
            // Property change observes the synchronous accepted visual state;
            // a slow first rendered frame alone could miss the entire200ms bug.
            labelObserver=(_,args)=>
            {
                if(args.Property!=TextBlock.TextProperty)return;
                bool open=PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true};
                if((open&&label.Text=="Close")||(!open&&label.Text=="Open"))
                    accepted.Add(new(stage,open,thumb.X));
            };
            label.PropertyChanged+=labelObserver;
            NativeRendering.Rendering+=observer;
            provider.Invoke();await Until(()=>PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true}&&label.Text=="Close","Actual native window action must open output and show current Close action.");
            Capture(f.Window,"pres-window-open-state-"+width);
            await Until(()=>poses.Count(p=>p.Stage=="open"&&p.Open)>=6,"Actual renderer must expose the accepted Open state.");
            stage="close";provider.Invoke();await Until(()=>PresPlanRead<Window?>(f,"_displayWindow") is not {IsVisible:true}&&label.Text=="Open","Actual native window action must close output and show current Open action.");
            Capture(f.Window,"pres-window-close-state-"+width);
            await Until(()=>poses.Count(p=>p.Stage=="close"&&!p.Open)>=6,"Actual renderer must expose the accepted Close state.");
            Assert.Equal(before,Scores(f.Match));
            Assert.Contains(accepted,p=>p.Stage=="open"&&p.Open);
            Assert.Contains(accepted,p=>p.Stage=="close"&&!p.Open);
            Assert.All(accepted,p=>Assert.Equal(p.Open?20d:0d,p.X,4));
            Assert.All(poses,p=>Assert.Equal(p.Open?20d:0d,p.X,4));
            var track=Named<Border>(f.Window,"WindowToggleTrack");
            var actualThumb=Named<Border>(f.Window,"WindowToggleThumb");
            var trackBox=FinalsPainted(f.Window,track);var thumbBox=FinalsPainted(f.Window,actualThumb);
            Assert.True(trackBox.Contains(thumbBox));
            Assert.Equal(2d,thumbBox.Left-trackBox.Left,2);
            Capture(f.Window,"pres-window-reduce-"+width);
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            if(labelObserver is not null)Named<TextBlock>(f.Window,"WindowToggleLabel").PropertyChanged-=labelObserver;
            Mr5FinalsWriteTrace("pres-window-reduce-"+width,new{Frames=trace,Accepted=accepted});
            Roche_Scoreboard.Services.OperatorAppearance.Configure(Roche_Scoreboard.Services.OperatorAppearance.GlassEnabled,previousReduce);
            Roche_Scoreboard.Services.OperatorMotion.AnimationOverride=previousOverride;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","AflPresentationIndividualAuthority")]
    public async Task SevenOriginalPresentationActionsPreserveMatchAndExposeTruthfulSelectionWithoutGlyphNames(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
        var windowCard=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,windowCard);
        if(PresPlanRead<Window?>(f,"_displayWindow") is not {IsVisible:true})PresPlanProvider(windowCard).Invoke();
        await Until(()=>PresPlanRead<Window?>(f,"_displayWindow") is {IsVisible:true},"Screensaver manual route requires explicitly enabled output.");
        // Start on another real screen so the first scorebug action must
        // actually change its target, not pass on the default startup surface.
        var initialSummary=Named<Control>(f.Window,"PresCardSummary");await PresPlanReveal(f,initialSummary);
        PresPlanProvider(initialSummary).Invoke();
        await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")=="summary"
            &&PresPlanRead<Control?>(f,"_breakScreen") is {IsVisible:true},"Initial distinct summary must be current before testing scorebug.");
        using var changes=new PresPlanChanges(f.Match);
        string before=Scores(f.Match);var receipts=new List<PresPlanSemanticReceipt>();
        for(int i=0;i<PresPlanCards.Length;i++)
        {
            var item=PresPlanCards[i];var action=Named<Control>(f.Window,item.Name);await PresPlanReveal(f,action);
            if(i%3==0)Key(f.Window,PhysicalKey.Space);
            else if(i%3==1)Key(f.Window,PhysicalKey.Enter);
            else PresPlanProvider(action).Invoke();
            await Until(()=>PresPlanRead<string>(f,"_activeScreenKey")==item.Key,"Actual original "+item.Name+" action must select its own presentation key.");
            await Until(()=>PresPlanRead<Control?>(f,item.OutputField) is {IsVisible:true},"Actual "+item.Key+" output surface must appear through the existing transition.");
            Assert.Equal(before,Scores(f.Match));Assert.Equal(0,changes.Count);
            foreach(var state in PresPlanCards)
            {
                var candidate=Named<Control>(f.Window,state.Name);var peer=ControlAutomationPeer.CreatePeerForElement(candidate);Assert.NotNull(peer);
                double check=Named<Border>(f.Window,state.Check).Opacity;Assert.Equal(state.Key==item.Key?1d:0d,check);
                receipts.Add(new(state.Name,peer.GetName()??"",AutomationProperties.GetItemStatus(candidate)??"",state.Key==item.Key));
            }
        }
        Mr5FinalsWriteTrace("pres-card-semantic-authority-"+width,receipts);
        // New semantic contract is checked AFTER all seven real routes were
        // reached, so one missing accessible label cannot hide routing evidence.
        Assert.All(receipts,r=>
        {
            Assert.DoesNotContain("✓",r.Name);Assert.False(string.IsNullOrWhiteSpace(r.Name));
            string expected=r.Card switch
            {
                "PresCardScorebug"=>"Live scorebug", "PresCardSummary"=>"Quarter summary",
                "PresCardWorm"=>"Margin flow", "PresCardStats"=>"Match stats",
                "PresCardWeather"=>"Weather", "PresCardQuarterBreak"=>"Quarter break",
                "PresCardScreensaver"=>"Screensaver", _=>throw new InvalidOperationException(r.Card)
            };
            Assert.Equal(expected,r.Name);
            Assert.Equal(r.Selected?"Selected":"Not selected",r.Status);
        });
        Capture(f.Window,"pres-card-semantic-authority-"+width);
    }
}
