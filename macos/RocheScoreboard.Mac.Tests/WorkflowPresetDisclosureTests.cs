using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","WorkflowPresetDisclosure")]
    public async Task OnlySelectedSavedClubOffersDeleteAndOptionalVideoStaysOutOfTheMainDraft(bool cricket)
    {
        await using var f=await KbRoot.Create();
        if(cricket) { await PresetStorage.SaveAllCricketAsync(KbPresetDirectory.Teams()); await PresetStorage.ReadAsync(KbCricket); }
        var wizard=await PendingActionWizard(f,cricket);
        var side=Named<Control>(wizard,"HomeTeamCard");
        var delete=Assert.Single(side.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Delete preset"));Assert.False(delete.IsEffectivelyVisible);
        var preset=Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>().First();await KbClick(f.Main,preset);
        Assert.True(delete.IsEffectivelyVisible);var save=Assert.Single(side.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Save as preset"));Assert.True(save.IsEffectivelyEnabled);
        if(!cricket)
        {
            var video=Assert.Single(side.GetVisualDescendants().OfType<Expander>(),x=>Equals(x.Tag,"team-goal-video:Home"));Assert.False(video.IsExpanded);
            await KbClick(f.Main,video);Assert.True(video.IsExpanded);
        }
    }
}
