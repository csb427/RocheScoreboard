using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static Button SetupAnnouncementAdd(SetupWizard wizard) => Assert.Single(wizard.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Add"));
    private static void AddSetupAnnouncement(SetupWizard wizard, string text)
    {
        Named<TextBox>(wizard, "SetupNewMessageBox").Text = text;
        PresPlanProvider(SetupAnnouncementAdd(wizard)).Invoke();
    }
    private static void AssertCurrentTickerLimits(AflFixture f)
    {
        Assert.Equal(8, f.Window.GetType().GetField("TickerSlotCount", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(140, f.Window.GetType().GetField("TickerMessageMaxLength", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
    }
    private static async Task TypeSetupAnnouncement(AflFixture f, SetupWizard wizard, string value)
    {
        var add = SetupAnnouncementAdd(wizard); await RevealActualWizardCommand(f, wizard, add);
        var box = Named<TextBox>(wizard, "SetupNewMessageBox"); box.BringIntoView(); Assert.True(box.Focus(NavigationMethod.Tab));
        await Settle(60); f.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        f.Window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None); f.Window.KeyTextInput(value); Assert.Equal(value, box.Text);
    }
    private static void EnterSetupAnnouncement(AflFixture f)
    {
        f.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        f.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }
    [AvaloniaTheory]
    [InlineData("capacity", 1280, 850)] [InlineData("length", 420, 320)]
    public async Task SetupAnnouncementRejectsDisplayOverflowWithoutLosingDraftOrColours(string problem, int width, int height)
    {
        await using var f = await AflFixture.Create(width, height); AssertCurrentTickerLimits(f);
        var wizard = await OpenActualAflMessageSetup(f); var messages = MessageDraft(wizard);
        if (problem == "capacity") for (int i = 0; i < 8; i++) AddSetupAnnouncement(wizard, "Announcement " + i);
        int expectedCount = messages.Count; string existing = TickerSlotState(messages), model = ManualWeatherModel(f);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        wizard.GetType().GetField("_newMessageTextColor", flags)!.SetValue(wizard, "#123456");
        wizard.GetType().GetField("_newMessageHighlightColor", flags)!.SetValue(wizard, "#654321");
        Named<Button>(wizard, "SetupNewMessageTextColorBtn").Background = new SolidColorBrush(Color.Parse("#123456"));
        Named<Button>(wizard, "SetupNewMessageHighlightColorBtn").Background = new SolidColorBrush(Color.Parse("#654321"));
        string draft = problem == "capacity" ? "Do not lose the ninth message" : new string('x', 141);
        await TypeSetupAnnouncement(f, wizard, draft); EnterSetupAnnouncement(f); await Settle(60);
        DeltaOwnerRecord("setup-limit-" + problem, new { problem, expectedCount, actualCount = messages.Count,
            draft, actualDraft = Named<TextBox>(wizard, "SetupNewMessageBox").Text, before = existing, after = TickerSlotState(messages) });
        Assert.Equal(expectedCount, messages.Count); Assert.Equal(existing, TickerSlotState(messages));
        Assert.Equal(draft, Named<TextBox>(wizard, "SetupNewMessageBox").Text);
        Assert.Equal("#123456", ManualWeatherRead<string?>(wizard, "_newMessageTextColor"));
        Assert.Equal("#654321", ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
        var feedback = Named<TextBlock>(wizard, "SetupMessageFeedback"); Assert.True(feedback.IsEffectivelyVisible);
        Assert.Contains(problem == "capacity" ? "8" : "140", feedback.Text);
        var rect = FinalsPainted(f.Window, feedback);
        Assert.InRange(rect.Left, -.5, f.Window.ClientSize.Width); Assert.InRange(rect.Right, 0, f.Window.ClientSize.Width + .5);
        Assert.InRange(rect.Top, -.5, f.Window.ClientSize.Height); Assert.InRange(rect.Bottom, 0, f.Window.ClientSize.Height + .5);
        CaptureMessageControl(f.Window, SetupAnnouncementAdd(wizard), "setup-limit-" + problem);
        EnterSetupAnnouncement(f); Assert.Equal(existing, TickerSlotState(messages)); Assert.Equal(model, ManualWeatherModel(f));
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task SetupAnnouncementAcceptsBoundaryAndDoesNotCountBlankRowsAsDeliveredSlots(bool blankRow)
    {
        await using var f = await AflFixture.Create(420, 320); var wizard = await OpenActualAflMessageSetup(f);
        var messages = MessageDraft(wizard);
        if (blankRow)
        {
            for (int i = 0; i < 8; i++) AddSetupAnnouncement(wizard, "Announcement " + i);
            messages[0].Text = " "; // Public bound-model correction: Finish already filters blank entries.
        }
        string draft = blankRow ? "Eighth delivered message" : new string('v', 140);
        string model = ManualWeatherModel(f); await TypeSetupAnnouncement(f, wizard, draft); EnterSetupAnnouncement(f);
        Assert.Equal(blankRow ? 8 : 1, messages.Count(m => !string.IsNullOrWhiteSpace(m.Text)));
        Assert.Equal(draft, messages.Last().Text); Assert.Equal("", Named<TextBox>(wizard, "SetupNewMessageBox").Text);
        Assert.Equal(model, ManualWeatherModel(f));
    }
    [AvaloniaTheory]
    [InlineData("length")] [InlineData("capacity")]
    public async Task SetupAnnouncementInvalidEditedRowsCannotReachReplaceConfirmationOrWriteDefaults(string problem)
    {
        await using var f = await AflFixture.Create(1280, 850); var wizard = await OpenActualAflMessageSetup(f);
        var messages = MessageDraft(wizard);
        if (problem == "capacity")
        {
            for (int i = 0; i < 8; i++) AddSetupAnnouncement(wizard, "Announcement " + i);
            messages[0].Text = " "; AddSetupAnnouncement(wizard, "Latest valid eighth");
            messages[0].Text = "Reactivated ninth"; // Reachable correction after the valid Add.
        }
        else { AddSetupAnnouncement(wizard, "Short draft"); messages[0].Text = new string('z', 141); }
        string state = TickerSlotState(messages), model = ManualWeatherModel(f);
        string path = (string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [false])!;
        byte[]?[] Bytes() => new[] { "", ".bak", ".tmp" }.Select(s => File.Exists(path + s) ? File.ReadAllBytes(path + s) : null).ToArray();
        var before = Bytes(); int confirmations = 0;
        var hook = wizard.GetType().GetProperty("ConfirmCompletion", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var previous = hook.GetValue(wizard); hook.SetValue(wizard, new Func<bool>(() => { confirmations++; return false; }));
        try
        {
            PresPlanProvider(Named<Button>(wizard, "NextButton")).Invoke(); await Settle(60);
            DeltaOwnerRecord("setup-finish-limit-" + problem, new { problem, confirmations, resultCreated = wizard.Result is not null,
                before = state, after = TickerSlotState(messages) });
            Assert.Equal(0, confirmations); Assert.Null(wizard.Result);
            Assert.Equal(state, TickerSlotState(messages)); Assert.Equal(model, ManualWeatherModel(f));
            var after = Bytes(); for (int i = 0; i < before.Length; i++) Assert.Equal(before[i], after[i]);
            Assert.Contains(problem == "capacity" ? "8" : "140", Named<TextBlock>(wizard, "SetupMessageFeedback").Text);
            Assert.False(ManualWeatherRead<bool>(wizard, "_completionPending"));
        }
        finally { hook.SetValue(wizard, previous); }
    }
    [AvaloniaTheory]
    [InlineData("length")] [InlineData("capacity")] [InlineData("pending")]
    public async Task SetupMessageCorrectionClearsFeedbackAndAllowsExactlyOneConfirmation(string problem)
    {
        await using var f = await AflFixture.Create(420, 320); var wizard = await OpenActualAflMessageSetup(f);
        var messages = MessageDraft(wizard);
        AddSetupAnnouncement(wizard, "Original announcement");
        var retired = messages[0];
        if (problem == "length") retired.Text = new string('q', 141);
        else if (problem == "capacity")
        {
            for (int i = 1; i < 8; i++) AddSetupAnnouncement(wizard, "Announcement " + i);
            retired.Text = " "; AddSetupAnnouncement(wizard, "Eighth ready"); retired.Text = "Ninth ready";
        }
        else Named<TextBox>(wizard, "SetupNewMessageBox").Text = "Unadded draft";
        int confirmations = 0;
        var hook = wizard.GetType().GetProperty("ConfirmCompletion", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var previous = hook.GetValue(wizard); hook.SetValue(wizard, new Func<bool>(() => { confirmations++; return false; }));
        try
        {
            PresPlanProvider(Named<Button>(wizard, "NextButton")).Invoke(); await Settle(60);
            Assert.Equal(0, confirmations); Assert.Null(wizard.Result);
            if (problem == "length") retired.Text = new string('c', 140);
            else if (problem == "capacity")
                PresPlanProvider(MessageItemAction(wizard, retired, "Remove this message")).Invoke();
            else AddSetupAnnouncement(wizard, "Unadded draft");
            await Settle(60);
            var feedback = Named<TextBlock>(wizard, "SetupMessageFeedback"); Assert.Contains("characters each", feedback.Text);
            string healthy = feedback.Text!;
            if (problem == "capacity") { retired.Text = new string('x', 141); Assert.Equal(healthy, feedback.Text); }
            PresPlanProvider(Named<Button>(wizard, "NextButton")).Invoke(); await Settle(60);
            Assert.Equal(1, confirmations); Assert.Null(wizard.Result); Assert.False(ManualWeatherRead<bool>(wizard, "_completionPending"));
        }
        finally { hook.SetValue(wizard, previous); }
    }
    [Fact]
    public void SharedTickerFlatteningRetainsOrdinarySpacesAndExistingLimits()
    {
        Assert.Equal(8, TickerMessagePolicy.SlotCount); Assert.Equal(140, TickerMessagePolicy.MaxTextLength);
        Assert.Equal(" a b c d  ", TickerMessagePolicy.Flatten(" a\rb\nc\td  "));
        Assert.Equal("", TickerMessagePolicy.Flatten(null));
        Assert.Equal(new string('x', 141), TickerMessagePolicy.Flatten(new string('x', 141)));
    }

    [AvaloniaFact]
    public async Task SetupEightBoundaryMessagesReachRealLiveSlotsWithoutTruncation()
    {
        await using var f = await AflFixture.Create(1280, 850); var wizard = await OpenActualAflMessageSetup(f);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        wizard.GetType().GetField("_newMessageTextColor", flags)!.SetValue(wizard, "#123456");
        wizard.GetType().GetField("_newMessageHighlightColor", flags)!.SetValue(wizard, "#654321");
        for (int i = 0; i < 8; i++) AddSetupAnnouncement(wizard, i + new string('v', 139));
        string[] expected = MessageDraft(wizard).Select(m => m.Text).ToArray();
        string before = ManualWeatherModel(f);
        foreach (string choice in new[] { "No", "Yes" })
        {
            Exception? callbackError = null; bool answered = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var dialog = f.Window.OwnedWindows.FirstOrDefault(w => w.Title == "New session");
                if (dialog is null) { callbackError = new InvalidOperationException("Expected real replacement confirmation."); return; }
                try
                {
                    PresPlanProvider(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, choice))).Invoke();
                    answered = true;
                }
                catch (Exception error) { callbackError = error; dialog.Close(); }
            });
            PresPlanProvider(Named<Button>(wizard, "NextButton")).Invoke(); await Settle(120);
            Assert.Null(callbackError); Assert.True(answered);
            if (choice == "No")
            { Assert.Null(wizard.Result); Assert.Equal(before, ManualWeatherModel(f)); Assert.Equal(expected, MessageDraft(wizard).Select(m => m.Text)); }
        }
        var result = Assert.IsType<SetupResult>(wizard.Result);
        Assert.Equal(expected, result.Messages.Select(m => m.Text));
        var live = ManualWeatherRead<System.Collections.ObjectModel.ObservableCollection<MarqueeMessage>>(f.Window, "_styledMessages");
        Assert.Equal(expected, live.Select(m => m.Text)); Assert.All(live, m =>
        { Assert.True(m.IsEnabled); Assert.Equal("#FF123456", m.TextColor); Assert.Equal("#FF654321", m.HighlightColor); });
        Assert.False(ManualWeatherRead<bool>(f.Window, "_setupFlowActive"));

    }

}
