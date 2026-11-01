// Outside-repository Delivery D2 metadata and D3 popup baseline draft.
// Self-contained source using current public controls; NOT compiled or run here.
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

[CollectionDefinition("Cricket delivery D2 D3 draft", DisableParallelization = true)]
public sealed class CricketDeliveryD23DraftCollection { }

[Collection("Cricket delivery D2 D3 draft")]
public sealed class CricketDeliveryD23RegressionDraftTests
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
    private static Rect InputPainted(Window window,Control control)
    {
        // Current SettingsChoice has a42px semantic layout slot and a32px
        // painted native Frame. The surrounding five-pixel slot spacing is not
        // part of its input surface. Keep the nominal slot bounds check below,
        // and prove all five positive hits on the actual attached template Frame.
        if(control is ComboBox)
        {
            var frame=Assert.Single(control.GetVisualDescendants().OfType<OperatorGlassSurface>(),part=>part.Name=="Frame");
            Assert.True(frame.IsLoaded&&frame.IsEffectivelyVisible);Rect paint=Painted(window,frame);
            Assert.True(Painted(window,control).Contains(paint));return paint;
        }
        return Painted(window,control);
    }
    private static bool Ready(Window window, Control control)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled || !control.IsHitTestVisible || !control.IsLoaded
            || !control.GetVisualAncestors().Contains(window) || control.Bounds.Width <= 8 || control.Bounds.Height <= 8) return false;
        var first = control.TranslatePoint(default, window);
        if (first is null) return false;
        if(!new Rect(window.ClientSize).Contains(Painted(window,control)))return false;
        var box = InputPainted(window, control);
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
        for(int attempt=0;attempt<40&&!Ready(window,control);attempt++)await Settle();
        if(!Ready(window,control))
        {
            string dir=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!,"reveal-"+control.Name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(dir,"actual.png"));}
            Rect box=InputPainted(window,control);var points=new[]{box.Center,new Point(box.Left+4,box.Center.Y),new Point(box.Right-4,box.Center.Y),new Point(box.Center.X,box.Top+4),new Point(box.Center.X,box.Bottom-4)};
            string details=JsonSerializer.Serialize(new{control.Name,control.IsLoaded,control.IsEffectivelyVisible,control.IsEffectivelyEnabled,control.IsHitTestVisible,control.Bounds,Painted=box,window.ClientSize,
                Focus=(window.FocusManager?.GetFocusedElement() as Control)?.Name,
                Probes=points.Select(point=>new{Point=point,Within=Within(control,window.InputHitTest(point)),Hit=(window.InputHitTest(point) as Control)?.Name,HitType=window.InputHitTest(point)?.GetType().Name}),
                Scrolls=control.GetVisualAncestors().OfType<ScrollViewer>().Select(scroll=>new{scroll.Name,scroll.Offset,scroll.Viewport,scroll.Extent,Bounds=Painted(window,scroll)})},new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(Path.Combine(dir,"geometry.json"),details);
            Assert.Fail("Explicit public focus/reveal must reach "+control.Name+"; "+details);
        }
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
        Assert.Equal(fixture.OpenedDeliveryCount,fixture.Match.CurrentInnings!.Deliveries.Count); Assert.Equal(0, fixture.ModelChanges); Assert.Equal(0, fixture.EditorClosures);
        Assert.Equal(raw, Named<TextBox>(fixture.Entry, "RunsBox").Text);
        Assert.True(fixture.Overlay.IsVisible); Assert.Same(fixture.Entry, Named<ContentControl>(fixture.Panel, "DeliveryEntryContent").Content);
    }
    private static Button TopClose(CricketDeliveryDialog entry) => Assert.Single(
        Named<OperatorGlassSurface>(entry, "TeamHeader").GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "×"));
    private static void AssertPurpose(Control control, string expected)
    {
        Assert.Equal(expected, Avalonia.Automation.AutomationProperties.GetName(control));
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control);
        Assert.NotNull(peer); Assert.Equal(expected, peer!.GetName()); // actual Avalonia peer, not WPF UIA/Narrator proof
    }
    private static async Task ChoiceTile(DeliveryFixture f, string panel, string caption)
    {
        var tile = Assert.Single(Named<WrapPanel>(f.Entry, panel).GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, caption));
        await Reveal(f.Window, tile); await Tap(f.Window, tile);
    }
    private static void CaptureMetadata(DeliveryFixture f, string stage, string? popupDetail = null)
    {
        string? captures = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(captures)) return;
        string directory = Path.Combine(captures, "delivery-d23-" + f.CaptureToken); Directory.CreateDirectory(directory);
        using var frame = f.Window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(directory, stage + ".png"));
        File.WriteAllText(Path.Combine(directory, stage + ".json"), JsonSerializer.Serialize(new
        {
            f.Source, stage, popupDetail, f.Window.ClientSize, f.ModelChanges, f.EditorClosures,
            f.Match.DeliveryRevision, f.OpenedRevision, Deliveries = f.Match.CurrentInnings!.Deliveries.Count,
            Controls = new Control[] { Named<ComboBox>(f.Entry,"TypeChoice"), Named<ComboBox>(f.Entry,"FielderChoice"),
                Named<ComboBox>(f.Entry,"BowlerChoice"), Named<TextBox>(f.Entry,"RunsBox"), Named<Button>(f.Entry,"CustomRunsButton"), TopClose(f.Entry) }
                .Select(control => new { control.Name, control.IsEffectivelyVisible, Purpose = Avalonia.Automation.AutomationProperties.GetName(control),
                    Help = Avalonia.Automation.AutomationProperties.GetHelpText(control), Peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control)?.GetName() }).ToArray(),
            FooterLive = Avalonia.Automation.AutomationProperties.GetLiveSetting(Named<TextBlock>(f.Entry,"ErrorText")).ToString(),
            Focus = (f.Window.FocusManager?.GetFocusedElement() as Control)?.Name
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [AvaloniaTheory]
    [Trait("Scope", "DeliveryD2")]
    [InlineData("Wide", "ADDITIONAL RUNS (PENALTY IS AUTOMATIC)")]
    [InlineData("No Ball", "ADDITIONAL RUNS (PENALTY IS AUTOMATIC)")]
    [InlineData("Bye", "RUNS")] [InlineData("Leg Bye", "RUNS")]
    public async Task CurrentExtraRunsPurposeAndErrorHelpFollowActualDraftWithoutChangingTheModel(string source, string purpose)
    {
        using var f = await DeliveryFixture.OpenActual(source, 700, 450);
        var runs = Named<TextBox>(f.Entry, "RunsBox"); var custom = Named<Button>(f.Entry, "CustomRunsButton");
        await ShowCustomRuns(f, "1"); Assert.True(runs.IsEffectivelyVisible);
        CaptureMetadata(f, "initial");
        AssertPurpose(runs, purpose); AssertPurpose(custom, "Enter custom " + purpose.ToLowerInvariant());
        Assert.Equal(Named<TextBlock>(f.Entry, "RunExplanation").Text, Avalonia.Automation.AutomationProperties.GetHelpText(runs));
        await ReplaceByNativeText(f.Window, runs, "bad"); CaptureMetadata(f, "invalid");
        Assert.Equal("Enter a whole number from 0 to 998.", Avalonia.Automation.AutomationProperties.GetHelpText(runs));
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, Avalonia.Automation.AutomationProperties.GetLiveSetting(Named<TextBlock>(f.Entry, "ErrorText")));
        await Tap(f.Window, Named<Button>(f.Entry, "ApplyButton"));
        AssertUnchanged(f, f.OpenedState, f.OpenedRevision, "bad"); Assert.Same(runs, f.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(Named<TextBlock>(f.Entry,"ErrorText").Text, Avalonia.Automation.AutomationProperties.GetHelpText(runs));
        await ReplaceByNativeText(f.Window, runs, "2");
        Assert.Equal(Named<TextBlock>(f.Entry,"RunExplanation").Text, Avalonia.Automation.AutomationProperties.GetHelpText(runs));
        Assert.False(Named<TextBlock>(f.Entry,"RunsValidationText").IsVisible); AssertPurpose(runs, purpose);
        Assert.Equal(f.OpenedState, State(f.Match)); Assert.Equal(f.OpenedRevision, f.Match.DeliveryRevision); Assert.Equal(0, f.ModelChanges);
        AssertPurpose(TopClose(f.Entry), "Cancel delivery entry"); await Tap(f.Window, TopClose(f.Entry));
        Assert.False(f.Overlay.IsVisible); Assert.Equal(1, f.EditorClosures); Assert.Equal(0, f.ModelChanges); Assert.Equal(f.OpenedState, State(f.Match));
    }

    [AvaloniaFact]
    [Trait("Scope", "DeliveryD2")]
    public async Task RealRunOutSourceChangeUpdatesCompletedRunsPurposeWithoutAcceptingAWicket()
    {
        using var f = await DeliveryFixture.OpenActual("Wicket", 700, 450);
        await ChoiceTile(f, "DismissalTiles", "Run out");
        await ShowCustomRuns(f, "0");
        var runs = Named<TextBox>(f.Entry, "RunsBox");
        CaptureMetadata(f, "runout-bat"); AssertPurpose(runs, "COMPLETED RUNS BEFORE THE DISMISSAL");
        var wide = Assert.Single(Named<Control>(f.Entry,"RunOutSourceTiles").GetVisualDescendants().OfType<Button>(), button => Equals(button.Content,"WIDE"));
        await Reveal(f.Window, wide); await Tap(f.Window, wide); CaptureMetadata(f, "runout-wide");
        AssertPurpose(runs, "COMPLETED RUNS (PENALTY IS AUTOMATIC)");
        AssertPurpose(Named<Button>(f.Entry,"CustomRunsButton"), "Enter custom completed runs (penalty is automatic)");
        Assert.Equal(Named<TextBlock>(f.Entry,"RunExplanation").Text, Avalonia.Automation.AutomationProperties.GetHelpText(runs));
        Assert.Equal(f.OpenedState,State(f.Match)); Assert.Equal(0,f.ModelChanges); Assert.Equal(0,f.EditorClosures);
        await Key(f.Window,PhysicalKey.Escape); Assert.False(f.Overlay.IsVisible); Assert.Equal(1,f.EditorClosures); Assert.Equal(f.OpenedState,State(f.Match));
    }

    [AvaloniaTheory]
    [Trait("Scope", "DeliveryD2")]
    [InlineData("Correction")] [InlineData("Wicket")]
    public async Task ActualCorrectionAndOptionalFielderHaveStablePurposeAndNoncommittingClose(string route)
    {
        using var f = await DeliveryFixture.OpenActual(route,700,450);
        if(route=="Correction")
        {
            Assert.True(Named<ComboBox>(f.Entry,"TypeChoice").IsEffectivelyVisible);
            await Reveal(f.Window,Named<ComboBox>(f.Entry,"TypeChoice"));
            AssertPurpose(Named<ComboBox>(f.Entry,"TypeChoice"),"Score type");
            await Reveal(f.Window,Named<ComboBox>(f.Entry,"BowlerChoice"));
            AssertPurpose(Named<ComboBox>(f.Entry,"BowlerChoice"),"Delivery bowler");
        }
        else
        {
            await ChoiceTile(f,"DismissalTiles","Caught");
            var fielder=Named<ComboBox>(f.Entry,"FielderChoice"); Assert.True(fielder.IsEffectivelyVisible);
            await Reveal(f.Window,fielder);
            AssertPurpose(fielder,"Fielder (optional)"); Assert.Equal("Choose a fielder, or leave Not recorded.",Avalonia.Automation.AutomationProperties.GetHelpText(fielder));
            Assert.Equal(0,fielder.SelectedIndex); Assert.Equal("Not recorded",fielder.SelectedItem);
        }
        CaptureMetadata(f,"purpose"); AssertPurpose(TopClose(f.Entry),"Cancel delivery entry");
        await Tap(f.Window,TopClose(f.Entry)); Assert.False(f.Overlay.IsVisible); Assert.Equal(1,f.EditorClosures);
        Assert.Equal(f.OpenedState,State(f.Match)); Assert.Equal(f.OpenedRevision,f.Match.DeliveryRevision); Assert.Equal(0,f.ModelChanges);
    }

    private static void PopupKey(TopLevel root,PhysicalKey key)
    {root.KeyPressQwerty(key,RawInputModifiers.None);root.KeyReleaseQwerty(key,RawInputModifiers.None);}

    // D3 BASELINE PROBES, not asserted reproduced failures. Do not apply a key
    // routing patch unless actual baseline/native evidence justifies one.
    [AvaloniaTheory]
    [Trait("Scope", "DeliveryD3")]
    [InlineData("Correction","TypeChoice")]
    [InlineData("Correction","BowlerChoice")]
    [InlineData("Wicket","FielderChoice")]
    public async Task FirstActualNativeDropdownEscapeClosesOnlyThatPopupAndSecondCancelsTheEditor(string route,string choiceName)
    {
        using var f=await DeliveryFixture.OpenActual(route,700,450);
        if(route=="Wicket")await ChoiceTile(f,"DismissalTiles","Caught");
        var combo=Named<ComboBox>(f.Entry,choiceName); Assert.True(combo.IsEffectivelyVisible&&combo.IsEffectivelyEnabled);
        await Reveal(f.Window,combo); int selected=combo.SelectedIndex; string raw=Named<TextBox>(f.Entry,"RunsBox").Text??"";
        await Key(f.Window,PhysicalKey.F4); Assert.True(combo.IsDropDownOpen,"F4 must open the actual native combo before interpreting Escape.");
        var popup=Assert.IsAssignableFrom<Avalonia.Controls.Primitives.Popup>(NativeTemplates.FindObject(combo,"PART_Popup"));
        Assert.True(popup.IsOpen); var child=Assert.IsAssignableFrom<Control>(popup.Child);
        await Until(()=>child.IsLoaded&&child.IsEffectivelyVisible&&TopLevel.GetTopLevel(child) is not null,"Actual dropdown content must attach/load at its real input root.");
        var inputRoot=Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(child));
        using(var frame=inputRoot.CaptureRenderedFrame())Assert.NotNull(frame);
        Assert.True(Within(child,inputRoot.FocusManager?.GetFocusedElement())||Within(combo,f.Window.FocusManager?.GetFocusedElement()),
            "Require actual focus owned by this popup/its combo, never a manufactured popup focus.");
        CaptureMetadata(f,"dropdown-open",choiceName+" root="+inputRoot.GetType().Name);
        PopupKey(inputRoot,PhysicalKey.Escape);await Settle();CaptureMetadata(f,"first-escape",choiceName);
        Assert.False(combo.IsDropDownOpen);Assert.False(popup.IsOpen);Assert.True(f.Overlay.IsVisible);Assert.Equal(0,f.EditorClosures);
        Assert.Same(f.Entry,Named<ContentControl>(f.Panel,"DeliveryEntryContent").Content);
        Assert.Equal(selected,combo.SelectedIndex);Assert.Equal(raw,Named<TextBox>(f.Entry,"RunsBox").Text);
        Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(f.OpenedRevision,f.Match.DeliveryRevision);Assert.Equal(0,f.ModelChanges);
        Assert.True(Within(combo,f.Window.FocusManager?.GetFocusedElement()),"Native popup Escape should restore current combo keyboard ownership.");
        await Key(f.Window,PhysicalKey.Escape);Assert.False(f.Overlay.IsVisible);Assert.Equal(1,f.EditorClosures);
        Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(f.OpenedRevision,f.Match.DeliveryRevision);Assert.Equal(0,f.ModelChanges);
    }

    private sealed class DeliveryFixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool oldSelfTest = ((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldMotion = OperatorMotion.AnimationOverride;
        private readonly AppTheme oldTheme = ((App)Application.Current!).ThemeService.CurrentTheme;
        private readonly UiDensity oldDensity = ((App)Application.Current!).ThemeService.CurrentDensity;
        private readonly string? oldHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string dataHome = Path.Combine(Path.GetTempPath(), "roche-delivery-d23-" + Guid.NewGuid().ToString("N"));
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
        internal int OpenedDeliveryCount { get; private set; }
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
                // Public model fixture setup only; correction is entered through a real history row key below.
                if (source == "Correction") { f.Match.RecordDelivery(0,CricketDeliveryType.Dot); Assert.Single(f.Match.CurrentInnings.Deliveries); }
                f.OpenedState = State(f.Match); f.OpenedRevision = f.Match.DeliveryRevision;
                f.OpenedDeliveryCount = f.Match.CurrentInnings.Deliveries.Count;
                f.panel = new CricketControlPanel(); f.Panel.SetMatch(f.Match);
                f.window = new NativeWindow { Width = width, Height = height, Content = f.Panel }; f.Window.Show();
                await Settle(600);
                Assert.True(Named<Border>(f.Panel, "ScoringCommands").IsEffectivelyEnabled);
                Assert.True(Named<Border>(f.Panel, "ExtraCommands").IsEffectivelyEnabled);
                f.overlay = Named<Border>(f.Panel, "DeliveryEntryOverlay"); Assert.False(f.Overlay.IsVisible);
                f.Match.MatchChanged += f.Changed; f.Overlay.PropertyChanged += f.OverlayChanged; f.subscribed = true;
                if (source == "Correction")
                {
                    var delivery = Assert.Single(f.Match.CurrentInnings.Deliveries);
                    var row = Assert.Single(Named<StackPanel>(f.Panel,"EventLog").GetVisualDescendants().OfType<Border>(), border => ReferenceEquals(border.Tag,delivery));
                    await Reveal(f.Window,row); await Key(f.Window,PhysicalKey.Enter);
                }
                else
                {
                    var group = Named<Border>(f.Panel,source=="Wicket"?"ScoringCommands":"ExtraCommands");
                    var action = Assert.Single(group.GetVisualDescendants().OfType<Button>(), button => source=="Wicket" ? Equals(ToolTip.GetTip(button),"Wicket") : Equals(button.Content,source));
                    await Reveal(f.Window,action); await Tap(f.Window,action);
                }
                await Until(() => f.Overlay.IsEffectivelyVisible && f.Overlay.GetVisualDescendants().OfType<CricketDeliveryDialog>().Any(), "Actual scoring source must embed a current delivery editor.");
                f.Entry = Assert.Single(f.Overlay.GetVisualDescendants().OfType<CricketDeliveryDialog>());
                Assert.Same(f.Entry, Named<ContentControl>(f.Panel, "DeliveryEntryContent").Content);
                Assert.True(Named<Button>(f.Entry, "ApplyButton").IsEffectivelyEnabled);
                Assert.Equal(0, f.ModelChanges); Assert.Equal(0, f.EditorClosures); Assert.Equal(f.OpenedDeliveryCount,f.Match.CurrentInnings.Deliveries.Count);
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
            if (errors.Count > 0) throw new AggregateException("Delivery D23 fixture cleanup failed", errors);
        }
    }
}
