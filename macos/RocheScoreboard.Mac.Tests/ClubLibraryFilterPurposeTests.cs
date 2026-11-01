using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ClubPresetPersistenceDraftTests
{
    [AvaloniaTheory]
    [InlineData(false,1080,760)] [InlineData(true,1080,760)]
    [InlineData(false,420,320)] [InlineData(true,420,320)]
    [Trait("Evidence","ClubLibraryFilterPurpose")]
    public async Task ClubFiltersExposeVisiblePurposeAndKeepNativeKeyboardFiltering(bool cricket,int width,int height)
    {
        using var fixture=new StoreFixture();
        string preferences=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        var window=fixture.Track(new TeamLibraryWindow("Home",cricket?TeamLibrarySport.Cricket:TeamLibrarySport.AustralianFootball){Width=width,Height=height});
        window.Show();await Settle(350);
        void Capture(string phase)
        {
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
            Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
            frame.Save(Path.Combine(directory,$"club-filters-{cricket}-{width}-{phase}.png"));
        }
        Capture("initial");
        foreach(var (name,purpose) in new[]{("LeagueFilter","Competition"),("StateFilter","State"),("SortFilter","Sort clubs")})
        {
            var filter=Named<ComboBox>(window,name);
            var label=Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(filter));
            Assert.Equal(purpose,label.Text);Assert.True(label.IsEffectivelyVisible);
            Assert.Equal(purpose,ControlAutomationPeer.CreatePeerForElement(filter)!.GetName());
            filter.BringIntoView();await Settle();Assert.True(filter.Focus());
            window.KeyPressQwerty(PhysicalKey.F4,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.F4,RawInputModifiers.None);await Settle();Assert.True(filter.IsDropDownOpen);
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);await Settle();
            Assert.False(filter.IsDropDownOpen);Assert.True(window.IsVisible);
            var origin=filter.TranslatePoint(default,window);Assert.NotNull(origin);
            Assert.True(new Rect(window.ClientSize).Contains(new Rect(origin!.Value,filter.Bounds.Size)),"Current filter must remain reachable after popup dismissal.");
        }
        Named<ComboBox>(window,"StateFilter").SelectedIndex=0;
        Named<ComboBox>(window,"SortFilter").SelectedIndex=0;
        Named<CheckBox>(window,"FavouriteOnly").IsChecked=false;
        var league=Named<ComboBox>(window,"LeagueFilter");league.SelectedIndex=0;league.BringIntoView();await Settle();Assert.True(league.Focus());
        window.KeyPressQwerty(PhysicalKey.ArrowDown,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.ArrowDown,RawInputModifiers.None);await Settle();
        Assert.Equal(1,league.SelectedIndex);
        string chosen=Assert.IsType<ComboBoxItem>(league.SelectedItem).Content!.ToString()!;
        var rows=Named<ListBox>(window,"TeamList").Items.OfType<TeamCatalogEntry>().ToArray();Assert.NotEmpty(rows);Assert.All(rows,t=>Assert.Equal(chosen,t.League));
        var expected=TeamCatalogService.GetTeams(cricket?TeamLibrarySport.Cricket:TeamLibrarySport.AustralianFootball).Where(t=>t.League==chosen).Select(t=>t.Id).OrderBy(id=>id);
        Assert.Equal(expected,rows.Select(t=>t.Id).OrderBy(id=>id));
        Assert.Contains(rows.Length+" club",Named<TextBlock>(window,"ResultCount").Text);
        Assert.Null(window.Selection);Assert.Equal(preferences,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
        Capture("filtered");
    }
}
