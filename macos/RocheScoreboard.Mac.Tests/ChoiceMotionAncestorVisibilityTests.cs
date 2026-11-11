using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

// Headless regression, not evidence of native Mac desktop acceptance.
public sealed class ChoiceMotionAncestorVisibilityTests
{
    [AvaloniaFact]
    public void HiddenAncestorSelectionChangesSettleImmediatelyAndVisibleMotionCanRestart()
    {
        bool? previous = OperatorMotion.AnimationOverride;
        var choice = new Button { Content = "Persisted selection" };
        var host = new Border { Child = choice };
        var window = new NativeWindow { Width = 320, Height = 180, Content = host };
        try
        {
            OperatorMotion.AnimationOverride = true;
            OperatorChoiceMotion.SetEnabled(choice, true);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(choice.IsLoaded);
            Assert.True(NativeEffectiveVisibility.IsVisible(choice));
            Assert.Equal(0, OperatorChoiceMotion.GetProgress(choice));

            OperatorChoiceMotion.SetSelected(choice, true);
            Assert.True(OperatorChoiceMotion.IsAnimating(choice));
            host.IsVisible = false;
            Assert.True(choice.IsLoaded);
            Assert.True(choice.IsVisible); // Local flag intentionally differs from ancestor state.
            Assert.False(NativeEffectiveVisibility.IsVisible(choice));
            Assert.Equal(1, OperatorChoiceMotion.GetProgress(choice));
            Assert.False(OperatorChoiceMotion.IsAnimating(choice));

            // Selection may change through binding while this route is hidden.
            // Assert synchronously: a normally settling spring is not sufficient.
            foreach (bool selected in new[] { false, true, false })
            {
                OperatorChoiceMotion.SetSelected(choice, selected);
                Assert.Equal(selected, OperatorChoiceMotion.GetSelected(choice));
                Assert.Equal(selected ? 1d : 0d, OperatorChoiceMotion.GetProgress(choice));
                Assert.False(OperatorChoiceMotion.IsAnimating(choice));
                Assert.True(choice.IsVisible);
            }

            host.IsVisible = true;
            Assert.True(NativeEffectiveVisibility.IsVisible(choice));
            OperatorChoiceMotion.SetSelected(choice, true);
            Assert.True(OperatorChoiceMotion.IsAnimating(choice));
            OperatorChoiceMotion.SetEnabled(choice, false);
            Assert.Equal(1, OperatorChoiceMotion.GetProgress(choice));
            Assert.False(OperatorChoiceMotion.IsAnimating(choice));
        }
        finally
        {
            OperatorChoiceMotion.SetEnabled(choice, false);
            window.Close();
            OperatorMotion.AnimationOverride = previous;
        }
    }
}
