using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>
/// Real translated score correction dialogs and their existing caller boundary.
/// These are headless Avalonia regressions; native WPF ownership, focus, automation
/// announcements and the compositor still require separate Windows acceptance.
/// </summary>
public sealed class ScoreEventCorrectionFlowTests
{
    private static async Task Settle(int milliseconds = 80)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(root.FindControl<Control>(name));

    private static void Key(Window window, PhysicalKey key, Window fallback)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        (window.IsVisible ? window : fallback).KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task PointerClick(Window window, Control action)
    {
        action.BringIntoView();
        await Settle();
        AssertInside(window, action);
        var origin = action.TranslatePoint(default, window);
        Assert.NotNull(origin);
        var point = origin.Value + new Vector(action.Bounds.Width / 2, action.Bounds.Height / 2);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await Settle();
    }

    private static void AssertInside(Window window, Control action)
    {
        Assert.True(action.IsEffectivelyVisible, action.Name + " must remain visible");
        Assert.True(action.Bounds.Width > 0 && action.Bounds.Height > 0,
            action.Name + " must retain a usable layout area");
        var origin = action.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(origin.Value, action.Bounds.Size)),
            action.Name + " must be reachable inside the modal viewport");
    }

    private static ToggleButton Choice(ScoreEventEditorWindow editor, string name)
    {
        // RadioButton derives from ToggleButton. Require a real selected state
        // and keyboard action, rather than a focusable decorative Border.
        var choice = Named<ToggleButton>(editor, name);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(choice)),
            name + " needs an accessible choice name");
        Assert.True(choice.Focusable, name + " must accept keyboard focus");
        return choice;
    }

    private static void AssertChoices(ScoreEventEditorWindow editor, TeamSide team, ScoreType type)
    {
        Assert.Equal(team, editor.SelectedTeam);
        Assert.Equal(type, editor.SelectedType);
        Assert.Equal(team == TeamSide.Home, Choice(editor, "HomeTeamBtn").IsChecked);
        Assert.Equal(team == TeamSide.Away, Choice(editor, "AwayTeamBtn").IsChecked);
        Assert.Equal(type == ScoreType.Goal, Choice(editor, "GoalBtn").IsChecked);
        Assert.Equal(type == ScoreType.Behind, Choice(editor, "BehindBtn").IsChecked);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool? previousMotion = OperatorMotion.AnimationOverride;
        internal readonly ScoreEvent Event = new()
        {
            Quarter = 2,
            GameTime = TimeSpan.FromSeconds(135),
            Team = TeamSide.Home,
            Type = ScoreType.Goal,
            HomeGoals = 2,
            HomeBehinds = 3,
            AwayGoals = 1,
            AwayBehinds = 2
        };
        internal readonly Button Source = new() { Content = "Edit this score" };
        internal readonly NativeWindow Owner;
        internal readonly ScoreEventEditorWindow Editor;
        internal readonly string Before;
        internal readonly Task<bool?> Completion;
        internal int Closed;

        internal Fixture(bool animate = false, bool compact = false, bool addMode = false)
        {
            OperatorMotion.AnimationOverride = animate;
            Before = JsonSerializer.Serialize(Event);
            Owner = new NativeWindow { Width = 760, Height = 560, Content = Source };
            // Establish the opener before constructing the modal. A focus
            // assertion only after dismissal would conflate fixture setup
            // with the production return-focus contract.
            NativeKeyboard.EnsureInitialized();
            Owner.Show();
            Owner.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Source.Focus(NavigationMethod.Tab));
            Assert.True(Source.IsFocused, "The fixture opener must own focus before the modal opens");
            Editor = addMode
                ? new ScoreEventEditorWindow("A deliberately long home club name",
                    "A deliberately long away club name", 3, TimeSpan.FromSeconds(725))
                : new ScoreEventEditorWindow(Event,
                    "A deliberately long home club name", "A deliberately long away club name");
            Editor.Owner = Owner;
            if (compact)
            {
                Editor.SizeToContent = SizeToContent.Manual;
                Editor.Height = 320;
                Editor.MaxHeight = 320;
            }
            Editor.Closed += (_, _) => Closed++;
            Completion = ((Window)Editor).ShowDialog<bool?>(Owner);
        }

        internal void AssertOriginalUnchanged() => Assert.Equal(Before, JsonSerializer.Serialize(Event));

        public void Dispose()
        {
            foreach (var child in Editor.OwnedWindows.ToArray()) child.Close();
            if (Editor.IsVisible) Editor.Close();
            foreach (var child in Owner.OwnedWindows.ToArray()) child.Close();
            Owner.Close();
            OperatorMotion.AnimationOverride = previousMotion;
        }
    }

    private static async Task AssertRetainedActionsAreInert(Fixture f, Button save, Button delete,
        Button cancel, ComboBox quarter, TextBox minutes, TextBox seconds)
    {
        var result = f.Editor.Result;
        int selectedQuarter = f.Editor.SelectedQuarter;
        TimeSpan selectedTime = f.Editor.SelectedGameTime;
        TeamSide selectedTeam = f.Editor.SelectedTeam;
        ScoreType selectedType = f.Editor.SelectedType;
        // Retained controls can receive a late routed callback even after their
        // window closes. A changed valid draft makes accidental reapplication
        // observable rather than merely reassigning the same result values.
        quarter.SelectedIndex = 0;
        minutes.Text = "31";
        seconds.Text = "59";
        int unexpectedNotices = 0;
        var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        closer.Tick += (_, _) =>
        {
            var notices = f.Editor.OwnedWindows.Concat(f.Owner.OwnedWindows)
                .Concat(NativeDesktop.MainWindow?.OwnedWindows ?? Array.Empty<Window>())
                .Where(window => !ReferenceEquals(window, f.Editor)).Distinct().ToArray();
            foreach (var notice in notices) { unexpectedNotices++; notice.Close(); }
        };
        closer.Start();
        try
        {
            foreach (var action in new[] { save, delete, cancel, save, cancel, delete })
            {
                Assert.Null(Record.Exception(() => Click(action)));
                Assert.Equal(result, f.Editor.Result);
                Assert.Equal(selectedQuarter, f.Editor.SelectedQuarter);
                Assert.Equal(selectedTime, f.Editor.SelectedGameTime);
                Assert.Equal(selectedTeam, f.Editor.SelectedTeam);
                Assert.Equal(selectedType, f.Editor.SelectedType);
            }
            await Settle();
        }
        finally { closer.Stop(); }
        Assert.Equal(0, unexpectedNotices);
        Assert.Equal(1, f.Closed);
        Assert.True(f.Completion.IsCompleted);
        Assert.False(f.Editor.IsVisible);
        Assert.Empty(f.Editor.OwnedWindows);
        Assert.Empty(f.Owner.OwnedWindows);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaTheory]
    [InlineData(true, true, 3, 725)]
    [InlineData(false, true, 1, 725)]
    [InlineData(true, false, 3, 587)]
    [InlineData(false, false, 1, 587)]
    public async Task AddModeDefaultsAndIndependentTimingOverridesReturnTheChosenValues(
        bool currentQuarter, bool currentTime, int expectedQuarter, int expectedSeconds)
    {
        using var f = new Fixture(addMode: true);
        await Settle();
        var useQuarter = Named<CheckBox>(f.Editor, "UseCurrentQuarterBox");
        var useTime = Named<CheckBox>(f.Editor, "UseCurrentTimeBox");
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        var minutes = Named<TextBox>(f.Editor, "MinutesBox");
        var seconds = Named<TextBox>(f.Editor, "SecondsBox");
        Assert.True(useQuarter.IsEffectivelyVisible);
        Assert.True(useTime.IsEffectivelyVisible);
        Assert.True(useQuarter.IsChecked);
        Assert.True(useTime.IsChecked);
        Assert.False(quarter.IsEffectivelyEnabled);
        Assert.False(minutes.IsEffectivelyEnabled);
        Assert.False(seconds.IsEffectivelyEnabled);
        Assert.False(Named<Button>(f.Editor, "DeleteButton").IsEffectivelyVisible);
        Assert.Equal(3, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(725), f.Editor.SelectedGameTime);
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Goal);

        useQuarter.IsChecked = false;
        useTime.IsChecked = false;
        quarter.SelectedIndex = 0;
        minutes.Text = "09";
        seconds.Text = "47";
        useQuarter.IsChecked = currentQuarter;
        useTime.IsChecked = currentTime;
        Assert.Equal(!currentQuarter, quarter.IsEffectivelyEnabled);
        Assert.Equal(!currentTime, minutes.IsEffectivelyEnabled);
        Assert.Equal(!currentTime, seconds.IsEffectivelyEnabled);
        Assert.False(f.Completion.IsCompleted);
        Click(Named<Button>(f.Editor, "SaveButton"));
        await Settle();
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Saved, f.Editor.Result);
        Assert.Equal(expectedQuarter, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), f.Editor.SelectedGameTime);
        Assert.Equal(TeamSide.Home, f.Editor.SelectedTeam);
        Assert.Equal(ScoreType.Goal, f.Editor.SelectedType);
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(1, f.Closed);
    }

    [AvaloniaFact]
    public async Task RejectedAddModeCustomTimingRetainsCapturedResultAndCanBeRepaired()
    {
        using var f = new Fixture(addMode: true);
        await Settle();
        Named<CheckBox>(f.Editor, "UseCurrentQuarterBox").IsChecked = false;
        Named<CheckBox>(f.Editor, "UseCurrentTimeBox").IsChecked = false;
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        var minutes = Named<TextBox>(f.Editor, "MinutesBox");
        var seconds = Named<TextBox>(f.Editor, "SecondsBox");
        quarter.SelectedIndex = 1;
        minutes.Text = "09";
        seconds.Text = "60";
        Click(Named<Button>(f.Editor, "SaveButton"));
        await Settle();
        Assert.True(f.Editor.IsVisible);
        Assert.False(f.Completion.IsCompleted);
        Assert.Equal(0, f.Closed);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(3, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(725), f.Editor.SelectedGameTime);
        Assert.Equal(1, quarter.SelectedIndex);
        Assert.Equal("09", minutes.Text);
        Assert.Equal("60", seconds.Text);
        Assert.True(seconds.IsFocused);
        var validation = Named<TextBlock>(f.Editor, "ValidationText");
        Assert.True(validation.IsEffectivelyVisible);
        Assert.Contains("second", (validation.Text ?? string.Empty).ToLowerInvariant());
        Assert.Empty(f.Editor.OwnedWindows);
        seconds.Text = "59";
        Click(Named<Button>(f.Editor, "SaveButton"));
        await Settle();
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Saved, f.Editor.Result);
        Assert.Equal(2, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(599), f.Editor.SelectedGameTime);
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(1, f.Closed);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetainedSaveDeleteAndCancelCallbacksCannotReplaceACompletedResult(bool saveResult)
    {
        using var f = new Fixture();
        await Settle();
        var save = Named<Button>(f.Editor, "SaveButton");
        var delete = Named<Button>(f.Editor, "DeleteButton");
        var cancel = Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel);
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        var minutes = Named<TextBox>(f.Editor, "MinutesBox");
        var seconds = Named<TextBox>(f.Editor, "SecondsBox");
        quarter.SelectedIndex = 2;
        minutes.Text = "09";
        seconds.Text = "47";
        Click(saveResult ? save : cancel);
        await Settle();
        Assert.Equal(saveResult ? ScoreEventEditorWindow.EditorResult.Saved
            : ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(saveResult ? 3 : 2, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(saveResult ? 587 : 135), f.Editor.SelectedGameTime);
        await AssertRetainedActionsAreInert(f, save, delete, cancel, quarter, minutes, seconds);
    }

    [AvaloniaFact]
    public async Task ExistingEventTimeAndQuarterAreEditableAndSaveReturnsTheExactDraft()
    {
        using var f = new Fixture();
        await Settle();
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        var minutes = Named<TextBox>(f.Editor, "MinutesBox");
        var seconds = Named<TextBox>(f.Editor, "SecondsBox");
        Assert.True(quarter.IsEffectivelyEnabled);
        Assert.True(minutes.IsEffectivelyEnabled,
            "Editing an existing score must not inherit the hidden add-mode current-time toggle");
        Assert.True(seconds.IsEffectivelyEnabled);
        Assert.False(minutes.IsReadOnly);
        Assert.False(seconds.IsReadOnly);
        Assert.Equal("02", minutes.Text);
        Assert.Equal("15", seconds.Text);
        quarter.SelectedIndex = 2;
        minutes.Text = "09";
        seconds.Text = "47";
        f.AssertOriginalUnchanged();
        Click(Named<Button>(f.Editor, "SaveButton"));
        await Settle();
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Saved, f.Editor.Result);
        Assert.Equal(3, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(587), f.Editor.SelectedGameTime);
        Assert.Equal(TeamSide.Home, f.Editor.SelectedTeam);
        Assert.Equal(ScoreType.Goal, f.Editor.SelectedType);
        Assert.Equal(1, f.Closed);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TeamAndTypeChoicesExposeSelectedStateAndAcceptPointerOrKeyboard(bool keyboard)
    {
        using var f = new Fixture();
        await Settle();
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Goal);
        foreach (var step in new[]
        {
            ("AwayTeamBtn", TeamSide.Away, ScoreType.Goal),
            ("BehindBtn", TeamSide.Away, ScoreType.Behind),
            ("HomeTeamBtn", TeamSide.Home, ScoreType.Behind),
            ("GoalBtn", TeamSide.Home, ScoreType.Goal)
        })
        {
            var action = Choice(f.Editor, step.Item1);
            if (keyboard)
            {
                action.BringIntoView();
                Assert.True(action.Focus(NavigationMethod.Tab));
                await Settle();
                Assert.True(action.IsFocused);
                Key(f.Editor, PhysicalKey.Space, f.Owner);
                await Settle();
            }
            else await PointerClick(f.Editor, action);
            AssertChoices(f.Editor, step.Item2, step.Item3);
            Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
            Assert.False(f.Completion.IsCompleted);
            f.AssertOriginalUnchanged();
        }
    }

    [AvaloniaTheory]
    [InlineData("", "15", "MinutesBox", "minute")]
    [InlineData("x", "15", "MinutesBox", "minute")]
    [InlineData("-1", "15", "MinutesBox", "minute")]
    [InlineData("02", "60", "SecondsBox", "second")]
    [InlineData("02", "-1", "SecondsBox", "second")]
    [InlineData("02", "x", "SecondsBox", "second")]
    public async Task InvalidApplyKeepsTheDraftOpenWithoutChangingTheResultAndExplainsTheField(
        string minutes, string seconds, string invalidField, string explanation)
    {
        using var f = new Fixture(compact: true);
        await Settle();
        Named<ComboBox>(f.Editor, "QuarterBox").SelectedIndex = 3;
        Named<TextBox>(f.Editor, "MinutesBox").Text = minutes;
        Named<TextBox>(f.Editor, "SecondsBox").Text = seconds;

        // A regression to a nested MessageBox must fail without hanging the
        // runner. The production contract is a reachable inline explanation.
        int nestedNotices = 0;
        var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        closer.Tick += (_, _) =>
        {
            var notices = f.Editor.OwnedWindows.Concat(f.Owner.OwnedWindows)
                .Concat(NativeDesktop.MainWindow?.OwnedWindows ?? Array.Empty<Window>())
                .Where(window => !ReferenceEquals(window, f.Editor)).Distinct().ToArray();
            foreach (var notice in notices) { nestedNotices++; notice.Close(); }
        };
        Exception? error;
        closer.Start();
        try { error = Record.Exception(() => Click(Named<Button>(f.Editor, "SaveButton"))); }
        finally { closer.Stop(); }

        Assert.Null(error);
        await Settle();
        Assert.Equal(0, nestedNotices);
        Assert.True(f.Editor.IsVisible);
        Assert.False(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(2, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(135), f.Editor.SelectedGameTime);
        f.AssertOriginalUnchanged();
        var field = Named<TextBox>(f.Editor, invalidField);
        Assert.Equal(invalidField == "MinutesBox" ? minutes : seconds, field.Text);
        Assert.True(field.IsFocused, "Rejected Apply must direct the operator to the failing input");
        var validation = Named<TextBlock>(f.Editor, "ValidationText");
        Assert.Contains(explanation, (validation.Text ?? string.Empty).ToLowerInvariant());
        AssertInside(f.Editor, validation);
        var cancel = Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(),
            button => button.IsCancel && button.IsEffectivelyVisible);
        cancel.BringIntoView();
        cancel.Focus(NavigationMethod.Tab);
        await Settle();
        AssertInside(f.Editor, cancel);
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("close")]
    public async Task DiscardingAChangedDraftLeavesTheOriginalUntouchedAndReturnsToItsOwner(string action)
    {
        using var f = new Fixture();
        await Settle();
        Named<ComboBox>(f.Editor, "QuarterBox").SelectedIndex = 3;
        var minutes = Named<TextBox>(f.Editor, "MinutesBox");
        minutes.Text = "19";
        Named<TextBox>(f.Editor, "SecondsBox").Text = "59";
        minutes.Focus(NavigationMethod.Tab);
        if (action == "cancel")
            Click(Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel));
        else if (action == "escape") Key(f.Editor, PhysicalKey.Escape, f.Owner);
        else f.Editor.Close();
        await Settle();
        Assert.True(f.Completion.IsCompleted);
        Assert.False(f.Editor.IsVisible);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(1, f.Closed);
        f.AssertOriginalUnchanged();
        Assert.True(f.Owner.IsEnabled);
        Assert.Empty(f.Owner.OwnedWindows);
        Assert.True(f.Source.IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteUsesAnOwnedConfirmationWhoseDefaultPreservesTheScore(bool confirmDelete)
    {
        using var f = new Fixture();
        await Settle();
        var retainedSave = Named<Button>(f.Editor, "SaveButton");
        var retainedDelete = Named<Button>(f.Editor, "DeleteButton");
        var retainedCancel = Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel);
        var retainedQuarter = Named<ComboBox>(f.Editor, "QuarterBox");
        var retainedMinutes = Named<TextBox>(f.Editor, "MinutesBox");
        var retainedSeconds = Named<TextBox>(f.Editor, "SecondsBox");
        Exception? responseError = null;
        bool observed = false;
        using var response = DispatcherTimer.RunOnce(() =>
        {
            Window? confirmation = null;
            try
            {
                confirmation = Assert.Single(f.Editor.OwnedWindows);
                observed = true;
                Assert.Same(f.Editor, confirmation.Owner);
                var buttons = confirmation.GetVisualDescendants().OfType<Button>().ToArray();
                var cancel = Assert.Single(buttons, button =>
                    Equals(button.Content, "Cancel") || Equals(button.Content, "No"));
                var delete = Assert.Single(buttons, button =>
                    Equals(button.Content, "Delete score event") || Equals(button.Content, "Yes"));
                Assert.True(cancel.IsDefault, "Enter must preserve the event until deletion is explicit");
                Assert.False(delete.IsDefault);
                Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
                f.AssertOriginalUnchanged();
                if (confirmDelete) Click(delete);
                else Key(confirmation, PhysicalKey.Enter, f.Owner);
            }
            catch (Exception error)
            {
                responseError = error;
                if (confirmation?.IsVisible == true) confirmation.Close();
                foreach (var notice in f.Owner.OwnedWindows.Where(window => window != f.Editor).ToArray())
                    notice.Close();
                foreach (var notice in NativeDesktop.MainWindow?.OwnedWindows.ToArray() ?? Array.Empty<Window>())
                    if (notice != f.Owner && notice != f.Editor) notice.Close();
            }
        }, TimeSpan.FromMilliseconds(100));
        var clickError = Record.Exception(() => Click(Named<Button>(f.Editor, "DeleteButton")));
        Assert.Null(clickError);
        await Settle(150);
        Assert.True(observed, "The real Delete handler must open an editor-owned confirmation");
        Assert.Null(responseError);
        Assert.Empty(f.Editor.OwnedWindows);
        Assert.Equal(confirmDelete ? ScoreEventEditorWindow.EditorResult.Deleted
            : ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(confirmDelete, f.Completion.IsCompleted);
        Assert.Equal(confirmDelete ? 1 : 0, f.Closed);
        f.AssertOriginalUnchanged();
        if (!confirmDelete)
        {
            Assert.True(f.Editor.IsVisible);
            Assert.True(Named<Button>(f.Editor, "DeleteButton").IsFocused);
        }
        else await AssertRetainedActionsAreInert(f, retainedSave, retainedDelete, retainedCancel,
            retainedQuarter, retainedMinutes, retainedSeconds);
    }

    [AvaloniaFact]
    public void SavingThroughTheActualHistoryHandlerRecalculatesTheChosenEventExactlyOnce()
    {
        var app = (App)Application.Current!;
        bool previousSelfTest = app.IsSelfTestMode;
        bool? previousMotion = OperatorMotion.AnimationOverride;
        app.IsSelfTestMode = true;
        OperatorMotion.AnimationOverride = false;
        Roche_Scoreboard.MainWindow? window = null;
        try
        {
            var owner = new Roche_Scoreboard.MainWindow();
            window = owner;
            owner.Show();
            var match = (MatchManager)typeof(Roche_Scoreboard.MainWindow)
                .GetField("_match", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            match.AddGoal(TeamSide.Home);
            match.AddGoal(TeamSide.Away);
            match.AddBehind(TeamSide.Home);
            var target = match.Events[^1];
            var otherEvents = match.Events.Take(2).Select(score => (score.Team, score.Type, score.GameTime)).ToArray();
            string before = JsonSerializer.Serialize(match.ToState());
            int notifications = 0;
            match.MatchChanged += () => notifications++;
            Exception? callbackError = null;
            bool opened = false;
            Dispatcher.UIThread.Post(async () =>
            {
                ScoreEventEditorWindow? editor = null;
                try
                {
                    editor = Assert.Single(owner.OwnedWindows.OfType<ScoreEventEditorWindow>());
                    opened = true;
                    Assert.Same(owner, editor.Owner);
                    await Settle();
                    Assert.True(Named<TextBox>(editor, "MinutesBox").IsEffectivelyEnabled);
                    await PointerClick(editor, Choice(editor, "AwayTeamBtn"));
                    var goal = Choice(editor, "GoalBtn");
                    goal.BringIntoView();
                    Assert.True(goal.Focus(NavigationMethod.Tab));
                    Key(editor, PhysicalKey.Space, owner);
                    Named<ComboBox>(editor, "QuarterBox").SelectedIndex = 0;
                    Named<TextBox>(editor, "MinutesBox").Text = "09";
                    Named<TextBox>(editor, "SecondsBox").Text = "47";
                    Assert.Equal(before, JsonSerializer.Serialize(match.ToState()));
                    Click(Named<Button>(editor, "SaveButton"));
                }
                catch (Exception error) { callbackError = error; if (editor?.IsVisible == true) editor.Close(); }
            });
            typeof(Roche_Scoreboard.MainWindow)
                .GetMethod("ScoreLogBar_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(owner, new object?[] { new Border { Tag = target }, null });
            Assert.True(opened);
            Assert.Null(callbackError);
            Assert.Equal(1, notifications);
            Assert.Equal(6, match.HomeTotal);
            Assert.Equal(12, match.AwayTotal);
            Assert.Equal(3, match.Events.Count);
            Assert.Equal(TeamSide.Away, target.Team);
            Assert.Equal(ScoreType.Goal, target.Type);
            Assert.Equal(1, target.Quarter);
            Assert.Equal(TimeSpan.FromSeconds(587), target.GameTime);
            Assert.Equal(otherEvents, match.Events.Take(2).Select(score => (score.Team, score.Type, score.GameTime)).ToArray());
            Assert.Empty(match.ValidateScoreSequence());
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            if (window is not null)
            {
                foreach (var child in window.OwnedWindows.ToArray()) child.Close();
                window.Close();
            }
            app.IsSelfTestMode = previousSelfTest;
            OperatorMotion.AnimationOverride = previousMotion;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactCorrectionKeepsSaveAndCancelReachableWithoutChangingTheDraft(bool animate)
    {
        using var f = new Fixture(animate, compact: true);
        await Settle();
        Named<TextBox>(f.Editor, "MinutesBox").Text = "08";
        Named<TextBox>(f.Editor, "SecondsBox").Text = "36";
        var save = Named<Button>(f.Editor, "SaveButton");
        var cancel = Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel);
        foreach (var action in new[] { save, cancel })
        {
            action.BringIntoView();
            Assert.True(action.Focus(NavigationMethod.Tab));
            await Settle();
            AssertInside(f.Editor, action);
            Assert.True(action.IsFocused);
            Assert.Equal("08", Named<TextBox>(f.Editor, "MinutesBox").Text);
            Assert.Equal("36", Named<TextBox>(f.Editor, "SecondsBox").Text);
            Assert.False(f.Completion.IsCompleted);
            f.AssertOriginalUnchanged();
        }
        Key(f.Editor, PhysicalKey.Escape, f.Owner);
        await Settle();
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        f.AssertOriginalUnchanged();
    }
    private static void CaptureCorrection(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, "afl-score-correction-" + name + ".png"));
    }

    private static OperatorSymbol ChoiceSymbol(ScoreEventEditorWindow editor, ToggleButton choice) =>
        Named<OperatorSymbol>(editor, choice.Name switch
        {
            "HomeTeamBtn" => "HomeTeamSymbol",
            "AwayTeamBtn" => "AwayTeamSymbol",
            "GoalBtn" => "GoalScoreSymbol",
            "BehindBtn" => "BehindScoreSymbol",
            _ => throw new InvalidOperationException("Unknown score correction choice")
        });

    private static Border SelectionMark(ToggleButton choice) =>
        Assert.Single(choice.GetVisualDescendants().OfType<Border>(), border => border.Name == "ScoreSelectionMark");

    private static (Rect Bounds, Point? Label, Point? Icon) ChoiceGeometry(
        ScoreEventEditorWindow editor, ToggleButton choice)
    {
        var label = choice.GetVisualDescendants().OfType<TextBlock>().First();
        return (choice.Bounds, label.TranslatePoint(default, choice),
            ChoiceSymbol(editor, choice).TranslatePoint(default, choice));
    }

    private static void AssertStationaryChoice(ScoreEventEditorWindow editor, ToggleButton choice,
        (Rect Bounds, Point? Label, Point? Icon) expected)
    {
        Assert.Equal(expected, ChoiceGeometry(editor, choice));
        Assert.Equal(Matrix.Identity, choice.RenderTransform?.Value ?? Matrix.Identity);
        Assert.Null(ChoiceSymbol(editor, choice).RenderTransform);
        var mark = SelectionMark(choice);
        Assert.Equal(choice.IsChecked == true, mark.IsVisible);
        Assert.Equal(1, mark.Opacity);
        Assert.Equal(Matrix.Identity, mark.RenderTransform?.Value ?? Matrix.Identity);
    }

    private sealed class MotionPreferences : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled;
        private readonly bool reduced = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly byte[]? stored = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        internal MotionPreferences() => OperatorAppearance.Configure(true, false);

        public void Dispose()
        {
            OperatorAppearance.Configure(glass, reduced);
            OperatorAppearance.ConfigureMotionStyle(style);
            if (stored is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, stored);
        }
    }

    [AvaloniaFact]
    public async Task PointerChoicePartsReverseWithoutMovingTheRadioLabelIconOrSteadyCheck()
    {
        using var f = new Fixture(animate: true);
        await Settle(750);
        var choices = new[] { "HomeTeamBtn", "AwayTeamBtn", "GoalBtn", "BehindBtn" }
            .Select(name => Choice(f.Editor, name)).ToArray();
        var geometry = choices.ToDictionary(choice => choice, choice => ChoiceGeometry(f.Editor, choice));
        foreach (var choice in choices)
        {
            Assert.True(OperatorChoiceMotion.GetEnabled(choice));
            Assert.False(OperatorControlMotion.GetEnabled(choice));
            Assert.False(CricketActionMotion.GetEnabled(choice));
            var lens = Assert.Single(choice.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            Assert.Equal(.5, lens.OriginX);
            Assert.Equal(OperatorChoiceMotion.GetProgress(choice), lens.Progress, 4);
        }
        Assert.Equal("teams", ChoiceSymbol(f.Editor, choices[0]).Kind);
        Assert.Equal("teams", ChoiceSymbol(f.Editor, choices[1]).Kind);
        Assert.Equal("sport-afl", ChoiceSymbol(f.Editor, choices[2]).Kind);
        Assert.Equal("sport-afl", ChoiceSymbol(f.Editor, choices[3]).Kind);
        CaptureCorrection(f.Editor, "parts-before");

        // Hold the pointer down long enough to expose an inherited whole-card
        // press. Label/icon positions are measured relative to the radio, so
        // outer scrolling cannot masquerade as a moving template.
        var away = choices[1];
        away.BringIntoView();
        await Settle();
        var origin = away.TranslatePoint(default, f.Editor);
        Assert.NotNull(origin);
        var point = origin.Value + new Vector(away.Bounds.Width / 2, away.Bounds.Height / 2);
        f.Editor.MouseDown(point, MouseButton.Left);
        await Settle(65);
        foreach (var choice in choices) AssertStationaryChoice(f.Editor, choice, geometry[choice]);
        CaptureCorrection(f.Editor, "parts-held");
        f.Editor.MouseUp(point, MouseButton.Left);
        await Settle(65);
        AssertChoices(f.Editor, TeamSide.Away, ScoreType.Goal);
        Assert.True(ChoiceSymbol(f.Editor, away).IsActive);
        Assert.False(ChoiceSymbol(f.Editor, choices[0]).IsActive);
        Assert.InRange(OperatorChoiceMotion.GetProgress(away), .001, .999);
        CaptureCorrection(f.Editor, "parts-away-arriving");
        await PointerClick(f.Editor, choices[0]);
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Goal);
        Assert.True(ChoiceSymbol(f.Editor, choices[0]).IsActive);
        Assert.False(ChoiceSymbol(f.Editor, away).IsActive);
        CaptureCorrection(f.Editor, "parts-home-reversal");
        await PointerClick(f.Editor, choices[3]);
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Behind);
        Assert.True(ChoiceSymbol(f.Editor, choices[3]).IsActive);
        Assert.False(ChoiceSymbol(f.Editor, choices[2]).IsActive);
        Assert.InRange(OperatorChoiceMotion.GetProgress(choices[3]), .001, .999);
        CaptureCorrection(f.Editor, "parts-behind-arriving");
        await PointerClick(f.Editor, choices[2]);
        for (int frame = 0; frame < 6; frame++)
        {
            foreach (var choice in choices) AssertStationaryChoice(f.Editor, choice, geometry[choice]);
            CaptureCorrection(f.Editor, "parts-reversal-" + frame.ToString("D2"));
            await Settle(65);
        }
        await Settle(650);
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Goal);
        foreach (var choice in choices)
        {
            Assert.Equal(choice.IsChecked == true ? 1 : 0, OperatorChoiceMotion.GetProgress(choice));
            var icon = ChoiceSymbol(f.Editor, choice);
            Assert.Equal(choice.IsChecked == true, icon.IsActive);
            Assert.Equal(choice.IsChecked == true ? 1 : 0, icon.FillProgress);
            Assert.False(icon.IsAnimating);
            AssertStationaryChoice(f.Editor, choice, geometry[choice]);
        }
        Assert.False(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaFact]
    public async Task LiveReducedMotionSettlesTheLatestChoicePartsWithoutChangingTheDraft()
    {
        using var preferences = new MotionPreferences();
        using var f = new Fixture(animate: true);
        OperatorMotion.AnimationOverride = null;
        Assert.True(OperatorMotion.Enabled, "The live reduction fixture must begin with motion enabled");
        await Settle(750);
        var away = Choice(f.Editor, "AwayTeamBtn");
        // The pointer helper's post-release delay can consume the whole
        // arrival on a loaded host. Observe one actual public nonendpoint after
        // the original gesture returns instead of sleeping before Reduce.
        away.BringIntoView();
        await Settle();
        AssertInside(f.Editor, away);
        bool gestureReturned = false, observationApplied = false, observerRetired = false, reductionQueued = false;
        DispatcherOperation? reductionOperation = null;
        Exception? observationError = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        (string Name, bool? Checked, double Progress, bool IconActive, double IconFill, bool IconAnimating)[] SnapshotChoices() =>
            new[] { "HomeTeamBtn", "AwayTeamBtn", "GoalBtn", "BehindBtn" }.Select(name =>
            {
                var choice = Choice(f.Editor, name);
                var symbol = ChoiceSymbol(f.Editor, choice);
                return (name, choice.IsChecked, OperatorChoiceMotion.GetProgress(choice),
                    symbol.IsActive, symbol.FillProgress, symbol.IsAnimating);
            }).ToArray();
        (string Name, bool? Checked, double Progress, bool IconActive, double IconFill, bool IconAnimating)[]? before = null, after = null;
        string? draftBeforeReduce = null, draftAfterReduce = null;
        double observedMilliseconds = 0;
        string Draft() => JsonSerializer.Serialize(new
        {
            f.Editor.SelectedTeam, f.Editor.SelectedType, f.Editor.SelectedQuarter,
            f.Editor.SelectedGameTime, f.Editor.Result,
            minutes = Named<TextBox>(f.Editor, "MinutesBox").Text,
            seconds = Named<TextBox>(f.Editor, "SecondsBox").Text,
            source = JsonSerializer.Serialize(f.Event), completed = f.Completion.IsCompleted
        });
        void RetireObserver()
        {
            if (observerRetired) return;
            observerRetired = true;
            away.PropertyChanged -= ObserveProperty;
        }
        void ApplyObservedReduction()
        {
            try
            {
                // This queued dispatcher action runs after the property
                // notification's animation lease update has returned. Require
                // a current partial pose again; never accept an elapsed clock.
                Assert.True(gestureReturned && observerRetired && reductionQueued);
                Assert.InRange(OperatorChoiceMotion.GetProgress(away), .001, .999);
                Assert.True(away.IsLoaded && away.IsEffectivelyVisible && away.IsEffectivelyEnabled);
                Assert.Same(f.Editor, TopLevel.GetTopLevel(away));
                AssertInside(f.Editor, away);
                AssertChoices(f.Editor, TeamSide.Away, ScoreType.Goal);
                Assert.True(OperatorMotion.Enabled);
                Assert.Null(OperatorMotion.AnimationOverride);
                f.AssertOriginalUnchanged();
                before = SnapshotChoices();
                Assert.InRange(before.Single(item => item.Name == "AwayTeamBtn").Progress, .001, .999);
                draftBeforeReduce = Draft();
                observedMilliseconds = elapsed.Elapsed.TotalMilliseconds;
                OperatorAppearance.Configure(true, true);
                // Snapshot the immediate product result before any I/O, render
                // capture or subsequent UI job can finish a surviving clock.
                after = SnapshotChoices();
                draftAfterReduce = Draft();
                observationApplied = true;
            }
            catch (Exception error) { observationError = error; RetireObserver(); }
        }
        void Observe()
        {
            if (observerRetired || !gestureReturned || reductionQueued) return;
            double progress = OperatorChoiceMotion.GetProgress(away);
            if (progress < .001 || progress > .999) return;
            // Queue exactly once, instead of injecting a preference change into
            // the synchronous ProgressProperty notification/lease publication.
            // A late queued endpoint fails the prerequisite; it is not retried.
            reductionQueued = true;
            RetireObserver();
            reductionOperation = Dispatcher.UIThread.InvokeAsync(ApplyObservedReduction, DispatcherPriority.Normal);
        }
        void ObserveProperty(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == OperatorChoiceMotion.ProgressProperty) Observe();
        }
        away.PropertyChanged += ObserveProperty;
        try
        {
            var origin = away.TranslatePoint(default, f.Editor);
            Assert.NotNull(origin);
            var point = origin.Value + new Vector(away.Bounds.Width / 2, away.Bounds.Height / 2);
            elapsed.Restart();
            f.Editor.MouseDown(point, MouseButton.Left);
            f.Editor.MouseUp(point, MouseButton.Left);
            gestureReturned = true;
            Observe();
            // Bounded readiness for this single gesture, never another click
            // or an artificial clock advance. A missed live pose is a fixture
            // prerequisite failure, not evidence about the Reduce oracle.
            for (int i = 0; i < 80 && !observationApplied && observationError is null; i++) await Settle(5);
        }
        finally { RetireObserver(); reductionOperation?.Abort(); }
        if (observationError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observationError).Throw();
        Assert.True(observationApplied, "One genuine mounted partial selection must precede Reduce; a missed pose is a prerequisite failure.");
        Assert.True(observerRetired);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(draftBeforeReduce, draftAfterReduce);
        string? captures = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            File.WriteAllText(Path.Combine(captures, "score-correction-live-reduce-observation.json"),
                JsonSerializer.Serialize(new { gestureReturned, observerRetired, reductionQueued, observedMilliseconds,
                    before, after, draftBeforeReduce, draftAfterReduce }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        foreach (var pose in after)
        {
            Assert.Equal(pose.Checked == true ? 1 : 0, pose.Progress);
            Assert.Equal(pose.Checked == true, pose.IconActive);
            Assert.Equal(pose.Checked == true ? 1 : 0, pose.IconFill);
            Assert.False(pose.IconAnimating);
        }
        Assert.False(OperatorMotion.Enabled);
        foreach (string name in new[] { "HomeTeamBtn", "AwayTeamBtn", "GoalBtn", "BehindBtn" })
        {
            var choice = Choice(f.Editor, name);
            var symbol = ChoiceSymbol(f.Editor, choice);
            Assert.Equal(choice.IsChecked == true ? 1 : 0, OperatorChoiceMotion.GetProgress(choice));
            Assert.Equal(choice.IsChecked == true, symbol.IsActive);
            Assert.Equal(choice.IsChecked == true ? 1 : 0, symbol.FillProgress);
            Assert.False(symbol.IsAnimating);
        }
        CaptureCorrection(f.Editor, "reduced-mid-arrival");
        var behind = Choice(f.Editor, "BehindBtn");
        behind.BringIntoView();
        Assert.True(behind.Focus(NavigationMethod.Tab));
        Key(f.Editor, PhysicalKey.Space, f.Owner);
        AssertChoices(f.Editor, TeamSide.Away, ScoreType.Behind);
        Assert.Equal(1, OperatorChoiceMotion.GetProgress(behind));
        Assert.Equal(1, ChoiceSymbol(f.Editor, behind).FillProgress);
        Assert.False(ChoiceSymbol(f.Editor, behind).IsAnimating);
        await Settle(750);
        AssertChoices(f.Editor, TeamSide.Away, ScoreType.Behind);
        Assert.Equal(1, OperatorChoiceMotion.GetProgress(behind));
        Assert.Equal(0, OperatorChoiceMotion.GetProgress(Choice(f.Editor, "GoalBtn")));
        Assert.Equal("02", Named<TextBox>(f.Editor, "MinutesBox").Text);
        Assert.Equal("15", Named<TextBox>(f.Editor, "SecondsBox").Text);
        Assert.False(f.Completion.IsCompleted);
        f.AssertOriginalUnchanged();
        CaptureCorrection(f.Editor, "reduced-final");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardChoicesKeepTheHitTargetAndReadablePartsFixed(bool compact)
    {
        using var f = new Fixture(animate: true, compact: compact);
        await Settle(750);
        foreach (string name in new[] { "AwayTeamBtn", "BehindBtn", "HomeTeamBtn", "GoalBtn" })
        {
            var choice = Choice(f.Editor, name);
            choice.BringIntoView();
            Assert.True(choice.Focus(NavigationMethod.Tab));
            await Settle();
            var geometry = ChoiceGeometry(f.Editor, choice);
            Key(f.Editor, PhysicalKey.Space, f.Owner);
            Assert.True(choice.IsChecked);
            Assert.True(ChoiceSymbol(f.Editor, choice).IsActive);
            Assert.True(SelectionMark(choice).IsVisible);
            AssertInside(f.Editor, choice);
            AssertStationaryChoice(f.Editor, choice, geometry);
            CaptureCorrection(f.Editor, "keyboard-" + compact + "-" + name);
            await Settle(65);
            AssertStationaryChoice(f.Editor, choice, geometry);
            Assert.False(f.Completion.IsCompleted);
            f.AssertOriginalUnchanged();
        }
        AssertChoices(f.Editor, TeamSide.Home, ScoreType.Goal);
    }

    // Screenshot fixture only: deliberately uses the existing constructor and
    // unchanged timing controls. It can run against the coordinator's prior
    // production assembly to obtain honest before images. It does not establish
    // semantic, motion, validation or acceptance behavior.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureOnlyExistingScoreCorrectionLayout(bool compact)
    {
        using var f = new Fixture(compact: compact);
        await Settle(750);
        Named<ComboBox>(f.Editor, "QuarterBox").BringIntoView();
        await Settle();
        CaptureCorrection(f.Editor, "layout-" + (compact ? "compact" : "normal"));
    }

    private static Rect QuarterPartBounds(Control part, Visual relativeTo)
    {
        var origin = part.TranslatePoint(default, relativeTo);
        Assert.NotNull(origin);
        return new Rect(origin.Value, part.Bounds.Size);
    }

    private static TextBlock QuarterArrow(ComboBox quarter) =>
        Assert.Single(quarter.GetVisualDescendants().OfType<TextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == "⌄");

    private static void PrepareQuarterDraft(Fixture f)
    {
        Named<TextBox>(f.Editor, "MinutesBox").Text = "08";
        Named<TextBox>(f.Editor, "SecondsBox").Text = "36";
        Choice(f.Editor, "AwayTeamBtn").IsChecked = true;
        Choice(f.Editor, "BehindBtn").IsChecked = true;
    }

    private static void AssertQuarterDraft(Fixture f, ComboBox quarter, int selectedIndex)
    {
        Assert.Equal(selectedIndex, quarter.SelectedIndex);
        Assert.Equal("Q" + (selectedIndex + 1),
            Assert.IsAssignableFrom<ComboBoxItem>(quarter.SelectedItem).Content);
        Assert.Single(quarter.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && text.Text == "Q" + (selectedIndex + 1));
        Assert.Equal("08", Named<TextBox>(f.Editor, "MinutesBox").Text);
        Assert.Equal("36", Named<TextBox>(f.Editor, "SecondsBox").Text);
        AssertChoices(f.Editor, TeamSide.Away, ScoreType.Behind);
        Assert.Equal(2, f.Editor.SelectedQuarter);
        Assert.Equal(TimeSpan.FromSeconds(135), f.Editor.SelectedGameTime);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.False(f.Completion.IsCompleted);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuarterSelectorReservesReadableValueAndSeparateArrowSpace(bool compact)
    {
        using var f = new Fixture(compact: compact);
        await Settle();
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        quarter.BringIntoView();
        await Settle();
        var frame = quarter.GetVisualDescendants().OfType<OperatorGlassSurface>()
            .FirstOrDefault(surface => surface.Name == "Frame");
        var arrow = quarter.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.IsEffectivelyVisible && text.Text == "⌄");
        var label = quarter.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.IsEffectivelyVisible && text.Text == "Q2");
        var presenter = quarter.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
            .FirstOrDefault(content => content.Name == "SelectionLabel");
        static object Box(Rect bounds) => new
        {
            bounds.X, bounds.Y, bounds.Width, bounds.Height
        };
        // Record all actual rectangles before the first geometry assertion,
        // including the template frame independently of the ComboBox bounds.
        // A wide control with a narrow painted frame is still a regression.
        string geometry = JsonSerializer.Serialize(new
        {
            Compact = compact,
            CurrentValue = (quarter.SelectedItem as ComboBoxItem)?.Content?.ToString(),
            Selector = Box(new Rect(quarter.Bounds.Size)),
            SelectorInWindow = Box(QuarterPartBounds(quarter, f.Editor)),
            SelectorWidthSpecification = quarter.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HorizontalAlignment = quarter.HorizontalAlignment.ToString(),
            Frame = frame is null ? null : Box(QuarterPartBounds(frame, quarter)),
            FrameWidthSpecification = frame?.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Arrow = arrow is null ? null : Box(QuarterPartBounds(arrow, quarter)),
            SelectedLabel = label is null ? null : Box(QuarterPartBounds(label, quarter)),
            SelectedTextWidth = label?.TextLayout.WidthIncludingTrailingWhitespace,
            SelectedTextAlignment = label?.TextAlignment.ToString(),
            SelectedTextPadding = label?.Padding.ToString(),
            SelectedTextLines = label?.TextLayout.TextLines.Select(line => new
            {
                line.Start, line.WidthIncludingTrailingWhitespace
            }).ToArray(),
            SelectionPresenter = presenter is null ? null : Box(QuarterPartBounds(presenter, quarter))
        }, new JsonSerializerOptions { WriteIndented = true });
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,
                "afl-score-correction-quarter-" + compact + "-geometry.json"), geometry);
        }
        CaptureCorrection(f.Editor, "quarter-field-" + compact);
        Assert.True(quarter.Bounds.Width >= 120, "Quarter selector needs usable width. " + geometry);
        Assert.True(frame is not null && arrow is not null && label is not null,
            "Quarter template must present its frame, current value and disclosure arrow. " + geometry);
        var actualFrame = Assert.IsAssignableFrom<OperatorGlassSurface>(frame);
        var actualArrow = Assert.IsAssignableFrom<TextBlock>(arrow);
        var actualLabel = Assert.IsAssignableFrom<TextBlock>(label);
        var frameBounds = QuarterPartBounds(actualFrame, quarter);
        var arrowBounds = QuarterPartBounds(actualArrow, quarter);
        var labelBounds = QuarterPartBounds(actualLabel, quarter);
        Assert.True(frameBounds.Width >= quarter.Bounds.Width - 2,
            "Painted selector frame must cover the usable field. " + geometry);
        Assert.True(actualLabel.Bounds.Width + .1 >= actualLabel.TextLayout.WidthIncludingTrailingWhitespace,
            "Current quarter must fit without clipping. " + geometry);
        Assert.True(arrowBounds.Left >= frameBounds.Right - 36 && arrowBounds.Right <= frameBounds.Right - 8,
            "Disclosure arrow needs its own right-hand space. " + geometry);
        // TextLine.Start is the formatter's actual alignment offset, also used
        // by TextLine.Draw. The stretched TextBlock bounds and its alignment
        // property alone do not identify the rendered Qn text's position.
        var textLine = Assert.Single(actualLabel.TextLayout.TextLines);
        double textLeft = labelBounds.Left + actualLabel.Padding.Left + textLine.Start;
        double textRight = textLeft + textLine.WidthIncludingTrailingWhitespace;
        Assert.True(labelBounds.Right <= arrowBounds.Left,
            "Quarter text allocation must reserve the disclosure arrow area. " + geometry);
        Assert.True(textLeft >= labelBounds.Left - .1 && textRight <= labelBounds.Right + .1,
            "Formatted quarter text must fit within its allocated label. " + geometry);
        Assert.True(textRight + 8 <= arrowBounds.Left,
            "Quarter text and disclosure arrow must not overlap. " + geometry);
        AssertInside(f.Editor, quarter);
        Assert.Equal(1, quarter.SelectedIndex);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryQuarterIsReachableByRealKeyboardWithoutApplyingOtherDraftFields(bool compact)
    {
        using var f = new Fixture(compact: compact);
        await Settle();
        PrepareQuarterDraft(f);
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        for (int index = 0; index < 4; index++)
        {
            quarter.BringIntoView();
            Assert.True(quarter.Focus(NavigationMethod.Tab));
            await Settle();
            Key(f.Editor, PhysicalKey.F4, f.Owner);
            await Settle();
            Assert.True(quarter.IsDropDownOpen);
            Key(f.Editor, PhysicalKey.Home, f.Owner);
            for (int step = 0; step < index; step++) Key(f.Editor, PhysicalKey.ArrowDown, f.Owner);
            Key(f.Editor, PhysicalKey.Enter, f.Owner);
            await Settle();
            Assert.False(quarter.IsDropDownOpen);
            AssertQuarterDraft(f, quarter, index);
            CaptureCorrection(f.Editor, "quarter-keyboard-" + compact + "-Q" + (index + 1));
        }
        Key(f.Editor, PhysicalKey.F4, f.Owner);
        await Settle();
        Assert.True(quarter.IsDropDownOpen);
        Key(f.Editor, PhysicalKey.Escape, f.Owner);
        await Settle();
        Assert.False(quarter.IsDropDownOpen);
        Assert.True(f.Editor.IsVisible);
        AssertQuarterDraft(f, quarter, 3);
        Key(f.Editor, PhysicalKey.Escape, f.Owner);
        await Settle();
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(1, f.Closed);
        f.AssertOriginalUnchanged();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryQuarterIsReachableByRealPopupPointerSelectionAndSafeCancel(bool compact)
    {
        using var f = new Fixture(compact: compact);
        await Settle();
        PrepareQuarterDraft(f);
        var quarter = Named<ComboBox>(f.Editor, "QuarterBox");
        for (int index = 0; index < 4; index++)
        {
            quarter.BringIntoView();
            await Settle();
            await PointerClick(f.Editor, QuarterArrow(quarter));
            Assert.True(quarter.IsDropDownOpen);
            var item = Assert.IsAssignableFrom<ComboBoxItem>(quarter.ContainerFromIndex(index));
            Assert.Equal("Q" + (index + 1), item.Content);
            Assert.True(item.IsEffectivelyVisible);
            var popupRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popupRoot);
            Assert.NotNull(point);
            Assert.True(item.Bounds.Width > 0 && item.Bounds.Height > 0);
            popupRoot.MouseDown(point.Value, MouseButton.Left);
            popupRoot.MouseUp(point.Value, MouseButton.Left);
            await Settle();
            Assert.False(quarter.IsDropDownOpen);
            AssertQuarterDraft(f, quarter, index);
            CaptureCorrection(f.Editor, "quarter-pointer-" + compact + "-Q" + (index + 1));
        }
        var cancel = Assert.Single(f.Editor.GetVisualDescendants().OfType<Button>(), button => button.IsCancel);
        await PointerClick(f.Editor, cancel);
        Assert.True(f.Completion.IsCompleted);
        Assert.Equal(ScoreEventEditorWindow.EditorResult.Cancelled, f.Editor.Result);
        Assert.Equal(1, f.Closed);
        f.AssertOriginalUnchanged();
    }

}

