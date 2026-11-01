using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData("reduce")] [InlineData("retarget")] [InlineData("unload")]
    [Trait("Evidence","TabFallbackLivePolicy")]
    public async Task FallbackTabTrailSettlesToCurrentSelectionOnLivePolicyAndRetiresOnUnload(string boundary)
    {
        using var scope=new Scope();
        var tabs=new TabControl {Width=360,Height=180,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
            Theme=Assert.IsType<ControlTheme>(Application.Current!.Resources["Native.Base.TabControl"])};
        foreach(string name in new[]{"Match","Output","Settings"})tabs.Items.Add(new TabItem{Header=name,Content=new TextBlock{Text=name+" contents"}});
        tabs.SelectedIndex=0;OperatorTabMotion.Enable(tabs);var window=scope.Window(tabs,520,340);await Settle(450);
        Assert.DoesNotContain(tabs.GetVisualDescendants(),v=>v is OperatorSelectionLens);
        var layer=NativeAdornerLayer.GetAdornerLayer(tabs);Assert.NotNull(layer);
        var trail=Assert.Single(layer.Children.OfType<NativeAdorner>(),a=>a.GetType().Name=="Trail"&&ReferenceEquals(a.AdornedElement,tabs));
        Assert.False(Motion.IsAnimating(trail));
        tabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();
        Assert.True(Motion.IsAnimating(trail),"Actual fallback clocks must be active after public selection.");
        if(boundary=="retarget") {tabs.SelectedIndex=2;Dispatcher.UIThread.RunJobs();Assert.True(Motion.IsAnimating(trail));}
        int selection=tabs.SelectedIndex;
        if(boundary=="unload")window.Content=null;else OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
        Assert.False(Motion.IsAnimating(trail));Assert.Equal(selection,tabs.SelectedIndex);
        if(boundary=="unload") {Assert.DoesNotContain(trail,layer.Children);Assert.False(trail.IsLoaded);return;}
        var selected=Assert.IsType<TabItem>(tabs.SelectedItem);Point? point=selected.TranslatePoint(default,tabs);Assert.NotNull(point);
        foreach(var (name,value) in new[]{("X",point.Value.X+3),("Y",point.Value.Y+selected.Bounds.Height-3),("BarWidth",Math.Max(1,selected.Bounds.Width-6))})
        {
            var property=Assert.IsAssignableFrom<AvaloniaProperty>(trail.GetType().GetField(name+"Property",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
            Assert.Equal(value,Convert.ToDouble(trail.GetValue(property)),3);
        }
        await Settle(300);Assert.False(Motion.IsAnimating(trail));Assert.Equal(selection,tabs.SelectedIndex);
    }
}
