// Source-grounded regression: authored meaning must remain searchable before template mounting.
// Four cases: two real Home/setup/settings query routes, two unhosted
// production-template metadata cases. The latter explicitly do NOT establish
// physical UI behavior or a never-mounted state for the real MainWindow.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
using SearchLogicalTree = RocheScoreboard.Mac.Native.LogicalTreeHelper;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static IEnumerable<AvaloniaObject> SearchLogicalNodes(AvaloniaObject root)
    {
        yield return root;
        foreach(var child in SearchLogicalTree.GetChildren(root).OfType<AvaloniaObject>())
            foreach(var descendant in SearchLogicalNodes(child))yield return descendant;
    }

    [AvaloniaTheory]
    [InlineData("finals")][InlineData("golden bar styling")]
    [Trait("Evidence","SettingsAuthoredSemanticSearch")]
    public async Task RealFreshSettingsSearchFindsTheOriginalTemplatedFinalsControlWithoutTogglingIt(string query)
    {
        await using var f=await AflFixture.Create(1280,850);
        var workspace=Assert.Single(f.Window.GetVisualDescendants().OfType<SettingsWorkspace>(),x=>x.IsEffectivelyVisible);
        var teams=Named<Border>(f.Window,"SettingsSection_Teams");
        var match=Named<Border>(f.Window,"SettingsSection_Match");
        var toggle=Named<CheckBox>(f.Window,"SettingsFinalsToggle");
        var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
        Assert.True(teams.IsVisible);Assert.True(match.IsVisible);
        Assert.Equal("MATCH",match.Tag);Assert.True(string.IsNullOrEmpty(search.Text));
        Assert.Null(toggle.Content);
        // The current 'teams' preference normalizes to MATCH, whose category
        // includes BOTH original Teams and Match sections. The real template
        // may already be mounted; no never-mounted assertion is made here.
        // Logical search must still find the authored control meaning.
        Assert.DoesNotContain(SearchLogicalNodes(match).OfType<TextBlock>(),
            text=>(text.Text??"").Contains(query,StringComparison.OrdinalIgnoreCase));
        string authored=(AutomationProperties.GetName(toggle)??"")+" "+(AutomationProperties.GetHelpText(toggle)??"");
        Assert.True(authored.Contains(query,StringComparison.OrdinalIgnoreCase));
        bool selected=toggle.IsChecked==true;int changes=0;toggle.IsCheckedChanged+=(_,_)=>changes++;
        string before=JsonSerializer.Serialize(f.Match.ToState());
        bool stoppage=OperatorWorkflowPreferences.Load().AflControlPanelCountdown;

        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);
        Assert.True(search.IsFocused);f.Window.KeyTextInput(query);await Settle(180);
        Assert.Equal(query,search.Text);Assert.True(search.IsFocused);
        Assert.True(match.IsVisible,"Authored visible/accessibility meaning of the real Finals control must be discoverable without opening/toggling it first.");
        Assert.False(teams.IsVisible);
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(0,changes);
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
        Assert.Equal(stoppage,OperatorWorkflowPreferences.Load().AflControlPanelCountdown);
        Capture(f.Window,"semantic-settings-"+query.Replace(' ','-'));

        Key(f.Window,PhysicalKey.Escape);await Settle(100);
        Assert.True(string.IsNullOrEmpty(search.Text));Assert.True(search.IsFocused);
        Assert.True(teams.IsVisible);Assert.True(match.IsVisible);
        Assert.Equal(selected,toggle.IsChecked==true);Assert.Equal(0,changes);
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
    }

    [AvaloniaTheory]
    [InlineData("Show stoppage clock in Match tab")]
    [InlineData("pauses the match clock")]
    [Trait("Evidence","SettingsUnmountedSemanticSearch")]
    public async Task UnmountedProductionTemplateUsesAuthoredMetadataWithoutBypassingScopeOrChangingState(string query)
    {
        await using var f=await AflFixture.Create(1280,850);
        var original=Named<CheckBox>(f.Window,"SettingsShowControlPanelCountdown");
        var theme=Assert.IsType<ControlTheme>(NativeResources.FindOrNull(f.Window,"ControlCentre.StoppageToggle"));
        var local=new CheckBox{Theme=theme,IsChecked=true};
        var excluded=new CheckBox{Theme=theme,IsChecked=false};
        foreach(var copy in new[]{local,excluded})
        {
            AutomationProperties.SetName(copy,AutomationProperties.GetName(original));
            AutomationProperties.SetHelpText(copy,AutomationProperties.GetHelpText(original));
        }
        var workspace=new SettingsWorkspace();
        var general=new Border{Tag="MATCH|scope:general",Child=local};
        var afl=new Border{Tag="MATCH|scope:afl",Child=excluded};
        var legacyText=new Border{Tag="SYSTEM|scope:general",Child=new TextBlock{Text="literal legacy probe"}};
        var legacyContent=new Border{Tag="SYSTEM|scope:general",Child=new Button{Content="caption legacy probe"}};
        var legacyTag=new Border{Tag="SYSTEM|scope:general|tag legacy probe",Child=new Border()};
        foreach(var section in new[]{general,afl,legacyText,legacyContent,legacyTag})workspace.Sections.Add(section);
        workspace.SelectScope("general");
        // This deliberately unhosted component contract uses the actual
        // OperatorSearchBox.TextChanged route, not private Matches/Filter or
        // fake automation callbacks. No Show/ApplyTemplate/layout occurs.
        var search=Assert.Single(SearchLogicalNodes(workspace).OfType<OperatorSearchBox>());
        Assert.False(local.IsLoaded);Assert.False(excluded.IsLoaded);
        Assert.Empty(local.GetVisualChildren());Assert.Empty(excluded.GetVisualChildren());
        Assert.Null(local.Content);Assert.Null(excluded.Content);
        int localChanges=0,excludedChanges=0;local.IsCheckedChanged+=(_,_)=>localChanges++;
        excluded.IsCheckedChanged+=(_,_)=>excludedChanges++;
        string before=JsonSerializer.Serialize(f.Match.ToState());

        search.Text=query;await Settle(60);
        Assert.True(general.IsVisible,"Authored Name/HelpText must be usable before a production control template is inflated.");
        Assert.False(afl.IsVisible,"Identical authored metadata may not reveal an excluded sport scope.");
        Assert.False(legacyText.IsVisible);Assert.False(legacyContent.IsVisible);Assert.False(legacyTag.IsVisible);
        Assert.False(local.IsLoaded);Assert.Empty(local.GetVisualChildren());
        Assert.True(local.IsChecked);Assert.False(excluded.IsChecked);
        Assert.Equal(0,localChanges);Assert.Equal(0,excludedChanges);

        foreach(var (legacy,phrase) in new[]{(legacyText,"literal legacy probe"),(legacyContent,"caption legacy probe"),(legacyTag,"tag legacy probe")})
        {
            search.Text=phrase;await Settle(40);Assert.True(legacy.IsVisible);
            Assert.False(general.IsVisible);Assert.False(afl.IsVisible);
        }
        search.Text="semantic-no-match-92731";await Settle(40);
        Assert.All(workspace.Sections.OfType<Control>(),section=>Assert.False(section.IsVisible));
        search.Text="";await Settle(40);
        Assert.True(general.IsVisible);Assert.False(afl.IsVisible);
        Assert.True(legacyText.IsVisible);Assert.True(legacyContent.IsVisible);Assert.True(legacyTag.IsVisible);
        Assert.True(local.IsChecked);Assert.False(excluded.IsChecked);
        Assert.Equal(0,localChanges);Assert.Equal(0,excludedChanges);
        Assert.Equal(before,JsonSerializer.Serialize(f.Match.ToState()));
    }
}
