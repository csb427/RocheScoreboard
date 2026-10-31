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

public sealed class UnifiedSetupTests
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    private static object? Invoke(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,args);
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Choose(RadioButton button){button.IsChecked=true;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void Key(Window window,PhysicalKey key){window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-9-captures";Directory.CreateDirectory(dir);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private sealed class Fixture:IDisposable
    {
        private readonly App app=(App)Application.Current!;private readonly bool previous;
        internal Roche_Scoreboard.MainWindow Window {get;}
        internal SetupWizard Afl=>Window.FindControl<SetupWizard>("SetupWizardPanel")!;
        internal CricketSetupWizard Cricket=>Window.FindControl<CricketSetupWizard>("CricketSetupWizardPanel")!;
        internal SportSelectionScreen Home=>Window.FindControl<SportSelectionScreen>("SportSelectionPanel")!;
        internal Fixture(){previous=app.IsSelfTestMode;app.IsSelfTestMode=true;GameStateService.Clear();Window=new();Window.Show();}
        internal async Task Ready(int width=1000,int height=760){await Settle(300);Window.Width=width;Window.Height=height;await Settle();}
        internal void Start()=>Click(Home.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="AflLaunchAction"));
        internal void Continue(bool cricket)
        {
            Choose(Afl.FindControl<RadioButton>(cricket?"CricketSportChoice":"AflSportChoice")!);
            Click(Afl.FindControl<Button>("NextButton")!);
        }
        public void Dispose(){foreach(var owned in Window.OwnedWindows.ToArray())owned.Close();Window.Close();GameStateService.Clear();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public void WizardCompletionIsAtMostOnceUntilReset(bool cricket)
    {
        UserControl wizard=cricket?new CricketSetupWizard():new SetupWizard();int completed=0;
        if(wizard is CricketSetupWizard c){c.Reset();c.SetupCompleted+=_=>completed++;}
        else {var a=(SetupWizard)wizard;a.Reset();a.SetupCompleted+=_=>completed++;}
        Invoke(wizard,"FinishWizard");Invoke(wizard,"FinishWizard");Assert.Equal(1,completed);
    }
    [AvaloniaFact]
    public void InvalidAflClockCannotSilentlyBecomeTheDefaultAndPersist()
    {
        var wizard=new SetupWizard();wizard.Reset();int completed=0;wizard.SetupCompleted+=_=>completed++;
        string before=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        wizard.FindControl<TextBox>("SetupQuarterMinutes")!.Text="oops";
        Invoke(wizard,"FinishWizard");Assert.Equal(0,completed);Assert.Null(wizard.Result);
        Assert.Equal(before,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
    }
    [AvaloniaTheory]
    [InlineData(1280,850,false)][InlineData(640,480,true)][InlineData(420,320,false)][InlineData(420,320,true)]
    public async Task OneHomeStartEntersWizardSportStepWithReachableChoicesAndCancel(int width,int height,bool cricket)
    {
        using var f=new Fixture();await f.Ready(width,height);
        Assert.True(f.Home.FindControl<Control>("AflCard")!.IsVisible);Assert.True(f.Home.FindControl<Control>("TrainingCard")!.IsVisible);
        Assert.False(f.Home.FindControl<Control>("CricketCard")!.IsVisible);Capture(f.Window,$"single-home-{width}x{height}");
        f.Start();await Settle(450);Assert.False(f.Home.IsVisible);
        Assert.True(f.Afl.FindControl<Control>("SportStepContainer")!.IsVisible);
        Assert.False(f.Afl.FindControl<Control>("TeamsStepContainer")!.IsVisible);
        Assert.True(f.Afl.FindControl<RadioButton>("SportStepNavigation")!.IsChecked);
        Assert.False(f.Afl.FindControl<RadioButton>("Step1Navigation")!.IsEnabled);
        Choose(f.Afl.FindControl<RadioButton>(cricket?"CricketSportChoice":"AflSportChoice")!);await Settle();
        Assert.Equal(cricket?"1 of 5":"1 of 4",f.Afl.FindControl<TextBlock>("WizardStepCounter")!.Text);
        Assert.Equal(cricket?"Players":"Clock",f.Afl.FindControl<TextBlock>("Step2Label")!.Text);
        Assert.Equal(cricket,f.Afl.FindControl<Control>("SportOptionsPreview")!.IsVisible);
        Capture(f.Window,$"sport-step-{cricket}-{width}x{height}");
        foreach(string name in new[]{"AflSportChoice","CricketSportChoice","NextButton","CancelSetupButton"})
        {
            var target=f.Afl.FindControl<Control>(name)!;target.Focus();target.BringIntoView();await Settle();
            var position=target.TranslatePoint(default,f.Window)!.Value;
            Assert.True(new Rect(f.Window.Bounds.Size).Intersects(new Rect(position,target.Bounds.Size)),name+" must be reachable");
        }
        f.Continue(cricket);await Settle(350);
        Control wizard=cricket?f.Cricket:f.Afl;
        Assert.True(wizard.FindControl<Control>("TeamsStepContainer")!.IsVisible);
        Assert.Equal(cricket?"2 of 5":"2 of 4",wizard.FindControl<TextBlock>("WizardStepCounter")!.Text);
        Capture(f.Window,$"unified-teams-{cricket}-{width}x{height}");
        Click(wizard.FindControl<Button>("BackButton")!);await Settle();
        Assert.False(f.Home.IsVisible);Assert.True(f.Afl.FindControl<Control>("SportStepContainer")!.IsVisible);
        Click(f.Afl.FindControl<Button>("CancelSetupButton")!);await Settle();Assert.True(f.Home.IsVisible);
        Assert.False(f.Afl.IsVisible);Assert.False(f.Cricket.IsVisible);
    }
    [AvaloniaFact]
    public async Task SportSwitchRetainsSeparateDraftsWithoutChangingCurrentMatchOrRecovery()
    {
        using var f=new Fixture();await f.Ready();
        var match=Field<MatchManager>(f.Window,"_match");match.AddGoal(TeamSide.Home);Set(f.Window,"_aflMatchReady",true);
        f.Home.IsVisible=false;f.Window.FindControl<Control>("MainContent")!.IsVisible=true;Invoke(f.Window,"GoHome");
        GameStateService.Save(new GameState{MatchState=match.ToState()});string saved=JsonSerializer.Serialize(GameStateService.Load());string live=JsonSerializer.Serialize(match.ToState());
        f.Start();f.Continue(false);await Settle();f.Afl.FindControl<TextBox>("SetupHomeNameBox")!.Text="Uncommitted AFL draft 993";
        Click(f.Afl.FindControl<Button>("BackButton")!);f.Continue(true);await Settle();
        Assert.NotEqual("Uncommitted AFL draft 993",f.Cricket.FindControl<TextBox>("SetupHomeNameBox")!.Text);
        f.Cricket.FindControl<TextBox>("SetupHomeNameBox")!.Text="Uncommitted cricket draft 994";
        Click(f.Cricket.FindControl<Button>("BackButton")!);f.Continue(false);await Settle();
        Assert.Equal("Uncommitted AFL draft 993",f.Afl.FindControl<TextBox>("SetupHomeNameBox")!.Text);
        Assert.Equal(saved,JsonSerializer.Serialize(GameStateService.Load()));Assert.Equal(live,JsonSerializer.Serialize(match.ToState()));
        Click(f.Afl.FindControl<Button>("CancelSetupButton")!);await Settle();
        Assert.True(f.Home.IsVisible);Assert.True(Field<bool>(f.Window,"_aflMatchReady"));Assert.True(Field<bool>(f.Window,"_outputActivationRequired"));
        Assert.Equal(saved,JsonSerializer.Serialize(GameStateService.Load()));Assert.Equal(live,JsonSerializer.Serialize(match.ToState()));
        Click(f.Home.FindControl<Button>("HomeResumeButton")!);Assert.True(f.Window.FindControl<Control>("MainContent")!.IsVisible);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task CanceledOrStaleSetupCannotCommitOrPersistDefaults(bool cricket)
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(cricket);await Settle();
        UserControl wizard=cricket?f.Cricket:f.Afl;
        string before=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        Click(wizard.FindControl<Button>("CancelSetupButton")!);await Settle();
        Invoke(wizard,"FinishWizard");Assert.Equal(before,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
        Assert.Null(cricket?f.Cricket.Result:f.Afl.Result);Assert.True(f.Home.IsVisible);Assert.False(Field<bool>(f.Window,"_aflMatchReady"));Assert.Null(Field<CricketMatchManager?>(f.Window,"_cricketMatch"));
    }
    [AvaloniaTheory]
    [InlineData("0","30")][InlineData("61","0")][InlineData("20","60")][InlineData("20","oops")][InlineData("-5","0")]
    public void AflClockValidationRejectsOutOfRangeValuesBeforePersistence(string minutes,string seconds)
    {
        var wizard=new SetupWizard();wizard.Reset();string before=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        wizard.FindControl<TextBox>("SetupQuarterMinutes")!.Text=minutes;wizard.FindControl<TextBox>("SetupQuarterSeconds")!.Text=seconds;
        Invoke(wizard,"FinishWizard");Assert.Null(wizard.Result);Assert.Equal(before,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
        Assert.True(wizard.FindControl<TextBlock>("SetupValidationMessage")!.IsVisible);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task EmptyNewMatchCompletesOnceThroughItsActualWizardControls(bool cricket)
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(cricket);await Settle();
        UserControl wizard=cricket?f.Cricket:f.Afl;
        wizard.FindControl<ComboBox>("SetupWeatherLocation")!.Text="";
        if(cricket)
        {((System.Collections.IList)Field<object>(wizard,"_homePlayers")).Clear();((System.Collections.IList)Field<object>(wizard,"_awayPlayers")).Clear();}
        for(int step=1;step<=(cricket?4:3);step++) {Click(wizard.FindControl<Button>("NextButton")!);await Settle(120);}
        await Settle(600);Assert.False(f.Home.IsVisible);Assert.False(Field<bool>(f.Window,"_setupFlowActive"));
        if(cricket)
        {
            var match=Field<CricketMatchManager>(f.Window,"_cricketMatch");Assert.NotNull(match);Invoke(wizard,"FinishWizard");Assert.Same(match,Field<CricketMatchManager>(f.Window,"_cricketMatch"));
        }
        else
        {
            var match=Field<MatchManager>(f.Window,"_match");Assert.True(Field<bool>(f.Window,"_aflMatchReady"));match.AddGoal(TeamSide.Home);
            Invoke(wizard,"FinishWizard");Assert.Equal(6,match.HomeTotal);
        }
    }
    [AvaloniaFact]
    public async Task ExistingCricketCanBeReplacedOnlyAfterFinalConfirmation()
    {
        using var f=new Fixture();await f.Ready();var old=CricketReliabilityChecks.CreateMatch(true);old.RecordDelivery(2,CricketDeliveryType.Runs);
        Set(f.Window,"_cricketMatch",old);Set(f.Window,"_sportMode",SportMode.Cricket);
        var panel=f.Window.FindControl<CricketControlPanel>("CricketControlPanelView")!;panel.SetMatch(old);
        f.Home.IsVisible=false;panel.IsVisible=true;Invoke(f.Window,"GoHome");
        f.Start();f.Continue(true);await Settle();
        ((System.Collections.IList)Field<object>(f.Cricket,"_homePlayers")).Clear();((System.Collections.IList)Field<object>(f.Cricket,"_awayPlayers")).Clear();
        f.Cricket.FindControl<ComboBox>("SetupWeatherLocation")!.Text="";
        for(int step=0;step<3;step++)Click(f.Cricket.FindControl<Button>("NextButton")!);
        string before=JsonSerializer.Serialize(old.CapturePersistenceState());
        foreach(string choice in new[]{"No","Yes"})
        {
            Exception? callbackError=null;bool answered=false;
            Dispatcher.UIThread.Post(()=>
            {
                var dialog=f.Window.OwnedWindows.FirstOrDefault(w=>w.Title=="New session");
                if(dialog is null){callbackError=new InvalidOperationException("Expected the existing-match confirmation.");return;}
                try {var no=dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"No"));Assert.True(no.IsDefault);Click(dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,choice)));answered=true;}
                catch(Exception error){callbackError=error;dialog.Close();}
            });
            Click(f.Cricket.FindControl<Button>("NextButton")!);Assert.Null(callbackError);Assert.True(answered);
            if(choice=="No"){Assert.Same(old,Field<CricketMatchManager>(f.Window,"_cricketMatch"));Assert.Null(f.Cricket.Result);Assert.Equal(before,JsonSerializer.Serialize(old.CapturePersistenceState()));}
        }
        await Settle(500);Assert.NotSame(old,Field<CricketMatchManager>(f.Window,"_cricketMatch"));Assert.False(Field<bool>(f.Window,"_setupFlowActive"));
        Assert.Equal(0,Field<CricketMatchManager>(f.Window,"_cricketMatch").TotalRuns);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task SportSelectionAndRapidBackCancelStayAuthoritativeWithAndWithoutMotion(bool animate)
    {
        bool? previous=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=animate;
        using var f=new Fixture();
        try
        {
            await f.Ready(640,480);f.Start();await Settle(50);
            for(int i=0;i<8;i++)
            {
                Choose(f.Afl.FindControl<RadioButton>(i%2==0?"AflSportChoice":"CricketSportChoice")!);
                Capture(f.Window,$"sport-selection-motion-{animate}-{i:D2}");await Settle(50);
            }
            Assert.True(f.Afl.FindControl<RadioButton>("CricketSportChoice")!.IsChecked);Assert.False(f.Afl.FindControl<RadioButton>("AflSportChoice")!.IsChecked);
            if(!animate)Assert.Equal(1,OperatorChoiceMotion.GetProgress(f.Afl.FindControl<RadioButton>("CricketSportChoice")!));
            f.Continue(true);Click(f.Cricket.FindControl<Button>("BackButton")!);Click(f.Afl.FindControl<Button>("CancelSetupButton")!);
            f.Start();await Settle(600);
            Assert.True(f.Afl.FindControl<Control>("SportStepContainer")!.IsVisible);Assert.False(f.Cricket.IsVisible);Assert.False(f.Home.IsVisible);
            Assert.Null(Field<CricketMatchManager?>(f.Window,"_cricketMatch"));
        }
        finally{OperatorMotion.AnimationOverride=previous;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task EscapeFromOrdinaryTeamEditingCancelsToTheSingleHome(bool cricket)
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(cricket);await Settle();
        UserControl wizard=cricket?f.Cricket:f.Afl;var field=wizard.FindControl<TextBox>("SetupHomeNameBox")!;field.Focus();field.BringIntoView();await Settle();
        Key(f.Window,PhysicalKey.Escape);await Settle();Assert.True(f.Home.IsVisible);Assert.False(f.Afl.IsVisible);Assert.False(f.Cricket.IsVisible);
        Assert.False(Field<bool>(f.Window,"_setupFlowActive"));
    }
    [AvaloniaFact]
    public async Task EscapeFirstClosesAnOpenWizardComboWithoutCancelingSetup()
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(false);await Settle();
        Click(f.Afl.FindControl<Button>("NextButton")!);Click(f.Afl.FindControl<Button>("NextButton")!);await Settle();
        var combo=f.Afl.FindControl<ComboBox>("SetupWeatherLocation")!;combo.Focus();combo.BringIntoView();combo.IsDropDownOpen=true;await Settle();
        Key(f.Window,PhysicalKey.Escape);await Settle();Assert.False(combo.IsDropDownOpen);Assert.True(f.Afl.IsVisible);Assert.False(f.Home.IsVisible);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task InterruptedFinalConfirmationInvalidatesTheRetainedYesResultAndDefaults(bool sportRoundTrip)
    {
        using var f=new Fixture();await f.Ready();GameStateService.Save(new GameState{MatchState=new MatchManager().ToState()});
        f.Start();f.Continue(false);await Settle();Click(f.Afl.FindControl<Button>("NextButton")!);Click(f.Afl.FindControl<Button>("NextButton")!);
        f.Afl.FindControl<TextBox>("SetupHomeNameBox")!.Text="Never persist this interrupted draft";
        string saved=JsonSerializer.Serialize(GameStateService.Load()),prefs=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
        Exception? callbackError=null;bool answered=false;
        Dispatcher.UIThread.Post(()=>
        {
            var dialog=f.Window.OwnedWindows.FirstOrDefault(w=>w.Title=="New session");
            if(dialog is null){callbackError=new InvalidOperationException("Missing replacement confirmation.");return;}
            try
            {
                if(sportRoundTrip){Invoke(f.Window,"ReturnToSportChoice");f.Continue(false);}
                else Invoke(f.Window,"CancelUnifiedSetup");
                Click(dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Yes")));answered=true;
            }
            catch(Exception error){callbackError=error;dialog.Close();}
        });
        Click(f.Afl.FindControl<Button>("NextButton")!);Assert.Null(callbackError);Assert.True(answered);await Settle();
        Assert.Null(f.Afl.Result);Assert.Equal(!sportRoundTrip,f.Home.IsVisible);Assert.False(Field<bool>(f.Window,"_aflMatchReady"));
        Assert.Equal(saved,JsonSerializer.Serialize(GameStateService.Load()));Assert.Equal(prefs,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
    }
    [AvaloniaFact]
    public async Task WizardTypingCannotNavigateOrPersistTheHiddenAflTabs()
    {
        using var f=new Fixture();await f.Ready();f.Start();f.Continue(false);await Settle();
        var preferences=Field<object>(f.Window,"_uxPreferences");var guided=preferences.GetType().GetProperty("GuidedOperatorMode")!;
        bool previous=(bool)guided.GetValue(preferences)!;
        try
        {
            guided.SetValue(preferences,false);var tabs=f.Window.FindControl<TabControl>("MainTabControl")!;tabs.SelectedIndex=0;
            var name=f.Afl.FindControl<TextBox>("SetupHomeNameBox")!;name.Focus();name.BringIntoView();await Settle();
            f.Window.KeyPressQwerty(PhysicalKey.Digit2,RawInputModifiers.Control);f.Window.KeyReleaseQwerty(PhysicalKey.Digit2,RawInputModifiers.Control);await Settle();
            Assert.Equal(0,tabs.SelectedIndex);Assert.True(f.Afl.IsVisible);Assert.False(f.Window.FindControl<Control>("MainContent")!.IsVisible);
        }
        finally{guided.SetValue(preferences,previous);}
    }
}
