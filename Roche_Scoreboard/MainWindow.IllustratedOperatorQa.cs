using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Reflection;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using TextBox=System.Windows.Controls.TextBox;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunIllustratedOperatorChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();bool? motion=OperatorMotion.AnimationOverride;bool hints=OperatorGuidance.GetShowHints(this);
        var theme=((App)System.Windows.Application.Current).ThemeService.CurrentTheme;
        var window=new Window{Owner=this,Width=1100,Height=820,ShowInTaskbar=false};OperatorDesign.Apply(window);window.Show();
        async Task Wait(int delay=120){await Task.Delay(delay);window.UpdateLayout();}
        try
        {
            OperatorMotion.AnimationOverride=true;OperatorDesign.UpdateGuidance(false);
            var home=new SportSelectionScreen();window.Content=home;await Wait();
            checks["landing-uses-three-full-scene-illustrations"]=QaDescendants(home).OfType<OperatorSportArtwork>().Count()==3;
            checks["landing-loads-all-three-bundled-generated-images"]=QaDescendants(home).OfType<OperatorSportArtwork>().All(art=>art.HasGeneratedArtwork);
            var sportSlot=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(home.SportCardsGrid);
            checks["sports-grid-fits-real-remaining-height-without-cropping-training"]=home.SportCardsGrid.ActualHeight<=sportSlot.Height+1;
            var sportCards=new[]{home.AflCard,home.CricketCard,home.TrainingCard};
            checks["sport-hover-content-clips-to-rounded-boundaries"]=sportCards.All(card=>card.Clip is RectangleGeometry shape && shape.RadiusX==28 && !shape.FillContains(new System.Windows.Point(1,1)));
            foreach(var card in sportCards)
            {
                InvokeCricketQa(home,"EngageHomeTile",card,true);await Wait(140);
                var body=(FrameworkElement)home.FindName(card==home.AflCard?"AflTileBody":card==home.CricketCard?"CricketTileBody":"TrainingTileBody");
                checks[$"visible-{card.Name}-body-responds-to-hover"]=body.RenderTransform.Value.M11>1.005;
                CaptureCricketQa(home,Path.Combine(directory,$"generated-home-hover-{card.Name}.png"));
                InvokeCricketQa(home,"EngageHomeTile",card,false);
            }
            for(int i=0;i<24;i++)
            {foreach(var card in sportCards)InvokeCricketQa(home,"EngageHomeTile",card,false);InvokeCricketQa(home,"EngageHomeTile",sportCards[i%3],true);await Wait(8);}
            foreach(var card in sportCards)InvokeCricketQa(home,"EngageHomeTile",card,false);
            await Wait(800);
            var motions=(System.Collections.IDictionary)typeof(SportSelectionScreen).GetField("_homeTiles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home)!;
            checks["rapid-cross-card-motion-retires-all-render-loops"]=motions.Values.Cast<object>().All(controller=>!(bool)controller.GetType().GetField("_running",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(controller)!);
            checks["hover-never-paints-the-extra-travelling-slab"]=home.HomeInteractionLens.Opacity==0;
            var action=QaDescendants(home).OfType<Button>().First(button=>button.Name=="AflLaunchAction");
            checks["navigation-hints-off-disables-launch-tooltip"]=!ToolTipService.GetIsEnabled(action);
            OperatorDesign.UpdateGuidance(true);await Wait(30);checks["navigation-hints-enable-updates-existing-controls"]=ToolTipService.GetIsEnabled(action);
            OperatorDesign.UpdateGuidance(false);
            checks["new-preferences-default-navigation-hints-off"]=!new UxPreferencesData().EnableQuickStartupHints;
            int selections=0;home.SportSelected+=_=>selections++;
            for(int i=0;i<15;i++){InvokeCricketQa(home,"ActivateSport",SportMode.Cricket);window.Content=new Grid();window.Content=home;home.PrepareForDisplay();await Wait(10);}
            checks["landing-rapid-reentry-never-latches-input"]=selections==15 && home.IsHitTestVisible;
            CaptureCricketQa(home,Path.Combine(directory,"illustrated-home.png"));

            var wizard=new CricketSetupWizard();window.Content=wizard;await Wait();InvokeCricketQa(wizard,"ShowStep",2,false);await Wait();
            var add=QaDescendants(wizard).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"add-home-player"));
            checks["setup-has-full-width-add-player-action"]=add is {IsVisible:true,ActualWidth:>180};
            add?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
            var first=QaDescendants(wizard).OfType<TextBox>().FirstOrDefault(field=>Equals(field.Tag,"new-player-first"));
            var last=QaDescendants(wizard).OfType<TextBox>().FirstOrDefault(field=>Equals(field.Tag,"new-player-last"));
            var apply=QaDescendants(wizard).OfType<Button>().LastOrDefault(button=>Equals(button.Tag,"apply"));
            checks["blank-player-cannot-be-added"]=first is not null && apply is {IsEnabled:false};
            if(first is not null && last is not null && apply is not null)
            {
                first.Text="Alex";last.Text="Morgan";
                foreach(var role in QaDescendants(wizard).OfType<CheckBox>().Where(role=>Equals(role.Tag,"new-player-captain")||Equals(role.Tag,"new-player-keeper")))role.IsChecked=true;
                checks["player-name-enables-confirm"]=apply.IsEnabled;
                foreach(var palette in new[]{AppTheme.Dark,AppTheme.Light})
                {
                    OperatorDesign.UpdateTheme(palette);await Wait(450);
                    checks[$"player-roles-selected-contrast-{palette}"]=QaDescendants(wizard).OfType<CheckBox>().Where(role=>role.Tag is string tag && tag.StartsWith("new-player-")).All(role=>
                        role.Template.FindName("RoleFill",role) is Border {Background:SolidColorBrush fill} && role.Foreground is SolidColorBrush ink && RoleContrast(fill.Color,ink.Color)>=4.5);
                    checks[$"player-field-labels-follow-palette-{palette}"]=QaDescendants(wizard).OfType<TextBlock>().Where(label=>Equals(label.Tag,"new-player-label")).All(label=>Equals(label.Foreground,label.FindResource("ControlCentre.TextBrush")));
                    CaptureCricketQa(wizard,Path.Combine(directory,$"new-player-sheet-{palette}.png"));
                }
                OperatorDesign.UpdateTheme(theme);await Wait(300);CaptureCricketQa(wizard,Path.Combine(directory,"new-player-sheet.png"));
                window.Width=420;window.Height=320;await Wait(300);
                var actionOrigin=apply.TranslatePoint(new System.Windows.Point(),wizard);
                checks["narrow-player-sheet-keeps-confirmation-in-viewport"]=apply.ActualWidth>100 && actionOrigin.Y>=0 && actionOrigin.Y+apply.ActualHeight<=wizard.ActualHeight+1;
                CaptureCricketQa(wizard,Path.Combine(directory,"new-player-sheet-narrow.png"));
                window.Width=1100;window.Height=820;await Wait(300);
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(330);
                var list=(ItemsControl)wizard.FindName("HomePlayerList");var player=list.Items.OfType<CricketPlayer>().FirstOrDefault();
                checks["new-player-keeps-name-and-selected-roles"]=player is{FirstName:"Alex",LastName:"Morgan",IsCaptain:true,IsWicketkeeper:true};
                CaptureCricketQa(wizard,Path.Combine(directory,"player-roster.png"));
                int notified=0;if(player is not null){player.PropertyChanged+=(_,_)=>notified++;player.IsCaptain=false;player.IsWicketkeeper=false;}
                checks["role-changes-notify-without-list-refresh"]=notified==2;
                int count=list.Items.Count;add!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();CricketPrompt.CancelAll((Grid)wizard.Content);await Wait();
                checks["cancel-player-entry-does-not-add-a-roster-entry"]=list.Items.Count==count;
                checks["wizard-teams-and-players-have-distinct-symbols"]=Equals(wizard.Step1Symbol.Kind,"team") && Equals(wizard.Step2Symbol.Kind,"players");
                checks["roster-has-no-inline-role-selectors"]=!QaDescendants(list).OfType<Button>().Any(button=>button.Content is string role && role is "WK" or "C");
                var edit=QaDescendants(list).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"edit-home-player"));
                edit?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
                var editFirst=QaDescendants(wizard).OfType<TextBox>().FirstOrDefault(field=>Equals(field.Tag,"new-player-first"));
                var editLast=QaDescendants(wizard).OfType<TextBox>().FirstOrDefault(field=>Equals(field.Tag,"new-player-last"));
                checks["three-dots-opens-populated-editor-directly"]=editFirst?.Text=="Alex" && editLast?.Text=="Morgan";
                if(editFirst is not null && editLast is not null)
                {
                    editFirst.Text="Jamie";editLast.Text="Reed";
                    foreach(var role in QaDescendants(wizard).OfType<CheckBox>().Where(role=>Equals(role.Tag,"new-player-captain")||Equals(role.Tag,"new-player-keeper")))role.IsChecked=true;
                    QaDescendants(wizard).OfType<Button>().Last(button=>Equals(button.Tag,"apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(330);
                    checks["edit-updates-original-player-name-and-roles"]=list.Items.Count==count && ReferenceEquals(player,list.Items[0]) && player is{FullName:"Jamie Reed",IsCaptain:true,IsWicketkeeper:true};
                    checks["edited-name-updates-visible-roster-binding"]=QaDescendants(list).OfType<TextBlock>().Any(text=>text.Text=="Jamie Reed");
                    edit?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
                    var unsaved=QaDescendants(wizard).OfType<TextBox>().First(field=>Equals(field.Tag,"new-player-first"));unsaved.Text="Cancelled";
                    CricketPrompt.CancelAll((Grid)wizard.Content);await Wait(300);
                    checks["cancel-player-edit-preserves-original-values"]=player?.FirstName=="Jamie";
                }
                CaptureCricketQa(wizard,Path.Combine(directory,"edited-player-roster.png"));
                QaDescendants(list).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"remove-home-player"))?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
                checks["home-native-remove-button-removes-player"]=list.Items.Count==count-1;
                var away=(ItemsControl)wizard.FindName("AwayPlayerList");
                var awayPlayers=(System.Collections.IList)typeof(CricketSetupWizard).GetField("_awayPlayers",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard)!;
                awayPlayers.Add(new CricketPlayer{FirstName="Away",LastName="Player"});await Wait();
                var awayEdit=QaDescendants(away).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"edit-away-player"));
                awayEdit?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
                checks["away-editor-targets-away-player"]=QaDescendants(wizard).OfType<TextBox>().Any(field=>Equals(field.Tag,"new-player-first") && field.Text=="Away");
                CricketPrompt.CancelAll((Grid)wizard.Content);await Wait(300);
                QaDescendants(away).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,"remove-away-player"))?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait();
                checks["away-native-remove-button-removes-player"]=away.Items.Count==0;
            }
            var liquid=OperatorLiquidSelection.Calculate(new System.Windows.Size(500,58),100,280,110,900,1500,2,1.7,true);
            checks["liquid-transition-has-visible-neck-and-leading-droplet"]=liquid.Deformation>.7 && Math.Abs(liquid.Direction)>.5 && OperatorLiquidSelection.Geometry(liquid).Bounds.Width>150;
            checks["liquid-resting-state-has-no-deformation"]=OperatorLiquidSelection.Calculate(new System.Windows.Size(500,58),100,210,110,0,0,2,1.7,false).Deformation==0;
            var presets=(System.Collections.IList)typeof(CricketSetupWizard).GetField("_presets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(wizard)!;
            presets.Clear();presets.Add(new TeamPreset{PresetName="Illustrated QA club",TeamName="Northside Cricket Club",Abbreviation="NCC",PrimaryColor="#2255AA"});
            var presetPanel=(WrapPanel)wizard.FindName("HomePresetPanel");var empty=(TextBlock)wizard.FindName("HomePresetEmpty");InvokeCricketQa(wizard,"BuildPresetTiles",presetPanel,empty,true);
            var tile=presetPanel.Children.OfType<Border>().First();InvokeCricketQa(wizard,"BuildPresetTiles",presetPanel,empty,true);
            checks["preset-selection-retains-native-tile-container"]=ReferenceEquals(tile,presetPanel.Children[0]);
            checks["preset-has-rounded-logo-and-name-composition"]=tile.CornerRadius.TopLeft>=20 && QaDescendants(tile).OfType<TextBlock>().Any(text=>text.Text=="Northside Cricket Club");
            checks["club-presets-are-compact-and-name-only"]=tile.Width<=152 && tile.Height<=62 && !QaDescendants(tile).OfType<TextBlock>().Any(text=>text.Text=="NCC");
            InvokeCricketQa(wizard,"ShowStep",1,false);await Wait();
            OperatorClubPresetDesign.Update(tile,(TeamPreset)tile.Tag,true);
            var tick=QaDescendants(tile).OfType<System.Windows.Shapes.Path>().First(path=>Equals(path.Tag,"preset-selection-marker"));
            checks["preset-selection-draws-the-tick"]=tick.HasAnimatedProperties;
            OperatorClubPresetDesign.Update(tile,(TeamPreset)tile.Tag,true);
            checks["repeated-preset-refresh-keeps-the-current-tick-animation"]=tick.HasAnimatedProperties;
            await Wait(110);CaptureCricketQa(tile,Path.Combine(directory,"compact-club-tick.png"));
            var whiteClub=new TeamPreset{TeamName="White club",Abbreviation="WHT",PrimaryColor="#FFFFFF"};OperatorClubPresetDesign.Update(tile,whiteClub,true);
            checks["white-club-preset-keeps-readable-dark-surface"]=tile.Background is GradientBrush gradient && gradient.GradientStops.All(stop=>stop.Color.R<120 && stop.Color.G<120 && stop.Color.B<120);

            var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);window.Content=panel;await Wait();
            InvokeCricketQa(panel,"ShowBatterSelection","Select batter");await Wait();
            panel.BatterSelectionList.SelectedIndex=1;
            var playerItem=panel.BatterSelectionList.ItemContainerGenerator.ContainerFromIndex(1) as System.Windows.Controls.ListBoxItem;
            checks["player-selector-owns-rounded-hover-and-tick-feedback"]=playerItem is not null && OperatorControlMotion.GetEnabled(playerItem) && SelectionCheckMotion.GetEnabled(playerItem)
                && playerItem.Template.FindName("SelectionStroke",playerItem) is System.Windows.Shapes.Path{HasAnimatedProperties:true};
            checks["player-row-hover-has-rounded-visual-clip"]=playerItem?.Template.FindName("PART_Surface",playerItem) is FrameworkElement{Clip:RectangleGeometry{RadiusX:15}};
            await Wait(110);CaptureCricketQa(panel,Path.Combine(directory,"rounded-player-selection.png"));panel.ClearMatch();panel.SetMatch(match);await Wait();
            match.SetBreak(CricketBreakKind.Lunch);await Wait();
            checks["break-panel-labels-follow-button-contrast"]=Equals(panel.ScoringReadyHint.Foreground,panel.ScoringLockBanner.Foreground) && Equals(panel.ScoringReadyDetail.Foreground,panel.ScoringLockBanner.Foreground);
            match.SetBreak(CricketBreakKind.None);panel.MainTabControl.SelectedItem=panel.ScorecardTab;await Wait(300);
            var tabLens=QaDescendants(panel).OfType<OperatorSelectionLens>().FirstOrDefault();
            panel.MainTabControl.SelectedIndex=2;
            checks["live-tab-switch-starts-connected-lens-motion"]=tabLens is {IsAnimating:true};
            if(tabLens is not null)InvokeCricketQa(tabLens,"Generated",tabLens,EventArgs.Empty);
            checks["container-notification-does-not-snap-the-travelling-tab-lens"]=tabLens is {IsAnimating:true};
            panel.MainTabControl.SelectedItem=panel.ScorecardTab;await Wait(300);
            var row=QaDescendants(panel).OfType<Grid>().FirstOrDefault(grid=>Equals(grid.Tag,"scorecard-player-batting-0"));
            InvokeCricketQa(panel,"SelectScorecardTeam",true);await Wait(300);InvokeCricketQa(panel,"SelectScorecardTeam",false);await Wait(300);
            checks["scorecard-mode-switch-reuses-figure-tree"]=row is not null && ReferenceEquals(row,QaDescendants(panel).OfType<Grid>().FirstOrDefault(grid=>Equals(grid.Tag,"scorecard-player-batting-0")));
            CaptureCricketQa(panel,Path.Combine(directory,"scorecard-mode-selection.png"));
            match.CurrentInnings!.BowlingAttack[0].FirstName="Renamed";InvokeCricketQa(panel,"RefreshScorecard",false);
            var refreshed=(CricketScorecardDraft?)typeof(CricketControlPanel).GetField("_scorecardDraft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel);
            checks["scorecard-bowler-name-change-invalidates-retained-draft"]=refreshed?.Bowlers[0].FirstName=="Renamed";
            panel.ClearMatch();

            panel.SetMatch(match);
            var editing=(Task<bool>)typeof(CricketControlPanel).GetMethod("OpenConditionsEditorAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,new object[]{false})!;await Wait();
            checks["format-editor-has-two-distinct-format-choices"]=QaDescendants(panel).OfType<Button>().Count(button=>button.Tag is string tag && tag.StartsWith("conditions-format-"))==2;
            CricketPrompt.CancelAll(panel.CricketRoot);await editing;panel.ClearMatch();

            var bug=new CricketScorebugControl();window.Content=bug;bug.UpdateFromMatch(match);await Wait();
            double original=bug.LeftScoreRail.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;
            bug.SetOverlayCompact(true);double firstPose=bug.LeftScoreRail.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;
            checks["overlay-background-retains-first-frame-position"]=Math.Abs(original-firstPose)<1;
            await Wait(100);double middle=bug.LeftScoreRail.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;
            checks["overlay-background-progresses-with-content"]=middle>original && middle<bug.CricketDesignRoot.Height*CricketBroadcastStyle.OverlayFraction;
            CaptureCricketQa(bug,Path.Combine(directory,"overlay-moving-backplate.png"));
            double current=bug.LeftScoreRail.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;bug.SetOverlayCompact(false);
            checks["overlay-background-reverses-from-visible-position"]=Math.Abs(current-bug.LeftScoreRail.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y)<1;
            await Wait(600);bug.Dispose();
        }
        finally{window.Close();OperatorMotion.AnimationOverride=motion;OperatorDesign.UpdateGuidance(hints);OperatorDesign.UpdateTheme(theme);}
        return checks;
    }
    private static double RoleContrast(System.Windows.Media.Color first,System.Windows.Media.Color second)
    {
        static double Luminance(System.Windows.Media.Color color)
        {
            static double Linear(byte value){double channel=value/255d;return channel<=.04045?channel/12.92:Math.Pow((channel+.055)/1.055,2.4);}
            return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        }
        double a=Luminance(first),b=Luminance(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
}

