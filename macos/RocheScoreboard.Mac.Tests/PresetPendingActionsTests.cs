using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private sealed class PendingActionIo(IPresetStorageIo inner) : IPresetStorageIo
    {
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Writes;
        public Task<string> ReadAllTextAsync(string path) => inner.ReadAllTextAsync(path);
        public async Task WriteAllTextAsync(string path, string content)
        { Interlocked.Increment(ref Writes); Started.TrySetResult(true); await release.Task; await inner.WriteAllTextAsync(path, content); }
        internal void Release() => release.TrySetResult(true);
    }
    private static async Task<UserControl> PendingActionWizard(KbRoot f, bool cricket)
    {
        if (!cricket) return f.Wizard;
        await KbClick(f.Main, Named<Button>(f.Wizard, "CancelSetupButton"));
        var home = Named<SportSelectionScreen>(f.Main, "SportSelectionPanel");
        await Until(() => home.IsEffectivelyVisible, "Cancel returns to actual Home.");
        await KbClick(f.Main, Assert.Single(home.GetVisualDescendants().OfType<Button>(), b => b.Name == "AflLaunchAction"));
        await KbClick(f.Main, Named<RadioButton>(f.Wizard, "CricketSportChoice"));
        await KbClick(f.Main, Named<Button>(f.Wizard, "NextButton"));
        var wizard = Named<CricketSetupWizard>(f.Main, "CricketSetupWizardPanel");
        await Until(() => wizard.IsEffectivelyVisible && wizard.GetVisualDescendants().OfType<Button>().Count(b => Equals(b.Content, "Save as preset")) == 2,
            "Actual mounted cricket teams must install both saved-club action groups.");
        return wizard;
    }
    [AvaloniaTheory]
    [InlineData(false, "Home")] [InlineData(false, "Away")] [InlineData(true, "Home")] [InlineData(true, "Away")]
    public async Task ActualSavedClubActionsShowPendingWithoutLockingDraftOrDuplicatingAcceptedWrite(bool cricket, string side)
    {
        await using var f = await KbRoot.Create(); var wizard = await PendingActionWizard(f, cricket);
        string captured = "Pending " + side + " " + cricket + " FC";
        var name = Named<TextBox>(wizard, "Setup" + side + "NameBox"); name.Text = captured;
        var surface = name.GetVisualAncestors().OfType<OperatorGlassSurface>().First();
        var save = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Save as preset"));
        var status = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "team-preset-status:" + side));
        var actions = wizard.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "Save as preset") || Equals(b.Content, "Delete preset")).ToArray();
        Assert.Equal(4, actions.Length); var before = f.Files.Bytes(); var recovery = f.Recovery();
        var gate = new PendingActionIo(PresetStorage.CurrentIoForTests);
        using (PresetStorage.UseIoForTests(gate))
        {
            try
            {
                await KbClick(f.Main, save); await Until(() => gate.Started.Task.IsCompleted, "Accepted save must reach the held real storage write.");
                KbEvidence("pending-actions-" + cricket + "-" + side, new { cricket, side, gate.Writes,
                    availability = actions.Select(b => new { b.Content, b.IsEffectivelyEnabled }).ToArray(), status.Text });
                Assert.True(f.Main.IsEnabled); Assert.True(name.IsEffectivelyEnabled);
                Assert.All(actions, button => Assert.False(button.IsEffectivelyEnabled));
                Exception? refusal = null;
                try { Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(save)).Invoke(); }
                catch (Exception error) { refusal = error; }
                Assert.True(KbAllowedRefusal(refusal)); await Pump(40); Assert.Equal(1, gate.Writes); f.Files.AssertBytes(before);
                name.BringIntoView(); Assert.True(name.Focus(NavigationMethod.Tab));
                f.Main.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); f.Main.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
                f.Main.KeyTextInput("Newer unsaved operator draft"); await Pump(40);
                Assert.Equal("Newer unsaved operator draft", name.Text); Assert.Contains("Saving", status.Text);
                gate.Release(); await Until(() => status.Text?.Contains("saved as a preset", StringComparison.Ordinal) == true && actions.All(b => b.IsEffectivelyEnabled),
                    "Completion must restore all saved-club actions without replacing the newer draft.");
                Assert.Equal("Newer unsaved operator draft", name.Text); Assert.Equal(1, gate.Writes);
                var stored = await PresetStorage.ReadAsync(cricket ? KbCricket : KbAfl);
                Assert.Single(stored.Presets, p => p.PresetName == captured); Assert.DoesNotContain(stored.Presets, p => p.PresetName == name.Text);
                f.AssertRecovery(recovery); if (wizard is CricketSetupWizard c) Assert.Null(c.Result);
                KbScreenshot(f.Main, "pending-actions-completed-" + cricket + "-" + side);
            }
            finally
            {
                // A failed assertion must never strand accepted IO or the fixture's queue.
                gate.Release(); await PresetStorage.ReadAsync(cricket ? KbCricket : KbAfl); await Pump(100);
            }
        }
    }
}
