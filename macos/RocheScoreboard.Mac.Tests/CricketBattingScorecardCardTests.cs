// Production component routes and translated rendering. Windows WPF input,
// UIA/Narrator, DPI and compositor acceptance remain separate evidence.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class CricketBattingScorecardCardTests
{
    private static async Task Pump(int milliseconds = 20) { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> ready, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.ElapsedMilliseconds < 3000) await Pump(8);
        Assert.True(ready(), reason);
    }
    private static T Named<T>(Control owner, string name) where T : Control => Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));
    private static T Read<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string State(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static void Save(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"));
    }
    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers); window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }
    private static void Pointer(Window window, Button button, bool requireHit = true)
    {
        var origin = button.TranslatePoint(default, window); Assert.NotNull(origin);
        var box = new Rect(origin.Value, button.Bounds.Size);
        Assert.True(new Rect(window.ClientSize).Contains(box), "The complete original Button must fit inside the actual window.");
        if (requireHit)
        {
            Point[] probes = [box.Center, new(box.Left + 4, box.Center.Y), new(box.Right - 4, box.Center.Y), new(box.Center.X, box.Top + 4), new(box.Center.X, box.Bottom - 4)];
            foreach (var point in probes)
            {
                var hit = window.InputHitTest(point);
                Assert.True(ReferenceEquals(hit, button) || hit is Visual visual && visual.GetVisualAncestors().Contains(button), "The original Button must own all five physical hit probes.");
            }
        }
        window.MouseMove(box.Center); window.MouseDown(box.Center, MouseButton.Left); window.MouseUp(box.Center, MouseButton.Left);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool glass = OperatorAppearance.GlassEnabled, reduce = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style = OperatorAppearance.MotionStyle;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly bool selfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly byte[]? preferences = File.Exists(OperatorAppearance.StoragePath) ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;
        internal readonly CricketMatchManager Match;
        internal readonly CricketControlPanel Panel;
        internal readonly CricketSummaryControl Summary = new() { IsVisible = false };
        internal readonly NativeWindow Window, Output;
        internal Button Batting => Named<Button>(Panel, "Presentationbatting");
        internal Button Live => Named<Button>(Panel, "Presentationscorebug");
        internal CricketBattingScorecardGlyph Glyph => Named<CricketBattingScorecardGlyph>(Panel, "CricketBattingGlyphView");
        internal TabControl Tabs => Named<TabControl>(Panel, "MainTabControl");
        internal Fixture(int width = 1280, int height = 850, bool tossPending = false)
        {
            ((App)Application.Current!).IsSelfTestMode = true;
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(glass, true); OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            Match = CricketReliabilityChecks.CreateMatch(!tossPending); Match.TossPending = tossPending;
            Panel = new CricketControlPanel(); Panel.SetMatch(Match); Panel.SetSummaryControl(Summary);
            Window = new NativeWindow { Width = width, Height = height, Content = Panel };
            // Real summary instance, bound through the public component API.
            // Its output owner is deliberately never shown by this fixture.
            Output = new NativeWindow { Width = 386, Height = 193, Content = Summary };
        }
        internal async Task Open()
        {
            Window.Show(); Tabs.SelectedItem = Named<TabItem>(Panel, "OutputTab");
            await Pump(100); Batting.BringIntoView(); await Pump(100);
            OffAir(); Assert.False(Output.IsVisible);
        }
        internal void OffAir() => Panel.GetType().GetMethod("SetOperatorOutputVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Panel, [false]);
        internal async Task StartArrival()
        {
            Assert.Equal(0d, Glyph.Progress);
            OperatorAppearance.Configure(glass, false); Assert.True(OperatorMotion.Enabled);
            Pointer(Window, Batting);
            await Until(() => Motion.IsAnimating(Glyph) && Glyph.Progress > .001 && Glyph.Progress < .999, "Require an actual batting arrival clock before interruption.");
        }
        internal void Selected()
        {
            Assert.Equal("batting", Read<string>(Panel, "_currentPresentation"));
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(Batting));
            Assert.Equal(1d, Named<TextBlock>(Panel, "CricketBattingCheck").Opacity);
            Assert.False(Output.IsVisible);
        }
        public void Dispose()
        {
            Summary.OverlayMotionEnabled = false; Summary.HideSummary(); Panel.ClearMatch(); Window.Close(); Output.Close();
            OperatorAppearance.ConfigureMotionStyle(style); OperatorAppearance.Configure(glass, reduce);
            OperatorMotion.AnimationOverride = animation; ((App)Application.Current!).IsSelfTestMode = selfTest;
            if (preferences is null) File.Delete(OperatorAppearance.StoragePath); else File.WriteAllBytes(OperatorAppearance.StoragePath, preferences);
        }
    }

    [AvaloniaTheory]
    [InlineData(420, 320)]
    [InlineData(1280, 850)]
    public async Task PhysicalBattingCardSelectsActualScorecardWithoutOpeningOutputOrChangingMatch(int width, int height)
    {
        using var f = new Fixture(width, height); await f.Open(); f.OffAir();
        Assert.False(OperatorControlMotion.GetEnabled(f.Batting)); Assert.False(OperatorChoiceMotion.GetEnabled(f.Batting));
        Assert.Equal("Batting scorecard", AutomationProperties.GetName(f.Batting));
        Assert.Contains("does not open", AutomationProperties.GetHelpText(f.Batting));
        Assert.Equal("BAT", Named<TextBlock>(f.Panel, "CricketBattingEyebrow").Text);
        Assert.Equal("BATTING", Named<TextBlock>(f.Panel, "CricketBattingTitle").Text);
        Assert.False(f.Glyph.IsHitTestVisible); Assert.False(f.Glyph.Focusable);
        Assert.Empty(f.Glyph.GetVisualDescendants().OfType<TextBlock>());
        foreach (string name in new[] { "CricketBattingFrame", "CricketBattingHeader", "CricketBattingRoster0", "CricketBattingRoster1", "CricketBattingRoster2", "CricketBattingRunsCell0", "CricketBattingBallsCell0" })
            Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == name);
        string before = State(f.Match); int modelEvents = 0; var events = new List<string>();
        f.Match.MatchChanged += () => modelEvents++; f.Panel.PresentationChanged += events.Add;
        Pointer(f.Window, f.Batting); f.Selected();
        Assert.Equal("OUTPUT OFF · PREVIEW · BATTING SCORECARD", Named<TextBlock>(f.Panel, "CricketOnAirText").Text);
        Assert.False(Read<bool>(f.Panel, "_operatorOutputVisible"));
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Assert.Contains(f.Match.BattingTeamName.ToUpper(), Named<TextBlock>(f.Summary, "HeaderText").Text);
        Assert.Contains(" BATTING | 1ST INNINGS", Named<TextBlock>(f.Summary, "HeaderText").Text);
        var headings = Named<Grid>(f.Summary, "SummaryColumns").Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Equal(new[] { "RUNS", "BALLS" }, headings);
        Assert.NotEmpty(Named<Panel>(f.Summary, "RowsPanel").Children);
        Pointer(f.Window, f.Batting); f.Selected();
        Assert.Equal(new[] { "batting", "batting" }, events); Assert.Equal(0, modelEvents); Assert.Equal(before, State(f.Match));
        Assert.False(Motion.IsAnimating(f.Glyph));
        Save(f.Window, "cricket-batting-card-" + width + "x" + height);
    }

    [AvaloniaFact]
    public async Task PhysicalTabSpaceAndEnterRetainVisibleFocusAndImmediateSelection()
    {
        using var f = new Fixture(); await f.Open(); f.OffAir();
        string before = State(f.Match); var events = new List<string>(); f.Panel.PresentationChanged += events.Add;
        Assert.True(OperatorDesign.GetIsEnabled(f.Panel));
        Assert.True(f.Live.Focus(NavigationMethod.Tab)); Key(f.Window, PhysicalKey.Tab); await Pump();
        Assert.True(f.Batting.IsFocused, "Forward Tab from LIVE must reach the original batting Button.");
        Assert.True(OperatorInputFocus.GetIsKeyboardMode(f.Batting));
        var focus = Assert.Single(f.Batting.GetVisualDescendants().OfType<Border>(), border => border.Name == "CricketBattingFocusFrame");
        Assert.True(focus.IsEffectivelyVisible); var focusBounds = focus.Bounds;
        var cardBounds = f.Batting.Bounds; var title = Named<TextBlock>(f.Panel, "CricketBattingTitle"); var titleBounds = title.Bounds;
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, false); Assert.True(OperatorMotion.Enabled);
        Key(f.Window, PhysicalKey.Space); f.Selected();
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Key(f.Window, PhysicalKey.Enter); f.Selected();
        Assert.Equal(new[] { "batting", "batting" }, events);
        await Pump(100);
        Assert.True(f.Batting.IsFocused); Assert.True(focus.IsEffectivelyVisible);
        Assert.Equal(focusBounds, focus.Bounds); Assert.Equal(cardBounds, f.Batting.Bounds); Assert.Equal(titleBounds, title.Bounds);
        Assert.Equal(before, State(f.Match)); Assert.False(Read<bool>(f.Panel, "_operatorOutputVisible"));
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Save(f.Window, "cricket-batting-card-keyboard-focus");
    }

    [AvaloniaFact]
    public async Task NaturalRosterThenRunsBallsArrivalReversesFromCurrentPoseAndYieldsToReducedMotion()
    {
        using var f = new Fixture(); await f.Open();
        string before = State(f.Match); var bounds = f.Batting.Bounds; var title = Named<TextBlock>(f.Panel, "CricketBattingTitle"); var titleBounds = title.Bounds;
        await f.StartArrival();
        var name0 = Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == "CricketBattingNameAccent0");
        var name2 = Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == "CricketBattingNameAccent2");
        var runs0 = Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == "CricketBattingRunsAccent0");
        var balls2 = Assert.Single(f.Glyph.GetVisualDescendants().OfType<Border>(), part => part.Name == "CricketBattingBallsAccent2");
        await Until(() => f.Glyph.Progress > .02 && f.Glyph.Progress < .35, "Witness early roster progression before the statistic columns.");
        Assert.True(Assert.IsAssignableFrom<Avalonia.Media.ScaleTransform>(name0.RenderTransform).ScaleX > Assert.IsAssignableFrom<Avalonia.Media.ScaleTransform>(name2.RenderTransform).ScaleX);
        Assert.Equal(13d,name0.Width);Assert.Equal(13d,name2.Width);
        Assert.Equal(0d, runs0.Opacity); Assert.Equal(0d, balls2.Opacity);
        using var early = f.Window.CaptureRenderedFrame(); Assert.NotNull(early);
        await Until(() => f.Glyph.Progress > .72 && f.Glyph.Progress < .99, "Witness the later paired statistic-cell arrival.");
        Assert.Equal(13d, name2.Width);Assert.Equal(1d,Assert.IsAssignableFrom<Avalonia.Media.ScaleTransform>(name2.RenderTransform).ScaleX);
        Assert.True(runs0.Opacity > balls2.Opacity); Assert.True(balls2.Opacity > 0);
        double from = f.Glyph.Progress;
        double? released=null,accepted=null;
        void Released(object? sender,PointerReleasedEventArgs args)=>released=f.Glyph.Progress;
        void Accepted(string target)=>accepted=f.Glyph.Progress;
        f.Live.AddHandler(InputElement.PointerReleasedEvent,Released,RoutingStrategies.Tunnel);
        f.Panel.PresentationChanged+=Accepted;
        try { Pointer(f.Window, f.Live); }
        finally { f.Live.RemoveHandler(InputElement.PointerReleasedEvent,Released);f.Panel.PresentationChanged-=Accepted; }
        Assert.NotNull(released);Assert.NotNull(accepted);
        Assert.InRange(Math.Abs(accepted.Value-released.Value), 0, .01);
        Assert.NotEqual("Selected", AutomationProperties.GetItemStatus(f.Batting));
        await Until(() => f.Glyph.Progress < from - .04, "Deselecting batting must reverse the current decorative pose.");
        released=accepted=null;
        f.Batting.AddHandler(InputElement.PointerReleasedEvent,Released,RoutingStrategies.Tunnel);
        f.Panel.PresentationChanged+=Accepted;
        try { Pointer(f.Window, f.Batting); }
        finally { f.Batting.RemoveHandler(InputElement.PointerReleasedEvent,Released);f.Panel.PresentationChanged-=Accepted; }
        Assert.NotNull(released);Assert.NotNull(accepted);
        Assert.InRange(Math.Abs(accepted.Value-released.Value), 0, .01);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        await Pump(500); f.Selected(); Assert.False(Motion.IsAnimating(f.Glyph));
        Assert.Equal(bounds, f.Batting.Bounds); Assert.Equal(titleBounds, title.Bounds); Assert.Equal(before, State(f.Match));
        string? directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); early.Save(Path.Combine(directory, "cricket-batting-card-early.png")); }
        Save(f.Window, "cricket-batting-card-settled");
    }

    [AvaloniaTheory]
    [InlineData("ancestor-disable")]
    [InlineData("viewport")]
    [InlineData("unload")]
    public async Task WitnessedBattingArrivalRetiresAtItsOriginalOwnerBoundaryWithoutReplay(string boundary)
    {
        using var f = new Fixture(1280, boundary == "viewport" ? 480 : 850); await f.Open(); await f.StartArrival();
        if (boundary == "ancestor-disable") Named<Grid>(f.Panel, "DisplayColumns").IsEnabled = false;
        if (boundary == "viewport") Named<ScrollViewer>(f.Panel, "CricketPresentationViewport").Offset = new Vector(0, 1000);
        if (boundary == "unload") Assert.IsAssignableFrom<ContentControl>(f.Window.Content).Content = null;
        await Until(() => !Motion.IsAnimating(f.Glyph), "An actual lifetime boundary must retire the witnessed original clock.");
        Assert.Equal(1d, f.Glyph.Progress);
        if (boundary == "ancestor-disable") Named<Grid>(f.Panel, "DisplayColumns").IsEnabled = true;
        if (boundary == "unload") Assert.IsAssignableFrom<ContentControl>(f.Window.Content).Content = f.Panel;
        f.Batting.BringIntoView(); await Pump(100); f.Selected();
        Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        await Pump(500); Assert.Equal(1d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
    }

    [AvaloniaFact]
    public async Task RebindingDuringArrivalCannotSelectBattingOrMutateTheReplacementMatch()
    {
        using var f = new Fixture(); await f.Open(); await f.StartArrival();
        string oldState = State(f.Match); var replacement = CricketReliabilityChecks.CreateMatch(true);
        replacement.SetTeams("Replacement batting side", replacement.TeamAAbbr, replacement.TeamBName, replacement.TeamBAbbr);
        string replacementState = State(replacement); var events = new List<string>(); f.Panel.PresentationChanged += events.Add;
        f.Panel.SetMatch(replacement); f.Panel.SetSummaryControl(f.Summary);
        f.Tabs.SelectedItem = Named<TabItem>(f.Panel, "OutputTab"); f.Batting.BringIntoView(); await Pump(100);
        Assert.Equal("scorebug", Read<string>(f.Panel, "_currentPresentation"));
        Assert.Equal(0d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph));
        Assert.NotEqual("Selected", AutomationProperties.GetItemStatus(f.Batting));
        await Pump(500);
        Assert.Equal(0d, f.Glyph.Progress); Assert.False(Motion.IsAnimating(f.Glyph)); Assert.Empty(events);
        Assert.Equal(oldState, State(f.Match)); Assert.Equal(replacementState, State(replacement)); Assert.False(f.Output.IsVisible);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true); Pointer(f.Window, f.Batting); f.Selected();
        Assert.Equal(new[] { "batting" }, events);
        Assert.Contains("REPLACEMENT BATTING SIDE", Named<TextBlock>(f.Summary, "HeaderText").Text);
        Assert.Equal(replacementState, State(replacement));
    }

    [AvaloniaFact]
    public async Task PendingTossKeepsBattingDisabledThroughPhysicalInputUntilTheExistingGuardResolves()
    {
        using var f = new Fixture(tossPending: true); await f.Open();
        var events = new List<string>(); f.Panel.PresentationChanged += events.Add; string before = State(f.Match);
        Assert.False(f.Batting.IsEnabled); Pointer(f.Window, f.Batting, requireHit: false);
        Assert.False(f.Batting.Focus(NavigationMethod.Tab)); Key(f.Window, PhysicalKey.Space);
        Assert.Empty(events); Assert.Equal(before, State(f.Match)); Assert.Equal(0d, f.Glyph.Progress); Assert.False(f.Output.IsVisible);
        Assert.True(f.Match.CompleteToss(true, true));
        await Until(() => f.Batting.IsEnabled, "The existing model toss completion must restore the batting route.");
        f.Batting.BringIntoView(); await Pump(); Pointer(f.Window, f.Batting); f.Selected();
        Assert.Equal(new[] { "batting" }, events);
    }
}
