using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
using TickerKey = Avalonia.Input.Key;
using AflAction = Roche_Scoreboard.Services.AflKeybindSettings.AflAction;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task<ObservableCollection<MarqueeMessage>> PrepareLiveTicker(AflFixture f)
    {
        ((IRemoteCommandTarget)f.Window).SetMessages(["First saved announcement", "Other saved announcement"]);
        await Mr4Navigate(f.Window, PhysicalKey.Digit2);
        await Until(() => Named<ItemsControl>(f.Window, "MessageList").IsEffectivelyVisible,
            "Actual output page must expose its eight saved ticker slots.");
        var slots = ManualWeatherRead<ObservableCollection<MarqueeMessage>>(f.Window, "_styledMessages");
        Assert.Equal(8, slots.Count); Assert.Equal("First saved announcement", slots[0].Text);
        return slots;
    }
    private static Button LiveTickerItemAction(AflFixture f, MarqueeMessage message, string purpose)
        => Assert.Single(Named<ItemsControl>(f.Window, "MessageList").GetVisualDescendants().OfType<Button>(),
            button => ReferenceEquals(button.Tag, message)
                && (ToolTip.GetTip(button)?.ToString() ?? "").Contains(purpose, StringComparison.OrdinalIgnoreCase));
    private static string TickerSlotState(IEnumerable<MarqueeMessage> slots)
        => JsonSerializer.Serialize(slots.Select(slot => new { slot.Text, slot.TextColor, slot.HighlightColor, slot.IsEnabled }));


    [AvaloniaFact]
    public async Task LiveTickerSavedHighlightDeleteCannotTriggerAConfiguredGoalAndKeepsOtherSlots()
    {
        await using var f = await AflFixture.Create(1280, 850);
        var slots = await PrepareLiveTicker(f);
        slots[0].TextColor = "#FFABCDEF"; slots[0].HighlightColor = "#FF123456";
        slots[1].HighlightColor = "#FF654321"; slots[1].IsEnabled = false;
        var bindings = ManualWeatherRead<Dictionary<AflAction, TickerKey>>(f.Window, "_aflKeybinds");
        TickerKey previous = bindings[AflAction.HomeGoal];
        try
        {
            bindings[AflAction.HomeGoal] = TickerKey.Delete;
            await Settle(120);
            Button target = LiveTickerItemAction(f, slots[0], "Highlight colour");
            await PresPlanReveal(f, target); Assert.True(target.IsFocused);
            string model = ManualWeatherModel(f);
            int homeGoals = f.Match.HomeGoals, events = f.Match.Events.Count;
            bool activation = PresPlanRead<bool>(f, "_outputActivationRequired");
            bool visible = PresPlanRead<Window?>(f, "_displayWindow")?.IsVisible == true;
            f.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            f.Window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None); await Settle(60);
            Assert.Equal(homeGoals, f.Match.HomeGoals); Assert.Equal(events, f.Match.Events.Count);
            Assert.Equal(model, ManualWeatherModel(f)); Assert.Equal(TickerKey.Delete, bindings[AflAction.HomeGoal]);
            Assert.Null(slots[0].HighlightColor);
            Assert.Equal(8, slots.Count); Assert.Equal("First saved announcement", slots[0].Text);
            Assert.Equal("#FFABCDEF", slots[0].TextColor); Assert.Equal("#FF654321", slots[1].HighlightColor); Assert.False(slots[1].IsEnabled);
            Assert.True(target.IsFocused); Assert.Equal(activation, PresPlanRead<bool>(f, "_outputActivationRequired"));
            Assert.Equal(visible, PresPlanRead<Window?>(f, "_displayWindow")?.IsVisible == true);
            Assert.Empty(f.Window.OwnedWindows);
            await Mr4Navigate(f.Window, PhysicalKey.Digit1);
            Assert.True(Named<Button>(f.Window, "HomeGoalButton").Focus(NavigationMethod.Tab));
            f.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            f.Window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None); await Settle(60);
            Assert.Equal(homeGoals + 1, f.Match.HomeGoals); Assert.Equal(events + 1, f.Match.Events.Count);
        }
        finally { bindings[AflAction.HomeGoal] = previous; }
    }

    [AvaloniaTheory]
    [InlineData("Text colour", "Saved message text colour: ")]
    [InlineData("Highlight colour", "Saved message highlight colour: ")]
    [InlineData("Clear this preset slot", "Clear saved message: ")]
    public async Task LiveTickerSavedSlotActionNamesFollowActualEditableText(string purpose, string prefix)
    {
        await using var f = await AflFixture.Create(1280, 850);
        var slots = await PrepareLiveTicker(f); await Settle(80);
        var action = LiveTickerItemAction(f, slots[0], purpose);
        Assert.Equal(prefix + slots[0].Text, AutomationProperties.GetName(action));
        Assert.Equal(prefix + slots[1].Text, AutomationProperties.GetName(LiveTickerItemAction(f, slots[1], purpose)));
        slots[0].Text = "Revised saved announcement"; await Settle(40);
        Assert.Equal(prefix + "Revised saved announcement", AutomationProperties.GetName(action));
        Assert.Equal(8, slots.Count);
    }

    [AvaloniaFact]
    public async Task LiveTickerFixedSlotsKeepTheRedundantStandaloneComposerHidden()
    {
        await using var f = await AflFixture.Create(1280, 850);
        var slots = await PrepareLiveTicker(f);
        Assert.Equal(8, slots.Count);
        Assert.True(Named<ItemsControl>(f.Window, "MessageList").IsEffectivelyVisible);
        Assert.False(Named<TextBox>(f.Window, "NewMessageBox").IsEffectivelyVisible);
        Assert.False(Named<Button>(f.Window, "NewMessageTextColorBtn").IsEffectivelyVisible);
        Assert.False(Named<Button>(f.Window, "NewMessageHighlightColorBtn").IsEffectivelyVisible);
    }
}
