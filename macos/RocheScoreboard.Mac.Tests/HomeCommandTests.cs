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

public sealed class HomeCommandTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Private)!.Invoke(owner,args);
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static async Task Settle(int ms=80){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Key(Window window,PhysicalKey key,RawInputModifiers modifiers=RawInputModifiers.None)
    {window.KeyPressQwerty(key,modifiers);window.KeyReleaseQwerty(key,modifiers);Dispatcher.UIThread.RunJobs();}
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-8-captures";
        Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private static void AflSession(Roche_Scoreboard.MainWindow window)
    {
        window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;
        window.FindControl<Control>("MainContent")!.IsVisible=true;
        Set(window,"_aflMatchReady",true);Set(window,"_sportMode",SportMode.AFL);
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(640,480)][InlineData(420,320)]
    public async Task HomeShowsOnlyValidSavedDataAndKeepsAllStartsScrollable(int width,int height)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var home=new SportSelectionScreen();var window=new NativeWindow{Width=width,Height=height,Content=home};window.Show();
        try
        {
            await Settle(550);home.PrepareForDisplay();
            Assert.False(home.FindControl<Button>("HomeResumeButton")!.IsVisible);
            Assert.Contains("No recoverable",home.FindControl<TextBlock>("SessionDetail")!.Text);
            Capture(window,$"home-empty-{width}x{height}");
            var match=new MatchManager();match.SetTeams("Roche with a very long club name","RCH","Visitors with a very long club name","VIS");match.AddGoal(TeamSide.Home);
            GameStateService.Save(new GameState{MatchState=match.ToState()});home.PrepareForDisplay();await Settle();
            Assert.True(home.FindControl<Button>("HomeResumeButton")!.IsVisible);
            Assert.Contains("6–0",home.FindControl<TextBlock>("SessionDetail")!.Text);
            Assert.Contains("Roche",home.FindControl<TextBlock>("SessionHeading")!.Text);
            Capture(window,$"home-saved-{width}x{height}");
            foreach(string name in new[]{"HomeResumeButton","AflCard","TrainingCard"})
            {
                var target=home.FindControl<Control>(name)!;target.BringIntoView();await Settle();
                var point=target.TranslatePoint(default,window)!.Value;
                Assert.True(new Rect(window.Bounds.Size).Intersects(new Rect(point,target.Bounds.Size)),name+" must be reachable by scrolling");
            }
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [Theory]
    [InlineData("empty")][InlineData("unknown-sport")][InlineData("cricket-without-innings")][InlineData("null-players")][InlineData("legacy-afl")][InlineData("valid-cricket")]
    public void RecoveryDescriptionRejectsMalformedPayloadsAndAcceptsOlderSavedGames(string kind)
    {
        GameState state=kind=="legacy-afl"?new(){MatchState=new MatchManager().ToState()}:new();
        if(kind is "valid-cricket" or "null-players" or "cricket-without-innings")
        {
            var match=CricketReliabilityChecks.CreateMatch(true);state.Sport="cricket";state.CricketMatchState=match.CapturePersistenceState();
            if(kind=="null-players")state.CricketMatchState.TeamAPlayers=null!;
            if(kind=="cricket-without-innings")state.CricketMatchState.AllInnings.Clear();
        }
        if(kind=="unknown-sport"){state.MatchState=new MatchManager().ToState();state.Sport="future-mode";}
        var description=RecoverySession.Describe(state);
        if(kind is "legacy-afl" or "valid-cricket"){Assert.NotNull(description);Assert.Contains("older save",description.Detail);}
        else Assert.Null(description);
    }
    [Fact]
    public void StructurallyInvalidPrimaryFallsBackToValidatedBackupWithoutOverwritingIt()
    {
        string dir=Path.Combine(Path.GetTempPath(),"roche-home-recovery-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"match.json");
        try
        {
            var state=new GameState{MatchState=new MatchManager().ToState()};string backup=GameStateService.SerializeRecoverySnapshot(state);
            File.WriteAllText(path,"{\"Sport\":\"cricket\",\"CricketMatchState\":{\"AllInnings\":null}}");File.WriteAllText(path+".bak",backup);
            Assert.NotNull(RecoverySession.Describe(GameStateService.LoadFrom(path)));Assert.Equal(backup,File.ReadAllText(path+".bak"));
        }
        finally{Directory.Delete(dir,true);}
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    public async Task KeyboardSearchEscapeAndUnavailableCommandsHaveNoScoringEffects(int width,int height)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var window=new Roche_Scoreboard.MainWindow();window.Show();window.Width=width;window.Height=height;
        try
        {
            await Settle(300);window.Width=width;window.Height=height;await Settle();
            Assert.InRange(window.Bounds.Width,width-1,width+1);Assert.InRange(window.Bounds.Height,height-1,height+1);
            AflSession(window);await Settle();var match=Field<MatchManager>(window,"_match");
            var original=window.FindControl<Button>("HomeGoalButton")!;Assert.True(original.Focus());
            string before=JsonSerializer.Serialize(match.ToState());Key(window,PhysicalKey.K,RawInputModifiers.Control);await Settle();
            Assert.True(window.FindControl<Grid>("CommandOverlay")!.IsVisible);Assert.False(window.OpenCommandBar());
            var search=window.FindControl<TextBox>("CommandSearch")!;Assert.True(search.IsFocused);
            search.Text="presentation";Key(window,PhysicalKey.A);await Settle();
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.Equal(2,window.FindControl<ListBox>("CommandResults")!.Items.Count);
            Capture(window,$"commands-{width}x{height}");
            search.Text="this does not exist";await Settle();Assert.Empty(window.FindControl<ListBox>("CommandResults")!.Items);
            Key(window,PhysicalKey.Enter);Assert.True(window.FindControl<Grid>("CommandOverlay")!.IsVisible);
            Key(window,PhysicalKey.Escape);Assert.False(window.FindControl<Grid>("CommandOverlay")!.IsVisible);Assert.True(original.IsFocused);
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
            Assert.True(window.OpenCommandBar());search.Text="match controls";await Settle();Key(window,PhysicalKey.Enter);
            Assert.False(window.FindControl<Grid>("CommandOverlay")!.IsVisible);Assert.False(window.ExecuteCommand("afl-match"));
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task HomeReturnPreservesCurrentStateAndDoesNotEnableOutputOrHiddenScoring()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();AflSession(window);var match=Field<MatchManager>(window,"_match");match.AddGoal(TeamSide.Away);
            string before=JsonSerializer.Serialize(match.ToState());Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("home"));
            var home=window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;
            Assert.True(home.IsVisible);Assert.Equal("Return to current session",home.FindControl<Button>("HomeResumeButton")!.Content);
            match.AddBehind(TeamSide.Away);before=JsonSerializer.Serialize(match.ToState());Assert.Contains("0–7",home.FindControl<TextBlock>("SessionDetail")!.Text);
            Key(window,PhysicalKey.G);Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
            Click(home.FindControl<Button>("HomeResumeButton")!);Assert.False(home.IsVisible);Assert.True(window.FindControl<Control>("MainContent")!.IsVisible);
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.True(Field<bool>(window,"_outputActivationRequired"));
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task PendingDecisionsAndScorecardDraftsCannotBeBypassedThroughCommands()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();var panel=window.FindControl<CricketControlPanel>("CricketControlPanelView")!;
            var match=CricketReliabilityChecks.CreateMatch(true);panel.SetMatch(match);Set(window,"_cricketMatch",match);Set(window,"_sportMode",SportMode.Cricket);
            window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;panel.IsVisible=true;
            panel.FindControl<Control>("BatterSelectionOverlay")!.IsVisible=true;Assert.False(window.OpenCommandBar());
            panel.FindControl<Control>("BatterSelectionOverlay")!.IsVisible=false;panel.FindControl<Control>("BowlerSelectionOverlay")!.IsVisible=false;panel.FindControl<Control>("TossOverlay")!.IsVisible=false;
            Set(panel,"_scorecardDirty",true);string before=JsonSerializer.Serialize(match.CapturePersistenceState());
            Assert.True(window.OpenCommandBar());Assert.False(window.ExecuteCommand("home"));Assert.False(window.ExecuteCommand("settings"));Assert.False(window.ExecuteCommand("bowler"));
            Assert.Contains("Apply or cancel",window.FindControl<TextBlock>("CommandHint")!.Text);
            Assert.Equal(before,JsonSerializer.Serialize(match.CapturePersistenceState()));Key(window,PhysicalKey.Escape);
            Set(panel,"_scorecardDirty",false);Assert.True(window.OpenCommandBar());Set(panel,"_workflowBusy",true);
            Assert.False(window.ExecuteCommand("scorecard"));Assert.False(window.FindControl<Grid>("CommandOverlay")!.IsVisible);
            Set(panel,"_workflowBusy",false);panel.ClearMatch();
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RestoredMatchesStayOffAirUntilExplicitOutputActivation(bool cricket)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();var saved=cricket?new GameState{Sport="cricket",CricketMatchState=CricketReliabilityChecks.CreateMatch(true).CapturePersistenceState()}
                :new GameState{MatchState=new MatchManager().ToState(),WeatherLocation=""};
            Invoke(window,"RestoreFromSavedState",saved);await Settle(200);
            var output=Field<NativeWindow>(window,cricket?"_cricketDisplayWindow":"_displayWindow");Assert.NotNull(output);Assert.False(output.IsVisible);
            if(cricket)Assert.Contains("OUTPUT OFF",window.FindControl<CricketControlPanel>("CricketControlPanelView")!.FindControl<TextBlock>("CricketOnAirText")!.Text);
            if(!cricket)Invoke(window,"SwitchToScreensaver_Click",window,new RoutedEventArgs());
            Assert.False(output.IsVisible);Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("enable-output"));Assert.True(output.IsVisible);
            Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("settings"));Assert.True(output.IsVisible);
            Invoke(window,"OnGlobalSettingsBackRequested");Assert.True(output.IsVisible);
            if(cricket)Assert.StartsWith("ON AIR",window.FindControl<CricketControlPanel>("CricketControlPanelView")!.FindControl<TextBlock>("CricketOnAirText")!.Text);
        }
        finally{foreach(var child in window.OwnedWindows.ToArray())child.Close();Field<NativeWindow?>(window,cricket?"_cricketDisplayWindow":"_displayWindow")?.Close();window.Close();app.IsSelfTestMode=previous;}
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RepeatedLiveSearchKeepsSelectionStableAndSupportsBothShortcutModifiers(bool commandKey)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();AflSession(window);await Settle();
            var match=Field<MatchManager>(window,"_match");string before=JsonSerializer.Serialize(match.ToState());
            for(int repeat=0;repeat<6;repeat++)
            {
                Key(window,PhysicalKey.K,commandKey?RawInputModifiers.Meta:RawInputModifiers.Control);await Settle();
                Assert.True(window.FindControl<Grid>("CommandOverlay")!.IsVisible);
                var search=window.FindControl<TextBox>("CommandSearch")!;var results=window.FindControl<ListBox>("CommandResults")!;
                search.Text="output";await Settle();Assert.Equal(2,results.Items.Count);results.SelectedIndex=1;
                search.Text="out";await Settle();Assert.Equal(1,results.SelectedIndex);
                search.Text="zzzzzz";await Settle();Assert.Empty(results.Items);
                search.Text="sound";await Settle();Assert.NotEmpty(results.Items);
                Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Key(window,PhysicalKey.Escape);
                Assert.False(window.FindControl<Grid>("CommandOverlay")!.IsVisible);Assert.True(window.FindControl<Control>("MainContent")!.IsEnabled);
            }
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task CommandArrivalUsesSharedMotionAndRemainsUsableUnderInterruptions(bool animate)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        bool? oldMotion=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=animate;
        bool glass=OperatorAppearance.GlassEnabled,reduce=OperatorAppearance.ReduceMotion;
        var window=new Roche_Scoreboard.MainWindow();window.Show();window.Width=640;window.Height=480;
        try
        {
            await Settle(300);window.Width=640;window.Height=480;await Settle();
            Assert.InRange(window.Bounds.Width,639,641);Assert.True(window.OpenCommandBar());
            if(RecoverySession.Describe(GameStateService.Load()) is null)
                Assert.Contains("Start Game",((Control)((ListBoxItem)window.FindControl<ListBox>("CommandResults")!.Items[0]!).Content!).GetVisualDescendants().OfType<TextBlock>().First().Text);
            for(int frame=0;frame<8;frame++){await Settle(55);Capture(window,$"command-arrival-{animate}-{frame:D2}");}
            var surface=window.FindControl<Control>("CommandSurface")!;
            if(!animate)Assert.Equal(1,surface.Opacity);
            Key(window,PhysicalKey.Escape);Assert.True(window.OpenCommandBar());await Settle(25);
            OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(glass,true);await Settle();Assert.Equal(1,surface.Opacity);
            Key(window,PhysicalKey.Escape);Assert.True(window.OpenCommandBar());Key(window,PhysicalKey.Escape);
            Assert.True(window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsEnabled);
        }
        finally{window.Close();OperatorAppearance.Configure(glass,reduce);OperatorMotion.AnimationOverride=oldMotion;app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task RecoveryPromptCannotDuplicateAndDeferringPreservesTheValidatedSave()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            var saved=new GameState{MatchState=new MatchManager().ToState()};GameStateService.Save(saved);
            string before=JsonSerializer.Serialize(GameStateService.Load());await Settle();
            var home=window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;home.PrepareForDisplay();
            Click(home.FindControl<Button>("HomeResumeButton")!);Invoke(window,"ResumeFromHome");await Settle();
            var prompt=Assert.Single(window.GetVisualDescendants().OfType<ResumeMatchPrompt>());
            Assert.False(window.OpenCommandBar());Click(prompt.FindControl<Button>("LaterButton")!);await Settle(250);
            Assert.Equal(before,JsonSerializer.Serialize(GameStateService.Load()));Assert.True(home.IsEnabled);Assert.True(home.FindControl<Button>("HomeResumeButton")!.IsVisible);
            Click(home.FindControl<Button>("HomeResumeButton")!);await Settle();prompt=Assert.Single(window.GetVisualDescendants().OfType<ResumeMatchPrompt>());
            Click(prompt.FindControl<Button>("DiscardButton")!);Assert.NotNull(GameStateService.Load());Click(prompt.FindControl<Button>("DiscardButton")!);await Settle(250);
            Assert.Null(GameStateService.Load());Assert.False(home.FindControl<Button>("HomeResumeButton")!.IsVisible);
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task SettingsRoundTripPreservesCricketIdentityAndCurrentSurface()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();var panel=window.FindControl<CricketControlPanel>("CricketControlPanelView")!;
            var match=CricketReliabilityChecks.CreateMatch(true);panel.SetMatch(match);Set(window,"_cricketMatch",match);Set(window,"_sportMode",SportMode.Cricket);
            window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;panel.IsVisible=true;
            foreach(string name in new[]{"TossOverlay","BatterSelectionOverlay","BowlerSelectionOverlay"})panel.FindControl<Control>(name)!.IsVisible=false;
            string before=JsonSerializer.Serialize(match.CapturePersistenceState());Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("settings"));
            Assert.Equal(SportMode.Cricket,Field<SportMode>(window,"_sportMode"));
            Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("home"));
            var home=window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;Assert.Contains("cricket",home.FindControl<TextBlock>("SessionDetail")!.Text);
            Click(home.FindControl<Button>("HomeResumeButton")!);Assert.True(panel.IsVisible);Assert.False(window.FindControl<Control>("GlobalSettingsPanel")!.IsVisible);
            Assert.Equal(before,JsonSerializer.Serialize(match.CapturePersistenceState()));panel.ClearMatch();
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task OpeningUnifiedSetupDoesNotReplaceRecoveryBeforeCompletion()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();GameStateService.Save(new GameState{MatchState=new MatchManager().ToState()});
            string before=JsonSerializer.Serialize(GameStateService.Load());
            Assert.True(window.OpenCommandBar());Assert.True(window.ExecuteCommand("start-game"));
            Assert.Empty(window.OwnedWindows);Assert.Equal(before,JsonSerializer.Serialize(GameStateService.Load()));
            var wizard=window.FindControl<SetupWizard>("SetupWizardPanel")!;Assert.True(wizard.FindControl<Control>("SportStepContainer")!.IsVisible);
            Click(wizard.FindControl<Button>("CancelSetupButton")!);
            Assert.True(window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible);
            Assert.Equal(before,JsonSerializer.Serialize(GameStateService.Load()));
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task CommandNavigationCannotDiscardAnUnfinishedSetupForm()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();Invoke(window,"OnSportSelected",SportMode.AFL);await Settle();
            var wizard=window.FindControl<SetupWizard>("SetupWizardPanel")!;Click(wizard.FindControl<Button>("NextButton")!);await Settle();
            var name=wizard.FindControl<TextBox>("SetupHomeNameBox")!;name.Text="Unfinished club name";
            Assert.True(window.OpenCommandBar());Assert.False(window.ExecuteCommand("home"));Assert.False(window.ExecuteCommand("settings"));
            Assert.Contains("Finish setup",window.FindControl<TextBlock>("CommandHint")!.Text);Key(window,PhysicalKey.Escape);
            Assert.True(wizard.IsVisible);Assert.Equal("Unfinished club name",name.Text);
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task WizardBackPreservesRecoveryAndTheRetainedCurrentMatch(bool current)
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();var match=Field<MatchManager>(window,"_match");match.AddGoal(TeamSide.Home);
            GameStateService.Save(new GameState{MatchState=match.ToState()});string saved=JsonSerializer.Serialize(GameStateService.Load());
            string live=JsonSerializer.Serialize(match.ToState());
            if(current){AflSession(window);await Settle();Invoke(window,"GoHome");}
            var home=window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;
            Invoke(window,"BeginUnifiedSetup");var wizard=window.FindControl<SetupWizard>("SetupWizardPanel")!;
            Click(wizard.FindControl<Button>("NextButton")!);Click(wizard.FindControl<Button>("BackButton")!);
            Assert.True(wizard.FindControl<Control>("SportStepContainer")!.IsVisible);Assert.False(home.IsVisible);
            Click(wizard.FindControl<Button>("CancelSetupButton")!);await Settle();
            Assert.Equal(saved,JsonSerializer.Serialize(GameStateService.Load()));Assert.Equal(live,JsonSerializer.Serialize(match.ToState()));
            Assert.True(home.IsVisible);Assert.True(home.FindControl<Button>("HomeResumeButton")!.IsVisible);
            if(current)
            {
                Assert.True(Field<bool>(window,"_aflMatchReady"));Click(home.FindControl<Button>("HomeResumeButton")!);
                Assert.True(window.FindControl<Control>("MainContent")!.IsVisible);Assert.False(window.FindControl<SetupWizard>("SetupWizardPanel")!.IsVisible);
            }
            else Assert.Equal("Resume saved match",home.FindControl<Button>("HomeResumeButton")!.Content);
        }
        finally{window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaFact]
    public async Task CommittingReplacementRetiresThePreviousModesTimersAndBindings()
    {
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        try
        {
            await Settle();Invoke(window,"EnterTrainingMode");await Settle();
            var timer=Field<DispatcherTimer>(window,"_trainingTickTimer");Assert.True(timer.IsEnabled);
            var match=Field<MatchManager>(window,"_match");match.StartClock();
            var cricket=CricketReliabilityChecks.CreateMatch(true);Set(window,"_cricketMatch",cricket);
            window.FindControl<CricketControlPanel>("CricketControlPanelView")!.SetMatch(cricket);
            Invoke(window,"RetireSessionForReplacement");
            Assert.False(timer.IsEnabled);Assert.False(match.ClockRunning);
            Assert.Null(Field<TrainingSession?>(window,"_trainingSession"));Assert.Null(Field<CricketMatchManager?>(window,"_cricketMatch"));
            Assert.False(Field<bool>(window,"_aflMatchReady"));Assert.True(Field<bool>(window,"_outputActivationRequired"));
            Assert.All(new[]{"MainContent","CricketControlPanelView","TrainingControlPanelView"},name=>Assert.False(window.FindControl<Control>(name)!.IsVisible));
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [Fact]
    public void RetiredWriterCannotOverwriteReplacementAndKeepsLastSavedRecovery()
    {
        string dir=Path.Combine(Path.GetTempPath(),"roche-retired-writer-"+Guid.NewGuid());Directory.CreateDirectory(dir);string path=Path.Combine(dir,"match.json");
        using var prepared=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var writer=new CricketRecoveryWriter(path,beforeCommit:()=>{prepared.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));});
        try
        {
            string original=GameStateService.SerializeRecoverySnapshot(new GameState{MatchState=new MatchManager().ToState()});File.WriteAllText(path,original);
            writer.Queue(new GameState{Sport="cricket",CricketMatchState=CricketReliabilityChecks.CreateMatch(true).CapturePersistenceState()});
            Assert.True(prepared.Wait(TimeSpan.FromSeconds(10)));writer.InvalidatePending();Assert.Equal(original,File.ReadAllText(path));
            var next=new MatchManager();next.AddGoal(TeamSide.Away);string replacement=GameStateService.SerializeRecoverySnapshot(new GameState{MatchState=next.ToState()});File.WriteAllText(path,replacement);
            release.Set();Assert.True(writer.Flush(TimeSpan.FromSeconds(10)));Assert.Equal(replacement,File.ReadAllText(path));Assert.Equal(0,writer.CompletedWrites);
        }
        finally{release.Set();writer.Flush(TimeSpan.FromSeconds(10));Directory.Delete(dir,true);}
    }
}
