using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [Trait("Evidence", "MediaSpeedLifecycle")]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task MediaSpeedStartsWithNormalAndKeyboardChoiceDoesNotScore(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        await OpenOriginalMediaTransport(f);
        var speed=Named<ComboBox>(f.Window,"VideoTabSpeedCombo");
        var player=Mr6Read<VideoPlayerControl>(f,"_videoPlayer");
        string model=ManualWeatherModel(f); bool output=PresWindowOpen(f);
        Assert.Equal(3,speed.SelectedIndex);
        Assert.Equal("Normal",Assert.IsType<ComboBoxItem>(speed.SelectedItem).Content);
        Assert.Equal(1,player.SpeedRatio);
        await RevealOriginalMediaTarget(f,speed);
        Assert.True(speed.Focus(NavigationMethod.Tab));
        // Closed ComboBox navigation uses arrows. End is a slider endpoint
        // convention and did not navigate this native ComboBox; that first
        // test expectation was not evidence of an application keyboard bug.
        Key(f.Window,PhysicalKey.ArrowDown); await Settle(80);
        Assert.Equal(4,speed.SelectedIndex); Assert.Equal(1.25,player.SpeedRatio);
        Key(f.Window,PhysicalKey.ArrowUp); await Settle(80);
        Assert.Equal(3,speed.SelectedIndex); Assert.Equal(1,player.SpeedRatio);
        Capture(f.Window,$"media-speed-keyboard-{width}");
        Assert.Equal(model,ManualWeatherModel(f)); Assert.Equal(output,PresWindowOpen(f));
        Assert.False(player.HasSource); Assert.False(player.IsPlaying);
    }

    [AvaloniaTheory]
    [Trait("Evidence", "MediaSpeedLifecycle")]
    [InlineData(1280,850)] [InlineData(420,320)]
    public async Task MediaSpeedChoiceSurvivesOutputRecreationWithoutStartingPlayback(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        await OpenOriginalMediaTransport(f);
        var speed=Named<ComboBox>(f.Window,"VideoTabSpeedCombo");
        // Public selection property drives the original SelectionChanged path.
        // Reopening below uses the production public command route, not a
        // private lifecycle handler or a replacement-player fixture assignment.
        speed.SelectedIndex=6; await Settle(80);
        var original=Mr6Read<VideoPlayerControl>(f,"_videoPlayer");
        Assert.Equal(1.75,original.SpeedRatio);
        string model=ManualWeatherModel(f);
        Mr6Read<NativeWindow>(f,"_displayWindow").Close(); await Settle(80);
        Assert.False(PresWindowOpen(f));
        Assert.True(f.Window.OpenCommandBar());
        Assert.True(f.Window.ExecuteCommand("enable-output")); await Settle(180);
        var replacement=Mr6Read<VideoPlayerControl>(f,"_videoPlayer");
        Assert.NotSame(original,replacement);
        Assert.Equal(6,speed.SelectedIndex);
        Assert.Equal(1.75,replacement.SpeedRatio);
        Assert.True(PresWindowOpen(f));
        Assert.False(replacement.HasSource); Assert.False(replacement.IsPlaying);
        Assert.Equal(model,ManualWeatherModel(f));
        speed.SelectedIndex=3; await Settle(80);
        Assert.Equal(1,replacement.SpeedRatio); Assert.Equal(1.75,original.SpeedRatio);
    }
}
