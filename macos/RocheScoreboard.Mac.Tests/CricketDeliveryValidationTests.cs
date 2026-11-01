// Outside-repository D1-only regression draft. Self-contained test source,
// authored for the current xUnit/Avalonia project; NOT compiled or run here.
// No private Show/Embed/Apply/preview invocation and no private state writes.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[CollectionDefinition("Cricket delivery D1 draft", DisableParallelization = true)]
public sealed class CricketDeliveryD1DraftCollection { }

[Collection("Cricket delivery D1 draft")]
public sealed class CricketDeliveryD1RegressionDraftTests
{
    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);
    private static string State(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static bool Within(Control root, IInputElement? target) => ReferenceEquals(root, target)
        || target is Visual visual && visual.GetVisualAncestors().Contains(root);
    private static async Task Settle(int milliseconds = 100)
    { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++) await Settle();
        Assert.True(condition(), message);
    }
    private static void KeyNow(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    { window.KeyPressQwerty(key, modifiers); window.KeyReleaseQwerty(key, modifiers); }
    private static async Task Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    { KeyNow(window, key, modifiers); await Settle(); }
    private static Rect Painted(Window window, Control control)
    {
        control.UpdateLayout();
        var points = new[] { new Point(), new Point(control.Bounds.Width, 0),
            new Point(0, control.Bounds.Height), new Point(control.Bounds.Width, control.Bounds.Height) }
            .Select(point => control.TranslatePoint(point, window)).ToArray();
        Assert.All(points, point => Assert.NotNull(point));
        double left = points.Min(point => point!.Value.X), top = points.Min(point => point!.Value.Y);
        return new Rect(left, top, points.Max(point => point!.Value.X) - left,
            points.Max(point => point!.Value.Y) - top);
    }
    private static void AssertFullPaintedBounds(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible && control.IsLoaded);
        Rect box = Painted(window, control);
        Assert.True(box.Width > 0 && box.Height > 0, control.Name + " must have actual painted geometry.");
        Assert.True(new Rect(window.ClientSize).Contains(box), control.Name + " must be fully inside the actual owner: " + box);
        // A scroller can clip a control even when the control is inside Window.
        foreach (var ancestor in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var viewport = ancestor.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().FirstOrDefault();
            if (viewport is not null)
                Assert.True(Painted(window, viewport).Contains(box), control.Name + " must fit the current scroll viewport, not merely Window.");
        }
    }
    private static bool Ready(Window window, Control control)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled || !control.IsHitTestVisible || !control.IsLoaded
            || !control.GetVisualAncestors().Contains(window) || control.Bounds.Width <= 8 || control.Bounds.Height <= 8) return false;
        var first = control.TranslatePoint(default, window);
        if (first is null) return false;
        var box = Painted(window, control);
        if (!new Rect(window.ClientSize).Contains(box)) return false;
        return new[] { box.Center, new Point(box.Left + 4, box.Center.Y), new Point(box.Right - 4, box.Center.Y),
            new Point(box.Center.X, box.Top + 4), new Point(box.Center.X, box.Bottom - 4) }
            .All(point => Within(control, window.InputHitTest(point)));
    }
    private static async Task StableReachable(Window window, Control control)
    {
        Rect? previous = null; int stableFrames = 0;
        await Until(() =>
        {
            using var rendered = window.CaptureRenderedFrame();
            if (!Ready(window, control)) { stableFrames = 0; return false; }
            var box = Painted(window, control);
            stableFrames = previous is { } old && Math.Abs(old.X - box.X) < .1 && Math.Abs(old.Y - box.Y) < .1
                && Math.Abs(old.Width - box.Width) < .1 && Math.Abs(old.Height - box.Height) < .1 ? stableFrames + 1 : 0;
            previous = box; return stableFrames >= 2;
        }, "Actual action must own all five pointer probes in consecutive rendered frames: " + control.Name);
    }
    private static async Task Tap(Window window, Control control)
    {
        await StableReachable(window, control);
        var box = Painted(window, control);
        window.MouseMove(box.Center); window.MouseDown(box.Center, MouseButton.Left); window.MouseUp(box.Center, MouseButton.Left);
        await Settle();
    }
    // Explicit fixture seam from the existing selector tests: hold real Tab,
    // use public focus/BringIntoView and positively check reachability/focus.
    // This is not a claim of complete sequential Tab traversal.
    private static async Task Reveal(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        try { Assert.True(control.Focus(NavigationMethod.Tab)); control.BringIntoView(); Dispatcher.UIThread.RunJobs(); }
        finally { window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); }
        await Until(() => Ready(window, control), "Explicit public focus/reveal must reach " + control.Name);
        Assert.True(Within(control, window.FocusManager?.GetFocusedElement()));
    }
    private static async Task ReplaceByNativeText(Window window, TextBox field, string raw)
    {
        // Never repairs focus after rejection. The product must have returned it.
        Assert.Same(field, window.FocusManager?.GetFocusedElement());
        await Key(window, PhysicalKey.A, RawInputModifiers.Control);
        await Key(window, PhysicalKey.Backspace);
        if (raw.Length > 0) { window.KeyTextInput(raw); await Settle(); }
        Assert.Equal(raw, field.Text); Assert.Same(field, window.FocusManager?.GetFocusedElement());
    }
    private static async Task ShowCustomRuns(DeliveryFixture fixture, string raw)
    {
        var button = Named<Button>(fixture.Entry, "CustomRunsButton");
        await Reveal(fixture.Window, button); await Tap(fixture.Window, button);
        var field = Named<TextBox>(fixture.Entry, "RunsBox");
        Assert.Same(field, fixture.Window.FocusManager?.GetFocusedElement());
        await Reveal(fixture.Window, field); // pre-input fixture reveal only
        await ReplaceByNativeText(fixture.Window, field, raw);
        CaptureDraft(fixture,"typed",raw);
    }
    private static void CaptureDraft(DeliveryFixture fixture,string stage,string raw)
    {
        string directory=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.GetTempPath(),"delivery-draft-"+fixture.CaptureToken);
        Directory.CreateDirectory(directory);
        using var frame=fixture.Window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,stage+".png"));
        var focused=fixture.Window.FocusManager?.GetFocusedElement() as Control;
        File.WriteAllText(Path.Combine(directory,stage+".json"),JsonSerializer.Serialize(new
        {
            fixture.Source,stage,raw,fixture.Window.ClientSize,FocusType=focused?.GetType().Name,FocusName=focused?.Name,
            Preview=Named<TextBlock>(fixture.Entry,"PreviewText").Text,Error=Named<TextBlock>(fixture.Entry,"ErrorText").Text,
            fixture.ModelChanges,fixture.EditorClosures,fixture.Match.DeliveryRevision,fixture.OpenedRevision,
            Deliveries=fixture.Match.CurrentInnings!.Deliveries.Count
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void AssertUnchanged(DeliveryFixture fixture, string state, int revision, string raw)
    {
        Assert.Equal(state, State(fixture.Match)); Assert.Equal(revision, fixture.Match.DeliveryRevision);
        Assert.Empty(fixture.Match.CurrentInnings!.Deliveries); Assert.Equal(0, fixture.ModelChanges); Assert.Equal(0, fixture.EditorClosures);
        Assert.Equal(raw, Named<TextBox>(fixture.Entry, "RunsBox").Text);
        Assert.True(fixture.Overlay.IsVisible); Assert.Same(fixture.Entry, Named<ContentControl>(fixture.Panel, "DeliveryEntryContent").Content);
    }
    private static void AssertAcceptedWide(DeliveryFixture fixture, int expected)
    {
        var innings = fixture.Match.CurrentInnings!; var delivery = Assert.Single(innings.Deliveries);
        Assert.Equal(CricketDeliveryType.Wide, delivery.Type); Assert.Equal(expected, delivery.Runs);
        Assert.Equal(expected, fixture.Match.TotalRuns); Assert.Equal(expected, innings.Wides);
        Assert.Equal(0, innings.LegalBallsBowled); Assert.Equal(0, fixture.Match.TotalWickets);
        Assert.Equal(1, fixture.ModelChanges); Assert.Equal(1, fixture.EditorClosures); Assert.False(fixture.Overlay.IsVisible);
        Assert.Null(Named<ContentControl>(fixture.Panel, "DeliveryEntryContent").Content);
    }

    // Four expected pre-fix failures. This isolates preview from repair focus,
    // so one early assertion cannot conceal the other contract's baseline.
    [AvaloniaTheory]
    [InlineData("bad", 700, 450)] [InlineData("", 420, 280)]
    [InlineData("-1", 700, 450)] [InlineData("999", 420, 280)]
    public async Task InvalidCustomWidePreviewDoesNotInventAComposition(string raw, int width, int height)
    {
        using var f = await DeliveryFixture.OpenActual("Wide", width, height);
        await ShowCustomRuns(f, raw); string before = f.OpenedState; int revision = f.OpenedRevision;
        AssertUnchanged(f, before, revision, raw);
        Assert.Equal("Check runs before applying.", Named<TextBlock>(f.Entry, "PreviewText").Text);
        var feedback = Named<TextBlock>(f.Entry, "RunsValidationText");
        Assert.True(feedback.IsEffectivelyVisible); Assert.Equal("Enter a whole number from 0 to 998.", feedback.Text);
        Assert.Same(Named<TextBox>(f.Entry, "RunsBox"), f.Window.FocusManager?.GetFocusedElement());
    }

    // Three expected pre-fix focus failures: no fixture reveal/focus after Apply.
    [AvaloniaTheory]
    [InlineData("bad", 700, 450)] [InlineData("-1", 700, 450)] [InlineData("999", 420, 280)]
    public async Task PointerRejectionKeepsTheRawWideDraftAndNativeRepairCommitsOnce(string raw, int width, int height)
    {
        using var f = await DeliveryFixture.OpenActual("Wide", width, height);
        await ShowCustomRuns(f, raw); var runs = Named<TextBox>(f.Entry, "RunsBox");
        string before = f.OpenedState; int revision = f.OpenedRevision;
        await Tap(f.Window, Named<Button>(f.Entry, "ApplyButton"));
        CaptureDraft(f,"rejected",raw);
        AssertUnchanged(f, before, revision, raw);
        Assert.Equal("Enter a whole number from 0 to 998.", Named<TextBlock>(f.Entry, "ErrorText").Text);
        Assert.Same(runs, f.Window.FocusManager?.GetFocusedElement());
        AssertFullPaintedBounds(f.Window, runs);
        var feedback = Named<TextBlock>(f.Entry, "RunsValidationText");
        Assert.True(feedback.IsEffectivelyVisible); Assert.Equal("Enter a whole number from 0 to 998.", feedback.Text);
        AssertFullPaintedBounds(f.Window, Named<Button>(f.Entry, "CancelButton"));
        AssertFullPaintedBounds(f.Window, Named<Button>(f.Entry, "ApplyButton"));
        await ReplaceByNativeText(f.Window, runs, "2");
        Assert.False(feedback.IsVisible); Assert.False(Named<TextBlock>(f.Entry, "ErrorText").IsVisible);
        Assert.Equal("1 wide + 2 completed = 3 wides", Named<TextBlock>(f.Entry, "PreviewText").Text);
        await Key(f.Window, PhysicalKey.Enter); AssertAcceptedWide(f, 3);
    }

    // Two existing-rule controls with the same missing invalid-field recovery.
    [AvaloniaTheory] [InlineData("Bye")] [InlineData("Leg Bye")]
    public async Task ZeroByeRejectionPreservesTheDraftAndCorrectedEnterCommitsOnce(string source)
    {
        using var f = await DeliveryFixture.OpenActual(source, 420, 280);
        await ShowCustomRuns(f, "0"); var runs = Named<TextBox>(f.Entry, "RunsBox");
        string before = f.OpenedState; int revision = f.OpenedRevision;
        await Tap(f.Window, Named<Button>(f.Entry, "ApplyButton")); CaptureDraft(f,"rejected","0"); AssertUnchanged(f, before, revision, "0");
        Assert.Equal("Choose at least one bye/leg-bye, or use a dot ball.", Named<TextBlock>(f.Entry, "ErrorText").Text);
        Assert.Same(runs, f.Window.FocusManager?.GetFocusedElement()); AssertFullPaintedBounds(f.Window, runs);
        Assert.Equal("Check runs before applying.", Named<TextBlock>(f.Entry, "PreviewText").Text);
        await ReplaceByNativeText(f.Window, runs, "1"); await Key(f.Window, PhysicalKey.Enter);
        var innings = f.Match.CurrentInnings!; var delivery = Assert.Single(innings.Deliveries);
        Assert.Equal(source == "Bye" ? CricketDeliveryType.Bye : CricketDeliveryType.LegBye, delivery.Type);
        Assert.Equal(1, f.Match.TotalRuns); Assert.Equal(1, innings.LegalBallsBowled);
        Assert.Equal(source == "Bye" ? 1 : 0, innings.Byes); Assert.Equal(source == "Leg Bye" ? 1 : 0, innings.LegByes);
        Assert.Equal(1, f.ModelChanges); Assert.Equal(1, f.EditorClosures); Assert.False(f.Overlay.IsVisible);
    }

    // Two controls must run against unchanged code BEFORE fixing anything.
    // They do not require a future RunsValidationText to exist, so a missing
    // new visual is not misreported as a changed legal parser contract.
    [AvaloniaTheory] [InlineData("0", 1)] [InlineData("998", 999)]
    public async Task ExistingLegalCustomWideBoundariesRemainImmediateAndCommitOnce(string raw, int expected)
    {
        using var f = await DeliveryFixture.OpenActual("Wide", 700, 450);
        await ShowCustomRuns(f, raw); var runs = Named<TextBox>(f.Entry, "RunsBox");
        Assert.True(Named<Button>(f.Entry, "ApplyButton").IsEnabled);
        Assert.False(Named<TextBlock>(f.Entry, "ErrorText").IsVisible);
        Assert.Equal(raw == "0" ? "1 wide = 1 run" : "1 wide + 998 completed = 999 wides", Named<TextBlock>(f.Entry, "PreviewText").Text);
        Assert.Equal(f.OpenedState, State(f.Match)); Assert.Equal(f.OpenedRevision, f.Match.DeliveryRevision);
        Assert.Equal(0, f.ModelChanges); Assert.Equal(0, f.EditorClosures); Assert.Same(runs, f.Window.FocusManager?.GetFocusedElement());
        await Key(f.Window, PhysicalKey.Enter); AssertAcceptedWide(f, expected);
    }

    private sealed class DeliveryFixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool oldSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldMotion = OperatorMotion.AnimationOverride;
        private readonly AppTheme oldTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity oldDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly string? oldHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-delivery-d1-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> directories = new(StringComparer.Ordinal);
        private NativeWindow? window;
        private CricketControlPanel? panel;
        private Border? overlay;
        private bool captured, subscribed, disposed;
        internal string CaptureToken {get;}=Guid.NewGuid().ToString("N");
        internal string Source {get;private set;}="";
        internal NativeWindow Window => window!;
        internal CricketControlPanel Panel => panel!;
        internal Border Overlay => overlay!;
        internal CricketMatchManager Match { get; private set; } = null!;
        internal CricketDeliveryDialog Entry { get; private set; } = null!;
        internal string OpenedState { get; private set; } = "";
        internal int OpenedRevision { get; private set; }
        internal int ModelChanges { get; private set; }
        internal int EditorClosures { get; private set; }

        internal static async Task<DeliveryFixture> OpenActual(string source, int width, int height)
        {
            var f = new DeliveryFixture {Source=source};
            try
            {
                SelfTestStorage.Initialize(true); Assert.True(SelfTestStorage.IsEnabled);
                Assert.Equal(Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)), SelfTestStorage.RootPath);
                if (Directory.Exists(SelfTestStorage.RootPath))
                {
                    foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath)) f.directories.Add(directory);
                    foreach (string file in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)) f.files[file] = File.ReadAllBytes(file);
                }
                f.captured = true; foreach (string file in f.files.Keys) File.Delete(file);
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", f.dataHome); Directory.CreateDirectory(f.dataHome);
                f.app.IsSelfTestMode = true; OperatorMotion.AnimationOverride = false;
                f.Match = CricketReliabilityChecks.CreateMatch(ready: true);
                Assert.False(f.Match.TossPending); Assert.False(f.Match.CurrentInnings!.NeedsBatterSelection); Assert.False(f.Match.CurrentInnings.NeedsBowlerSelection);
                f.OpenedState = State(f.Match); f.OpenedRevision = f.Match.DeliveryRevision;
                f.panel = new CricketControlPanel(); f.Panel.SetMatch(f.Match);
                f.window = new NativeWindow { Width = width, Height = height, Content = f.Panel }; f.Window.Show();
                await Settle(600);
                Assert.True(Named<Border>(f.Panel, "ScoringCommands").IsEffectivelyEnabled);
                Assert.True(Named<Border>(f.Panel, "ExtraCommands").IsEffectivelyEnabled);
                f.overlay = Named<Border>(f.Panel, "DeliveryEntryOverlay"); Assert.False(f.Overlay.IsVisible);
                f.Match.MatchChanged += f.Changed; f.Overlay.PropertyChanged += f.OverlayChanged; f.subscribed = true;
                var action = Assert.Single(Named<Border>(f.Panel, "ExtraCommands").GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, source));
                await Reveal(f.Window, action); await Tap(f.Window, action);
                await Until(() => f.Overlay.IsEffectivelyVisible && f.Overlay.GetVisualDescendants().OfType<CricketDeliveryDialog>().Any(), "Actual scoring source must embed a current delivery editor.");
                f.Entry = Assert.Single(f.Overlay.GetVisualDescendants().OfType<CricketDeliveryDialog>());
                Assert.Same(f.Entry, Named<ContentControl>(f.Panel, "DeliveryEntryContent").Content);
                Assert.True(Named<Button>(f.Entry, "ApplyButton").IsEffectivelyEnabled);
                Assert.Equal(0, f.ModelChanges); Assert.Equal(0, f.EditorClosures); Assert.Empty(f.Match.CurrentInnings.Deliveries);
                return f;
            }
            catch { f.Dispose(); throw; }
        }
        private void Changed() => ModelChanges++;
        private void OverlayChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            // Public observation of real parent completion. Embedded Complete
            // calls its owner callback, so the standalone Completed event is
            // deliberately NOT counted as this path's completion evidence.
            if (change.Property == Visual.IsVisibleProperty && change.OldValue is true && change.NewValue is false) EditorClosures++;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            if (subscribed) Attempt(() => { Match.MatchChanged -= Changed; Overlay.PropertyChanged -= OverlayChanged; });
            if (panel is not null) Attempt(panel.ClearMatch);
            if (window is not null) Attempt(() => { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); Dispatcher.UIThread.RunJobs(); });
            Attempt(() => OperatorMotion.AnimationOverride = oldMotion); Attempt(() => app.IsSelfTestMode = oldSelfTest);
            Attempt(() => app.ThemeService.SetTheme(oldTheme)); Attempt(() => app.ThemeService.SetDensity(oldDensity));
            if (captured)
            {
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath)) foreach (string file in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)) if (!files.ContainsKey(file)) File.Delete(file); });
                foreach (var (path, bytes) in files) Attempt(() => { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); });
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath)) foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length)) if (!directories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); });
            }
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", oldHome));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            if (errors.Count > 0) throw new AggregateException("Delivery D1 fixture cleanup failed", errors);
        }
    }
}
