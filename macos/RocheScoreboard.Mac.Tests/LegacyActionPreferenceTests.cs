using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class SharedPreferenceMotionTests
{
    [AvaloniaFact]
    [Trait("Evidence","LegacyActionSelectedRadio")]
    public async Task ASelectedLegacyRadioKeepsItsValueWhenItsActionMotionSettles()
    {
        using var appearance=new AppearanceScope();appearance.EnableAnimations();using var f=new Fixture(true);var choice=Choice(true);f.Add(choice);await f.Show();
        Assert.True(CricketActionMotion.GetEnabled(choice));var scale=Part<ScaleTransform>(choice,"TbSc");var lift=Part<TranslateTransform>(choice,"TbLift");var glow=Part<Control>(choice,"ActionGlow");
        using var events=new SemanticEvents(choice);var before=events.Counts;var bounds=choice.Bounds;
        var center=choice.TranslatePoint(new Point(choice.Bounds.Width/2,choice.Bounds.Height/2),f.Window);Assert.NotNull(center);f.Window.MouseMove(center.Value);Assert.True(Motion.IsAnimating(scale));
        appearance.Reduce();
        Assert.True(choice.IsChecked);AssertNoSemanticEvents(events,before);Assert.Equal(bounds,choice.Bounds);
        Assert.False(Motion.IsAnimating(scale));Assert.False(Motion.IsAnimating(lift));Assert.False(Motion.IsAnimating(glow));Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,lift.Y);
    }
    [AvaloniaTheory]
    [InlineData("hidden")][InlineData("disabled")][InlineData("removed")][InlineData("attached-off")]
    [Trait("Evidence","LegacyActionPreferenceOwner")]
    public async Task LegacyActionRetirementStopsOwnedClocksAndReentryRestoresItsPreferenceWatch(string retirement)
    {
        using var appearance=new AppearanceScope();appearance.EnableAnimations();using var f=new Fixture(true);var action=LegacyAction(f,"CricketActionButton");await f.Show();
        var scale=Part<ScaleTransform>(action,"TbSc");var lift=Part<TranslateTransform>(action,"TbLift");var glow=Part<Control>(action,"ActionGlow");
        void Hover(){var center=action.TranslatePoint(new Point(action.Bounds.Width/2,action.Bounds.Height/2),f.Window);Assert.NotNull(center);f.Window.MouseMove(new Point(2,2));f.Window.MouseMove(center.Value);Assert.True(Motion.IsAnimating(scale));}
        void Rest(){Assert.False(Motion.IsAnimating(scale));Assert.False(Motion.IsAnimating(lift));Assert.False(Motion.IsAnimating(glow));Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,lift.Y);}
        Hover();
        switch(retirement){case "hidden":action.IsVisible=false;break;case "disabled":action.IsEnabled=false;break;case "removed":f.Body.Children.Remove(action);break;default:CricketActionMotion.SetEnabled(action,false);break;}
        Rest();appearance.Reduce();Rest();appearance.EnableAnimations();
        switch(retirement){case "hidden":action.IsVisible=true;break;case "disabled":action.IsEnabled=true;break;case "removed":f.Body.Children.Add(action);break;default:CricketActionMotion.SetEnabled(action,true);break;}
        await Settle(30);Hover();appearance.Reduce();Rest();
    }
    private static Button LegacyAction(Fixture f,string style)
    {
        f.Window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary(style=="CricketActionButton"?"CricketActions":"OperatorControls"));
        var action=new Button {Content="Use this choice",Width=280,Height=48};action.SetResourceReference(Control.ThemeProperty,style);f.Add(action);return action;
    }
    [AvaloniaTheory]
    [InlineData("CricketActionButton")][InlineData("ToolBtn")][InlineData("PrimaryCommandButton")]
    [Trait("Evidence","LegacyActionPreferenceOwner")]
    public async Task LegacyActionLiveReduceStopsItsOwnClocksAndKeepsKeyboardAcceptanceImmediate(string style)
    {
        using var appearance=new AppearanceScope();appearance.EnableAnimations();using var f=new Fixture(true);var action=LegacyAction(f,style);await f.Show();
        Assert.True(CricketActionMotion.GetEnabled(action));var scale=Part<ScaleTransform>(action,"TbSc");var lift=Part<TranslateTransform>(action,"TbLift");var glow=Part<Control>(action,"ActionGlow");
        int accepted=0;action.Click+=(_,_)=>accepted++;var bounds=action.Bounds;
        var center=action.TranslatePoint(new Point(action.Bounds.Width/2,action.Bounds.Height/2),f.Window);Assert.NotNull(center);f.Window.MouseMove(center.Value);Assert.True(action.IsPointerOver);
        Assert.True(Motion.IsAnimating(scale));Assert.True(Motion.IsAnimating(lift));Assert.True(Motion.IsAnimating(glow));
        // Real pointer-started clock witness, then immediate actual app setting.
        // No sampled/artificial clock, private owner call or native-video claim.
        appearance.Reduce();
        Assert.False(Motion.IsAnimating(scale));Assert.False(Motion.IsAnimating(lift));Assert.False(Motion.IsAnimating(glow));
        Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,lift.Y);Assert.Equal(bounds,action.Bounds);Assert.Equal(0,accepted);
        Assert.True(action.Focus(NavigationMethod.Tab));Key(f.Window,PhysicalKey.Space);Assert.Equal(1,accepted);
        Assert.False(Motion.IsAnimating(scale));Assert.Equal(1,scale.ScaleX);Assert.Equal(1,scale.ScaleY);Assert.Equal(0,lift.Y);Assert.Equal(bounds,action.Bounds);
    }
}
