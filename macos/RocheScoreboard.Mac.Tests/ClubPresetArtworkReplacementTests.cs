using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaFact]
    [Trait("Evidence","ClubBadgeArtworkReplacement")]
    public async Task SameTileArtworkReplacementRetiresDetachedClockOwnersAndKeepsCurrentSelection()
    {
        SelfTestStorage.Initialize(true);Directory.CreateDirectory(Path.GetDirectoryName(OperatorAppearance.StoragePath)!);using var preferences=new ClubBadgePreferences();var teams=KbPresetDirectory.Teams();var tile=new Border();
        var host=new Grid();host.Children.Add(tile);OperatorClubPresetDesign.Update(tile,teams[0],false);
        var window=new NativeWindow{Width=420,Height=320,Content=new UserControl{Content=host}};bool witnessed=false;
        EventHandler observer=(_,_)=>{if(ClubBadgeMoving(tile))witnessed=true;};
        try
        {
            window.Show();window.Activate();await Pump(70);NativeRendering.Rendering+=observer;
            OperatorClubPresetDesign.Update(tile,teams[0],true);
            await Until(()=>witnessed,"Require naturally partial original check/pop clocks before replacement; no fabricated sample.");
            Assert.True(ClubBadgeMoving(tile),"Replacement must still occur during the witnessed live phase.");var old=ClubBadgeParts(tile);
            OperatorClubPresetDesign.Update(tile,teams[1],true);var current=ClubBadgeParts(tile);Assert.NotSame(old.Check,current.Check);Assert.NotSame(old.Pop,current.Pop);
            bool oldCheckActive=Motion.IsAnimating(old.Check),oldPopActive=Motion.IsAnimating(old.Pop);
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
            {Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"replacement.json"),JsonSerializer.Serialize(new{witnessed,oldCheckActive,oldPopActive,oldDash=old.Check.StrokeDashOffset,oldScale=old.Pop.ScaleX},new JsonSerializerOptions{WriteIndented=true}));}
            Assert.False(oldCheckActive);Assert.False(oldPopActive);Assert.Equal(0,old.Check.StrokeDashOffset);Assert.Equal(1,old.Pop.ScaleX);Assert.Equal(1,old.Pop.ScaleY);
            OperatorAppearance.Configure(true,true);ClubBadgeSettled(tile);await Pump(400);ClubBadgeSettled(tile);
        }
        finally{NativeRendering.Rendering-=observer;window.Close();}
    }
}
