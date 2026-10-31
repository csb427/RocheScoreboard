// Actual operator-row and silent-refresh coverage; native desktop acceptance remains separate.
// Four cases. Make the COPIED repository baseline test class partial, or insert
// these methods into it; keep /tmp baseline SHA194bdc... unchanged.
// Reuses its private safe StorageScope, Named/Read/Pump/Until/Unrelated helpers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static bool RotationWithin(Control root,object? value)=>ReferenceEquals(root,value)
        ||value is Visual visual&&visual.GetVisualAncestors().Contains(root);
    private static Rect RotationBox(Window owner,Control target)
    {
        var p=target.TranslatePoint(default,owner);Assert.NotNull(p);
        return new Rect(p.Value,target.Bounds.Size);
    }
    private static Point[] RotationProbes(Rect r)=>new[]{r.Center,new Point(r.Left+4,r.Center.Y),new Point(r.Right-4,r.Center.Y),new Point(r.Center.X,r.Top+4),new Point(r.Center.X,r.Bottom-4)};
    private static void RotationKey(Window owner,PhysicalKey key,RawInputModifiers modifiers=RawInputModifiers.None)
    {owner.KeyPressQwerty(key,modifiers);owner.KeyReleaseQwerty(key,RawInputModifiers.None);}
    private static void RotationTap(Window owner,Control target)
    {
        Assert.True(target.IsLoaded);Assert.Same(owner,TopLevel.GetTopLevel(target));
        Assert.True(target.IsEffectivelyVisible);Assert.True(target.IsEffectivelyEnabled);
        var box=RotationBox(owner,target);Assert.True(new Rect(owner.ClientSize).Contains(box));
        Assert.All(RotationProbes(box),p=>Assert.True(RotationWithin(target,owner.InputHitTest(p))));
        owner.MouseMove(box.Center);owner.MouseDown(box.Center,MouseButton.Left);owner.MouseUp(box.Center,MouseButton.Left);
    }
    private static async Task<(GlobalSettingsScreen Preferences,SettingsWorkspace Workspace)> RotationOpen(StorageScope scope,int width,int height)
    {
        scope.AssertOwnedPaths();scope.Main=new Roche_Scoreboard.MainWindow();var main=scope.Main;main.Show();
        main.Width=width;main.Height=height;
        await Until(()=>Named<SportSelectionScreen>(main,"SportSelectionPanel").IsEffectivelyVisible,"Actual Home must be visible before opening global preferences.");
        var home=Named<SportSelectionScreen>(main,"SportSelectionPanel");
        await Until(()=>home.GetVisualDescendants().OfType<Button>().Any(value=>value.Name=="SettingsLaunchAction"&&value.IsEffectivelyVisible),"Actual dynamic Home settings action must be mounted.");
        var launch=Assert.Single(home.GetVisualDescendants().OfType<Button>(), value=>value.Name=="SettingsLaunchAction");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(launch)).Invoke();
        var preferences=Named<GlobalSettingsScreen>(main,"GlobalSettingsPanel");
        await Until(()=>preferences.IsLoaded&&preferences.IsEffectivelyVisible&&ReferenceEquals(TopLevel.GetTopLevel(preferences),main),"Actual Home Invoke must attach the global settings view.");
        var cricket=Named<RadioButton>(preferences,"CricketSettingsScopeTab");
        Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(cricket)).Select();
        await Until(()=>cricket.IsChecked==true,"Actual Cricket scope must select.");
        Assert.False(Named<CheckBox>(preferences,"BreakScreenToggle").IsEffectivelyVisible);
        var workspace=Assert.Single(preferences.GetVisualDescendants().OfType<SettingsWorkspace>());
        await Pump(520); // New action after the existing navigation-pointer guard.
        return(preferences,workspace);
    }
    private static async Task RotationReveal(Window owner,SettingsWorkspace workspace,Control target)
    {
        Assert.True(target.IsLoaded);Assert.Same(owner,TopLevel.GetTopLevel(target));
        Assert.True(target.IsEffectivelyVisible);Assert.True(target.IsEffectivelyEnabled);
        Assert.True(target.Focus(NavigationMethod.Tab));target.BringIntoView();await Pump(100);
        bool Ready(){var r=RotationBox(owner,target);return r.Width>16&&r.Height>16&&new Rect(owner.ClientSize).Contains(r)&&RotationProbes(r).All(p=>RotationWithin(target,owner.InputHitTest(p)));}
        for(int i=0;i<45&&!Ready();i++)
        {
            var scroller=workspace.Scroller;Assert.True(scroller.IsEffectivelyVisible);
            var wheel=scroller.TranslatePoint(new Point(3,scroller.Bounds.Height/2),owner);Assert.NotNull(wheel);
            Assert.True(RotationWithin(scroller,owner.InputHitTest(wheel.Value)),"Physical wheel must reach the actual settings scroller.");
            var r=RotationBox(owner,target);bool top=RotationWithin(target,owner.InputHitTest(RotationProbes(r)[3])),bottom=RotationWithin(target,owner.InputHitTest(RotationProbes(r)[4]));
            double direction=!top&&bottom?1:top&&!bottom?-1:r.Center.Y<wheel.Value.Y?1:-1;
            owner.MouseWheel(wheel.Value,new Vector(0,direction),RawInputModifiers.None);await Pump(65);
        }
        Assert.True(Ready(),"New cricket preference must own five real hit probes after bounded physical scrolling.");
        var presenter=Assert.Single(workspace.Scroller.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>(), value=>value.IsEffectivelyVisible);
        var row=Assert.IsType<Grid>(target.Parent);
        Assert.True(RotationBox(owner,presenter).Contains(RotationBox(owner,row)),"Focusing the original switch must reveal its complete paired label row, not leave the title above the visible viewport.");
        foreach(var text in row.GetVisualDescendants().OfType<TextBlock>())
        {
            Assert.True(RotationBox(owner,presenter).Contains(RotationBox(owner,text)),"The full paired label must remain inside the actual scroll presenter.");
            Assert.True(text.TextLayout.Width<=text.Bounds.Width+.01);
            Assert.True(text.TextLayout.Height<=text.Bounds.Height+.01);
            Assert.DoesNotContain(text.TextLayout.TextLines,line=>line.HasCollapsed);
        }
    }
    private sealed record RotationFile(byte[]? Bytes,DateTime? LastWriteUtc);
    private static Dictionary<string,RotationFile> RotationFiles(StorageScope scope)
    {
        scope.AssertOwnedPaths();var result=new Dictionary<string,RotationFile>(StringComparer.Ordinal);
        foreach(string path in new[]{scope.WorkflowPath,scope.WorkflowPath+".bak",scope.WorkflowPath+".tmp"})
            result[path]=File.Exists(path)?new(File.ReadAllBytes(path),File.GetLastWriteTimeUtc(path)):new(null,null);
        return result;
    }
    private static void RotationFilesUnchanged(StorageScope scope,Dictionary<string,RotationFile> before)
    {
        var after=RotationFiles(scope);foreach(var (path,expected) in before)
        {Assert.Equal(expected.Bytes,after[path].Bytes);Assert.Equal(expected.LastWriteUtc,after[path].LastWriteUtc);}
    }

    [AvaloniaTheory][InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketBreakRotationOriginalOperatorRow")]
    public async Task CricketBreakRotationSearchAndOriginalSpaceProviderPointerKeepAflAndMatchStateIndependent(int width,int height)
    {
        await using var scope=new StorageScope(false,true);
        var (preferences,workspace)=await RotationOpen(scope,width,height);var main=scope.Main!;
        var model=Read<MatchManager>(main,"_match");Assert.False(model.ClockRunning);
        string state=JsonSerializer.Serialize(model.ToState());string unrelated=Unrelated(OperatorWorkflowPreferences.Load());
        var existing=Named<CricketControlPanel>(main,"CricketControlPanelView");
        var toggle=Named<CheckBox>(preferences,"CricketBreakRotationDefaultToggle");
        Assert.Contains("cricket",(AutomationProperties.GetName(toggle)??"").ToLowerInvariant());
        Assert.Contains("break",(AutomationProperties.GetHelpText(toggle)??"").ToLowerInvariant());
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
        // Real search-field pointer/typing route. No claim that Ctrl+F from an
        // external scope-header control is handled by the inner workspace.
        RotationTap(main,search);Assert.True(search.IsFocused);
        RotationKey(main,PhysicalKey.A,RawInputModifiers.Control);main.KeyTextInput("break rotation");await Pump(150);
        Assert.Equal("break rotation",search.Text);Assert.True(toggle.IsEffectivelyVisible);
        Assert.False(Named<CheckBox>(preferences,"BreakScreenToggle").IsEffectivelyVisible);
        await RotationReveal(main,workspace,toggle);
        var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle));
        Assert.True(toggle.IsChecked);Assert.True(existing.BreakPresentationRotationEnabled);
        int changes=0,events=0;toggle.IsCheckedChanged+=(_,_)=>changes++;preferences.CricketOutputPreferencesChanged+=()=>events++;
        void Authority(bool value,int expectedChanges)
        {
            Assert.Equal(value,toggle.IsChecked==true);Assert.Equal(expectedChanges,changes);Assert.Equal(expectedChanges,events);
            Assert.Equal(value?ToggleState.On:ToggleState.Off,provider.ToggleState);
            Assert.Equal(value,OperatorWorkflowPreferences.Load().CricketBreakPresentationRotation);
            Assert.Equal(value,existing.BreakPresentationRotationEnabled);
            Assert.False(OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
            Assert.False(Named<CheckBox>(main,"SettingsBreakScreenToggle").IsChecked);
            Assert.Equal(unrelated,Unrelated(OperatorWorkflowPreferences.Load()));
            Assert.Equal(state,JsonSerializer.Serialize(model.ToState()));Assert.False(model.ClockRunning);
        }
        RotationKey(main,PhysicalKey.Space);Authority(false,1);
        provider.Toggle();Authority(true,2);
        RotationTap(main,toggle);Authority(false,3);
        provider.Toggle();Authority(true,4);
        await Pump(500);Authority(true,4);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);using var frame=main.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"cricket-break-row-{width}-{height}.png"));
            var row=Assert.IsType<Grid>(toggle.Parent);
            File.WriteAllText(Path.Combine(directory,$"cricket-break-row-layout-{width}-{height}.json"),JsonSerializer.Serialize(new
            {
                width,height,Input=RotationBox(main,toggle),Row=RotationBox(main,row),Scroller=RotationBox(main,workspace.Scroller),workspace.Scroller.Viewport,workspace.Scroller.Offset,
                Text=row.GetVisualDescendants().OfType<TextBlock>().Select(text=>new{text.Text,Bounds=RotationBox(main,text),text.TextLayout.Width,text.TextLayout.Height,Lines=text.TextLayout.TextLines.Select(line=>new{line.Width,line.HasCollapsed})}),
                Presenters=workspace.Scroller.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().Select(p=>new{p.Name,p.IsEffectivelyVisible,Bounds=RotationBox(main,p)})
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        scope.FreshPanel=new CricketControlPanel();scope.FreshWindow=new Window{Width=900,Height=650,Content=scope.FreshPanel};scope.FreshWindow.Show();
        await Until(()=>scope.FreshPanel.IsLoaded,"Fresh original panel must mount from the saved independent preference.");
        Assert.True(scope.FreshPanel.BreakPresentationRotationEnabled);Authority(true,4);
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","CricketBreakRotationMigrationAndSilentRefresh")]
    public async Task LegacyDefaultAndVisibleConstructorLoadedRefreshDoNotWriteOrEmitPreferenceEvents(bool legacyChoice)
    {
        await using var scope=new StorageScope(legacyChoice,null);
        var (preferences,_)=await RotationOpen(scope,1280,850);var main=scope.Main!;
        var toggle=Named<CheckBox>(preferences,"CricketBreakRotationDefaultToggle");
        var existing=Named<CricketControlPanel>(main,"CricketControlPanelView");
        Assert.Equal(legacyChoice,toggle.IsChecked==true);Assert.Equal(legacyChoice,existing.BreakPresentationRotationEnabled);
        Assert.Equal(legacyChoice,OperatorWorkflowPreferences.Load().CricketBreakPresentationRotation);
        string state=JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState());
        string unrelated=Unrelated(OperatorWorkflowPreferences.Load());int events=0,changes=0;
        preferences.CricketOutputPreferencesChanged+=()=>events++;toggle.IsCheckedChanged+=(_,_)=>changes++;
        // Fixture-only external store update makes refresh change a real UI
        // value. It is not described as another operator-edit route.
        OperatorWorkflowPreferences.Update(p=>p.CricketBreakPresentationRotation=!legacyChoice);
        var beforeRefresh=RotationFiles(scope);
        preferences.RefreshCricketPreferences();
        Assert.Equal(!legacyChoice,toggle.IsChecked==true);Assert.Equal(1,changes);Assert.Equal(0,events);
        RotationFilesUnchanged(scope,beforeRefresh);
        preferences.RefreshCricketPreferences();Assert.Equal(1,changes);Assert.Equal(0,events);RotationFilesUnchanged(scope,beforeRefresh);
        // Silent refresh updates the view only; runtime changes remain owned
        // by the explicit global event. No current match is started/mutated.
        Assert.Equal(legacyChoice,existing.BreakPresentationRotationEnabled);
        var fresh=new GlobalSettingsScreen();
        var freshToggle=Assert.IsType<CheckBox>(NativeLookup.Find(fresh,"CricketBreakRotationDefaultToggle"));
        Assert.Equal(!legacyChoice,freshToggle.IsChecked==true);RotationFilesUnchanged(scope,beforeRefresh);
        int freshEvents=0,freshChanges=0;fresh.CricketOutputPreferencesChanged+=()=>freshEvents++;
        freshToggle.IsCheckedChanged+=(_,_)=>freshChanges++;
        // A second fixture store change before Show independently exercises
        // the real Loaded refresh's suppression, rather than a same-value no-op.
        OperatorWorkflowPreferences.Update(p=>p.CricketBreakPresentationRotation=legacyChoice);
        var beforeLoaded=RotationFiles(scope);
        scope.FreshWindow=new Window{Width=900,Height=650,Content=fresh};scope.FreshWindow.Show();
        await Until(()=>fresh.IsLoaded,"Fresh original preferences view must actually receive Loaded.");await Pump(80);
        Assert.Equal(legacyChoice,freshToggle.IsChecked==true);Assert.Equal(1,freshChanges);Assert.Equal(0,freshEvents);
        RotationFilesUnchanged(scope,beforeLoaded);
        Assert.Equal(unrelated,Unrelated(OperatorWorkflowPreferences.Load()));
        Assert.Equal(legacyChoice,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
        Assert.Equal(legacyChoice,existing.BreakPresentationRotationEnabled);
        Assert.Equal(state,JsonSerializer.Serialize(Read<MatchManager>(main,"_match").ToState()));
    }
}
