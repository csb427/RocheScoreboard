using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Point = System.Windows.Point;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunAppleReviewChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var window=new Window{Width=1100,Height=800,Owner=this,ShowInTaskbar=false};
        OperatorDesign.Apply(window);window.Show();
        bool glass=OperatorAppearance.GlassEnabled,reduced=OperatorAppearance.ReduceMotion;
        var theme=((App)System.Windows.Application.Current).ThemeService.CurrentTheme;
        try
        {
            var settings=new GlobalSettingsScreen();window.Content=settings;await Task.Delay(180);window.UpdateLayout();
            foreach(var workspace in QaDescendants(settings).OfType<SettingsWorkspace>())
            {
                var choices=QaDescendants(workspace).OfType<Button>().Where(button=>button.IsVisible && button.Tag is string category && category is "ALL" or "APPEARANCE" or "CONTROLS" or "SCOREBOARD" or "AUTOMATION" or "SYSTEM").ToArray();
                File.WriteAllText(Path.Combine(directory,"review-settings-icon-bounds.json"),System.Text.Json.JsonSerializer.Serialize(choices.Select(button=>new{button.Tag,button.ActualWidth,button.ActualHeight,Symbols=QaDescendants(button).OfType<OperatorSymbol>().Select(symbol=>new{symbol.Kind,symbol.ActualWidth,symbol.ActualHeight,Inside=CricketQaBoundsWithin(symbol,button)}).ToArray()}).ToArray()));
                checks["review-settings-category-icons-have-small-centred-optical-frames"]=choices.Length>=5 && choices.All(button=>QaDescendants(button).OfType<OperatorSymbol>().All(symbol=>symbol.ActualWidth<=20 && symbol.ActualHeight<=20 && CricketQaBoundsWithin(symbol,button)));
            }
            CaptureCricketQa(settings,Path.Combine(directory,"review-settings-refined-glass.png"));
            window.Width=420;window.Height=520;await Task.Delay(120);window.UpdateLayout();
            var categoryButton=QaDescendants(settings).OfType<Button>().First(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Settings categories");
            var settingsSearch=QaDescendants(settings).OfType<OperatorSearchBox>().First();
            checks["review-narrow-settings-search-has-its-own-full-width-row"]=settingsSearch.TranslatePoint(new Point(),settings).Y>=categoryButton.TranslatePoint(new Point(0,categoryButton.ActualHeight),settings).Y && settingsSearch.ActualWidth>300;
            CaptureCricketQa(settings,Path.Combine(directory,"review-settings-stacked-header.png"));
            foreach(var (sport,wizard) in new (string,FrameworkElement)[]{("afl",new SetupWizard()),("cricket",new CricketSetupWizard())})
            {
                window.Width=1100;window.Height=800;window.Content=wizard;await Task.Delay(700);window.UpdateLayout();
                var teams=(Grid)wizard.FindName("TeamsStepContainer");
                checks[$"review-{sport}-team-form-replaces-legacy-stack"]=teams.Children.Count==1 && QaDescendants(teams).OfType<OperatorGlassSurface>().Count(surface=>surface.Padding.Left==24)==2;
                foreach(string side in new[]{"Home","Away"})
                {
                    var input=(TextBox)wizard.FindName($"Setup{side}NameBox");input.Text="Review "+side;
                    checks[$"review-{sport}-{side}-team-name-keeps-original-live-input"]=input.IsVisible && input.ActualWidth>100 && input.IsEnabled;
                    var search=QaDescendants(teams).OfType<Button>().Where(button=>Equals(button.Content,"Find a club")).ToArray();
                    checks[$"review-{sport}-club-actions-have-full-width-primary-hierarchy"]=search.Length==2 && search.All(button=>button.ActualHeight>=46);
                }
                CaptureCricketQa(wizard,Path.Combine(directory,$"review-{sport}-team-editor-wide.png"));
                window.Width=420;window.Height=520;await Task.Delay(180);window.UpdateLayout();
                var viewport=FindQaVisual<WizardViewport>(wizard)!;
                checks[$"review-{sport}-new-team-editor-no-horizontal-overflow"]=viewport.ScrollableWidth<1;
                viewport.ScrollToBottom();await Task.Delay(50);CaptureCricketQa(wizard,Path.Combine(directory,$"review-{sport}-team-editor-narrow.png"));
                InvokeCricketQa(wizard,"ShowStep",sport=="afl"?3:4,false);await Task.Delay(150);viewport.ScrollToBottom();await Task.Delay(50);
                CaptureCricketQa(wizard,Path.Combine(directory,$"review-{sport}-venue-weather.png"));
                InvokeCricketQa(wizard,"ShowStep",1,false);await Task.Delay(100);
                checks[$"review-{sport}-team-fields-survive-navigation"]=((TextBox)wizard.FindName("SetupHomeNameBox")).Text=="Review Home";
            }
            window.Width=1000;window.Height=760;var format=new CricketSetupWizard();window.Content=format;await Task.Delay(500);InvokeCricketQa(format,"ShowStep",3,false);await Task.Delay(150);
            var scroll=FindQaVisual<WizardViewport>(format)!;scroll.ScrollToTop();window.UpdateLayout();
            foreach(var choice in new[]{format.LimitedFormatChoice,format.MultiFormatChoice})
            {
                bool coverage=true;
                foreach(var point in new[]{new Point(3,3),new Point(choice.ActualWidth/2,choice.ActualHeight/2),new Point(choice.ActualWidth-3,choice.ActualHeight-3)})
                {
                    DependencyObject? hit=choice.InputHitTest(point) as DependencyObject;
                    while(hit is not null && !ReferenceEquals(hit,choice))hit=VisualTreeHelper.GetParent(hit);
                    coverage &= ReferenceEquals(hit,choice);
                }
                checks[$"review-format-entire-card-is-native-hit-target-{choice.Name}"]=coverage;
                var peer=new RadioButtonAutomationPeer(choice);((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                checks[$"review-format-native-accessibility-selection-{choice.Name}"]=choice.IsChecked==true;
            }
            await Task.Delay(300);CaptureCricketQa(format,Path.Combine(directory,"review-format-full-card-selection.png"));
            var match=CricketReliabilityChecks.CreateMatch();match.TossPending=true;
            var panel=new CricketControlPanel();panel.SetMatch(match);window.Content=panel;await Task.Delay(150);
            InvokeCricketQa(panel,"OpenToss_Click",this,new RoutedEventArgs());await Task.Delay(350);
            panel.TossHomeChoice.IsChecked=true;panel.TossBowlChoice.IsChecked=true;await Task.Delay(300);window.UpdateLayout();
            checks["review-toss-sheet-uses-shared-glass-material"]=panel.TossSheet is OperatorGlassSurface && panel.ConfirmTossButton.IsEnabled;
            CaptureCricketQa(panel,Path.Combine(directory,"review-toss-glass-selection.png"));
            OperatorDesign.UpdateTheme(AppTheme.Light);await Task.Delay(120);window.UpdateLayout();
            var teamTitle=QaDescendants(panel.TossHomeChoice).OfType<TextBlock>().First(text=>text.Text==match.TeamAName);
            checks["review-toss-selection-ink-adapts-to-light-appearance"]=teamTitle.Foreground is SolidColorBrush ink && ink.Color.R<128;
            CaptureCricketQa(panel,Path.Combine(directory,"review-toss-light-selection.png"));OperatorDesign.UpdateTheme(theme);
            panel.ClearMatch();window.Content=null;
            foreach(bool material in new[]{true,false})
            {
                OperatorAppearance.Configure(material,!material);
                var library=new TeamLibraryWindow("home",Models.TeamLibrarySport.Cricket){Owner=window};library.Show();await Task.Delay(180);
                checks[$"review-club-library-window-separates-backdrop-from-glass-{material}"]=library.Content is Grid && library.ListSurface is OperatorGlassSurface && library.LibraryToolbar.IsVisible;
                CaptureCricketQa((FrameworkElement)library.Content,Path.Combine(directory,$"review-club-library-glass-{material}.png"));library.Close();
            }
        }
        finally {window.Close();OperatorAppearance.Configure(glass,reduced);OperatorDesign.UpdateTheme(theme);}
        return checks;
    }
}
