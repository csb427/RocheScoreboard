using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static bool HiddenBarTimer(PresentationScoreBar bar, string field)
    {
        var value = bar.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(bar);
        return Assert.IsType<DispatcherTimer>(value).IsEnabled;
    }

    [AvaloniaTheory]
    [InlineData("self")]
    [InlineData("ancestor")]
    public async Task ScoreBarHiddenBreakRetiresActualFadeAndResumesFreshNaturalCadence(string boundary)
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var match = BreakFadeMatch(1);
        string state = DeltaOwnerState(match);
        var bar = new PresentationScoreBar { Height = 64 };
        var slot = new Grid(); slot.Children.Add(bar);
        var window = new Window { Width = 707, Height = 100, Content = slot };
        EventHandler? observer = null; Exception? error = null;
        bool probing = false, hidden = false;
        bool? clockAfterHide = null, cadenceAfterHide = null, refreshAfterHide = null;
        try
        {
            BreakFadePopulate(bar, match, 1); window.Show(); await Settle(100);
            var text = Named<TextBlock>(bar, "BreakCenterText");
            string label = QuarterLabelService.GetBreakLabel(1);
            Assert.Equal(label, text.Text);
            Assert.True(HiddenBarTimer(bar, "_clockTimer"));
            Assert.True(HiddenBarTimer(bar, "_breakCentreTimer"));
            observer = (_, _) =>
            {
                if (probing || hidden || error is not null || !Motion.IsAnimating(text)
                    || text.Opacity <= .10 || text.Opacity >= .96 || text.Text != label) return;
                probing = true;
                try
                {
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    if (!Motion.IsAnimating(text) || text.Opacity <= .10 || text.Opacity >= .96 || text.Text != label) return;
                    Capture(window, "hidden-break-" + boundary + "-before-hide");
                    hidden = true;
                    if (boundary == "self") bar.IsVisible = false; else slot.IsVisible = false;
                    clockAfterHide = Motion.IsAnimating(text);
                    cadenceAfterHide = HiddenBarTimer(bar, "_breakCentreTimer");
                    refreshAfterHide = HiddenBarTimer(bar, "_clockTimer");
                }
                catch (Exception caught) { error = caught; }
                finally { probing = false; }
            };
            NativeRendering.Rendering += observer;
            for (int i = 0; i < 1250 && !hidden && error is null; i++) await Settle(20);
            Assert.Null(error); Assert.True(hidden, "Witness the real painted17s fade, not a synthetic Tick.");
            NativeRendering.Rendering -= observer; observer = null;
            Assert.True(bar.IsLoaded); // Hide is deliberately distinct from the already covered unload path.
            await Settle(1200);
            DeltaOwnerRecord("hidden-break-" + boundary + "-retired", new
            {
                clockAfterHide, cadenceAfterHide, refreshAfterHide,
                text = text.Text, opacity = text.Opacity, clock = Motion.IsAnimating(text),
                cadence = HiddenBarTimer(bar, "_breakCentreTimer"), refresh = HiddenBarTimer(bar, "_clockTimer"),
                before = state, after = DeltaOwnerState(match)
            });
            Assert.False(clockAfterHide); Assert.False(cadenceAfterHide); Assert.False(refreshAfterHide);
            Assert.Equal(label, text.Text); Assert.Equal(1d, text.Opacity, 6); Assert.False(Motion.IsAnimating(text));
            // Late refresh/rebinding is allowed to set current data, never restart hidden timers.
            BreakFadePopulate(bar, match, 1);
            var replacement = BreakFadeMatch(2); string replacementState = DeltaOwnerState(replacement);
            BreakFadePopulate(bar, replacement, 2);
            Assert.False(HiddenBarTimer(bar, "_clockTimer")); Assert.False(HiddenBarTimer(bar, "_breakCentreTimer"));
            string freshLabel = QuarterLabelService.GetBreakLabel(2);
            Assert.Equal(freshLabel, text.Text); Assert.False(Motion.IsAnimating(text));
            if (boundary == "self") bar.IsVisible = true; else slot.IsVisible = true;
            await Settle(120);
            Assert.True(HiddenBarTimer(bar, "_clockTimer")); Assert.True(HiddenBarTimer(bar, "_breakCentreTimer"));
            Assert.Equal(freshLabel, text.Text); Assert.False(Motion.IsAnimating(text));
            bool witnessedFresh = false;
            observer = (_, _) =>
            {
                if (probing || witnessedFresh || error is not null || !Motion.IsAnimating(text)
                    || text.Opacity <= .10 || text.Opacity >= .96 || text.Text != freshLabel) return;
                probing = true;
                try { using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    if (Motion.IsAnimating(text) && text.Opacity > .10 && text.Opacity < .96 && text.Text == freshLabel)
                        witnessedFresh = true;
                }
                catch (Exception caught) { error = caught; }
                finally { probing = false; }
            };
            NativeRendering.Rendering += observer;
            for (int i = 0; i < 1250 && !witnessedFresh && error is null; i++) await Settle(20);
            Assert.Null(error); Assert.True(witnessedFresh, "Returning must preserve a fresh real17s cadence.");
            NativeRendering.Rendering -= observer; observer = null;
            await Settle(1000);
            Assert.NotEqual(freshLabel, text.Text); Assert.False(Motion.IsAnimating(text));
            Assert.Equal(1d, text.Opacity, 6); Assert.Equal(state, DeltaOwnerState(match));
            Assert.Equal(replacementState, DeltaOwnerState(replacement));
            Capture(window, "hidden-break-" + boundary + "-fresh-clock");
        }
        finally { if (observer is not null) NativeRendering.Rendering -= observer; window.Close(); }
    }
}
