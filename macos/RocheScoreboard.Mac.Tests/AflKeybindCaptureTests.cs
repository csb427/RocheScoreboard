// Integrated regression cases reuse AflSettingsInputSafetyTests' calibrated
// Fixture/Field/Click/Settle helpers and actual public input routes.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
using AflAction = Roche_Scoreboard.Services.AflKeybindSettings.AflAction;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class AflSettingsInputSafetyTests
{
    private static readonly string[] KbButtonNames =
    [ "KeybindToggleClockButton", "KeybindHomeGoalButton", "KeybindHomeBehindButton",
      "KeybindAwayGoalButton", "KeybindAwayBehindButton", "KeybindEndQuarterButton" ];

    private sealed class KbFileScope : IDisposable
    {
        private readonly string path;
        private readonly Dictionary<string, byte[]?> original = new();
        private readonly bool directoryExisted;
        private readonly bool rootDirectoryExisted;
        internal KbFileScope()
        {
            // Latching is intentional process isolation. No preference is
            // written until UseDefaults is called inside the using scope.
            SelfTestStorage.Initialize(true);
            rootDirectoryExisted = Directory.Exists(SelfTestStorage.RootPath);
            path = Path.Combine(SelfTestStorage.DocumentsDirectory, "afl_keybind_settings.json");
            directoryExisted = Directory.Exists(Path.GetDirectoryName(path));
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                original[path + suffix] = File.Exists(path + suffix) ? File.ReadAllBytes(path + suffix) : null;
        }
        internal void UseDefaults()
        {
            AflKeybindSettings.SaveBindings(AflKeybindSettings.GetDefaultBindings());
            Assert.True(File.Exists(path), "Start from actual saved isolated bindings, not absent-file equality.");
            Assert.Equal(Map(AflKeybindSettings.GetDefaultBindings()), Map(AflKeybindSettings.LoadBindings()));
            Assert.DoesNotContain(AflKeybindSettings.LoadBindings(), item => item.Value == Key.J);
        }
        internal Dictionary<string, byte[]?> Bytes() => new[] { "", ".bak", ".tmp" }.ToDictionary(
            suffix => suffix, suffix => File.Exists(path + suffix) ? File.ReadAllBytes(path + suffix) : null);
        public void Dispose()
        {
            var failures = new List<Exception>();
            foreach (var (file, bytes) in original)
                try
                {
                    if (bytes is null) { if (File.Exists(file)) File.Delete(file); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); }
                }
                catch (Exception error) { failures.Add(error); }
            try
            {
                string directory = Path.GetDirectoryName(path)!;
                if (!directoryExisted && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
                if (!rootDirectoryExisted && Directory.Exists(SelfTestStorage.RootPath)
                    && !Directory.EnumerateFileSystemEntries(SelfTestStorage.RootPath).Any())
                    Directory.Delete(SelfTestStorage.RootPath);
            }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Keybinding storage restore failed.", failures);
        }
    }

    private sealed class KbChanges : IDisposable
    {
        private readonly MatchManager match;
        internal int Count;
        internal KbChanges(MatchManager match) { this.match = match; match.MatchChanged += Changed; }
        private void Changed() => Count++;
        public void Dispose() => match.MatchChanged -= Changed;
    }

    private static string[] Map(IReadOnlyDictionary<AflAction, Key> map) => map.OrderBy(item => item.Key)
        .Select(item => item.Key + ":" + item.Value).ToArray();
    private static string KbState(Fixture f) => JsonSerializer.Serialize(f.Match.ToState());
    private static Dictionary<string, string?> KbLabels(Fixture f) => KbButtonNames.ToDictionary(name => name,
        name => f.Named<Button>(name).Content as string);
    private static void KbAssertLabels(Dictionary<string, string?> expected, Fixture f)
    { foreach (var (name, label) in expected) Assert.Equal(label, f.Named<Button>(name).Content as string); }
    private static void KbAssertBytes(Dictionary<string, byte[]?> expected, Dictionary<string, byte[]?> actual)
    { foreach (var (suffix, bytes) in expected) Assert.Equal(bytes, actual[suffix]); }
    private static Dictionary<string, byte[]?> KbRecovery(Fixture f, bool save = false)
    {
        if (save) GameStateService.Save(new GameState { Sport = "afl", MatchState = f.Match.ToState() });
        Assert.True(File.Exists(GameStateService.RecoveryStatePath));
        return new[] { "", ".bak", ".tmp" }.ToDictionary(suffix => suffix,
            suffix => File.Exists(GameStateService.RecoveryStatePath + suffix)
                ? File.ReadAllBytes(GameStateService.RecoveryStatePath + suffix) : null);
    }
    private static bool KbIsArmedHint(string? hint) => hint is not null
        && hint.Replace(" ", "").Contains("HomeGoal", StringComparison.OrdinalIgnoreCase)
        && (hint.Contains("press", StringComparison.OrdinalIgnoreCase) || hint.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    private static bool KbWithin(Control root, IInputElement? hit) => ReferenceEquals(root, hit)
        || hit is Visual visual && visual.GetVisualAncestors().Contains(root);
    private static bool KbLiveSource(Fixture f, Control source) => source.IsLoaded && source.IsEffectivelyVisible
        && source.IsEffectivelyEnabled && ReferenceEquals(TopLevel.GetTopLevel(source), f.Window)
        && source.TranslatePoint(default, f.Window) is not null;
    private static Rect KbPainted(Window owner, Control target)
    {
        target.UpdateLayout();
        var points = new[] { new Point(), new Point(target.Bounds.Width, 0), new Point(0, target.Bounds.Height),
            new Point(target.Bounds.Width, target.Bounds.Height) }.Select(p => target.TranslatePoint(p, owner)).ToArray();
        Assert.All(points, point => Assert.NotNull(point));
        double left = points.Min(p => p!.Value.X), top = points.Min(p => p!.Value.Y);
        return new Rect(left, top, points.Max(p => p!.Value.X) - left, points.Max(p => p!.Value.Y) - top);
    }
    private static async Task KbTap(Fixture f, Control action)
    {
        // Deliberately no keyboard focus/Tab/reveal here: while capture is
        // armed, current production would capture that preparatory Tab itself.
        Assert.True(action.IsEffectivelyVisible && action.IsEffectivelyEnabled);
        Rect box = KbPainted(f.Window, action);
        Assert.True(box.Width > 8 && box.Height > 8);
        Assert.True(new Rect(f.Window.Bounds.Size).Contains(box));
        foreach (var point in new[] { box.Center, new Point(box.Left + 4, box.Center.Y), new Point(box.Right - 4, box.Center.Y),
            new Point(box.Center.X, box.Top + 4), new Point(box.Center.X, box.Bottom - 4) })
            Assert.True(KbWithin(action, f.Window.InputHitTest(point)), "Actual pointer target is occluded: " + action.Name);
        f.Window.MouseMove(box.Center); f.Window.MouseDown(box.Center, MouseButton.Left); f.Window.MouseUp(box.Center, MouseButton.Left);
        await Settle();
    }
    private static void KbKey(Fixture f, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    { f.Window.KeyPressQwerty(physical, modifiers); f.Window.KeyReleaseQwerty(physical, modifiers); }
    private static void KbObservedPlainKey(Fixture f, PhysicalKey physical, Key expected)
    {
        Key? observed = null;
        EventHandler<KeyEventArgs> observer = (_, args) => { if (args.PhysicalKey == physical) observed = args.Key; };
        f.Window.AddHandler(InputElement.KeyDownEvent, observer, RoutingStrategies.Tunnel, handledEventsToo: true);
        try { KbKey(f, physical); }
        finally { f.Window.RemoveHandler(InputElement.KeyDownEvent, observer); }
        Assert.Equal(expected, observed); // NumPad mapping failure is fixture evidence, not a key-policy defect.
    }
    private static async Task KbCategory(Fixture f, string category)
    {
        var workspace = Field<SettingsWorkspace>(f.Window, "_aflSettingsWorkspace");
        var action = Assert.Single(workspace.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.Tag, category) && button.IsEffectivelyVisible);
        await KbTap(f, action);
        Assert.True(KbWithin(action, f.Window.FocusManager?.GetFocusedElement()), "The actual navigation command must own non-text input.");
    }
    private static Task KbArmHomeGoal(Fixture f) => KbArm(f, "KeybindHomeGoalButton", AflAction.HomeGoal);
    private static async Task KbArm(Fixture f, string buttonName, AflAction action)
    {
        var source = f.Named<Button>(buttonName);
        Assert.Equal(AflKeybindSettings.FormatKey(AflKeybindSettings.GetDefaultBindings()[action]), source.Content as string);
        // Preparation is safe before arming; use the existing actual Tab
        // viewport contract only to reveal the intended source button.
        f.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        try { Assert.True(source.Focus(NavigationMethod.Tab)); source.BringIntoView(); }
        finally { f.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); }
        await Settle(); await KbTap(f, source);
        Assert.True(source.IsFocused);
        string hint = f.Named<TextBlock>("KeybindHintText").Text ?? "";
        string expectedLabel = action == AflAction.ToggleClock ? "Start or pause clock" : action.ToString();
        Assert.Contains(expectedLabel.Replace(" ", ""), hint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Press", hint, StringComparison.OrdinalIgnoreCase);
    }
    private static async Task KbMatchPage(Fixture f, bool assertCaptureRetired = false)
    {
        KbKey(f, PhysicalKey.Digit1, RawInputModifiers.Control); await Settle();
        Assert.Same(f.Named<TabItem>("MatchControlTab"), f.Named<TabControl>("MainTabControl").SelectedItem);
        if (assertCaptureRetired)
            Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text),
                "Actual tab navigation must retire capture before preparing any later focus or key.");
        var command = f.Named<Button>("HomeGoalButton");
        Assert.True(command.IsEffectivelyVisible && command.Focus(NavigationMethod.Tab));
        Assert.Same(command, f.Window.FocusManager?.GetFocusedElement());
        Assert.IsNotType<TextBox>(f.Window.FocusManager?.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task KeybindCurrentVisibleCaptureCanSaveFreeJAndThenScoreExactlyOnce()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArmHomeGoal(f);
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        var expected = AflKeybindSettings.GetDefaultBindings().ToDictionary(item => item.Key, item => item.Value);
        expected[AflAction.HomeGoal] = Key.J;
        Assert.Equal(Map(expected), Map(AflKeybindSettings.LoadBindings()));
        Assert.Equal("J", f.Named<Button>("KeybindHomeGoalButton").Content as string);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
        await KbMatchPage(f); int goals = f.Match.HomeGoals, events = f.Match.Events.Count;
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(goals + 1, f.Match.HomeGoals); Assert.Equal(events + 1, f.Match.Events.Count); Assert.Equal(1, changes.Count);
    }

    [AvaloniaFact]
    public async Task KeybindEscapeCancelsActualVisibleCaptureWithoutSaving()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes(); var labels = KbLabels(f);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        using var changes = new KbChanges(f.Match);
        await KbArmHomeGoal(f);
        KbKey(f, PhysicalKey.Escape); await Settle();
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes()); KbAssertLabels(labels, f);
        Assert.Equal(before, KbState(f)); Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        Assert.True(f.Named<Button>("KeybindHomeGoalButton").IsFocused);
        Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeybindCaptureRetiresWhenItsActualCategoryOrTabIsLeft(bool leaveTab)
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes(); var labels = KbLabels(f);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArmHomeGoal(f);
        if (leaveTab) await KbMatchPage(f, assertCaptureRetired: true);
        else await KbCategory(f, "MATCH");
        Assert.False(KbLiveSource(f, f.Named<Button>("KeybindHomeGoalButton")),
            "The old capture source must be collapsed or detached after its actual category/tab is left.");
        Assert.IsNotType<TextBox>(f.Window.FocusManager?.GetFocusedElement());
        bool armedHintAfterLeave = KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text);
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes()); KbAssertLabels(labels, f);
        Assert.False(armedHintAfterLeave, "Leaving the capture owner must retire it then, rather than waiting for an unrelated later key.");
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
        if (leaveTab) { KbKey(f, PhysicalKey.Digit4, RawInputModifiers.Control); await Settle(); }
        await KbCategory(f, "CONTROLS");
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        await KbMatchPage(f); int goals = f.Match.HomeGoals, events = f.Match.Events.Count;
        KbObservedPlainKey(f, PhysicalKey.A, Key.A); await Settle();
        Assert.Equal(goals + 1, f.Match.HomeGoals); Assert.Equal(events + 1, f.Match.Events.Count); Assert.Equal(1, changes.Count);
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Digit1, Key.D1)]
    [InlineData(PhysicalKey.NumPad1, Key.NumPad1)]
    public async Task KeybindRejectsPresentationOwnedDigitWithoutSavingOrExecutingItDuringCapture(PhysicalKey physical, Key logical)
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        Assert.True(f.Named<Button>("KeybindHomeGoalButton").Focus(NavigationMethod.Tab));
        KbObservedPlainKey(f, PhysicalKey.Digit2, Key.D2); await Settle(); // Positive actual presentation-router witness.
        Assert.Equal("summary", Field<string>(f.Window, "_activeScreenKey"));
        var output = Field<Window?>(f.Window, "_displayWindow"); bool? outputVisible = output?.IsVisible;
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes(); var labels = KbLabels(f);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArmHomeGoal(f);
        KbObservedPlainKey(f, physical, logical); await Settle();
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes());
        string hint = f.Named<TextBlock>("KeybindHintText").Text ?? "";
        Assert.True(hint.Contains("presentation", StringComparison.OrdinalIgnoreCase) || hint.Contains("scorebug", StringComparison.OrdinalIgnoreCase),
            "The local explanation must name the existing owner of this reserved shortcut.");
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
        Assert.Equal("summary", Field<string>(f.Window, "_activeScreenKey"));
        Assert.Same(output, Field<Window?>(f.Window, "_displayWindow")); Assert.Equal(outputVisible, output?.IsVisible);
        // Implementation may retain capture for retry or finish safely. Escape
        // makes either policy safe before proving ordinary presentation routing.
        KbKey(f, PhysicalKey.Escape); await Settle(); KbAssertLabels(labels, f);
        KbObservedPlainKey(f, physical, logical); await Settle();
        Assert.Equal("scorebug", Field<string>(f.Window, "_activeScreenKey"));
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count);
    }

    [AvaloniaFact]
    public async Task KeybindCaptureRetiresOnActualHideAndCanBeArmedAgainAfterShow()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes();
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArmHomeGoal(f);
        f.Window.Hide(); await Settle();
        Assert.False(f.Window.IsVisible);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        f.Window.Show(); await Settle();
        Assert.True(f.Window.IsVisible);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes());
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
        await KbArmHomeGoal(f); KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(Key.J, AflKeybindSettings.LoadBindings()[AflAction.HomeGoal]);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count);
    }

    [AvaloniaFact]
    public async Task KeybindCaptureRetiresImmediatelyWhenSameManagerLoadsReplacementState()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes();
        await KbArmHomeGoal(f);
        var replacement = f.Match.ToState();
        f.Match.LoadState(replacement);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        using var changes = new KbChanges(f.Match);
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes());
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
    }

    [AvaloniaFact]
    public async Task KeybindCaptureYieldsToActualCommandBarWithoutCapturingSearchText()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        var map = Map(AflKeybindSettings.LoadBindings()); var bytes = storage.Bytes();
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArmHomeGoal(f);
        KbKey(f, PhysicalKey.K, RawInputModifiers.Control); await Settle();
        Assert.True(f.Named<Grid>("CommandOverlay").IsEffectivelyVisible);
        Assert.False(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        var search = f.Named<TextBox>("CommandSearch"); Assert.True(search.IsFocused);
        KbObservedPlainKey(f, PhysicalKey.J, Key.J);
        f.Window.KeyTextInput("j"); await Settle();
        Assert.Equal("j", search.Text);
        Assert.Equal(map, Map(AflKeybindSettings.LoadBindings())); KbAssertBytes(bytes, storage.Bytes());
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
        KbKey(f, PhysicalKey.Escape); await Settle();
        Assert.False(f.Named<Grid>("CommandOverlay").IsEffectivelyVisible);
        await KbArmHomeGoal(f); KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(Key.J, AflKeybindSettings.LoadBindings()[AflAction.HomeGoal]);
    }

    [AvaloniaTheory]
    [InlineData("KeybindToggleClockButton", "ToggleClock")]
    [InlineData("KeybindHomeBehindButton", "HomeBehind")]
    [InlineData("KeybindAwayGoalButton", "AwayGoal")]
    [InlineData("KeybindAwayBehindButton", "AwayBehind")]
    [InlineData("KeybindEndQuarterButton", "EndQuarter")]
    public async Task EachOtherKeybindButtonCapturesOnlyItsOwnActionWithoutExecutingIt(string buttonName, string actionName)
    {
        AflAction action = Enum.Parse<AflAction>(actionName);
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        await KbArm(f, buttonName, action);
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        var expected = AflKeybindSettings.GetDefaultBindings().ToDictionary(item => item.Key, item => item.Value);
        expected[action] = Key.J;
        Assert.Equal(Map(expected), Map(AflKeybindSettings.LoadBindings()));
        Assert.Equal("J", f.Named<Button>(buttonName).Content as string);
        foreach (string other in KbButtonNames.Where(name => name != buttonName))
            Assert.NotEqual("J", f.Named<Button>(other).Content as string);
        Assert.DoesNotContain("Press a new key", f.Named<TextBlock>("KeybindHintText").Text ?? "");
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
    }

    [AvaloniaFact]
    public async Task PublicKeybindInvocationClaimsKeyboardFocusBeforeAcceptingCapture()
    {
        using var storage = new KbFileScope(); storage.UseDefaults();
        using var f = new Fixture(); await f.Ready(); await f.OpenCategory("CONTROLS");
        using var changes = new KbChanges(f.Match);
        string before = KbState(f); var recovery = KbRecovery(f, save: true);
        var source = f.Named<Button>("KeybindHomeGoalButton");
        source.BringIntoView(); await Settle();
        var workspace = Field<SettingsWorkspace>(f.Window, "_aflSettingsWorkspace");
        var search = Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>());
        Assert.True(search.Focus(NavigationMethod.Tab)); Assert.True(search.IsFocused);
        Assert.True(source.IsEffectivelyVisible && source.IsEffectivelyEnabled);
        // Public routed Click models an invocation that does not first move
        // pointer/keyboard focus. This is not native Windows UIA execution.
        source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(source.IsFocused, "A capture requested through public invocation must claim its key source, not leave typing owned by another field.");
        Assert.True(KbIsArmedHint(f.Named<TextBlock>("KeybindHintText").Text));
        KbObservedPlainKey(f, PhysicalKey.J, Key.J); await Settle();
        Assert.Equal(Key.J, AflKeybindSettings.LoadBindings()[AflAction.HomeGoal]);
        Assert.Equal(before, KbState(f)); Assert.Equal(0, changes.Count); KbAssertBytes(recovery, KbRecovery(f));
    }
}
