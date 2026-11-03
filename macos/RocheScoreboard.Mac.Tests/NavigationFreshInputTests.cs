using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsSearchKeyboardTests
{
    [AvaloniaFact]
    [Trait("Evidence", "NavigationFreshInput")]
    public async Task NavigationContinuationProtectionDoesNotSuppressTheNextFreshGoal()
    {
        await using var fixture = await AflFixture.Create(1280, 850);
        var window = fixture.Window;
        PointerPressedEventArgs? press = null;
        EventHandler<PointerPressedEventArgs> observe = (_, e) => press = e;
        window.AddHandler(InputElement.PointerPressedEvent, observe, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        try
        {
            RawNavTap(window, Named<TabItem>(window, "MatchControlTab"));
            Assert.NotNull(press); var original = press;
            var goal = Named<Button>(window, "HomeGoalButton"); window.UpdateLayout();
            var goalPosition = goal.TranslatePoint(default, window)!.Value + new Point(goal.Bounds.Width/2, goal.Bounds.Height/2);
            int before = fixture.Match.HomeTotal, events = fixture.Match.Events.Count;
            // Routed metadata policy, not physical OS double-click evidence.
            var repeated = new PointerPressedEventArgs(goal,original.Pointer,window,goalPosition,original.Timestamp+1,
                original.GetCurrentPoint(window).Properties,KeyModifiers.None,2);
            goal.RaiseEvent(repeated); Assert.True(repeated.Handled); Assert.False(goal.IsPressed);
            var continuation = new PointerPressedEventArgs(window,original.Pointer,window,original.GetPosition(window),original.Timestamp+2,
                original.GetCurrentPoint(window).Properties,KeyModifiers.None,1);
            window.RaiseEvent(continuation); Assert.True(continuation.Handled);
            Assert.Equal(before,fixture.Match.HomeTotal); Assert.Equal(events,fixture.Match.Events.Count);
            RawNavTap(window,goal);RawNavTap(window,goal);
            Assert.Equal(before+12,fixture.Match.HomeTotal);Assert.Equal(events+2,fixture.Match.Events.Count);
            Assert.False(fixture.Match.ClockRunning);
        }
        finally { window.RemoveHandler(InputElement.PointerPressedEvent,observe); }
    }

    private static void RawNavTap(Window window, Control target)
    {
        window.UpdateLayout();
        Assert.True(target.IsEffectivelyVisible && target.IsEnabled);
        var origin = target.TranslatePoint(default, window); Assert.NotNull(origin);
        var point = origin.Value + new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        Assert.True(new Rect(window.ClientSize).Contains(point));
        var hit = window.InputHitTest(point);
        Assert.True(ReferenceEquals(hit,target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
            "Fresh pointer must hit the intended control, rather than a covering surface.");
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Evidence", "NavigationFreshInput")]
    public async Task FreshGoalsImmediatelyAfterPointerOrKeyboardNavigationAreNotDiscarded(bool keyboard)
    {
        await using var fixture = await AflFixture.Create(1280, 850);
        var window = fixture.Window; var matchTab = Named<TabItem>(window, "MatchControlTab");
        int before = fixture.Match.HomeTotal, events = fixture.Match.Events.Count;
        if (keyboard)
        {
            var tabs = Named<TabControl>(window, "MainTabControl");
            Assert.True(Named<TabItem>(window, "MatchSetupTab").Focus());
            window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
            Assert.Same(matchTab, tabs.SelectedItem);
        }
        else RawNavTap(window, matchTab);
        Assert.Same(matchTab, Named<TabControl>(window, "MainTabControl").SelectedItem);
        var goal = Named<Button>(window, "HomeGoalButton");
        // No settling delay: these are two new raw press/release gestures,
        // distinct from the press which selected the tab.
        RawNavTap(window, goal); RawNavTap(window, goal);
        Assert.Equal(before + 12, fixture.Match.HomeTotal);
        Assert.Equal(events + 2, fixture.Match.Events.Count);
        Assert.False(fixture.Match.ClockRunning);
    }
}
