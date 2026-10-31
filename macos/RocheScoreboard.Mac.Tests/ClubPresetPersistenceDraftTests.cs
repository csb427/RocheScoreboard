// Central verification owns compilation/execution and baseline receipts.
// Uses the current test host's existing friend access for fixture isolation.
// Current public services + actual visible pointer routes; no reflected handlers.
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

// The existing PresetStorage self-test path is process-global, not per-fixture.
// This collection must exclude other collections while these fixtures own it.
[CollectionDefinition("Club preset persistence draft", DisableParallelization = true)]
public sealed class ClubPresetPersistenceDraftCollection { }

[Collection("Club preset persistence draft")]
public sealed class ClubPresetPersistenceDraftTests
{
    private static async Task Settle(int milliseconds = 60)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task Until(Func<bool> condition, string failure)
    {
        for (int i = 0; i < 160; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.True(condition(), failure);
    }
    private static T Named<T>(Control owner, string name) where T : Control
        => Assert.IsAssignableFrom<T>(owner.FindControl<T>(name));
    private static async Task Tap(Window owner, Control target)
    {
        target.BringIntoView();
        await Settle();
        Assert.True(target.IsEffectivelyVisible, "The real control must be visible.");
        Assert.True(target.IsEnabled, "The real control must be enabled.");
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0);
        Point? origin = target.TranslatePoint(default, owner);
        Assert.NotNull(origin);
        Assert.True(new Rect(owner.ClientSize).Contains(new Rect(origin!.Value, target.Bounds.Size)),
            "The whole control must be reachable before activating it.");
        Point center = origin.Value + new Vector(target.Bounds.Width / 2, target.Bounds.Height / 2);
        var hit = owner.InputHitTest(center);
        Assert.True(ReferenceEquals(hit, target) || hit is Visual child && child.GetVisualAncestors().Contains(target),
            "The actual pointer must hit the requested control/template, not an occluding panel.");
        owner.MouseMove(center);
        owner.MouseDown(center, MouseButton.Left);
        owner.MouseUp(center, MouseButton.Left);
        await Settle(100);
    }
    private static TeamPreset Target() => new()
    {
        PresetName = "Committed club", TeamName = "Committed club", Abbreviation = "OLD",
        PrimaryColor = "#123456", SecondaryColor = "#FEDCBA",
        LibraryTeamId = "fixture-committed-club",
        CricketPlayers = [new() { FirstName = "Retained", LastName = "Player" }]
    };
    private static TeamPreset Other() => new()
    {
        PresetName = "Unrelated club", TeamName = "Unrelated club", Abbreviation = "OTH",
        PrimaryColor = "#654321", SecondaryColor = "#ABCDEF"
    };
    private static string Snapshot(IEnumerable<TeamPreset> presets) => JsonSerializer.Serialize(presets);
    private static List<TeamPreset> Tiles(Control wizard, string side)
        => Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>()
            .Select(tile => Assert.IsType<TeamPreset>(tile.Tag)).ToList();
    private static Button TeamEditorAction(Control wizard, string side, bool delete)
    {
        // WizardTeamEditor moves the original handler-bearing buttons out of
        // PresetCard into this side's TeamCard and changes their visible labels.
        // Scope to the mounted form containing the real named input, rather than
        // a legacy XAML caption/container or another side's matching action.
        var editor = Named<Border>(wizard, side + "TeamCard");
        Assert.True(editor.IsEffectivelyVisible, side + " team editor must be mounted.");
        Assert.Contains(editor, Field(wizard, side, "NameBox").GetVisualAncestors());
        string caption = delete ? "Delete preset" : "Save as preset";
        return Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.Content, caption) && button.IsEffectivelyVisible);
    }
    private static TextBox Field(Control wizard, string side, string suffix)
        => Named<TextBox>(wizard, "Setup" + side + suffix);

    // Twelve independent write-failure cases; no production API changes needed.
    [AvaloniaTheory]
    [InlineData(false, "Home", "new")]
    [InlineData(false, "Away", "new")]
    [InlineData(true, "Home", "new")]
    [InlineData(true, "Away", "new")]
    [InlineData(false, "Home", "overwrite")]
    [InlineData(false, "Away", "overwrite")]
    [InlineData(true, "Home", "overwrite")]
    [InlineData(true, "Away", "overwrite")]
    [InlineData(false, "Home", "delete")]
    [InlineData(false, "Away", "delete")]
    [InlineData(true, "Home", "delete")]
    [InlineData(true, "Away", "delete")]
    public async Task FailedWizardWriteKeepsConfirmedTilesSelectionAndCurrentDraft(bool cricket, string side, string action)
    {
        using var fixture = new StoreFixture();
        List<TeamPreset> committed = [Target(), Other()];
        fixture.Save(cricket, committed);
        Assert.Equal(Snapshot(committed), Snapshot(fixture.Load(cricket))); // Real initial file, not missing==missing.
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        await Until(() => Named<Control>(wizard, side + "PresetPanel").IsEffectivelyVisible, "Team presets must mount.");
        var targetTile = Assert.Single(Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == "Committed club");
        await Tap(window, targetTile); // Exercises normal production tile selection/capture route.
        Assert.Equal("Committed club", Field(wizard, side, "NameBox").Text);
        Assert.Contains("Selected", Avalonia.Automation.AutomationProperties.GetItemStatus(
            Assert.Single(Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>(),
                tile => tile.Tag is TeamPreset preset && preset.PresetName == "Committed club")) ?? "");
        string priorHomeTiles = Snapshot(Tiles(wizard, "Home"));
        string priorAwayTiles = Snapshot(Tiles(wizard, "Away"));
        Field(wizard, side, "NameBox").Text = action == "overwrite" ? "Committed club" : "Unsaved draft club";
        Field(wizard, side, "AbbrBox").Text = "DRF";
        Field(wizard, side, "ColorBox").Text = "#AABBCC";
        Field(wizard, side, "SecondaryBox").Text = "#112233";
        string? draftName = Field(wizard, side, "NameBox").Text;
        fixture.BlockWrites(cricket); // Filename is now a directory: deterministic IOException, no permission hack.
        await Tap(window, TeamEditorAction(wizard, side, delete: action == "delete"));

        // Do not only assert an error string: catch added/deleted tiles AND alias mutation on overwrite.
        Assert.Equal(priorHomeTiles, Snapshot(Tiles(wizard, "Home")));
        Assert.Equal(priorAwayTiles, Snapshot(Tiles(wizard, "Away")));
        Assert.Equal(draftName, Field(wizard, side, "NameBox").Text);
        Assert.Equal("DRF", Field(wizard, side, "AbbrBox").Text);
        Assert.Equal("#AABBCC", Field(wizard, side, "ColorBox").Text);
        Assert.Equal("#112233", Field(wizard, side, "SecondaryBox").Text);
        var selectedTile = Assert.Single(Named<WrapPanel>(wizard, side + "PresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == "Committed club");
        Assert.Equal("Selected", Avalonia.Automation.AutomationProperties.GetItemStatus(selectedTile));
        Assert.True(Directory.Exists(fixture.PathFor(cricket)), "The write failure must remain active.");
        Assert.Equal(fixture.BlockedOriginal(cricket), File.ReadAllBytes(fixture.BlockedOriginalPath(cricket)));
        // A failed write must be locally explained. Exact product copy is intentionally not prescribed.
        string visibleText = string.Join("\n", wizard.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
        Assert.True(visibleText.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("couldn't", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("failed", StringComparison.OrdinalIgnoreCase),
            "The unchanged saved state needs a visible persistence failure explanation.");
    }

    // Paired real-route positives. These detect a disconnected/occluded action fixture.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulWizardSavePersistsCurrentDraftAndLeavesUnrelatedClub(bool cricket)
    {
        using var fixture = new StoreFixture();
        fixture.Save(cricket, [Other()]);
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show(); await Settle(180);
        Field(wizard, "Home", "NameBox").Text = "New committed club";
        Field(wizard, "Home", "AbbrBox").Text = "NEW";
        await Tap(window, TeamEditorAction(wizard, "Home", delete: false));
        var loaded = fixture.Load(cricket);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, preset => preset.PresetName == "New committed club" && preset.Abbreviation == "NEW");
        Assert.Contains(loaded, preset => preset.PresetName == "Unrelated club" && preset.PrimaryColor == "#654321");
        Assert.Equal(Snapshot(loaded), Snapshot(Tiles(wizard, "Home")));
    }

    // Existing library verification may already satisfy these two cases. Keep that functionality.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLibraryWriteDoesNotPublishCommittedMembershipOrSuccess(bool initiallySaved)
    {
        using var fixture = new StoreFixture();
        var team = TeamCatalogService.GetTeams().First();
        await fixture.PrepareOfflineAflLogo(team);
        var savedTeam = new TeamPreset { PresetName = team.TeamName, TeamName = team.TeamName,
            Abbreviation = team.Abbreviation, LibraryTeamId = team.Id };
        fixture.Save(false, initiallySaved ? [Other(), savedTeam] : [Other()]);
        string before = Snapshot(fixture.Load(false));
        var library = fixture.Track(new TeamLibraryWindow("Home")); library.Show();
        await PrepareSelectedClub(library, team);
        fixture.BlockWrites(false, keepReadableBackup: true); // Read valid .bak; fail only destination replacement.
        await Tap(library, Named<Button>(library, "FavouriteButton"));
        await Until(() => Named<Button>(library, "FavouriteButton").IsEnabled
            && (Named<TextBlock>(library, "StatusText").Text ?? "").Contains("Could not save", StringComparison.OrdinalIgnoreCase),
            "Accepted favourite must finish with visible failure, not success.");
        Assert.False(library.PresetsChanged);
        Assert.Equal(before, Snapshot(fixture.Load(false))); // Valid backup remains authoritative recovery evidence.
        Assert.Equal(initiallySaved, Named<Button>(library, "FavouriteButton").Tag);
        Assert.Equal(team.Id, Assert.IsType<TeamCatalogEntry>(Named<ListBox>(library, "TeamList").SelectedItem).Id);
        Assert.DoesNotContain("Added to", Named<TextBlock>(library, "StatusText").Text ?? "");
        Assert.DoesNotContain("Removed from", Named<TextBlock>(library, "StatusText").Text ?? "");
        await Tap(library, Named<CheckBox>(library, "FavouriteOnly"));
        var results = Named<ListBox>(library, "TeamList").Items.Cast<TeamCatalogEntry>().ToArray();
        Assert.Equal(initiallySaved, results.Any(item => item.Id == team.Id));
        // Tests final confirmed state, not absence of a transient optimistic message. That needs a pause seam.
    }

    [AvaloniaFact]
    public async Task CorruptPrimaryWithValidBackupPreservesUnrelatedSavedClubWhenAddingAnother()
    {
        using var fixture = new StoreFixture();
        var teams = TeamCatalogService.GetTeams(); var team = teams[0]; var unrelated = teams[1];
        await fixture.PrepareOfflineAflLogo(team);
        fixture.Save(false, [new() { PresetName = unrelated.TeamName, TeamName = unrelated.TeamName,
            Abbreviation = unrelated.Abbreviation, LibraryTeamId = unrelated.Id }]);
        File.Copy(fixture.PathFor(false), fixture.PathFor(false) + ".bak");
        File.WriteAllText(fixture.PathFor(false), "{ invalid primary");
        Assert.Equal(unrelated.Id, Assert.Single(fixture.Load(false)).LibraryTeamId); // Prove real recovery first.
        var library = fixture.Track(new TeamLibraryWindow("Home")); library.Show();
        await PrepareSelectedClub(library, team);
        await Tap(library, Named<Button>(library, "FavouriteButton"));
        await Until(() => library.PresetsChanged && Named<Button>(library, "FavouriteButton").IsEnabled,
            "Normal backup-based add must complete.");
        var reloaded = fixture.Load(false);
        Assert.Equal(2, reloaded.Count);
        Assert.Contains(reloaded, preset => preset.LibraryTeamId == team.Id);
        Assert.Contains(reloaded, preset => preset.LibraryTeamId == unrelated.Id);
    }

    [AvaloniaFact]
    public async Task BothCorruptReadsMustNotBeOverwrittenAsAnEmptyStoreByFavourite()
    {
        using var fixture = new StoreFixture();
        var teams = TeamCatalogService.GetTeams(); var team = teams[0]; var unrelated = teams[1];
        await fixture.PrepareOfflineAflLogo(team);
        fixture.Save(false, [new() { PresetName = unrelated.TeamName, TeamName = unrelated.TeamName,
            LibraryTeamId = unrelated.Id }]);
        Assert.Equal(unrelated.Id, Assert.Single(fixture.Load(false)).LibraryTeamId);
        var library = fixture.Track(new TeamLibraryWindow("Home")); library.Show();
        await PrepareSelectedClub(library, team); // Library has positively loaded prior membership.
        File.WriteAllText(fixture.PathFor(false), "{ invalid primary");
        File.WriteAllText(fixture.PathFor(false) + ".bak", "{ invalid backup");
        byte[] corruptPrimary = File.ReadAllBytes(fixture.PathFor(false));
        byte[] corruptBackup = File.ReadAllBytes(fixture.PathFor(false) + ".bak");
        await Tap(library, Named<Button>(library, "FavouriteButton"));
        await Until(() => Named<Button>(library, "FavouriteButton").IsEnabled,
            "Favourite must reach a completed result; a disabled action is not proof of refusal.");
        Assert.False(library.PresetsChanged);
        Assert.Equal(corruptPrimary, File.ReadAllBytes(fixture.PathFor(false)));
        Assert.Equal(corruptBackup, File.ReadAllBytes(fixture.PathFor(false) + ".bak"));
        Assert.False(Equals(Named<Button>(library, "FavouriteButton").Tag, true));
        Assert.DoesNotContain("Added to saved", Named<TextBlock>(library, "StatusText").Text ?? "");
        // This proves unreadable-file refusal, not a simulated IO read failure over still-valid bytes.
    }

    [AvaloniaFact]
    public async Task ValidEmptyStoreCanSaveFirstFavouriteThroughVisibleAction()
    {
        using var fixture = new StoreFixture();
        var team = TeamCatalogService.GetTeams().First();
        await fixture.PrepareOfflineAflLogo(team);
        fixture.Save(false, []); Assert.Equal("[]", File.ReadAllText(fixture.PathFor(false)).Trim());
        var library = fixture.Track(new TeamLibraryWindow("Home")); library.Show();
        await PrepareSelectedClub(library, team);
        await Tap(library, Named<Button>(library, "FavouriteButton"));
        await Until(() => library.PresetsChanged && Named<Button>(library, "FavouriteButton").IsEnabled,
            "Valid empty store must remain writable; do not fix unreadable data by rejecting every empty list.");
        Assert.Equal(team.Id, Assert.Single(fixture.Load(false)).LibraryTeamId);
        Assert.Equal(true, Named<Button>(library, "FavouriteButton").Tag);
    }

    private static async Task PrepareSelectedClub(TeamLibraryWindow library, TeamCatalogEntry team)
    {
        Named<ComboBox>(library, "LeagueFilter").SelectedIndex = 0;
        Named<ComboBox>(library, "StateFilter").SelectedIndex = 0;
        Named<ComboBox>(library, "SortFilter").SelectedIndex = 0;
        Named<CheckBox>(library, "FavouriteOnly").IsChecked = false;
        await Settle();
        var list = Named<ListBox>(library, "TeamList");
        Assert.Contains(list.Items.Cast<TeamCatalogEntry>(), item => item.Id == team.Id);
        list.SelectedItem = list.Items.Cast<TeamCatalogEntry>().Single(item => item.Id == team.Id);
        await Until(() => Named<Button>(library, "UseButton").IsEnabled
            && Named<Button>(library, "FavouriteButton").IsEnabled
            && Named<Image>(library, "LogoPreview").Source is not null,
            "Public list selection must prepare a real cached logo and enable actual actions.");
        Assert.Equal(team.Id, Assert.IsType<TeamCatalogEntry>(list.SelectedItem).Id);
    }

    private sealed class FailingWriteIo(IPresetStorageIo inner) : IPresetStorageIo
    {
        internal int Writes;
        public Task<string> ReadAllTextAsync(string path) => inner.ReadAllTextAsync(path);
        public Task WriteAllTextAsync(string path, string content)
        {
            Interlocked.Increment(ref Writes);
            return Task.FromException(new IOException("Controlled write failure after a successful read."));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualWriterFailureAfterAValidReadPreservesSavedTilesDraftAndPrimaryBytes(bool cricket)
    {
        using var fixture = new StoreFixture();
        fixture.Save(cricket, [Target(), Other()]);
        Control wizard = cricket ? new CricketSetupWizard() : new SetupWizard();
        var window = fixture.Track(new NativeWindow { Width = 1280, Height = 1000, Content = wizard });
        window.Show();
        await Until(() => Named<WrapPanel>(wizard, "HomePresetPanel").Children.Count == 2, "Both confirmed clubs must mount before the IO seam.");
        var selected = Assert.Single(Named<WrapPanel>(wizard, "HomePresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == "Committed club");
        await Tap(window, selected);
        Field(wizard, "Home", "NameBox").Text = "Uncommitted replacement draft";
        string beforeHome = Snapshot(Tiles(wizard, "Home"));
        string beforeAway = Snapshot(Tiles(wizard, "Away"));
        byte[] bytes = File.ReadAllBytes(fixture.PathFor(cricket));
        var failure = new FailingWriteIo(PresetStorage.CurrentIoForTests);
        using (PresetStorage.UseIoForTests(failure))
        {
            await Tap(window, TeamEditorAction(wizard, "Home", delete: false));
            await Until(() => TeamEditorAction(wizard, "Home", delete: false).IsEnabled && failure.Writes > 0,
                "The real action must attempt and finish the controlled write failure.");
            // Drain accepted work while the backend is still installed.
            await PresetStorage.ReadAsync(cricket ? TeamLibrarySport.Cricket : TeamLibrarySport.AustralianFootball);
        }
        Assert.Equal(1, failure.Writes);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.PathFor(cricket)));
        Assert.Equal(beforeHome, Snapshot(Tiles(wizard, "Home")));
        Assert.Equal(beforeAway, Snapshot(Tiles(wizard, "Away")));
        Assert.Equal("Uncommitted replacement draft", Field(wizard, "Home", "NameBox").Text);
        var retainedSelection = Assert.Single(Named<WrapPanel>(wizard, "HomePresetPanel").Children.OfType<Border>(),
            tile => tile.Tag is TeamPreset preset && preset.PresetName == "Committed club");
        Assert.Equal("Selected", Avalonia.Automation.AutomationProperties.GetItemStatus(retainedSelection));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedNullRosterIsAnExplicitReadFailureOrRecoversAValidBackup(bool validBackup)
    {
        using var fixture = new StoreFixture();
        fixture.Save(true, [Target()]);
        string path = fixture.PathFor(true);
        if (validBackup) File.Copy(path, path + ".bak", true);
        else if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        const string malformed = "[{\"PresetName\":\"Malformed roster\",\"TeamName\":\"Malformed roster\",\"CricketPlayers\":[null]}]";
        File.WriteAllText(path, malformed);

        var result = await PresetStorage.ReadAsync(TeamLibrarySport.Cricket);

        Assert.Equal(validBackup ? PresetReadStatus.RecoveredBackup : PresetReadStatus.Failed, result.Status);
        Assert.Equal(validBackup, result.Succeeded);
        if (validBackup)
        {
            var retained = Assert.Single(result.Presets);
            Assert.Equal("Committed club", retained.PresetName);
            Assert.NotNull(retained.CricketPlayers);
            Assert.Equal("Retained", Assert.Single(retained.CricketPlayers).FirstName);
        }
        Assert.Equal(malformed, File.ReadAllText(path));
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly App app = Assert.IsType<App>(Application.Current);
        private readonly bool previousSelfTest;
        private readonly bool? previousMotion;
        private readonly string? previousDataHome;
        private readonly string scratch = Path.Combine(Path.GetTempPath(), "roche-club-draft-" + Guid.NewGuid().ToString("N"));
        private readonly string presets = Path.Combine(Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(), "presets");
        private readonly Dictionary<TeamCatalogEntry, bool> oldFavourites = TeamCatalogService.GetTeams()
            .Concat(TeamCatalogService.GetTeams(TeamLibrarySport.Cricket)).ToDictionary(team => team, team => team.IsFavourite);
        private readonly List<Window> windows = [];
        private readonly Dictionary<bool, byte[]> blockedBytes = [];
        private string? savedPresets;
        private bool ownsPresets, disposed;
        internal StoreFixture()
        {
            previousSelfTest = app.IsSelfTestMode;
            previousMotion = OperatorMotion.AnimationOverride;
            previousDataHome = Environment.GetEnvironmentVariable("ROCHE_DATA_HOME");
            try
            {
                Directory.CreateDirectory(scratch);
                if (Directory.Exists(presets))
                {
                    savedPresets = Path.Combine(scratch, "original-process-presets");
                    Directory.Move(presets, savedPresets); // Preserve every original file/backup/temp atomically.
                }
                ownsPresets = true;
                Directory.CreateDirectory(presets);
                app.IsSelfTestMode = true;
                OperatorMotion.AnimationOverride = false;
                Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", Path.Combine(scratch, "documents"));
            }
            catch (Exception setup)
            {
                try { Dispose(); } catch (Exception cleanup) { throw new AggregateException(setup, cleanup); }
                throw;
            }
        }
        internal T Track<T>(T window) where T : Window { windows.Add(window); return window; }
        internal string PathFor(bool cricket) => Path.Combine(presets, cricket ? "cricket_team_presets.json" : "team_presets.json");
        internal List<TeamPreset> Load(bool cricket) => cricket ? PresetStorage.LoadAllCricket() : PresetStorage.LoadAll();
        internal void Save(bool cricket, List<TeamPreset> value)
        { if (cricket) PresetStorage.SaveAllCricket(value); else PresetStorage.SaveAll(value); }
        internal string BlockedOriginalPath(bool cricket) => Path.Combine(scratch, cricket ? "blocked-cricket.json" : "blocked-afl.json");
        internal byte[] BlockedOriginal(bool cricket) => blockedBytes[cricket];
        internal void BlockWrites(bool cricket, bool keepReadableBackup = false)
        {
            string path = PathFor(cricket);
            Assert.True(File.Exists(path));
            blockedBytes.Add(cricket, File.ReadAllBytes(path));
            if (keepReadableBackup) File.Copy(path, path + ".bak", overwrite: true);
            else if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            File.Move(path, BlockedOriginalPath(cricket));
            Directory.CreateDirectory(path);
            Assert.True(Directory.Exists(path)); Assert.False(File.Exists(path));
        }
        internal async Task PrepareOfflineAflLogo(TeamCatalogEntry afl)
        {
            // Public bundled Cricket route is offline. Its real valid PNG seeds the documented AFL v2 cache.
            // This avoids changing catalog entries or contacting the network for an AFL logo.
            var bundled = TeamCatalogService.GetTeams(TeamLibrarySport.Cricket)
                .First(team => team.Id == "cricket-vsdca-altona" && !string.IsNullOrWhiteSpace(team.BundledLogo));
            string png = await TeamLogoService.GetTransparentLogoAsync(bundled);
            Assert.True(File.Exists(png)); Assert.True(new FileInfo(png).Length > 128);
            string directory = Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Roche_Scoreboard", "team_library", "logos");
            Directory.CreateDirectory(directory);
            string aflCache = Path.Combine(directory, afl.Id + "-crest-v2.png");
            File.Copy(png, aflCache, overwrite: true);
            Assert.Equal(aflCache, await TeamLogoService.GetTransparentLogoAsync(afl));
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            foreach (var window in windows.AsEnumerable().Reverse()) Attempt(() => window.Close());
            Dispatcher.UIThread.RunJobs();
            Attempt(() => { if (ownsPresets && Directory.Exists(presets)) Directory.Delete(presets, recursive: true); });
            Attempt(() => { if (savedPresets is not null && Directory.Exists(savedPresets)) Directory.Move(savedPresets, presets); });
            Attempt(() => { foreach (var pair in oldFavourites) pair.Key.IsFavourite = pair.Value; });
            Attempt(() => app.IsSelfTestMode = previousSelfTest);
            Attempt(() => OperatorMotion.AnimationOverride = previousMotion);
            Attempt(() => Environment.SetEnvironmentVariable("ROCHE_DATA_HOME", previousDataHome));
            // If restoration failed, preserve scratch (including original-process-presets) for recovery.
            if (errors.Count == 0) Attempt(() => { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); });
            if (errors.Count != 0) throw new AggregateException("Preset draft fixture rollback failed; scratch=" + scratch, errors);
        }
    }
}
