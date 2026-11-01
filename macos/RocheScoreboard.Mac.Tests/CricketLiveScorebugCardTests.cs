// Translated production OutputTab / original Button route checks.
// Native Windows/Mac desktop pointer, UIA, OS policy and DPI acceptance remain separate.
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketLiveScorebugCardTests
{
    private static void SaveFrame(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        string? directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if(directory is null)return;
        Directory.CreateDirectory(directory);frame.Save(Path.Combine(directory,name+".png"));
    }
    private static async Task Pump(int milliseconds = 20) { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> predicate, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate() && clock.ElapsedMilliseconds < 3000) await Pump(8);
        Assert.True(predicate(), reason);
    }
    private static T Read<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) ?? owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner))!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void OutputVisible(CricketControlPanel panel, bool value) => panel.GetType().GetMethod("SetOperatorOutputVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [value]);

    private sealed class Fixture : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled, reduce = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly byte[]? preferences = File.Exists(OperatorAppearance.StoragePath) ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;
        private readonly bool selfTest = ((App)Application.Current!).IsSelfTestMode;
        internal readonly CricketMatchManager Match = CricketReliabilityChecks.CreateMatch(true);
        internal readonly CricketControlPanel Panel;
        internal readonly NativeWindow Window;
        internal Button Live => Panel.FindControl<Button>("Presentationscorebug")!;
        internal Button Batting => Panel.FindControl<Button>("Presentationbatting")!;
        internal CricketLiveScorebugGlyph Glyph => Panel.FindControl<CricketLiveScorebugGlyph>("CricketLiveScorebugGlyphView")!;
        internal TabControl Tabs => Panel.FindControl<TabControl>("MainTabControl")!;
        internal Fixture(int width = 1280, int height = 850)
        {
            ((App)Application.Current!).IsSelfTestMode = true;
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(glass, true);
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            Panel = new CricketControlPanel(); Panel.SetMatch(Match);
            Window = new NativeWindow { Width = width, Height = height, Content = Panel };
        }
        internal async Task Open()
        {
            Window.Show(); Tabs.SelectedItem = Panel.FindControl<TabItem>("OutputTab")!;
            await Pump(100); Live.BringIntoView(); await Pump(100);
        }
        internal async Task StartArrival()
        {
            Click(Batting); Assert.Equal(0d, Glyph.Progress);
            OperatorAppearance.Configure(glass, false);
            Assert.True(OperatorMotion.Enabled);
            Click(Live);
            await Until(() => Motion.IsAnimating(Glyph) && Glyph.Progress > .001 && Glyph.Progress < .999, "Require a witnessed production glyph animation before interruption.");
        }
        public void Dispose()
        {
            Panel.ClearMatch(); Window.Close();
            OperatorAppearance.ConfigureMotionStyle(style); OperatorAppearance.Configure(glass, reduce);
            OperatorMotion.AnimationOverride = animation;
            ((App)Application.Current!).IsSelfTestMode = selfTest;
            if (preferences is null) File.Delete(OperatorAppearance.StoragePath); else File.WriteAllBytes(OperatorAppearance.StoragePath, preferences);
        }
    }

    [AvaloniaTheory]
    [InlineData(420, 320)]
    [InlineData(1280, 850)]
    public async Task ActualOutputCardHasCricketAnatomyAndImmediateSelectionWithoutOpeningOutput(int width, int height)
    {
        using var f = new Fixture(width, height); await f.Open();
        Assert.False(OperatorControlMotion.GetEnabled(f.Live)); Assert.False(OperatorChoiceMotion.GetEnabled(f.Live));
        Assert.Equal("Live scorebug", AutomationProperties.GetName(f.Live));
        Assert.Contains("does not open", AutomationProperties.GetHelpText(f.Live));
        string[] parts = ["CricketLiveFrame", "CricketLiveScoreRail", "CricketLiveTopBatter", "CricketLiveBottomBatter", "CricketLiveDetailRail"];
        foreach (string name in parts) Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == name);
        Assert.False(f.Glyph.IsHitTestVisible); Assert.False(f.Glyph.Focusable);
        Assert.Equal("LIVE", f.Panel.FindControl<TextBlock>("CricketLiveScorebugEyebrow")!.Text);
        Assert.Equal("SCOREBUG", f.Panel.FindControl<TextBlock>("CricketLiveScorebugTitle")!.Text);
        var actualBounds = f.Live.TranslatePoint(default, f.Window); Assert.NotNull(actualBounds);
        Assert.True(new Rect(f.Window.ClientSize).Contains(new Rect(actualBounds.Value, f.Live.Bounds.Size)), "The real card must fit after bringing it into the compact Output viewport.");
        int events = 0, matchEvents = 0; f.Panel.PresentationChanged += _ => events++; f.Match.MatchChanged += () => matchEvents++;
        int revision = f.Match.DeliveryRevision, balls = f.Match.CurrentInnings!.Deliveries.Count;
        OutputVisible(f.Panel, false); Click(f.Batting); Click(f.Live);
        Assert.Equal("Selected", AutomationProperties.GetItemStatus(f.Live));
        Assert.Equal(1d, f.Panel.FindControl<TextBlock>("CricketLiveScorebugCheck")!.Opacity);
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Assert.Equal("OUTPUT OFF · PREVIEW · LIVE SCOREBUG", f.Panel.FindControl<TextBlock>("CricketOnAirText")!.Text);
        int generation = Read<int>(f.Panel, "_liveCardGeneration");
        Click(f.Live); Assert.Equal(3, events); Assert.Equal(generation, Read<int>(f.Panel, "_liveCardGeneration"));
        Assert.False(Read<bool>(f.Panel, "_operatorOutputVisible")); Assert.Equal(0, matchEvents);
        Assert.Equal(revision, f.Match.DeliveryRevision); Assert.Equal(balls, f.Match.CurrentInnings.Deliveries.Count);
        Assert.True(f.Panel.FindControl<Button>("Presentationwindow")!.IsEnabled);
        using var rendered = f.Window.CaptureRenderedFrame(); Assert.NotNull(rendered);
        SaveFrame(rendered,"cricket-live-card-"+width+"x"+height);
    }

    [AvaloniaFact]
    public async Task OriginalComponentButtonAcceptsPhysicalPointerAndKeyboardRoutesWithStableFocusAndOffAirAuthority()
    {
        using var f = new Fixture(); await f.Open();
        // Component scope: the production panel's XAML OperatorDesign opt-in
        // installs its normal OperatorInputFocus hooks. No keyboard-mode value
        // or substitute input/focus hook is installed by this fixture.
        Assert.True(OperatorDesign.GetIsEnabled(f.Panel));
        OutputVisible(f.Panel, false);
        var events = new List<string>(); int matchEvents = 0;
        f.Panel.PresentationChanged += target => events.Add(target);
        f.Match.MatchChanged += () => matchEvents++;
        var innings = f.Match.CurrentInnings!; var deliveries = innings.Deliveries.ToArray();
        int runs = innings.TotalRuns, revision = f.Match.DeliveryRevision;
        var cardBounds = f.Live.Bounds; var title = f.Panel.FindControl<TextBlock>("CricketLiveScorebugTitle")!;
        var titleBounds = title.Bounds;

        void Pointer(Button button)
        {
            var origin = button.TranslatePoint(default, f.Window); Assert.NotNull(origin);
            var box = new Rect(origin.Value, button.Bounds.Size);
            Assert.True(new Rect(f.Window.ClientSize).Contains(box));
            Point[] probes = [box.Center, new(box.Left + 4, box.Center.Y), new(box.Right - 4, box.Center.Y), new(box.Center.X, box.Top + 4), new(box.Center.X, box.Bottom - 4)];
            foreach (var point in probes)
            {
                var hit = f.Window.InputHitTest(point);
                Assert.True(ReferenceEquals(hit, button) || hit is Visual visual && visual.GetVisualAncestors().Contains(button), "Actual Window hit testing must reach the original Button at all five probes.");
            }
            f.Window.MouseMove(box.Center);
            f.Window.MouseDown(box.Center, MouseButton.Left);
            f.Window.MouseUp(box.Center, MouseButton.Left);
        }
        void Key(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            f.Window.KeyPressQwerty(key, modifiers);
            f.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        }
        void Authority()
        {
            Assert.Same(innings, f.Match.CurrentInnings); Assert.Equal(deliveries, innings.Deliveries.ToArray());
            Assert.Equal(runs, innings.TotalRuns); Assert.Equal(revision, f.Match.DeliveryRevision); Assert.Equal(0, matchEvents);
            Assert.False(Read<bool>(f.Panel, "_operatorOutputVisible"));
            Assert.Equal("scorebug", Read<string>(f.Panel, "_currentPresentation"));
            Assert.Equal("OUTPUT OFF · PREVIEW · LIVE SCOREBUG", f.Panel.FindControl<TextBlock>("CricketOnAirText")!.Text);
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(f.Live));
            Assert.Equal(1d, f.Panel.FindControl<TextBlock>("CricketLiveScorebugCheck")!.Opacity);
            Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
            Assert.Equal(cardBounds, f.Live.Bounds); Assert.Equal(titleBounds, title.Bounds);
        }

        Pointer(f.Batting); Pointer(f.Live); Authority(); Pointer(f.Batting);
        Assert.True(f.Batting.Focus(NavigationMethod.Tab));
        Key(PhysicalKey.Tab, RawInputModifiers.Shift); await Pump();
        Assert.True(f.Live.IsFocused, "Actual backward Tab navigation from batting must focus the preceding original LIVE Button.");
        Assert.True(OperatorInputFocus.GetIsKeyboardMode(f.Live), "The physical Tab event must pass through the production panel focus hooks.");
        var focus = Assert.Single(f.Live.GetVisualDescendants().OfType<Border>(), part => part.Name == "CricketLiveFocusFrame");
        Assert.True(focus.IsEffectivelyVisible); var focusBounds = focus.Bounds;
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, false);
        Assert.True(OperatorMotion.Enabled);
        Key(PhysicalKey.Space); Authority(); Assert.True(f.Live.IsFocused); Assert.True(focus.IsEffectivelyVisible);
        Key(PhysicalKey.Space); Authority();
        Assert.Equal(new[] { "batting", "scorebug", "batting", "scorebug", "scorebug" }, events);
        await Pump(100);
        Assert.True(f.Live.IsFocused); Assert.True(focus.IsEffectivelyVisible); Assert.Equal(focusBounds, focus.Bounds); Authority();
        using var frame = f.Window.CaptureRenderedFrame(); Assert.NotNull(frame);
        SaveFrame(frame, "cricket-live-card-physical-keyboard-focus");
    }

    [AvaloniaFact]
    public async Task NaturalAccentHandoffReversesFromCurrentPoseAndLiveReduceRemovesItsClock()
    {
        using var f = new Fixture(); await f.Open();
        var fixedCard = f.Live.Bounds; var fixedTitle = f.Panel.FindControl<TextBlock>("CricketLiveScorebugTitle")!.Bounds;
        await f.StartArrival();
        await Until(() => f.Glyph.Progress > .02 && f.Glyph.Progress < .4, "Observe an early naturally animated score-rail phase.");
        var score = f.Glyph.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "CricketLiveScoreAccent");
        var batter = f.Glyph.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "CricketLiveTopBatterAccent");
        var detail = f.Glyph.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "CricketLiveDetailAccent");
        Assert.True(score.Opacity > batter.Opacity); Assert.Equal(0d, detail.Opacity);
        using var earlyFrame = f.Window.CaptureRenderedFrame(); Assert.NotNull(earlyFrame);
        await Until(() => f.Glyph.Progress > .6 && f.Glyph.Progress < .99, "Observe the later naturally animated detail handoff.");
        using var lateFrame = f.Window.CaptureRenderedFrame(); Assert.NotNull(lateFrame);
        Assert.True(detail.Opacity > 0); double reverseFrom = f.Glyph.Progress;
        Click(f.Batting); Assert.InRange(Math.Abs(f.Glyph.Progress - reverseFrom), 0, .01);
        Assert.Equal("Not selected", AutomationProperties.GetItemStatus(f.Live));
        await Until(() => f.Glyph.Progress < reverseFrom - .04, "The accepted batting choice reverses the rendered glyph.");
        double reopenFrom = f.Glyph.Progress; Click(f.Live); Assert.InRange(Math.Abs(f.Glyph.Progress - reopenFrom), 0, .01);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Assert.Equal(fixedCard, f.Live.Bounds); Assert.Equal(fixedTitle, f.Panel.FindControl<TextBlock>("CricketLiveScorebugTitle")!.Bounds);
        await Pump(500); Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        // Encode after observing motion so PNG work cannot consume its phases.
        SaveFrame(earlyFrame,"cricket-live-card-early");SaveFrame(lateFrame,"cricket-live-card-late");
    }

    [AvaloniaTheory]
    [InlineData("tab")]
    [InlineData("disabled")]
    [InlineData("ancestor-disabled")]
    [InlineData("hidden")]
    [InlineData("viewport")]
    [InlineData("unload")]
    [InlineData("close")]
    public async Task WitnessedArrivalRetiresAtItsActualSourceLifetimeBoundary(string boundary)
    {
        using var f = new Fixture(1280, boundary == "viewport" ? 480 : 850); await f.Open(); await f.StartArrival();
        switch (boundary)
        {
            case "tab": f.Tabs.SelectedIndex = 0; break;
            case "disabled": f.Live.IsEnabled = false; break;
            case "ancestor-disabled": f.Panel.FindControl<Grid>("DisplayColumns")!.IsEnabled = false; break;
            case "hidden": f.Panel.IsVisible = false; break;
            case "viewport": f.Panel.FindControl<ScrollViewer>("CricketPresentationViewport")!.Offset = new Vector(0, 1000); break;
            case "unload": Assert.IsAssignableFrom<ContentControl>(f.Window.Content).Content = null; break;
            case "close": f.Window.Close(); break;
        }
        await Until(() => !Motion.IsAnimating(f.Glyph), "The original lifetime boundary must remove the witnessed glyph clock.");
        Assert.Equal(1d, f.Glyph.Progress);
        if (boundary == "unload") Assert.False(Read<bool>(f.Panel, "_liveCardEpochActive"));
        if (boundary != "close")
        {
            if (boundary == "unload") Assert.IsAssignableFrom<ContentControl>(f.Window.Content).Content = f.Panel;
            f.Panel.IsVisible = true; f.Live.IsEnabled = true;
            f.Panel.FindControl<Grid>("DisplayColumns")!.IsEnabled = true;
            f.Tabs.SelectedItem = f.Panel.FindControl<TabItem>("OutputTab")!;
            f.Live.BringIntoView(); await Pump(100);
            Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        }
        await Pump(500); Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
    }
}
