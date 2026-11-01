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
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class AppleDesignRefinementTests
{
    private static async Task Settle(int ms=100){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-11";Directory.CreateDirectory(dir);
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    private sealed class AppearanceScope:IDisposable
    {
        private readonly OperatorMotionStyle style=OperatorAppearance.MotionStyle;
        private readonly bool glass=OperatorAppearance.GlassEnabled,reduce=OperatorAppearance.ReduceMotion;
        private readonly bool? animation=OperatorMotion.AnimationOverride;
        private readonly byte[]? bytes=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        public void Dispose()
        {
            OperatorMotion.AnimationOverride=animation;OperatorAppearance.ConfigureMotionStyle(style);OperatorAppearance.Configure(glass,reduce);
            if(bytes is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,bytes);
        }
    }
    private sealed class WindowFilesScope:IDisposable
    {
        private readonly Dictionary<string,byte[]?> files=new();
        internal WindowFilesScope()
        {
            string workflow=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
            foreach(string path in new[]{Path.Combine(SelfTestStorage.DocumentsDirectory,"window_layout.json"),workflow})files[path]=File.Exists(path)?File.ReadAllBytes(path):null;
        }
        public void Dispose()
        {
            foreach(var (path,bytes) in files){if(bytes is null)File.Delete(path);else{Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}}
        }
    }
    [AvaloniaTheory]
    [InlineData(800,700)][InlineData(420,320)]
    public async Task MaterialPreferencesOffersAReachableMotionFeelWithoutAnotherSettingsPage(int width,int height)
    {
        using var appearance=new AppearanceScope();
        var preferences=new OperatorMaterialPreferences();OperatorDesign.SetIsEnabled(preferences,true);
        var scroller=new ScrollViewer{Content=preferences};var window=new NativeWindow{Width=width,Height=height,Content=scroller};window.Show();
        try
        {
            await Settle(600);Capture(window,"material-preferences-"+width);
            var choices=preferences.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.Contains(choices,choice=>Equals(choice.Content,"Expressive"));
            var calm=Assert.Single(choices,choice=>Equals(choice.Content,"Calm"));
            window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);Assert.True(calm.Focus(NavigationMethod.Tab));
            window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);await Settle();
            var rect=new Rect(calm.TranslatePoint(default,window)!.Value,calm.Bounds.Size);
            Assert.True(new Rect(window.Bounds.Size).Contains(rect));
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task MotionChoicePersistsImmediatelyWhilePreviewCannotDelayOrDisableControls()
    {
        using var appearance=new AppearanceScope();OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
        var preferences=new OperatorMaterialPreferences();OperatorDesign.SetIsEnabled(preferences,true);
        var window=new NativeWindow{Width=800,Height=700,Content=preferences};window.Show();
        try
        {
            await Settle(500);double expressive=OperatorMotion.MaterialArrival.TotalMilliseconds;
            var choices=preferences.GetVisualDescendants().OfType<RadioButton>().ToArray();
            var calm=Assert.Single(choices,x=>Equals(x.Content,"Calm"));var full=Assert.Single(choices,x=>Equals(x.Content,"Expressive"));
            calm.IsChecked=true;Assert.Equal(OperatorMotionStyle.Calm,OperatorAppearance.MotionStyle);
            Assert.Equal(325,OperatorMotion.MaterialArrival.TotalMilliseconds);Assert.True(OperatorMotion.MaterialArrival.TotalMilliseconds<expressive);
            Assert.Equal(.4,OperatorMotion.TravelScale);Assert.Equal(1.5,OperatorMotion.SpringResponse);
            using(var saved=JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))Assert.Equal(0,saved.RootElement.GetProperty("MotionStyle").GetInt32());
            var preview=Assert.Single(preferences.GetVisualDescendants().OfType<Border>(),x=>x.Name=="OperatorMotionPreview");
            var button=Assert.Single(preferences.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Content,"Preview motion"));
            for(int i=0;i<5;i++)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            full.IsChecked=true;calm.IsChecked=true;Assert.True(button.IsEnabled);Assert.Equal(OperatorMotionStyle.Calm,OperatorAppearance.MotionStyle);
            for(int frame=0;frame<8;frame++){Capture(window,"motion-preview-"+frame.ToString("D2"));await Settle(45);}
            OperatorAppearance.Configure(true,true);Assert.False(OperatorMotion.Enabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(1,preview.Opacity);
            Assert.True(preview.RenderTransform is null||preview.RenderTransform.Value==Matrix.Identity);
            window.Close();await Settle(400);Assert.True(preview.RenderTransform is null||preview.RenderTransform.Value==Matrix.Identity);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData("{}")] [InlineData("{\"MotionStyle\":999}")]
    public void OlderAndInvalidMotionPreferencesKeepTheExistingExpressiveDefault(string json)
    {
        using var appearance=new AppearanceScope();Directory.CreateDirectory(Path.GetDirectoryName(OperatorAppearance.StoragePath)!);File.WriteAllText(OperatorAppearance.StoragePath,json);
        var loaded=typeof(OperatorAppearance).GetMethod("Load",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
        Assert.Equal(OperatorMotionStyle.Expressive,loaded.GetType().GetProperty("MotionStyle")!.GetValue(loaded));
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ScrubbingPreviewsWithoutChangingTheSettingUntilRelease(bool reduced)
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,reduced);OperatorMotion.AnimationOverride=null;
        var panel=new OperatorSegmentedPanel{Rows=1,Height=52,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};
        var first=new RadioButton{Content="First",GroupName="scrub-test",IsChecked=true};var last=new RadioButton{Content="Last",GroupName="scrub-test"};
        first.SetResourceReference(Control.ThemeProperty,"SettingsSegment");last.SetResourceReference(Control.ThemeProperty,"SettingsSegment");
        panel.Children.Add(first);panel.Children.Add(last);OperatorDesign.SetIsEnabled(panel,true);
        typeof(OperatorSegmentedPanel).GetProperty("AllowScrubbing")?.SetValue(panel,true);
        var window=new NativeWindow{Width=480,Height=180,Content=panel};window.Show();
        try
        {
            await Settle(400);int changes=0;last.IsCheckedChanged+=(_,_)=>{if(last.IsChecked==true)changes++;};
            Point Start()=>first.TranslatePoint(new Point(first.Bounds.Width/2,first.Bounds.Height/2),window)!.Value;
            Point End()=>last.TranslatePoint(new Point(last.Bounds.Width/2,last.Bounds.Height/2),window)!.Value;
            window.MouseDown(Start(),MouseButton.Left);window.MouseMove(End(),RawInputModifiers.LeftMouseButton);await Settle(80);Capture(window,"scrub-preview-"+reduced);
            Assert.True(first.IsChecked);Assert.False(last.IsChecked);Assert.Equal(0,changes);
            Assert.True((bool?)typeof(OperatorSegmentedPanel).GetProperty("IsPreviewingSelection",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel));
            window.MouseUp(End(),MouseButton.Left);await Settle(500);Assert.True(last.IsChecked);Assert.False(first.IsChecked);Assert.Equal(1,changes);
            Capture(window,"scrub-committed-"+reduced);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData("escape")][InlineData("outside")][InlineData("close")][InlineData("disabled")]
    public async Task InterruptedScrubbingLeavesTheSettingAndCommitCountUnchanged(string interruption)
    {
        var panel=new OperatorSegmentedPanel{Rows=1,AllowScrubbing=true,Height=52,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center};
        var first=new RadioButton{Content="First",GroupName="cancel-scrub",IsChecked=true};var last=new RadioButton{Content="Last",GroupName="cancel-scrub"};
        first.SetResourceReference(Control.ThemeProperty,"SettingsSegment");last.SetResourceReference(Control.ThemeProperty,"SettingsSegment");
        panel.Children.Add(first);panel.Children.Add(last);OperatorDesign.SetIsEnabled(panel,true);var window=new NativeWindow{Width=480,Height=180,Content=panel};window.Show();
        try
        {
            await Settle(250);int changes=0;last.IsCheckedChanged+=(_,_)=>{if(last.IsChecked==true)changes++;};
            var start=first.TranslatePoint(new Point(first.Bounds.Width/2,first.Bounds.Height/2),window)!.Value;
            var end=last.TranslatePoint(new Point(last.Bounds.Width/2,last.Bounds.Height/2),window)!.Value;
            window.MouseDown(start,MouseButton.Left);window.MouseMove(end,RawInputModifiers.LeftMouseButton);Assert.True(panel.IsPreviewingSelection);
            if(interruption=="escape"){window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);}
            else if(interruption=="outside")end=new Point(end.X,window.Bounds.Height-1);
            else if(interruption=="close")window.Close();
            else panel.IsEnabled=false;
            if(interruption!="close")window.MouseUp(end,MouseButton.Left);
            await Settle(200);Assert.True(first.IsChecked);Assert.False(last.IsChecked);Assert.Equal(0,changes);Assert.False(panel.IsPreviewingSelection);
        }
        finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(900,700)][InlineData(420,320)]
    public async Task CommandResultsUseMeaningfulSymbolsAndAnAnimatedSelectionSurface(int width,int height)
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,false);OperatorMotion.AnimationOverride=true;
        using var savedFiles=new WindowFilesScope();
        var app=(App)Application.Current!;bool prior=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow{Width=width,Height=height};window.Show();
        try
        {
            await Settle(300);window.Width=width;window.Height=height;await Settle();Assert.Equal(width,window.Bounds.Width);
            Assert.True(window.OpenCommandBar());await Settle(550);Capture(window,"command-results-"+width);
            window.FindControl<TextBox>("CommandSearch")!.Text="start game";
            await Settle();
            var list=window.FindControl<ListBox>("CommandResults")!;var selected=Assert.IsType<ListBoxItem>(list.SelectedItem);
            var symbol=Assert.Single(selected.GetVisualDescendants().OfType<OperatorSymbol>());
            Assert.Equal("launch",symbol.Kind);Assert.Equal(selected.IsSelected,symbol.IsActive);Assert.Single(selected.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            window.FindControl<TextBox>("CommandSearch")!.Text="resume";await Settle();
            var resume=Assert.IsType<ListBoxItem>(list.SelectedItem);
            Assert.Equal("resume",Assert.Single(resume.GetVisualDescendants().OfType<OperatorSymbol>()).Kind);
            window.FindControl<TextBox>("CommandSearch")!.Text="";
            await Settle();selected=Assert.IsType<ListBoxItem>(list.SelectedItem);
            var move=list.SelectedIndex==list.ItemCount-1?PhysicalKey.ArrowUp:PhysicalKey.ArrowDown;
            window.KeyPressQwerty(move,RawInputModifiers.None);window.KeyReleaseQwerty(move,RawInputModifiers.None);
            await Settle(60);Capture(window,"command-results-moving-"+width);
            Assert.NotSame(selected,list.SelectedItem);
            var arriving=Assert.IsType<ListBoxItem>(list.SelectedItem);
            Assert.InRange(OperatorChoiceMotion.GetProgress(arriving),.001,.999);
            var fill=Assert.Single(arriving.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            Assert.Equal(OperatorChoiceMotion.GetProgress(arriving),fill.Progress,3);
            for(int i=0;i<4;i++)
            {
                var key=i%2==0?PhysicalKey.ArrowUp:PhysicalKey.ArrowDown;
                window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);await Settle(30);
            }
            await Settle(600);
            foreach(var row in list.Items.OfType<ListBoxItem>())
            {Assert.Equal(row.IsSelected?1:0,OperatorChoiceMotion.GetProgress(row));Assert.Equal(row.IsSelected,Assert.Single(row.GetVisualDescendants().OfType<OperatorSymbol>()).IsActive);}
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);
            Assert.False(window.FindControl<Grid>("CommandOverlay")!.IsVisible);Assert.True(window.OpenCommandBar());
            OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,true);
            list.SelectedIndex=1;await Settle();
            foreach(var row in list.Items.OfType<ListBoxItem>())Assert.Equal(row.IsSelected?1:0,OperatorChoiceMotion.GetProgress(row));
        }
        finally{window.Close();app.IsSelfTestMode=prior;}
    }
    [AvaloniaFact]
    public async Task HomeDetailSheetExpandsFromItsTriggerAndReversesWithoutStaleCompletion()
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);OperatorMotion.AnimationOverride=true;
        var home=new SportSelectionScreen();var window=new NativeWindow{Width=900,Height=700,Content=home};window.Show();
        try
        {
            await Settle(700);var trigger=home.FindControl<Button>("ChangelogToggle")!;trigger.Focus();
            var scale=(Avalonia.Media.ScaleTransform)NativeLookup.Find(home,"ChangelogPanelScale")!;
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Assert the start geometry before yielding/capturing: screenshot
            // rendering can legitimately advance a fast spring under CI load.
            Assert.True(scale.ScaleX<.8,"The sheet should grow from its small source tile, rather than a 96% centre pop.");
            var card=home.FindControl<OperatorGlassSurface>("ChangelogPanel")!;
            double sourceScale=Math.Clamp(trigger.Bounds.Width/card.Bounds.Width,.04,1);
            Assert.Equal(1-(1-sourceScale)*OperatorMotion.TravelScale,scale.ScaleX,5);
            await Settle(5);Capture(window,"home-detail-source-early");
            var hide=typeof(SportSelectionScreen).GetMethod("HideChangelog",BindingFlags.Instance|BindingFlags.NonPublic)!;
            hide.Invoke(home,null);trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(850);
            Assert.True(home.FindControl<Grid>("ChangelogOverlay")!.IsVisible);Assert.Equal(1,scale.ScaleX,3);
            Capture(window,"home-detail-expanded");
            Assert.Null(home.FindControl<Grid>("HomeLayout")!.Effect);
            Assert.InRange(home.FindControl<Grid>("HomeLayout")!.Opacity,.09,.11);
            Assert.IsType<Avalonia.Media.BlurEffect>(home.FindControl<Grid>("HomeBackdrop")!.Effect);
            OperatorAppearance.Configure(false,false);Assert.Null(home.FindControl<Grid>("HomeBackdrop")!.Effect);
            hide.Invoke(home,null);for(int frame=0;frame<5;frame++){await Settle(35);Capture(window,"home-detail-return-"+frame.ToString("D2"));}
            await Settle(850);Assert.False(home.FindControl<Grid>("ChangelogOverlay")!.IsVisible);Assert.True(trigger.IsFocused);
            Assert.Equal(1,home.FindControl<Grid>("HomeLayout")!.Opacity);Assert.Null(home.FindControl<Grid>("HomeBackdrop")!.Effect);
        }
        finally{window.Close();}
    }
    [Theory]
    [InlineData(24,16,96,44,720,580)]
    [InlineData(400,300,140,60,600,500)]
    public void ExpansionGeometryKeepsTheOriginalControlCenterAndSize(double x,double y,double width,double height,double targetWidth,double targetHeight)
    {
        var origin=ExpansionOrigin.FromRect(new Rect(x,y,width,height),new Size(targetWidth,targetHeight));
        Assert.Equal(width,origin.ScaleX*targetWidth,6);Assert.Equal(height,origin.ScaleY*targetHeight,6);
        Assert.Equal(x+width/2,targetWidth/2+origin.OffsetX,6);Assert.Equal(y+height/2,targetHeight/2+origin.OffsetY,6);
        Assert.Equal(new ExpansionOrigin(1,1,0,0),origin.WithTravel(0));
        var full=origin.WithTravel(1);Assert.Equal(origin.ScaleX,full.ScaleX,10);Assert.Equal(origin.ScaleY,full.ScaleY,10);
        Assert.Equal(origin.OffsetX,full.OffsetX,10);Assert.Equal(origin.OffsetY,full.OffsetY,10);
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task SpeakerWavesAndMuteSlashRetargetIndependentlyAndSettleOnUnload(bool reduced)
    {
        using var appearance=new AppearanceScope();OperatorMotion.AnimationOverride=!reduced;
        var symbol=new OperatorSymbol{Kind="audio",AudioLevel=.8,Width=80,Height=80};var window=new NativeWindow{Width=420,Height=200,Content=symbol};window.Show();
        try
        {
            await Settle(300);Assert.Equal(.8,symbol.RenderedAudioLevel,5);
            symbol.AudioMuted=true;Assert.True(symbol.AudioMuted);Assert.Equal(.8,symbol.AudioLevel);
            await Settle(60);
            if(reduced){Assert.Equal(0,symbol.RenderedAudioLevel);Assert.Equal(1,symbol.RenderedMute);}
            else{Assert.InRange(symbol.RenderedAudioLevel,.001,.799);Assert.InRange(symbol.RenderedMute,.001,.999);}
            symbol.AudioMuted=false;symbol.AudioLevel=.25;symbol.AudioMuted=true;symbol.AudioMuted=false;
            for(int i=0;i<8;i++){Capture(window,"speaker-part-"+reduced+"-"+i.ToString("D2"));await Settle(45);}
            await Settle(250);Assert.Equal(.25,symbol.RenderedAudioLevel,5);Assert.Equal(0,symbol.RenderedMute,5);
            symbol.AudioMuted=true;window.Close();Assert.False(symbol.IsAnimating);Assert.Equal(0,symbol.RenderedAudioLevel);Assert.Equal(1,symbol.RenderedMute);
        }
        finally{window.Close();}
    }
    [Fact]
    public void MoreDotsHaveIndividualPartMotionAndAStableRestingState()
    {
        Assert.Equal(-2.6,OperatorSymbol.MoreDotOffset(0,.275),6);
        Assert.NotEqual(OperatorSymbol.MoreDotOffset(0,.275),OperatorSymbol.MoreDotOffset(1,.275));
        for(int i=0;i<3;i++){Assert.Equal(0,OperatorSymbol.MoreDotOffset(i,0),6);Assert.Equal(0,OperatorSymbol.MoreDotOffset(i,1),6);}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task DetailPressInflatesTemplatePixelsWithoutMovingHitBoundsOrDelayingTheCommand(bool reduced)
    {
        using var appearance=new AppearanceScope();OperatorMotion.AnimationOverride=!reduced;
        var button=new Button{Content="Open details",Width=180,Height=48};button.SetResourceReference(Control.ThemeProperty,"ToolBtn");
        OperatorControlMotion.SetEnabled(button,true);OperatorControlMotion.SetInflateOnPress(button,true);
        var body=new Grid();body.Children.Add(button);OperatorDesign.SetIsEnabled(body,true);
        var window=new NativeWindow{Width=420,Height=220,Content=body};int clicks=0;button.Click+=(_,_)=>clicks++;window.Show();
        try
        {
            await Settle(400);var bounds=button.Bounds;var point=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value;
            window.MouseDown(point,MouseButton.Left);await Settle(200);
            var scale=Assert.IsType<Avalonia.Media.ScaleTransform>(NativeTemplates.FindObject(button,"TbSc"));
            Assert.Equal(bounds,button.Bounds);Assert.Equal(0,clicks);
            if(reduced)Assert.Equal(1,scale.ScaleX);else Assert.InRange(scale.ScaleX,1.001,1.05);
            Capture(window,"detail-press-"+reduced);window.MouseUp(point,MouseButton.Left);Assert.Equal(1,clicks);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task MediaAudioAndItsSymbolKeepTheOperatorsChoiceWhenOutputIsRecreated()
    {
        using var savedFiles=new WindowFilesScope();
        var app=(App)Application.Current!;bool previous=app.IsSelfTestMode;app.IsSelfTestMode=true;
        var window=new Roche_Scoreboard.MainWindow();window.Show();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Call(string name,params object[] args)=>typeof(Roche_Scoreboard.MainWindow).GetMethod(name,flags)!.Invoke(window,args);
        VideoPlayerControl Player()=>(VideoPlayerControl)typeof(Roche_Scoreboard.MainWindow).GetField("_videoPlayer",flags)!.GetValue(window)!;
        try
        {
            await Settle(200);window.FindControl<SportSelectionScreen>("SportSelectionPanel")!.IsVisible=false;
            Call("FinalizeAflSetup",new Roche_Scoreboard.Models.SetupResult{HomeName="Home",AwayName="Away",WeatherLocation=""});await Settle(550);
            window.FindControl<Slider>("VideoTabVolumeSlider")!.Value=.25;
            window.FindControl<Button>("VideoTabMuteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Player().IsMuted);Assert.Equal(.25,Player().Volume);
            ((NativeWindow)typeof(Roche_Scoreboard.MainWindow).GetField("_displayWindow",flags)!.GetValue(window)!).Close();
            Call("EnableOperatorOutput");await Settle(300);
            Assert.True(Player().IsMuted);Assert.Equal(.25,Player().Volume);Assert.False(Player().HasSource);
            var symbol=window.FindControl<OperatorSymbol>("VideoTabAudioSymbol")!;
            Assert.True(symbol.AudioMuted);Assert.Equal(.25,symbol.AudioLevel);
            Assert.Contains("Unmute",Avalonia.Automation.AutomationProperties.GetName(window.FindControl<Button>("VideoTabMuteButton")!));
        }
        finally{window.Close();app.IsSelfTestMode=previous;}
    }
    [AvaloniaTheory]
    [InlineData(900,700)][InlineData(420,320)]
    public async Task CorrectionHasAPinnedBalanceSummaryAndSeparateFigureSections(int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=width,Height=height,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle(250);
            var launch=panel.GetVisualDescendants().OfType<Button>().Single(button=>Equals(button.Tag,"scorecard-edit-totals"));launch.Focus();launch.BringIntoView();
            launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(600);Capture(window,"correction-workspace-"+width);
            var equation=panel.GetVisualDescendants().OfType<TextBlock>().Single(text=>Equals(text.Tag,"scorecard-totals-equation"));
            Assert.DoesNotContain(equation.GetVisualAncestors(),ancestor=>ancestor is ScrollViewer scroll&&Equals(scroll.Tag,"cricket-prompt-body"));
            foreach(string part in new[]{"batters","extras","bowling"})Assert.Single(panel.GetVisualDescendants().OfType<Expander>(),group=>Equals(group.Tag,"scorecard-correction-"+part));
            var summary=panel.GetVisualDescendants().OfType<Border>().Single(x=>Equals(x.Tag,"scorecard-correction-summary"));
            var scroller=panel.GetVisualDescendants().OfType<ScrollViewer>().Single(x=>Equals(x.Tag,"cricket-prompt-body"));
            if(scroller.Viewport.Height>=48 && !double.IsInfinity(scroller.MaxHeight))
            {
                var before=summary.TranslatePoint(default,window);scroller.Offset=new Vector(0,scroller.Extent.Height);await Settle(50);
                Assert.Equal(before,summary.TranslatePoint(default,window));
            }
            var cancel=panel.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Tag,"scorecard-correction-cancel"));
            cancel.Focus();cancel.BringIntoView();await Settle();
            Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(cancel.TranslatePoint(default,window)!.Value,cancel.Bounds.Size)));
            Assert.Equal(0,match.TotalRuns);Assert.Empty(match.CurrentInnings!.ScorecardChanges);
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaFact]
    public async Task InvalidCorrectionRevealsItsFoldedFieldWithoutLosingKeyboardFocus()
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,false);OperatorMotion.AnimationOverride=null;
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=700,Height=500,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle(200);
            Button Action(string tag)=>panel.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Tag,tag));
            Action("scorecard-edit-totals").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(500);
            var wide=panel.GetVisualDescendants().OfType<TextBox>().Single(x=>Equals(x.Tag,"scorecard-edit-wides"));wide.Text="-1";
            var extras=panel.GetVisualDescendants().OfType<Expander>().Single(x=>Equals(x.Tag,"scorecard-correction-extras"));extras.IsExpanded=false;await Settle(300);
            string before=JsonSerializer.Serialize(match.ToState());Action("apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(650);
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));Assert.True(extras.IsExpanded);
            Assert.True(wide.IsKeyboardFocusWithin,$"Invalid correction must keep focus at its field; visible={wide.IsEffectivelyVisible}, current={window.FocusManager?.GetFocusedElement()}.");
            Capture(window,"invalid-correction-repair");
            var fieldRect=new Rect(wide.TranslatePoint(default,window)!.Value,wide.Bounds.Size);
            Assert.True(new Rect(window.Bounds.Size).Contains(fieldRect),$"Invalid field bounds {fieldRect}; viewport {window.Bounds.Size}; scrolls {string.Join(';',wide.GetVisualAncestors().OfType<ScrollViewer>().Select(x=>$"{x.Tag}:{x.Offset}/{x.Extent}/{x.Viewport}"))}");
            wide.Text="-2";
            for(int repeat=0;repeat<3;repeat++)Action("apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Action("scorecard-correction-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(250);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
            Assert.False(wide.IsKeyboardFocusWithin); // queued repair work cannot reclaim focus after cancellation
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task CorrectionEntryAndRepeatedFoldsCannotApplyOrLeaveAnObsoleteSheet(bool reduced)
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,reduced);OperatorMotion.AnimationOverride=null;
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=900,Height=700,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle(200);
            Button Action(string tag)=>panel.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Tag,tag));
            string before=JsonSerializer.Serialize(match.ToState());var launch=Action("scorecard-edit-totals");launch.Focus();launch.BringIntoView();await Settle();
            Action("scorecard-edit-totals").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int frame=0;frame<7;frame++){await Settle(55);Capture(window,$"correction-entry-{reduced}-{frame:D2}");}
            var group=panel.GetVisualDescendants().OfType<Expander>().Single(x=>Equals(x.Tag,"scorecard-correction-batters"));
            for(int i=0;i<5;i++){group.IsExpanded=false;await Settle(15);group.IsExpanded=true;}
            await Settle(350);Assert.True(group.IsExpanded);
            Assert.True(panel.GetVisualDescendants().OfType<TextBox>().Single(x=>Equals(x.Tag,"scorecard-edit-batter-0")).IsEffectivelyVisible);
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);await Settle(250);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
            Action("scorecard-edit-totals").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(30);
            OperatorAppearance.Configure(true,true);await Settle(20);
            var surface=panel.GetVisualDescendants().OfType<OperatorGlassSurface>().Single(x=>Equals(x.Tag,"scorecard-correction-surface"));
            Assert.Equal(Matrix.Identity,surface.RenderTransform!.Value);
            panel.ClearMatch();await Settle(600);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Border>(),x=>Equals(x.Tag,"cricket-decision-sheet"));
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaFact]
    public async Task ACollapsedCorrectionGroupKeepsItsChevronWhenItsSheetIsReused()
    {
        using var appearance=new AppearanceScope();OperatorAppearance.Configure(true,false);OperatorMotion.AnimationOverride=null;
        var panel=new CricketControlPanel();
        var section=((Expander Group,StackPanel Body,TextBlock Detail))typeof(CricketControlPanel)
            .GetMethod("CorrectionSection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,["Batters","bat","reuse-check"])!;
        var host=new Grid();host.Children.Add(section.Group);
        var window=new NativeWindow{Width=700,Height=500,Content=host};window.Show();
        try
        {
            await Settle(150);section.Group.IsExpanded=false;await Settle(350);
            host.Children.Remove(section.Group);await Settle(50);host.Children.Add(section.Group);await Settle(350);
            Assert.False(section.Group.IsExpanded);
            var chevron=((Grid)section.Group.Header!).Children.OfType<OperatorSymbol>().Single(x=>Equals(x.Kind,"forward"));
            Assert.Equal(0,Assert.IsType<Avalonia.Media.RotateTransform>(chevron.RenderTransform).Angle,3);
        }
        finally{window.Close();}
    }
}
