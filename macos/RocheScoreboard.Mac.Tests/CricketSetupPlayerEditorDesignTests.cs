using Avalonia.Automation.Peers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketWizardInterruptionTests
{
    private static void PlayerEditorFrame(Fixture f,string name)
    {
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);using var frame=f.Window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData(true,false,1280,850)][InlineData(false,true,420,320)]
    [Trait("Evidence","CricketSetupPlayerEditor")]
    public async Task PlayerEditorNamesFieldsAndExplainsItsNameRequirement(bool home,bool edit,int width,int height)
    {
        using var f=new Fixture(width,height);await Settle(350);
        if(edit)f.Players(home).Add(new CricketPlayer{FirstName="Existing",LastName="Player",IsCaptain=true});
        await f.OpenPlayer(home,edit);await Settle(200);PlayerEditorFrame(f,$"player-editor-{home}-{edit}-{width}");
        var first=f.Tagged<TextBox>("new-player-first");var last=f.Tagged<TextBox>("new-player-last");
        Assert.Equal("First name",ControlAutomationPeer.CreatePeerForElement(first)!.GetName());Assert.Equal("Last name",ControlAutomationPeer.CreatePeerForElement(last)!.GetName());
        Assert.Equal(edit?"Ready to save this player.":"Enter a first name or last name to enable Add player.",f.Tagged<TextBlock>("new-player-validation").Text);Assert.Equal(edit,f.Tagged<Button>("apply").IsEnabled);
        Assert.Equal(edit?"Existing":"",first.Text);Assert.Equal(edit?"Player":"",last.Text);Assert.Equal(edit,f.Tagged<CheckBox>("new-player-captain").IsChecked);
        f.Escape();await Settle(250);Assert.Equal(edit?1:0,f.Players(home).Count);Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSetupPlayerEditor")]
    public async Task DisabledApplyCallbackCannotInsertAnUnnamedPlayer()
    {
        using var f=new Fixture();await Settle(350);await f.OpenPlayer(true);var apply=f.Tagged<Button>("apply");Assert.False(apply.IsEnabled);
        Click(apply);await Settle(400);Assert.Empty(f.Players(true));Assert.Empty(f.Players(false));
        Assert.Single(f.Wizard.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
        f.Escape();await Settle(250);Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);
    }
    [AvaloniaTheory]
    [InlineData(true,false)][InlineData(false,true)]
    [Trait("Evidence","CricketSetupPlayerEditor")]
    public async Task PlayerEditorSavesTrimmedNamesAndSelectedRolesOnlyAfterAcceptance(bool home,bool edit)
    {
        using var f=new Fixture(edit?1280:420,edit?850:320);await Settle(350);
        var previousCaptain=new CricketPlayer{FirstName="Previous",LastName="Captain",IsCaptain=true};f.Players(home).Add(previousCaptain);
        var existing=new CricketPlayer{FirstName="Existing",LastName="Player"};if(edit)f.Players(home).Add(existing);
        if(edit)
        {
            f.Step(2);await Settle(150);var source=Assert.Single(f.Wizard.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"edit-away-player")&&ReferenceEquals(x.DataContext,existing));
            Assert.True(source.Focus(NavigationMethod.Tab));source.BringIntoView();Click(source);await Settle(150);
        }
        else await f.OpenPlayer(home);
        var first=f.Tagged<TextBox>("new-player-first");var last=f.Tagged<TextBox>("new-player-last");
        Assert.True(first.Focus(NavigationMethod.Tab));first.SelectAll();f.Window.KeyTextInput("  Ada  ");Assert.True(last.Focus(NavigationMethod.Tab));last.SelectAll();f.Window.KeyTextInput("  Lovelace  ");await Settle(100);
        foreach(string tag in new[]{"new-player-captain","new-player-keeper"})
        {
            var role=f.Tagged<CheckBox>(tag);Assert.True(role.Focus(NavigationMethod.Tab));f.Window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);f.Window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);Assert.True(role.IsChecked);
        }
        Assert.True(previousCaptain.IsCaptain);Assert.Equal(edit?2:1,f.Players(home).Count);if(edit)Assert.Equal("Existing",existing.FirstName);
        Assert.True(f.Tagged<Button>("apply").IsEnabled);Click(f.Tagged<Button>("apply"));await Settle(300);
        var saved=Assert.Single(f.Players(home),x=>x.FirstName=="Ada");Assert.Equal("Lovelace",saved.LastName);Assert.True(saved.IsCaptain);Assert.True(saved.IsWicketkeeper);Assert.False(previousCaptain.IsCaptain);
        if(edit)Assert.Same(existing,saved);Assert.Empty(f.Players(!home));Assert.Equal(0,f.Completions);Assert.Equal(0,f.Cancellations);Assert.Null(f.Wizard.Result);
    }
    [AvaloniaFact]
    [Trait("Evidence","CricketSetupPlayerEditor")]
    public async Task PointerOpenedPlayerFormOwnsItsOriginClocksAndSettlesOnReducedMotion()
    {
        using var f=new Fixture(1280,850);await Settle(350);f.Step(2);await Settle(500);
        OperatorAppearance.Configure(true,false);Assert.True(OperatorMotion.Enabled);
        var source=f.Tagged<Button>("add-home-player");f.AssertWithin(source,f.Window);
        var box=new Rect(source.TranslatePoint(default,f.Window)!.Value,source.Bounds.Size);
        foreach(var point in new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)})
        {
            var hit=f.Window.InputHitTest(point);Assert.True(ReferenceEquals(hit,source)||hit is Visual visual&&visual.GetVisualAncestors().Contains(source));
        }
        f.Window.MouseMove(box.Center);f.Window.MouseDown(box.Center,MouseButton.Left);f.Window.MouseUp(box.Center,MouseButton.Left);await Settle(35);
        var surface=f.Tagged<Control>("source-form-surface");var group=Assert.IsType<TransformGroup>(surface.RenderTransform);
        Assert.Contains(group.Children,x=>Motion.IsAnimating(Assert.IsAssignableFrom<AvaloniaObject>(x)));PlayerEditorFrame(f,"player-editor-motion-opening");
        OperatorAppearance.Configure(true,true);await Settle(50);
        Assert.All(group.Children,x=>Assert.False(Motion.IsAnimating(Assert.IsAssignableFrom<AvaloniaObject>(x))));
        var scale=Assert.IsType<ScaleTransform>(group.Children[0]);var shift=Assert.IsType<TranslateTransform>(group.Children[1]);Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,shift.X);Assert.Equal(0,shift.Y);
        f.Escape();await Settle(200);Assert.Empty(f.Players(true));Assert.Empty(f.Players(false));Assert.Equal(0,f.Cancellations);Assert.Equal(0,f.Completions);
    }

}
