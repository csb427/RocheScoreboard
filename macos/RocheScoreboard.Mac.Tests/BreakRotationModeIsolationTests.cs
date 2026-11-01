// OUTSIDE SAFE-SCOPE DRAFT: four cases, UNCOMPILED/UNRUN. Root owns integration/execution.
// Uses a private GUID data-home while preserving the current self-test latch mode.
// Idle existing/fresh cricket panels, actual Home settings+scope+Toggle providers.
// Reflection reads state/storage paths and detaches fixture static subscription;
// no private application handler, Normalize, or synthetic event is invoked.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[Collection("Cricket player menu preferences baseline")]
public sealed partial class BreakRotationModeIsolationTests
{
    private static readonly JsonSerializerOptions Camel = new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    private const BindingFlags Private = BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Read<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static T Named<T>(Control root,string name) where T:Control=>
        Assert.IsAssignableFrom<T>(root.FindControl<T>(name));
    private static async Task Pump(int milliseconds=50){await Task.Delay(milliseconds);Dispatcher.UIThread.RunJobs();}
    private static async Task Until(Func<bool> ready,string reason)
    {for(int i=0;i<80&&!ready();i++)await Pump(25);Assert.True(ready(),reason);}
    private static bool? CricketChoice(OperatorWorkflowPreferencesData data)
    {
        var element=JsonSerializer.SerializeToElement(data,Camel);
        return element.TryGetProperty("cricketBreakPresentationRotation",out var value)
            &&value.ValueKind is JsonValueKind.True or JsonValueKind.False?value.GetBoolean():null;
    }
    private static string Unrelated(OperatorWorkflowPreferencesData data)
    {
        var sorted=new SortedDictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var property in JsonSerializer.SerializeToElement(data,Camel).EnumerateObject())
            if(property.Name is not ("aflBreakScreenEnabled" or "cricketBreakPresentationRotation"))sorted[property.Name]=property.Value.Clone();
        return JsonSerializer.Serialize(sorted);
    }
    private sealed class StorageScope:IAsyncDisposable
    {
        private readonly App app=Assert.IsType<App>(Application.Current);
        private readonly bool priorSelfTest=((App)Application.Current!).IsSelfTestMode;
        private readonly bool? priorOverride=OperatorMotion.AnimationOverride;
        private readonly bool latched=SelfTestStorage.IsEnabled;
        private readonly string? priorHome=Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string privateHome=Path.Combine(Path.GetTempPath(),"roche-break-rotation-"+Guid.NewGuid().ToString("N"));
        private readonly string processRoot=Path.Combine(Path.GetTempPath(),"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        private readonly Dictionary<string,byte[]?> saved=new(StringComparer.Ordinal);
        private readonly HashSet<string> priorDirectories=new(StringComparer.Ordinal);
        private string documents="";
        private bool pathsReady,captured,homeCreated,environmentChanged,disposed;
        private bool processRootExisted,workflowDirectoryExisted;
        internal string WorkflowPath{get;private set;}="";
        internal Roche_Scoreboard.MainWindow? Main;
        internal Window? FreshWindow;
        internal CricketControlPanel? FreshPanel;
        private static string Full(string path)=>Path.GetFullPath(path);
        private static bool Within(string root,string path)=>Full(path).StartsWith(Full(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal);
        private string WorkflowDirectory=>Path.Combine(processRoot,"workflow");
        private void AssertOwnedFile(string path)
        {
            Assert.True(pathsReady,"No file read/write/delete may precede fixture path proof.");
            bool workflow=path==WorkflowPath||path==WorkflowPath+".bak"||path==WorkflowPath+".tmp";
            Assert.True(Within(privateHome,path)||(latched&&Within(processRoot,path))||workflow,
                "File must belong to the private home, already-latched process root, or exact PID workflow whitelist: "+path);
        }
        internal void AssertOwnedPaths()
        {
            Assert.Equal(latched,SelfTestStorage.IsEnabled); // Never change the latch to pass a guard.
            Assert.Equal(Full(processRoot),Full(SelfTestStorage.RootPath));
            Assert.Equal(Full(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),Full(Path.GetDirectoryName(privateHome)!));
            string name=Path.GetFileName(privateHome);const string prefix="roche-break-rotation-";
            Assert.StartsWith(prefix,name,StringComparison.Ordinal);
            Assert.True(Guid.TryParseExact(name[prefix.Length..],"N",out _));
            Assert.Equal(Full(privateHome),Full(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
            foreach(var folder in new[]{Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData})
                Assert.Equal(Full(privateHome),NativeStoragePaths.GetFolderPath(folder));
            string expected=latched?Path.Combine(processRoot,"preferences"):Path.Combine(privateHome,"Roche_Scoreboard");
            Assert.Equal(Full(expected),Full(SelfTestStorage.DocumentsDirectory));
            Assert.True(app.IsSelfTestMode); // Workflow's separate selector uses this flag, not the latch.
            string workflow=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
            Assert.Equal(Full(Path.Combine(processRoot,"workflow","operator_workflow.json")),Full(workflow));
            if(WorkflowPath.Length>0)Assert.Equal(Full(WorkflowPath),Full(workflow));
            string recovery=latched?Path.Combine(processRoot,"recovery","game_state.json"):Path.Combine(privateHome,"Roche_Scoreboard","game_state.json");
            Assert.Equal(Full(recovery),Full(GameStateService.RecoveryStatePath));
            string legacy=(string)typeof(GameStateService).GetProperty("LegacyStateFilePath",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(Full(latched?Path.Combine(processRoot,"legacy","game_state.json"):Path.Combine(AppContext.BaseDirectory,"game_state.json")),Full(legacy));
        }
        private void Preserve(string path)
        {
            foreach(string file in new[]{path,path+".bak",path+".tmp"})
            {
                AssertOwnedFile(file);
                if(!saved.ContainsKey(file))saved[file]=File.Exists(file)?File.ReadAllBytes(file):null;
            }
        }
        internal StorageScope(bool legacyAfl,bool? explicitCricket)
        {
            try
            {
                // Prove this newly generated GUID path before its first IO;
                // do not accept an ambient temp/env path as fixture ownership.
                Assert.Equal(Full(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),Full(Path.GetDirectoryName(privateHome)!));
                const string prefix="roche-break-rotation-";string name=Path.GetFileName(privateHome);
                Assert.StartsWith(prefix,name,StringComparison.Ordinal);Assert.True(Guid.TryParseExact(name[prefix.Length..],"N",out _));
                Assert.Equal(Full(processRoot),Full(SelfTestStorage.RootPath));
                Assert.False(Directory.Exists(privateHome));
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",privateHome);environmentChanged=true;
                app.IsSelfTestMode=true;OperatorMotion.AnimationOverride=false;
                AssertOwnedPaths();
                documents=SelfTestStorage.DocumentsDirectory;
                WorkflowPath=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
                pathsReady=true;
                processRootExisted=Directory.Exists(processRoot);workflowDirectoryExisted=Directory.Exists(WorkflowDirectory);
                if(latched&&processRootExisted)
                {
                    foreach(string directory in Directory.EnumerateDirectories(processRoot,"*",SearchOption.AllDirectories).Append(processRoot))priorDirectories.Add(directory);
                    foreach(string file in Directory.EnumerateFiles(processRoot,"*",SearchOption.AllDirectories))
                    {AssertOwnedFile(file);saved[file]=File.ReadAllBytes(file);}
                }
                // An unlatched executable-side legacy save is outside our
                // ownership. Refuse the fixture if present; never read/delete it.
                string legacy=(string)typeof(GameStateService).GetProperty("LegacyStateFilePath",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
                if(!latched)Assert.False(File.Exists(legacy),"Executable-side legacy recovery exists outside this fixture; preserve it and report the blocked Home prerequisite.");
                Directory.CreateDirectory(privateHome);homeCreated=true;
                // GetStoragePath creates a directory. Its no-IO directory
                // selector was proved above BEFORE permitting this call.
                string ux=UxPreferences.GetStoragePath();Assert.Equal(Full(Path.Combine(documents,"ux_preferences.json")),Full(ux));
                Preserve(WorkflowPath);Preserve(GameStateService.RecoveryStatePath);Preserve(ux);
                if(latched)Preserve(legacy);
                AssertOwnedPaths();captured=true;
                foreach(string path in saved.Keys){AssertOwnedFile(path);if(File.Exists(path))File.Delete(path);}
                var seed=new Dictionary<string,object>{["aflBreakScreenEnabled"]=legacyAfl,["aflGoalAnimation"]="clean",["goalAnimationPreferenceVersion"]=1,["cricketAutomaticOverlays"]=false};
                if(explicitCricket.HasValue)seed["cricketBreakPresentationRotation"]=explicitCricket.Value;
                AssertOwnedFile(WorkflowPath);Directory.CreateDirectory(WorkflowDirectory);
                File.WriteAllText(WorkflowPath,JsonSerializer.Serialize(seed));
            }
            catch(Exception original)
            {
                try{RestoreFiles();}catch(Exception cleanup){throw new AggregateException("Rotation fixture setup and restoration failed",original,cleanup);}
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            if(disposed)return;disposed=true;var errors=new List<Exception>();
            void Attempt(Action action){try{action();}catch(Exception error){errors.Add(error);}}
            Attempt(()=>FreshPanel?.ClearMatch());Attempt(()=>FreshWindow?.Close());
            if(Main is not null)
            {
                Attempt(Main.Close);
                Attempt(()=>{var method=Main.GetType().GetMethod("OnQuarterLabelServiceChanged",Private);if(method is not null)QuarterLabelService.LabelsChanged-=method.CreateDelegate<Action>(Main);});
            }
            try{await Pump(150);}catch(Exception error){errors.Add(error);}
            Attempt(RestoreFiles);
            if(errors.Count>0)throw new AggregateException("Rotation fixture cleanup failed",errors);
        }
        private void RestoreFiles()
        {
            var errors=new List<Exception>();void Attempt(Action action){try{action();}catch(Exception error){errors.Add(error);}}
            try
            {
                if(captured&&pathsReady)
                {
                    if(latched&&Directory.Exists(processRoot))
                        foreach(string path in Directory.EnumerateFiles(processRoot,"*",SearchOption.AllDirectories))
                            if(!saved.ContainsKey(path))Attempt(()=>{AssertOwnedFile(path);File.Delete(path);});
                    foreach(var (path,bytes) in saved)
                        Attempt(()=>{AssertOwnedFile(path);if(bytes is null){if(File.Exists(path))File.Delete(path);}else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}});
                    if(latched&&Directory.Exists(processRoot))
                        foreach(string directory in Directory.EnumerateDirectories(processRoot,"*",SearchOption.AllDirectories).Append(processRoot).OrderByDescending(path=>path.Length))
                            if(!priorDirectories.Contains(directory))Attempt(()=>{if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);});
                    if(!latched)
                    {
                        // Only exact newly created workflow/root directories;
                        // never enumerate/delete unrelated process-root files.
                        if(!workflowDirectoryExisted)Attempt(()=>{if(Directory.Exists(WorkflowDirectory)&&!Directory.EnumerateFileSystemEntries(WorkflowDirectory).Any())Directory.Delete(WorkflowDirectory);});
                        if(!processRootExisted)Attempt(()=>{if(Directory.Exists(processRoot)&&!Directory.EnumerateFileSystemEntries(processRoot).Any())Directory.Delete(processRoot);});
                    }
                }
                if(homeCreated)Attempt(()=>{if(Directory.Exists(privateHome))Directory.Delete(privateHome,true);});
            }
            finally
            {
                OperatorMotion.AnimationOverride=priorOverride;
                try{if(environmentChanged)Environment.SetEnvironmentVariable("ROCHE_DATA_HOME",priorHome);}
                finally{app.IsSelfTestMode=priorSelfTest;}
            }
            if(errors.Count>0)throw new AggregateException("Rotation fixture file restoration failed",errors);
        }
    }

    [AvaloniaTheory]
    [InlineData(true,null)][InlineData(false,null)]
    [InlineData(false,true)][InlineData(true,false)]
    public async Task AflOnlyGlobalRotationEditPreservesExistingAndReloadedCricketChoice(bool legacyAfl,bool? explicitCricket)
    {
        await using var scope=new StorageScope(legacyAfl,explicitCricket);
        scope.AssertOwnedPaths();
        bool expectedCricket=explicitCricket??legacyAfl;
        byte[] rawBefore=File.ReadAllBytes(scope.WorkflowPath);
        bool backupBefore=File.Exists(scope.WorkflowPath+".bak");
        var loadedBefore=OperatorWorkflowPreferences.Load();
        bool? normalizedCricket=CricketChoice(loadedBefore);
        // Public Load should normalize detached data, never save the migration.
        Assert.Equal(rawBefore,File.ReadAllBytes(scope.WorkflowPath));
        Assert.Equal(backupBefore,File.Exists(scope.WorkflowPath+".bak"));
        string unrelated=Unrelated(loadedBefore);

        scope.Main=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};scope.Main.Show();
        var main=scope.Main;
        await Until(()=>Named<SportSelectionScreen>(main,"SportSelectionPanel").IsEffectivelyVisible,"Actual initial Home must own input.");
        var existing=Named<CricketControlPanel>(main,"CricketControlPanelView");
        bool initialCricket=existing.BreakPresentationRotationEnabled;
        var model=Read<MatchManager>(main,"_match");Assert.False(model.ClockRunning);
        string state=JsonSerializer.Serialize(model.ToState());
        Assert.Null(Read<CricketMatchManager?>(main,"_cricketMatch"));
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.GetVisualDescendants().OfType<Button>().Any(button=>button.Name=="SettingsLaunchAction"&&button.IsEffectivelyVisible),"Real Home settings action must be mounted.");
        var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(), value=>value.Name=="SettingsLaunchAction");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(launch)).Invoke();
        var preferences=Named<GlobalSettingsScreen>(main,"GlobalSettingsPanel");
        await Until(()=>preferences.IsEffectivelyVisible,"Native Home Settings Invoke must open current global settings.");
        var afl=Named<RadioButton>(preferences,"AflSettingsTab");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(afl)).Select();
        var toggle=Named<CheckBox>(preferences,"BreakScreenToggle");
        await Until(()=>afl.IsChecked==true&&toggle.IsEffectivelyVisible,"Actual AFL scope must expose its original rotation control.");
        Assert.True(toggle.IsEffectivelyEnabled);Assert.Equal(legacyAfl,toggle.IsChecked==true);
        toggle.BringIntoView();await Pump(100);
        int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).Toggle();
        // Synchronous authoritative AFL state, before await/fresh panel creation.
        Assert.Equal(!legacyAfl,toggle.IsChecked==true);Assert.Equal(1,changes);
        Assert.Equal(!legacyAfl,Named<CheckBox>(main,"SettingsBreakScreenToggle").IsChecked==true);
        Assert.Equal(!legacyAfl,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
        bool afterAflEditCricket=existing.BreakPresentationRotationEnabled;
        Assert.Equal(state,JsonSerializer.Serialize(model.ToState()));
        Assert.Null(Read<CricketMatchManager?>(main,"_cricketMatch"));
        scope.AssertOwnedPaths();
        var loadedAfter=OperatorWorkflowPreferences.Load();
        bool? savedCricket=CricketChoice(loadedAfter);
        bool rawHasCricket;bool? rawCricket;
        using(var file=JsonDocument.Parse(File.ReadAllText(scope.WorkflowPath)))
        {rawHasCricket=file.RootElement.TryGetProperty("cricketBreakPresentationRotation",out var value);rawCricket=rawHasCricket&&value.ValueKind is JsonValueKind.True or JsonValueKind.False?value.GetBoolean():null;}
        scope.FreshPanel=new CricketControlPanel();
        scope.FreshWindow=new Window{Width=900,Height=650,Content=scope.FreshPanel};scope.FreshWindow.Show();
        await Until(()=>scope.FreshPanel.IsLoaded,"Fresh public panel constructor/Loaded must read saved presentation preferences.");
        bool freshCricket=scope.FreshPanel.BreakPresentationRotationEnabled;
        Assert.Equal(state,JsonSerializer.Serialize(model.ToState()));
        string afterUnrelated=Unrelated(loadedAfter);

        // Retain ALL real phases even if current-source initialization or
        // cross-mode assignment fails the desired isolation contract below.
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,$"rotation-isolation-afl-{legacyAfl}-cricket-{explicitCricket?.ToString()??"legacy"}.json"),
                JsonSerializer.Serialize(new{scope="Linux headless actual public Home/settings scope/Toggle providers; idle existing/fresh panels, not native desktop or match-save/reopen",legacyAfl,explicitCricket,expectedCricket,normalizedCricket,initialCricket,afterAflEditCricket,savedCricket,rawHasCricket,rawCricket,freshCricket,changes},new JsonSerializerOptions{WriteIndented=true}));
        }
        Assert.Equal(expectedCricket,initialCricket);
        Assert.Equal(expectedCricket,afterAflEditCricket);
        Assert.Equal(expectedCricket,freshCricket);
        Assert.Equal(expectedCricket,normalizedCricket);
        Assert.Equal(expectedCricket,savedCricket);Assert.True(rawHasCricket);Assert.Equal(expectedCricket,rawCricket);
        Assert.Equal(unrelated,afterUnrelated);
    }
}
