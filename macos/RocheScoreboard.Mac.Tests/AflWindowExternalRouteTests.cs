// OUTSIDE DRAFT ONLY. Four cases, UNCOMPILED/UNRUN. No production writes.
// Actual public Ctrl+K/search/Enter and original attached HomeResume pointer route.
// Remote route calls the real public command-target API directly; this does NOT
// exercise authenticated transport, native desktop focus or OS accessibility.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record PresExternalReceipt(string Route,bool ActualOpen,string Label,string Name,string Status,
        double Thumb,bool ActivationRequired,string Screen,string Model,int MatchEvents);
    private static PresExternalReceipt PresExternalRead(AflFixture f,string route,PresPlanChanges changes)
    {
        var card=Named<Control>(f.Window,"PresCardWindow");
        var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
        return new(route,PresWindowOpen(f),Named<TextBlock>(f.Window,"WindowToggleLabel").Text??"",
            peer.GetName()??"",AutomationProperties.GetItemStatus(card)??"",PresPlanThumb(f).X,
            PresPlanRead<bool>(f,"_outputActivationRequired"),PresPlanRead<string>(f,"_activeScreenKey"),Scores(f.Match),changes.Count);
    }
    private static void PresExternalTruth(PresExternalReceipt receipt,bool open,bool activationRequired,string screen,string state)
    {
        Assert.Equal(open,receipt.ActualOpen);
        Assert.Equal(open?"Close":"Open",receipt.Label);
        Assert.Equal(open?"Close output window":"Open output window",receipt.Name);
        Assert.Equal(open?"Window open":"Window closed",receipt.Status);
        Assert.Equal(open?20d:0d,receipt.Thumb,4);
        Assert.Equal(activationRequired,receipt.ActivationRequired);
        Assert.Equal(screen,receipt.Screen);Assert.Equal(state,receipt.Model);Assert.Equal(0,receipt.MatchEvents);
    }
    private static async Task PresExternalCommand(AflFixture f,string query,string commandId)
    {
        f.Window.Activate();Key(f.Window,PhysicalKey.K,RawInputModifiers.Control);
        var overlay=Named<Grid>(f.Window,"CommandOverlay");var search=Named<TextBox>(f.Window,"CommandSearch");
        await Until(()=>overlay.IsEffectivelyVisible&&search.IsFocused,"Actual Ctrl+K must mount and focus the public command bar.");
        Key(f.Window,PhysicalKey.A,RawInputModifiers.Control);f.Window.KeyTextInput(query);
        var results=Named<ListBox>(f.Window,"CommandResults");
        await Until(()=>results.SelectedItem is ListBoxItem item&&Equals(item.Tag,commandId),
            "Actual search must naturally select the intended public "+commandId+" command; do not force results selection.");
        Assert.Equal(commandId,Assert.IsType<ListBoxItem>(results.SelectedItem).Tag);
        Key(f.Window,PhysicalKey.Enter);
        await Until(()=>!overlay.IsVisible,"The accepted public command must dismiss the palette.");
    }
    private static async Task PresExternalHomeResumePointer(AflFixture f,SportSelectionScreen home)
    {
        // Current ATTACHED button avoids a cached detached Home namescope target.
        var resume=Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>
            button.Name=="HomeResumeButton"&&button.IsEffectivelyVisible&&button.GetVisualAncestors().Contains(f.Window));
        Assert.Equal("Return to current session",resume.Content);Assert.True(resume.IsEffectivelyEnabled);
        Assert.True(resume.Focus(NavigationMethod.Tab));resume.BringIntoView();await Settle(100);
        var viewport=Named<ScrollViewer>(home,"ResponsiveScrollHost");
        bool Ready()
        {
            using var frame=f.Window.CaptureRenderedFrame();
            var box=FinalsPainted(f.Window,resume);
            return box.Width>16&&box.Height>16&&new Rect(f.Window.ClientSize).Contains(box)
                &&FinalsProbes(box).All(point=>Within(resume,f.Window.InputHitTest(point)));
        }
        for(int step=0;step<50&&!Ready();step++)
        {
            var wheel=viewport.TranslatePoint(new Point(3,viewport.Bounds.Height/2),f.Window);Assert.NotNull(wheel);
            Assert.True(Within(viewport,f.Window.InputHitTest(wheel.Value)),"Home reveal must scroll the actual Home viewport.");
            var box=FinalsPainted(f.Window,resume);
            bool top=Within(resume,f.Window.InputHitTest(FinalsProbes(box)[3])),bottom=Within(resume,f.Window.InputHitTest(FinalsProbes(box)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:box.Center.Y<wheel.Value.Y?1:-1;
            f.Window.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Settle(80);
        }
        Assert.True(Ready(),"Actual attached HomeResume needs its entire painted five-point input region reachable before the public pointer route.");
        Rect actual=FinalsPainted(f.Window,resume);
        f.Window.MouseMove(actual.Center);f.Window.MouseDown(actual.Center,MouseButton.Left);f.Window.MouseUp(actual.Center,MouseButton.Left);
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","AflWindowExternalPublicRoutes")]
    public async Task ClosedOriginalWindowReopenedByActualCommandAndPublicRemoteKeepsItsCardTruthful(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        var receipts=new List<PresExternalReceipt>();string? error=null;
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            var original=PresPlanProvider(card);
            // Make the direct ORIGINAL close wrapper a positive precondition,
            // even if setup initially left output closed.
            if(!PresWindowOpen(f))original.Invoke();
            await Until(()=>PresWindowOpen(f),"Original card must actually open output before its direct close wrapper is exercised.");
            original.Invoke();await Until(()=>!PresWindowOpen(f)&&Named<TextBlock>(f.Window,"WindowToggleLabel").Text=="Open","Original close wrapper must leave an actually closed output and current Open verb.");
            using var changes=new PresPlanChanges(f.Match);string state=Scores(f.Match),screen=PresPlanRead<string>(f,"_activeScreenKey");
            PresExternalTruth(PresExternalRead(f,"original-close",changes),false,true,screen,state);
            await PresExternalCommand(f,"Show output window","enable-output");
            await Until(()=>PresWindowOpen(f),"Public enable-output command must ACTUALLY show the output before testing UI truth.");
            receipts.Add(PresExternalRead(f,"physical-command-enable-output",changes));
            await PresPlanReveal(f,card);Capture(f.Window,$"pres-window-external-command-{width}");
            // Provider dispatch reads the actual output, independent of stale copy.
            original.Invoke();await Until(()=>!PresWindowOpen(f)&&Named<TextBlock>(f.Window,"WindowToggleLabel").Text=="Open","Original wrapper must close the command-opened output before the separate public remote route.");
            ((IRemoteCommandTarget)f.Window).ShowScoreboardWindow();
            await Until(()=>PresWindowOpen(f),"Real public command-target API must actually show output; transport is intentionally outside this fixture.");
            receipts.Add(PresExternalRead(f,"public-remote-command-target",changes));
            await PresPlanReveal(f,card);Capture(f.Window,$"pres-window-external-remote-{width}");
            // BOTH public routes reached before the new stale-state oracle.
            Assert.Equal(2,receipts.Count);
            Assert.All(receipts,receipt=>PresExternalTruth(receipt,true,false,screen,state));
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            Mr5FinalsWriteTrace($"pres-window-external-command-remote-{width}",new{Error=error,Receipts=receipts});
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","AflWindowExternalPublicRoutes")]
    public async Task ActualHomeHidesOutputAndPhysicalCurrentResumeKeepsItOffAndCardTruthful(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        var receipts=new List<PresExternalReceipt>();string? error=null;
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);OperatorMotion.AnimationOverride=null;
            Assert.False(OperatorMotion.Enabled);await Mr4Navigate(f.Window,PhysicalKey.Digit2);
            var card=Named<Control>(f.Window,"PresCardWindow");await PresPlanReveal(f,card);
            if(!PresWindowOpen(f))PresPlanProvider(card).Invoke();
            await Until(()=>PresWindowOpen(f)&&Named<TextBlock>(f.Window,"WindowToggleLabel").Text=="Close","Original action must positively open output before actual Home hides it.");
            using var changes=new PresPlanChanges(f.Match);string state=Scores(f.Match),screen=PresPlanRead<string>(f,"_activeScreenKey");
            PresExternalTruth(PresExternalRead(f,"original-open",changes),true,false,screen,state);
            await PresExternalCommand(f,"Home / resume","home");
            var home=Named<SportSelectionScreen>(f.Window,"SportSelectionPanel");
            await Until(()=>home.IsEffectivelyVisible&&!PresWindowOpen(f),"The actual public Home command must mount Home and hide actual output.");
            Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            receipts.Add(PresExternalRead(f,"actual-home-output-hidden",changes));
            Capture(f.Window,$"pres-window-external-home-{width}");
            await PresExternalHomeResumePointer(f,home);
            await Until(()=>!home.IsVisible&&Named<Control>(f.Window,"MainContent").IsEffectivelyVisible,"Physical current-session Resume must return to the existing operator console.");
            Assert.False(PresWindowOpen(f));Assert.True(PresPlanRead<bool>(f,"_outputActivationRequired"));
            await PresPlanReveal(f,card);receipts.Add(PresExternalRead(f,"physical-current-session-resume",changes));
            Capture(f.Window,$"pres-window-external-resumed-off-{width}");
            Assert.Equal(2,receipts.Count);
            Assert.All(receipts,receipt=>PresExternalTruth(receipt,false,true,screen,state));
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            Mr5FinalsWriteTrace($"pres-window-external-home-resume-{width}",new{Error=error,Receipts=receipts});
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
