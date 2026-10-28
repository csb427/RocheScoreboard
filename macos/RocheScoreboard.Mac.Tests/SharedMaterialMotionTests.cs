using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;

public sealed class SharedMaterialMotionTests
{
    private static async Task Settle(int ms){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window w,string name)
    {string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-4-captures";Directory.CreateDirectory(dir);using var f=w.CaptureRenderedFrame();f!.Save(Path.Combine(dir,name+".png"));}
    [AvaloniaTheory]
    [InlineData(true)][InlineData(false)]
    public async Task TossSelectionIsImmediateInterruptibleAndSettlesWithTheLatestCheckedState(bool animate)
    {
        bool oldGlass=OperatorAppearance.GlassEnabled, oldReduce=OperatorAppearance.ReduceMotion;
        OperatorAppearance.Configure(oldGlass,false);
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=animate;
        var match=CricketReliabilityChecks.CreateMatch(true);match.TossPending=true;
        var panel=new CricketControlPanel();panel.SetMatch(match);var w=new NativeWindow{Width=700,Height=500,Content=panel};w.Show();
        try
        {
            await Settle(650);typeof(CricketControlPanel).GetMethod("OpenToss_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,[panel,new RoutedEventArgs()]);await Settle(650);
            var home=panel.FindControl<RadioButton>("TossHomeChoice")!;var away=panel.FindControl<RadioButton>("TossAwayChoice")!;var bat=panel.FindControl<RadioButton>("TossBatChoice")!;
            void Tap(RadioButton button){var p=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),w)!.Value;w.MouseDown(p,MouseButton.Left);w.MouseUp(p,MouseButton.Left);Dispatcher.UIThread.RunJobs();}
            Tap(home);Tap(bat);Assert.True(home.IsChecked);Assert.True(bat.IsChecked);Assert.True(panel.FindControl<Button>("ConfirmTossButton")!.IsEnabled);
            await Settle(65);Capture(w,$"toss-selection-{animate}-early");
            Assert.NotEmpty(home.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            if(animate)Assert.InRange(OperatorChoiceMotion.GetProgress(home),.001,.999);
            Tap(away);Assert.True(away.IsChecked);Assert.False(home.IsChecked);Tap(home);Assert.True(home.IsChecked);Assert.False(away.IsChecked);
            if(animate)
                for(int frame=0;frame<13;frame++){Capture(w,$"toss-motion-{frame:D2}");await Settle(45);}
            await Settle(650);Capture(w,$"toss-selection-{animate}-settled");
            Assert.Equal(1,OperatorChoiceMotion.GetProgress(home));Assert.Equal(0,OperatorChoiceMotion.GetProgress(away));
            Assert.Contains(match.TeamAName,panel.FindControl<TextBlock>("TossSelectionSummary")!.Text);
            if(animate)
            {
                Tap(away);await Settle(35);OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(oldGlass,true);
                Assert.Equal(0,OperatorChoiceMotion.GetProgress(home));Assert.Equal(1,OperatorChoiceMotion.GetProgress(away));
            }
        }
        finally{panel.ClearMatch();w.Close();OperatorAppearance.Configure(oldGlass,oldReduce);OperatorMotion.AnimationOverride=previous;}
    }
    [AvaloniaFact]
    public async Task SharedWindowArrivalKeepsItsCommandUsableAndReducedMotionStatic()
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=true;
        var command=new Button{Content="Act now",Width=120,Height=45};int clicks=0;command.Click+=(_,_)=>clicks++;
        var surface=new Grid();surface.Children.Add(command);var w=new NativeWindow{Width=700,Height=450,Content=surface};OperatorFeedback.Enable(w);w.Show();
        try
        {
            await Settle(65);Capture(w,"shared-window-arrival-early");Assert.InRange(surface.Opacity,.70,.999);
            var p=command.TranslatePoint(new Point(command.Bounds.Width/2,command.Bounds.Height/2),w)!.Value;w.MouseDown(p,MouseButton.Left);w.MouseUp(p,MouseButton.Left);Assert.Equal(1,clicks);
            SharedSurfaceMotion.Reveal(surface);SharedSurfaceMotion.Reveal(surface);await Settle(700);Assert.InRange(surface.Opacity,.999,1);
            OperatorMotion.AnimationOverride=false;SharedSurfaceMotion.Reveal(surface);Assert.Equal(1,surface.Opacity);Assert.True(surface.RenderTransform is null || surface.RenderTransform.Value==Matrix.Identity);
        }
        finally{w.Close();OperatorMotion.AnimationOverride=previous;}
    }

    [AvaloniaTheory]
    [InlineData(true)][InlineData(false)]
    public async Task PlayerMenuCanBeClosedAndReopenedDuringItsArrivalWithoutExecutingAStaleAction(bool animate)
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=animate;
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var w=new NativeWindow{Width=700,Height=500,Content=panel};w.Show();
        try
        {
            await Settle(650);var anchor=panel.GetVisualDescendants().OfType<Button>().First(b=>Equals(b.Content,"⋯")&&b.IsEffectivelyVisible);
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(65);
            var field=typeof(CricketControlPanel).GetField("_playerMenu",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var popup=(Avalonia.Controls.Primitives.Popup)field.GetValue(panel)!;Assert.True(popup.IsOpen);
            var surface=(Control)popup.Child!;
            var scale=((Avalonia.Media.TransformGroup)surface.RenderTransform!).Children.OfType<Avalonia.Media.ScaleTransform>().Single();
            if(animate)Assert.InRange(scale.ScaleY,.68,.999);else Assert.Equal(1,scale.ScaleY);
            if(TopLevel.GetTopLevel(surface) is { } top)
            {string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-4-captures";Directory.CreateDirectory(dir);using var f=top.CaptureRenderedFrame();f?.Save(Path.Combine(dir,$"player-menu-arrival-{animate}.png"));}
            var stale=surface.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Tag,"swap-strike"));
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Null(field.GetValue(panel));
            string before=JsonSerializer.Serialize(match.ToState());stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(650);Assert.NotNull(field.GetValue(panel));
            panel.ClearMatch();await Settle(250);Assert.Null(field.GetValue(panel));Assert.False(popup.IsOpen);
        }
        finally{panel.ClearMatch();w.Close();OperatorMotion.AnimationOverride=previous;}
    }
}
