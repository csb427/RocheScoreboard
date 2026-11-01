// Supporting187-source-copy regressions. Production GlobalSettingsScreen/SettingsWorkspace
// mounted in a real compact Window; not a full Home navigation/native WPF claim.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class SettingsCompactLifetimeTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Read<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Hidden)!.GetValue(owner)!;
    private static async Task Settle(int milliseconds=70){await Task.Delay(milliseconds);Avalonia.Threading.Dispatcher.UIThread.RunJobs();}
    private static async Task Until(Func<bool> ready,string reason)
    {var timer=Stopwatch.StartNew();while(!ready()&&timer.ElapsedMilliseconds<3500)await Settle(25);Assert.True(ready(),reason);}
    private static bool Within(Control expected,IInputElement? actual)=>actual is Control value&&(ReferenceEquals(expected,value)||value.GetVisualAncestors().Contains(expected));
    private static Rect Box(TopLevel window,Control value)
    {
        var points=new[]{new Point(0,0),new Point(value.Bounds.Width,0),new Point(0,value.Bounds.Height),new Point(value.Bounds.Width,value.Bounds.Height)}.Select(p=>value.TranslatePoint(p,window)).ToArray();
        Assert.All(points,p=>Assert.NotNull(p));var actual=points.Select(p=>p!.Value).ToArray();return new Rect(new Point(actual.Min(p=>p.X),actual.Min(p=>p.Y)),new Point(actual.Max(p=>p.X),actual.Max(p=>p.Y)));
    }
    private static void Ready(Window window,Control value)
    {
        Assert.True(value.IsLoaded&&value.IsEffectivelyVisible&&value.IsEffectivelyEnabled);
        var box=Box(window,value);Assert.True(box.Width>16&&box.Height>16);Assert.True(new Rect(window.ClientSize).Contains(box));
        foreach(var p in new[]{box.Center,new Point(box.Left+3,box.Center.Y),new Point(box.Right-3,box.Center.Y),new Point(box.Center.X,box.Top+3),new Point(box.Center.X,box.Bottom-3)})
            Assert.True(Within(value,window.InputHitTest(p)),"Actual compact opener must own all five input probes.");
    }
    private static void Press(Window window,Control value)
    {Ready(window,value);var p=Box(window,value).Center;window.MouseMove(p);window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);}
    private static void Key(Window window,PhysicalKey key)
    {window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);}
    private static Dictionary<string,string> Files(string root)=>Directory.Exists(root)?Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(root,p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))):new();
    private static string State(Fixture f)=>JsonSerializer.Serialize(new{Category=Read<string>(f.Workspace,"_category"),Scope=Read<string?>(f.Workspace,"_scope"),Query=Read<OperatorSearchBox>(f.Workspace,"_search").Text,Changes=f.CategoryChanges});
    private static void Trace(string name,object payload)
    {string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Private capture directory required.");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true}));}
    private sealed class Fixture:IAsyncDisposable
    {
        private readonly string? previousData=Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly bool? previousMotion=OperatorMotion.AnimationOverride;
        internal readonly string Data=Path.Combine(Path.GetTempPath(),"roche-settings-compact-"+Guid.NewGuid().ToString("N"));
        internal string StorageRoot="";
        private Dictionary<string,byte[]>? latchedSnapshot;
        internal Window Window=null!;internal GlobalSettingsScreen View=null!;internal SettingsWorkspace Workspace=null!;
        internal Border Ancestor=null!;internal TextBox Sentinel=null!;internal Button Opener=null!;internal Popup Popup=null!;
        internal int CategoryChanges;internal bool Closed;
        internal static async Task<Fixture> Create()
        {
            var f=new Fixture();
            try
            {
                // TestApp's own PID root and every native folder must be proven
                // before preferences can be read or any path enumerated/deleted.
                string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                string expected=Path.Combine(temp,"roche-production-tests-"+Environment.ProcessId);
                Assert.Equal(Path.GetFullPath(expected),Path.GetFullPath(f.previousData??throw new InvalidOperationException("Missing TestApp private PID root.")));
                Assert.StartsWith(temp+Path.DirectorySeparatorChar,Path.GetFullPath(f.Data),StringComparison.Ordinal);Assert.False(Directory.Exists(f.Data));
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",f.Data);
                foreach(var folder in new[]{Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData,Environment.SpecialFolder.MyDocuments})
                    Assert.Equal(Path.GetFullPath(f.Data),NativeStoragePaths.GetFolderPath(folder));
                string processRoot=Path.Combine(temp,"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(Path.GetFullPath(processRoot),Path.GetFullPath(SelfTestStorage.RootPath));
                f.StorageRoot=SelfTestStorage.IsEnabled?SelfTestStorage.RootPath:f.Data;
                Assert.StartsWith(Path.GetFullPath(f.StorageRoot)+Path.DirectorySeparatorChar,Path.GetFullPath(SelfTestStorage.DocumentsDirectory),StringComparison.Ordinal);
                if(SelfTestStorage.IsEnabled)
                    f.latchedSnapshot=Directory.Exists(f.StorageRoot)?Directory.EnumerateFiles(f.StorageRoot,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(f.StorageRoot,p),File.ReadAllBytes):new();
                Directory.CreateDirectory(f.Data);OperatorMotion.AnimationOverride=false;
                f.View=new GlobalSettingsScreen();
                f.Ancestor=new Border{Child=f.View};f.Sentinel=new TextBox{Name="OutsideSettingsFocus",Text="Keep this focus"};
                var root=new Grid();root.RowDefinitions.Add(new RowDefinition(GridLength.Star));root.RowDefinitions.Add(new RowDefinition(new GridLength(42)));
                root.Children.Add(f.Ancestor);Grid.SetRow(f.Sentinel,1);root.Children.Add(f.Sentinel);
                f.Window=new Window{Width=420,Height=320,Content=root,Title="Settings lifecycle supporting fixture"};f.Window.Closed+=(sender,_)=>{if(ReferenceEquals(sender,f.Window))f.Closed=true;};
                f.Window.Show();f.Window.Activate();
                await Until(()=>f.View.GetVisualDescendants().OfType<SettingsWorkspace>().Any(value=>value.IsLoaded&&value.IsEffectivelyVisible&&value.Bounds.Width>0),"Production workspace must mount its actual visual template and lay out.");
                f.Workspace=Assert.Single(f.View.GetVisualDescendants().OfType<SettingsWorkspace>());
                f.Opener=Read<Button>(f.Workspace,"_compactChoice");f.Popup=Read<Popup>(f.Workspace,"_compactMenu");
                await Until(()=>f.Opener.IsEffectivelyVisible&&f.Opener.Bounds.Width>16,"Actual compact category opener must be visible.");
                Ready(f.Window,f.Opener);Assert.Equal("Settings categories",AutomationProperties.GetName(f.Opener));Assert.False(f.Popup.IsOpen);
                f.Workspace.CategoryChanged+=f.Changed;await Settle();return f;
            }
            catch{await f.DisposeAsync();throw;}
        }
        private void Changed(string _)=>CategoryChanges++;
        internal async Task Open()
        {
            Press(Window,Opener);
            await Until(()=>Popup.IsOpen&&Popup.Child is Control c&&c.IsLoaded&&c.IsEffectivelyVisible&&c.Bounds.Width>16&&TopLevel.GetTopLevel(c) is TopLevel host&&host.IsVisible&&c.GetVisualDescendants().OfType<Button>().Any(b=>b.IsLoaded&&b.IsEffectivelyVisible&&b.Bounds.Width>16),"Actual category popup must attach to its visible platform host and lay out before retirement.");
            Assert.True(Workspace.IsLoaded);Assert.Same(Opener,Popup.PlacementTarget);
            var child=Assert.IsAssignableFrom<Control>(Popup.Child);var root=Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(child));
            using(var image=root.CaptureRenderedFrame())Assert.NotNull(image);
            var choice=Assert.Single(child.GetVisualDescendants().OfType<Button>(),b=>AutomationProperties.GetName(b)=="All settings");
            var box=Box(root,choice);
            Assert.True(box.Width>16&&box.Height>16);Assert.True(new Rect(root.ClientSize).Contains(box));
            foreach(var point in new[]{box.Center,new Point(box.Left+3,box.Center.Y),new Point(box.Right-3,box.Center.Y),new Point(box.Center.X,box.Top+3),new Point(box.Center.X,box.Bottom-3)})
                Assert.True(Within(choice,root.InputHitTest(point)),"Actual popup choice must own all five rendered input probes before retirement.");
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if(Workspace is not null)Workspace.CategoryChanged-=Changed;
                if(Window is not null&&!Closed)Window.Close();await Settle(30);
                if(latchedSnapshot is not null)
                {
                    foreach(string p in Directory.Exists(StorageRoot)?Directory.EnumerateFiles(StorageRoot,"*",SearchOption.AllDirectories):Array.Empty<string>())
                        if(!latchedSnapshot.ContainsKey(Path.GetRelativePath(StorageRoot,p)))File.Delete(p);
                    foreach(var p in latchedSnapshot){string path=Path.Combine(StorageRoot,p.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,p.Value);}
                }
            }
            finally
            {
                OperatorMotion.AnimationOverride=previousMotion;Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",previousData);
                if(Directory.Exists(Data))Directory.Delete(Data,true);
            }
        }
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","SettingsCompactAcceptedNavigationAndOwnerReplacement")]
    public async Task RealCategoryKeysAndReparentObserveOnlyTheCurrentOwnerWithoutPreferenceChanges(bool reduced)
    {
        await using var f=await Fixture.Create();bool previousGlass=OperatorAppearance.GlassEnabled,previousReduce=OperatorAppearance.ReduceMotion;
        Window? old=null;
        try
        {
            OperatorAppearance.Configure(previousGlass,reduced);OperatorMotion.AnimationOverride=null;
            var files=Files(f.StorageRoot);await f.Open();
            var choices=Read<Dictionary<string,Button>>(f.Workspace,"_compactButtons");var last=choices.Values.Last();string lastCategory=(string)last.Tag!;Assert.NotEqual("ALL",lastCategory);
            await Until(()=>choices["ALL"].IsFocused,"Current Opened must focus the selected real All settings choice.");
            Key(f.Window,PhysicalKey.End);Assert.True(last.IsFocused);Key(f.Window,PhysicalKey.Enter);await Settle();
            Assert.False(f.Popup.IsOpen);Assert.True(f.Opener.IsFocused);Assert.Equal(lastCategory,Read<string>(f.Workspace,"_category"));Assert.Equal(1,f.CategoryChanges);
            Assert.Equal(AutomationProperties.GetName(last),Read<TextBlock>(f.Workspace,"_compactLabel").Text);Assert.Equal(files,Files(f.StorageRoot));
            await f.Open();await Until(()=>last.IsFocused,"Reopened current category must own real popup focus.");
            Key(f.Window,PhysicalKey.Home);Assert.True(choices["ALL"].IsFocused);Key(f.Window,PhysicalKey.Enter);await Settle();
            Assert.Equal("ALL",Read<string>(f.Workspace,"_category"));Assert.Equal(2,f.CategoryChanges);Assert.False(f.Popup.IsOpen);Assert.True(f.Opener.IsFocused);Assert.Equal(files,Files(f.StorageRoot));
            string state=State(f);old=f.Window;f.Ancestor.Child=null;await Settle();Assert.False(f.Workspace.IsLoaded);Assert.False(f.Popup.IsOpen);
            f.Ancestor=new Border{Child=f.View};var root=new Grid();root.RowDefinitions.Add(new RowDefinition(GridLength.Star));root.RowDefinitions.Add(new RowDefinition(new GridLength(42)));
            f.Sentinel=new TextBox{Name="NewOwnerFocus",Text="New owner"};root.Children.Add(f.Ancestor);Grid.SetRow(f.Sentinel,1);root.Children.Add(f.Sentinel);
            f.Window=new Window{Width=420,Height=320,Content=root,Title="Replacement Settings owner"};f.Window.Closed+=(sender,_)=>{if(ReferenceEquals(sender,f.Window))f.Closed=true;};f.Window.Show();f.Window.Activate();
            await Until(()=>f.Workspace.IsLoaded&&f.Opener.IsEffectivelyVisible,"The same production workspace must really reattach to its new owner.");await Settle();Ready(f.Window,f.Opener);await f.Open();
            old.IsEnabled=false;old.Hide();old.Close();old=null;await Settle();
            Assert.True(f.Popup.IsOpen,"Retired old-owner disable/hide/Close must not dismiss the current new-owner menu.");Assert.Equal(state,State(f));Assert.Equal(files,Files(f.StorageRoot));
            f.Ancestor.IsEnabled=false;await Settle();Assert.False(f.Popup.IsOpen,"Current new-owner ancestry must still retire the menu.");Assert.Equal(state,State(f));Assert.Equal(files,Files(f.StorageRoot));
            f.Ancestor.IsEnabled=true;await Settle();Assert.True(f.Sentinel.Focus(NavigationMethod.Tab));await Settle();Assert.True(f.Sentinel.IsFocused);Assert.False(f.Popup.IsOpen);
            using var image=f.Window.CaptureRenderedFrame();Assert.NotNull(image);string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!;image.Save(Path.Combine(dir,"compact-current-owner-"+reduced+".png"));
            Trace("compact-current-owner-"+reduced,new{Reduced=reduced,Category=Read<string>(f.Workspace,"_category"),f.CategoryChanges,f.Popup.IsOpen,OutsideFocus=f.Sentinel.IsFocused,FilesUnchanged=JsonSerializer.Serialize(files)==JsonSerializer.Serialize(Files(f.StorageRoot))});
        }
        finally{old?.Close();OperatorAppearance.Configure(previousGlass,previousReduce);}
    }

    [AvaloniaTheory]
    [InlineData("owner-hide")][InlineData("ancestor-hide")][InlineData("owner-disable")][InlineData("ancestor-disable")]
    [Trait("Evidence","SettingsCompactActualLifetime")]
    public async Task OpenOriginalCompactMenuRetiresAtItsActualHostBoundaryWithoutChangingCategory(string boundary)
    {
        await using var f=await Fixture.Create();await f.Open();string before=State(f);var files=Files(f.StorageRoot);
        try
        {
            switch(boundary){case "owner-hide":f.Window.Hide();break;case "ancestor-hide":f.Ancestor.IsVisible=false;break;case "owner-disable":f.Window.IsEnabled=false;break;case "ancestor-disable":f.Ancestor.IsEnabled=false;break;}
            await Settle();
            Trace("compact-boundary-"+boundary,new{f.Popup.IsOpen,PopupChildVisible=(f.Popup.Child as Control)?.IsEffectivelyVisible,PopupHostVisible=(f.Popup.Host as Control)?.IsEffectivelyVisible,PopupRoot=(f.Popup.Child as Control)?.GetVisualRoot()?.GetType().Name,WorkspaceLoaded=f.Workspace.IsLoaded,WorkspaceVisible=f.Workspace.IsEffectivelyVisible,WorkspaceEnabled=f.Workspace.IsEffectivelyEnabled,Before=before,After=State(f),FilesUnchanged=JsonSerializer.Serialize(files)==JsonSerializer.Serialize(Files(f.StorageRoot))});
            Assert.False(f.Popup.IsOpen,"The real compact category popup must retire when its retained owner/ancestor becomes unavailable.");
            Assert.Equal(before,State(f));Assert.Equal(files,Files(f.StorageRoot));
        }
        finally{f.Ancestor.IsVisible=true;f.Ancestor.IsEnabled=true;f.Window.IsEnabled=true;if(!f.Window.IsVisible)f.Window.Show();}
        await Settle();Assert.False(f.Popup.IsOpen);Assert.Equal(before,State(f));
        Assert.True(f.Sentinel.Focus(NavigationMethod.Tab));await Settle();Assert.True(f.Sentinel.IsFocused,"Retired work may not take focus back on Show/enable.");
        await f.Open();Assert.Equal(before,State(f));Key(f.Window,PhysicalKey.Escape);await Settle();Assert.False(f.Popup.IsOpen);
        Assert.True(f.Opener.IsFocused,"Current valid Escape returns focus to the compact opener.");Assert.Equal(before,State(f));
    }

    [AvaloniaFact][Trait("Evidence","SettingsCompactActualLifetime")]
    public async Task ActualOwnerCloseRetiresTheMenuWithoutChangingItsCurrentCategory()
    {
        await using var f=await Fixture.Create();await f.Open();string before=State(f);var files=Files(f.StorageRoot);
        f.Window.Close();await Settle();
        Trace("compact-owner-close",new{f.Closed,f.Popup.IsOpen,WorkspaceLoaded=f.Workspace.IsLoaded,Before=before,After=State(f)});
        Assert.True(f.Closed);Assert.False(f.Popup.IsOpen);Assert.False(f.Workspace.IsLoaded);Assert.Equal(before,State(f));Assert.Equal(files,Files(f.StorageRoot));
    }

    [AvaloniaFact][Trait("Evidence","SettingsCompactActualLifetime")]
    public async Task ActualQueuedOpenThenOwnerHideCannotFocusOrReopenAnUnavailableMenu()
    {
        await using var f=await Fixture.Create();string before=State(f);var files=Files(f.StorageRoot);
        // Raw pointer release accepts the original opener; no dispatcher pump or
        // await occurs before Hide, leaving its normal Opened focus queued.
        Press(f.Window,f.Opener);Assert.True(f.Popup.IsOpen);f.Window.Hide();await Settle();
        Trace("compact-queued-open-hidden",new{f.Popup.IsOpen,PopupChildVisible=(f.Popup.Child as Control)?.IsEffectivelyVisible,PopupHostVisible=(f.Popup.Host as Control)?.IsEffectivelyVisible,PopupRoot=(f.Popup.Child as Control)?.GetVisualRoot()?.GetType().Name,WorkspaceLoaded=f.Workspace.IsLoaded,Before=before,After=State(f)});
        Assert.False(f.Popup.IsOpen,"Queued Opened focus cannot preserve a popup after its actual owner hides.");Assert.Equal(before,State(f));Assert.Equal(files,Files(f.StorageRoot));
        f.Window.Show();await Settle();Assert.True(f.Sentinel.Focus(NavigationMethod.Tab));await Settle(130);Assert.True(f.Sentinel.IsFocused);Assert.False(f.Popup.IsOpen);Assert.Equal(before,State(f));
    }
}
