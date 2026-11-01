using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Views;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    // Public Reset must restore editor state, including a previously filtered
    // item set. This is a control-state regression, not physical typing evidence.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WeatherLocationResetRestoresSuggestionsAndClosesThePreviousPopup(bool cricket)
    {
        await using var f = await AflFixture.Create(1280, 850);
        UserControl wizard = cricket ? Named<CricketSetupWizard>(f.Window, "CricketSetupWizardPanel")
            : Named<SetupWizard>(f.Window, "SetupWizardPanel");
        var combo = Named<ComboBox>(wizard, "SetupWeatherLocation");
        // Exact reachable state after filtering to a different venue.
        combo.Items.Clear(); combo.Items.Add("Darwin, Australia");
        combo.Text = "Darwin";
        string expectedText = cricket ? "Melbourne, Australia" : OperatorWorkflowPreferences.Load().AflWeatherLocation;
        if (wizard is CricketSetupWizard c) c.Reset(); else ((SetupWizard)wizard).Reset();
        var expected = cricket ? CricketSetupWizard.AustralianCities : SetupWizard.AustralianCities;
        Assert.Equal(expected, combo.Items.Cast<string>());
        Assert.False(combo.IsDropDownOpen);
        Assert.Contains("Melbourne, Australia", combo.Items.Cast<string>());
        Assert.DoesNotContain(combo.Items.Cast<string>(), city => city == "Darwin");
        Assert.Equal(expectedText, combo.Text);
    }
    [AvaloniaTheory]
    [InlineData("afl")]
    [InlineData("cricket")]
    [InlineData("settings")]
    public async Task WeatherLocationEditorHasAnExplicitVenueName(string owner)
    {
        await using var f = await AflFixture.Create(1280, 850);
        ComboBox combo = owner == "settings" ? Named<ComboBox>(f.Window, "SettingsWeatherLocation")
            : Named<ComboBox>(owner == "cricket" ? (Control)Named<CricketSetupWizard>(f.Window, "CricketSetupWizardPanel")
                : Named<SetupWizard>(f.Window, "SetupWizardPanel"), "SetupWeatherLocation");
        Assert.Equal("Weather location", AutomationProperties.GetName(combo));
    }

    [AvaloniaTheory]
    [InlineData("Custom oval")]
    [InlineData("")]
    public async Task WeatherLocationSettingsRestoreRebuildsSuggestionsWithoutStartingManualFetch(string location)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            var f = controlled.Fixture;
            var combo = Named<ComboBox>(f.Window, "SettingsWeatherLocation");
            combo.Items.Clear(); combo.Items.Add("Darwin, Australia"); combo.Text = "Darwin";
            string model = ManualWeatherModel(f);
            ((IRemoteCommandTarget)f.Window).SetWeatherLocation(location);
            Assert.Equal(SetupWizard.AustralianCities, combo.Items.Cast<string>());
            Assert.Equal(location, combo.Text); Assert.False(combo.IsDropDownOpen);
            Assert.Empty(controlled.Pending); Assert.Empty(controlled.Feedback);
            Assert.Equal(model, ManualWeatherModel(f));
            await Settle(20);
        });
    }

}
