using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;

public sealed class CricketSelectionRefinementTests
{
    private static async Task Settle(int ms=600){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window w,string name)
    {string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-4-captures";Directory.CreateDirectory(dir);using var f=w.CaptureRenderedFrame();f!.Save(Path.Combine(dir,name+".png"));}
    [AvaloniaTheory]
    [InlineData(false,700,450)][InlineData(true,700,450)][InlineData(false,420,320)][InlineData(true,420,320)]
    public async Task SelectionHoverHasRoomInsideTheListViewport(bool bowling,int width,int height)
    {
        var panel=new CricketControlPanel();panel.SetMatch(CricketReliabilityChecks.CreateMatch(true));
        var w=new NativeWindow{Width=width,Height=height,Content=panel};w.Show();
        try
        {
            await Settle();typeof(CricketControlPanel).GetMethod(bowling?"ShowBowlerSelection":"ShowBatterSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,bowling?[false]:["SELECT BATTER"]);await Settle();
            var list=panel.FindControl<ListBox>(bowling?"BowlerSelectionList":"BatterSelectionList")!;
            var item=list.GetVisualDescendants().OfType<ListBoxItem>().First(x=>x.Bounds.Width>0);
            var p=item.TranslatePoint(new Point(item.Bounds.Width/2,item.Bounds.Height/2),w)!.Value;w.MouseMove(p);await Settle(350);
            var surface=item.GetVisualDescendants().OfType<Control>().Single(x=>x.Name=="PART_Surface");
            Capture(w,$"selection-hover-{bowling}-{width}x{height}");
            var top=surface.TranslatePoint(default,list)!.Value;var bottom=surface.TranslatePoint(new Point(surface.Bounds.Width,surface.Bounds.Height),list)!.Value;
            Assert.True(top.X>=1 && bottom.X<=list.Bounds.Width-1,$"hover shape must remain inside list: {top}..{bottom} in {list.Bounds}");
        }
        finally{panel.ClearMatch();w.Close();}
    }
    [AvaloniaFact]
    public async Task MenuControlHasANeutralKeyboardRingWithoutAMouseFocusOutline()
    {
        var panel=new CricketControlPanel();panel.SetMatch(CricketReliabilityChecks.CreateMatch(true));
        var w=new NativeWindow{Width=700,Height=450,Content=panel};w.Show();
        try
        {
            await Settle();var menu=panel.GetVisualDescendants().OfType<Button>().First(x=>Equals(x.Content,"⋯")&&x.IsEffectivelyVisible);
            menu.Focus(NavigationMethod.Pointer);await Settle();Capture(w,"player-menu-pointer-focus");
            var ring=menu.GetVisualDescendants().OfType<Border>().SingleOrDefault(x=>x.Name=="MenuKeyboardFocus");Assert.NotNull(ring);Assert.False(ring.IsVisible);
            w.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);menu.Focus(NavigationMethod.Tab);menu.BringIntoView();w.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);await Settle();
            Capture(w,"player-menu-keyboard-focus");Assert.True(ring.IsVisible);
        }
        finally{panel.ClearMatch();w.Close();}
    }
    [AvaloniaFact]
    public async Task CricketWizardHasDistinctMeaningfulStepIcons()
    {
        var view=new CricketSetupWizard();var w=new NativeWindow{Width=800,Height=500,Content=view};w.Show();
        try
        {
            await Settle();Capture(w,"wizard-step-icons");
            var kinds=Enumerable.Range(1,4).Select(i=>view.FindControl<OperatorSymbol>($"Step{i}Symbol")!.Kind).ToArray();
            Assert.Equal(new[]{"team","players","clock","sliders"},kinds);
        }
        finally{w.Close();}
    }
}
