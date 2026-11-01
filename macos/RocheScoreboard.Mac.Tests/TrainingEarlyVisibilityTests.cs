using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Prepared outside the repository. These synchronous tests deliberately do not
// await, pump the dispatcher, update layout, or force Loaded/private state before
// the public Start/Tick route. IsLoaded=false is an asserted fixture precondition.
public sealed class TrainingEarlyVisibilityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(TrainingScorebugControl output, string name) =>
        typeof(TrainingScorebugControl).GetField(name, Private)!.GetValue(output);
    private static TextBlock NamedText(TrainingScorebugControl output, string name) =>
        Assert.IsType<TextBlock>(output.FindControl<TextBlock>(name));

    private static TrainingSession Session(out TrainingTimer first, out TrainingTimer next)
    {
        var session = new TrainingSession { AutoAdvance = true };
        first = new TrainingTimer("Finished interval", TimeSpan.Zero);
        next = new TrainingTimer("Next interval", TimeSpan.FromMinutes(2));
        session.AddTimer(first);
        session.AddTimer(next);
        return session;
    }

    private static void AssertNoFinishWork(TrainingScorebugControl output)
    {
        Assert.False((bool)Field(output, "_flashing")!);
        foreach (string name in new[] { "_holdTimer", "_sessionCompleteTimer", "_promotionTimer" })
            Assert.Null(Field(output, name));
        Assert.False(Assert.IsType<Grid>(output.FindControl<Grid>("StagedTimerHost")).IsVisible);
        var mark=Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Shape>(output.FindControl<Control>("CompletionMark"));
        Assert.Equal(0,mark.Opacity);Assert.False(Motion.IsAnimating(mark));
        Assert.False(Motion.IsAnimating(Assert.IsType<TranslateTransform>(NamedText(output, "TitleText").RenderTransform)));
    }

    private static void Cleanup(TrainingScorebugControl output, NativeWindow? window)
    {
        // Only after assertions; no setup event, timer or visual state is forced.
        output.ResetVisuals();
        if (window is not null) { window.Content = null; window.Close(); }
        Motion.StopAll(Assert.IsType<TranslateTransform>(NamedText(output, "TitleText").RenderTransform));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void JustShownOutputOwnsImmediateAdvanceAndTheFinishedHoldBeforeLoaded(bool accessibleHost)
    {
        var session = Session(out var first, out var next);
        var output = new TrainingScorebugControl();
        output.Bind(session);
        var window = new NativeWindow { Title = accessibleHost ? "Operator probe" : "Training Display", Width = 386, Height = 193, Content = output };
        int finished = 0, advances = 0;
        session.IntervalFinished += _ => finished++;
        session.StructuralChanged += () => { if (ReferenceEquals(session.Active, next)) advances++; };
        try
        {
            window.Show();
            Assert.True(window.IsVisible);
            Assert.True(output.IsVisible);
            if (!accessibleHost) Assert.Same(window, output.GetVisualRoot());
            Assert.Same(window, NativeWindow.GetWindow(output));
            Assert.False(output.IsLoaded, "This regression requires the real Show-before-Loaded event order, without a dispatcher pump.");

            session.Start();
            session.Tick();

            Assert.True(first.HasFinished);
            Assert.Equal(1, finished);
            Assert.Equal(1, advances);
            Assert.Same(next, session.Active);
            Assert.Equal(1, session.ActiveIndex);
            Assert.True(session.IsRunning);
            Assert.False(next.HasFinished);
            Assert.Equal(TimeSpan.FromMinutes(2), next.Remaining);
            Assert.Equal("FINISHED INTERVAL", NamedText(output, "TitleText").Text);
            Assert.Equal("00:00", NamedText(output, "TimerText").Text);
            Assert.Equal("NEXT INTERVAL", NamedText(output, "StagedTitleText").Text);
            Assert.True(Assert.IsType<Grid>(output.FindControl<Grid>("StagedTimerHost")).IsVisible);
            Assert.True((bool)Field(output, "_flashing")!);
            var hold = Assert.IsType<DispatcherTimer>(Field(output, "_holdTimer"));
            Assert.Equal(TimeSpan.FromMilliseconds(7500), hold.Interval);
            Assert.True(hold.IsEnabled);
            Assert.Equal(Color.Parse("#102A21"),Assert.IsType<SolidColorBrush>(Assert.IsType<Grid>(output.FindControl<Grid>("RootGrid")).Background).Color);
            Assert.InRange(Assert.IsType<Border>(output.FindControl<Border>("GradientBg")).Opacity,.349,.351);
            Assert.True(Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Shape>(output.FindControl<Control>("CompletionMark")).IsVisible);
            Assert.Null(Field(output, "_sessionCompleteTimer"));
            Assert.Null(Field(output, "_promotionTimer"));

            // The model already runs the next timer during this presentation.
            // A second ordinary Tick must not consume another interval or reset
            // the currently owned finished-panel hold.
            session.Tick();
            Assert.Same(next, session.Active);
            Assert.Equal(1, finished);
            Assert.Equal(1, advances);
            Assert.Same(hold, Field(output, "_holdTimer"));
            Assert.Equal("FINISHED INTERVAL", NamedText(output, "TitleText").Text);
        }
        finally { Cleanup(output, window); }
    }

    [AvaloniaFact]
    public void NeverShownAndDetachedBeforeLoadedViewsCannotConsumeTheSessionsFinish()
    {
        foreach (bool detachAfterShow in new[] { false, true })
        {
            var session = Session(out var first, out var next);
            var output = new TrainingScorebugControl();
            output.Bind(session);
            NativeWindow? window = null;
            int finished = 0, advances = 0;
            session.IntervalFinished += _ => finished++;
            session.StructuralChanged += () => advances++;
            try
            {
                if (detachAfterShow)
                {
                    window = new NativeWindow { Title = "Training Display", Width = 386, Height = 193, Content = output };
                    window.Show();
                    Assert.True(window.IsVisible);
                    Assert.False(output.IsLoaded, "Detach the actual just-shown view before its first Loaded event.");
                    window.Content = null;
                }
                Assert.False(output.IsLoaded);
                Assert.Null(output.GetVisualRoot());
                AssertNoFinishWork(output);

                session.Start();
                session.Tick();

                Assert.True(first.HasFinished);
                Assert.Equal(1, finished);
                Assert.Equal(0, advances);
                Assert.Same(first, session.Active);
                Assert.Equal(0, session.ActiveIndex);
                Assert.False(session.IsRunning);
                Assert.False(session.IsExhausted);
                Assert.Same(next, session.UpNext);
                Assert.False(next.HasFinished);
                Assert.Equal(TimeSpan.FromMinutes(2), next.Remaining);
                AssertNoFinishWork(output);

                // TrainingSession.Tick alone stops at the finished interval.
                // MainWindow's separately tested off-screen fallback advances;
                // an unowned bound view must not act as that controller.
                session.Tick();
                Assert.Equal(1, finished);
                Assert.Equal(0, advances);
                Assert.Same(first, session.Active);
                Assert.False(session.IsRunning);
                AssertNoFinishWork(output);
            }
            finally { Cleanup(output, window); }
        }
    }
}
