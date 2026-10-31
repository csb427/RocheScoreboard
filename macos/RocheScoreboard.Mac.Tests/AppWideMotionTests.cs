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

public sealed class AppWideMotionTests
{
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-app-wide";Directory.CreateDirectory(dir);
        using var image=window.CaptureRenderedFrame();Assert.NotNull(image);image.Save(Path.Combine(dir,name+".png"));
    }
    private sealed class Preferences:IDisposable
    {
        private readonly bool glass=OperatorAppearance.GlassEnabled,reduce=OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style=OperatorAppearance.MotionStyle;
        private readonly bool? animation=OperatorMotion.AnimationOverride;
        private readonly byte[]? bytes=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        public Preferences(bool reduced=false)
        {OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,reduced);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);}
        public void Dispose()
        {
            OperatorMotion.AnimationOverride=animation;OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(glass,reduce);
            if(bytes is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,bytes);
        }
    }
    [AvaloniaTheory]
    [InlineData(900,700)][InlineData(420,320)]
    public async Task ExactQuarterDurationIsEditableAndValidationKeepsInvalidEditsOutOfSavedDefaults(int width,int height)
    {
        using var appearance=new Preferences(true);
        string path=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[false])!;
        byte[]? previous=File.Exists(path)?File.ReadAllBytes(path):null;
        var wizard=new SetupWizard();wizard.Reset();var window=new NativeWindow{Width=width,Height=height,Content=wizard};window.Show();
        try
        {
            await Settle(300);wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[2,false]);
            wizard.FindControl<RadioButton>("SetupCountdown")!.IsChecked=true;await Settle(60);
            Capture(window,"duration-before-"+width);
            var minutes=wizard.FindControl<TextBox>("SetupQuarterMinutes")!;var seconds=wizard.FindControl<TextBox>("SetupQuarterSeconds")!;
            Assert.True(minutes.IsEffectivelyVisible);Assert.True(seconds.IsEffectivelyVisible);
            async Task Type(TextBox field,string value)
            {
                field.Focus(NavigationMethod.Tab);field.BringIntoView();await Settle(70);Assert.True(field.IsFocused);
                field.SelectAll();window.KeyTextInput(value);Assert.Equal(value,field.Text);
            }
            string saved=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
            await Type(minutes,"20");await Type(seconds,"30");
            var controls=wizard.FindControl<StackPanel>("QuarterLengthControls")!;
            Assert.Single(controls.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Content,"+")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("21",minutes.Text);Assert.Equal("30",seconds.Text);
            Assert.Single(controls.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Content,"−")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("20",minutes.Text);Assert.Equal("30",seconds.Text);
            Assert.Single(controls.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Content,"20")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("20",minutes.Text);Assert.Equal("00",seconds.Text);
            await Type(seconds,"61");wizard.GetType().GetMethod("FinishWizard",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,null);
            Assert.Null(wizard.Result);Assert.True(seconds.IsFocused);Assert.Equal(saved,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
            await Type(seconds,"30");Capture(window,"duration-after-"+width);
            wizard.GetType().GetMethod("FinishWizard",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,null);
            Assert.NotNull(wizard.Result);Assert.Equal(20,wizard.Result.QuarterMinutes);Assert.Equal(30,wizard.Result.QuarterSeconds);
            var persisted=OperatorWorkflowPreferences.Load();Assert.Equal(20,persisted.AflQuarterMinutes);Assert.Equal(30,persisted.AflQuarterSeconds);
        }
        finally{window.Close();if(previous is null)File.Delete(path);else File.WriteAllBytes(path,previous);}
    }
    [AvaloniaTheory]
    [InlineData(900,700,false)][InlineData(420,320,true)]
    public async Task AflClockChoicesUseTheOriginalRadioStateWithReachableKeyboardAndIndependentHands(int width,int height,bool reduced)
    {
        using var preferences=new Preferences(reduced);var wizard=new SetupWizard();wizard.Reset();
        var window=new NativeWindow{Width=width,Height=height,Content=wizard};window.Show();
        try
        {
            await Settle(350);wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[2,false]);
            await Settle(100);Capture(window,$"clock-choice-before-{width}");
            var up=wizard.FindControl<RadioButton>("SetupCountUp")!;var down=wizard.FindControl<RadioButton>("SetupCountdown")!;
            Assert.True(up.IsEffectivelyVisible);Assert.True(down.IsEffectivelyVisible);
            Assert.Same(wizard.FindControl<OperatorGlassSurface>("CountUpCard"),up.Parent);
            string saved=JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
            down.IsChecked=true;await Settle(450);int captured=0;
            foreach(var choice in new[]{up,down,up,down})
            {
                choice.Focus(NavigationMethod.Tab);choice.BringIntoView();await Settle(50);Assert.True(choice.IsFocused);
                window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);
                Assert.Equal(choice==up,up.IsChecked);Assert.Equal(choice==down,down.IsChecked);
                var icon=wizard.FindControl<OperatorSymbol>(choice==up?"CountUpActionSymbol":"CountdownActionSymbol")!;
                Assert.Equal(choice==up?"clock-up":"clock-down",icon.Kind);Assert.True(icon.IsActive);
                Assert.Null(icon.RenderTransform);
                Assert.Equal(.12,Assert.Single(choice.GetVisualDescendants().OfType<OperatorChoiceSurface>()).OriginX);
                if(!reduced && captured++<2)
                {
                    Assert.True(icon.IsAnimating);var position=icon.TranslatePoint(default,choice);
                    for(int frame=0;frame<6;frame++){Capture(window,$"clock-part-{icon.Kind}-{frame:D2}");await Settle(60);}
                    Assert.Equal(position,icon.TranslatePoint(default,choice));
                }
            }
            Assert.Equal(saved,JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
            OperatorAppearance.Configure(true,true);await Settle(80);Capture(window,$"clock-choice-after-{width}");
            foreach(var choice in new[]{up,down})Assert.Equal(choice.IsChecked==true?1:0,OperatorChoiceMotion.GetProgress(choice));
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false,320)][InlineData(false,420)][InlineData(true,320)][InlineData(true,420)]
    public async Task CompactWizardNavigationKeepsTheWholeActionLabelVisible(bool cricket,int width)
    {
        using var preferences=new Preferences(true);UserControl wizard=cricket?new CricketSetupWizard():new SetupWizard();
        wizard.GetType().GetMethod("Reset")!.Invoke(wizard,null);
        wizard.GetType().GetMethod("EnableUnifiedSetup",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,null);
        var window=new NativeWindow{Width=width,Height=320,Content=wizard};window.Show();
        try
        {
            await Settle(300);Capture(window,$"wizard-nav-before-{cricket}-{width}");
            var back=wizard.FindControl<Button>("BackButton")!;
            var label=Assert.Single(back.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="Back");
            Assert.True(label.Bounds.Width>=label.TextLayout.Width,$"Back label needs {label.TextLayout.Width}px; has {label.Bounds.Width}px");
            foreach(string name in new[]{"BackButton","NextButton","CancelSetupButton"})
            {
                var action=wizard.FindControl<Button>(name)!;
                var bounds=new Rect(action.TranslatePoint(default,window)!.Value,action.Bounds.Size);
                Assert.True(new Rect(window.Bounds.Size).Contains(bounds),name+" must stay fully reachable");
            }
            wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[cricket?4:3,false]);
            await Settle(60);var finish=wizard.FindControl<TextBlock>("NextButtonText")!;
            Assert.True(finish.Bounds.Width>=finish.TextLayout.Width,$"Start match needs {finish.TextLayout.Width}px; has {finish.Bounds.Width}px");
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task WizardStepMotionSettlesOnPreferenceChangeWithoutAlteringTheDraft(bool cricket)
    {
        using var preferences=new Preferences();UserControl wizard=cricket?new CricketSetupWizard():new SetupWizard();
        wizard.GetType().GetMethod("Reset")!.Invoke(wizard,null);
        var window=new NativeWindow{Width=900,Height=700,Content=wizard};window.Show();
        try
        {
            await Settle(350);var name=wizard.FindControl<TextBox>("SetupHomeNameBox")!;name.Text="Keep this draft";
            wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[2,true]);
            var active=wizard.FindControl<Control>("Step3Panel")!;
            Assert.NotNull(wizard.GetType().GetField("_stepTransitionStoryboard",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard));
            OperatorAppearance.Configure(true,true);
            Assert.Null(wizard.GetType().GetField("_stepTransitionStoryboard",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard));
            Assert.Equal(Matrix.Identity,active.RenderTransform!.Value);Assert.Equal(1,active.Opacity);Assert.True(active.IsVisible);
            await Settle(350);Assert.Equal("Keep this draft",name.Text);Assert.True(active.IsVisible);
            OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
            wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[1,true]);
            var teams=wizard.FindControl<Control>("TeamsStepContainer")!;var shift=Assert.IsType<Avalonia.Media.TranslateTransform>(teams.RenderTransform);
            Motion.SampleAt(shift,TimeSpan.Zero);Assert.Equal(-8*.4,shift.X,4);
            OperatorAppearance.Configure(true,true);Assert.Equal(Matrix.Identity,teams.RenderTransform!.Value);
            Assert.Equal("Keep this draft",name.Text);
            OperatorAppearance.Configure(true,false);
            wizard.GetType().GetMethod("ShowStep",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[2,true]);
            wizard.IsVisible=false;
            Assert.Null(wizard.GetType().GetField("_stepTransitionStoryboard",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard));
            await Settle(350);wizard.IsVisible=true;Assert.True(active.IsVisible);Assert.Equal("Keep this draft",name.Text);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task TeamColourActionsHaveKeyboardSemanticsAndCancelPreservesTheCurrentColour(bool cricket)
    {
        using var preferences=new Preferences();UserControl wizard=cricket?new CricketSetupWizard():new SetupWizard();
        wizard.GetType().GetMethod("Reset")!.Invoke(wizard,null);
        var window=new NativeWindow{Width=cricket?420:900,Height=cricket?320:700,Content=wizard};window.Show();
        try
        {
            await Settle(400);Capture(window,"team-colour-before-"+cricket);
            foreach(string side in new[]{"Home","Away"})foreach(string colour in new[]{"Color","Secondary"})
            {
                var action=Assert.IsType<OperatorActionBorder>(wizard.FindControl<Border>($"Setup{side}{colour}Preview"));
                Assert.True(action.Focusable);Assert.True(action.Bounds.Width>=40);Assert.True(action.Bounds.Height>=40);
                Assert.Contains(side.ToLowerInvariant(),Avalonia.Automation.AutomationProperties.GetName(action)!.ToLowerInvariant());
            }
            var droppers=wizard.GetVisualDescendants().OfType<OperatorActionBorder>().Where(a=>Avalonia.Automation.AutomationProperties.GetName(a)?.EndsWith("from screen")==true).ToArray();
            Assert.Equal(4,droppers.Length);
            foreach(var dropper in droppers)Assert.Equal("eyedropper",Assert.IsType<OperatorSymbol>(dropper.Child).Kind);
            var field=wizard.FindControl<TextBox>("SetupHomeColorBox")!;string before=field.Text!;
            var pick=wizard.FindControl<OperatorActionBorder>("SetupHomeColorPreview")!;
            pick.Focus(NavigationMethod.Tab);pick.BringIntoView();await Settle(120);Assert.True(pick.IsFocused);
            Assert.Equal(new Thickness(2),pick.BorderThickness);Capture(window,"team-colour-focused-"+cricket);
            bool opened=false;
            using var close=DispatcherTimer.RunOnce(()=>
            {
                var dialog=window.OwnedWindows.OfType<ColorPickerWindow>().SingleOrDefault();
                opened=dialog is not null;
                if(dialog is not null){dialog.FindControl<TextBox>("HexBox")!.Text="#123456";dialog.Close(false);}
            },TimeSpan.FromMilliseconds(120));
            window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);
            await Settle(120);Assert.True(opened);Assert.Equal(before,field.Text);
            Assert.Empty(window.OwnedWindows.OfType<ColorPickerWindow>());
            Assert.True(pick.IsFocused);
        }
        finally{foreach(var owned in window.OwnedWindows.ToArray())owned.Close();window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task SportChoiceUsesDistinctIconPartsWithoutMovingLabelsOrDelayingState(bool compact)
    {
        using var preferences=new Preferences();var wizard=new SetupWizard();wizard.Reset();
        typeof(SetupWizard).GetMethod("ShowSportChoice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(wizard,[SportMode.AFL]);
        var window=new NativeWindow{Width=compact?420:900,Height=compact?320:700,Content=wizard};window.Show();
        try
        {
            await Settle(400);Capture(window,"sport-choice-before-"+compact);
            var afl=wizard.FindControl<RadioButton>("AflSportChoice")!;var cricket=wizard.FindControl<RadioButton>("CricketSportChoice")!;
            var aflIcon=wizard.FindControl<OperatorSymbol>("AflSportSymbol")!;var cricketIcon=wizard.FindControl<OperatorSymbol>("CricketSportSymbol")!;
            Assert.Equal("sport-afl",aflIcon.Kind);Assert.Equal("sport-cricket",cricketIcon.Kind);
            Assert.Equal("sport",Assert.Single(wizard.FindControl<RadioButton>("SportStepNavigation")!.GetVisualDescendants().OfType<OperatorSymbol>()).Kind);
            var label=wizard.FindControl<TextBlock>("CricketSportLabel")!;var position=label.TranslatePoint(default,window);
            cricket.IsChecked=true;
            Assert.True(cricketIcon.IsActive);Assert.False(aflIcon.IsActive);
            Assert.Equal("1 of 5",wizard.FindControl<TextBlock>("WizardStepCounter")!.Text);
            Assert.Equal(position,label.TranslatePoint(default,window));
            for(int frame=0;frame<7;frame++){Capture(window,$"sport-choice-cricket-{compact}-{frame:D2}");await Settle(65);}
            foreach(bool chooseCricket in new[]{false,true,false,true,false})
            {
                if(chooseCricket)cricket.IsChecked=true;else afl.IsChecked=true;
                Assert.Equal(chooseCricket?"1 of 5":"1 of 4",wizard.FindControl<TextBlock>("WizardStepCounter")!.Text);
                Assert.Equal(chooseCricket,cricket.IsChecked);Assert.Equal(!chooseCricket,afl.IsChecked);
                await Settle(25);
            }
            OperatorAppearance.Configure(true,true);
            Assert.True(afl.IsChecked);Assert.True(aflIcon.IsActive);Assert.False(cricketIcon.IsActive);
            Assert.False(aflIcon.IsAnimating);Assert.False(cricketIcon.IsAnimating);
            Assert.Null(aflIcon.RenderTransform);Assert.Null(cricketIcon.RenderTransform);
            Assert.Equal("1 of 4",wizard.FindControl<TextBlock>("WizardStepCounter")!.Text);
            await Settle(60); // Let the renderer observe the already-settled reduced-motion state.
            Capture(window,"sport-choice-after-"+compact);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task DisclosureSettlesImmediatelyWhenReducedMotionChangesDuringATransition(bool close)
    {
        using var preferences=new Preferences();
        var home=new SportSelectionScreen();
        var disclosure=new Expander{Header="More changes",Content=new Border{Height=180},
            Theme=(Avalonia.Styling.ControlTheme)home.Resources["ReleaseDisclosure"]!};
        var host=new Grid();host.Children.Add(disclosure);
        var window=new NativeWindow{Width=640,Height=420,Content=host};window.Show();
        try
        {
            await Settle(150);disclosure.IsExpanded=true;
            if(close){await Settle(350);disclosure.IsExpanded=false;}
            await Settle(30);
            var body=Assert.IsAssignableFrom<Control>(NativeTemplates.Find(disclosure,"Body"));
            Assert.False(double.IsNaN(body.Height));
            OperatorAppearance.Configure(true,true);
            Assert.True(double.IsNaN(body.Height));Assert.Equal(!close,body.IsVisible);
            await Settle(350);Assert.Equal(!close,body.IsVisible);
            disclosure.IsExpanded=!close;disclosure.IsExpanded=close;disclosure.IsExpanded=!close;
            Assert.True(double.IsNaN(body.Height));Assert.Equal(!close,body.IsVisible);
            host.Children.Remove(disclosure);OperatorAppearance.Configure(true,false);host.Children.Add(disclosure);
            await Settle(100);disclosure.IsExpanded=close;await Settle(30);
            OperatorAppearance.Configure(true,true);Assert.True(double.IsNaN(body.Height));Assert.Equal(close,body.IsVisible);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RecoveryGrowsFromResumeWithoutChangingSavedStateBeforeAChoice(bool anotherControlTakesFocus)
    {
        using var preferences=new Preferences();
        var match=CricketReliabilityChecks.CreateMatch(true);var saved=new GameState{Sport="cricket",CricketMatchState=match.CapturePersistenceState()};
        string before=JsonSerializer.Serialize(saved);
        var origin=new Button{Content="Resume saved match",Width=190,Height=44,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top,Margin=new Thickness(20)};
        var next=new TextBox{Width=120,Height=44,Margin=new Thickness(230,20,0,0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top};
        var host=new Grid();host.Children.Add(origin);host.Children.Add(next);var window=new NativeWindow{Width=900,Height=700,Content=host};window.Show();
        try
        {
            await Settle(200);origin.Focus();var prompt=new ResumeMatchPrompt(saved);
            typeof(ResumeMatchPrompt).GetMethod("SetExpansionOrigin",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(prompt,[origin]);
            host.Children.Add(prompt);window.UpdateLayout();Dispatcher.UIThread.RunJobs();
            var shell=prompt.FindControl<OperatorGlassSurface>("DialogShell")!;
            if(typeof(ResumeMatchPrompt).GetField("_recoveryScale",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(prompt) is AvaloniaObject scale)
                Motion.SampleAt(scale,TimeSpan.Zero);
            double initial=shell.RenderTransform!.Value.M11;
            Assert.True(prompt.FindControl<OperatorSymbol>("RecoverySymbol")!.IsAnimating);
            Capture(window,"recovery-source");
            Assert.True(initial<.8,"Recovery must expand from the small Resume source, not a 94% generic pop.");
            Assert.Equal(Math.Clamp(origin.Bounds.Width/shell.Bounds.Width,.04,1),initial,5);
            for(int frame=0;frame<8;frame++){Capture(window,$"recovery-open-{frame:D2}");await Settle(55);}
            Assert.Equal(before,JsonSerializer.Serialize(saved));Assert.False(prompt.ResumeChosen);Assert.False(prompt.DiscardChosen);
            var later=prompt.FindControl<Button>("LaterButton")!;later.Focus();int completions=0;prompt.Completed+=()=>{completions++;if(anotherControlTakesFocus)next.Focus();};
            later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(300);
            Assert.Equal(1,completions);Assert.True(prompt.DeferredChosen);Assert.Equal(before,JsonSerializer.Serialize(saved));
            Assert.True(anotherControlTakesFocus?next.IsKeyboardFocusWithin:origin.IsFocused);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(900,700,false)][InlineData(420,320,true)]
    public async Task RecoveryChoiceRemainsReachableWithReducedMotionAndInterruptedEntry(int width,int height,bool reduced)
    {
        using var preferences=new Preferences(reduced);
        var match=CricketReliabilityChecks.CreateMatch(true);var saved=new GameState{Sport="cricket",CricketMatchState=match.CapturePersistenceState()};
        var prompt=new ResumeMatchPrompt(saved);var window=new NativeWindow{Width=width,Height=height,Content=prompt};window.Show();
        try
        {
            await Settle(50);int completions=0;prompt.Completed+=()=>completions++;
            var later=prompt.FindControl<Button>("LaterButton")!;later.Focus(NavigationMethod.Tab);later.BringIntoView();await Settle(100);Capture(window,$"recovery-compact-{width}-{reduced}");
            Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(later.TranslatePoint(default,window)!.Value,later.Bounds.Size)));
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);
            OperatorAppearance.Configure(true,true);await Settle(350);
            Assert.Equal(1,completions);Assert.True(prompt.DeferredChosen);Assert.False(prompt.ResumeChosen);Assert.False(prompt.DiscardChosen);
            Assert.True(prompt.FindControl<OperatorGlassSurface>("DialogShell")!.RenderTransform is null || prompt.FindControl<OperatorGlassSurface>("DialogShell")!.RenderTransform!.Value==Matrix.Identity);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(900,700,false)][InlineData(420,320,true)]
    public async Task ReleaseFiltersKeepARealSelectedStateThroughRapidChangesAndReopening(int width,int height,bool reduced)
    {
        using var preferences=new Preferences(reduced);
        var home=new SportSelectionScreen();var window=new NativeWindow{Width=width,Height=height,Content=home};window.Show();
        try
        {
            await Settle(600);home.FindControl<Button>("ChangelogToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(700);Capture(window,$"release-filter-{width}-before-selection");
            var group=Assert.IsType<OperatorSegmentedPanel>(home.FindControl<Panel>("ReleaseFilters"));
            var all=home.FindControl<RadioButton>("AllReleaseFilter")!;var added=home.FindControl<RadioButton>("NewReleaseFilter")!;var fixedChoice=home.FindControl<RadioButton>("FixedReleaseFilter")!;
            Assert.True(all.IsChecked);Assert.False(added.IsChecked);Assert.False(fixedChoice.IsChecked);
            foreach(var choice in new[]{added,fixedChoice,all,added,fixedChoice})choice.IsChecked=true;
            Assert.Equal("Fixed",typeof(SportSelectionScreen).GetField("_changelogFilter",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home));
            Assert.True(fixedChoice.IsChecked);Assert.False(all.IsChecked);Assert.False(added.IsChecked);
            await Settle(650);Capture(window,$"release-filter-{width}-selected");
            Assert.Equal(fixedChoice.TranslatePoint(default,group)!.Value.X+3,group.VisualShape.Bounds.X,2);
            Assert.True(fixedChoice.Focus(NavigationMethod.Tab));Assert.True(fixedChoice.IsFocused);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.ArrowLeft,RawInputModifiers.None);await Settle(30);
            Assert.True(added.IsChecked);Assert.Equal("New",typeof(SportSelectionScreen).GetField("_changelogFilter",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home));
            fixedChoice.IsChecked=true;
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);await Settle(850);
            Assert.False(home.FindControl<Grid>("ChangelogOverlay")!.IsVisible);Assert.False(group.IsAnimating);
            home.FindControl<Button>("ChangelogToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(700);
            Assert.True(fixedChoice.IsChecked);Assert.Equal("Fixed",typeof(SportSelectionScreen).GetField("_changelogFilter",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home));
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task ReleaseFilterDragCannotCommitAfterItsOwningSheetStartsClosing()
    {
        using var preferences=new Preferences();var home=new SportSelectionScreen();var window=new NativeWindow{Width=900,Height=700,Content=home};window.Show();
        try
        {
            await Settle(550);home.FindControl<Button>("ChangelogToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(700);
            var group=home.FindControl<OperatorSegmentedPanel>("ReleaseFilters")!;var fixedChoice=home.FindControl<RadioButton>("FixedReleaseFilter")!;var all=home.FindControl<RadioButton>("AllReleaseFilter")!;
            fixedChoice.IsChecked=true;await Settle(500);
            Point Center(Control item)=>item.TranslatePoint(new Point(item.Bounds.Width/2,item.Bounds.Height/2),window)!.Value;
            var end=Center(all);window.MouseDown(Center(fixedChoice),MouseButton.Left);window.MouseMove(end,RawInputModifiers.LeftMouseButton);await Settle(30);
            Assert.True(group.IsPreviewingSelection);
            int navigations=0;home.SportSelected+=_=>navigations++;
            typeof(SportSelectionScreen).GetMethod("HideChangelog",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(home,null);
            Assert.False(group.IsPreviewingSelection);
            home.GetVisualDescendants().OfType<Button>().Single(button=>button.Name=="AflLaunchAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(0,navigations);
            window.MouseUp(end,MouseButton.Left);all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(850);
            Assert.Equal("Fixed",typeof(SportSelectionScreen).GetField("_changelogFilter",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home));
            Assert.True(fixedChoice.IsChecked);Assert.False(home.FindControl<Grid>("ChangelogOverlay")!.IsVisible);
        }
        finally{window.Close();}
    }
    private static object Tile(SportSelectionScreen home,string name)
    {
        var map=(System.Collections.IDictionary)typeof(SportSelectionScreen).GetField("_homeTiles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home)!;
        return map[home.FindControl<OperatorGlassSurface>(name)!]!;
    }
    private static double TileTarget(object tile,string field)
    {
        var spring=tile.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile)!;
        return (double)spring.GetType().GetField("Target")!.GetValue(spring)!;
    }
    [AvaloniaFact]
    public async Task StartGamePressInflatesItsArtworkAndNavigationIsImmediate()
    {
        using var preferences=new Preferences();var home=new SportSelectionScreen();var window=new NativeWindow{Width=900,Height=700,Content=home};window.Show();
        try
        {
            await Settle(650);var action=home.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="AflLaunchAction");
            var point=action.TranslatePoint(new Point(action.Bounds.Width/2,action.Bounds.Height/2),window)!.Value;
            window.MouseDown(point,MouseButton.Left);var tile=Tile(home,"AflCard");
            Assert.True((bool)tile.GetType().GetField("_pressed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile)!);
            Assert.True(TileTarget(tile,"_size")>1,"Start Game should inflate its illustrated content on press rather than use the old universal shrink.");
            for(int frame=0;frame<6;frame++){Capture(window,$"start-game-press-{frame:D2}");await Settle(45);}
            int actions=0;home.SportSelected+=_=>actions++;window.MouseUp(point,MouseButton.Left);Assert.Equal(1,actions);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task StartGameMotionRetargetsCalmAndStopsImmediatelyForReducedMotion()
    {
        using var preferences=new Preferences();var home=new SportSelectionScreen();var window=new NativeWindow{Width=900,Height=700,Content=home};window.Show();
        try
        {
            await Settle(650);var tile=Tile(home,"AflCard");tile.GetType().GetMethod("Engage")!.Invoke(tile,[true]);
            double expressive=TileTarget(tile,"_size");OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Calm);
            Assert.Equal(1+(expressive-1)*.4,TileTarget(tile,"_size"),6);
            await Settle(60);OperatorAppearance.Configure(true,true);
            var scale=(Avalonia.Media.ScaleTransform)tile.GetType().GetField("_scale",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile)!;
            var shift=(Avalonia.Media.TranslateTransform)tile.GetType().GetField("_shift",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile)!;
            Assert.Equal(1,scale.ScaleX);Assert.Equal(0,shift.X);Assert.Equal(0,shift.Y);
            Assert.False((bool)tile.GetType().GetField("_running",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile)!);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task TrainingHomePressMovesItsIllustrationWithoutMovingItsLabelOrDelayingLaunch()
    {
        using var preferences=new Preferences();var home=new SportSelectionScreen();var window=new NativeWindow{Width=900,Height=700,Content=home};window.Show();
        try
        {
            await Settle(650);var art=home.GetVisualDescendants().OfType<OperatorSportArtwork>().Single(item=>Equals(item.Sport,"training"));
            var tile=Tile(home,"TrainingCard");var scale=tile.GetType().GetField("_scale",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tile);
            Assert.Contains(Assert.IsType<Avalonia.Media.TransformGroup>(art.RenderTransform).Children,item=>ReferenceEquals(item,scale));
            var label=home.FindControl<StackPanel>("TrainingTextPanel")!;var before=label.TranslatePoint(default,window);
            var action=home.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="TrainingLaunchAction");var point=action.TranslatePoint(new Point(action.Bounds.Width/2,action.Bounds.Height/2),window)!.Value;
            window.MouseDown(point,MouseButton.Left);Assert.Equal(.99,TileTarget(tile,"_size"),6);Assert.True(TileTarget(tile,"_y")>0);
            for(int frame=0;frame<6;frame++){Capture(window,$"training-home-press-{frame:D2}");await Settle(45);}
            Assert.Equal(before,label.TranslatePoint(default,window));
            SportMode? chosen=null;home.SportSelected+=mode=>chosen=mode;window.MouseUp(point,MouseButton.Left);Assert.Equal(SportMode.Training,chosen);
        }
        finally{window.Close();}
    }
}
