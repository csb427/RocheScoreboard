using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

/// <summary>Existing production launcher interactions, with fake copy subscribers only.</summary>
public sealed class CloudRemoteLauncherInteractionTests
{
    private const string Code = "ABC123";
    private const string Url = "https://remote.example.invalid/join/ABC123";

    private static async Task Settle(int milliseconds = 100)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(CloudRemoteLauncher launcher, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(launcher.FindControl<T>(name));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Key(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static Control CodeCommand(CloudRemoteLauncher launcher)
    {
        var text = Named<TextBlock>(launcher, "JoinCodeText");
        var command = text.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        return command is not null ? command : Assert.IsAssignableFrom<Control>(text.Parent);
    }

    private static void AssertOpen(CloudRemoteLauncher launcher)
    {
        Assert.True(launcher.IsExpanded, "Required live disclosure precondition: " + launcher.DisclosureDiagnostics);
        Assert.True(Named<StackPanel>(launcher, "ExpandedPanel").IsEffectivelyVisible,
            "Required visible details precondition: " + launcher.DisclosureDiagnostics);
    }

    private static void Copy(CloudRemoteLauncher launcher, string target)
    {
        AssertOpen(launcher);
        if (target == "URL") Click(Named<Button>(launcher, "CopyUrlButton"));
        else
        {
            // Invoke the existing production handler so its current mouse-only
            // Border can participate in feedback tests before it becomes a Button.
            typeof(CloudRemoteLauncher).GetMethod("JoinCodeBox_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(launcher, new object[] { launcher, new RoutedEventArgs() });
        }
    }

    private static IEnumerable<TextBlock> Texts(CloudRemoteLauncher launcher) =>
        launcher.GetVisualDescendants().OfType<TextBlock>()
            .Concat(Named<StackPanel>(launcher, "ExpandedPanel").GetVisualDescendants().OfType<TextBlock>()).Distinct();

    private static bool IsCopied(TextBlock text) =>
        text.Text?.Contains("copied", StringComparison.OrdinalIgnoreCase) == true
        && text.Text?.Contains("not copied", StringComparison.OrdinalIgnoreCase) != true;

    private static void AssertShareValues(CloudRemoteLauncher launcher, string code = Code, string url = Url)
    {
        Assert.Equal(code, Named<TextBlock>(launcher, "JoinCodeText").Text);
        Assert.Equal(url, Named<TextBlock>(launcher, "JoinUrlText").Text);
    }

    private static void SubscribeCopy(CloudRemoteLauncher launcher, string target, EventHandler handler)
    {
        if (target == "URL") launcher.CopyJoinUrlRequested += handler;
        else launcher.CopyJoinCodeRequested += handler;
    }

    private static async Task RevealWithKeyboard(Window owner, Control control)
    {
        owner.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(control.Focus(NavigationMethod.Tab), "The actual command must accept keyboard focus");
        control.BringIntoView();
        owner.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await Settle();
    }

    private static void AssertInsideHost(Control control, string action)
    {
        var host = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        Assert.True(control.IsEffectivelyVisible, action + " must be visible");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, action + " must have a hit area");
        var origin = control.TranslatePoint(default, host);
        Assert.NotNull(origin);
        Assert.True(new Rect(host.ClientSize).Contains(new Rect(origin.Value, control.Bounds.Size)),
            action + " must fit the real hosting TopLevel after keyboard reveal");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly CloudRemoteLauncher Launcher = new() { Width = 300 };
        internal readonly Grid Host = new();
        internal readonly NativeWindow Window;

        internal Fixture()
        {
            Launcher.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Launcher.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            Host.Children.Add(Launcher);
            Window = new NativeWindow { Width = 680, Height = 680, Content = Host };
            Launcher.ApplyState(true, true, Code, Url);
            Launcher.ApplyConnection(true, true);
            Window.Show();
            Launcher.Expand();
        }

        public void Dispose() => Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Space)]
    [InlineData(PhysicalKey.Enter)]
    public async Task JoinCodeCopyHasNativeKeyboardActivationAndDescriptiveAccessibleName(PhysicalKey key)
    {
        using var f = new Fixture();
        await Settle();
        int calls = 0;
        f.Launcher.CopyJoinCodeRequested += (_, _) => calls++;
        var command = CodeCommand(f.Launcher);
        await RevealWithKeyboard(f.Window, command);
        string accessibleName = AutomationProperties.GetName(command) ?? string.Empty;
        Assert.Contains("copy", accessibleName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code", accessibleName, StringComparison.OrdinalIgnoreCase);
        Key(f.Window, key);
        await Settle();
        Assert.Equal(1, calls);
        AssertShareValues(f.Launcher);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task AcceptedCopyLeavesBothShareValuesReadableAndUsesSeparateFeedback(string target)
    {
        using var f = new Fixture();
        await Settle();
        int calls = 0;
        SubscribeCopy(f.Launcher, target, (_, _) => calls++);
        Copy(f.Launcher, target);
        Assert.Equal(1, calls);
        AssertShareValues(f.Launcher);
        var code = Named<TextBlock>(f.Launcher, "JoinCodeText");
        var url = Named<TextBlock>(f.Launcher, "JoinUrlText");
        Assert.Contains(Texts(f.Launcher), text => !ReferenceEquals(text, code) && !ReferenceEquals(text, url)
            && text.IsEffectivelyVisible && IsCopied(text));
        await Settle(1450);
        AssertShareValues(f.Launcher);
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task RejectedCopyIsContainedAndNeverShowsClipboardSuccess(string target)
    {
        using var f = new Fixture();
        await Settle();
        int calls = 0;
        SubscribeCopy(f.Launcher, target, (_, _) =>
        {
            calls++;
            throw new ExternalException("Fake clipboard is busy");
        });
        var exception = Record.Exception(() => Copy(f.Launcher, target));
        Assert.Null(exception);
        Assert.Equal(1, calls);
        AssertShareValues(f.Launcher);
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
        Assert.Contains(Texts(f.Launcher), text => text.IsEffectivelyVisible
            && text.Text?.Contains("could not copy", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(Named<Button>(f.Launcher, "CopyUrlButton").IsEnabled);
        Assert.True(Named<Button>(f.Launcher, "EndButton").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task AnUnownedCopyRequestCannotClaimThatAClipboardWriteSucceeded(string target)
    {
        using var f = new Fixture();
        await Settle();
        Copy(f.Launcher, target);
        AssertShareValues(f.Launcher);
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
        Assert.Contains(Texts(f.Launcher), text => text.IsEffectivelyVisible
            && text.Text?.Contains("could not copy", StringComparison.OrdinalIgnoreCase) == true);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task RepeatedCopyFeedbackSettlesToCurrentValuesInsteadOfRestoringCopiedText(string target)
    {
        using var f = new Fixture();
        await Settle();
        int calls = 0;
        SubscribeCopy(f.Launcher, target, (_, _) => calls++);
        Copy(f.Launcher, target);
        await Settle(180);
        Copy(f.Launcher, target);
        await Settle(1450);
        Assert.Equal(2, calls);
        AssertShareValues(f.Launcher);
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task ReplacingTheLiveSessionInvalidatesFeedbackWithoutRestoringItsOldShareValues(string target)
    {
        using var f = new Fixture();
        await Settle();
        SubscribeCopy(f.Launcher, target, (_, _) => { });
        Copy(f.Launcher, target);
        f.Launcher.ApplyState(true, false, null, null);
        f.Launcher.ApplyState(true, true, "NEW456", "https://remote.example.invalid/join/NEW456");
        f.Launcher.ApplyConnection(false, true);
        await Settle(1450);
        AssertShareValues(f.Launcher, "NEW456", "https://remote.example.invalid/join/NEW456");
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
        Assert.Contains("reconnecting", Named<TextBlock>(f.Launcher, "StatusText").Text, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task UnloadCancelsPendingFeedbackAndOldCallbacksCannotMutateTheDetachedView(string target)
    {
        using var f = new Fixture();
        await Settle();
        SubscribeCopy(f.Launcher, target, (_, _) => { });
        Copy(f.Launcher, target);
        var texts = Texts(f.Launcher).ToArray();
        var originalOwnerContent = Assert.IsAssignableFrom<Control>(f.Window.Content);
        f.Window.Content = null;
        await Settle(50);
        var detached = texts.ToDictionary(text => text, text => text.Text);
        await Settle(1450);
        foreach (var (text, expected) in detached) Assert.Equal(expected, text.Text);
        AssertShareValues(f.Launcher);
        f.Window.Content = originalOwnerContent;
        f.Launcher.ApplyState(true, true, "RESTORED", "https://remote.example.invalid/join/RESTORED");
        await Settle();
        AssertShareValues(f.Launcher, "RESTORED", "https://remote.example.invalid/join/RESTORED");
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
    }

    [AvaloniaFact]
    public async Task DisclosureExposesExpandedStateAndEscapeFromDetailsRestoresItsCommandFocus()
    {
        using var f = new Fixture();
        await Settle();
        f.Launcher.Collapse();
        var command = Named<Button>(f.Launcher, "CollapsedButton");
        await RevealWithKeyboard(f.Window, command);
        Key(f.Window, PhysicalKey.Space);
        await Settle();
        Assert.True(f.Launcher.IsExpanded);
        bool hasExpandedState = command is ToggleButton toggle && toggle.IsChecked == true
            || AutomationProperties.GetItemStatus(command)?.Contains("expanded", StringComparison.OrdinalIgnoreCase) == true
            || AutomationProperties.GetName(command)?.Contains("collapse", StringComparison.OrdinalIgnoreCase) == true;
        Assert.True(hasExpandedState, "Disclosure must expose its expanded state through native toggle or accessible state/action");
        var copy = Named<Button>(f.Launcher, "CopyUrlButton");
        await RevealWithKeyboard(f.Window, copy);
        Key(f.Window, PhysicalKey.Escape);
        await Settle();
        Assert.False(f.Launcher.IsExpanded);
        Assert.True(command.IsFocused);
        AssertShareValues(f.Launcher);
    }

    [AvaloniaTheory]
    [InlineData(760, 560)]
    [InlineData(420, 320)]
    public async Task CompactTrainingDisclosureKeepsTheLiveReadoutInPlaceAndAllRemoteActionsReachable(int width, int height)
    {
        var session = new TrainingSession();
        session.AddTimer(new TrainingTimer("Current drill", TimeSpan.FromMinutes(3)));
        var panel = new TrainingControlPanel();
        panel.Bind(session);
        var launcher = panel.FindControl<CloudRemoteLauncher>("TrainingCloudLauncher")!;
        launcher.ApplyState(true, true, Code, Url);
        launcher.ApplyConnection(true, true);
        int exits = 0, ends = 0;
        panel.ExitRequested += () => exits++;
        launcher.EndRequested += (_, _) => ends++;
        var window = new NativeWindow { Width = width, Height = height, Content = panel };
        window.Show();
        try
        {
            await Settle();
            var liveReadout = panel.FindControl<TextBlock>("ActiveRemainingText")!;
            var header = Named<Button>(launcher, "CollapsedButton");
            await RevealWithKeyboard(window, header);
            var before = liveReadout.TranslatePoint(default, window);
            Assert.NotNull(before);
            Key(window, PhysicalKey.Space);
            await Settle();
            Assert.True(launcher.IsExpanded);
            var after = liveReadout.TranslatePoint(default, window);
            Assert.NotNull(after);
            Assert.InRange(Math.Abs(after.Value.Y - before.Value.Y), 0, 1);
            foreach (var (command, label) in new[]
            {
                (CodeCommand(launcher), "Copy join code"),
                ((Control)Named<Button>(launcher, "CopyUrlButton"), "Copy URL"),
                ((Control)Named<Button>(launcher, "EndButton"), "End session")
            })
            {
                await RevealWithKeyboard(window, command);
                AssertInsideHost(command, label);
            }
            Assert.Equal(0, ends);
            Assert.Equal(0, exits);
            Assert.Same(session.Timers[0], session.Active);
            Assert.False(session.IsRunning);
            Key(window, PhysicalKey.Escape);
            await Settle();
            Assert.False(launcher.IsExpanded);
            Assert.Equal(0, exits);
            Assert.True(header.IsFocused);
            AssertShareValues(launcher);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task ExplicitHostRejectionKeepsShareValuesAndReportsFailureWithoutAnException(string target)
    {
        using var f = new Fixture();
        await Settle();
        int calls = 0;
        SubscribeCopy(f.Launcher, target, (_, args) =>
        {
            calls++;
            var request = Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args);
            Assert.Equal(target == "code" ? Code : Url, request.Value);
            request.Succeeded = false;
        });
        Copy(f.Launcher, target);
        Assert.Equal(1, calls);
        AssertShareValues(f.Launcher);
        Assert.DoesNotContain(Texts(f.Launcher), IsCopied);
        Assert.Contains("Could not copy", Named<TextBlock>(f.Launcher, "CopyFeedbackText").Text);
    }

    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("URL")]
    public async Task CopyCapturesDisplayedValueAndCannotPublishSuccessForAStateReplacedInsideItsHandler(string target)
    {
        using var f = new Fixture();
        await Settle();
        string? captured = null;
        SubscribeCopy(f.Launcher, target, (_, args) =>
        {
            var request = Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args);
            captured = request.Value;
            f.Launcher.ApplyState(true, true, "REPLACED", "https://remote.example.invalid/join/REPLACED");
            f.Launcher.ApplyConnection(false, true);
            request.Succeeded = true;
            Assert.Equal(target == "code" ? Code : Url, request.Value);
        });
        Copy(f.Launcher, target);
        Assert.Equal(target == "code" ? Code : Url, captured);
        AssertShareValues(f.Launcher, "REPLACED", "https://remote.example.invalid/join/REPLACED");
        Assert.True(string.IsNullOrEmpty(Named<TextBlock>(f.Launcher, "CopyFeedbackText").Text));
        await Settle(1450);
        AssertShareValues(f.Launcher, "REPLACED", "https://remote.example.invalid/join/REPLACED");
        Assert.Contains("reconnecting", Named<TextBlock>(f.Launcher, "StatusText").Text, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task AnUnchangedHostRefreshPreservesCurrentCopyFeedbackUntilItsOwnedTimeout()
    {
        using var f = new Fixture();
        await Settle();
        SubscribeCopy(f.Launcher, "code", (_, args) => Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args).Succeeded = true);
        Copy(f.Launcher, "code");
        f.Launcher.ApplyState(true, true, Code, Url);
        f.Launcher.ApplyConnection(true, true);
        Assert.Contains("copied", Named<TextBlock>(f.Launcher, "CopyFeedbackText").Text, StringComparison.OrdinalIgnoreCase);
        await Settle(1450);
        Assert.True(string.IsNullOrEmpty(Named<TextBlock>(f.Launcher, "CopyFeedbackText").Text));
        AssertShareValues(f.Launcher);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisablingTheInteractionOwnerClosesDetailsAndCannotStealCommandSearchFocus(bool duringCopy)
    {
        var launcher = new CloudRemoteLauncher { Width = 300 };
        var scope = new Border { Child = launcher, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var search = new TextBox { Text = "Command search", Width = 240, IsVisible = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var root = new Grid(); root.Children.Add(scope); root.Children.Add(search);
        var window = new NativeWindow { Width = 680, Height = 680, Content = root };
        launcher.ApplyState(true, true, Code, Url);
        int copies = 0;
        void DisableForCommandScope()
        {
            scope.IsEnabled = false;
            search.IsVisible = true;
            Assert.True(search.Focus(NavigationMethod.Tab));
        }
        launcher.CopyJoinCodeRequested += (_, args) =>
        {
            copies++;
            if (duringCopy) DisableForCommandScope();
            Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args).Succeeded = true;
        };
        window.Show();
        try
        {
            await Settle(); launcher.Expand(); await Settle(50);
            Copy(launcher, "code");
            if (!duringCopy) DisableForCommandScope();
            Assert.Equal(1, copies);
            Assert.False(launcher.IsExpanded);
            Assert.False(launcher.IsDetailsAnimating);
            Assert.True(search.IsFocused);
            Assert.True(string.IsNullOrEmpty(Named<TextBlock>(launcher, "CopyFeedbackText").Text));
            launcher.Expand(); // A late host callback cannot reopen a disabled console.
            Assert.False(launcher.IsExpanded);
            await Settle(1450);
            Assert.True(search.IsFocused);
            AssertShareValues(launcher);
            Assert.True(string.IsNullOrEmpty(Named<TextBlock>(launcher, "CopyFeedbackText").Text));
        }
        finally { window.Close(); }
    }

    private sealed class AppearanceScope : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled, reduced = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animationOverride = OperatorMotion.AnimationOverride;
        private readonly byte[]? settings = File.Exists(OperatorAppearance.StoragePath) ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;
        private readonly byte[]? backup = File.Exists(OperatorAppearance.StoragePath + ".bak") ? File.ReadAllBytes(OperatorAppearance.StoragePath + ".bak") : null;

        internal AppearanceScope(bool reduce, OperatorMotionStyle motionStyle = OperatorMotionStyle.Expressive, bool enableGlass = true)
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(enableGlass, reduce);
            OperatorAppearance.ConfigureMotionStyle(motionStyle);
        }

        public void Dispose()
        {
            OperatorAppearance.Configure(glass, reduced);
            OperatorAppearance.ConfigureMotionStyle(style);
            OperatorMotion.AnimationOverride = animationOverride;
            if (settings is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, settings);
            if (backup is null) File.Delete(OperatorAppearance.StoragePath + ".bak");
            else File.WriteAllBytes(OperatorAppearance.StoragePath + ".bak", backup);
        }
    }

    private static double ChevronAngle(CloudRemoteLauncher launcher)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        // WPF generates a field; the native name registry exposes transforms
        // through properties. Read the same actual named transform in either.
        var transform = typeof(CloudRemoteLauncher).GetField("ChevronRotate", flags)?.GetValue(launcher)
            ?? typeof(CloudRemoteLauncher).GetProperty("ChevronRotate", flags)?.GetValue(launcher);
        Assert.NotNull(transform);
        return Assert.IsType<double>(transform.GetType().GetProperty("Angle")!.GetValue(transform));
    }

    private static void CaptureDetails(CloudRemoteLauncher launcher, string name)
    {
        var host = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(Named<OperatorGlassSurface>(launcher, "DetailsSurface")));
        string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-cloud-launcher");
        Directory.CreateDirectory(directory);
        using var frame = host.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyboardOpeningEntersTheFirstSafeActionAndRealTabTraversalStaysInTheDisclosure(bool reduced)
    {
        using var preferences = new AppearanceScope(reduced);
        using var f = new Fixture();
        await Settle(); f.Launcher.Collapse(); await Settle(200);
        var header = Named<Button>(f.Launcher, "CollapsedButton");
        await RevealWithKeyboard(f.Window, header);
        Key(f.Window, PhysicalKey.Space);
        await Settle(100);
        Assert.True(Named<Button>(f.Launcher, "CopyCodeButton").IsFocused);
        foreach (string next in new[] { "CopyUrlButton", "EndButton", "DetailsCloseButton", "CopyCodeButton" })
        {
            Key(f.Window, PhysicalKey.Tab);
            await Settle(30);
            Assert.True(Named<Button>(f.Launcher, next).IsFocused, "Tab must move to " + next);
        }
        Key(f.Window, PhysicalKey.Escape);
        await Settle(180);
        Assert.False(f.Launcher.IsExpanded);
        Assert.True(header.IsFocused);
    }

    [AvaloniaFact]
    public async Task ReducedMotionHasFinalPoseAndProgrammaticExpansionDoesNotMoveExistingKeyboardFocus()
    {
        using var preferences = new AppearanceScope(true);
        using var f = new Fixture();
        await Settle(); f.Launcher.Collapse();
        var outside = new TextBox { Text = "An independent command draft", Width = 240,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        f.Host.Children.Add(outside); await Settle(); Assert.True(outside.Focus(NavigationMethod.Tab));
        f.Launcher.Expand(); await Settle();
        Assert.True(outside.IsFocused);
        Assert.False(f.Launcher.IsDetailsAnimating);
        Assert.Equal(new ExpansionOrigin(1, 1, 0, 0), f.Launcher.CurrentDetailsPose);
        Assert.Equal(180, ChevronAngle(f.Launcher), 3);
        AssertShareValues(f.Launcher);
        f.Launcher.Collapse();
        Assert.False(f.Launcher.IsDetailsAnimating);
        Assert.Equal(0, ChevronAngle(f.Launcher), 3);
        Assert.True(outside.IsFocused);
    }

    [AvaloniaTheory]
    [InlineData("Calm")]
    [InlineData("Expressive")]
    public async Task SourceAnchoredMotionHasABoundedEarlyPoseAndSettlesWithoutMovingTheHeader(string style)
    {
        using var preferences = new AppearanceScope(false, style == "Calm" ? OperatorMotionStyle.Calm : OperatorMotionStyle.Expressive);
        using var f = new Fixture();
        var header = Named<Button>(f.Launcher, "CollapsedButton");
        await Settle(20);
        AssertOpen(f.Launcher);
        var bounds = header.Bounds;
        var early = f.Launcher.CurrentDetailsPose;
        Assert.True(f.Launcher.IsDetailsAnimating);
        Assert.InRange(early.ScaleX, .95, 1);
        Assert.InRange(early.ScaleY, style == "Calm" ? .975 : .93, 1);
        Assert.True(early.ScaleY < .9999 || Math.Abs(early.OffsetY) > .001, "The early material pose must differ from its settled pose");
        Assert.InRange(Math.Abs(early.OffsetX), 0, style == "Calm" ? 4.81 : 12.01);
        Assert.InRange(Math.Abs(early.OffsetY), 0, style == "Calm" ? 6.41 : 16.01);
        AssertShareValues(f.Launcher);
        var surface = Named<OperatorGlassSurface>(f.Launcher, "DetailsSurface");
        // Geometry follows the disclosure command. Material sampling has a
        // separate contract and must never use this interactive header tree.
        Assert.Same(header, Named<Popup>(f.Launcher, "DetailsPopup").PlacementTarget);
        Assert.InRange(surface.Opacity, .9, 1);
        CaptureDetails(f.Launcher, "cloud-motion-" + style + "-early");
        await Settle(650);
        Assert.True(f.Launcher.IsExpanded);
        Assert.False(f.Launcher.IsDetailsAnimating);
        Assert.Equal(new ExpansionOrigin(1, 1, 0, 0), f.Launcher.CurrentDetailsPose);
        Assert.Equal(180, ChevronAngle(f.Launcher), 3);
        Assert.Equal(bounds, header.Bounds);
        CaptureDetails(f.Launcher, "cloud-motion-" + style + "-settled");
    }

    private static void AssertOpaqueOwnerlessMaterial(CloudRemoteLauncher launcher, Window owner)
    {
        Assert.Null(OperatorDesign.GetBackdrop(owner));
        var surface = Named<OperatorGlassSurface>(launcher, "DetailsSurface");
        Assert.Equal(GlassMaterial.Opaque, surface.Material);
        Assert.Null(surface.GlassSource);
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(NativeResources.FindOrNull(surface, "ControlCentre.SurfaceBrush"));
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background);
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal((byte)255, actual.Color.A);
        Assert.Same(Named<Button>(launcher, "CollapsedButton"), Named<Popup>(launcher, "DetailsPopup").PlacementTarget);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Light, true)]
    [InlineData(AppTheme.Dark, true)]
    [InlineData(AppTheme.Light, false)]
    [InlineData(AppTheme.Dark, false)]
    public async Task OwnerWithoutDecorativeBackdropUsesOpaqueSemanticMaterialAcrossReopenAndPreferenceChange(AppTheme theme, bool glass)
    {
        var app = (App)Application.Current!;
        var previousTheme = app.ThemeService.CurrentTheme;
        using var appearance = new AppearanceScope(true, enableGlass: glass);
        try
        {
            app.ThemeService.SetTheme(theme);
            using var f = new Fixture();
            await Settle(150);
            AssertOpen(f.Launcher);
            // A launcher-local scene is not a substitute for an owner-sized
            // decorative backdrop, even when one was injected into this view.
            AssertOpaqueOwnerlessMaterial(f.Launcher, f.Window);
            AssertShareValues(f.Launcher);
            OperatorAppearance.Configure(!glass, true);
            await Settle(60);
            AssertOpen(f.Launcher);
            AssertOpaqueOwnerlessMaterial(f.Launcher, f.Window);
            f.Launcher.Collapse();
            await Settle(50);
            Assert.Null(Named<OperatorGlassSurface>(f.Launcher, "DetailsSurface").GlassSource);
            f.Launcher.Expand();
            await Settle(120);
            AssertOpen(f.Launcher);
            AssertOpaqueOwnerlessMaterial(f.Launcher, f.Window);
            AssertShareValues(f.Launcher);
        }
        finally { app.ThemeService.SetTheme(previousTheme); }
    }

    private sealed class ProductionMaterialFixture : IAsyncDisposable
    {
        private readonly App app = (App)Application.Current!;
        private readonly bool previousSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly AppTheme previousTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly OperatorGlassIntensity previousIntensity = OperatorAppearance.GlassIntensity;
        private readonly OperatorBackdropStyle previousBackdrop = OperatorAppearance.Backdrop;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-cloud-material-contract-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]?> files = new();
        private AppearanceScope? appearance;
        private Roche_Scoreboard.MainWindow? window;
        internal Roche_Scoreboard.MainWindow Window => window!;
        internal TrainingControlPanel Panel { get; private set; } = null!;
        internal CloudRemoteLauncher Launcher { get; private set; } = null!;

        internal static async Task<ProductionMaterialFixture> Create(int width, int height)
        {
            var fixture = new ProductionMaterialFixture();
            try
            {
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", fixture.dataHome);
                Directory.CreateDirectory(SelfTestStorage.DocumentsDirectory);
                string workflow = (string)typeof(OperatorWorkflowPreferences)
                    .GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false])!;
                foreach (string path in new[]
                {
                    OperatorAppearance.StoragePath,
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json"),
                    UxPreferences.GetStoragePath(), workflow, GameStateService.RecoveryStatePath,
                    Path.Combine(SelfTestStorage.DocumentsDirectory, "training_recent_timers.json")
                }.Distinct())
                    foreach (string candidate in new[] { path, path + ".bak" })
                        fixture.files[candidate] = File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;

                fixture.app.IsSelfTestMode = true;
                fixture.appearance = new AppearanceScope(true, enableGlass: true);
                OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid, OperatorBackdropStyle.Colour);
                fixture.app.ThemeService.SetTheme(AppTheme.Dark);
                fixture.window = new Roche_Scoreboard.MainWindow();
                fixture.Window.Show();
                await Settle(180);
                fixture.Window.Width = width;
                fixture.Window.Height = height;
                await Settle(100);
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                var selectSport = fixture.Window.GetType().GetMethod("OnSportSelected", hidden)!;
                selectSport.Invoke(fixture.Window, [Enum.Parse(selectSport.GetParameters()[0].ParameterType, "Training")]);
                await Settle(200);
                fixture.Panel = Assert.IsType<TrainingControlPanel>(fixture.Window.FindControl<TrainingControlPanel>("TrainingControlPanelView"));
                Assert.True(fixture.Panel.IsEffectivelyVisible);
                var session = Assert.IsType<TrainingSession>(fixture.Window.GetType().GetField("_trainingSession", hidden)!.GetValue(fixture.Window));
                session.AddTimer(new TrainingTimer("Material source contract", TimeSpan.FromMinutes(3)));
                fixture.Launcher = Assert.IsType<CloudRemoteLauncher>(fixture.Panel.FindControl<CloudRemoteLauncher>("TrainingCloudLauncher"));
                fixture.Launcher.ApplyState(true, true, Code, Url);
                fixture.Launcher.ApplyConnection(true, true);
                await Settle(120);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                window?.Close();
                await Settle(100);
                app.ThemeService.SetTheme(previousTheme);
                OperatorAppearance.ConfigureMaterial(previousIntensity, previousBackdrop);
                appearance?.Dispose();
                foreach (var (path, bytes) in files)
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }
            finally
            {
                app.IsSelfTestMode = previousSelfTest;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome);
                if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true);
            }
        }
    }

    private static void AssertProductionOwnerMaterial(ProductionMaterialFixture fixture, OperatorSceneBackdrop expectedScene)
    {
        var surface = Named<OperatorGlassSurface>(fixture.Launcher, "DetailsSurface");
        var header = Named<Button>(fixture.Launcher, "CollapsedButton");
        Assert.Same(expectedScene, OperatorDesign.GetBackdrop(fixture.Window));
        Assert.Same(expectedScene, surface.GlassSource);
        Assert.Equal(GlassMaterial.Regular, surface.Material);
        Assert.False(expectedScene.IsHitTestVisible);
        Assert.Same(fixture.Window, TopLevel.GetTopLevel(expectedScene));
        Assert.NotSame(Named<Control>(fixture.Launcher, "HeaderSurface"), surface.GlassSource);
        Assert.NotSame(fixture.Window.Content, surface.GlassSource);
        Assert.NotSame(fixture.Panel, surface.GlassSource);
        Assert.True(expectedScene.Bounds.Width > header.Bounds.Width);
        Assert.True(expectedScene.Bounds.Height > header.Bounds.Height);
        Assert.Same(header, Named<Popup>(fixture.Launcher, "DetailsPopup").PlacementTarget);
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    public async Task ProductionDisclosureSamplesOnlyItsFullOwnerSceneAcrossPreferenceChangeAndReopen(int width, int height)
    {
        await using var fixture = await ProductionMaterialFixture.Create(width, height);
        var expectedScene = Assert.IsType<OperatorSceneBackdrop>(OperatorDesign.GetBackdrop(fixture.Window));
        var header = Named<Button>(fixture.Launcher, "CollapsedButton");
        var headerBounds = header.Bounds;
        var liveReadout = Assert.IsType<TextBlock>(fixture.Panel.FindControl<TextBlock>("ActiveRemainingText"));
        var livePosition = liveReadout.TranslatePoint(default, fixture.Window);
        fixture.Launcher.Expand();
        await Settle(120);
        AssertOpen(fixture.Launcher);
        AssertProductionOwnerMaterial(fixture, expectedScene);
        Assert.Equal(headerBounds, header.Bounds);
        Assert.Equal(livePosition, liveReadout.TranslatePoint(default, fixture.Window));
        AssertShareValues(fixture.Launcher);

        OperatorAppearance.Configure(false, true);
        await Settle(60);
        AssertOpen(fixture.Launcher);
        AssertProductionOwnerMaterial(fixture, expectedScene);
        fixture.Launcher.Collapse();
        await Settle(50);
        Assert.Null(Named<OperatorGlassSurface>(fixture.Launcher, "DetailsSurface").GlassSource);
        const string nextCode = "DEF456";
        const string nextUrl = "https://remote.example.invalid/join/DEF456";
        fixture.Launcher.ApplyState(true, true, nextCode, nextUrl);
        OperatorAppearance.Configure(true, true);
        fixture.Launcher.Expand();
        await Settle(120);
        AssertOpen(fixture.Launcher);
        AssertProductionOwnerMaterial(fixture, expectedScene);
        AssertShareValues(fixture.Launcher, nextCode, nextUrl);
        Assert.Equal(headerBounds, header.Bounds);
        Assert.Equal(livePosition, liveReadout.TranslatePoint(default, fixture.Window));
    }

    [AvaloniaFact]
    public async Task RapidReversalsAndLiveReducedMotionCannotLeaveOldCloseOrUnloadWorkAttached()
    {
        using var preferences = new AppearanceScope(false);
        using var f = new Fixture();
        await Settle(20);
        for (int i = 0; i < 4; i++) { f.Launcher.Collapse(); await Settle(20); f.Launcher.Expand(); await Settle(20); }
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
        Assert.True(f.Launcher.IsExpanded);
        Assert.False(f.Launcher.IsDetailsAnimating);
        Assert.Equal(new ExpansionOrigin(1, 1, 0, 0), f.Launcher.CurrentDetailsPose);
        await Settle(600);
        Assert.True(f.Launcher.IsExpanded);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, false);
        f.Launcher.Collapse(); f.Launcher.Expand();
        f.Window.Content = null;
        await Settle(650);
        Assert.False(f.Launcher.IsExpanded);
        Assert.False(f.Launcher.IsDetailsAnimating);
        Assert.Equal(new ExpansionOrigin(1, 1, 0, 0), f.Launcher.CurrentDetailsPose);
        Assert.Null(Named<OperatorGlassSurface>(f.Launcher, "DetailsSurface").GlassSource);
        AssertShareValues(f.Launcher);
    }

    [AvaloniaFact]
    public async Task ResizeClosureDoesNotConsumeTheNextActualHeaderClick()
    {
        using var preferences = new AppearanceScope(true);
        using var f = new Fixture();
        await Settle();
        var header = Named<Button>(f.Launcher, "CollapsedButton");
        var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseMove(point);
        f.Window.Width += 20; await Settle();
        Assert.False(f.Launcher.IsExpanded);
        point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), f.Window)!.Value;
        f.Window.MouseDown(point, MouseButton.Left); f.Window.MouseUp(point, MouseButton.Left);
        await Settle();
        Assert.True(f.Launcher.IsExpanded);
        AssertShareValues(f.Launcher);
    }

    [AvaloniaTheory]
    [InlineData("start")]
    [InlineData("end")]
    public async Task ConsequentialHostRequestDispatchesOnceAfterTheLocalDetailScopeHasClosed(string action)
    {
        using var preferences = new AppearanceScope(true);
        using var f = new Fixture();
        await Settle();
        if (action == "start") f.Launcher.ApplyState(true, false, null, null);
        int calls = 0;
        EventHandler handler = (_, _) =>
        {
            calls++;
            Assert.False(f.Launcher.IsExpanded);
            Assert.False(f.Launcher.IsDetailsAnimating);
            Assert.False(Named<StackPanel>(f.Launcher, "ExpandedPanel").IsVisible);
        };
        if (action == "start") f.Launcher.StartRequested += handler; else f.Launcher.EndRequested += handler;
        var command = Named<Button>(f.Launcher, action == "start" ? "StartButton" : "EndButton");
        Click(command);
        Click(command); // A detached/repeated callback must not dispatch again.
        Assert.Equal(1, calls);
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double v = value / 255d;
            return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Light, true)]
    [InlineData(AppTheme.Dark, true)]
    [InlineData(AppTheme.Light, false)]
    [InlineData(AppTheme.Dark, false)]
    public async Task PopupTextAndCopyFailureUseReadableThemeColorsWithGlassAndSolidFallback(AppTheme theme, bool glass)
    {
        var app = (App)Application.Current!;
        var previous = app.ThemeService.CurrentTheme;
        using var preferences = new AppearanceScope(true, enableGlass: glass);
        try
        {
            app.ThemeService.SetTheme(theme);
            using var f = new Fixture(); await Settle(180);
            SubscribeCopy(f.Launcher, "code", (_, args) => Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args).Succeeded = false);
            Copy(f.Launcher, "code");
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(Named<Border>(f.Launcher, "HeaderSurface").Background).Color;
            var title = Named<Grid>(f.Launcher, "DetailsHeader").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Cloud remote");
            foreach (var text in new[] { title, Named<TextBlock>(f.Launcher, "StatusText"), Named<TextBlock>(f.Launcher, "CopyFeedbackText") })
            {
                var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
                Assert.True(Contrast(foreground, background) >= 4.5, "Unbacked popup text must contrast with its semantic surface in " + theme);
            }
            Assert.Contains("Could not copy", Named<TextBlock>(f.Launcher, "CopyFeedbackText").Text);
            AssertShareValues(f.Launcher);
            CaptureDetails(f.Launcher, "cloud-theme-" + theme + "-" + (glass ? "glass" : "solid"));
        }
        finally { app.ThemeService.SetTheme(previous); }
    }


    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    public async Task CaptureExistingTrainingLauncherDisclosureWithoutAssumingItsPresentationMechanism(int width, int height)
    {
        // Capture-only contract intentionally uses APIs and names from before
        // the popup change. Root can execute this method against frozen old IL.
        var session = new TrainingSession();
        session.AddTimer(new TrainingTimer("Warm-up and kicking accuracy", TimeSpan.FromMinutes(3)));
        var panel = new TrainingControlPanel(); panel.Bind(session);
        var launcher = panel.FindControl<CloudRemoteLauncher>("TrainingCloudLauncher")!;
        launcher.ApplyState(true, true, Code, Url);
        launcher.ApplyConnection(true, true);
        var window = new NativeWindow { Width = width, Height = height, Content = panel };
        window.Show();
        try
        {
            await Settle(180);
            CaptureHost(panel, "cloud-training-source-" + width);
            launcher.Expand();
            await Settle(650);
            CaptureHost(panel, "cloud-training-expanded-owner-" + width);
            CaptureHost(Named<Control>(launcher, "ExpandedPanel"), "cloud-training-expanded-details-" + width);
        }
        finally { window.Close(); }
    }

    private static void CaptureHost(Control control, string name)
    {
        var host = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "roche-cloud-launcher");
        Directory.CreateDirectory(directory);
        using var frame = host.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(1280, 850)]
    [InlineData(420, 320)]
    [Trait("Evidence", "CaptureOnly")]
    public async Task CaptureProductionMainWindowTrainingDisclosureAndMaterialSources(int width, int height)
    {
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        // Use the production sport-selection route and operator backdrop. The
        // generic NativeWindow comparison above deliberately remains unchanged.
        var app = (App)Application.Current!;
        bool previousSelfTest = app.IsSelfTestMode;
        var previousTheme = app.ThemeService.CurrentTheme;
        var previousIntensity = OperatorAppearance.GlassIntensity;
        var previousBackdrop = OperatorAppearance.Backdrop;
        string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        string dataHome = Path.Combine(Path.GetTempPath(), "roche-cloud-production-capture-" + Guid.NewGuid().ToString("N"));
        var files = new Dictionary<string, byte[]?>();
        AppearanceScope? appearance = null;
        Roche_Scoreboard.MainWindow? window = null;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", dataHome);
            Directory.CreateDirectory(SelfTestStorage.DocumentsDirectory);
            // SelfTestStorage can already be process-latched by another case;
            // preserve those actual paths as well as isolating native paths.
            string workflow = (string)typeof(OperatorWorkflowPreferences)
                .GetMethod("GetStoragePath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [false])!;
            foreach (string path in new[]
            {
                OperatorAppearance.StoragePath,
                Path.Combine(SelfTestStorage.DocumentsDirectory, "window_layout.json"),
                UxPreferences.GetStoragePath(),
                workflow,
                GameStateService.RecoveryStatePath,
                Path.Combine(SelfTestStorage.DocumentsDirectory, "training_recent_timers.json")
            }.Distinct())
            {
                foreach (string candidate in new[] { path, path + ".bak" })
                    files[candidate] = File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;
            }

            app.IsSelfTestMode = true; // Production Loaded skips remote startup.
            appearance = new AppearanceScope(false, enableGlass: true);
            OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid, OperatorBackdropStyle.Colour);
            app.ThemeService.SetTheme(AppTheme.Dark);
            window = new Roche_Scoreboard.MainWindow();
            window.Show();
            await Settle(200);
            window.Width = width;
            window.Height = height;
            await Settle(120);

            var selectSport = window.GetType().GetMethod("OnSportSelected", hidden)!;
            selectSport.Invoke(window, [Enum.Parse(selectSport.GetParameters()[0].ParameterType, "Training")]);
            await Settle(650);
            var panel = Assert.IsType<TrainingControlPanel>(window.FindControl<TrainingControlPanel>("TrainingControlPanelView"));
            Assert.True(panel.IsEffectivelyVisible);
            var session = Assert.IsType<TrainingSession>(window.GetType().GetField("_trainingSession", hidden)!.GetValue(window));
            session.AddTimer(new TrainingTimer("Warm-up and kicking accuracy", TimeSpan.FromMinutes(3)));
            var launcher = Assert.IsType<CloudRemoteLauncher>(panel.FindControl<CloudRemoteLauncher>("TrainingCloudLauncher"));
            // Fake presentation state only: never start a cloud session, copy,
            // contact a service, or invoke a consequential remote command.
            launcher.ApplyState(true, true, Code, Url);
            launcher.ApplyConnection(true, true);
            await Settle(180);
            CaptureHost(panel, "cloud-production-training-source-" + width);
            launcher.Expand();
            await Settle(35);
            CaptureHost(panel, "cloud-production-training-expanded-early-owner-" + width);
            await Settle(650);
            AssertOpen(launcher);
            CaptureHost(panel, "cloud-production-training-expanded-owner-" + width);
            CaptureHost(Named<Control>(launcher, "ExpandedPanel"), "cloud-production-training-expanded-details-" + width);

            var surface = Named<OperatorGlassSurface>(launcher, "DetailsSurface");
            string Identity(object? value) => value is Control control
                ? control.GetType().FullName + " name=" + control.Name
                : value?.GetType().FullName ?? "null";
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "cloud-production-training-material-" + width + ".txt"),
            [
                "Capture-only diagnostic; source identity is not a material-quality assertion.",
                "Owner: " + Identity(window),
                "Details TopLevel: " + Identity(TopLevel.GetTopLevel(surface)),
                "Anchor HeaderSurface: " + Identity(Named<Control>(launcher, "HeaderSurface")),
                "Explicit GlassSource: " + Identity(surface.GlassSource),
                "Owner backdrop: " + Identity(OperatorDesign.GetBackdrop(panel)),
                "Details inherited backdrop: " + Identity(OperatorDesign.GetBackdrop(surface)),
                "GlassSource backdrop: " + (surface.GlassSource is { } source ? Identity(OperatorDesign.GetBackdrop(source)) : "null"),
                "Theme=Dark; glass=true; intensity=Vivid; backdrop=Colour; motion=Expressive.",
                launcher.DisclosureDiagnostics
            ]);
        }
        finally
        {
            try
            {
                window?.Close();
                await Settle(100);
                app.ThemeService.SetTheme(previousTheme);
                OperatorAppearance.ConfigureMaterial(previousIntensity, previousBackdrop);
                appearance?.Dispose();
                foreach (var (path, bytes) in files)
                {
                    if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                }
            }
            finally
            {
                app.IsSelfTestMode = previousSelfTest;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome);
                if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true);
            }
        }
    }


    [AvaloniaFact]
    public async Task ImmediateProgrammaticExpandSurvivesTheOwnersFirstLayoutWithoutAFixtureReopen()
    {
        using var preferences = new AppearanceScope(true);
        using var f = new Fixture(); // Expand is deliberately immediate after Show.
        Assert.True(f.Launcher.IsExpanded, f.Launcher.DisclosureDiagnostics);
        await Settle(150);
        AssertOpen(f.Launcher);
        int copies = 0;
        SubscribeCopy(f.Launcher, "code", (_, args) =>
        {
            copies++;
            Assert.IsType<CloudRemoteLauncher.CopyRequestEventArgs>(args).Succeeded = true;
        });
        Copy(f.Launcher, "code");
        Assert.Equal(1, copies);
        AssertShareValues(f.Launcher);
    }

}
