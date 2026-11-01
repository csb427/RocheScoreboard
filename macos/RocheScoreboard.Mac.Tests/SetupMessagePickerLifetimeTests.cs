using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task SetupMessagePickerUsesActualHostAndRejectsAcceptedChoiceAfterReset(bool existing, bool highlight, bool reset)
    {
        await using var f = await AflFixture.Create(1280, 850);
        var wizard = await OpenActualAflMessageSetup(f);
        var messages = MessageDraft(wizard);
        var message = new MarqueeMessage("Original announcement", "#ABCDEF", "#123456");
        messages.Add(message); await Settle(80);
        string model = ManualWeatherModel(f), oldText = message.TextColor!, oldHighlight = message.HighlightColor!;
        Button target = existing ? MessageItemAction(wizard, message, highlight ? "highlight colour" : "text colour")
            : Named<Button>(wizard, highlight ? "SetupNewMessageHighlightColorBtn" : "SetupNewMessageTextColorBtn");
        await RevealActualWizardCommand(f, wizard, target);
        string recentPath = Path.Combine(SelfTestStorage.DocumentsDirectory, "recent_colours.json");
        byte[]?[] RecentBytes() => new[] { "", ".bak", ".tmp" }
            .Select(suffix => File.Exists(recentPath + suffix) ? File.ReadAllBytes(recentPath + suffix) : null).ToArray();
        var recentBefore = RecentBytes();
        ExceptionDispatchInfo? callFailure = null, driverFailure = null;
        int opened = 0; bool closedByOriginalAction = false; bool? dialogAccepted = null;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var driver = DispatcherTimer.RunOnce(async () =>
        {
            ColorPickerWindow? picker = null;
            try
            {
                await Until(() => f.Window.OwnedWindows.OfType<ColorPickerWindow>().Any(),
                    "Announcement action must open a picker owned by its actual wizard host.");
                picker = Assert.Single(f.Window.OwnedWindows.OfType<ColorPickerWindow>()); opened++;
                Assert.Same(f.Window, picker.Owner);
                var hex = Named<TextBox>(picker, "HexBox");
                await Until(() => picker.IsLoaded && hex.Bounds.Width > 0, "Actual owned modal must measure before typing.");
                hex.BringIntoView(); Assert.True(hex.Focus(NavigationMethod.Tab)); await Settle(40);
                picker.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                picker.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
                picker.KeyTextInput("#C72E85"); await Settle(40); Assert.Equal("#C72E85", hex.Text);
                if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } dir)
                {
                    Directory.CreateDirectory(dir);
                    using var frame = picker.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(dir, $"picker-{existing}-{highlight}-{reset}.png"));
                }
                if (reset) wizard.Reset(); // Public lifetime boundary while the real modal is pending.
                var accept = Assert.Single(picker.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "USE COLOUR"));
                accept.BringIntoView(); accept.Focus(NavigationMethod.Tab); await Settle(40);
                PresPlanProvider(accept).Invoke();
                await Until(() => !picker.IsVisible, "Original Use must accept/close before cleanup.");
                closedByOriginalAction = true; dialogAccepted = picker.DialogResult == true;
            }
            catch (Exception error) { driverFailure = ExceptionDispatchInfo.Capture(error); }
            finally
            {
                if (picker?.IsVisible == true) picker.Close(false);
                foreach (var child in f.Window.OwnedWindows.OfType<ColorPickerWindow>().ToArray()) child.Close(false);
                finished.TrySetResult();
            }
        }, TimeSpan.FromMilliseconds(60));
        try { PresPlanProvider(target).Invoke(); }
        catch (Exception error) { callFailure = ExceptionDispatchInfo.Capture(error); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        callFailure?.Throw(); driverFailure?.Throw();
        Assert.Equal(1, opened); Assert.True(closedByOriginalAction);
        Assert.Empty(f.Window.OwnedWindows.OfType<ColorPickerWindow>());
        DeltaOwnerRecord($"message-picker-{existing}-{highlight}-{reset}", new
        {
            existing, highlight, reset, opened, closedByOriginalAction, dialogAccepted,
            text = message.TextColor, highlightColour = message.HighlightColor,
            currentText = ManualWeatherRead<string?>(wizard, "_newMessageTextColor"),
            currentHighlight = ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"),
            messageCount = messages.Count, before = model, after = ManualWeatherModel(f),
            recentUnchanged = recentBefore.Zip(RecentBytes()).All(pair => pair.First is null ? pair.Second is null
                : pair.Second is not null && pair.First.SequenceEqual(pair.Second))
        });
        Assert.Equal(model, ManualWeatherModel(f));
        Assert.Equal(!reset, dialogAccepted);
        if (reset)
        {
            Assert.Empty(messages); Assert.Null(ManualWeatherRead<string?>(wizard, "_newMessageTextColor"));
            Assert.Null(ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
            Assert.Equal(oldText, message.TextColor); Assert.Equal(oldHighlight, message.HighlightColor);
            var recentAfter = RecentBytes();
            for (int i = 0; i < recentBefore.Length; i++) Assert.Equal(recentBefore[i], recentAfter[i]);
        }
        else if (existing)
        {
            Assert.Same(message, Assert.Single(messages));
            Assert.Equal(highlight ? oldText : "#C72E85", message.TextColor);
            Assert.Equal(highlight ? "#C72E85" : oldHighlight, message.HighlightColor);
        }
        else
        {
            Assert.Equal(highlight ? null : "#C72E85", ManualWeatherRead<string?>(wizard, "_newMessageTextColor"));
            Assert.Equal(highlight ? "#C72E85" : null, ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
            Assert.Equal(oldText, message.TextColor); Assert.Equal(oldHighlight, message.HighlightColor);
        }
    }
}
