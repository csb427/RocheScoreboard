using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ClubPresetPersistenceDraftTests
{
    [AvaloniaTheory]
    [InlineData(1080,760)] [InlineData(420,320)]
    [Trait("Evidence","ClubCrestReadiness")]
    public async Task UnreadableCachedCrestNeverBecomesReadyAndReopeningAfterRepairRecovers(int width,int height)
    {
        using var fixture=new StoreFixture();
        var team=TeamCatalogService.GetTeams().First(t=>string.IsNullOrWhiteSpace(t.BundledLogo));
        await fixture.PrepareOfflineAflLogo(team);
        string path=await TeamLogoService.GetTransparentLogoAsync(team);byte[] good=File.ReadAllBytes(path);
        await File.WriteAllBytesAsync(path,new byte[256]);Assert.Equal(path,await TeamLogoService.GetTransparentLogoAsync(team));
        string presets=Snapshot(PresetStorage.LoadAll());
        TeamLibraryWindow Open()
        {
            var library=fixture.Track(new TeamLibraryWindow("Home"){Width=width,Height=height});library.Show();
            Named<ComboBox>(library,"LeagueFilter").SelectedIndex=0;Named<ComboBox>(library,"StateFilter").SelectedIndex=0;
            Named<CheckBox>(library,"FavouriteOnly").IsChecked=false;
            var list=Named<ListBox>(library,"TeamList");list.SelectedItem=Assert.Single(list.Items.OfType<TeamCatalogEntry>(),t=>t.Id==team.Id);
            return library;
        }
        async Task Capture(TeamLibraryWindow library,string phase)
        {
            var status=Named<TextBlock>(library,"StatusText");status.BringIntoView();await Settle(150);
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
            Directory.CreateDirectory(directory);using var frame=library.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"crest-{width}-{phase}.png"));
        }
        var broken=Open();await Until(()=>!Named<TextBlock>(broken,"StatusText").Text!.StartsWith("Preparing"),"Current logo request must complete rather than a timing miss.");
        await Capture(broken,"unreadable");
        Assert.Null(Named<Image>(broken,"LogoPreview").Source);
        Assert.False(Named<Button>(broken,"UseButton").IsEnabled);Assert.False(Named<Button>(broken,"FavouriteButton").IsEnabled);
        Assert.True(Named<TextBlock>(broken,"LogoPlaceholder").IsVisible);Assert.Contains("unavailable",Named<TextBlock>(broken,"LogoPlaceholder").Text,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ready",Named<TextBlock>(broken,"StatusText").Text);Assert.Null(broken.Selection);Assert.Equal(presets,Snapshot(PresetStorage.LoadAll()));
        broken.Close();await File.WriteAllBytesAsync(path,good);
        var recovered=Open();await Until(()=>Named<Button>(recovered,"UseButton").IsEnabled,"Reopened current club must recover with repaired actual cached PNG.");
        await Capture(recovered,"recovered");
        Assert.NotNull(Named<Image>(recovered,"LogoPreview").Source);Assert.False(Named<TextBlock>(recovered,"LogoPlaceholder").IsVisible);
        Assert.Equal("Ready to select",Named<TextBlock>(recovered,"StatusText").Text);Assert.Null(recovered.Selection);
        Assert.Equal(presets,Snapshot(PresetStorage.LoadAll()));
    }
}
