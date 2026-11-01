using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    [Trait("Evidence","ClubBadgeCalm")]
    public async Task FreshCalmClubBadgeUsesRestrainedTravelWithoutChangingImmediateSelection(bool cricket)
    {
        await using var f=await KbRoot.Create();var(wizard,tile)=await OpenClubBadge(f,cricket);using var preferences=new ClubBadgePreferences();
        OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);await Pump(100);
        var saved=f.Files.Bytes();var recovery=f.Recovery();var point=await KbPoint(f.Main,tile);
        var(check,_,pop)=ClubBadgeParts(tile);double minimum=1,maximum=1;int samples=0;bool witnessedMoving=false;
        EventHandler observe=(_,_)=>{if(ClubBadgeMoving(tile))witnessedMoving=true;};
        EventHandler<AvaloniaPropertyChangedEventArgs> observed=(_,args)=>
        {
            if(args.Property!=ScaleTransform.ScaleXProperty)return;
            minimum=Math.Min(minimum,pop.ScaleX);maximum=Math.Max(maximum,pop.ScaleX);samples++;observe(null,EventArgs.Empty);
        };
        pop.PropertyChanged+=observed;NativeRendering.Rendering+=observe;
        try
        {
            f.Main.MouseMove(point);f.Main.MouseDown(point,MouseButton.Left);f.Main.MouseUp(point,MouseButton.Left);
            Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);
            Assert.Equal("Selected",AutomationProperties.GetItemStatus(tile));
            await Until(()=>witnessedMoving,"Require a recorded real partial check/pop clock witness, not an endpoint or private sample.");
            await Pump(450);ClubBadgeSettled(tile);Assert.True(samples>1);
            Assert.InRange(minimum,.81,.999);Assert.InRange(maximum,1,1.04);
            Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);f.AssertRecovery(recovery);f.Files.AssertBytes(saved);
        }
        finally
        {
            pop.PropertyChanged-=observed;NativeRendering.Rendering-=observe;
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
            {Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"calm-travel-"+cricket+".json"),JsonSerializer.Serialize(new{minimum,maximum,samples,witnessedMoving,cricket},new JsonSerializerOptions{WriteIndented=true}));}
        }
    }
}
