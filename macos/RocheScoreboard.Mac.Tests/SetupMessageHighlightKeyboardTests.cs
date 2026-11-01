using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static async Task MessageActions(bool cricket, Func<AflFixture, UserControl, NativeWindow, Task> exercise)
    {
        await using var f = await AflFixture.Create(1280, 850);
        UserControl wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        if (wizard is CricketSetupWizard c) c.Reset(); else ((SetupWizard)wizard).Reset();
        var window = new NativeWindow { Width = 900, Height = 700, Content = wizard, Owner = f.Window };
        try
        {
            window.Show(); await Settle(180);
            var nav = Named<RadioButton>(wizard, cricket ? "Step4Navigation" : "Step3Navigation");
            nav.IsChecked = true; nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Named<ComboBox>(wizard, "SetupWeatherLocation").IsEffectivelyVisible,
                "Actual wizard Options must reveal its active venue editor.");
            await exercise(f, wizard, window);
        }
        finally { window.Close(); await Settle(60); }
    }
    private static ObservableCollection<MarqueeMessage> MessageDraft(UserControl wizard)
        => ManualWeatherRead<ObservableCollection<MarqueeMessage>>(wizard, "_setupMessages");
    private static Button MessageItemAction(UserControl wizard, MarqueeMessage message, string purpose)
        => Assert.Single(wizard.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Tag, message)
            && (ToolTip.GetTip(button)?.ToString() ?? "").Contains(purpose, StringComparison.OrdinalIgnoreCase));
    private static void CaptureMessageControl(NativeWindow window, Button target, string name)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SetupMessageHighlightDeleteUsesFocusedControlAndPreservesOtherMessageState(bool cricket, bool existing)
    {
        await MessageActions(cricket, async (f, wizard, window) =>
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var draft = MessageDraft(wizard);
            var first = new MarqueeMessage("First announcement", "#ABCDEF", "#123456");
            var other = new MarqueeMessage("Other announcement", "#111111", "#654321");
            draft.Add(first); draft.Add(other); await Settle(120);
            var current = Named<Button>(wizard, "SetupNewMessageHighlightColorBtn");
            wizard.GetType().GetField("_newMessageHighlightColor", fields)!.SetValue(wizard, "#123456");
            current.Background = new SolidColorBrush(Color.Parse("#123456"));
            Button target = existing ? MessageItemAction(wizard, first, "highlight colour") : current;
            target.BringIntoView(); await Settle(180);
            Assert.True(target.IsEffectivelyVisible && target.IsEffectivelyEnabled);
            Assert.True(target.Focus(NavigationMethod.Tab)); Assert.True(target.IsFocused);
            var point = Assert.IsType<Point>(target.TranslatePoint(default, window));
            Assert.True(new Rect(window.ClientSize).Contains(new Rect(point, target.Bounds.Size)),
                "Focused swatch must fit its actual visible owner before Delete.");
            string state = ManualWeatherModel(f);
            CaptureMessageControl(window, target, $"{cricket}-{existing}-before");
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None); await Settle(40);
            if (existing)
            {
                Assert.Null(first.HighlightColor); Assert.Equal("#123456", ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
            }
            else
            {
                Assert.Null(ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(current.Background).Color);
                Assert.Equal("#123456", first.HighlightColor);
            }
            Assert.Equal("#ABCDEF", first.TextColor); Assert.Equal("#654321", other.HighlightColor);
            Assert.Equal(2, draft.Count); Assert.Equal(state, ManualWeatherModel(f));
            Assert.True(target.IsFocused); Assert.Empty(window.OwnedWindows.OfType<ColorPickerWindow>());
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None); await Settle(20);
            Assert.Equal(2, draft.Count); Assert.Equal("#654321", other.HighlightColor);
            CaptureMessageControl(window, target, $"{cricket}-{existing}-after");
        });
    }

    [AvaloniaTheory]
    [InlineData(false, "text colour", "Text colour for announcement: ")]
    [InlineData(false, "highlight colour", "Highlight colour for announcement: ")]
    [InlineData(false, "Remove this message", "Remove announcement: ")]
    public async Task SetupSavedMessageActionNamesFollowTheirActualMessage(bool cricket, string purpose, string prefix)
    {
        await MessageActions(cricket, async (_, wizard, _) =>
        {
            var first = new MarqueeMessage("Boundary at river end", "#ABCDEF", "#123456");
            var second = new MarqueeMessage("Other announcement", "#FFFFFF", "#654321");
            MessageDraft(wizard).Add(first); MessageDraft(wizard).Add(second); await Settle(120);
            Button action = MessageItemAction(wizard, first, purpose);
            Assert.Equal(prefix + first.Text, AutomationProperties.GetName(action));
            Assert.Equal(prefix + second.Text, AutomationProperties.GetName(MessageItemAction(wizard, second, purpose)));
            first.Text = "Revised announcement"; await Settle(40);
            Assert.Equal(prefix + "Revised announcement", AutomationProperties.GetName(action));
        });
    }
    [AvaloniaFact]
    public async Task CricketLegacyTickerComposerRemainsHiddenOnActualOptionsScreen()
    {
        await MessageActions(true, async (_, wizard, _) =>
        {
            Assert.Equal(4, ManualWeatherRead<int>(wizard, "_currentStep"));
            Assert.True(Named<ComboBox>(wizard, "SetupWeatherLocation").IsEffectivelyVisible);
            Assert.False(Named<TextBox>(wizard, "SetupNewMessageBox").IsEffectivelyVisible);
            Assert.False(Named<Button>(wizard, "SetupNewMessageTextColorBtn").IsEffectivelyVisible);
            Assert.False(Named<Button>(wizard, "SetupNewMessageHighlightColorBtn").IsEffectivelyVisible);
            await Settle(20);
        });
    }

}
