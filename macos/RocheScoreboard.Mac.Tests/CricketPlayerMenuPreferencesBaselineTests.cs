// Actual-route menu regressions; first baseline evidence is preserved separately.
// Real physical anchors and actual rooted rows; no private Show/Dismiss invocation.
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[CollectionDefinition("Cricket player menu preferences baseline", DisableParallelization = true)]
public sealed class CricketPlayerMenuPreferencesCollection { }

[Collection("Cricket player menu preferences baseline")]
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static T Named<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), item => item.Name == name);
    // Reads only: actual session identity/state and hidden output cleanup.
    private static T Read<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string State(CricketMatchManager match) => JsonSerializer.Serialize(match.ToState());
    private static bool Within(Control root, IInputElement? input) => ReferenceEquals(root, input)
        || input is Visual visual && visual.GetVisualAncestors().Contains(root);
    private static async Task Settle(int milliseconds = 100)
    { await Task.Delay(milliseconds); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> condition, string reason)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++) await Settle();
        Assert.True(condition(), reason);
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
    private static bool Ready(Window window, Control control)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled || !control.IsHitTestVisible
            || !control.GetVisualAncestors().Contains(window) || control.Bounds.Width <= 8 || control.Bounds.Height <= 8)
            return false;
        var origin = control.TranslatePoint(default, window);
        if (origin is null) return false;
        var box = Painted(window, control);
        return new Rect(window.Bounds.Size).Contains(box) && Within(control, window.InputHitTest(box.Center));
    }
    private static void TapNow(Window window, Control control)
    {
        Assert.True(Ready(window, control), "Actual attached control must be visible, enabled, reachable and own its pointer hit: " + control.Name);
        Rect box = Painted(window, control);
        foreach (var point in new[] { box.Center, new Point(box.Left + 4, box.Center.Y),
            new Point(box.Right - 4, box.Center.Y), new Point(box.Center.X, box.Top + 4),
            new Point(box.Center.X, box.Bottom - 4) })
        {
            var actual=window.InputHitTest(point);
            if(!Within(control,actual) && Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
            {
                Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,"occluded-"+control.Name+".png"));
                File.WriteAllText(Path.Combine(directory,"occluded-"+control.Name+".json"),JsonSerializer.Serialize(new{control.Name,box,point,actualType=actual?.GetType().Name,actualName=(actual as Control)?.Name,window.ClientSize,window.Bounds},new JsonSerializerOptions{WriteIndented=true}));
            }
            Assert.True(Within(control,actual),"Actual transformed pointer probe is occluded: "+control.Name+" at "+point+" hit "+(actual as Control)?.Name+":"+actual?.GetType().Name);
        }
        window.MouseMove(box.Center); window.MouseDown(box.Center, MouseButton.Left); window.MouseUp(box.Center, MouseButton.Left);
    }
    // Window/sheet arrivals transform ancestors. Pump real rendering and keep
    // all five painted hit probes; center-only readiness races the hit scene.
    private static async Task StableReachable(Window window, Control control)
    {
        Rect? previousBox=null; int stableFrames=0;
        await Until(()=>
        {
            using var rendered=window.CaptureRenderedFrame();
            if(!Ready(window,control))
            {
                stableFrames=0;
                if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
                {
                    Directory.CreateDirectory(directory);rendered?.Save(Path.Combine(directory,"unready-"+control.Name+".png"));
                    File.WriteAllText(Path.Combine(directory,"unready-"+control.Name+".json"),JsonSerializer.Serialize(new
                    {control.Name,control.Bounds,painted=Painted(window,control),window.ClientSize,focus=(window.FocusManager?.GetFocusedElement() as Control)?.Name,
                        labels=control.GetVisualDescendants().OfType<TextBlock>().Select(t=>new{t.Name,t.Text,painted=Painted(window,t)}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
                }
                return false;
            }
            var box=Painted(window,control);
            bool allHits=new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),
                new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)}.All(point=>Within(control,window.InputHitTest(point)));
            if(!allHits){stableFrames=0;return false;}
            stableFrames=previousBox is {} previous && Math.Abs(previous.X-box.X)<.1 && Math.Abs(previous.Y-box.Y)<.1
                && Math.Abs(previous.Width-box.Width)<.1 && Math.Abs(previous.Height-box.Height)<.1?stableFrames+1:0;
            previousBox=box;return stableFrames>=2;
        },"Actual action must own all five pointer hits in consecutive stable rendered frames: "+control.Name);
    }
    private static async Task Tap(Window window, Control control)
    { TapNow(window, control); await Settle(); }
    private static async Task Reveal(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        try
        {
            Assert.True(control.Focus(NavigationMethod.Tab)); control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        }
        finally { window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); }
        await Until(() => Ready(window, control), "Keyboard reveal must reach the actual painted action.");
        Assert.True(Within(control, window.FocusManager?.GetFocusedElement()), "Reveal must establish actual focus before input.");
    }
    private static Button Anchor(CricketControlPanel panel, string role) => Assert.Single(
        Named<Border>(panel, role switch { "top" => "StrikerBar", "bottom" => "NonStrikerBar", _ => "BowlerBar" })
            .GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "⋯"));
    private static ScaleTransform Scale(Control surface) => Assert.Single(
        Assert.IsType<TransformGroup>(surface.RenderTransform).Children.OfType<ScaleTransform>());
    private static TranslateTransform Shift(Control surface) => Assert.Single(
        Assert.IsType<TransformGroup>(surface.RenderTransform).Children.OfType<TranslateTransform>());
    private static void Record(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task<(Popup Popup, Control Surface, TopLevel Root, Button First)> Open(
        Window window, CricketControlPanel panel, string role)
    {
        var anchor = Anchor(panel, role);
        await Reveal(window, anchor); await StableReachable(window, anchor);
        TapNow(window, anchor);
        var popup = Read<Popup>(panel, "_playerMenu");
        await Until(() => popup.IsOpen && popup.Child is Control child && child.IsLoaded
            && TopLevel.GetTopLevel(child) is not null, "A real rooted popup child must load after actual anchor input.");
        var surface = Assert.IsAssignableFrom<Control>(popup.Child);
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(surface));
        var first = surface.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyEnabled);
        await Until(() => Within(first, root.FocusManager?.GetFocusedElement()),
            "The actual opened first enabled row must own keyboard focus.");
        return (popup, surface, root, first);
    }
    private static void EscapeNow(TopLevel root)
    { root.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); root.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
    private static void Retired(CricketControlPanel panel, Popup popup)
    {
        Assert.False(popup.IsOpen); Assert.Null(popup.Child);
        Assert.DoesNotContain(Named<Grid>(panel, "CricketRoot").Children, child => ReferenceEquals(child, popup));
        Assert.False(ReferenceEquals(Read<Popup?>(panel, "_retiringPlayerMenu"), popup));
    }

    // 3 expected failures: the current Dismiss unsubscribes the only preference
    // listener before its closing branch could receive live Reduce.
    [AvaloniaTheory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("bowler")]
    public async Task LiveReduceDuringActualEscapeRetirementCompletesThatPopupImmediately(string role)
    {
        using var scope = new Scope();
        var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var opened = await Open(window, panel, role); await Settle(650);
        Assert.True(opened.Popup.IsOpen); Assert.True(opened.Surface.IsLoaded);
        string before = State(match); int revision = match.DeliveryRevision, events = 0;
        void Changed() => events++;
        match.MatchChanged += Changed;
        try
        {
            EscapeNow(opened.Root); // Do not await the 140ms natural retirement.
            Assert.Null(Read<Popup?>(panel, "_playerMenu"));
            Assert.Same(opened.Popup, Read<Popup?>(panel, "_retiringPlayerMenu"));
            Assert.True(opened.Popup.IsOpen); // Genuine MID-close precondition.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
            Assert.False(OperatorMotion.Enabled);
            // Snapshot immediately, before rendering can finish the old clock.
            var atPolicy = new { opened.Popup.IsOpen, HasChild = opened.Popup.Child is not null,
                StillRooted = Named<Grid>(panel, "CricketRoot").Children.Contains(opened.Popup),
                StillRetiring = ReferenceEquals(Read<Popup?>(panel, "_retiringPlayerMenu"), opened.Popup) };
            Record("menu-reduce-mid-close-" + role, atPolicy);
            Assert.False(atPolicy.IsOpen); Assert.False(atPolicy.HasChild);
            Assert.False(atPolicy.StillRooted); Assert.False(atPolicy.StillRetiring);
            Retired(panel, opened.Popup);
            Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events);
            await Settle(250); Retired(panel, opened.Popup);
            Assert.Same(Anchor(panel, role), window.FocusManager?.GetFocusedElement());
        }
        finally { match.MatchChanged -= Changed; }
    }

    // 3 expected failures: Calm currently only shortens MaterialArrival timing;
    // the actual opening scale and translation retain Expressive travel.
    // Observe the real popup Opened event through the public children collection
    // before IsOpen, rather than driving a private animation/sample hook.
    [AvaloniaTheory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("bowler")]
    public async Task CalmOpeningUsesCalmTravelFromItsActualAnchor(string role)
    {
        using var scope = new Scope(); OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
        var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var root = Named<Grid>(panel, "CricketRoot");
        (Popup Popup, double X, double Y, double Shift, Point Origin)? initial = null;
        void Added(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.NewItems is null) return;
            foreach (var popup in args.NewItems.OfType<Popup>())
                popup.Opened += (_, _) =>
                {
                    var surface = Assert.IsAssignableFrom<Control>(popup.Child);
                    // Arrival now waits for real popup layout. Observe its
                    // initial pose after the product's Loaded handler, while
                    // Open() still proves usable rows and actual keyboard focus.
                    void Capture()
                    {
                        Assert.NotNull(TopLevel.GetTopLevel(surface)); Assert.True(surface.IsLoaded);
                        var scale = Scale(surface); var shift = Shift(surface);
                        initial = (popup, scale.ScaleX, scale.ScaleY, shift.Y, surface.RenderTransformOrigin.Point);
                    }
                    void Loaded(object? _, RoutedEventArgs e) { surface.Loaded -= Loaded; Capture(); }
                    if (surface.IsLoaded) Capture(); else surface.Loaded += Loaded;
                };
        }
        ((INotifyCollectionChanged)root.Children).CollectionChanged += Added;
        try
        {
            string before = State(match); int revision = match.DeliveryRevision;
            var opened = await Open(window, panel, role);
            Assert.NotNull(initial); Assert.Same(opened.Popup, initial.Value.Popup);
            Assert.True(opened.Surface.IsLoaded); using var frame = opened.Root.CaptureRenderedFrame(); Assert.NotNull(frame);
            Assert.Equal(.4, OperatorMotion.TravelScale);
            await Settle(650);
            var measured = SourceGeometry(window, Anchor(panel,role), opened.Surface);
            Record("menu-calm-initial-" + role, new { initial.Value.X, initial.Value.Y, initial.Value.Shift, initial.Value.Origin, measured });
            // Narrow tolerance only permits the elapsed microseconds from the
            // public Opened callback. Existing hardcoded .94/.68/-18 fail these.
            Assert.InRange(initial.Value.X, .975, .981);
            Assert.InRange(initial.Value.Y, .871, .881);
            if(measured.Direction==0)Assert.InRange(initial.Value.Shift,-.0001,.0001);
            else Assert.InRange(initial.Value.Shift*measured.Direction,6.9,7.21);
            Assert.InRange(initial.Value.Origin.X,measured.Origin.X-.002,measured.Origin.X+.002);
            Assert.InRange(initial.Value.Origin.Y,measured.Origin.Y-.002,measured.Origin.Y+.002);
            await Settle(650);
            Assert.InRange(Scale(opened.Surface).ScaleX, .9999, 1.0001);
            Assert.InRange(Scale(opened.Surface).ScaleY, .9999, 1.0001);
            Assert.InRange(Shift(opened.Surface).Y, -.0001, .0001);
            Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision);
            EscapeNow(opened.Root); await Settle(250); Retired(panel, opened.Popup);
        }
        finally { ((INotifyCollectionChanged)root.Children).CollectionChanged -= Added; }
    }

    // 3 expected failures: current glyph-only anchors have generic "Options"
    // tooltips and no player-specific automation name before opening.
    [AvaloniaTheory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("bowler")]
    public async Task ActualSourceNamesIdentifyTheCurrentPlayerAndRoleBeforeOpeningAndAfterStrikeSwap(string role)
    {
        using var scope = new Scope(); var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var anchor = Anchor(panel, role); await Reveal(window, anchor); await StableReachable(window, anchor);
        void NamedForCurrentPlayer()
        {
            var innings = match.CurrentInnings!;
            int index = role == "top" ? Read<int>(panel, "_cpTopBatterIndex")
                : role == "bottom" ? Read<int>(panel, "_cpBottomBatterIndex") : innings.CurrentBowlerIndex;
            var player = role == "bowler" ? innings.BowlingAttack[index] : innings.BattingOrder[index];
            string expectedRole = role == "bowler" ? "bowler" : index == innings.StrikerIndex ? "striker" : "non-striker";
            string actual = AutomationProperties.GetName(anchor) ?? "";
            Record("menu-source-name-" + role, new { player.FullName, expectedRole, actual });
            Assert.Contains(player.FullName, actual, StringComparison.Ordinal);
            Assert.Contains(expectedRole, actual, StringComparison.OrdinalIgnoreCase);
            if (expectedRole == "striker") Assert.DoesNotContain("non-striker", actual, StringComparison.OrdinalIgnoreCase);
        }
        string before = State(match); int revision = match.DeliveryRevision;
        NamedForCurrentPlayer(); Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision);
        int stableIndexBefore = role == "top" ? Read<int>(panel, "_cpTopBatterIndex")
            : role == "bottom" ? Read<int>(panel, "_cpBottomBatterIndex") : match.CurrentInnings!.CurrentBowlerIndex;
        string nameBeforeSwap = AutomationProperties.GetName(anchor) ?? "";
        int oldStriker = match.CurrentInnings!.StrikerIndex, oldNonStriker = match.CurrentInnings.NonStrikerIndex;
        match.RotateStrike(); // Intentional public state change: row names must follow the actual role.
        Assert.Equal(oldStriker, match.CurrentInnings.NonStrikerIndex); Assert.Equal(oldNonStriker, match.CurrentInnings.StrikerIndex);
        NamedForCurrentPlayer(); Assert.Empty(match.CurrentInnings.Deliveries); Assert.Equal(revision, match.DeliveryRevision);
        int stableIndexAfter = role == "top" ? Read<int>(panel, "_cpTopBatterIndex")
            : role == "bottom" ? Read<int>(panel, "_cpBottomBatterIndex") : match.CurrentInnings.CurrentBowlerIndex;
        Assert.Equal(stableIndexBefore, stableIndexAfter);
        if (role == "bowler") Assert.Equal(nameBeforeSwap, AutomationProperties.GetName(anchor));
        else Assert.NotEqual(nameBeforeSwap, AutomationProperties.GetName(anchor));
    }

    // Additional integration preservation: completing an old retiring popup on
    // Reduce must not close the replacement current popup or steal its focus.
    [AvaloniaFact]
    public async Task LiveReduceRetiresOldPopupWhileKeepingRapidlyReopenedCurrentMenuUsable()
    {
        using var scope = new Scope(); var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var old = await Open(window, panel, "top"); await Settle(650);
        string before = State(match); int revision = match.DeliveryRevision;
        EscapeNow(old.Root); Assert.Same(old.Popup, Read<Popup?>(panel, "_retiringPlayerMenu")); Assert.True(old.Popup.IsOpen);
        // This source's geometry is already stable. Do not await the old exit.
        TapNow(window, Anchor(panel, "top"));
        var current = Read<Popup>(panel, "_playerMenu"); Assert.NotSame(old.Popup, current); Assert.True(current.IsOpen);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled, true);
        bool oldOpenAtPolicy = old.Popup.IsOpen;
        Record("menu-reduce-overlap", new { oldOpenAtPolicy, current.IsOpen });
        Assert.False(oldOpenAtPolicy); Retired(panel, old.Popup);
        Assert.Same(current, Read<Popup>(panel, "_playerMenu")); Assert.True(current.IsOpen);
        var surface = Assert.IsAssignableFrom<Control>(current.Child);
        await Until(() => surface.IsLoaded && TopLevel.GetTopLevel(surface) is not null, "The replacement menu must retain its real input root.");
        var inputRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(surface));
        var first = surface.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyEnabled);
        await Until(() => Within(first, inputRoot.FocusManager?.GetFocusedElement()), "Old completion must not steal replacement row focus.");
        Assert.InRange(Scale(surface).ScaleX, .9999, 1.0001);
        Assert.InRange(Scale(surface).ScaleY, .9999, 1.0001);
        Assert.InRange(Shift(surface).Y, -.0001, .0001);
        Assert.Equal(1, surface.Opacity);
        Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision);
        EscapeNow(inputRoot); Retired(panel, current);
    }


    // Additional actual exit-frame evidence. The chosen Calm close should
    // remain within2px/.024scale travel before its finite retirement; these
    // assertions do not pretend an opening sample proves closing behavior.
    [AvaloniaTheory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("bowler")]
    public async Task CalmActualExitUsesShortTravelAndRetiresWithoutChangingMatch(string role)
    {
        using var scope = new Scope(); OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
        var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var opened = await Open(window, panel, role); await Settle(650);
        Assert.Equal(1, opened.Surface.Opacity);
        Assert.InRange(Scale(opened.Surface).ScaleX, .9999, 1.0001);
        Assert.InRange(Scale(opened.Surface).ScaleY, .9999, 1.0001);
        Assert.InRange(Shift(opened.Surface).Y, -.0001, .0001);
        string before = State(match); int revision = match.DeliveryRevision;
        var measured = SourceGeometry(window,Anchor(panel,role),opened.Surface);
        var clock = Stopwatch.StartNew(); EscapeNow(opened.Root);
        Assert.True(opened.Popup.IsOpen); Assert.Same(opened.Popup, Read<Popup?>(panel, "_retiringPlayerMenu"));
        var samples = new List<(double Milliseconds, bool Open, double X, double Y, double Shift, double Opacity)>();
        for (int frame = 0; frame < 8 && opened.Popup.IsOpen; frame++)
        {
            await Settle(12);
            if (!opened.Popup.IsOpen) break;
            using var rendered = opened.Root.CaptureRenderedFrame(); Assert.NotNull(rendered);
            samples.Add((clock.Elapsed.TotalMilliseconds, opened.Popup.IsOpen, Scale(opened.Surface).ScaleX,
                Scale(opened.Surface).ScaleY, Shift(opened.Surface).Y, opened.Surface.Opacity));
            if (Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is { Length: > 0 } dir)
            { Directory.CreateDirectory(dir); rendered!.Save(Path.Combine(dir, $"menu-calm-close-{role}-{frame:D2}.png")); }
        }
        Record("menu-calm-close-" + role, samples.Select(s => new { s.Milliseconds, s.Open, s.X, s.Y, s.Shift, s.Opacity }));
        // Real rendered mid-fade geometry is the witness, regardless of frame age.
        // Calm lasts97.5ms: a captured21ms pose is valid even if the next
        // scheduled poll arrives after the popup has already retired.
        var meaningful = samples.Where(s => s.Open && s.Opacity > .01 && s.Opacity < .99).ToArray();
        Assert.NotEmpty(meaningful);
        Assert.All(meaningful, s =>
        {
            Assert.InRange(s.X, .97599, .99999); Assert.InRange(s.Y, .97599, .99999);
            if(measured.Direction==0)Assert.InRange(s.Shift,-.0001,.0001);
            else Assert.InRange(s.Shift*measured.Direction,.00001,2.0001);
        });
        await Until(() => !opened.Popup.IsOpen, "The actual Calm exit must complete its popup lifetime.");
        Retired(panel, opened.Popup);
        Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision);
        Assert.Same(Anchor(panel, role), window.FocusManager?.GetFocusedElement());
    }

    // Source labels must not retain a prior match, and a confirmed player
    // replacement must update the same persistent source rather than a new
    // button or a private-label-only test fixture.
    [AvaloniaTheory]
    [InlineData("top")] [InlineData("bottom")] [InlineData("bowler")]
    public async Task SourceNameClearsOldMatchAndTracksConfirmedPlayerReplacement(string role)
    {
        using var scope = new Scope(); var match = CricketReliabilityChecks.CreateMatch(true);
        foreach (var player in match.CurrentInnings!.BattingOrder.Concat(match.CurrentInnings.BowlingAttack))
        { player.FirstName = "Original"; player.LastName = "Match " + player.RosterIndex; }
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(650);
        var anchor = Anchor(panel, role); await Reveal(window, anchor); await StableReachable(window, anchor);
        string oldName = AutomationProperties.GetName(anchor) ?? "";
        Assert.Contains("Original", oldName, StringComparison.Ordinal);
        string oldState = State(match); int oldRevision = match.DeliveryRevision;
        panel.ClearMatch();
        string cleared = AutomationProperties.GetName(anchor) ?? "";
        Assert.Contains("options", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Original", cleared, StringComparison.Ordinal);
        Assert.DoesNotContain("striker", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(oldState, State(match)); Assert.Equal(oldRevision, match.DeliveryRevision);

        var replacement = CricketReliabilityChecks.CreateMatch(true);
        foreach (var player in replacement.CurrentInnings!.BattingOrder.Concat(replacement.CurrentInnings.BowlingAttack))
        { player.FirstName = "Replacement"; player.LastName = "Player " + player.RosterIndex; }
        panel.SetMatch(replacement); Assert.Same(anchor, Anchor(panel, role));
        string rebound = AutomationProperties.GetName(anchor) ?? "";
        Assert.Contains("Replacement", rebound, StringComparison.Ordinal); Assert.DoesNotContain("Original", rebound, StringComparison.Ordinal);
        var innings = replacement.CurrentInnings!;
        int beforeIndex = role == "top" ? Read<int>(panel, "_cpTopBatterIndex")
            : role == "bottom" ? Read<int>(panel, "_cpBottomBatterIndex") : innings.CurrentBowlerIndex;
        int chosen = role == "bowler" ? Enumerable.Range(0, innings.BowlingAttack.Count).First(i => i != beforeIndex && replacement.CanSelectBowler(i))
            : Enumerable.Range(0, innings.BattingOrder.Count).First(i => i != innings.StrikerIndex && i != innings.NonStrikerIndex && !innings.BattingOrder[i].IsOut);
        var selected = role == "bowler" ? innings.BowlingAttack[chosen] : innings.BattingOrder[chosen];
        selected.FirstName = "Confirmed"; selected.LastName = "Unique " + role;
        int revision = replacement.DeliveryRevision;
        if (role == "bowler") replacement.ConfirmBowler(chosen);
        else if (role == "top") replacement.SetStriker(chosen);
        else replacement.SetNonStriker(chosen);
        Assert.Same(anchor, Anchor(panel, role));
        int actualIndex = role == "top" ? Read<int>(panel, "_cpTopBatterIndex")
            : role == "bottom" ? Read<int>(panel, "_cpBottomBatterIndex") : innings.CurrentBowlerIndex;
        Assert.Equal(chosen, actualIndex); Assert.NotEqual(beforeIndex, actualIndex);
        string actual = AutomationProperties.GetName(anchor) ?? "";
        Assert.Contains(selected.FullName, actual, StringComparison.Ordinal);
        Assert.DoesNotContain("Original", actual, StringComparison.Ordinal);
        Assert.Equal(revision, replacement.DeliveryRevision); Assert.Empty(innings.Deliveries);
        Assert.Equal(oldState, State(match)); Assert.Equal(oldRevision, match.DeliveryRevision);
    }


    // Automation-name observers are real synchronous property notifications.
    // Public SetMatch during the new naming handoff must stop both obsolete
    // UpdateUI and ClearMatch continuations, preserving the replacement log.
    [AvaloniaTheory][InlineData("refresh")][InlineData("clear")][InlineData("set-match")]
    public async Task SourceNameObserverCanRebindWithoutResumingObsoleteOuterRefresh(string route)
    {
        using var scope = new Scope();
        var old = CricketReliabilityChecks.CreateMatch(true); old.RecordDelivery(1, CricketDeliveryType.Wide);
        var replacement = CricketReliabilityChecks.CreateMatch(true); replacement.RecordDelivery(4, CricketDeliveryType.Four);
        foreach (var player in replacement.CurrentInnings!.BattingOrder.Concat(replacement.CurrentInnings.BowlingAttack))
        { player.FirstName = "Replacement"; player.LastName = "Observed " + player.RosterIndex; }
        var panel = new CricketControlPanel(); panel.SetMatch(old);
        var window = scope.Panel(panel); await Settle(650);
        var anchor = Anchor(panel, "top"); await Reveal(window, anchor); await StableReachable(window, anchor);
        string initialName = AutomationProperties.GetName(anchor) ?? "";
        Assert.False(string.IsNullOrWhiteSpace(initialName));
        string replacementBefore = State(replacement); int revision = replacement.DeliveryRevision;
        var attempted = CricketReliabilityChecks.CreateMatch(true);
        string attemptedBefore = State(attempted); int attemptedRevision = attempted.DeliveryRevision;
        int changes = 0, nestedLogCount = -1; string? nestedOver = null, nestedName = null;
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property != AutomationProperties.NameProperty || changes != 0) return;
            changes++; panel.SetMatch(replacement);
            nestedLogCount = Named<StackPanel>(panel, "EventLog").Children.Count;
            nestedOver = Named<TextBlock>(panel, "OverDisplay").Text;
            nestedName = AutomationProperties.GetName(anchor);
        }
        anchor.PropertyChanged += Changed;
        try
        {
            if (route == "clear") panel.ClearMatch();
            else if (route == "set-match") panel.SetMatch(attempted);
            else old.RotateStrike();
            Assert.Equal(1, changes); Assert.Same(replacement, Read<CricketMatchManager?>(panel, "_match"));
            Assert.True(nestedLogCount > 0); Assert.Equal(nestedLogCount, Named<StackPanel>(panel, "EventLog").Children.Count);
            Assert.Equal(nestedOver, Named<TextBlock>(panel, "OverDisplay").Text);
            Assert.Equal($"This over: {string.Join("  ", replacement.CurrentInnings!.CurrentOverBalls)}", nestedOver);
            Assert.NotEqual($"This over: {string.Join("  ", old.CurrentInnings!.CurrentOverBalls)}", nestedOver);
            Assert.Equal(nestedName, AutomationProperties.GetName(anchor));
            Assert.Contains("Replacement", nestedName ?? "", StringComparison.Ordinal);
            Assert.Same(replacement.CurrentInnings, Read<CricketInnings?>(panel, "_loggedInnings"));
            Assert.Equal(1, Read<int>(panel, "_loggedDeliveryCount"));
            Assert.Equal(replacementBefore, State(replacement)); Assert.Equal(revision, replacement.DeliveryRevision);
            Assert.Single(old.CurrentInnings.Deliveries); Assert.Equal(1, old.TotalRuns);
            Assert.Equal(attemptedBefore, State(attempted)); Assert.Equal(attemptedRevision, attempted.DeliveryRevision);
        }
        finally { anchor.PropertyChanged -= Changed; }
    }


    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task ActualMidCloseOwnerRemovalRetiresThePopupEvenWithoutAnimationCompletion(bool closeWindow)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Panel(panel);await Settle(650);
        var opened=await Open(window,panel,"top");await Settle(650);string before=State(match);int revision=match.DeliveryRevision;
        EscapeNow(opened.Root);Assert.True(opened.Popup.IsOpen);Assert.Same(opened.Popup,Read<Popup?>(panel,"_retiringPlayerMenu"));
        if(closeWindow)window.Close();else window.Content=null;
        await Until(()=>!panel.IsLoaded,"Actual owner removal must unload the panel.");
        await Settle(250);Retired(panel,opened.Popup);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        // A later real preference notification must not resurrect the retired UI.
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await Settle(180);Retired(panel,opened.Popup);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
    }


    [AvaloniaTheory][InlineData("top")][InlineData("bottom")][InlineData("bowler")]
    public async Task MenuExpansionOriginTracksItsActualSourceAcrossMeasuredPlacements(string role)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Panel(panel);await Settle(650);string before=State(match);int revision=match.DeliveryRevision;
        var opened=await Open(window,panel,role);await Settle(650);
        var anchor=Anchor(panel,role);
        var source=opened.Surface.PointFromScreen(anchor.PointToScreen(new Point(anchor.Bounds.Width/2,anchor.Bounds.Height/2)));
        Assert.True(opened.Surface.Bounds.Width>100&&opened.Surface.Bounds.Height>50);
        var independentlyMeasured=SourceGeometry(window,anchor,opened.Surface);
        var expected=independentlyMeasured.Origin;
        var actual=opened.Surface.RenderTransformOrigin.Point;
        Record("menu-source-origin-"+role,new{source,expected,actual,surface=opened.Surface.Bounds,anchor=Painted(window,anchor)});
        if(role=="top")Assert.True(source.Y<0,"The top source should actually witness a below-source placement.");
        if(role=="bowler")Assert.True(source.Y>opened.Surface.Bounds.Height,"The bowler source should actually witness an above-source placement.");
        Assert.True(Math.Abs(actual.X-expected.X)<.002&&Math.Abs(actual.Y-expected.Y)<.002,
            $"Expansion should originate at its real source: expected {expected}, actual {actual}.");
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        EscapeNow(opened.Root);await Settle(250);Retired(panel,opened.Popup);
    }



    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task PendingArrivalCannotRestartAfterRealReduceOrOwnerCloseBeforeLoaded(bool closeOwner)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Panel(panel);await Settle(650);string before=State(match);int revision=match.DeliveryRevision;
        var root=Named<Grid>(panel,"CricketRoot");Popup? captured=null;int notifications=0;
        void Added(object? sender,NotifyCollectionChangedEventArgs args)
        {
            if(args.NewItems is null)return;
            foreach(var popup in args.NewItems.OfType<Popup>())popup.Opened+=(_,_)=>
            {
                captured=popup;notifications++;
                var surface=Assert.IsAssignableFrom<Control>(popup.Child);
                Assert.False(surface.IsLoaded);Assert.Equal(0,surface.Opacity);
                Record("pending-arrival-"+closeOwner,new{surface.IsLoaded,surface.Opacity,popup.IsOpen,closeOwner});
                if(closeOwner)window.Close();else OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            };
        }
        ((INotifyCollectionChanged)root.Children).CollectionChanged+=Added;
        try
        {
            if(closeOwner)
            {
                var anchor=Anchor(panel,"top");await Reveal(window,anchor);await StableReachable(window,anchor);TapNow(window,anchor);
                await Until(()=>!panel.IsLoaded,"Closing the actual owner must unload the pending menu's panel.");
                Assert.False(window.IsVisible);Assert.NotNull(captured);Retired(panel,captured!);
            }
            else
            {
                var opened=await Open(window,panel,"top");Assert.Same(captured,opened.Popup);await Settle(650);
                Assert.True(OperatorAppearance.ReduceMotion);Assert.Equal(1,opened.Surface.Opacity);
                Assert.Equal(1,Scale(opened.Surface).ScaleX);Assert.Equal(1,Scale(opened.Surface).ScaleY);Assert.Equal(0,Shift(opened.Surface).Y);
                Assert.False(Motion.IsAnimating(opened.Surface));Assert.False(Motion.IsAnimating(Scale(opened.Surface)));Assert.False(Motion.IsAnimating(Shift(opened.Surface)));
                EscapeNow(opened.Root);await Settle(100);Retired(panel,opened.Popup);
            }
            Assert.Equal(1,notifications);Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        }
        finally{((INotifyCollectionChanged)root.Children).CollectionChanged-=Added;}
        // Open a fresh owner/menu; queued work from the cancelled arrival must
        // neither resurrect its old child nor take the current row's focus.
        Assert.NotNull(captured);var old=captured!;var oldPanel=panel;
        if(closeOwner)
        {
            // A closed native accessibility wrapper still owns its old body.
            // A fresh owner uses a fresh panel bound to the same live model;
            // no unsupported visual reparenting is part of this acceptance.
            oldPanel.ClearMatch();panel=new CricketControlPanel();panel.SetMatch(match);
            window=scope.Panel(panel);await Settle(650);
        }
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        var current=await Open(window,panel,"top");Assert.NotSame(old,current.Popup);await Settle(650);
        Assert.True(current.Popup.IsOpen);Assert.True(Within(current.First,current.Root.FocusManager?.GetFocusedElement()));Retired(oldPanel,old);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);
        EscapeNow(current.Root);await Settle(250);Retired(panel,current.Popup);
    }

    private static (Point Origin,int Direction) SourceGeometry(Window window,Control anchor,Control surface)
    {
        // Independent owner-coordinate bounds rather than the product's
        // cross-root screen conversion or its private direction variable.
        Assert.Same(window,TopLevel.GetTopLevel(surface));
        var button=Painted(window,anchor);var card=Painted(window,surface);
        var source=new Point(button.Center.X-card.Left,button.Center.Y-card.Top);
        return (new Point(Math.Clamp(source.X/card.Width,0,1),Math.Clamp(source.Y/card.Height,0,1)),
            source.Y<0?-1:source.Y>card.Height?1:0);
    }

    private sealed class Scope : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool oldSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldMotion = OperatorMotion.AnimationOverride;
        private readonly bool oldGlass = OperatorAppearance.GlassEnabled;
        private readonly bool oldReduce = OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle oldMotionStyle = OperatorAppearance.MotionStyle;
        private readonly AppTheme oldTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity oldDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly string? oldHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-selector-cancel-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> directories = new(StringComparer.Ordinal);
        private readonly List<Window> windows = new();
        private readonly List<CricketControlPanel> panels = new();
        private bool captured, disposed;
        internal Scope(bool reduced=false)
        {
            try
            {
                SelfTestStorage.Initialize(true); Assert.True(SelfTestStorage.IsEnabled);
                if (Directory.Exists(SelfTestStorage.RootPath))
                {
                    foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath)) directories.Add(directory);
                    foreach (string file in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)) files[file] = File.ReadAllBytes(file);
                }
                captured = true; foreach (string file in files.Keys) File.Delete(file);
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", dataHome); Directory.CreateDirectory(dataHome);
                app.IsSelfTestMode = true;
                // Leave the override unset: these tests must observe the real
                // operator preference changing while a popup is retiring.
                OperatorMotion.AnimationOverride = null;
                OperatorAppearance.Configure(oldGlass, reduced);
                OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
                Assert.Equal(reduced, OperatorAppearance.ReduceMotion);
                Assert.Equal(OperatorMotionStyle.Expressive, OperatorAppearance.MotionStyle);
                Assert.Null(OperatorMotion.AnimationOverride);
                Assert.Equal(!reduced, OperatorMotion.Enabled);
            }
            catch { Dispose(); throw; }
        }
        internal NativeWindow Window(Control content, int width, int height)
        {
            var window = new NativeWindow { Width = width, Height = height, Content = content }; windows.Add(window);
            panels.AddRange(content is CricketControlPanel panel ? new[] { panel } : content.GetVisualDescendants().OfType<CricketControlPanel>());
            window.Show(); return window;
        }
        internal NativeWindow Panel(CricketControlPanel panel) => Window(panel, 700, 450);
        internal Roche_Scoreboard.MainWindow Main()
        {
            var window = new Roche_Scoreboard.MainWindow(); windows.Add(window); window.Show(); window.Width = 1280; window.Height = 850; return window;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            foreach (var panel in panels) Attempt(panel.ClearMatch);
            foreach (var window in windows.AsEnumerable().Reverse()) Attempt(() =>
            {
                if (window is Roche_Scoreboard.MainWindow main)
                {
                    Named<CricketControlPanel>(main, "CricketControlPanelView").ClearMatch();
                    Read<Window?>(main, "_cricketDisplayWindow")?.Close(); Read<Window?>(main, "_displayWindow")?.Close();
                }
                foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); Dispatcher.UIThread.RunJobs();
            });
            Attempt(() => OperatorAppearance.ConfigureMotionStyle(oldMotionStyle));
            Attempt(() => OperatorAppearance.Configure(oldGlass, oldReduce));
            Attempt(() => OperatorMotion.AnimationOverride = oldMotion); Attempt(() => app.IsSelfTestMode = oldSelfTest);
            Attempt(() => app.ThemeService.SetTheme(oldTheme)); Attempt(() => app.ThemeService.SetDensity(oldDensity));
            if (captured)
            {
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath)) foreach (string file in Directory.EnumerateFiles(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)) if (!files.ContainsKey(file)) File.Delete(file); });
                foreach (var (path, bytes) in files) Attempt(() => { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); });
                Attempt(() => { if (Directory.Exists(SelfTestStorage.RootPath)) foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories).Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length))
                    if (!directories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); });
            }
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", oldHome));
            Attempt(() => { if (Directory.Exists(dataHome)) Directory.Delete(dataHome, true); });
            if (errors.Count > 0) throw new AggregateException("Cricket selector fixture cleanup failed", errors);
        }
    }
}
