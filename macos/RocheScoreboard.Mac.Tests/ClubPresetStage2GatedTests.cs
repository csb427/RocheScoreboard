// Public-path regressions in the existing nonparallel club-preset collection.
// Every accepted write delegates to the captured real AtomicFile backend.
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

[Collection("Club preset persistence draft")]
public sealed partial class ClubPresetStage2GatedTests
{
    private const TeamLibrarySport Afl = TeamLibrarySport.AustralianFootball;
    private const TeamLibrarySport Cricket = TeamLibrarySport.Cricket;
    private static string Json(IEnumerable<TeamPreset> value) => JsonSerializer.Serialize(value);
    private static TeamPreset Other(string name = "Unrelated retained club") => new()
    { PresetName = name, TeamName = name, Abbreviation = "OTH", PrimaryColor = "#123456", SecondaryColor = "#FEDCBA" };
    private static T Named<T>(Control owner, string name) where T : Control =>
        Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));
    private static List<TeamPreset> Tiles(Control wizard) => Named<WrapPanel>(wizard, "HomePresetPanel")
        .Children.OfType<Border>().Select(tile => Assert.IsType<TeamPreset>(tile.Tag)).ToList();
    private static Button[] PresetActions(Control wizard) => Named<Border>(wizard, "HomeTeamCard")
        .GetVisualDescendants().OfType<Button>()
        .Where(button => button.Content is string text && (text == "Save as preset" || text == "Delete preset")).ToArray();
    private static async Task Until(Func<bool> predicate, string failure, Func<string>? details = null)
    {
        for (int attempt = 0; attempt < 160; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(25);
        }
        Assert.True(predicate(), failure + (details?.Invoke() ?? ""));
    }
    private static async Task<Point> PointerTarget(Window owner, Control target)
    {
        target.BringIntoView();
        await Until(() => target.IsEffectivelyVisible && target.IsEnabled && target.Bounds.Width > 0 && target.Bounds.Height > 0,
            "Actual favourite action must be visible and enabled before pointer input.");
        Point? origin = target.TranslatePoint(default, owner);
        Assert.NotNull(origin);
        Assert.True(new Rect(owner.ClientSize).Contains(new Rect(origin!.Value, target.Bounds.Size)),
            "Whole favourite target must fit its actual viewport.");
        Point center = origin.Value + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
        var hit = owner.InputHitTest(center);
        if (!(ReferenceEquals(hit, target) || hit is Visual actualChild && actualChild.GetVisualAncestors().Contains(target)))
            CapturePointerMiss(owner, target, origin.Value, center, hit);
        Assert.True(ReferenceEquals(hit, target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
            "Pointer must hit the real favourite control/template.");
        return center;
    }
    private static void CapturePointerMiss(Window owner, Control target, Point origin, Point center, object? hit)
    {
        // Observe the failing state before attempting a frame. No layout update,
        // dispatcher pumping, wait, retry, focus change or alternate hit point.
        try
        {
            object RectData(Rect value) => new { x = value.X, y = value.Y, width = value.Width, height = value.Height };
            object? PointData(Point? value) => value is Point point ? new { x = point.X, y = point.Y } : null;
            Point[] localCorners = [new(0, 0), new(target.Bounds.Width, 0),
                new(target.Bounds.Width, target.Bounds.Height), new(0, target.Bounds.Height)];
            object Describe(Visual visual)
            {
                Control? control = visual as Control;
                Point?[] corners = localCorners.Select(point => target.TranslatePoint(point, visual)).ToArray();
                Rect localBounds = new(visual.Bounds.Size);
                ScrollViewer? scroll = control as ScrollViewer;
                return new
                {
                    name = control?.Name,
                    type = visual.GetType().FullName,
                    bounds = RectData(visual.Bounds),
                    desiredSize = control?.DesiredSize.ToString(),
                    width = control?.Width, height = control?.Height,
                    minWidth = control?.MinWidth, minHeight = control?.MinHeight,
                    maxWidth = control?.MaxWidth, maxHeight = control?.MaxHeight,
                    margin = control?.Margin.ToString(),
                    isLoaded = control?.IsLoaded,
                    isVisible = control?.IsVisible,
                    isEffectivelyVisible = control?.IsEffectivelyVisible,
                    isEnabled = control?.IsEnabled,
                    isHitTestVisible = control?.IsHitTestVisible,
                    opacity = control?.Opacity,
                    clipToBounds = control?.ClipToBounds,
                    clipType = control?.Clip?.GetType().FullName,
                    clip = control?.Clip?.ToString(),
                    clipBounds = control?.Clip is { } clip ? RectData(clip.Bounds) : null,
                    renderTransformType = control?.RenderTransform?.GetType().FullName,
                    renderTransform = control?.RenderTransform?.Value.ToString(),
                    renderTransformOrigin = control?.RenderTransformOrigin.ToString(),
                    transformToOwner = visual.TransformToVisual(owner)?.ToString(),
                    targetCornersInThisVisual = corners.Select(PointData).ToArray(),
                    allTargetCornersInsideThisLocalBounds = corners.All(point => point is Point p && localBounds.Contains(p)),
                    targetComputedCenterInThisVisual = PointData(target.TranslatePoint(
                        new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), visual)),
                    scrollOffset = scroll?.Offset.ToString(),
                    scrollExtent = scroll?.Extent.ToString(),
                    scrollViewport = scroll?.Viewport.ToString(),
                    horizontalScrollBarVisibility = scroll?.HorizontalScrollBarVisibility.ToString(),
                    verticalScrollBarVisibility = scroll?.VerticalScrollBarVisibility.ToString()
                };
            }
            string directory = Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "roche-club-pointer-diagnostics");
            Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, "club-pointer-miss-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")
                + "-" + Guid.NewGuid().ToString("N"));
            var snapshot = new
            {
                capturedUtc = DateTime.UtcNow.ToString("O"),
                captureOrder = "Geometry and cached hit recorded before optional rendered frame; original assertion follows unchanged.",
                owner = new
                {
                    owner.Name, owner.Title, type = owner.GetType().FullName,
                    owner.Width, owner.Height, owner.MinWidth, owner.MinHeight, owner.MaxWidth, owner.MaxHeight,
                    bounds = RectData(owner.Bounds),
                    clientSize = owner.ClientSize.ToString(), clientBounds = RectData(new Rect(owner.ClientSize)),
                    frameSize = owner.FrameSize?.ToString(), position = owner.Position.ToString(),
                    owner.RenderScaling, owner.WindowState, owner.IsLoaded, owner.IsVisible, owner.IsEffectivelyVisible,
                    owner.IsActive, owner.IsEnabled, owner.IsHitTestVisible
                },
                originUsedByExistingAssertion = PointData(origin),
                axisAlignedRectangleUsedByExistingAssertion = RectData(new Rect(origin, target.Bounds.Size)),
                centerUsedForInputHitTest = PointData(center),
                transformedActualCenter = PointData(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), owner)),
                transformedFullCorners = localCorners.Select(point => PointData(target.TranslatePoint(point, owner))).ToArray(),
                target = Describe(target),
                targetAncestors = target.GetVisualAncestors().Select(Describe).ToArray(),
                hitName = (hit as Control)?.Name, hitType = hit?.GetType().FullName,
                hit = hit is Visual hitVisual ? Describe(hitVisual) : null,
                hitAncestors = hit is Visual hitAncestor ? hitAncestor.GetVisualAncestors().Select(Describe).ToArray() : [],
                viewportClipNote = "Ancestor local bounds/corners, ClipToBounds/Clip and ScrollViewer viewport/extent/offset are observations; clip-bounds containment is not exact rounded-geometry fill testing."
            };
            File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            }));
            Console.WriteLine("Club preset pointer diagnostic: " + prefix + ".json");
            try
            {
                using var frame = owner.CaptureRenderedFrame();
                if (frame is not null) frame.Save(prefix + ".png");
            }
            catch (Exception frameError) { File.WriteAllText(prefix + "-frame-error.txt", frameError.ToString()); }
        }
        catch (Exception diagnosticError)
        {
            Console.WriteLine("Club preset pointer diagnostics failed without replacing the original assertion: " + diagnosticError);
        }
    }
    private static void SendPointer(Window owner, Point center)
    {
        owner.MouseMove(center); owner.MouseDown(center, MouseButton.Left); owner.MouseUp(center, MouseButton.Left);
        // Intentionally no await: capture AcceptedPresetChanges immediately after
        // real pointer activation, before the gated async handler can finish.
    }
    private static TeamCatalogEntry Team(TeamLibrarySport sport, int index = 0) =>
        TeamCatalogService.GetTeams(sport).Where(team => sport == Afl || !string.IsNullOrWhiteSpace(team.BundledLogo)).ElementAt(index);
    private static async Task SelectReady(TeamLibraryWindow library, TeamCatalogEntry team, bool favouriteMustBeEnabled = true)
    {
        var list = Named<ListBox>(library, "TeamList");
        var row = Assert.Single(list.Items.Cast<TeamCatalogEntry>(), item => item.Id == team.Id);
        list.SelectedItem = row; // Public selection-routing coverage; not a pointer-selection claim.
        await Until(() => Named<Button>(library, "UseButton").IsEnabled && Named<Image>(library, "LogoPreview").Source is not null
            && Named<TextBlock>(library, "SelectedName").Text == team.TeamName
            && (!favouriteMustBeEnabled || Named<Button>(library, "FavouriteButton").IsEnabled),
            "Public list selection must load its real offline logo and enable the intended action.");
        Assert.Equal(team.Id, Assert.IsType<TeamCatalogEntry>(list.SelectedItem).Id);
    }
    private static async Task<TeamLibraryWindow> ReadyLibrary(StoreFixture fixture, TeamLibrarySport sport, TeamCatalogEntry team)
    {
        await fixture.OfflineLogo(team, sport);
        var library = fixture.Track(new TeamLibraryWindow("Home", sport));
        library.Show();
        Named<ComboBox>(library, "LeagueFilter").SelectedIndex = 0;
        Named<ComboBox>(library, "StateFilter").SelectedIndex = 0;
        Named<ComboBox>(library, "SortFilter").SelectedIndex = 0;
        Named<CheckBox>(library, "FavouriteOnly").IsChecked = false;
        await SelectReady(library, team);
        return library;
    }
    private static async Task<(GateIo Gate, Task<PresetCommitResult[]> Accepted)> StartFavourite(StoreFixture fixture, TeamLibraryWindow library)
    {
        Point point = await PointerTarget(library, Named<Button>(library, "FavouriteButton"));
        GateIo gate = fixture.GateWrites(); // Only after initial view/membership/logo readiness.
        SendPointer(library, point);
        Task<PresetCommitResult[]> accepted = fixture.Watch(library.AcceptedPresetChanges);
        Assert.False(accepted.IsCompleted, "Real pointer action must register accepted work before its first await.");
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(1, gate.WriteCalls);
        Assert.False(accepted.IsCompleted);
        return (gate, accepted);
    }

    [AvaloniaFact]
    public async Task PendingFavouriteForADoesNotReselectAOrReplaceBLogoOrReadyStatus()
    {
        await using var fixture = await StoreFixture.Create();
        await fixture.Seed(Cricket, [Other()]);
        TeamCatalogEntry a = Team(Cricket), b = Team(Cricket, 1);
        var library = await ReadyLibrary(fixture, Cricket, a);
        await fixture.OfflineLogo(b, Cricket);
        var (gate, accepted) = await StartFavourite(fixture, library);
        Assert.False(Equals(Named<Button>(library, "FavouriteButton").Tag, true));
        await SelectReady(library, b, favouriteMustBeEnabled: false);
        object? bLogo = Named<Image>(library, "LogoPreview").Source;
        Assert.Equal("Ready to select", Named<TextBlock>(library, "StatusText").Text);
        gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted).Status);
        await Until(() => Named<Button>(library, "FavouriteButton").IsEnabled, "B action must leave pending state.");
        PresetReadResult disk = await fixture.Read(Cricket);
        Assert.Contains(disk.Presets, preset => preset.LibraryTeamId == a.Id);
        Assert.DoesNotContain(disk.Presets, preset => preset.LibraryTeamId == b.Id);
        Assert.Contains(disk.Presets, preset => preset.PresetName == "Unrelated retained club");
        Assert.Equal(b.Id, Assert.IsType<TeamCatalogEntry>(Named<ListBox>(library, "TeamList").SelectedItem).Id);
        Assert.Same(bLogo, Named<Image>(library, "LogoPreview").Source);
        Assert.Equal(b.TeamName, Named<TextBlock>(library, "SelectedName").Text);
        Assert.Equal("Ready to select", Named<TextBlock>(library, "StatusText").Text);
        Assert.False(Equals(Named<Button>(library, "FavouriteButton").Tag, true));
        Assert.True(Named<TextBlock>(library, "PresetStatus").IsVisible);
        Assert.Contains(a.TeamName, Named<TextBlock>(library, "PresetStatus").Text ?? "");
    }

    [AvaloniaFact]
    public async Task PublicCloseCannotCancelAcceptedFavouriteAndCompletionDoesNotMutateClosedUi()
    {
        await using var fixture = await StoreFixture.Create();
        await fixture.Seed(Cricket, [Other()]);
        TeamCatalogEntry a = Team(Cricket);
        var library = await ReadyLibrary(fixture, Cricket, a);
        var (gate, accepted) = await StartFavourite(fixture, library);
        library.Close(); Dispatcher.UIThread.RunJobs();
        Assert.False(library.IsVisible);
        Assert.False(accepted.IsCompleted);
        var status = Named<TextBlock>(library, "StatusText");
        var membership = Named<TextBlock>(library, "PresetStatus");
        var favourite = Named<Button>(library, "FavouriteButton");
        var logo = Named<Image>(library, "LogoPreview");
        var before = (status.Text, membership.Text, favourite.IsEnabled, favourite.Content, favourite.Tag, logo.Source);
        int uiMutations = 0;
        void Observe(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property.Name is "Text" or "Source" or "IsEnabled" or "Content" or "Tag") uiMutations++; }
        foreach (Control target in new Control[] { status, membership, favourite, logo }) target.PropertyChanged += Observe;
        try
        {
            gate.Release();
            Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted).Status);
            PresetReadResult disk = await fixture.Read(Cricket);
            await Until(() => library.PresetsChanged, "Closed library must observe its actual completed storage receipt before asserting no UI publication.");
            Assert.Contains(disk.Presets, preset => preset.LibraryTeamId == a.Id);
            Assert.Contains(disk.Presets, preset => preset.PresetName == "Unrelated retained club");
            Assert.Equal(before, (status.Text, membership.Text, favourite.IsEnabled, favourite.Content, favourite.Tag, logo.Source));
            Assert.Equal(0, uiMutations);
            Assert.True(library.PresetsChanged); // Storage receipt may update; closed UI may not.
        }
        finally { foreach (Control target in new Control[] { status, membership, favourite, logo }) target.PropertyChanged -= Observe; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewlyMountedWizardReadsThroughAnEarlierClosedLibraryCommit(bool cricket)
    {
        await using var fixture = await StoreFixture.Create();
        TeamLibrarySport sport = cricket ? Cricket : Afl;
        await fixture.Seed(sport, [Other()]);
        TeamCatalogEntry a = Team(sport);
        var library = await ReadyLibrary(fixture, sport, a);
        var (gate, accepted) = await StartFavourite(fixture, library);
        library.Close();
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        await Until(() => wizard.IsLoaded && Named<WrapPanel>(wizard, "HomePresetPanel").IsEffectivelyVisible,
            "New wizard must mount without blocking on the gated accepted write.");
        Assert.DoesNotContain(Tiles(wizard), preset => preset.LibraryTeamId == a.Id);
        Assert.Contains(Tiles(wizard), preset => preset.PresetName == "Unrelated retained club");
        Button[] actions = PresetActions(wizard); Assert.NotEmpty(actions); Assert.All(actions, action => Assert.False(action.IsEnabled));
        Assert.False(accepted.IsCompleted);
        gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted).Status);
        await Until(() => Tiles(wizard).Any(preset => preset.LibraryTeamId == a.Id) && PresetActions(wizard).All(action => action.IsEnabled),
            "Mounted wizard must publish confirmed data after its queued read passes the earlier accepted commit.");
        Assert.Equal(Json((await fixture.Read(sport)).Presets), Json(Tiles(wizard)));
        Assert.Contains(Tiles(wizard), preset => preset.PresetName == "Unrelated retained club");
        Assert.Equal("", Named<TextBox>(wizard, "SetupHomeNameBox").Text); // Preset refresh must not select a team.
    }

    [AvaloniaFact]
    public async Task HiddenLibraryObservesAcceptedCommitWhenPubliclyShownAgain()
    {
        await using var fixture = await StoreFixture.Create();
        await fixture.Seed(Cricket, [Other()]);
        TeamCatalogEntry a = Team(Cricket);
        var library = await ReadyLibrary(fixture, Cricket, a);
        int loaded = 0, unloaded = 0;
        library.Loaded += (_, _) => loaded++; library.Unloaded += (_, _) => unloaded++;
        var (gate, accepted) = await StartFavourite(fixture, library);
        library.Hide(); Dispatcher.UIThread.RunJobs(); Assert.False(library.IsVisible);
        gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted).Status);
        Assert.Contains((await fixture.Read(Cricket)).Presets, preset => preset.LibraryTeamId == a.Id);
        await Until(() => library.PresetsChanged, "Accepted completion must be observed while the library is still hidden.");
        library.Show();
        await Until(() => Named<Button>(library, "FavouriteButton").IsEnabled && Equals(Named<Button>(library, "FavouriteButton").Tag, true),
            "Public Hide/Show must refresh confirmed membership without requiring synthetic Loaded events.",
            () => $" Actual extra Loaded={loaded}, Unloaded={unloaded}.");
        Assert.True(library.IsVisible, $"Actual extra Loaded={loaded}, Unloaded={unloaded}.");
        Assert.Equal(a.Id, Assert.IsType<TeamCatalogEntry>(Named<ListBox>(library, "TeamList").SelectedItem).Id);
        Assert.True(Assert.Single(Named<ListBox>(library, "TeamList").Items.Cast<TeamCatalogEntry>(), item => item.Id == a.Id).IsFavourite);
        Assert.True(Named<Button>(library, "UseButton").IsEnabled);
    }

    [AvaloniaFact]
    public async Task AlreadyMountedWizardResetWhileHiddenReadsPendingCommitAfterVisibilityReturns()
    {
        await using var fixture = await StoreFixture.Create();
        await fixture.Seed(Afl, [Other()]);
        var wizard = new SetupWizard();
        int loaded = 0, unloaded = 0;
        wizard.Loaded += (_, _) => loaded++; wizard.Unloaded += (_, _) => unloaded++;
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        await Until(() => Tiles(wizard).Count == 1 && PresetActions(wizard).Length > 0 && PresetActions(wizard).All(action => action.IsEnabled),
            "Original mounted wizard must finish its initial confirmed read.");
        TeamCatalogEntry a = Team(Afl);
        var library = await ReadyLibrary(fixture, Afl, a);
        var (gate, accepted) = await StartFavourite(fixture, library);
        wizard.IsVisible = false; Dispatcher.UIThread.RunJobs();
        wizard.Reset(); // Actual public hidden reset, not a synthetic Loaded event.
        Assert.False(wizard.IsVisible);
        Assert.DoesNotContain(Tiles(wizard), preset => preset.LibraryTeamId == a.Id);
        gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted).Status);
        Assert.Contains((await fixture.Read(Afl)).Presets, preset => preset.LibraryTeamId == a.Id);
        await Until(() => library.PresetsChanged, "Old library must observe its accepted receipt while the mounted wizard is hidden.");
        library.Close();
        wizard.IsVisible = true;
        await Until(() => Tiles(wizard).Any(preset => preset.LibraryTeamId == a.Id) && PresetActions(wizard).All(action => action.IsEnabled),
            "Visible mounted wizard must finish hidden Reset's pending snapshot without requiring synthetic Loaded events.",
            () => $" Actual Loaded={loaded}, Unloaded={unloaded}.");
        Assert.True(wizard.IsEffectivelyVisible, $"Actual Loaded={loaded}, Unloaded={unloaded}.");
        Assert.Equal(Json((await fixture.Read(Afl)).Presets), Json(Tiles(wizard)));
        Assert.Equal("", Named<TextBox>(wizard, "SetupHomeNameBox").Text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WizardStaleDeleteRefreshesConfirmedTilesWithoutOverwritingItsUnsavedTeamAndCanRetry(bool cricket)
    {
        await using var fixture = await StoreFixture.Create();
        TeamLibrarySport sport = cricket ? Cricket : Afl;
        TeamPreset target = Other("Selected saved club"); target.TeamName = "Originally saved team"; target.Abbreviation = "OLD";
        await fixture.Seed(sport, [target, Other()]);
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        await Until(() => wizard.IsLoaded && Tiles(wizard).Count == 2 && PresetActions(wizard).Length == 2
            && PresetActions(wizard).All(action => action.IsEnabled), "Wizard must finish its original confirmed preset read.");
        Border TargetTile() => Assert.Single(Named<WrapPanel>(wizard, "HomePresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == target.PresetName);
        Button DeleteAction() => Assert.Single(PresetActions(wizard), action => Equals(action.Content, "Delete preset"));
        TextBlock LocalStatus() => Assert.Single(Named<Border>(wizard, "HomeTeamCard").GetVisualDescendants().OfType<TextBlock>(),
            text => Equals(text.Tag, "team-preset-status:Home"));
        var name = Named<TextBox>(wizard, "SetupHomeNameBox");
        var abbreviation = Named<TextBox>(wizard, "SetupHomeAbbrBox");
        var primary = Named<TextBox>(wizard, "SetupHomeColorBox");
        var secondary = Named<TextBox>(wizard, "SetupHomeSecondaryBox");
        SendPointer(window, await PointerTarget(window, TargetTile()));
        await Until(() => name.Text == "Originally saved team"
            && Avalonia.Automation.AutomationProperties.GetItemStatus(TargetTile()) == "Selected",
            "Actual saved-tile pointer route must select the captured old record before the independent edit.");
        name.Text = "Unsaved team draft"; abbreviation.Text = "DRFT";
        primary.Text = "#445566"; secondary.Text = "#FFEEDD";
        var draft = (name.Text, abbreviation.Text, primary.Text, secondary.Text);

        // Keep this mounted wizard's selected basis stale while another actual
        // typed transaction commits a newer same-name record to the real store.
        GateIo gate = fixture.GateWrites();
        TeamPreset edited = PresetStorage.Clone(target); edited.TeamName = "Newer persisted team";
        edited.Abbreviation = "NEW"; edited.PrimaryColor = "#ABCDEF"; edited.SecondaryColor = "#0A1B2C";
        Task<PresetCommitResult> edit = fixture.Watch(PresetStorage.SavePresetAsync(sport, edited, false));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.False(edit.IsCompleted); gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, (await edit).Status);
        PresetReadResult newest = await fixture.Read(sport);
        byte[] newestBytes = File.ReadAllBytes(fixture.PathFor(sport));
        Assert.Equal("Newer persisted team", Assert.Single(newest.Presets, preset => preset.PresetName == target.PresetName).TeamName);
        Assert.Equal("OLD", Assert.IsType<TeamPreset>(TargetTile().Tag).Abbreviation);
        Assert.Equal("Selected", Avalonia.Automation.AutomationProperties.GetItemStatus(TargetTile()));
        Assert.Equal(draft, (name.Text, abbreviation.Text, primary.Text, secondary.Text));
        Assert.Equal(1, gate.WriteCalls);

        SendPointer(window, await PointerTarget(window, DeleteAction()));
        PresetReadResult afterConflict = await fixture.Read(sport); // Drains the actual pointer-submitted deletion.
        await Until(() => LocalStatus().IsEffectivelyVisible && (LocalStatus().Text ?? "").Contains(
            "changed before it could be removed", StringComparison.OrdinalIgnoreCase) && DeleteAction().IsEnabled,
            "The public stale-delete action must finish with a local conflict explanation and usable retry.");
        string? conflict = LocalStatus().Text;
        Assert.Equal(newest.Revision, afterConflict.Revision);
        Assert.Equal(Json(newest.Presets), Json(afterConflict.Presets));
        Assert.Equal(newestBytes, File.ReadAllBytes(fixture.PathFor(sport)));
        Assert.Equal(1, gate.WriteCalls); // Conflict is not a file write or a hidden deletion.
        Assert.Equal(draft, (name.Text, abbreviation.Text, primary.Text, secondary.Text));
        await Until(() => Tiles(wizard).Any(preset => preset.PresetName == target.PresetName
            && preset.TeamName == "Newer persisted team" && preset.Abbreviation == "NEW"
            && preset.PrimaryColor == "#ABCDEF" && preset.SecondaryColor == "#0A1B2C"),
            "Conflict must reconcile the confirmed tile snapshot; keeping stale tiles traps the next Delete in the same conflict.");
        Assert.Equal(Json(newest.Presets), Json(Tiles(wizard)));
        Assert.Equal("Selected", Avalonia.Automation.AutomationProperties.GetItemStatus(TargetTile()));
        Assert.Equal(draft, (name.Text, abbreviation.Text, primary.Text, secondary.Text));
        await fixture.Read(sport); Dispatcher.UIThread.RunJobs();
        Assert.Equal(conflict, LocalStatus().Text); // Publishing the snapshot must not erase the explanation.
        Assert.True(LocalStatus().IsEffectivelyVisible);
        Assert.Equal(newestBytes, File.ReadAllBytes(fixture.PathFor(sport)));

        // A second explicit user activation must use the refreshed selected
        // record without re-clicking its tile (which would overwrite the draft).
        SendPointer(window, await PointerTarget(window, DeleteAction()));
        PresetReadResult retried = await fixture.Read(sport);
        await Until(() => Tiles(wizard).All(preset => preset.PresetName != target.PresetName) && DeleteAction().IsEnabled,
            "Explicit retry must delete the now-current selected record rather than repeat the stale-basis conflict.");
        Assert.Equal("Unrelated retained club", Assert.Single(retried.Presets).PresetName);
        Assert.Equal(Json(retried.Presets), Json(Tiles(wizard)));
        Assert.True(retried.Revision > newest.Revision);
        Assert.Equal(2, gate.WriteCalls);
        Assert.Equal(draft, (name.Text, abbreviation.Text, primary.Text, secondary.Text));
        Assert.NotEqual(conflict, LocalStatus().Text);
    }

    [AvaloniaFact]
    public async Task DeleteUsesExactCurrentDuplicateIndexAndRejectsStaleDuplicatesAndNewerSameNameEdits()
    {
        await using var fixture = await StoreFixture.Create();
        var duplicate = Other("Duplicate club");
        await fixture.Seed(Afl, [duplicate, Other(), PresetStorage.Clone(duplicate)]);
        PresetReadResult basis = await fixture.Read(Afl);
        GateIo gate = fixture.GateWrites();
        Task<PresetCommitResult> append = fixture.Watch(PresetStorage.SavePresetAsync(Afl, Other("Later unrelated club"), false));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Task<PresetCommitResult> staleDuplicateDelete = fixture.Watch(PresetStorage.DeletePresetAsync(Afl, basis.Presets, 2, basis.Revision));
        Assert.False(staleDuplicateDelete.IsCompleted);
        gate.Release();
        Assert.Equal(PresetCommitStatus.Committed, (await append).Status);
        Assert.Equal(PresetCommitStatus.Conflict, (await staleDuplicateDelete).Status);
        Assert.Equal(1, gate.WriteCalls); // Rejected delete must not write.
        PresetReadResult fresh = await fixture.Read(Afl);
        Assert.Equal(new[] { "Duplicate club", "Unrelated retained club", "Duplicate club", "Later unrelated club" }, fresh.Presets.Select(preset => preset.PresetName));
        PresetCommitResult exact = await fixture.Watch(PresetStorage.DeletePresetAsync(Afl, fresh.Presets, 2, fresh.Revision));
        Assert.Equal(PresetCommitStatus.Committed, exact.Status);
        Assert.Equal(new[] { "Duplicate club", "Unrelated retained club", "Later unrelated club" }, (await fixture.Read(Afl)).Presets.Select(preset => preset.PresetName));

        await fixture.Seed(Cricket, [Other("Same name club"), Other()]);
        PresetReadResult older = await fixture.Read(Cricket);
        TeamPreset edited = PresetStorage.Clone(older.Presets[0]); edited.TeamName = "Newer edited team"; edited.Abbreviation = "NEW"; edited.PrimaryColor = "#ABCDEF";
        Assert.Equal(PresetCommitStatus.Committed, (await fixture.Watch(PresetStorage.SavePresetAsync(Cricket, edited, false))).Status);
        PresetReadResult newer = await fixture.Read(Cricket);
        byte[] beforeRefusal = File.ReadAllBytes(fixture.PathFor(Cricket));
        PresetCommitResult refused = await fixture.Watch(PresetStorage.DeletePresetAsync(Cricket, older.Presets, 0, older.Revision));
        Assert.Equal(PresetCommitStatus.Conflict, refused.Status);
        Assert.Equal(newer.Revision, refused.Snapshot.Revision);
        Assert.Equal(Json(newer.Presets), Json((await fixture.Read(Cricket)).Presets));
        Assert.Equal(beforeRefusal, File.ReadAllBytes(fixture.PathFor(Cricket)));
        Assert.Equal("Newer edited team", Assert.Single(refused.Snapshot.Presets, preset => preset.PresetName == "Same name club").TeamName);
    }

    // Append inside ClubPresetStage2GatedTests. Outside-repository draft only;
    // existing fixture/helpers and frozen PointerTarget diagnostics are reused.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearingNameToNullDuringAcceptedSaveKeepsCapturedClubAndEmptyDraftRetryable(bool cricket)
    {
        StoreFixture fixture = await StoreFixture.Create();
        TeamLibrarySport sport = cricket ? Cricket : Afl;
        GateIo? gate = null;
        Exception? uiFailure = null;
        void ObserveUiFailure(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            uiFailure ??= args.Exception;
            // Keep the runner alive long enough to drain accepted IO and fail
            // explicitly below. Handling the event never makes the test pass.
            args.Handled = true;
        }
        Dispatcher.UIThread.UnhandledException += ObserveUiFailure;
        try
        {
            await fixture.Seed(sport, [Other()]);
            Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
            var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
            window.Show();
            await Until(() => wizard.IsLoaded && Tiles(wizard).Count == 1 && PresetActions(wizard).Length == 2
                && PresetActions(wizard).All(action => action.IsEnabled),
                "Wizard must finish its original confirmed read before the real Save action.");
            Button SaveAction() => Assert.Single(PresetActions(wizard), action => Equals(action.Content, "Save as preset"));
            TextBlock LocalStatus() => Assert.Single(Named<Border>(wizard, "HomeTeamCard").GetVisualDescendants().OfType<TextBlock>(),
                text => Equals(text.Tag, "team-preset-status:Home"));
            var name = Named<TextBox>(wizard, "SetupHomeNameBox");
            var abbreviation = Named<TextBox>(wizard, "SetupHomeAbbrBox");
            var primary = Named<TextBox>(wizard, "SetupHomeColorBox");
            var secondary = Named<TextBox>(wizard, "SetupHomeSecondaryBox");
            const string capturedName = "Club captured before name clear";
            name.Text = capturedName; abbreviation.Text = "CAPT";
            primary.Text = "#224466"; secondary.Text = "#EECCAA";
            Point savePoint = await PointerTarget(window, SaveAction());
            gate = fixture.GateWrites(); // Initial read/layout/physical target are positively ready first.
            SendPointer(window, savePoint);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.Equal(1, gate.WriteCalls);
            Assert.All(PresetActions(wizard), action => Assert.False(action.IsEnabled));
            Assert.DoesNotContain(Tiles(wizard), preset => preset.PresetName == capturedName);
            Task<PresetReadResult> afterAcceptedWrite = fixture.Read(sport); // Public queue barrier, not a private handler/Task.
            Assert.False(afterAcceptedWrite.IsCompleted);

            name.Text = null;
            Assert.Null(name.Text); // Establish the nullable Text precondition, not just an empty-looking control.
            Assert.Equal("CAPT", abbreviation.Text);
            gate.Release();
            PresetReadResult persisted = await afterAcceptedWrite.WaitAsync(TimeSpan.FromSeconds(4));
            await Until(() => uiFailure is not null || (PresetActions(wizard).All(action => action.IsEnabled)
                && (LocalStatus().Text ?? "").Contains("saved as a preset", StringComparison.OrdinalIgnoreCase)),
                "Accepted Save must complete its UI continuation after the current name has become null.");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(uiFailure); // Baseline null dereference must fail here, never be swallowed as success.
            TeamPreset captured = Assert.Single(persisted.Presets, preset => preset.PresetName == capturedName);
            Assert.Equal(capturedName, captured.TeamName); Assert.Equal("CAPT", captured.Abbreviation);
            Assert.Equal("#224466", captured.PrimaryColor); Assert.Equal("#EECCAA", captured.SecondaryColor);
            Assert.Contains(persisted.Presets, preset => preset.PresetName == "Unrelated retained club");
            Assert.Equal(Json(persisted.Presets), Json(Tiles(wizard)));
            Assert.Null(name.Text);
            Assert.Equal("CAPT", abbreviation.Text); Assert.Equal("#224466", primary.Text); Assert.Equal("#EECCAA", secondary.Text);
            Border CapturedTile() => Assert.Single(Named<WrapPanel>(wizard, "HomePresetPanel").Children.OfType<Border>(),
                tile => tile.Tag is TeamPreset preset && preset.PresetName == capturedName);
            Assert.Equal("Not selected", Avalonia.Automation.AutomationProperties.GetItemStatus(CapturedTile()));
            Assert.All(PresetActions(wizard), action => Assert.True(action.IsEnabled));

            // Prove the pending state is actually retryable, rather than merely
            // painting an enabled button whose next action is still rejected.
            name.Text = "Explicit retry club";
            SendPointer(window, await PointerTarget(window, SaveAction()));
            PresetReadResult retried = await fixture.Read(sport);
            await Until(() => uiFailure is not null || (PresetActions(wizard).All(action => action.IsEnabled)
                && Tiles(wizard).Any(preset => preset.PresetName == "Explicit retry club")),
                "A new explicit non-null Save must remain usable after the null-name completion.");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(uiFailure);
            Assert.Equal(2, gate.WriteCalls);
            Assert.Equal("Explicit retry club", name.Text);
            Assert.Contains(retried.Presets, preset => preset.PresetName == capturedName);
            Assert.Contains(retried.Presets, preset => preset.PresetName == "Explicit retry club");
            Assert.Equal(Json(retried.Presets), Json(Tiles(wizard)));
        }
        finally
        {
            gate?.Release();
            try
            {
                await fixture.DisposeAsync(); // Drains writes/read queues and closes/restores before capture detaches.
                Dispatcher.UIThread.RunJobs();
            }
            finally { Dispatcher.UIThread.UnhandledException -= ObserveUiFailure; }
        }
        Assert.Null(uiFailure); // Includes any late UI continuation seen during fixture cleanup.
    }

    // Actual owned Browse route; modeless substitutes do not satisfy these cases.
    private static List<TeamPreset> OwnedModalTiles(Control wizard, string side) =>
        Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>()
            .Select(tile => Assert.IsType<TeamPreset>(tile.Tag)).ToList();
    private static string?[] OwnedModalDraft(Control wizard, string side) =>
        new[] { "NameBox", "AbbrBox", "ColorBox", "SecondaryBox" }
            .Select(suffix => Named<TextBox>(wizard, "Setup" + side + suffix).Text).ToArray();
    private static void OwnedModalSetDraft(Control wizard, string side, string name, string abbr)
    {
        Named<TextBox>(wizard, "Setup" + side + "NameBox").Text = name;
        Named<TextBox>(wizard, "Setup" + side + "AbbrBox").Text = abbr;
        Named<TextBox>(wizard, "Setup" + side + "ColorBox").Text = "#224466";
        Named<TextBox>(wizard, "Setup" + side + "SecondaryBox").Text = "#EECCAA";
    }

    [AvaloniaTheory]
    [InlineData(false, "Home", "use")]
    [InlineData(false, "Away", "use")]
    [InlineData(true, "Home", "use")]
    [InlineData(true, "Away", "use")]
    [InlineData(false, "Home", "close-glyph")]
    [InlineData(false, "Away", "close-glyph")]
    [InlineData(true, "Home", "close-glyph")]
    [InlineData(true, "Away", "close-glyph")]
    [InlineData(false, "Home", "escape")]
    [InlineData(false, "Away", "escape")]
    [InlineData(true, "Home", "escape")]
    [InlineData(true, "Away", "escape")]
    public async Task ActualOwnedBrowseReturnsBeforeAcceptedFavouriteAndNeverReappliesOverCurrentDraft(
        bool cricket, string side, string decision)
    {
        StoreFixture fixture = await StoreFixture.Create();
        TeamLibrarySport sport = cricket ? Cricket : Afl;
        GateIo? gate = null;
        Task<PresetCommitResult[]>? accepted = null;
        TeamLibraryWindow? library = null;
        TeamCatalogEntry? selectedB = null;
        Exception? driverFailure = null, uiFailure = null;
        bool browseReturned = false, modalActionSent = false, driverFinished = false;
        Task? driver = null;
        var closedUiChanges = new List<string>();
        Control[] observedClosedControls = [];
        void ObserveClosedUi(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            // Ignore scheduled layout/render/motion bookkeeping after close; pin
            // the forbidden publication fields, as the existing public Close case does.
            if (args.Property.Name is "Text" or "Source" or "IsEnabled" or "Content" or "Tag" or "IsVisible")
                closedUiChanges.Add((sender as Control)?.Name + ":" + args.Property.Name);
        }
        void ObserveUiFailure(object? sender, DispatcherUnhandledExceptionEventArgs args)
        { uiFailure ??= args.Exception; args.Handled = true; } // Always asserted, never a success workaround.
        Dispatcher.UIThread.UnhandledException += ObserveUiFailure;
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        NativeWindow? owner = null;
        watchdog.Tick += (_, _) =>
        {
            if (waiting.Elapsed <= TimeSpan.FromSeconds(15)) return;
            driverFailure ??= new TimeoutException("Actual owned Browse interaction exceeded 15 seconds.");
            watchdog.Stop();
            gate?.Release();
            if (owner is not null)
                foreach (var child in owner.OwnedWindows.OfType<TeamLibraryWindow>().ToArray()) child.Close();
        };
        try
        {
            await fixture.Seed(sport, [Other()]);
            PresetReadResult originalStore = await fixture.Read(sport);
            byte[] originalBytes = File.ReadAllBytes(fixture.PathFor(sport));
            TeamCatalogEntry a = Team(sport), b = Team(sport, 1);
            // Fully prepare both real offline logos BEFORE accepting any gated IO.
            await fixture.OfflineLogo(a, sport); await fixture.OfflineLogo(b, sport);
            string aPath = await TeamLogoService.GetTransparentLogoAsync(a);
            string bPath = await TeamLogoService.GetTransparentLogoAsync(b);
            Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
            owner = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
            owner.Show();
            await Until(() => wizard.IsLoaded && OwnedModalTiles(wizard, "Home").Count == 1
                && OwnedModalTiles(wizard, "Away").Count == 1 && PresetActions(wizard).Any() && PresetActions(wizard).All(action => action.IsEnabled),
                "Owner wizard must finish its initial confirmed read before actual Browse.");
            OwnedModalSetDraft(wizard, side, "Draft before actual Browse", "PRE");
            string opposite = side == "Home" ? "Away" : "Home";
            string?[] originalDraft = OwnedModalDraft(wizard, side), oppositeDraft = OwnedModalDraft(wizard, opposite);
            var originalLogo = (Named<TextBlock>(wizard, "Setup" + side + "LogoText").Text,
                Named<Image>(wizard, "Setup" + side + "LogoImage").Source);
            var oppositeLogo = (Named<TextBlock>(wizard, "Setup" + opposite + "LogoText").Text,
                Named<Image>(wizard, "Setup" + opposite + "LogoImage").Source);
            // WizardTeamEditor reparents Browse beside BOTH named cards at runtime.
            // Find the nearest actual side body shared by those existing cards.
            var teamCard = Named<Border>(wizard, side + "TeamCard");
            var sideBody = Named<Border>(wizard, side + "PresetCard").GetVisualAncestors().OfType<StackPanel>()
                .First(panel => panel.GetVisualDescendants().Contains(teamCard));
            var browse = Assert.Single(sideBody.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Find a club") && button.IsEffectivelyVisible);
            Point browsePoint = await PointerTarget(owner, browse);
            Assert.Empty(owner.OwnedWindows.OfType<TeamLibraryWindow>());
            Assert.True(browse.Focus(NavigationMethod.Tab));
            Assert.Same(browse, owner.FocusManager?.GetFocusedElement());

            async Task DriveActualOwnedModal()
            {
                try
                {
                    // This continuation is scheduled BEFORE real Browse pointer input.
                    // NativeAwait's nested DispatcherFrame runs it while ShowDialog waits.
                    // Bounded 15 ms observer, started before pointer activation.
                    // It never constructs a second modeless substitute dialog.
                    while (!owner.OwnedWindows.OfType<TeamLibraryWindow>().Any()
                        && driverFailure is null && waiting.Elapsed < TimeSpan.FromSeconds(15))
                        await Task.Delay(15);
                    if (driverFailure is not null) return;
                    library = fixture.Track(Assert.Single(owner.OwnedWindows.OfType<TeamLibraryWindow>()));
                    Assert.Same(owner, library.Owner); Assert.False(browseReturned);
                    Assert.Contains(library, owner.OwnedWindows);
                    Assert.Equal(cricket ? "Cricket Club Library" : "Australian Football Club Library", library.Title);
                    Named<ComboBox>(library, "LeagueFilter").SelectedIndex = 0;
                    Named<ComboBox>(library, "StateFilter").SelectedIndex = 0;
                    Named<ComboBox>(library, "SortFilter").SelectedIndex = 0;
                    Named<CheckBox>(library, "FavouriteOnly").IsChecked = false;
                    await SelectReady(library, a);
                    (gate, accepted) = await StartFavourite(fixture, library);
                    Assert.False(browseReturned); Assert.False(accepted.IsCompleted);
                    Assert.Equal(1, gate.WriteCalls);
                    await SelectReady(library, b, favouriteMustBeEnabled: false);
                    selectedB = Assert.IsType<TeamCatalogEntry>(Named<ListBox>(library, "TeamList").SelectedItem);
                    Assert.Equal(b.Id, selectedB.Id);
                    Assert.True(Named<Button>(library, "UseButton").IsEnabled);
                    Assert.False(Named<Button>(library, "FavouriteButton").IsEnabled);
                    Assert.False(accepted.IsCompleted);
                    if (decision == "use")
                        SendPointer(library, await PointerTarget(library, Named<Button>(library, "UseButton")));
                    else if (decision == "close-glyph")
                    {
                        var close = Assert.Single(library.GetVisualDescendants().OfType<Button>(),
                            button => Equals(button.Content, "×") && button.IsEffectivelyVisible);
                        SendPointer(library, await PointerTarget(library, close));
                    }
                    else
                    {
                        // Actual library key route; no invocation of Window_KeyDown.
                        Named<ListBox>(library, "TeamList").Focus(NavigationMethod.Tab);
                        library.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                        // Escape closes synchronously. The real release goes to
                        // the restored owner; a disposed headless top-level has
                        // no platform implementation to receive an input event.
                        if (library.IsVisible) library.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                        else owner.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                    }
                    modalActionSent = true;
                    Assert.False(accepted.IsCompleted);
                }
                catch (Exception error)
                {
                    driverFailure ??= error;
                    foreach (var child in owner.OwnedWindows.OfType<TeamLibraryWindow>().ToArray()) child.Close();
                }
                finally { driverFinished = true; }
            }
            waiting.Restart(); watchdog.Start();
            driver = DriveActualOwnedModal();
            // Synchronously enters production Browse→NativeWindow.ShowDialog. This
            // call must return while the storage write is still positively held.
            SendPointer(owner, browsePoint);
            browseReturned = true;
            watchdog.Stop();
            await driver;
            Assert.True(driverFinished); Assert.Null(driverFailure); Assert.Null(uiFailure);
            Assert.True(modalActionSent); Assert.NotNull(library); Assert.NotNull(gate); Assert.NotNull(accepted);
            Assert.False(library!.IsVisible);
            Assert.DoesNotContain(library, owner.OwnedWindows);
            Assert.True(owner.IsEnabled);
            Assert.Same(browse, owner.FocusManager?.GetFocusedElement());
            Assert.False(accepted!.IsCompleted);
            Assert.Equal(1, gate!.WriteCalls);
            if (decision == "use")
            {
                Assert.True(library.DialogResult);
                Assert.NotNull(library.Selection);
                Assert.Equal(b.Id, library.Selection!.Team.Id);
                // RefreshResults intentionally produces detached presentation rows.
                // Use must freeze the actual selected B row, not catalogue identity.
                Assert.Same(selectedB, library.Selection.Team);
                Assert.Equal(bPath, library.Selection.TransparentLogoPath);
                Assert.False(string.IsNullOrWhiteSpace(library.Selection.TransparentLogoPath));
                Assert.True(File.Exists(library.Selection.TransparentLogoPath));
                Assert.Equal(b.TeamName, Named<TextBox>(wizard, "Setup" + side + "NameBox").Text);
                Assert.Equal(b.Abbreviation, Named<TextBox>(wizard, "Setup" + side + "AbbrBox").Text);
                Assert.Equal(b.PrimaryColor, Named<TextBox>(wizard, "Setup" + side + "ColorBox").Text);
                Assert.Equal(b.SecondaryColor, Named<TextBox>(wizard, "Setup" + side + "SecondaryBox").Text);
                Assert.Equal(Path.GetFileName(library.Selection.TransparentLogoPath),
                    Named<TextBlock>(wizard, "Setup" + side + "LogoText").Text);
                Assert.NotNull(Named<Image>(wizard, "Setup" + side + "LogoImage").Source);
            }
            else
            {
                Assert.Null(library.DialogResult);
                Assert.Null(library.Selection);
                Assert.Equal(originalDraft, OwnedModalDraft(wizard, side));
                Assert.Equal(originalLogo, (Named<TextBlock>(wizard, "Setup" + side + "LogoText").Text,
                    Named<Image>(wizard, "Setup" + side + "LogoImage").Source));
            }
            Assert.Equal(oppositeDraft, OwnedModalDraft(wizard, opposite));
            Assert.Equal(oppositeLogo, (Named<TextBlock>(wizard, "Setup" + opposite + "LogoText").Text,
                Named<Image>(wizard, "Setup" + opposite + "LogoImage").Source));
            Assert.DoesNotContain(OwnedModalTiles(wizard, side), preset => preset.LibraryTeamId == a.Id);
            Task<PresetReadResult> barrier = fixture.Read(sport);
            Assert.False(barrier.IsCompleted); // Real store queue is still behind accepted A.
            byte[] stillOld = File.ReadAllBytes(fixture.PathFor(sport));
            Assert.Equal(originalBytes, stillOld);
            Assert.DoesNotContain(a.TeamName, System.Text.Encoding.UTF8.GetString(stillOld), StringComparison.Ordinal);
            OwnedModalSetDraft(wizard, side, "Edited after real modal return", "NOW");
            // Keep current owner focus on a real editable field while the old
            // dialog continuation completes. This is a public focus operation.
            var currentName = Named<TextBox>(wizard, "Setup" + side + "NameBox");
            Assert.True(currentName.Focus(NavigationMethod.Tab));
            Assert.Same(currentName, owner.FocusManager?.GetFocusedElement());
            string?[] currentDraft = OwnedModalDraft(wizard, side);
            var currentLogo = (Named<TextBlock>(wizard, "Setup" + side + "LogoText").Text,
                Named<Image>(wizard, "Setup" + side + "LogoImage").Source);
            observedClosedControls = [Named<TextBlock>(library, "StatusText"), Named<TextBlock>(library, "PresetStatus"),
                Named<Image>(library, "LogoPreview"), Named<TextBlock>(library, "SelectedName"), Named<Button>(library, "FavouriteButton")];
            foreach (Control control in observedClosedControls) control.PropertyChanged += ObserveClosedUi;
            gate.Release();
            Assert.Equal(PresetCommitStatus.Committed, Assert.Single(await accepted.WaitAsync(TimeSpan.FromSeconds(4))).Status);
            PresetReadResult persisted = await barrier.WaitAsync(TimeSpan.FromSeconds(4));
            await Until(() => library.PresetsChanged && OwnedModalTiles(wizard, side).Any(preset => preset.LibraryTeamId == a.Id)
                && Json(OwnedModalTiles(wizard, "Home")) == Json(persisted.Presets)
                && Json(OwnedModalTiles(wizard, "Away")) == Json(persisted.Presets),
                "Current owner must reconcile the latest confirmed snapshot after real modal return.");
            Assert.Equal(1, gate.WriteCalls);
            Assert.True(persisted.Revision > originalStore.Revision);
            Assert.Equal(2, persisted.Presets.Count);
            TeamPreset savedA = Assert.Single(persisted.Presets, preset => preset.LibraryTeamId == a.Id);
            Assert.Equal(a.TeamName, savedA.TeamName);
            Assert.Equal(a.Abbreviation, savedA.Abbreviation);
            Assert.Equal(a.PrimaryColor, savedA.PrimaryColor);
            Assert.Equal(a.SecondaryColor, savedA.SecondaryColor);
            Assert.Equal(aPath, savedA.LogoPath);
            Assert.Equal(Json(originalStore.Presets), Json(persisted.Presets.Where(preset => preset.LibraryTeamId != a.Id)));
            Assert.Equal(Json(persisted.Presets), Json(JsonSerializer.Deserialize<List<TeamPreset>>(
                File.ReadAllText(fixture.PathFor(sport)))!));
            Assert.DoesNotContain(persisted.Presets, preset => preset.LibraryTeamId == b.Id);
            Assert.Equal(currentDraft, OwnedModalDraft(wizard, side));
            Assert.Equal(oppositeDraft, OwnedModalDraft(wizard, opposite));
            Assert.Equal(currentLogo, (Named<TextBlock>(wizard, "Setup" + side + "LogoText").Text,
                Named<Image>(wizard, "Setup" + side + "LogoImage").Source));
            Assert.Equal(oppositeLogo, (Named<TextBlock>(wizard, "Setup" + opposite + "LogoText").Text,
                Named<Image>(wizard, "Setup" + opposite + "LogoImage").Source));
            Assert.Same(currentName, owner.FocusManager?.GetFocusedElement());
            Assert.Empty(closedUiChanges);
            Assert.Null(uiFailure);
        }
        finally
        {
            watchdog.Stop();
            gate?.Release(); // Before any await/drain/file restoration, including assertion failures.
            if (owner is not null)
                foreach (var child in owner.OwnedWindows.OfType<TeamLibraryWindow>().ToArray()) child.Close();
            if (driver is not null) await driver;
            try { await fixture.DisposeAsync(); Dispatcher.UIThread.RunJobs(); }
            finally
            {
                foreach (Control control in observedClosedControls) control.PropertyChanged -= ObserveClosedUi;
                Dispatcher.UIThread.UnhandledException -= ObserveUiFailure;
            }
        }
        Assert.Null(driverFailure); Assert.Null(uiFailure); // Includes drain/cleanup continuations.
    }

    private sealed class GateIo(IPresetStorageIo inner) : IPresetStorageIo
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int writes;
        internal int WriteCalls => Volatile.Read(ref writes);
        internal void Release() => released.TrySetResult();
        public Task<string> ReadAllTextAsync(string path) => inner.ReadAllTextAsync(path);
        public async Task WriteAllTextAsync(string path, string content)
        {
            Interlocked.Increment(ref writes); Entered.TrySetResult();
            await released.Task.ConfigureAwait(false);
            await inner.WriteAllTextAsync(path, content).ConfigureAwait(false);
        }
    }

    private sealed class StoreFixture : IAsyncDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest;
        private readonly bool? previousMotion = OperatorMotion.AnimationOverride;
        private readonly string? previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
        private readonly string scratch = Path.Combine(Path.GetTempPath(), "roche-club-stage2-" + Guid.NewGuid().ToString("N"));
        private readonly string presets = Path.Combine(SelfTestStorage.RootPath, "presets");
        private readonly List<Window> windows = [];
        private readonly List<Task> accepted = [];
        private readonly Dictionary<string, byte[]?> guardedFiles = new();
        private readonly Dictionary<TeamCatalogEntry, bool> favourites = TeamCatalogService.GetTeams(Afl).Concat(TeamCatalogService.GetTeams(Cricket)).ToDictionary(team => team, team => team.IsFavourite);
        private readonly HashSet<string> previousDirectories = Directory.Exists(SelfTestStorage.RootPath)
            ? Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                .Append(SelfTestStorage.RootPath).ToHashSet(StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
        private string? savedPresets;
        private GateIo? gate;
        private IDisposable? ioScope;
        private bool ownsPresets, disposed;
        private StoreFixture() { previousSelfTest = app.IsSelfTestMode; }
        internal static async Task<StoreFixture> Create()
        {
            var fixture = new StoreFixture();
            try { await fixture.Initialize(); return fixture; }
            catch (Exception setup)
            {
                try { await fixture.DisposeAsync(); } catch (Exception cleanup) { throw new AggregateException(setup, cleanup); }
                throw;
            }
        }
        private async Task Initialize()
        {
            SelfTestStorage.Initialize(true); app.IsSelfTestMode = true; OperatorMotion.AnimationOverride = false;
            // Drain old process-path work before moving its files. Never point a
            // queued operation at the restored directory while it is still live.
            await Task.WhenAll(PresetStorage.ReadAsync(Afl), PresetStorage.ReadAsync(Cricket));
            Directory.CreateDirectory(scratch);
            if (Directory.Exists(presets)) { savedPresets = Path.Combine(scratch, "original-presets"); Directory.Move(presets, savedPresets); }
            else Assert.False(File.Exists(presets), "Preset directory is obstructed; do not modify unrelated data.");
            ownsPresets = true; Directory.CreateDirectory(presets);
            string workflow = Path.Combine(SelfTestStorage.RootPath, "workflow", "operator_workflow.json");
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                guardedFiles[workflow + suffix] = File.Exists(workflow + suffix) ? File.ReadAllBytes(workflow + suffix) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(workflow)!);
            File.WriteAllText(workflow, "{}");
            foreach (string suffix in new[] { ".bak", ".tmp" }) if (File.Exists(workflow + suffix)) File.Delete(workflow + suffix);
            Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", Path.Combine(scratch, "documents"));
            await Seed(Afl, []); await Seed(Cricket, []); // Retire any cached confirmed data from the previous fixture.
        }
        internal T Track<T>(T window) where T : Window { windows.Add(window); return window; }
        internal Task<T> Watch<T>(Task<T> task) { accepted.Add(task); return task; }
        internal string PathFor(TeamLibrarySport sport) => Path.Combine(presets, sport == Cricket ? "cricket_team_presets.json" : "team_presets.json");
        internal Task<PresetReadResult> Read(TeamLibrarySport sport) => Watch(PresetStorage.ReadAsync(sport));
        internal Task Seed(TeamLibrarySport sport, List<TeamPreset> value)
        {
            Task task = sport == Cricket ? PresetStorage.SaveAllCricketAsync(value) : PresetStorage.SaveAllAsync(value);
            accepted.Add(task); return task;
        }
        internal GateIo GateWrites()
        {
            Assert.Null(gate); // One gate scope per case; subsequent writes use its released delegation.
            gate = new GateIo(PresetStorage.CurrentIoForTests);
            ioScope = PresetStorage.UseIoForTests(gate); return gate;
        }
        internal async Task OfflineLogo(TeamCatalogEntry team, TeamLibrarySport sport)
        {
            if (sport == Cricket)
            {
                Assert.False(string.IsNullOrWhiteSpace(team.BundledLogo));
                string bundled = await TeamLogoService.GetTransparentLogoAsync(team);
                Assert.True(File.Exists(bundled)); Assert.True(new FileInfo(bundled).Length > 128); return;
            }
            TeamCatalogEntry offline = Team(Cricket);
            string png = await TeamLogoService.GetTransparentLogoAsync(offline);
            Assert.True(File.Exists(png)); Assert.True(new FileInfo(png).Length > 128);
            string directory = Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard", "team_library", "logos");
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, team.Id + "-crest-v2.png"); File.Copy(png, target, overwrite: true);
            Assert.Equal(target, await TeamLogoService.GetTransparentLogoAsync(team)); // Must hit offline cache.
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return; disposed = true;
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            // Capture every library receipt even if an assertion failed before
            // StartFavourite returned. Closing never cancels those Tasks.
            foreach (TeamLibraryWindow library in windows.OfType<TeamLibraryWindow>()) accepted.Add(library.AcceptedPresetChanges);
            foreach (Window window in windows.AsEnumerable().Reverse()) Attempt(window.Close);
            gate?.Release(); // Unconditional release precedes every drain / IO restoration.
            try { await Task.WhenAll(accepted); } catch (Exception error) { errors.Add(error); }
            try { await Task.WhenAll(PresetStorage.ReadAsync(Afl), PresetStorage.ReadAsync(Cricket)); }
            catch (Exception error) { errors.Add(error); }
            Attempt(() => Dispatcher.UIThread.RunJobs());
            Attempt(() => ioScope?.Dispose()); // All accepted Tasks and both store queues are drained first.
            Attempt(() => { if (ownsPresets && Directory.Exists(presets)) Directory.Delete(presets, true); });
            Attempt(() => { if (savedPresets is not null && Directory.Exists(savedPresets)) Directory.Move(savedPresets, presets); });
            foreach (var (path, bytes) in guardedFiles)
                Attempt(() => { if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); } });
            Attempt(() =>
            {
                if (!Directory.Exists(SelfTestStorage.RootPath)) return;
                foreach (string directory in Directory.EnumerateDirectories(SelfTestStorage.RootPath, "*", SearchOption.AllDirectories)
                    .Append(SelfTestStorage.RootPath).OrderByDescending(path => path.Length))
                    if (!previousDirectories.Contains(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            });
            Attempt(() => { foreach (var pair in favourites) pair.Key.IsFavourite = pair.Value; });
            Attempt(() => app.IsSelfTestMode = previousSelfTest);
            Attempt(() => OperatorMotion.AnimationOverride = previousMotion);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome));
            if (errors.Count == 0) Attempt(() => { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); });
            if (errors.Count > 0) throw new AggregateException("Gated fixture rollback failed; original storage retained under " + scratch, errors);
        }
    }
}
