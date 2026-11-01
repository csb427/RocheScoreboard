// OUTSIDE DIRECT-UI-REFRESH SEAM DRAFT: exactly2 cases, UNCOMPILED/UNRUN.
// Reflection invokes ONLY the existing visual UpdatePresentationCards method,
// explicitly authorized to isolate its supplied-argument contract. This is NOT
// public setup/resume coverage, and invokes no scoring/model command privately.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed record LiveRefreshReceipt(string Stage,string ActiveField,bool AttachedSelected,double Progress,
        double Surface,double Bound,double Check,string Name,string Status,string Paint,string ExpectedPaint,
        bool OutputOpen,string Model,int MatchEvents);

    [AvaloniaTheory][InlineData(420,320)][InlineData(1280,850)]
    [Trait("Evidence","DIRECT_UI_REFRESH_SEAM_NOT_PUBLIC_SETUP_RESUME")]
    public async Task DirectLiveScorebugUiRefreshUsesSuppliedSelectionWithoutMutatingOldPresentationField(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        bool reduce=OperatorAppearance.ReduceMotion;bool? animation=OperatorMotion.AnimationOverride;
        var receipts=new List<LiveRefreshReceipt>();string? error=null;
        try
        {
            var (card,_)=await LiveAfterPrepare(f);var parts=LiveAfterRead(card);
            Assert.True(OperatorAppearance.ReduceMotion);Assert.False(OperatorMotion.Enabled);
            Assert.Equal("summary",PresPlanRead<string>(f,"_activeScreenKey"));
            Assert.False(PresWindowOpen(f));Assert.False(OperatorChoiceMotion.GetSelected(card));
            await Until(()=>OperatorChoiceMotion.GetProgress(card)==0&&parts.Glyph.Progress==0,
                "The real public Summary route must establish its actual deselected endpoint before the isolated UI refresh seam.");
            using var changes=new PresPlanChanges(f.Match);string model=Scores(f.Match);
            var peer=ControlAutomationPeer.CreatePeerForElement(card);Assert.NotNull(peer);
            var border=Assert.IsAssignableFrom<Border>(card);
            LiveRefreshReceipt Read(string stage)=>new(stage,PresPlanRead<string>(f,"_activeScreenKey"),
                OperatorChoiceMotion.GetSelected(card),OperatorChoiceMotion.GetProgress(card),parts.Selection.Progress,parts.Glyph.Progress,
                Named<Border>(f.Window,"PresCheckScorebug").Opacity,peer!.GetName()??"",AutomationProperties.GetItemStatus(card)??"",
                Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color.ToString(),
                Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(f.Window,"ControlCentre.SelectionBrush")).Color.ToString(),
                PresWindowOpen(f),Scores(f.Match),changes.Count);
            var refresh=f.Window.GetType().GetMethod("UpdatePresentationCards",HiddenInstance);
            Assert.NotNull(refresh);Assert.Equal(typeof(void),refresh.ReturnType);
            // Exact direct visual seam: all call-site state assignments and
            // setup/resume transitions are intentionally outside this test.
            refresh.Invoke(f.Window,new object[]{"scorebug"});
            receipts.Add(Read("immediate-return"));
            // Reduce means there is no animation to await. Drain ordinary
            // binding/layout work before the separate eventual-bound oracle.
            await Settle(100);using(var image=f.Window.CaptureRenderedFrame())Assert.NotNull(image);
            receipts.Add(Read("after-normal-binding-and-render"));
            Capture(f.Window,$"live-scorebug-direct-ui-refresh-{width}");
            Assert.Equal(2,receipts.Count);
            foreach(var receipt in receipts)
            {
                Assert.Equal("summary",receipt.ActiveField);Assert.Equal(model,receipt.Model);Assert.Equal(0,receipt.MatchEvents);Assert.False(receipt.OutputOpen);
                Assert.Equal("Live scorebug",receipt.Name);Assert.Equal("Selected",receipt.Status);Assert.Equal(1d,receipt.Check);
                Assert.Equal(receipt.ExpectedPaint,receipt.Paint);
                Assert.True(receipt.AttachedSelected,"The supplied visual selection must not be overwritten by the not-yet-updated controller field.");
                Assert.Equal(1d,receipt.Progress,4);
            }
            var bound=receipts[1];Assert.Equal(1d,bound.Surface,4);Assert.Equal(1d,bound.Bound,4);
        }
        catch(Exception failure){error=failure.ToString();throw;}
        finally
        {
            Mr5FinalsWriteTrace($"live-scorebug-direct-ui-refresh-{width}",new{Evidence="DIRECT_UI_REFRESH_SEAM_NOT_PUBLIC_SETUP_RESUME",Error=error,Receipts=receipts});
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,reduce);OperatorMotion.AnimationOverride=animation;
        }
    }
}
