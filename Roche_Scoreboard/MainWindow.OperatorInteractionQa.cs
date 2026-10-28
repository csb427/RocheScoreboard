using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using TabControl=System.Windows.Controls.TabControl;
using RadioButton=System.Windows.Controls.RadioButton;

namespace Roche_Scoreboard;
public partial class MainWindow
{
    internal async Task<Dictionary<string,bool>> RunOperatorInteractionChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();bool? old=OperatorMotion.AnimationOverride;
        foreach(var check in await OperatorSetupTrainingChecks.RunAsync())checks[check.Key]=check.Value;
        foreach(var check in await SharedSurfaceMotionChecks.RunAsync())checks[check.Key]=check.Value;
        foreach(var check in await RunSportInteractionChecksAsync(directory))checks[check.Key]=check.Value;
        var stack=new StackPanel();var window=new Window {Width=620,Height=600,Content=new System.Windows.Documents.AdornerDecorator {Child=stack},ShowInTaskbar=false};
        try
        {
            OperatorMotion.AnimationOverride=true;window.Show();await Task.Delay(80);
            var button=new Button {Content="Shared action",Width=180,Height=44};
            var text=new TextBox {Text="Edit here",Height=32};
            var combo=new ComboBox {ItemsSource=new[]{"First","Second"},SelectedIndex=0,IsEditable=true,Height=36};
            var check=new System.Windows.Controls.CheckBox {Content="Native toggle"};
            var slider=new Slider {Minimum=0,Maximum=10,Value=4,Height=28};
            var formatChoice=new RadioButton {Content="Limited overs",Height=40};
            var tiles=new TabControl {Height=110};tiles.Items.Add(new TabItem {Header="First",Content="Page one"});tiles.Items.Add(new TabItem {Header="Second",Content="Page two"});
            var disclosure=new Expander {Header="Options",Content=new TextBlock {Text="Measured disclosure content",Height=74}};
            DisclosureMotion.SetEnabled(disclosure,true);
            var action=new OperatorActionBorder {Child=new TextBlock {Text="Keyboard tile"},Width=180,Height=40,Background=System.Windows.Media.Brushes.SteelBlue};int invoked=0;action.MouseLeftButtonUp+=(_,_)=>invoked++;
            foreach(UIElement element in new UIElement[]{button,text,combo,check,slider,formatChoice,tiles,disclosure,action})stack.Children.Add(element);
            await Task.Delay(80);window.UpdateLayout();
            var before=button.RenderSize;var transform=button.RenderTransform.Value;
            InteractionMotion.Pulse(button);
            checks["templated-actions-own-feedback-without-a-second-adorner"] = OperatorFeedback.LiveAdorners(window)==0;
            var formatTile=(Border)formatChoice.Template.FindName("Tile",formatChoice);
            var tileWash=(Border)formatChoice.Template.FindName("ActionGlow",formatChoice);
            checks["format-selection-wash-fills-the-tile-instead-of-boxing-its-label"] = Math.Abs(tileWash.ActualWidth-(formatTile.ActualWidth-4))<1;
            var iconBox=new Border {Child=new TextBlock {Text="Nested icon"}};button.Content=iconBox;window.UpdateLayout();
            checks["inherited-hand-cursor-does-not-turn-template-content-into-another-button"] = ReferenceEquals(OperatorFeedback.Target(iconBox,window),button);
            CricketActionMotion.SetEnabled(button,false);
            button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0)
                {RoutedEvent=UIElement.PreviewMouseMoveEvent,Source=button});
            checks["routed-pointer-reaches-legacy-control-feedback"] = OperatorFeedback.LiveAdorners(window)>0
                && button.RenderSize==before && button.RenderTransform.Value==transform;
            InteractionMotion.Pulse(button);await Task.Delay(70);
            checks["shared-feedback-does-not-move-or-resize-hit-target"] = button.RenderSize==before && button.RenderTransform.Value==transform && OperatorFeedback.LiveAdorners(window)>0;
            await Task.Delay(400);
            button.Visibility=Visibility.Collapsed;await Task.Delay(40);
            checks["hidden-command-clears-feedback-adorners"] = OperatorFeedback.LiveAdorners(window)==0;
            button.Visibility=Visibility.Visible;
            var peer=new CheckBoxAutomationPeer(check);((IToggleProvider)peer).Toggle();await Task.Delay(400);
            checks["global-toggle-keeps-native-automation-semantics"] = check.IsChecked==true;
            combo.Text="Typed custom value";await Task.Delay(30);
            checks["global-editable-dropdown-retains-editable-part-and-text"] = combo.Template.FindName("PART_EditableTextBox",combo) is TextBox part && part.IsVisible && combo.Text=="Typed custom value";
            var actionPeer=UIElementAutomationPeer.CreatePeerForElement(action)!;((IInvokeProvider)actionPeer.GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(40);
            checks["mouse-action-tile-supports-one-native-automation-invoke"] = invoked==1 && actionPeer.GetAutomationControlType()==System.Windows.Automation.Peers.AutomationControlType.Button;
            disclosure.IsExpanded=true;await Task.Delay(60);disclosure.IsExpanded=false;await Task.Delay(40);disclosure.IsExpanded=true;await Task.Delay(320);
            checks["rapid-accordion-reversal-keeps-expanded-content-visible"] = disclosure.IsExpanded && disclosure.Content is FrameworkElement body && body.IsVisible;
            var disclosureBody=(FrameworkElement)disclosure.Template.FindName("Body",disclosure);
            double openHeight=disclosureBody.ActualHeight;
            disclosure.IsExpanded=false;await Task.Delay(60);
            checks["accordion-close-animates-from-the-last-arranged-height"] = disclosureBody.IsVisible
                && disclosureBody.Height>0 && disclosureBody.Height<=openHeight && disclosureBody.HasAnimatedProperties;
            if(!checks["accordion-close-animates-from-the-last-arranged-height"])
                File.WriteAllText(Path.Combine(directory,"disclosure-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new {openHeight,height=disclosureBody.Height.ToString(),actual=disclosureBody.ActualHeight,visible=disclosureBody.IsVisible,animated=disclosureBody.HasAnimatedProperties}));
            disclosure.IsExpanded=true;await Task.Delay(280);
            OperatorMotion.AnimationOverride=false;disclosure.IsExpanded=false;disclosure.IsExpanded=true;
            checks["reduced-motion-accordion-settles-without-delay"] = disclosure.IsExpanded;
            tiles.SelectedIndex=1;await Task.Delay(30);window.Width=430;await Task.Delay(50);
            checks["tab-motion-preserves-selection-after-resize"] = tiles.SelectedIndex==1;
            var emptyTabs=new TabControl();stack.Children.Add(emptyTabs);await Task.Delay(40);emptyTabs.Items.Add(new TabItem {Header="Loaded later",Content="Later page"});await Task.Delay(40);
            checks["empty-tab-host-can-receive-items-without-a-stale-selection"] = emptyTabs.Items.Count==1;
            var menu=new System.Windows.Controls.ContextMenu {PlacementTarget=button};var menuItem=new System.Windows.Controls.MenuItem {Header="A menu command"};int menuCalls=0;menuItem.Click+=(_,_)=>menuCalls++;menu.Items.Add(menuItem);button.ContextMenu=menu;menu.IsOpen=true;await Task.Delay(280);
            checks["shared-context-menu-opens-with-readable-custom-surface"] = menu.IsOpen && menuItem.IsVisible;
            ((IInvokeProvider)new MenuItemAutomationPeer(menuItem).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(30);menu.IsOpen=false;
            checks["shared-menu-keeps-one-command-activation"] = menuCalls==1;
            checks["broadcast-window-is-excluded-from-operator-decoration"] = !OperatorFeedback.IsOperatorWindow(new Window {WindowStyle=WindowStyle.None});
            CaptureCricketQa(stack,Path.Combine(directory,"shared-operator-inputs.png"));
            OnSportSelected(Models.SportMode.AFL);
            FinalizeAflSetup(new Models.SetupResult {HomeName="UI Home",AwayName="UI Away"});await Task.Delay(480);
            checks["afl-control-panel-is-visible-for-the-screen-checks"] = MainContent.IsVisible && !SportSelectionPanel.IsVisible;
            var actionTiles=new HashSet<OperatorActionBorder>();
            foreach(var tab in MainTabControl.Items.OfType<TabItem>())
            {
                tab.Visibility=Visibility.Visible;MainTabControl.SelectedItem=tab;await Task.Delay(100);
                foreach(var tile in QaDescendants(this).OfType<OperatorActionBorder>())actionTiles.Add(tile);
                CaptureCricketQa(this,Path.Combine(directory,"afl-operator-tab-"+MainTabControl.SelectedIndex+".png"));
            }
            checks["afl-operator-presentation-and-settings-tiles-have-keyboard-semantics"] = actionTiles.Count>=50;
            await Task.Delay(40);
        }
        finally {OperatorMotion.AnimationOverride=old;window.Close();}
        return checks;
    }
}
