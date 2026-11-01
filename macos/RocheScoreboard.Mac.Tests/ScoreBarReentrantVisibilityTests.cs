using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData("self")]
    [InlineData("ancestor")]
    public async Task ScoreBarResumeTextNotificationCannotRestartAReentrantlyHiddenOwner(string boundary)
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var match = BreakFadeMatch(1);
        var bar = new PresentationScoreBar { Height = 64 };
        var slot = new Grid(); slot.Children.Add(bar);
        var window = new Window { Width = 707, Height = 100, Content = slot };
        TextBlock? total = null; EventHandler<AvaloniaPropertyChangedEventArgs>? observer = null;
        bool hiddenAgain = false;
        void Hide() { if (boundary == "self") bar.IsVisible = false; else slot.IsVisible = false; }
        void Show() { if (boundary == "self") bar.IsVisible = true; else slot.IsVisible = true; }
        try
        {
            BreakFadePopulate(bar, match, 1); window.Show(); await Settle(100);
            total = Named<TextBlock>(bar, "HomeTotalText"); Assert.Equal("0", total.Text);
            Hide(); Assert.False(HiddenBarTimer(bar, "_clockTimer"));
            match.AddGoal(TeamSide.Home); string state = DeltaOwnerState(match);
            Assert.Equal("0", total.Text); // Hidden data has not yet been refreshed through Populate.
            observer = (_, args) =>
            {
                if (hiddenAgain || args.Property != TextBlock.TextProperty || total.Text != "6") return;
                hiddenAgain = true; Hide(); // Synchronous notification inside public show's refresh.
            };
            total.PropertyChanged += observer;
            Show(); Assert.True(hiddenAgain, "Exercise the actual current-score text handoff during show.");
            DeltaOwnerRecord("resume-reentrant-" + boundary, new
            {
                hiddenAgain, loaded = bar.IsLoaded, localVisible = bar.IsVisible, ancestorVisible = slot.IsVisible,
                refresh = HiddenBarTimer(bar, "_clockTimer"), cadence = HiddenBarTimer(bar, "_breakCentreTimer"),
                text = total.Text, before = state, after = DeltaOwnerState(match)
            });
            Assert.True(bar.IsLoaded); Assert.False(HiddenBarTimer(bar, "_clockTimer"));
            Assert.False(HiddenBarTimer(bar, "_breakCentreTimer"));
            DeltaOwnerGone(bar, true); DeltaOwnerGone(bar, false); Assert.Equal(state, DeltaOwnerState(match));
            total.PropertyChanged -= observer; observer = null;
            Show(); await Settle(100);
            Assert.Equal("6", total.Text); Assert.True(HiddenBarTimer(bar, "_clockTimer"));
            Assert.True(HiddenBarTimer(bar, "_breakCentreTimer"));
            Assert.Equal(state, DeltaOwnerState(match));
        }
        finally { if (total is not null && observer is not null) total.PropertyChanged -= observer; window.Close(); }
    }

    [AvaloniaFact]
    public async Task ScoreBarBreakTextNotificationRebindCannotReviveTheOldFadeIn()
    {
        await using var storage = await AflFixture.Create(1280, 850);
        var oldMatch = BreakFadeMatch(1); var currentMatch = BreakFadeMatch(2);
        string oldState = DeltaOwnerState(oldMatch), currentState = DeltaOwnerState(currentMatch);
        var bar = new PresentationScoreBar { Height = 64 };
        var window = new Window { Width = 707, Height = 100, Content = bar };
        TextBlock? text = null; EventHandler? frameObserver = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? textObserver = null;
        bool probing = false, paintedFade = false, rebound = false;
        Exception? error = null; bool? clockImmediatelyAfter = null; string? immediatelyAfter = null;
        try
        {
            BreakFadePopulate(bar, oldMatch, 1); window.Show(); await Settle(100);
            text = Named<TextBlock>(bar, "BreakCenterText");
            string oldLabel = QuarterLabelService.GetBreakLabel(1), currentLabel = QuarterLabelService.GetBreakLabel(2);
            frameObserver = (_, _) =>
            {
                if (probing || paintedFade || error is not null || !Motion.IsAnimating(text)
                    || text.Opacity <= .10 || text.Opacity >= .96 || text.Text != oldLabel) return;
                probing = true;
                try { using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    if (Motion.IsAnimating(text) && text.Opacity > .10 && text.Opacity < .96 && text.Text == oldLabel)
                        paintedFade = true;
                }
                catch (Exception caught) { error = caught; }
                finally { probing = false; }
            };
            textObserver = (_, args) =>
            {
                if (!paintedFade || rebound || error is not null || args.Property != TextBlock.TextProperty || text.Text == oldLabel) return;
                rebound = true;
                try { BreakFadePopulate(bar, currentMatch, 2); clockImmediatelyAfter = Motion.IsAnimating(text); immediatelyAfter = text.Text; }
                catch (Exception caught) { error = caught; }
            };
            text.PropertyChanged += textObserver; NativeRendering.Rendering += frameObserver;
            for (int i = 0; i < 1250 && !rebound && error is null; i++) await Settle(20);
            Assert.Null(error); Assert.True(paintedFade); Assert.True(rebound);
            text.PropertyChanged -= textObserver; textObserver = null;
            NativeRendering.Rendering -= frameObserver; frameObserver = null;
            await Settle(1000);
            DeltaOwnerRecord("break-reentrant-rebind", new { paintedFade, rebound, clockImmediatelyAfter, immediatelyAfter,
                text = text.Text, opacity = text.Opacity, clock = Motion.IsAnimating(text) });
            Assert.False(clockImmediatelyAfter); Assert.Equal(currentLabel, immediatelyAfter); Assert.Equal(currentLabel, text.Text);
            Assert.False(Motion.IsAnimating(text)); Assert.Equal(1d, text.Opacity, 6);
            Assert.Equal(oldState, DeltaOwnerState(oldMatch)); Assert.Equal(currentState, DeltaOwnerState(currentMatch));
        }
        finally
        {
            if (text is not null && textObserver is not null) text.PropertyChanged -= textObserver;
            if (frameObserver is not null) NativeRendering.Rendering -= frameObserver;
            window.Close();
        }
    }
}
