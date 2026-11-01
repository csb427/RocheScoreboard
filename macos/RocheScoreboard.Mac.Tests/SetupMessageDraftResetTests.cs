using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    public async Task SetupMessageWholeDraftResetClearsUnfinishedTextAndColoursBeforeNextMessage(bool cricket)
    {
        await using var f = await AflFixture.Create(1280, 850);
        UserControl wizard = cricket ? Named<CricketSetupWizard>(f.Window, "CricketSetupWizardPanel")
            : Named<SetupWizard>(f.Window, "SetupWizardPanel");
        var text = Named<TextBox>(wizard, "SetupNewMessageBox");
        var textColour = Named<Button>(wizard, "SetupNewMessageTextColorBtn");
        var highlight = Named<Button>(wizard, "SetupNewMessageHighlightColorBtn");
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // Seed a coherent previously edited draft; this verifies public Reset,
        // not colour-picker acceptance, physical typing or native automation.
        wizard.GetType().GetField("_newMessageTextColor", fields)!.SetValue(wizard, "#123456");
        wizard.GetType().GetField("_newMessageHighlightColor", fields)!.SetValue(wizard, "#654321");
        textColour.Background = new SolidColorBrush(Color.Parse("#123456"));
        highlight.Background = new SolidColorBrush(Color.Parse("#654321"));
        text.Text = "Unfinished old announcement";
        var messages = ManualWeatherRead<ObservableCollection<MarqueeMessage>>(wizard, "_setupMessages");
        messages.Add(new MarqueeMessage("Old queued announcement", "#123456", "#654321"));
        string state = ManualWeatherModel(f);
        if (wizard is CricketSetupWizard c) c.Reset(); else ((SetupWizard)wizard).Reset();
        Assert.Equal("", text.Text); Assert.Empty(messages);
        Assert.Null(ManualWeatherRead<string?>(wizard, "_newMessageTextColor"));
        Assert.Null(ManualWeatherRead<string?>(wizard, "_newMessageHighlightColor"));
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(textColour.Background).Color);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(highlight.Background).Color);
        text.Text = " Fresh announcement ";
        var add = Assert.Single(wizard.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Add"));
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var message = Assert.Single(messages);
        Assert.Equal("Fresh announcement", message.Text); Assert.Null(message.TextColor); Assert.Null(message.HighlightColor);
        Assert.Equal("", text.Text); Assert.Equal(state, ManualWeatherModel(f));
    }

    [AvaloniaTheory]
    [InlineData(false, "SetupNewMessageBox", "New announcement")]
    [InlineData(false, "SetupNewMessageTextColorBtn", "New announcement text colour")]
    [InlineData(false, "SetupNewMessageHighlightColorBtn", "New announcement highlight colour")]
    public async Task SetupMessageComposerControlsHaveExplicitSemanticNames(bool cricket, string name, string expected)
    {
        await using var f = await AflFixture.Create(1280, 850);
        UserControl wizard = cricket ? Named<CricketSetupWizard>(f.Window, "CricketSetupWizardPanel")
            : Named<SetupWizard>(f.Window, "SetupWizardPanel");
        Assert.Equal(expected, AutomationProperties.GetName(Named<Control>(wizard, name)));
    }
}
