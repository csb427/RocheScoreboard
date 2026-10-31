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
    // OUTSIDE TEST-ONLY DRAFT: UNCOMPILED/UNRUN. Replace only the existing method.
    // Actual Loaded state and public headless pointer input are observed before
    // any await/PNG. No native65ms claim and no production timeline change.
    [AvaloniaFact]
    public async Task SharedWindowArrivalKeepsItsCommandUsableAndReducedMotionStatic()
    {
        bool? previous = OperatorMotion.AnimationOverride;
        var command = new Button { Content = "Act now", Width = 120, Height = 45 };
        int clicks = 0; command.Click += (_, _) => clicks++;
        var surface = new Grid(); surface.Children.Add(command);
        var w = new NativeWindow { Width = 700, Height = 450, Content = surface };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var trace = new List<object>();
        double? loadedOpacity = null;
        bool loadedClock = false, loadedVisible = false, loadedHit = false, arrivalHit = false, arrivalEnded = false, probing = false;
        bool dispatchStarted = false, dispatchCompleted = false, upDuringArrival = false;
        string? inputPhase = null;
        string? loadedInputError = null;
        bool Within(object? hit) => ReferenceEquals(hit, command)
            || hit is Avalonia.Visual visual && visual.GetVisualAncestors().Contains(command);
        Point? ReadyPoint()
        {
            var corners = new[] { new Point(), new Point(command.Bounds.Width, 0),
                new Point(0, command.Bounds.Height), new Point(command.Bounds.Width, command.Bounds.Height) }
                .Select(point => command.TranslatePoint(point, w)).ToArray();
            if (corners.Any(point => point is null) || command.Bounds.Width <= 0 || command.Bounds.Height <= 0) return null;
            double left = corners.Min(point => point!.Value.X), top = corners.Min(point => point!.Value.Y);
            var box = new Rect(left, top, corners.Max(point => point!.Value.X) - left, corners.Max(point => point!.Value.Y) - top);
            if (!new Rect(w.ClientSize).Contains(box)) return null;
            var probes = new[] { box.Center, new Point(box.Left + 3, box.Center.Y), new Point(box.Right - 3, box.Center.Y),
                new Point(box.Center.X, box.Top + 3), new Point(box.Center.X, box.Bottom - 3) };
            return probes.All(point => Within(w.InputHitTest(point))) ? box.Center : null;
        }
        void Loaded(object? sender, RoutedEventArgs args)
        {
            if (loadedOpacity is not null) return;
            // Source Loaded installed all timeline callbacks before this observer.
            // Run the observation after Motion.Tick, never against a stale pose.
            NativeRendering.Rendering += Frame;
            // Enable() attaches the actual Surface.Loaded handler first.
            // No assert or awaited continuation can delay this observation.
            loadedOpacity = surface.Opacity;
            loadedClock = Motion.IsAnimating(surface);
            loadedVisible = w.IsVisible && surface.IsEffectivelyVisible && command.IsEffectivelyEnabled;
            trace.Add(new { Source = "Loaded", Milliseconds = watch.Elapsed.TotalMilliseconds,
                Opacity = loadedOpacity, ActiveClock = loadedClock, Visible = loadedVisible });
            try
            {
                // Diagnostic logical hit only. Platform input always waits for
                // the guarded rendered scene in Frame, never this early event.
                loadedHit = ReadyPoint() is not null;
            }
            catch (Exception error) { loadedInputError = error.ToString(); }
        }
        void OpacityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.OpacityProperty)
                trace.Add(new { Source = "OpacityChanged", Milliseconds = watch.Elapsed.TotalMilliseconds,
                    Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface) });
        }
        void Frame(object? sender, EventArgs args)
        {
            trace.Add(new { Source = "NativeRendering", Milliseconds = watch.Elapsed.TotalMilliseconds,
                Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface) });
            if (loadedOpacity is not null && !Motion.IsAnimating(surface) && surface.Opacity >= .999)
                arrivalEnded = true;
            // Loaded precedes the headless hit scene. Observe the first real
            // usable rendering phase, still requiring an active, mid-arrival
            // timeline; there is no click fallback after arrival has finished.
            if (!dispatchStarted && !probing && Motion.IsAnimating(surface) && surface.Opacity is >= .70 and < .999)
            {
                probing = true;
                try
                {
                    // NativeRendering precedes publication of the headless
                    // hit scene. Render before injecting platform input, then
                    // recheck the actual clock/pose instead of clicking late.
                    using var rendered = w.CaptureRenderedFrame();
                    if (rendered is not null && Motion.IsAnimating(surface) && surface.Opacity is >= .70 and < .999
                        && ReadyPoint() is Point point)
                    {
                        inputPhase = "NativeRendering"; dispatchStarted = true;
                        trace.Add(new { Source = "InputBeforeMove", Milliseconds = watch.Elapsed.TotalMilliseconds,
                            Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface), command.IsPointerOver });
                        w.MouseMove(point);
                        trace.Add(new { Source = "InputBeforeDispatch", Milliseconds = watch.Elapsed.TotalMilliseconds,
                            Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface), command.IsPointerOver });
                        // MouseMove pumps native rendering. Do not publish a
                        // completed witness before Down/Up have actually returned,
                        // or a reentrant await can assert/close the owner mid-move.
                        if (!Motion.IsAnimating(surface) || surface.Opacity is < .70 or >= .999
                            || ReadyPoint() is null) return;
                        w.MouseDown(point, MouseButton.Left);
                        trace.Add(new { Source = "InputAfterDown", Milliseconds = watch.Elapsed.TotalMilliseconds,
                            Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface), Clicks = clicks });
                        upDuringArrival = Motion.IsAnimating(surface) && surface.Opacity is >= .70 and < .999;
                        w.MouseUp(point, MouseButton.Left);
                        trace.Add(new { Source = "InputAfterUp", Milliseconds = watch.Elapsed.TotalMilliseconds,
                            Opacity = surface.Opacity, ActiveClock = Motion.IsAnimating(surface), Clicks = clicks });
                        dispatchCompleted = true; arrivalHit = true;
                    }
                }
                catch (Exception error) { loadedInputError = error.ToString(); }
                finally { probing = false; }
            }
        }
        try
        {
            OperatorMotion.AnimationOverride = true;
            surface.PropertyChanged += OpacityChanged;
            OperatorFeedback.Enable(w);
            surface.Loaded += Loaded;
            w.Show();
            for (int attempt = 0; attempt < 100 && (probing || (!arrivalHit && !arrivalEnded)); attempt++) await Settle(12);
            Assert.NotNull(loadedOpacity);
            Assert.InRange(loadedOpacity!.Value, .70, .999);
            Assert.True(loadedClock, "The actual Loaded arrival must install an active opacity clock, not a static dim pose.");
            Assert.True(loadedVisible); Assert.Null(loadedInputError);
            Assert.True(arrivalHit, "The command needs its complete actual hit region during an observed active arrival.");
            Assert.True(dispatchStarted); Assert.True(dispatchCompleted); Assert.True(upDuringArrival);
            Assert.Equal(1, clicks);
            // Re-entry remains real source behavior. No clock is rewound or
            // sampled synthetically; eventual real rendering supplies the end.
            SharedSurfaceMotion.Reveal(surface); SharedSurfaceMotion.Reveal(surface);
            await Settle(700);
            Capture(w, "shared-window-arrival-after-command");
            Assert.InRange(surface.Opacity, .999, 1);
            OperatorMotion.AnimationOverride = false;
            SharedSurfaceMotion.Reveal(surface);
            Assert.Equal(1, surface.Opacity);
            Assert.True(surface.RenderTransform is null || surface.RenderTransform.Value == Matrix.Identity);
            Assert.False(Motion.IsAnimating(surface));
            Assert.True(ReadyPoint() is not null);
        }
        finally
        {
            NativeRendering.Rendering -= Frame; surface.Loaded -= Loaded; surface.PropertyChanged -= OpacityChanged;
            try { w.Close(); } finally { OperatorMotion.AnimationOverride = previous; }
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "shared-window-arrival-observations.json"),
                    JsonSerializer.Serialize(new { LoadedOpacity = loadedOpacity, LoadedClock = loadedClock,
                        LoadedLogicalHitReady = loadedHit, ArrivalHit = arrivalHit, ArrivalEnded = arrivalEnded, DispatchStarted = dispatchStarted, DispatchCompleted = dispatchCompleted, UpDuringArrival = upDuringArrival, InputPhase = inputPhase, LoadedInputError = loadedInputError, Clicks = clicks, Samples = trace },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
        }
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
