using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingOutputLifetimeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner);
    private static async Task Settle(int milliseconds = 180)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly TrainingSession Session;
        internal readonly TrainingScorebugControl Output = new();
        internal readonly NativeWindow Window;
        internal readonly TrainingTimer First, Next, Third;
        internal TextBlock Title => Assert.IsType<TextBlock>(Output.FindControl<TextBlock>("TitleText"));
        internal TextBlock Time => Assert.IsType<TextBlock>(Output.FindControl<TextBlock>("TimerText"));
        internal Grid Staged => Assert.IsType<Grid>(Output.FindControl<Grid>("StagedTimerHost"));
        internal TranslateTransform TitleTravel => Assert.IsType<TranslateTransform>(Title.RenderTransform);

        internal Fixture(string firstTitle = "Finished interval", bool autoAdvance = false, bool following = true)
        {
            Session = new TrainingSession { AutoAdvance = autoAdvance };
            First = new TrainingTimer(firstTitle, TimeSpan.Zero);
            Next = new TrainingTimer("Next interval", TimeSpan.FromMinutes(2));
            Third = new TrainingTimer("Last interval", TimeSpan.FromMinutes(3));
            Session.AddTimer(First);
            if (following) { Session.AddTimer(Next); Session.AddTimer(Third); }
            Output.Bind(Session);
            Window = new NativeWindow { Title = "Training Display", Width = 386, Height = 193, Content = Output };
            Window.Show();
        }

        internal async Task Ready()
        {
            await Settle();
            Assert.True(Output.IsLoaded);
            Assert.Same(First, Session.Active);
            Assert.False(Session.IsRunning);
        }

        internal void FinishFirst()
        {
            Session.Start();
            Session.Tick();
            Assert.True(First.HasFinished);
        }

        internal void AssertNoFinishWork()
        {
            Assert.False((bool)Field(Output, "_flashing")!);
            foreach (string name in new[] { "_holdTimer", "_sessionCompleteTimer", "_promotionTimer" })
                Assert.Null(Field(Output, name));
            Assert.False(Staged.IsVisible);
            var mark=Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Shape>(Output.FindControl<Control>("CompletionMark"));
            Assert.Equal(0,mark.Opacity);Assert.False(Motion.IsAnimating(mark));
        }

        public void Dispose()
        {
            Output.ResetVisuals();
            Window.Content = null;
            Window.Close();
            // Cleanup only, after the assertions: native lifecycle normally
            // stops this loop; also cover a failed assertion before detachment.
            Motion.StopAll(TitleTravel);
        }
    }

    [AvaloniaFact]
    public async Task ClearFromTheAdvanceNotificationLeavesNoFinishedPresentation()
    {
        using var f = new Fixture();
        await f.Ready();
        bool mutated = false;
        void ClearOnce()
        {
            if (mutated || !ReferenceEquals(f.Session.Active, f.Next)) return;
            mutated = true;
            f.Session.Clear();
        }
        // Loaded reattaches the view's handlers. Register after it so this is
        // an actual nested mutation during AdvanceAfterFinish notifications.
        f.Session.StructuralChanged += ClearOnce;
        try { f.FinishFirst(); }
        finally { f.Session.StructuralChanged -= ClearOnce; }

        Assert.True(mutated);
        Assert.Empty(f.Session.Timers);
        Assert.Null(f.Session.Active);
        Assert.False(f.Session.IsRunning);
        Assert.Equal("NO TIMERS", f.Title.Text);
        Assert.Equal("00:00", f.Time.Text);
        f.AssertNoFinishWork();
        await Settle(550);
        Assert.Empty(f.Session.Timers);
        Assert.Equal("NO TIMERS", f.Title.Text);
        f.AssertNoFinishWork();
    }

    [AvaloniaFact]
    public async Task RebindFromTheAdvanceNotificationCannotRearmTheOldFinish()
    {
        using var f = new Fixture();
        await f.Ready();
        var replacement = new TrainingSession { AutoAdvance = false };
        var fresh = new TrainingTimer("Fresh session", TimeSpan.FromMinutes(5));
        replacement.AddTimer(fresh);
        bool mutated = false;
        void RebindOnce()
        {
            if (mutated || !ReferenceEquals(f.Session.Active, f.Next)) return;
            mutated = true;
            f.Output.Bind(replacement);
        }
        f.Session.StructuralChanged += RebindOnce;
        try { f.FinishFirst(); }
        finally { f.Session.StructuralChanged -= RebindOnce; }

        Assert.True(mutated);
        Assert.Same(f.Next, f.Session.Active);
        Assert.Equal(1, f.Session.ActiveIndex);
        Assert.Same(fresh, replacement.Active);
        Assert.Equal(0, replacement.ActiveIndex);
        Assert.False(replacement.IsRunning);
        Assert.False(fresh.HasFinished);
        Assert.Equal(TimeSpan.FromMinutes(5), fresh.Remaining);
        Assert.Equal("FRESH SESSION", f.Title.Text);
        Assert.Equal("05:00", f.Time.Text);
        f.AssertNoFinishWork();
        await Settle(550);
        Assert.Same(fresh, replacement.Active);
        Assert.False(replacement.IsRunning);
        Assert.Equal("FRESH SESSION", f.Title.Text);
        f.AssertNoFinishWork();
    }

    [AvaloniaFact]
    public async Task ClosingOutputFromTheAdvanceNotificationLeavesNoDetachedFinishTimers()
    {
        using var f = new Fixture();
        await f.Ready();
        bool closed = false;
        void CloseOnce()
        {
            if (closed || !ReferenceEquals(f.Session.Active, f.Next)) return;
            closed = true;
            f.Window.Close();
        }
        f.Session.StructuralChanged += CloseOnce;
        try { f.FinishFirst(); }
        finally { f.Session.StructuralChanged -= CloseOnce; }

        Assert.True(closed);
        await Settle();
        Assert.False(f.Output.IsLoaded);
        Assert.Same(f.Next, f.Session.Active);
        Assert.Equal(1, f.Session.ActiveIndex);
        Assert.False(f.Session.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(2), f.Next.Remaining);
        f.AssertNoFinishWork();
        Assert.False(Motion.IsAnimating(f.TitleTravel));
    }

    [AvaloniaFact]
    public async Task SkipFromTheAdvanceNotificationShowsTheResultingQueueWithoutAnOldHold()
    {
        using var f = new Fixture();
        await f.Ready();
        bool skipped = false;
        void SkipOnce()
        {
            if (skipped || !ReferenceEquals(f.Session.Active, f.Next)) return;
            skipped = true;
            f.Session.Skip();
        }
        f.Session.StructuralChanged += SkipOnce;
        try { f.FinishFirst(); }
        finally { f.Session.StructuralChanged -= SkipOnce; }

        Assert.True(skipped);
        Assert.True(f.Next.HasFinished);
        Assert.Same(f.Third, f.Session.Active);
        Assert.Equal(2, f.Session.ActiveIndex);
        Assert.False(f.Session.IsRunning);
        Assert.False(f.Third.HasFinished);
        Assert.Equal(TimeSpan.FromMinutes(3), f.Third.Remaining);
        Assert.Equal("LAST INTERVAL", f.Title.Text);
        Assert.Equal("03:00", f.Time.Text);
        f.AssertNoFinishWork();
        await Settle(550);
        Assert.Same(f.Third, f.Session.Active);
        Assert.Equal("LAST INTERVAL", f.Title.Text);
        f.AssertNoFinishWork();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUninterruptedFinishAdvancesImmediatelyAndRetainsTheExistingVisualHold(bool autoAdvance)
    {
        using var f = new Fixture(autoAdvance: autoAdvance);
        await f.Ready();
        f.FinishFirst();

        Assert.Same(f.Next, f.Session.Active);
        Assert.Equal(1, f.Session.ActiveIndex);
        Assert.Equal(autoAdvance, f.Session.IsRunning);
        Assert.Equal("FINISHED INTERVAL", f.Title.Text);
        Assert.Equal("00:00", f.Time.Text);
        Assert.True(f.Staged.IsVisible);
        Assert.Equal("NEXT INTERVAL", f.Output.FindControl<TextBlock>("StagedTitleText")!.Text);
        Assert.True((bool)Field(f.Output, "_flashing")!);
        Assert.Equal(Color.Parse("#102A21"),Assert.IsType<SolidColorBrush>(Assert.IsType<Grid>(f.Output.FindControl<Grid>("RootGrid")).Background).Color);
        Assert.InRange(Assert.IsType<Border>(f.Output.FindControl<Border>("GradientBg")).Opacity,.349,.351);
        Assert.True(Assert.IsAssignableFrom<Avalonia.Controls.Shapes.Shape>(f.Output.FindControl<Control>("CompletionMark")).IsVisible);
        var hold = Field(f.Output, "_holdTimer");
        Assert.NotNull(hold);
        Assert.Equal(TimeSpan.FromMilliseconds(7500), (TimeSpan)hold.GetType().GetProperty("Interval")!.GetValue(hold)!);
        Assert.Null(Field(f.Output, "_sessionCompleteTimer"));
        if (autoAdvance)
        {
            Assert.True(f.Session.ActiveRemaining > TimeSpan.Zero);
            Assert.True(f.Session.ActiveRemaining <= TimeSpan.FromMinutes(2));
        }
        else Assert.Equal(TimeSpan.FromMinutes(2), f.Session.ActiveRemaining);
    }

    [AvaloniaFact]
    public async Task TheFinalFinishExhaustsTheModelImmediatelyAndKeepsTheTenSecondPresentation()
    {
        using var f = new Fixture(following: false);
        await f.Ready();
        f.FinishFirst();

        Assert.Null(f.Session.Active);
        Assert.True(f.Session.IsExhausted);
        Assert.False(f.Session.IsRunning);
        Assert.Equal("SESSION COMPLETE", f.Title.Text);
        Assert.Equal("00:00", f.Time.Text);
        Assert.False(f.Staged.IsVisible);
        Assert.Null(Field(f.Output, "_holdTimer"));
        var completion = Field(f.Output, "_sessionCompleteTimer");
        Assert.NotNull(completion);
        Assert.Equal(TimeSpan.FromSeconds(10), (TimeSpan)completion.GetType().GetProperty("Interval")!.GetValue(completion)!);
    }

    [AvaloniaTheory]
    [InlineData("A very long training interval title that exceeds the entire display and must scroll so every word can be read", true)]
    [InlineData("Drill", false)]
    public async Task TitleClockStopsOnUnloadAndReopensAccordingToActualOverflow(string title, bool overflow)
    {
        using var f = new Fixture(firstTitle: title);
        await f.Ready();
        f.Title.Measure(Size.Infinity);
        var clip = Assert.IsType<Canvas>(f.Output.FindControl<Canvas>("TitleClip"));
        Assert.Equal(overflow, f.Title.DesiredSize.Width > clip.Bounds.Width - 8);
        Assert.Equal(overflow, Motion.IsAnimating(f.TitleTravel));

        f.Window.Content = null;
        f.Window.UpdateLayout();
        await Settle();
        Assert.False(f.Output.IsLoaded);
        Assert.False(Motion.IsAnimating(f.TitleTravel));

        f.Window.Content = f.Output;
        await Settle();
        Assert.True(f.Output.IsLoaded);
        Assert.Equal(title.ToUpperInvariant(), f.Title.Text);
        Assert.Equal(overflow, Motion.IsAnimating(f.TitleTravel));
        if (!overflow) Assert.Equal(0, f.TitleTravel.X, 3);
        Assert.Same(f.First, f.Session.Active);
        Assert.Equal(0, f.Session.ActiveIndex);
        Assert.False(f.Session.IsRunning);
    }
}
