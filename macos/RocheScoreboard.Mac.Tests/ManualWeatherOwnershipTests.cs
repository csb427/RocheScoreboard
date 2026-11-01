// External draft: UNCOMPILED/UNRUN. Twelve controlled-operation cases.
// Public original control Invoke and existing IRemoteCommandTarget/local command
// routes; no private command invocation, progress writes, network or credentials.
// Feedback sink proves controller requests, not desktop modal presentation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Reuse the existing isolated, public-setup fixture; no fixture changes.
public sealed partial class LiveSettingsClockChoiceTests
{
    private sealed class ManualWeatherPending(string location)
    {
        internal readonly string Location = location;
        internal readonly TaskCompletionSource<WeatherSnapshot?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Returned;
        internal Task? Controller;
        internal async Task<WeatherSnapshot?> Run()
        {
            try { return await Completion.Task; }
            finally { Returned = true; }
        }
    }
    private sealed class ManualWeatherControlledScope : IAsyncDisposable
    {
        internal readonly AflFixture Fixture;
        internal readonly List<ManualWeatherPending> Pending = new();
        internal readonly List<Task> Operations = new();
        internal readonly List<(string Text, string Caption, bool Warning)> Feedback = new();
        private readonly List<object> attempts = new();
        private readonly Func<string, Task<WeatherSnapshot?>>? previousFetch;
        private readonly Action<string, string, bool>? previousFeedback;
        private readonly Action<Task>? previousObserver;
        private bool cleaning, disposed, seamsRestored;

        private ManualWeatherControlledScope(AflFixture value)
        {
            Fixture = value;
            Assert.True(((App)Application.Current!).IsSelfTestMode);
            Assert.Null(PresPlanRead<object?>(value, "_weatherService"));
            previousFetch = value.Window.ManualWeatherFetchAsyncOverride;
            previousFeedback = value.Window.ManualWeatherFeedbackOverride;
            previousObserver = value.Window.ManualWeatherOperationObserved;
            value.Window.ManualWeatherFetchAsyncOverride = location =>
            {
                var operation = new ManualWeatherPending(location); Pending.Add(operation); return operation.Run();
            };
            value.Window.ManualWeatherFeedbackOverride = (text, caption, warning) =>
            {
                Feedback.Add((text, caption, warning));
                attempts.Add(new { Stage = cleaning ? "cleanup-feedback" : "body-feedback", text, caption, warning });
            };
            value.Window.ManualWeatherOperationObserved = operation =>
            {
                Operations.Add(operation);
                attempts.Add(new { Stage = cleaning ? "cleanup-controller-observed" : "body-controller-observed", Index = Operations.Count - 1 });
            };
        }

        // Keep the original test failure if cleanup also fails. Both exceptions
        // and all controller/feedback attempts are retained in a durable receipt.
        internal static async Task Run(Func<ManualWeatherControlledScope, Task> body, [CallerMemberName] string caseName = "")
        {
            var fixture = await AflFixture.Create(1280, 850);
            ManualWeatherControlledScope? scope = null;
            Exception? failure = null;
            try { scope = new ManualWeatherControlledScope(fixture); await body(scope); }
            catch (Exception exception) { failure = exception; }
            try
            {
                if (scope is not null) await scope.DisposeAsync();
                else await fixture.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                if (failure is null) failure = cleanup;
                else failure.Data["ManualWeatherCleanupFailure"] = cleanup.ToString();
            }
            if (scope is not null)
            {
                try { scope.WriteReceipt(caseName, failure); }
                catch (Exception receiptFailure)
                {
                    if (failure is null) failure = receiptFailure;
                    else failure.Data["ManualWeatherReceiptFailure"] = receiptFailure.ToString();
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private async Task DrainControllers(string stage)
        {
            attempts.Add(new { Stage = stage, Requests = Pending.Count, Controllers = Operations.Count });
            // Complete only the controlled provider results. The observation is
            // the ACTUAL original controller Task, including feedback and air.
            // A UI flush first admits any already-queued real Invoke callbacks.
            await Settle(20);
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var request in Pending.ToArray()) request.Completion.TrySetResult(null);
                var tasks = Operations.ToArray();
                await Until(() => tasks.All(task => task.IsCompleted), stage + ": every observed actual controller must terminate within the bounded readiness window.");
                foreach (var task in tasks) await task; // Do not hide faults.
                int requests = Pending.Count, controllers = Operations.Count;
                await Settle(20);
                if (requests == Pending.Count && controllers == Operations.Count
                    && Pending.All(request => request.Completion.Task.IsCompleted) && Operations.All(task => task.IsCompleted)) return;
            }
            throw new TimeoutException(stage + ": queued controller work did not become terminal after four bounded drains.");
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true; cleaning = true;
            var failures = new List<Exception>();
            try { await DrainControllers("before-owner-teardown"); }
            catch (Exception exception) { failures.Add(exception); }
            // AflFixture owns the real Window close AND its later UI/storage
            // flush. The feedback sink remains installed through all of it.
            Task? teardown = null;
            try { teardown = Fixture.DisposeAsync().AsTask(); await teardown.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception exception) { failures.Add(exception); }
            try { await DrainControllers("after-owner-teardown"); }
            catch (Exception exception) { failures.Add(exception); }
            bool ownerClosed = !Fixture.Window.IsVisible && PresPlanRead<bool>(Fixture, "_remoteCommandOwnerClosed");
            bool terminal = Operations.All(task => task.IsCompleted) && Pending.All(request => request.Completion.Task.IsCompleted);
            bool teardownTerminal = teardown?.IsCompleted == true;
            attempts.Add(new { Stage = "cleanup-terminal-check", OwnerClosed = ownerClosed, Terminal = terminal, TeardownTerminal = teardownTerminal });
            if (ownerClosed && terminal && teardownTerminal)
            {
                Fixture.Window.ManualWeatherFetchAsyncOverride = previousFetch;
                Fixture.Window.ManualWeatherFeedbackOverride = previousFeedback;
                Fixture.Window.ManualWeatherOperationObserved = previousObserver;
                seamsRestored = true;
            }
            else
            {
                // Never expose the real MessageBox path to unproven pending
                // work. Keep this closed fixture's sinks installed and fail.
                failures.Add(new InvalidOperationException("Manual weather cleanup could not prove closed-owner/controller termination; controlled sinks retained."));
            }
            if (failures.Count > 0) throw new AggregateException("Manual weather bounded cleanup failed.", failures);
        }

        private void WriteReceipt(string caseName, Exception? failure)
        {
            string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "roche-manual-weather-cleanup");
            Directory.CreateDirectory(directory);
            var receipt = new
            {
                Case = caseName, Error = failure?.ToString(), CleanupError = failure?.Data["ManualWeatherCleanupFailure"],
                SeamsRestored = seamsRestored, Attempts = attempts,
                Controllers = Operations.Select((task, index) => new { Index = index, Status = task.Status.ToString(), task.IsCompleted }),
                Requests = Pending.Select(request => new { request.Location, request.Returned, ProviderTerminal = request.Completion.Task.IsCompleted,
                    ControllerTerminal = request.Controller?.IsCompleted }),
                Feedback = Feedback.Select(item => new { item.Text, item.Caption, item.Warning })
            };
            File.WriteAllText(Path.Combine(directory, "manual-weather-cleanup-" + caseName + "-" + Guid.NewGuid().ToString("N") + ".json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static T ManualWeatherRead<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static WeatherSnapshot ManualWeatherSnapshot(double temperature) => new(temperature, temperature - 1, temperature - 3, temperature + 4,
        0, "☀", "Controlled clear", new[] { new HourlyForecast(new DateTime(2026, 10, 31, 12, 0, 0), temperature, 0, "☀", "Controlled clear", 0, 0) });
    private static string ManualWeatherModel(AflFixture f) => JsonSerializer.Serialize(f.Match.ToState());
    private static string? ManualWeatherOverlay(ScorebugControl bug)
        => typeof(ScorebugControl).GetField("_activeOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bug)?.ToString();
    private static string ManualWeatherSurfaces(AflFixture f) => string.Join("|", new[] { "_scorebug", "_breakScreen", "_weatherScreen", "_statsScreen", "_scoreworm" }
        .Select(name => name + ":" + (PresPlanRead<Control?>(f, name)?.IsVisible.ToString() ?? "absent")));

    private static async Task<ScorebugControl> ManualWeatherPrepare(AflFixture f, ManualWeatherControlledScope controlled, bool offAir)
    {
        // The existing fixture finishes real AFL setup with an empty weather
        // field. Install the per-instance seam BEFORE this public location route.
        var remote = (IRemoteCommandTarget)f.Window;
        remote.SetWeatherLocation("Controlled venue A");
        Assert.Null(PresPlanRead<object?>(f, "_weatherService"));
        Assert.Empty(controlled.Pending); // Applying location must not request fake manual data either.
        remote.SwitchScreen("scorebug");
        if (!offAir) remote.ShowScoreboardWindow();
        else if (PresWindowOpen(f))
        {
            // Closing really destroys the old output controls. Select the real
            // scorebug again to create the current hidden/off-air surface.
            PresPlanProvider(Named<Control>(f.Window, "PresCardWindow")).Invoke();
            await Until(() => PresPlanRead<Window?>(f, "_displayWindow") is null,
                "The actual output close must complete before constructing a new hidden surface.");
            remote.SwitchScreen("scorebug");
        }
        await Mr4Navigate(f.Window, PhysicalKey.Digit2); await Settle(650);
        Assert.Equal(!offAir, PresWindowOpen(f));
        var bug = PresPlanRead<ScorebugControl>(f, "_scorebug"); Assert.Null(bug.GetWeatherSnapshot());
        return bug;
    }
    private static async Task<ManualWeatherPending> ManualWeatherInvoke(AflFixture f, ManualWeatherControlledScope controlled, string caption)
    {
        // The presentation selector also has a WEATHER card. Find the
        // actual three-card weather-overlay group by its complete semantics.
        var group = Assert.Single(f.Window.GetVisualDescendants().OfType<UniformGrid>(),
            item => item.Children.OfType<OperatorActionBorder>().Any(card => card.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "FORECAST"))
                && item.Children.OfType<OperatorActionBorder>().Any(card => card.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "RAIN")));
        var card = Assert.Single(group.Children.OfType<OperatorActionBorder>(),
            item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == caption));
        await PresPlanReveal(f, card);
        int count = controlled.Pending.Count, operations = controlled.Operations.Count;
        PresPlanProvider(card).Invoke();
        await Until(() => controlled.Pending.Count == count + 1 && controlled.Operations.Count == operations + 1, "The actual original " + caption + " control Invoke must reach the deferred fetch.");
        var pending = controlled.Pending[count]; pending.Controller = controlled.Operations[operations]; Assert.Equal("Controlled venue A", pending.Location);
        Assert.False(pending.Completion.Task.IsCompleted);
        Assert.Null(PresPlanRead<object?>(f, "_weatherService"));
        return pending;
    }
    private static async Task ManualWeatherComplete(ManualWeatherPending pending, WeatherSnapshot? snapshot)
    {
        pending.Completion.SetResult(snapshot);
        Assert.NotNull(pending.Controller);
        await Until(() => pending.Controller.IsCompleted, "The actual original controller operation must finish feedback/cache/air processing before any negative assertion.");
        await pending.Controller; // Surface actual operation faults, not only provider completion.
        Assert.True(pending.Returned);
    }

    [AvaloniaTheory]
    [InlineData("FORECAST", true)]
    [InlineData("RAIN", false)]
    public async Task ManualWeatherOwnershipCurrentRequestPublishesOnlyItsSnapshotAndPreservesOutputPolicy(string caption, bool offAir)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
        var f = controlled.Fixture;
        var bug = await ManualWeatherPrepare(f, controlled, offAir);
        string model = ManualWeatherModel(f); using var changes = new PresPlanChanges(f.Match);
        var pending = await ManualWeatherInvoke(f, controlled, caption); var snapshot = ManualWeatherSnapshot(17);
        Assert.Equal("Loading weather for Controlled venue A…", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
        await ManualWeatherComplete(pending, snapshot);
        await Until(() => ManualWeatherOverlay(bug) == (caption == "RAIN" ? "RainForecast" : "Forecast"), "The still-current original manual request must reach its real overlay dispatcher.");
        Assert.Same(snapshot, bug.GetWeatherSnapshot()); Assert.Empty(controlled.Feedback);
        Assert.Equal("Weather ready for Controlled venue A", Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
        Assert.Equal(!offAir, PresWindowOpen(f)); Assert.Equal(offAir, PresPlanRead<bool>(f, "_outputActivationRequired"));
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(0, changes.Count);
        Assert.Null(PresPlanRead<object?>(f, "_weatherService"));
        });
    }

    [AvaloniaTheory]
    [InlineData("newer-presentation", false)]
    [InlineData("cancel-failure", true)]
    [InlineData("location-return", false)]
    [InlineData("output-close", false)]
    [InlineData("home", false)]
    [InlineData("new-setup", false)]
    [InlineData("same-model-reset", false)]
    [InlineData("owner-close", true)]
    public async Task ManualWeatherOwnershipObsoleteResponseCannotWriteFeedbackCacheOrAir(string boundary, bool fail)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
        var f = controlled.Fixture;
        var bug = await ManualWeatherPrepare(f, controlled, offAir: false);
        var pending = await ManualWeatherInvoke(f, controlled, "FORECAST");
        var remote = (IRemoteCommandTarget)f.Window;
        long lifetime = f.Match.LifecycleVersion;
        switch (boundary)
        {
            case "newer-presentation": remote.SwitchScreen("summary"); break;
            case "cancel-failure":
                var cancel = Named<Button>(f.Window, "CancelOverlayButton");
                Assert.True(cancel.IsEffectivelyVisible && cancel.IsEffectivelyEnabled);
                Assert.True(cancel.Focus(NavigationMethod.Tab)); cancel.BringIntoView(); await Settle(100);
                // Exercise the actual button's semantic action. Full pointer
                // geometry is a separately retained layout acceptance hold.
                PresPlanProvider(cancel).Invoke(); break;
            case "location-return": remote.SetWeatherLocation("Controlled venue B"); remote.SetWeatherLocation("Controlled venue A"); break;
            case "output-close": PresPlanRead<Window>(f, "_displayWindow").Close(); break;
            case "home": Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("home")); break;
            case "new-setup":
                Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("home"));
                Assert.True(f.Window.OpenCommandBar()); Assert.True(f.Window.ExecuteCommand("start-game")); break;
            // Explicit lower-level public-model boundary: proves that readonly
            // manager reference equality alone misses real lifetime retirement.
            case "same-model-reset": f.Match.ResetForNewGame(); Assert.True(f.Match.LifecycleVersion > lifetime); break;
            case "owner-close": f.Window.Close(); break;
        }
        await Settle(650);
        string status = Named<TextBlock>(f.Window, "WeatherLocationStatus").Text ?? "";
        string model = ManualWeatherModel(f), screen = PresPlanRead<string>(f, "_activeScreenKey"), surfaces = ManualWeatherSurfaces(f);
        bool open = PresWindowOpen(f), activation = PresPlanRead<bool>(f, "_outputActivationRequired");
        using var changes = new PresPlanChanges(f.Match);
        await ManualWeatherComplete(pending, fail ? null : ManualWeatherSnapshot(19));
        Assert.Null(bug.GetWeatherSnapshot());
        Assert.DoesNotContain(ManualWeatherOverlay(bug), new[] { "Forecast", "WeatherStats", "RainForecast" });
        Assert.Empty(controlled.Feedback); Assert.Equal(status, Named<TextBlock>(f.Window, "WeatherLocationStatus").Text);
        Assert.Equal(screen, PresPlanRead<string>(f, "_activeScreenKey")); Assert.Equal(surfaces, ManualWeatherSurfaces(f));
        Assert.Equal(open, PresWindowOpen(f)); Assert.Equal(activation, PresPlanRead<bool>(f, "_outputActivationRequired"));
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(0, changes.Count);
        Assert.Null(PresPlanRead<object?>(f, "_weatherService"));
        });
    }

    [AvaloniaFact]
    public async Task ManualWeatherOwnershipLatestRepeatedRequestWinsEvenWhenOlderResponseFinishesLast()
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
        var f = controlled.Fixture;
        var bug = await ManualWeatherPrepare(f, controlled, offAir: false);
        string model = ManualWeatherModel(f); using var changes = new PresPlanChanges(f.Match);
        var older = await ManualWeatherInvoke(f, controlled, "FORECAST");
        var latest = await ManualWeatherInvoke(f, controlled, "FORECAST");
        var latestSnapshot = ManualWeatherSnapshot(23);
        await ManualWeatherComplete(latest, latestSnapshot);
        await Until(() => ManualWeatherOverlay(bug) == "Forecast", "The latest accepted request must air before the older response returns.");
        string status = Named<TextBlock>(f.Window, "WeatherLocationStatus").Text ?? "";
        await ManualWeatherComplete(older, ManualWeatherSnapshot(4));
        Assert.Same(latestSnapshot, bug.GetWeatherSnapshot()); Assert.Equal("Forecast", ManualWeatherOverlay(bug));
        Assert.Equal(status, Named<TextBlock>(f.Window, "WeatherLocationStatus").Text); Assert.Empty(controlled.Feedback);
        Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(0, changes.Count); Assert.True(PresWindowOpen(f));
        });
    }

    [AvaloniaFact]
    public async Task ManualWeatherOwnershipOrdinaryScoreAndClockChangesKeepTheCurrentRequestAlive()
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
        var f = controlled.Fixture;
        var bug = await ManualWeatherPrepare(f, controlled, offAir: false);
        var pending = await ManualWeatherInvoke(f, controlled, "WEATHER"); long lifetime = f.Match.LifecycleVersion;
        var remote = (IRemoteCommandTarget)f.Window;
        remote.HomeBehind(); remote.SetElapsedClock(3, 11);
        Assert.Equal(lifetime, f.Match.LifecycleVersion); Assert.Equal(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(11), f.Match.ElapsedInQuarter);
        string model = ManualWeatherModel(f); using var changes = new PresPlanChanges(f.Match);
        var snapshot = ManualWeatherSnapshot(21); await ManualWeatherComplete(pending, snapshot);
        await Until(() => ManualWeatherOverlay(bug) == "WeatherStats", "A normal accepted score and clock adjustment must not retire the still-current request.");
        Assert.Same(snapshot, bug.GetWeatherSnapshot()); Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(0, changes.Count);
        Assert.Empty(controlled.Feedback); Assert.False(f.Match.ClockRunning); Assert.True(PresWindowOpen(f));
        });
    }
}
