using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(false,1280,850)] [InlineData(true,420,320)]
    [Trait("Evidence","ResetSettingsEntry")]
    public async Task SettingsResetExplainsConfirmationAndKeepsItsOwnedSafeDecision(bool keyboard,int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        Key(f.Window,PhysicalKey.F,RawInputModifiers.Control);f.Window.KeyTextInput("reset match");await Settle(120);
        var action=Assert.Single(Named<Border>(f.Window,"SettingsSection_Advanced").GetVisualDescendants().OfType<Control>().Where(x=>x is OperatorActionBorder or Button),
            x=>x.GetVisualDescendants().OfType<TextBlock>().Any(t=>string.Equals(t.Text,"Reset match",StringComparison.OrdinalIgnoreCase)));
        await MediaBrowseReveal(f,action);Capture(f.Window,"reset-settings-entry-"+width);
        // Mounted form normalization already raises the authored9px copy to13.
        // The remaining defect is missing consequence/confirmation semantics.
        Assert.Contains("confirmation",AutomationProperties.GetHelpText(action)??"",StringComparison.OrdinalIgnoreCase);
        Assert.All(action.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Text?.Length>3),t=>Assert.True(t.FontSize>=13));
        string model=ManualWeatherModel(f);var output=PresPlanRead<Window>(f,"_displayWindow");bool outputVisible=output.IsVisible;
        if(keyboard)
        {
            Assert.True(action.Focus(NavigationMethod.Tab));Assert.Same(action,f.Window.FocusManager?.GetFocusedElement());
            f.Window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);
            bool activeOnDown=PresPlanRead<bool>(f,"_resetDecisionActive");
            f.Window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir,"settings-reset-key-boundary.json"),System.Text.Json.JsonSerializer.Serialize(new{activeOnDown,activeOnRelease=PresPlanRead<bool>(f,"_resetDecisionActive")}));
        }else await Tap(f.Window,action);
        await Settle(100);Assert.True(PresPlanRead<bool>(f,"_resetDecisionActive"));Assert.True(Named<Control>(f.Window,"ResetOverlay").IsVisible);
        var cancel=Named<Button>(f.Window,"CancelResetBtn");Assert.Same(cancel,f.Window.FocusManager?.GetFocusedElement());
        Assert.False(f.Window.OpenCommandBar());Assert.Equal(model,ManualWeatherModel(f));Assert.Same(output,PresPlanRead<Window>(f,"_displayWindow"));Assert.Equal(outputVisible,output.IsVisible);
        Capture(f.Window,"reset-settings-confirmation-"+width);
        Key(f.Window,keyboard?PhysicalKey.Enter:PhysicalKey.Escape);await Settle(450);
        Assert.False(PresPlanRead<bool>(f,"_resetDecisionActive"));Assert.False(Named<Control>(f.Window,"ResetOverlay").IsVisible);
        Assert.Same(action,f.Window.FocusManager?.GetFocusedElement());Assert.Equal(model,ManualWeatherModel(f));Assert.Equal(outputVisible,output.IsVisible);
    }
}
