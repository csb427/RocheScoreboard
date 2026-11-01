using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>
/// Integrated Recent quick-add regressions reuse the current queue fixture,
/// real input and assertion helpers. No duplicate setup or motion engine.
/// </summary>
public sealed partial class TrainingQueueCommandRegressionTests
{
    private const string RecentTwinTitle = "Same drill";
    private const string RecentLongTitle = "Long training drill name 123456789012345";
    private static readonly (string Title, int Seconds)[] RecentSeeds =
    [
        (RecentTwinTitle, 45),
        (RecentTwinTitle, 90),
        (RecentLongTitle, 75),
        ("Other easy drill", 60)
    ];

    private static Button RecentChip(Fixture fixture, string title, int seconds) =>
        Assert.Single(Named<WrapPanel>(fixture.Panel, "RecentTimersPanel").Children.OfType<Button>(),
            button => button.Tag is RecentTrainingTimerStorage.Entry entry
                && string.Equals(entry.Title, title, StringComparison.OrdinalIgnoreCase) && entry.Seconds == seconds);

    private static (string? Title, string? Minutes, string? Seconds) RecentRawForm(Fixture fixture) =>
        (Named<TextBox>(fixture.Panel, "NewTitleBox").Text,
            Named<TextBox>(fixture.Panel, "NewMinutesBox").Text,
            Named<TextBox>(fixture.Panel, "NewSecondsBox").Text);

    private static string[] RecentKeys(IEnumerable<RecentTrainingTimerStorage.Entry> entries) => entries
        .Select(entry => entry.Title.ToUpperInvariant() + "\u001f" + entry.Seconds)
        .OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private static void AssertRecentSeedReady(Fixture fixture)
    {
        Assert.Equal(40, RecentLongTitle.Length);
        var entries = RecentTrainingTimerStorage.Load();
        Assert.Equal(RecentSeeds.Length, entries.Count);
        Assert.Equal(RecentSeeds.Length, Named<WrapPanel>(fixture.Panel, "RecentTimersPanel").Children.OfType<Button>().Count());
        Assert.Equal(2, entries.Count(entry => entry.Title == RecentTwinTitle));
        Assert.False(Named<TextBlock>(fixture.Panel, "RecentTimersEmptyText").IsVisible);
    }

    private static void AssertRecentAccepted(Fixture fixture, TrainingTimer[] original, string originalValues,
        string title, int seconds, int appendCount, int structural, int changes,
        TimeSpan before, Stopwatch observation, Draft? draft = null)
    {
        Assert.Equal(original.Length + appendCount, fixture.Session.Timers.Count);
        for (int index = 0; index < original.Length; index++) Assert.Same(original[index], fixture.Session.Timers[index]);
        Assert.Equal(originalValues, Values(original));
        var appended = fixture.Session.Timers.Skip(original.Length).ToArray();
        Assert.Equal(appendCount, appended.Select(timer => timer.Id).Distinct().Count());
        foreach (var timer in appended)
        {
            Assert.DoesNotContain(timer.Id, original.Select(previous => previous.Id));
            Assert.Equal(title, timer.Title);
            Assert.Equal(TimeSpan.FromSeconds(seconds), timer.Duration);
            Assert.Equal(timer.Duration, timer.Remaining);
            Assert.False(timer.HasFinished);
        }
        Assert.Equal(appendCount, structural);
        Assert.Equal(appendCount, changes);
        Assert.Same(fixture.Active, fixture.Session.Active);
        Assert.Equal(0, fixture.Session.ActiveIndex);
        Assert.True(fixture.Session.AutoAdvance);
        AssertRunningProgress(fixture.Session, before, observation);
        if (draft is not null) AssertDraft(fixture.Panel, draft);
        var entries = RecentTrainingTimerStorage.Load();
        Assert.Equal(title, entries[0].Title);
        Assert.Equal(seconds, entries[0].Seconds);
        Assert.Equal(RecentSeeds.Length, entries.Count);
        Assert.Equal(RecentSeeds.Select(seed => seed.Title.ToUpperInvariant() + "\u001f" + seed.Seconds)
            .OrderBy(key => key, StringComparer.Ordinal), RecentKeys(entries));
        Assert.Equal(1, entries.Count(entry => string.Equals(entry.Title, title, StringComparison.OrdinalIgnoreCase)
            && entry.Seconds == seconds));
        Assert.Equal(0, fixture.Exits);
    }

    [AvaloniaTheory]
    [InlineData(980, 780)]
    [InlineData(420, 500)]
    public async Task RecentSpaceRepeatPreservesCurrentChipIdentityRunningClockAndRawCorrectionWithoutFocusRepair(int width, int height)
    {
        await using var fixture = await Fixture.Create(width, height, recentSeeds: RecentSeeds);
        AssertRecentSeedReady(fixture);
        await fixture.OpenDraft();
        fixture.Session.Start();
        var chip = RecentChip(fixture, RecentTwinTitle, 45);
        await TabTo(fixture.Window, chip);
        ActualPoint(fixture.Window, chip);
        var draft = ReadDraft(fixture.Panel);
        var formBefore = RecentRawForm(fixture);
        var original = fixture.Session.Timers.ToArray();
        string originalValues = Values(original);
        string[] keys = RecentKeys(RecentTrainingTimerStorage.Load());
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        int structure = 0, changes = 0;
        void CountStructure() => structure++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.Changed += CountChanges;
        try
        {
            await Activate(fixture.Window, chip, false);
            AssertRecentAccepted(fixture, original, originalValues, RecentTwinTitle, 45, 1,
                structure, changes, before, observation, draft);
            Assert.Equal(keys, RecentKeys(RecentTrainingTimerStorage.Load()));
            Assert.Equal(formBefore, RecentRawForm(fixture));
            var current = RecentChip(fixture, RecentTwinTitle, 45);
            Assert.True(current.IsFocused, "Accepted quick-add must keep focus on its same title+seconds identity.");
            ActualPoint(fixture.Window, current);

            // No Tab/Focus/BringIntoView between activations. This second real
            // Space must repeat quick-add, not hit global Start/Pause or another chip.
            await Activate(fixture.Window, current, false);
            AssertRecentAccepted(fixture, original, originalValues, RecentTwinTitle, 45, 2,
                structure, changes, before, observation, draft);
            var repeated = RecentChip(fixture, RecentTwinTitle, 45);
            Assert.True(repeated.IsFocused);
            Assert.Equal(formBefore, RecentRawForm(fixture));
            ActualPoint(fixture.Window, repeated);
            Assert.Equal(keys, RecentKeys(RecentTrainingTimerStorage.Load()));
            Assert.Equal(formBefore, RecentRawForm(fixture));
        }
        finally { fixture.Session.StructuralChanged -= CountStructure; fixture.Session.Changed -= CountChanges; }
    }

    [AvaloniaTheory]
    [InlineData(980, 780, false)]
    [InlineData(420, 500, true)]
    public async Task RecentPointerQuickAddPreservesExactCapturedEntryAndCurrentOwnedFocus(int width, int height, bool longTitle)
    {
        await using var fixture = await Fixture.Create(width, height, recentSeeds: RecentSeeds);
        AssertRecentSeedReady(fixture);
        await fixture.OpenDraft();
        fixture.Session.Start();
        string title = longTitle ? RecentLongTitle : RecentTwinTitle;
        int seconds = longTitle ? 75 : 90;
        var chip = RecentChip(fixture, title, seconds);
        await TabTo(fixture.Window, chip);
        ActualPoint(fixture.Window, chip);
        var draft = ReadDraft(fixture.Panel);
        var formBefore = RecentRawForm(fixture);
        var original = fixture.Session.Timers.ToArray();
        string originalValues = Values(original);
        string[] keys = RecentKeys(RecentTrainingTimerStorage.Load());
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        int structure = 0, changes = 0;
        void CountStructure() => structure++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.Changed += CountChanges;
        try
        {
            await Activate(fixture.Window, chip, true);
            AssertRecentAccepted(fixture, original, originalValues, title, seconds, 1,
                structure, changes, before, observation, draft);
            var current = RecentChip(fixture, title, seconds);
            Assert.True(current.IsFocused, "Native pointer-owned chip focus must follow the accepted identity.");
            ActualPoint(fixture.Window, current);
            Assert.Equal(keys, RecentKeys(RecentTrainingTimerStorage.Load()));
            Assert.Equal(formBefore, RecentRawForm(fixture));
        }
        finally { fixture.Session.StructuralChanged -= CountStructure; fixture.Session.Changed -= CountChanges; }
    }

    [AvaloniaFact]
    public async Task RecentNamesExposeAddFullStoredTitleAndDurationAndSameTitleDurationsNeverAlias()
    {
        await using var fixture = await Fixture.Create(recentSeeds: RecentSeeds);
        AssertRecentSeedReady(fixture);
        string before = QueueState(fixture.Session);
        string[] originalKeys = RecentKeys(RecentTrainingTimerStorage.Load());
        foreach (var entry in RecentTrainingTimerStorage.Load())
        {
            var chip = RecentChip(fixture, entry.Title, entry.Seconds);
            string name = AutomationProperties.GetName(chip) ?? "";
            Assert.Contains("Add", name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(entry.Title, name, StringComparison.Ordinal);
            Assert.Contains($"{entry.Seconds / 60}:{entry.Seconds % 60:D2}", name, StringComparison.Ordinal);
        }
        Assert.Equal(before, QueueState(fixture.Session));
        var original = fixture.Session.Timers.ToArray();
        string originalValues = Values(original);
        int structure = 0, changes = 0;
        void CountStructure() => structure++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.Changed += CountChanges;
        try
        {
            foreach (int seconds in new[] { 45, 90 })
            {
                var chip = RecentChip(fixture, RecentTwinTitle, seconds);
                await TabTo(fixture.Window, chip);
                ActualPoint(fixture.Window, chip);
                await Activate(fixture.Window, chip, false);
                Assert.Equal(RecentTwinTitle, fixture.Session.Timers[^1].Title);
                Assert.Equal(TimeSpan.FromSeconds(seconds), fixture.Session.Timers[^1].Duration);
                Assert.Equal(seconds, RecentTrainingTimerStorage.Load()[0].Seconds);
            }
            Assert.Equal(2, structure);
            Assert.Equal(2, changes);
            Assert.Equal(original.Length + 2, fixture.Session.Timers.Count);
            Assert.Equal(originalValues, Values(original));
            Assert.Equal(new[] { TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(90) },
                fixture.Session.Timers.Skip(original.Length).Select(timer => timer.Duration));
            Assert.NotEqual(fixture.Session.Timers[^2].Id, fixture.Session.Timers[^1].Id);
            Assert.Same(fixture.Active, fixture.Session.Active);
            Assert.False(fixture.Session.IsRunning);
            Assert.True(fixture.Session.AutoAdvance);
            Assert.Equal(2, RecentTrainingTimerStorage.Load().Count(entry => entry.Title == RecentTwinTitle));
            Assert.Equal(originalKeys, RecentKeys(RecentTrainingTimerStorage.Load()));
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= CountStructure; fixture.Session.Changed -= CountChanges; }
    }

    [AvaloniaTheory]
    [InlineData("rebind")]
    [InlineData("hide_then_show")]
    public async Task RecentAcceptedCompletionCannotReclaimFocusFromAReboundOrNewlyShownScope(string boundary)
    {
        await using var fixture = await Fixture.Create(recentSeeds: RecentSeeds);
        AssertRecentSeedReady(fixture);
        fixture.Session.Start();
        var original = fixture.Session.Timers.ToArray();
        string originalValues = Values(original);
        var replacement = new TrainingSession();
        replacement.AddTimer(new TrainingTimer("Replacement current", TimeSpan.FromSeconds(70)));
        replacement.AddTimer(new TrainingTimer("Replacement next", TimeSpan.FromSeconds(80)));
        string replacementBefore = QueueState(replacement);
        var chip = RecentChip(fixture, RecentTwinTitle, 45);
        await TabTo(fixture.Window, chip);
        ActualPoint(fixture.Window, chip);
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        int callbacks = 0, structure = 0, changes = 0;
        void CountStructure() => structure++;
        void CountChanges() => changes++;
        void Interrupt()
        {
            fixture.Session.StructuralChanged -= Interrupt;
            callbacks++;
            if (boundary == "rebind") fixture.Panel.Bind(replacement);
            else fixture.Panel.IsVisible = false;
            for (int step = 0; step < 40 && !fixture.OtherScope.IsFocused; step++) Key(fixture.Window, PhysicalKey.Tab);
            Assert.True(fixture.OtherScope.IsFocused, "Actual interruption Tab must establish another input before completion.");
            Key(fixture.Window, PhysicalKey.A, RawInputModifiers.Control);
            fixture.Window.KeyTextInput("s r independent draft");
            Assert.Equal("s r independent draft", fixture.OtherScope.Text);
            if (boundary == "hide_then_show") fixture.Panel.IsVisible = true;
        }
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.StructuralChanged += Interrupt;
        fixture.Session.Changed += CountChanges;
        try
        {
            await Activate(fixture.Window, chip, false);
            Assert.Equal(1, callbacks);
            AssertRecentAccepted(fixture, original, originalValues, RecentTwinTitle, 45, 1,
                structure, changes, before, observation);
            Assert.True(fixture.OtherScope.IsFocused, "Accepted old recent action must not restore its retired focus over current input.");
            Assert.Equal("s r independent draft", fixture.OtherScope.Text);
            Assert.Equal(replacementBefore, QueueState(replacement));

            // Supplemental deterministic callback guard, not physical-input
            // evidence: the accepted native Space retired this exact source.
            // Real hit-testing cannot target a detached chip. Delivering its
            // public routed Click must not apply it to the current binding.
            Assert.DoesNotContain(chip, Named<WrapPanel>(fixture.Panel, "RecentTimersPanel").Children);
            string acceptedOldState = QueueState(fixture.Session);
            var acceptedRecents = RecentTrainingTimerStorage.Load()
                .Select(entry => (entry.Title, entry.Seconds, entry.LastUsedUtc)).ToArray();
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Assert.Equal(acceptedOldState, QueueState(fixture.Session));
            Assert.Equal(replacementBefore, QueueState(replacement));
            Assert.Equal(1, structure);
            Assert.Equal(1, changes);
            Assert.Equal(1, callbacks);
            Assert.Equal(acceptedRecents, RecentTrainingTimerStorage.Load()
                .Select(entry => (entry.Title, entry.Seconds, entry.LastUsedUtc)).ToArray());
            Assert.True(fixture.OtherScope.IsFocused);
            Assert.Equal("s r independent draft", fixture.OtherScope.Text);
            AssertRunningProgress(fixture.Session, before, observation);
            Assert.Equal(0, fixture.Exits);
        }
        finally
        {
            fixture.Session.StructuralChanged -= CountStructure;
            fixture.Session.StructuralChanged -= Interrupt;
            fixture.Session.Changed -= CountChanges;
        }
    }

    [AvaloniaFact]
    public async Task RecentRefreshFromManualTitleEnterDoesNotReviveAnEarlierChipFocus()
    {
        await using var fixture = await Fixture.Create(recentSeeds: RecentSeeds);
        AssertRecentSeedReady(fixture);
        await fixture.OpenDraft();
        fixture.Session.Start();
        // Establish a real previous chip focus, then deliberately leave it for
        // the form. Remembering last-used chip is not permission to focus it.
        var priorChip = RecentChip(fixture, RecentTwinTitle, 45);
        await TabTo(fixture.Window, priorChip);
        ActualPoint(fixture.Window, priorChip);
        var title = Named<TextBox>(fixture.Panel, "NewTitleBox");
        var minutes = Named<TextBox>(fixture.Panel, "NewMinutesBox");
        var seconds = Named<TextBox>(fixture.Panel, "NewSecondsBox");
        await TabTo(fixture.Window, title);
        ActualPoint(fixture.Window, title);
        Key(fixture.Window, PhysicalKey.A, RawInputModifiers.Control);
        fixture.Window.KeyTextInput("Manual fresh timer");
        minutes.Text = "1";
        seconds.Text = "7";
        Assert.True(title.IsFocused);
        int caret = title.CaretIndex, selectionStart = title.SelectionStart, selectionEnd = title.SelectionEnd;
        var draft = ReadDraft(fixture.Panel);
        var formBefore = RecentRawForm(fixture);
        var original = fixture.Session.Timers.ToArray();
        string originalValues = Values(original);
        string[] originalKeys = RecentKeys(RecentTrainingTimerStorage.Load());
        var observation = Stopwatch.StartNew();
        TimeSpan before = fixture.Session.ActiveRemaining;
        int structure = 0, changes = 0;
        void CountStructure() => structure++;
        void CountChanges() => changes++;
        fixture.Session.StructuralChanged += CountStructure;
        fixture.Session.Changed += CountChanges;
        try
        {
            Key(fixture.Window, PhysicalKey.Enter);
            await Settle();
            Assert.Equal(1, structure);
            Assert.Equal(1, changes);
            Assert.Equal(original.Length + 1, fixture.Session.Timers.Count);
            Assert.Equal(originalValues, Values(original));
            Assert.Equal("Manual fresh timer", fixture.Session.Timers[^1].Title);
            Assert.Equal(TimeSpan.FromSeconds(67), fixture.Session.Timers[^1].Duration);
            Assert.Same(fixture.Active, fixture.Session.Active);
            Assert.True(fixture.Session.AutoAdvance);
            AssertRunningProgress(fixture.Session, before, observation);
            AssertDraft(fixture.Panel, draft);
            Assert.True(title.IsFocused);
            Assert.Equal("Manual fresh timer", title.Text);
            Assert.Equal("1", minutes.Text);
            Assert.Equal("7", seconds.Text);
            Assert.Equal(formBefore, RecentRawForm(fixture));
            Assert.Equal(caret, title.CaretIndex);
            Assert.Equal(selectionStart, title.SelectionStart);
            Assert.Equal(selectionEnd, title.SelectionEnd);
            var stored = RecentTrainingTimerStorage.Load();
            Assert.Equal(RecentSeeds.Length + 1, stored.Count);
            Assert.Equal("Manual fresh timer", stored[0].Title);
            Assert.Equal(67, stored[0].Seconds);
            Assert.Equal(originalKeys, RecentKeys(stored.Skip(1)));
            Assert.False(RecentChip(fixture, "Manual fresh timer", 67).IsFocused);
            Assert.Equal(0, fixture.Exits);
        }
        finally { fixture.Session.StructuralChanged -= CountStructure; fixture.Session.Changed -= CountChanges; }
    }
}
