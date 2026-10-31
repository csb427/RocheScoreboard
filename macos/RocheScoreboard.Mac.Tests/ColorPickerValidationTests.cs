// OUTSIDE UNCOMPILED/UNRUN DRAFT. Integrate selectively under the existing exclusive UI collection.
// Uses production TryPick + actual owned native modal + real Ctrl+A/TextInput + Invoke provider.
// Exactly four cases: two rejection/focus sizes, repair/help contract, cancel positive.
// No execution; original before pins remain current. Existing draft preserved.
// No reflection/private Accept invocation. Storage ownership is proven before any fixture IO;
// existing self-test latch and environment mode are preserved, never activated to pass a guard.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[Collection("Cricket player menu preferences baseline")]
public sealed partial class ColorPickerValidationCurrentDraftTests
{
    private static async Task Pump(int ms=50){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static async Task Until(Func<bool> ready,string reason)
    {for(int count=0;count<120&&!ready();count++)await Pump(20);Assert.True(ready(),reason);}
    private static void Key(Window owner,PhysicalKey key,RawInputModifiers modifiers=RawInputModifiers.None)
    {
        Window? modalOwner=owner.Owner as Window;
        owner.KeyPressQwerty(key,modifiers);
        // Escape can synchronously close the modal on key-down. The real
        // key-up then belongs to the activated owner, not a disposed backend.
        Window releaseTarget=owner.IsVisible?owner:Assert.IsAssignableFrom<Window>(modalOwner);
        Assert.True(releaseTarget.IsVisible);releaseTarget.KeyReleaseQwerty(key,RawInputModifiers.None);
    }
    private static T Named<T>(Control root,string name) where T:Control =>
        Assert.IsType<T>(NativeLookup.Find(root,name));
    private static Button Action(ColorPickerWindow dialog,string label) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b=>Equals(b.Content,label));
    private static void Invoke(Button button)=>
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button)).Invoke();
    private static void Onscreen(Control control,Window window)
    {
        Assert.True(control.IsEffectivelyVisible);Assert.True(control.Bounds.Width>0&&control.Bounds.Height>0);
        Point point=Assert.IsType<Point>(control.TranslatePoint(default,window));
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(point,control.Bounds.Size)),
            $"{control.Name} must remain wholly visible; {point}, {control.Bounds.Size}, viewport {window.ClientSize}.");
    }
    private static async Task Type(ColorPickerWindow picker,TextBox box,string value)
    {
        box.BringIntoView();box.Focus(NavigationMethod.Tab);await Pump();Assert.True(box.IsFocused);
        Key(picker,PhysicalKey.A,RawInputModifiers.Control);picker.KeyTextInput(value);Assert.Equal(value,box.Text);
    }
    private static async Task<(bool accepted,Color selected)> Pick(Window owner,Color initial,
        List<Color> preview,Func<ColorPickerWindow,Task> exercise)
    {
        await Until(()=>owner.IsLoaded&&owner.IsVisible&&owner.ClientSize.Width>0&&owner.ClientSize.Height>0,
            "Actual owner must be loaded before public TryPick assigns its Owner.");
        Assert.Empty(owner.OwnedWindows.OfType<ColorPickerWindow>());
        bool? previousOverride=OperatorMotion.AnimationOverride;
        ExceptionDispatchInfo? failure=null;
        var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var trigger=DispatcherTimer.RunOnce(async ()=>
        {
            ColorPickerWindow? dialog=null;
            try
            {
                await Until(()=>owner.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Public TryPick must attach its actual owned modal.");
                dialog=Assert.Single(owner.OwnedWindows.OfType<ColorPickerWindow>());
                Assert.Same(owner,dialog.Owner);
                await Until(()=>dialog.IsLoaded&&dialog.IsVisible&&Named<TextBox>(dialog,"HexBox").Bounds.Width>0,
                    "Owned modal and original HexBox must load/measure before validation input.");
                await exercise(dialog);
            }
            catch(Exception error){failure=ExceptionDispatchInfo.Capture(error);}
            finally
            {
                if(dialog?.IsVisible==true)dialog.Close(false);
                foreach(var remaining in owner.OwnedWindows.OfType<ColorPickerWindow>().ToArray())remaining.Close(false);
                finished.TrySetResult();
            }
        },TimeSpan.FromMilliseconds(60));
        // NativeAwait pumps the modal loop; callback always closes it, even on assertion failure.
        try
        {
            OperatorMotion.AnimationOverride=false;
            bool accepted=ColorPickerWindow.TryPick(owner,initial,out Color selected,preview.Add);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));failure?.Throw();
            await Until(()=>!owner.OwnedWindows.OfType<ColorPickerWindow>().Any(),"Public modal completion must release owned picker.");
            return(accepted,selected);
        }
        finally{OperatorMotion.AnimationOverride=previousOverride;}
    }

    private static void CapturePicker(ColorPickerWindow picker,string stage)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-colour-captures");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,stage+"-"+picker.ClientSize.Width+"x"+picker.ClientSize.Height);
        using var frame=picker.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(path+".png");
        var box=Named<TextBox>(picker,"HexBox");var feedback=Named<TextBlock>(picker,"ValidationText");
        File.WriteAllText(path+".json",System.Text.Json.JsonSerializer.Serialize(new
        {
            stage,picker.ClientSize,Text=box.Text,Selected=picker.SelectedColor.ToString(),HexFocused=box.IsFocused,
            Name=AutomationProperties.GetName(box),Help=AutomationProperties.GetHelpText(box),Feedback=feedback.Text,
            Live=AutomationProperties.GetLiveSetting(feedback).ToString(),picker.DialogResult,picker.IsVisible
        },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }

    [AvaloniaTheory]
    [InlineData(500,760)][InlineData(420,320)]
    public async Task InvalidAcceptPreservesRawDraftLastValidPreviewAndHistoryAndRevealsCorrection(int width,int height)
    {
        using var storage=new PickerStorage();var owner=new NativeWindow{Width=900,Height=700};owner.Show();
        var preview=new List<Color>();Color initial=Colors.CornflowerBlue;
        try
        {
            var result=await Pick(owner,initial,preview,async picker=>
            {
                picker.Width=width;picker.MaxHeight=height;picker.Height=height;await Pump();
                var box=Named<TextBox>(picker,"HexBox");var feedback=Named<TextBlock>(picker,"ValidationText");
                await Type(picker,box,"#123456");Color valid=picker.SelectedColor;
                int validPreviewCount=preview.Count;byte[]?[] history=storage.Bytes();
                await Type(picker,box,"#GGGGGG");Assert.Equal(valid,picker.SelectedColor);
                // Put the footer in view first, so this cannot pass from pre-focused visible hex.
                var accept=Action(picker,"USE COLOUR");accept.BringIntoView();accept.Focus();await Pump();
                Assert.False(box.IsFocused);Onscreen(accept,picker);Invoke(accept);await Pump();
                Assert.Null(picker.DialogResult);Assert.True(picker.IsVisible);
                Assert.Equal("#GGGGGG",box.Text);Assert.Equal(valid,picker.SelectedColor);
                Assert.Equal(validPreviewCount,preview.Count);storage.AssertBytes(history);
                CapturePicker(picker,"invalid-rejected");
                Assert.True(box.IsFocused);Assert.Equal("Hex colour",AutomationProperties.GetName(box));
                Assert.Equal("Enter a valid six-digit colour before continuing.",feedback.Text);
                Assert.Equal(feedback.Text,AutomationProperties.GetHelpText(box));
                Assert.Equal(AutomationLiveSetting.Polite,AutomationProperties.GetLiveSetting(feedback));
                Onscreen(box,picker);Onscreen(feedback,picker);
                Invoke(accept);await Pump(); // rejected repeatedly, no lost text/preview/history
                Assert.Equal("#GGGGGG",box.Text);Assert.Equal(validPreviewCount,preview.Count);
                storage.AssertBytes(history);Assert.True(box.IsFocused);
            });
            Assert.False(result.accepted);Assert.Equal(initial,result.selected);
            Assert.NotEmpty(preview);Assert.Equal(initial,preview[^1]); // actual TryPick rollback
        }
        finally{foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();}
    }

    [AvaloniaFact]
    public async Task CorrectingInvalidTextClearsAssociatedErrorAndAcceptsCurrentColour()
    {
        using var storage=new PickerStorage();var owner=new NativeWindow{Width=900,Height=700};owner.Show();
        var preview=new List<Color>();Color expected=Color.FromRgb(0x12,0xAB,0x34);
        try
        {
            var result=await Pick(owner,Colors.Red,preview,async picker=>
            {
                var box=Named<TextBox>(picker,"HexBox");var feedback=Named<TextBlock>(picker,"ValidationText");
                byte[]?[] history=storage.Bytes();
                await Type(picker,box,"oops");Invoke(Action(picker,"USE COLOUR"));await Pump();
                Assert.True(picker.IsVisible);Assert.Equal("oops",box.Text);storage.AssertBytes(history);
                await Type(picker,box,"#12AB34");Assert.Equal(expected,picker.SelectedColor);storage.AssertBytes(history);
                CapturePicker(picker,"repaired-valid");
                Assert.Equal(string.Empty,feedback.Text);
                Assert.Equal("Enter six hexadecimal digits, with an optional #.",AutomationProperties.GetHelpText(box));
                Invoke(Action(picker,"USE COLOUR"));
                await Until(()=>!picker.IsVisible,"Actual valid Use must close before cleanup; cleanup cannot satisfy acceptance.");
            });
            Assert.True(result.accepted);Assert.Equal(expected,result.selected);
            Assert.Equal(expected,preview[^1]);
            Assert.Contains("#12AB34",RecentColorStorage.Load());
        }
        finally{foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();}
    }

    [AvaloniaFact]
    public async Task CancelAfterInvalidDraftReturnsInitialColourAndDoesNotRecordPreview()
    {
        using var storage=new PickerStorage();byte[]?[] history=storage.Bytes();
        var owner=new NativeWindow{Width=900,Height=700};owner.Show();var preview=new List<Color>();
        try
        {
            var result=await Pick(owner,Colors.Red,preview,async picker=>
            {
                var box=Named<TextBox>(picker,"HexBox");await Type(picker,box,"#123456");
                await Type(picker,box,"invalid");CapturePicker(picker,"before-real-escape");Key(picker,PhysicalKey.Escape);
                await Until(()=>!picker.IsVisible,"Actual Escape must close the modal before cleanup; cleanup cannot satisfy cancellation.");
            });
            Assert.False(result.accepted);Assert.Equal(Colors.Red,result.selected);
            Assert.Equal(Colors.Red,preview[^1]);storage.AssertBytes(history);
        }
        finally{foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();}
    }

    [AvaloniaTheory][InlineData("CANCEL")][InlineData("×")]
    public async Task OriginalCancelActionsCloseThePublicModalAndRollBackWithoutCleanup(string label)
    {
        using var storage=new PickerStorage();byte[]?[] history=storage.Bytes();
        var owner=new NativeWindow{Width=900,Height=700};owner.Show();var preview=new List<Color>();
        int clicks=0;bool hiddenByAction=false;
        try
        {
            var result=await Pick(owner,Colors.Red,preview,async picker=>
            {
                await Type(picker,Named<TextBox>(picker,"HexBox"),"#123456");
                var cancel=Action(picker,label);Assert.True(cancel.IsCancel);
                cancel.BringIntoView();cancel.Focus();await Pump();Onscreen(cancel,picker);
                EventHandler<Avalonia.Interactivity.RoutedEventArgs> observed=(_,_)=>clicks++;
                cancel.Click+=observed;
                try
                {
                    Invoke(cancel);await Pump();hiddenByAction=!picker.IsVisible;
                    if(picker.IsVisible)CapturePicker(picker,label=="CANCEL"?"cancel-button-did-not-close":"close-button-did-not-close");
                    Assert.Equal(1,clicks);
                    await Until(()=>!picker.IsVisible,"Actual cancel Invoke must hide the modal before cleanup.");hiddenByAction=true;
                }
                finally{cancel.Click-=observed;}
            });
            Assert.True(hiddenByAction);Assert.False(result.accepted);Assert.Equal(Colors.Red,result.selected);
            Assert.Equal(Colors.Red,preview[^1]);storage.AssertBytes(history);
        }
        finally
        {
            string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.GetTempPath();Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,label=="CANCEL"?"cancel-action-receipt.json":"close-action-receipt.json"),
                System.Text.Json.JsonSerializer.Serialize(new{label,clicks,hiddenByAction}));
            foreach(var child in owner.OwnedWindows.ToArray())child.Close();owner.Close();
        }
    }

    // Exactly this picker reads/writes recent_colours.json/.bak and AtomicFile's
    // GUID sibling temporary files; no settings/main-window/appearance mutation.
    private sealed class PickerStorage:IDisposable
    {
        private readonly bool latched=SelfTestStorage.IsEnabled;
        private readonly string? previous=Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string home=Path.Combine(Path.GetTempPath(),"roche-colour-picker-"+Guid.NewGuid().ToString("N"));
        private readonly string processRoot=Path.Combine(Path.GetTempPath(),"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        private readonly Dictionary<string,byte[]> saved=new(StringComparer.Ordinal);
        private readonly string docs,path;
        private bool ready,created,docsExisted,snapshotComplete,mayMutate,disposed;
        internal PickerStorage()
        {
            docs=latched?Path.Combine(processRoot,"preferences"):Path.Combine(home,"Roche_Scoreboard");
            path=Path.Combine(docs,"recent_colours.json");
            try
            {
                // Prove exact owned temp/PID paths before reading, enumerating or deleting.
                Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(Path.GetDirectoryName(home)!));
                Assert.True(Guid.TryParseExact(Path.GetFileName(home)["roche-colour-picker-".Length..],"N",out _));
                Assert.Equal(Path.GetFullPath(processRoot),Path.GetFullPath(SelfTestStorage.RootPath));
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",home);
                foreach(var folder in new[]{Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData})
                    Assert.Equal(Path.GetFullPath(home),NativeStoragePaths.GetFolderPath(folder));
                Assert.Equal(latched,SelfTestStorage.IsEnabled);
                Assert.Equal(Path.GetFullPath(docs),Path.GetFullPath(SelfTestStorage.DocumentsDirectory));
                string actual=(string)typeof(RecentColorStorage).GetProperty("StoragePath",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                Assert.Equal(Path.GetFullPath(path),Path.GetFullPath(actual));ready=true;
                docsExisted=Directory.Exists(docs);
                if(docsExisted)foreach(string file in Files())saved[file]=File.ReadAllBytes(file);
                snapshotComplete=true;
                Assert.False(Directory.Exists(home));Directory.CreateDirectory(home);created=true;
                mayMutate=true;foreach(string file in saved.Keys)File.Delete(file);
            }
            catch{Dispose();throw;}
        }
        private string[] Files()
        {
            Assert.True(ready);Assert.Equal(latched,SelfTestStorage.IsEnabled);
            Assert.Equal(Path.GetFullPath(docs),Path.GetFullPath(SelfTestStorage.DocumentsDirectory));
            // AtomicFile currently uses <path>.<PID>.<GUID>.tmp; preserve every matching sibling.
            return !Directory.Exists(docs)?[]:Directory.GetFiles(docs).Where(file=>
                file==path||file==path+".bak"||file==path+".tmp"||
                (Path.GetFileName(file).StartsWith("recent_colours.json.",StringComparison.Ordinal)&&file.EndsWith(".tmp",StringComparison.Ordinal))).ToArray();
        }
        internal byte[]?[] Bytes()
        {
            Assert.True(ready&&snapshotComplete);Assert.Equal(latched,SelfTestStorage.IsEnabled);
            Assert.Equal(Path.GetFullPath(home),Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
            return new[]{path,path+".bak"}.Select(file=>File.Exists(file)?File.ReadAllBytes(file):null).ToArray();
        }
        internal void AssertBytes(byte[]?[] expected)
        {var actual=Bytes();Assert.Equal(expected.Length,actual.Length);for(int i=0;i<actual.Length;i++)Assert.Equal(expected[i],actual[i]);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try
            {
                if(ready&&snapshotComplete&&mayMutate)
                {
                    foreach(string file in Files())File.Delete(file);
                    foreach(var(file,bytes)in saved){Directory.CreateDirectory(docs);File.WriteAllBytes(file,bytes);}
                    if(!docsExisted&&Directory.Exists(docs)&&!Directory.EnumerateFileSystemEntries(docs).Any())Directory.Delete(docs);
                }
                if(created&&Directory.Exists(home))Directory.Delete(home,true);
            }
            finally{Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",previous);}
        }
    }
}
