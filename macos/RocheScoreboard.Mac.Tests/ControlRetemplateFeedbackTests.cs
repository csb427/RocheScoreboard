using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class SharedPreferenceMotionTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    [Trait("Evidence","ControlRetemplateFeedback")]
    public async Task ReplacingTheProductionTemplateRetiresOldGlowAndRestoresCurrentHover(bool reduced)
    {
        using var appearance=new AppearanceScope();appearance.EnableAnimations();if(reduced)appearance.Reduce();
        using var f=new Fixture();var choice=Choice();f.Add(choice);await f.Show();
        using var semantic=new SemanticEvents(choice);var before=semantic.Counts;
        var center=choice.TranslatePoint(new Point(choice.Bounds.Width/2,choice.Bounds.Height/2),f.Window);Assert.NotNull(center);
        f.Window.MouseMove(center.Value);Assert.True(choice.IsPointerOver);
        var old=Assert.Single(choice.GetVisualDescendants().OfType<Border>(),x=>x.Name=="ActionGlow");
        Motion.SampleAt(old,TimeSpan.FromMilliseconds(140));Assert.Equal(.035,old.Opacity,5);
        // A fresh instance of the SAME production dictionary supplies the real
        // template. No fabricated template, private state or handler invocation.
        var replacement=NativeAssets.Dictionary("ControlCentre");
        Assert.True(replacement.TryGetResource("SettingsTile",ThemeVariant.Dark,out var theme));
        choice.Theme=Assert.IsType<ControlTheme>(theme);
        choice.ApplyTemplate();await Settle(180);
        var current=Assert.Single(choice.GetVisualDescendants().OfType<Border>(),x=>x.Name=="ActionGlow");
        Assert.NotSame(old,current);Assert.True(choice.IsPointerOver);
        Motion.SampleAt(current,TimeSpan.FromMilliseconds(140));
        Assert.Equal(.035,current.Opacity,5);
        Assert.False(Motion.IsAnimating(old));
        Assert.False(choice.IsChecked==true);AssertNoSemanticEvents(semantic,before);
        f.Window.MouseMove(new Point(2,2));Motion.SampleAt(current,TimeSpan.FromMilliseconds(140));Assert.Equal(0,current.Opacity,5);
    }
}
