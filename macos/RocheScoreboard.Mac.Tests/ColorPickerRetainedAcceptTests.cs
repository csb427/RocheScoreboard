// OUTSIDE ONLY: UNCOMPILED/UNRUN. Supplemental partial using the existing
// exclusive collection/public Pick/real modal/PickerStorage. No private handler,
// constructor, mutable-field writes, direct RecentColorStorage.Record or fake provider.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ColorPickerValidationCurrentDraftTests
{
    private static object RetainedHistoryFingerprints(byte[]?[] history)=>
        history.Select(bytes=>bytes is null?null:new{bytes=bytes.Length,sha256=Convert.ToHexString(SHA256.HashData(bytes))}).ToArray();
    private static bool PermittedClosedProviderRefusal(Exception? error)=>error is null || error.GetType().FullName is
        "Avalonia.Automation.ElementNotEnabledException" or "Avalonia.Automation.ElementNotAvailableException";
    private static void RetainedProviderEvidence(string name,object evidence)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-picker-retained-provider");
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }

    [AvaloniaFact]
    public async Task RetainedOriginalAcceptProviderAfterActualCancelCannotRecordHistoryOrChangeClosedResult()
    {
        using var storage=new PickerStorage();var owner=new NativeWindow{Width=900,Height=700};owner.Show();
        var previews=new List<Color>();Color initial=Colors.CornflowerBlue;
        IInvokeProvider? retained=null;ColorPickerWindow? closed=null;Button? oldAccept=null;int lateClicks=0;
        try
        {
            var cancelled=await Pick(owner,initial,previews,async picker=>
            {
                closed=picker;await Type(picker,Named<TextBox>(picker,"HexBox"),"#12AB34");
                oldAccept=Action(picker,"USE COLOUR");
                retained=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(oldAccept));
                // Keep the actual original provider, then CANCEL through the
                // original actual button; no cleanup may satisfy this oracle.
                Invoke(Action(picker,"CANCEL"));await Until(()=>!picker.IsVisible,"Actual original CANCEL must hide before Pick cleanup.");
            });
            Assert.False(cancelled.accepted);Assert.Equal(initial,cancelled.selected);Assert.Equal(initial,previews[^1]);
            Assert.NotNull(closed);Assert.NotNull(retained);Assert.NotNull(oldAccept);Assert.False(closed.IsVisible);
            Assert.False(closed.DialogResult);Assert.Empty(owner.OwnedWindows.OfType<ColorPickerWindow>());
            Color selectedBefore=closed.SelectedColor;string? rawBefore=Named<TextBox>(closed,"HexBox").Text;
            var previewsBefore=previews.ToArray();byte[]?[] historyBefore=storage.Bytes();oldAccept.Click+=(_,_)=>lateClicks++;
            Exception? invokeError=null;
            try{retained.Invoke();}catch(Exception error){invokeError=error;}
            await Pump(80);
            RetainedProviderEvidence("cancelled-retained-original-accept",new
            {
                scope="Actual public TryPick has returned false and rolled back before retained original provider invocation; no private handler/field route.",
                cancelled.accepted,returned=cancelled.selected.ToString(),wasSelected=selectedBefore.ToString(),nowSelected=closed.SelectedColor.ToString(),
                rawBefore,rawNow=Named<TextBox>(closed,"HexBox").Text,closed.IsVisible,closed.DialogResult,lateClicks,
                invokeError=invokeError?.ToString(),providerRefusalRecognized=PermittedClosedProviderRefusal(invokeError),
                beforeHistory=RetainedHistoryFingerprints(historyBefore),afterHistory=RetainedHistoryFingerprints(storage.Bytes()),
                beforePreview=previewsBefore.Select(c=>c.ToString()),afterPreview=previews.Select(c=>c.ToString())
            });
            // FIRST post-invocation oracles are real preserved state/storage.
            // An exception after a stale handler write cannot hide that failure.
            storage.AssertBytes(historyBefore);Assert.Equal(selectedBefore,closed.SelectedColor);
            Assert.Equal(rawBefore,Named<TextBox>(closed,"HexBox").Text);Assert.Equal(previewsBefore,previews.ToArray());
            Assert.False(closed.IsVisible);Assert.False(closed.DialogResult);Assert.Empty(owner.OwnedWindows.OfType<ColorPickerWindow>());
            Assert.True(PermittedClosedProviderRefusal(invokeError),"Unexpected retained-provider exception requires diagnosis; see JSON. A documented provider refusal is allowed, not a fabricated app-handler reproduction.");
        }
        finally{foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();}
    }

    [AvaloniaFact]
    public async Task RepeatedAcceptedProviderCannotRewriteHistoryOrAffectNewActualPicker()
    {
        using var storage=new PickerStorage();var owner=new NativeWindow{Width=900,Height=700};owner.Show();
        var oldPreviews=new List<Color>();var currentPreviews=new List<Color>();
        IInvokeProvider? retained=null;ColorPickerWindow? old=null;Button? oldAccept=null;int repeatedClicks=0;
        Color first=Color.FromRgb(0x12,0xAB,0x34),currentInitial=Colors.CornflowerBlue;
        try
        {
            var accepted=await Pick(owner,Colors.Red,oldPreviews,async picker=>
            {
                old=picker;await Type(picker,Named<TextBox>(picker,"HexBox"),"#12AB34");oldAccept=Action(picker,"USE COLOUR");
                retained=Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(oldAccept));
                retained.Invoke();await Until(()=>!picker.IsVisible,"First current valid acceptance must hide actual modal before cleanup.");
            });
            Assert.True(accepted.accepted);Assert.Equal(first,accepted.selected);Assert.NotNull(old);Assert.False(old.IsVisible);Assert.True(old.DialogResult);
            Assert.NotNull(retained);Assert.NotNull(oldAccept);var oldPreviewBefore=oldPreviews.ToArray();Color oldSelected=old.SelectedColor;
            // The first legitimate acceptance must have produced only one main
            // history payload in this isolated empty scope, not a replacement backup.
            byte[]?[] afterFirst=storage.Bytes();Assert.NotNull(afterFirst[0]);Assert.Null(afterFirst[1]);
            oldAccept.Click+=(_,_)=>repeatedClicks++;
            var currentCancelled=await Pick(owner,currentInitial,currentPreviews,async current=>
            {
                Assert.NotSame(old,current);Assert.True(current.IsVisible);Assert.Null(current.DialogResult);
                await Type(current,Named<TextBox>(current,"HexBox"),"#4567AB");
                Color currentSelected=current.SelectedColor;string? raw=Named<TextBox>(current,"HexBox").Text;
                var currentPreviewBefore=currentPreviews.ToArray();byte[]?[] historyBefore=storage.Bytes();
                // Two repeated confirmations retained from the completed OLD
                // modal while the actual NEW owned modal is current. Do not
                // alter old fields/reopen old window or call its private handler.
                for(int attempt=0;attempt<2;attempt++)
                {
                    Exception? error=null;try{retained.Invoke();}catch(Exception exception){error=exception;}
                    await Pump(60);
                    RetainedProviderEvidence("accepted-obsolete-provider-"+attempt,new
                    {
                        scope="Actual first TryPick returned true; retained old original provider invoked while second actual public owned modal is open.",attempt,repeatedClicks,
                        providerError=error?.ToString(),providerRefusalRecognized=PermittedClosedProviderRefusal(error),oldVisible=old.IsVisible,old.DialogResult,
                        oldSelected=old.SelectedColor.ToString(),currentVisible=current.IsVisible,currentResult=current.DialogResult,
                        currentSelected=current.SelectedColor.ToString(),rawNow=Named<TextBox>(current,"HexBox").Text,
                        beforeHistory=RetainedHistoryFingerprints(historyBefore),afterHistory=RetainedHistoryFingerprints(storage.Bytes()),
                        beforeOldPreview=oldPreviewBefore.Select(c=>c.ToString()),afterOldPreview=oldPreviews.Select(c=>c.ToString()),
                        beforeCurrentPreview=currentPreviewBefore.Select(c=>c.ToString()),afterCurrentPreview=currentPreviews.Select(c=>c.ToString())
                    });
                    storage.AssertBytes(historyBefore);Assert.Equal(oldSelected,old.SelectedColor);Assert.Equal(oldPreviewBefore,oldPreviews.ToArray());
                    Assert.False(old.IsVisible);Assert.True(old.DialogResult);Assert.True(current.IsVisible);Assert.Null(current.DialogResult);
                    Assert.Same(current,Assert.Single(owner.OwnedWindows.OfType<ColorPickerWindow>()));
                    Assert.Equal(currentSelected,current.SelectedColor);Assert.Equal(raw,Named<TextBox>(current,"HexBox").Text);
                    Assert.Equal(currentPreviewBefore,currentPreviews.ToArray());
                    Assert.True(PermittedClosedProviderRefusal(error),"Unexpected old-provider exception requires diagnosis; not a claimed handler failure without state evidence.");
                }
                storage.AssertBytes(afterFirst);Invoke(Action(current,"CANCEL"));
                await Until(()=>!current.IsVisible,"Current picker cancellation must be actual, before cleanup.");
            });
            Assert.False(currentCancelled.accepted);Assert.Equal(currentInitial,currentCancelled.selected);Assert.Equal(currentInitial,currentPreviews[^1]);
            storage.AssertBytes(afterFirst);Assert.Equal(oldPreviewBefore,oldPreviews.ToArray());Assert.Empty(owner.OwnedWindows.OfType<ColorPickerWindow>());
        }
        finally{foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();}
    }
}
