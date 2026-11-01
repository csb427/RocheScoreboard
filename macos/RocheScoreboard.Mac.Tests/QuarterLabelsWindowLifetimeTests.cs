// Actual shown Home windows and public service events. No GC/leak or native
// desktop claim: old text stability is the oracle; subscriber counts diagnostic.
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflResetScopeTests
{
    private sealed record QuarterLabelPose(string? Text, string? Hint, string? Tooltip);
    private static QuarterLabelPose QuarterLabelRead(Fixture fixture) => new(
        Named<TextBox>(fixture.Window, "QuarterLabelCurrentBox").Text,
        Named<TextBlock>(fixture.Window, "QuarterLabelCurrentHint").Text,
        ToolTip.GetTip(Named<TextBox>(fixture.Window, "QuarterLabelCurrentBox")) as string);

    private static int QuarterLabelOwnedSubscriptions(Fixture fixture) =>
        (typeof(QuarterLabelService).GetField("LabelsChanged", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as Delegate)?.GetInvocationList()
            .Count(handler => ReferenceEquals(handler.Target, fixture.Window)) ?? 0;

    private static void QuarterLabelRecord(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class QuarterLabelWindowScope : IDisposable
    {
        private readonly List<Fixture> fixtures = new();
        private readonly string? previous;
        internal QuarterLabelWindowScope()
        {
            SelfTestStorage.Initialize(true);
            previous = QuarterLabelService.CurrentOverride;
        }
        internal async Task<Fixture> Open()
        {
            var fixture = await Fixture.Create(startMatch: false);
            fixtures.Add(fixture);
            Assert.True(fixture.Window.IsVisible);
            Assert.True(Named<Roche_Scoreboard.Views.SportSelectionScreen>(fixture.Window, "SportSelectionPanel").IsEffectivelyVisible);
            return fixture;
        }
        public void Dispose()
        {
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            foreach (var fixture in fixtures.AsEnumerable().Reverse())
            {
                Attempt(fixture.Window.Close);
                // Only after all test assertions: keep a failing original-source
                // baseline from leaving its own known subscription in the suite.
                // Never remove another window's callback or clear the event.
                Attempt(() =>
                {
                    var method = fixture.Window.GetType().GetMethod("OnQuarterLabelServiceChanged",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method is not null)
                        QuarterLabelService.LabelsChanged -= method.CreateDelegate<Action>(fixture.Window);
                });
                Attempt(fixture.Dispose);
            }
            Attempt(() => QuarterLabelService.SetCurrentOverride(previous));
            Attempt(() => Dispatcher.UIThread.RunJobs());
            if (errors.Count > 0) throw new AggregateException("Quarter label fixture cleanup failed.", errors);
        }
    }

    [AvaloniaFact]
    public async Task QuarterLabelsOpenHomeStillTracksPublicSetClearAndOperatorEdit()
    {
        using var scope = new QuarterLabelWindowScope();
        var fixture = await scope.Open();
        string state = Snapshot(fixture.Match); int changes = fixture.Changes;
        QuarterLabelService.SetCurrentOverride("  ABCDEFGHIJKLMNOP  ");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ABCDEFGHIJKL", QuarterLabelService.CurrentOverride);
        Assert.Equal("ABCDEFGHIJKL", QuarterLabelRead(fixture).Text);
        Assert.Contains("Override active.", QuarterLabelRead(fixture).Hint);
        QuarterLabelService.ClearCurrentOverride(); Dispatcher.UIThread.RunJobs();
        Assert.Null(QuarterLabelService.CurrentOverride);
        Assert.Equal(string.Empty, QuarterLabelRead(fixture).Text);
        Assert.Contains("Showing default", QuarterLabelRead(fixture).Hint);
        Assert.Contains(QuarterLabelService.GetQuarterLabel(fixture.Match.Quarter), QuarterLabelRead(fixture).Tooltip);
        Named<TextBox>(fixture.Window, "QuarterLabelCurrentBox").Text = "OPERATOR";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OPERATOR", QuarterLabelService.CurrentOverride);
        Assert.Equal("OPERATOR", QuarterLabelRead(fixture).Text);
        Assert.Equal(state, Snapshot(fixture.Match)); Assert.Equal(changes, fixture.Changes);
        QuarterLabelRecord("quarter-label-open-home", new { pose = QuarterLabelRead(fixture),
            subscriptionsDiagnostic = QuarterLabelOwnedSubscriptions(fixture) });
    }

    [AvaloniaFact]
    public async Task QuarterLabelsClosedWindowStaysFrozenWhileNewHomeTracksCurrentService()
    {
        using var scope = new QuarterLabelWindowScope();
        var old = await scope.Open();
        QuarterLabelService.SetCurrentOverride("OLD OWNER"); Dispatcher.UIThread.RunJobs();
        Assert.Equal("OLD OWNER", QuarterLabelRead(old).Text);
        bool closed = false; old.Window.Closed += (_, _) => closed = true;
        old.Window.Close(); Assert.True(closed); Assert.False(old.Window.IsVisible);
        var retired = QuarterLabelRead(old); string oldState = Snapshot(old.Match); int oldChanges = old.Changes;
        var current = await scope.Open();
        string currentState = Snapshot(current.Match); int currentChanges = current.Changes;
        QuarterLabelService.SetCurrentOverride("CURRENT"); Dispatcher.UIThread.RunJobs();
        var afterSet = QuarterLabelRead(old);
        Assert.Equal("CURRENT", QuarterLabelRead(current).Text);
        QuarterLabelService.ClearCurrentOverride(); Dispatcher.UIThread.RunJobs();
        var afterClear = QuarterLabelRead(old);
        Assert.Equal(string.Empty, QuarterLabelRead(current).Text);
        Assert.Contains("Showing default", QuarterLabelRead(current).Hint);
        QuarterLabelRecord("quarter-label-close-new-home", new { retired, afterSet, afterClear,
            current = QuarterLabelRead(current), oldSubscriptionsDiagnostic = QuarterLabelOwnedSubscriptions(old),
            currentSubscriptionsDiagnostic = QuarterLabelOwnedSubscriptions(current) });
        Assert.Equal(retired, afterSet); Assert.Equal(retired, afterClear);
        Assert.Equal(oldState, Snapshot(old.Match)); Assert.Equal(oldChanges, old.Changes);
        Assert.Equal(currentState, Snapshot(current.Match)); Assert.Equal(currentChanges, current.Changes);
    }

    [AvaloniaFact]
    public async Task QuarterLabelsAlreadyQueuedReceiveThreadRefreshCannotPaintAfterActualClose()
    {
        using var scope = new QuarterLabelWindowScope();
        var fixture = await scope.Open();
        QuarterLabelService.SetCurrentOverride("BEFORE"); Dispatcher.UIThread.RunJobs();
        var before = QuarterLabelRead(fixture); Assert.Equal("BEFORE", before.Text);
        int uiThread = Environment.CurrentManagedThreadId, receiveThread = 0;
        Exception? receiveError = null;
        var receiver = new Thread(() =>
        {
            try { receiveThread = Environment.CurrentManagedThreadId; QuarterLabelService.SetCurrentOverride("QUEUED"); }
            catch (Exception error) { receiveError = error; }
        }) { IsBackground = true };
        receiver.Start();
        // Production BeginInvoke is asynchronous. A bounded Join withholds UI
        // pumping without awaiting the queued callback or taking a service lock.
        Assert.True(receiver.Join(TimeSpan.FromSeconds(5)), "Service notification must return without waiting for UI dispatch.");
        Assert.Null(receiveError); Assert.NotEqual(uiThread, receiveThread);
        Assert.Equal("QUEUED", QuarterLabelService.CurrentOverride);
        Assert.Equal(before, QuarterLabelRead(fixture)); // Real callback remains queued.
        bool closed = false; QuarterLabelPose? atClosed = null;
        fixture.Window.Closed += (_, _) => { closed = true; atClosed = QuarterLabelRead(fixture); };
        fixture.Window.Close(); Assert.True(closed); Assert.False(fixture.Window.IsVisible);
        Assert.Equal(before, atClosed); // Close did not prematurely drain the witness.
        string state = Snapshot(fixture.Match); int changes = fixture.Changes;
        Dispatcher.UIThread.RunJobs();
        var afterDrain = QuarterLabelRead(fixture);
        QuarterLabelRecord("quarter-label-queued-close", new { uiThread, receiveThread, before, atClosed, afterDrain,
            subscriptionsDiagnostic = QuarterLabelOwnedSubscriptions(fixture) });
        Assert.Equal(before, afterDrain);
        Assert.Equal(state, Snapshot(fixture.Match)); Assert.Equal(changes, fixture.Changes);
    }
}
