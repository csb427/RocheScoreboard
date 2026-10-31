// Real-route selector cancellation regressions. No private Show/handler invocation.
using System.Reflection;
using System.Text.Json;
using Avalonia;
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

[CollectionDefinition("Cricket selector cancel baseline", DisableParallelization = true)]
public sealed class CricketSelectorCancelCollection { }

[Collection("Cricket selector cancel baseline")]
public sealed class CricketSelectorCancelBaselineTests
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
    private static Border Overlay(CricketControlPanel panel, bool bowling) =>
        Named<Border>(panel, bowling ? "BowlerSelectionOverlay" : "BatterSelectionOverlay");
    private static ListBox Choices(CricketControlPanel panel, bool bowling) =>
        Named<ListBox>(panel, bowling ? "BowlerSelectionList" : "BatterSelectionList");
    private static Button Confirm(CricketControlPanel panel, bool bowling) =>
        Named<Button>(panel, bowling ? "ConfirmBowlerButton" : "ConfirmBatterButton");
    private static Button Skip(CricketControlPanel panel, bool bowling) =>
        Assert.Single(Overlay(panel, bowling).GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "SKIP FOR NOW"));
    private static Button Dot(CricketControlPanel panel) => Assert.Single(
        Named<Border>(panel, "ScoringCommands").GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "•"));
    private static void Closed(CricketControlPanel panel)
    { Assert.False(Overlay(panel, false).IsVisible); Assert.False(Overlay(panel, true).IsVisible); }
    private static void Gated(CricketControlPanel panel)
    {
        Assert.False(Named<Border>(panel, "ScoringCommands").IsEnabled);
        Assert.False(Named<Border>(panel, "ExtraCommands").IsEnabled);
        Assert.True(Named<Button>(panel, "ScoringLockBanner").IsEffectivelyVisible);
        Assert.False(Dot(panel).IsEffectivelyEnabled);
    }
    private static CricketMatchManager Required(bool bowling)
    {
        var match = CricketReliabilityChecks.CreateMatch(ready: false);
        Assert.False(match.TossPending);
        if (bowling) { match.SetStriker(0); match.SetNonStriker(1); }
        Assert.Equal(!bowling, match.CurrentInnings!.NeedsBatterSelection);
        Assert.True(match.CurrentInnings.NeedsBowlerSelection);
        return match;
    }
    private static async Task<Button> Open(Window window, CricketControlPanel panel, bool bowling)
    {
        Closed(panel);
        var source = Named<Button>(panel, bowling ? "SelectOpeningBowler" : "SelectOpeningBatters");
        if(source.IsEffectivelyVisible)
        {
            await Reveal(window, source);await Key(window, PhysicalKey.Enter);
        }
        else
        {
            // In a ready innings the required-selection action is correctly
            // hidden. Open the actual bowler menu rather than forcing a gate.
            Assert.True(bowling,"This fixture's optional chooser route is the bowler options menu.");
            source=Assert.Single(Named<Border>(panel,"BowlingZone").GetVisualDescendants().OfType<Button>(),
                button=>Equals(ToolTip.GetTip(button),"Options"));
            await Reveal(window,source);await Tap(window,source);
            var popup=Read<Avalonia.Controls.Primitives.Popup>(panel,"_playerMenu");
            await Until(()=>popup.IsOpen && popup.Child is Control,"The actual bowler options popup must open.");
            var choose=Assert.Single(((Control)popup.Child!).GetVisualDescendants().OfType<Button>(),
                button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Choose another bowler");
            // Explicitly render the actual popup host in this headless fixture;
            // IsOpen alone is not evidence that its child has reached a visual root.
            if(popup.Host is Control host){host.ApplyTemplate();host.UpdateLayout();}
            var hostTop=popup.Host is Visual hostVisual?TopLevel.GetTopLevel(hostVisual):null;
            using(var frame=(hostTop??window).CaptureRenderedFrame())
            {
                if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} menuDirectory)
                {Directory.CreateDirectory(menuDirectory);frame?.Save(Path.Combine(menuDirectory,"actual-bowler-menu.png"));}
            }
            Dispatcher.UIThread.RunJobs();
            if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} menuDiagnostics)
                File.WriteAllText(Path.Combine(menuDiagnostics,"actual-bowler-menu.json"),JsonSerializer.Serialize(new
                {popup.IsOpen,hostType=popup.Host?.GetType().Name,hostRoot=hostTop?.GetType().Name,childLoaded=((Control)popup.Child!).IsLoaded,
                 rowLoaded=choose.IsLoaded,sourceRoot=TopLevel.GetTopLevel(source)?.GetType().Name,sourceFocusable=source.Focusable,
                 sourceName=source.Name,focusType=window.FocusManager?.GetFocusedElement()?.GetType().Name,
                 host=popup.Host is Control hc?new{hc.IsLoaded,hc.IsVisible,hc.IsEffectivelyVisible,hc.IsMeasureValid,hc.IsArrangeValid,
                    desired=hc.DesiredSize.ToString(),bounds=hc.Bounds.ToString(),theme=hc.Theme?.GetType().Name,template=(hc as Avalonia.Controls.Primitives.TemplatedControl)?.Template?.GetType().Name,
                    presenter=(hc as Avalonia.Controls.ContentControl)?.Presenter?.GetType().Name,
                    contentSame=ReferenceEquals((hc as ContentControl)?.Content,popup.Child),visualChildren=hc.GetVisualChildren().Select(v=>v.GetType().Name).ToArray(),
                    logicalParent=(hc as Avalonia.LogicalTree.ILogical)?.LogicalParent?.GetType().Name}:null,
                 popupParent=(popup as Avalonia.LogicalTree.ILogical)?.LogicalParent?.GetType().Name,
                 presenter=popup.Host is ContentControl cc && cc.Presenter is {} pr?new{pr.IsLoaded,pr.IsVisible,pr.IsEffectivelyVisible,
                    bounds=pr.Bounds.ToString(),desired=pr.DesiredSize.ToString(),contentType=pr.Content?.GetType().Name,contentSame=ReferenceEquals(pr.Content,popup.Child),children=pr.GetVisualChildren().Select(v=>v.GetType().Name).ToArray()}:null},new JsonSerializerOptions{WriteIndented=true}));
            await Until(()=>TopLevel.GetTopLevel(choose) is not null && choose.IsEffectivelyVisible,
                "The actual opened popup row must attach to its real input root. IsOpen="+popup.IsOpen+" ChildLoaded="+((Control)popup.Child!).IsLoaded+" RowLoaded="+choose.IsLoaded);
            var popupRoot=Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(choose));
            await Until(()=>Within(choose,popupRoot.FocusManager?.GetFocusedElement()),"The actual popup must focus its first Choose another bowler action.");
            popupRoot.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);
            popupRoot.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);
            await Settle();
        }
        await Until(() => Overlay(panel, bowling).IsEffectivelyVisible && Choices(panel, bowling).Items.Count > 0,
            "Actual source Enter must open the required selector.");
        Assert.False(Overlay(panel, !bowling).IsVisible);
        var list=Choices(panel,bowling);var actualFocus=window.FocusManager?.GetFocusedElement();
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,"selector-open-"+bowling+".png"));
            File.WriteAllText(Path.Combine(directory,"selector-open-"+bowling+".json"),JsonSerializer.Serialize(new
            {bowling,list.Focusable,list.IsEffectivelyVisible,list.IsEffectivelyEnabled,list.IsLoaded,list.Bounds,selected=list.SelectedIndex,
                focusType=actualFocus?.GetType().Name,focusName=(actualFocus as Control)?.Name,
                containers=list.GetVisualDescendants().OfType<ListBoxItem>().Select(item=>new{item.Focusable,item.IsEffectivelyVisible,item.IsEffectivelyEnabled,item.Bounds}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        }
        Assert.True(Within(list, actualFocus), "Show must establish real list/item focus. Actual="+(actualFocus as Control)?.Name+":"+actualFocus?.GetType().Name+" ListFocusable="+list.Focusable);
        Assert.InRange(Choices(panel, bowling).SelectedIndex, 0, Choices(panel, bowling).Items.Count - 1);
        return source;
    }
    private static async Task FocusChild(Window window, CricketControlPanel panel, bool bowling, string child)
    {
        if(child=="item")
        {
            await Key(window,PhysicalKey.ArrowDown);
            var current=Choices(panel,bowling);var focused=window.FocusManager?.GetFocusedElement();
            var item=Assert.IsAssignableFrom<ListBoxItem>(focused);
            Assert.Contains(item,current.GetVisualDescendants());Assert.True(item.IsSelected);return;
        }
        Control target = child switch { "list" => Choices(panel, bowling), "confirm" => Confirm(panel, bowling), "skip" => Skip(panel, bowling), _ => throw new ArgumentOutOfRangeException(nameof(child)) };
        if (child != "list") await Reveal(window, target);
        Assert.True(Within(target, window.FocusManager?.GetFocusedElement()), "The named actual selector child must own Escape.");
    }

    // 6 first baselines: the opening preselection must remain uncommitted.
    [AvaloniaTheory]
    [InlineData(false, "list")] [InlineData(false, "confirm")] [InlineData(false, "skip")]
    [InlineData(true, "list")] [InlineData(true, "confirm")] [InlineData(true, "skip")]
    [InlineData(false,"item")] [InlineData(true,"item")]
    public async Task EscapeFromActualSelectorChildCancelsWithoutChoosing(bool bowling, string child)
    {
        using var scope = new Scope();
        var match = Required(bowling); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(600); Gated(panel);
        var source = await Open(window, panel, bowling); await FocusChild(window, panel, bowling, child);
        string before = State(match); int revision = match.DeliveryRevision, events = 0;
        void Changed() => events++;
        match.MatchChanged += Changed;
        try
        {
            await Key(window, PhysicalKey.Escape);
            Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events);
            Closed(panel); Gated(panel);
            Assert.Same(source, window.FocusManager?.GetFocusedElement());
            await Key(window, PhysicalKey.Escape);
            Assert.Equal(before, State(match)); Assert.Equal(0, events); Closed(panel); Gated(panel);
        }
        finally { match.MatchChanged -= Changed; }
    }

    // 2 controls that must preserve existing Skip data behavior; focus is a new oracle.
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public async Task PointerSkipCancelsWithoutChoosingAndReturnsToActualSource(bool bowling)
    {
        using var scope = new Scope();
        var match = Required(bowling); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(600); Gated(panel);
        var source = await Open(window, panel, bowling); string before = State(match); int revision = match.DeliveryRevision, events = 0;
        void Changed() => events++;
        match.MatchChanged += Changed;
        try
        {
            await Tap(window, Skip(panel, bowling));
            Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision); Assert.Equal(0, events); Closed(panel); Gated(panel);
            Assert.Same(source, window.FocusManager?.GetFocusedElement());
        }
        finally { match.MatchChanged -= Changed; }
    }

    [AvaloniaFact]
    public async Task SixRealDotsCancelToCurrentGateThenEnterReopensAndEligibleConfirmResumesScoring()
    {
        using var scope = new Scope();
        var match = CricketReliabilityChecks.CreateMatch(ready: true); var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = scope.Panel(panel); await Settle(600); var innings = match.CurrentInnings!;
        Assert.False(innings.NeedsBatterSelection); Assert.False(innings.NeedsBowlerSelection);
        Assert.True(Named<Border>(panel, "ScoringCommands").IsEnabled);
        int oldBowler = innings.CurrentBowlerIndex;
        for (int ball = 1; ball <= 6; ball++)
        {
            await Reveal(window, Dot(panel)); await Tap(window, Dot(panel));
            Assert.Equal(ball, innings.LegalBallsBowled); Assert.Equal(ball, innings.Deliveries.Count);
        }
        Assert.Equal(1, innings.CompletedOvers); Assert.True(innings.NeedsBowlerSelection); Gated(panel);
        Assert.True(Overlay(panel, true).IsVisible); Assert.Equal("End of over — choose the next bowler", Named<TextBlock>(panel, "BowlerSelectionHint").Text);
        Assert.True(Within(Choices(panel, true), window.FocusManager?.GetFocusedElement()));
        var expectedEligible = Enumerable.Range(0, innings.BowlingAttack.Count).Where(match.CanSelectBowler).OrderBy(index => index).ToArray();
        var firstMap = Assert.IsType<List<int>>(Choices(panel, true).Tag);
        Assert.Equal(expectedEligible, firstMap.OrderBy(index => index)); Assert.DoesNotContain(oldBowler, firstMap);
        int suggested = match.SuggestedNextBowler(); Assert.Equal(suggested, firstMap[Choices(panel, true).SelectedIndex]);
        string before = State(match); int revision = match.DeliveryRevision;
        await Key(window, PhysicalKey.Escape);
        Assert.Equal(before, State(match)); Assert.Equal(revision, match.DeliveryRevision); Closed(panel); Gated(panel);
        var gate = Named<Button>(panel, "ScoringLockBanner"); Assert.True(gate.Focusable,"The current scoring gate must be focusable for a real keyboard return."); Assert.Same(gate, window.FocusManager?.GetFocusedElement());
        await StableReachable(window,gate);Assert.True(Ready(window,gate));
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(directory,"returned-scoring-gate.png"));
            File.WriteAllText(Path.Combine(directory,"returned-scoring-gate.json"),JsonSerializer.Serialize(new
            {painted=Painted(window,gate),window.ClientSize,focus=(window.FocusManager?.GetFocusedElement() as Control)?.Name,
                labels=gate.GetVisualDescendants().OfType<TextBlock>().Select(t=>new{t.Name,t.Text,painted=Painted(window,t)}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        }
        await Key(window, PhysicalKey.Enter);
        Assert.True(Overlay(panel, true).IsVisible); Assert.Equal(before, State(match));
        var reopenedMap = Assert.IsType<List<int>>(Choices(panel, true).Tag);
        Assert.Equal(expectedEligible, reopenedMap.OrderBy(index => index));
        int chosen = reopenedMap[Choices(panel, true).SelectedIndex]; Assert.True(match.CanSelectBowler(chosen));
        int changed = 0; void Changed() => changed++; match.MatchChanged += Changed;
        try { await Tap(window, Confirm(panel, true)); Assert.Equal(1, changed); }
        finally { match.MatchChanged -= Changed; }
        await Settle(600); Closed(panel); Assert.Equal(chosen, innings.CurrentBowlerIndex); Assert.False(innings.NeedsBowlerSelection);
        Assert.Equal(6, innings.LegalBallsBowled); Assert.Equal(6, innings.Deliveries.Count);
        Assert.True(Named<Border>(panel, "ScoringCommands").IsEnabled);
        await Reveal(window, Dot(panel)); await Tap(window, Dot(panel));
        Assert.Equal(7, innings.LegalBallsBowled); Assert.Equal(7, innings.Deliveries.Count);
    }

    [AvaloniaFact]
    public async Task CancelFromSecondBatterKeepsAcceptedStrikerAndOriginalOpeningSource()
    {
        using var scope = new Scope(); var match = Required(false); var innings = match.CurrentInnings!;
        var panel = new CricketControlPanel(); panel.SetMatch(match); var window = scope.Panel(panel); await Settle(600);
        var source = await Open(window, panel, false);
        int striker = Assert.IsType<List<int>>(Choices(panel, false).Tag)[Choices(panel, false).SelectedIndex];
        await Tap(window, Confirm(panel, false));
        Assert.Equal(striker, innings.StrikerIndex); Assert.Null(innings.NonStriker); Assert.True(innings.NeedsBatterSelection);
        Assert.True(Overlay(panel, false).IsVisible); Assert.DoesNotContain(striker, Assert.IsType<List<int>>(Choices(panel, false).Tag));
        string accepted = State(match); await FocusChild(window, panel, false, "confirm"); await Key(window, PhysicalKey.Escape);
        Assert.Equal(accepted, State(match)); Assert.Equal(striker, innings.StrikerIndex); Assert.Null(innings.NonStriker);
        Closed(panel); Gated(panel); Assert.Same(source, window.FocusManager?.GetFocusedElement());
        await Key(window, PhysicalKey.Enter); Assert.True(Overlay(panel, false).IsVisible);
        int nonStriker = Assert.IsType<List<int>>(Choices(panel, false).Tag)[Choices(panel, false).SelectedIndex];
        Assert.NotEqual(striker, nonStriker); await Tap(window, Confirm(panel, false));
        Assert.Equal(striker, innings.StrikerIndex); Assert.Equal(nonStriker, innings.NonStrikerIndex); Assert.False(innings.NeedsBatterSelection);
        Assert.True(Overlay(panel, true).IsVisible); Assert.True(innings.NeedsBowlerSelection); Gated(panel);
    }

    // Public lifecycle changes happen immediately after real Escape, before an
    // awaited dispatcher settlement. No test invokes a queued callback or Show.
    [AvaloniaTheory] [InlineData("rebind")] [InlineData("hide")] [InlineData("detach")] [InlineData("focus-claim")]
    public async Task CancelCannotStealFocusAfterOwnerOrOperatorChangesAndCurrentOpeningStillWorks(string transition)
    {
        using var scope = new Scope(); var oldMatch = Required(true); var panel = new CricketControlPanel(); panel.SetMatch(oldMatch);
        var sentinel = new Button { Name = "OtherOperatorAction", Content = "Other operator action", Height = 36 };
        var host = new DockPanel(); DockPanel.SetDock(sentinel, Dock.Top); host.Children.Add(sentinel); host.Children.Add(panel);
        var window = scope.Window(host, 700, 500); await Settle(600); await Open(window, panel, true);
        string oldState = State(oldMatch); var current = transition == "rebind" ? Required(true) : oldMatch;
        KeyNow(window, PhysicalKey.Escape); Closed(panel); Assert.Equal(oldState, State(oldMatch));
        if (transition == "rebind") panel.SetMatch(current);
        else if (transition == "hide") panel.IsVisible = false;
        else if (transition == "detach") host.Children.Remove(panel);
        // Independent actual pointer focus claim, not Focus() or a private field.
        window.UpdateLayout(); using(var rendered=window.CaptureRenderedFrame()){}
        TapNow(window, sentinel); Assert.Same(sentinel, window.FocusManager?.GetFocusedElement());
        string currentState = State(current); await Settle(600);
        Assert.Same(sentinel, window.FocusManager?.GetFocusedElement()); Assert.Equal(oldState, State(oldMatch)); Assert.Equal(currentState, State(current));
        if (transition == "rebind") Closed(panel);
        if (transition == "detach") host.Children.Add(panel);
        panel.IsVisible = true;
        // Real later opening is the positive control for invalidation; retain
        // this same binding after hide/detach/operator focus changes.
        await Settle(); Closed(panel); Gated(panel);
        await Open(window, panel, true); Assert.True(Within(Choices(panel, true), window.FocusManager?.GetFocusedElement()));
        await Reveal(window, Skip(panel,true)); await StableReachable(window,Skip(panel,true));
        await Tap(window, Skip(panel, true)); Closed(panel); Assert.Equal(currentState, State(current));
    }

    [AvaloniaFact]
    public async Task RealOldConfirmationRetirementCannotHideARequiredSelectorOpenedByPublicModelDots()
    {
        using var scope = new Scope(); var match = CricketReliabilityChecks.CreateMatch(ready: true);
        var panel = new CricketControlPanel(); panel.SetMatch(match); var window = scope.Panel(panel); await Settle(600);
        await Open(window, panel, true);
        // Keep the real close retirement pending: do not await after Confirm.
        TapNow(window, Confirm(panel, true));
        Assert.False(Named<Border>(panel, "BowlerSelectionCard").IsHitTestVisible);
        Assert.False(match.CurrentInnings!.NeedsBowlerSelection);
        // The retiring overlay still blocks underlying physical scoring. Use
        // the real public model route for this lifetime probe; the separate
        // over-boundary test above supplies all-six-button pointer evidence.
        for (int ball = 1; ball <= 6; ball++)
        {
            Assert.False(Named<Border>(panel, "BowlerSelectionCard").IsHitTestVisible); // actual old close remains pending before the sixth ball
            match.RecordDelivery(0, CricketDeliveryType.Dot);
            Assert.Equal(ball, match.CurrentInnings.LegalBallsBowled); Assert.Equal(ball, match.CurrentInnings.Deliveries.Count);
        }
        Assert.True(match.CurrentInnings.NeedsBowlerSelection); Assert.True(Overlay(panel, true).IsVisible);
        string state = State(match); await Settle(600);
        Assert.Null(Read<Avalonia.Controls.Primitives.Popup?>(panel,"_retiringPlayerMenu"));
        Assert.DoesNotContain(Named<Grid>(panel,"CricketRoot").Children,child=>child is Avalonia.Controls.Primitives.Popup);
        Assert.Equal(state, State(match)); Assert.True(Overlay(panel, true).IsVisible); Gated(panel);
        Assert.True(Within(Choices(panel, true), window.FocusManager?.GetFocusedElement()));
    }

    [AvaloniaFact]
    public async Task ActualMainWindowHomeRecoveryRouteYieldsSelectorKeysWithoutAflOrTabEffects()
    {
        using var scope = new Scope(); var seed = CricketReliabilityChecks.CreateMatch(ready: true);
        GameStateService.Save(new GameState { Sport = "cricket", CricketMatchState = seed.CapturePersistenceState() });
        Assert.NotNull(RecoverySession.Describe(GameStateService.Load())); Assert.True(File.Exists(GameStateService.RecoveryStatePath));
        var window = scope.Main(); await Settle(600);
        var home = Named<SportSelectionScreen>(window, "SportSelectionPanel"); home.PrepareForDisplay();
        // Resolve the currently attached Home action; a namescope fallback can
        // refer to an obsolete detached visual from an earlier implementation.
        var resume = Assert.Single(home.GetVisualDescendants().OfType<Button>(),button=>button.Name=="HomeResumeButton");
        await StableReachable(window,resume);
        await Tap(window, resume!);
        var recovery = Assert.Single(window.GetVisualDescendants().OfType<ResumeMatchPrompt>());
        var resumePrompt = Named<Button>(recovery, "ResumeButton");
        await StableReachable(window,resumePrompt);
        await Tap(window, resumePrompt);
        var panel = Named<CricketControlPanel>(window, "CricketControlPanelView");
        await Until(() => panel.IsEffectivelyVisible && !Named<Control>(window, "RecoveryOverlay").IsVisible,
            "Public recovery must enter the actual Cricket panel."); await Settle(600);
        var match = Read<CricketMatchManager>(window, "_cricketMatch");
        Assert.Equal(SportMode.Cricket, Read<SportMode>(window, "_sportMode")); Assert.False(match.TossPending);
        Assert.NotSame(seed, match); Assert.Same(match, Read<CricketMatchManager>(panel, "_match"));
        Assert.True(Named<Border>(panel, "ScoringCommands").IsEnabled);
        var afl = Read<MatchManager>(window, "_match"); string aflBefore = JsonSerializer.Serialize(afl.ToState());
        var source = await Open(window, panel, true); int selectedTab = Named<TabControl>(panel, "MainTabControl").SelectedIndex;
        string before = State(match);
        await Key(window, PhysicalKey.Digit2, RawInputModifiers.Control); // root must yield while selector owns keys
        await Key(window, PhysicalKey.A); // hidden AFL scoring shortcut must have no effects
        Assert.Equal(selectedTab, Named<TabControl>(panel, "MainTabControl").SelectedIndex);
        Assert.Equal(before, State(match)); Assert.Equal(aflBefore, JsonSerializer.Serialize(afl.ToState()));
        await FocusChild(window, panel, true, "confirm"); await Key(window, PhysicalKey.Escape);
        Assert.Equal(before, State(match)); Assert.Equal(aflBefore, JsonSerializer.Serialize(afl.ToState())); Closed(panel);
        Assert.Same(source, window.FocusManager?.GetFocusedElement());
        Assert.True(Named<Border>(panel, "ScoringCommands").IsEnabled);
        Assert.False(Named<Control>(window, "SportSelectionPanel").IsVisible);
    }

    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public async Task ActualListArrowsDoNotCommitAndExplicitKeyboardConfirmUsesCurrentMappedChoice(bool bowling)
    {
        using var scope=new Scope();var match=Required(bowling);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Panel(panel);await Settle(600);await Open(window,panel,bowling);
        var list=Choices(panel,bowling);int start=list.SelectedIndex;Assert.True(list.Items.Count>1);
        string before=State(match);await Key(window,PhysicalKey.ArrowDown);
        Assert.Equal(Math.Min(start+1,list.Items.Count-1),list.SelectedIndex);Assert.Equal(before,State(match));
        int chosen=Assert.IsType<List<int>>(list.Tag)[list.SelectedIndex];
        await Key(window,PhysicalKey.Enter);Assert.Equal(before,State(match));Assert.True(Overlay(panel,bowling).IsVisible);
        await Reveal(window,Confirm(panel,bowling));int notifications=0;void Changed()=>notifications++;match.MatchChanged+=Changed;
        try{await Key(window,PhysicalKey.Enter);Assert.Equal(1,notifications);}
        finally{match.MatchChanged-=Changed;}
        if(bowling){Assert.Equal(chosen,match.CurrentInnings!.CurrentBowlerIndex);Assert.False(match.CurrentInnings.NeedsBowlerSelection);}
        else{Assert.Equal(chosen,match.CurrentInnings!.StrikerIndex);Assert.Null(match.CurrentInnings.NonStriker);Assert.True(match.CurrentInnings.NeedsBatterSelection);}
    }

    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public async Task ReducedMotionCancellationKeepsCurrentModelAndReturnsToOpeningSource(bool bowling)
    {
        using var scope=new Scope(reduced:true);var match=Required(bowling);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Panel(panel);await Settle(600);var source=await Open(window,panel,bowling);
        string before=State(match);int revision=match.DeliveryRevision;
        await Key(window,PhysicalKey.Escape);Closed(panel);Gated(panel);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Same(source,window.FocusManager?.GetFocusedElement());
        await Key(window,PhysicalKey.Enter);Assert.True(Overlay(panel,bowling).IsVisible);
        await Tap(window,Skip(panel,bowling));Closed(panel);Gated(panel);
        Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Same(source,window.FocusManager?.GetFocusedElement());
    }

    [AvaloniaTheory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task SynchronousCollapseObserverCannotRetireNewMatchSelection(bool confirmOld,bool requiredOld)
    {
        using var scope=new Scope(); var old=confirmOld && !requiredOld?CricketReliabilityChecks.CreateMatch(ready:true):Required(true); bool newBowler=confirmOld&&!requiredOld; var replacement=Required(newBowler);
        var panel=new CricketControlPanel();panel.SetMatch(old);var window=scope.Panel(panel);await Settle(600);
        await Open(window,panel,true);var oldOverlay=Overlay(panel,true);
        var replacementSource=Named<Button>(panel,newBowler?"SelectOpeningBowler":"SelectOpeningBatters");int replacements=0;
        void OnCollapse(object? sender,AvaloniaPropertyChangedEventArgs change)
        {
            if(change.Property!=Visual.IsVisibleProperty || oldOverlay.IsVisible || replacements!=0)return;
            replacements++;
            panel.SetMatch(replacement);
            Assert.True(replacementSource.Focus(NavigationMethod.Tab));
            panel.ResumePendingSelection(); // Real public pending workflow, not a private Show/handler.
            Assert.True(Overlay(panel,newBowler).IsVisible);
        }
        oldOverlay.PropertyChanged+=OnCollapse;
        try
        {
            string untouched=State(replacement);
            if(confirmOld) await Tap(window,Confirm(panel,true)); else await Key(window,PhysicalKey.Escape);
            await Until(()=>replacements==1,"The actual old overlay collapse must invoke the public replacement workflow.");
            await Settle(600);Assert.Equal(untouched,State(replacement));
            Assert.True(Overlay(panel,newBowler).IsVisible);Assert.False(Overlay(panel,!newBowler).IsVisible);Gated(panel);
            Assert.True(Within(Choices(panel,newBowler),window.FocusManager?.GetFocusedElement()));
            await Key(window,PhysicalKey.Escape);Closed(panel);Gated(panel);
            Assert.Equal(untouched,State(replacement));Assert.Same(replacementSource,window.FocusManager?.GetFocusedElement());
        }
        finally{oldOverlay.PropertyChanged-=OnCollapse;}
    }

    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public async Task BatterConfirmationCannotWriteIntoReplacementMatchDuringItsOwnCollapse(bool nonStriker)
    {
        using var scope=new Scope();var old=Required(false);if(nonStriker)old.SetStriker(0);
        var replacement=Required(false);var panel=new CricketControlPanel();panel.SetMatch(old);
        var window=scope.Panel(panel);await Settle(600);await Open(window,panel,false);
        string oldState=State(old),newState=State(replacement);int switches=0,oldChanges=0,newChanges=0;
        var overlay=Overlay(panel,false);var source=Named<Button>(panel,"SelectOpeningBatters");
        void OldChanged()=>oldChanges++;void NewChanged()=>newChanges++;
        void OnCollapse(object? sender,AvaloniaPropertyChangedEventArgs change)
        {
            if(change.Property!=Visual.IsVisibleProperty||overlay.IsVisible||switches!=0)return;
            switches++;panel.SetMatch(replacement);Assert.True(source.Focus(NavigationMethod.Tab));panel.ResumePendingSelection();
        }
        old.MatchChanged+=OldChanged;replacement.MatchChanged+=NewChanged;overlay.PropertyChanged+=OnCollapse;
        try
        {
            await Tap(window,Confirm(panel,false));Assert.Equal(1,switches);
            Assert.Equal(oldState,State(old));Assert.Equal(newState,State(replacement));Assert.Equal(0,oldChanges);Assert.Equal(0,newChanges);
            Assert.True(Overlay(panel,false).IsVisible);Gated(panel);Assert.True(Within(Choices(panel,false),window.FocusManager?.GetFocusedElement()));
            await Key(window,PhysicalKey.Escape);Closed(panel);Assert.Same(source,window.FocusManager?.GetFocusedElement());
        }
        finally{old.MatchChanged-=OldChanged;replacement.MatchChanged-=NewChanged;overlay.PropertyChanged-=OnCollapse;}
    }

    // Process globals and latched self-test files are restored even if setup
    // fails. The collection is nonparallel; root should run this feature alone.
    private sealed class Scope : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool oldSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldMotion = OperatorMotion.AnimationOverride;
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
                // Keep natural selector motion enabled for the real-retirement
                // control. Baseline oracles wait existing 600ms fixture duration.
                OperatorMotion.AnimationOverride = !reduced;
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
