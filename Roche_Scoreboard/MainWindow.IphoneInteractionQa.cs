using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Slider = System.Windows.Controls.Slider;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunIphoneControlChecksAsync(string directory)
    {
        var checks = new Dictionary<string,bool>();
        bool? motion = OperatorMotion.AnimationOverride;
        var theme = ((App)System.Windows.Application.Current).ThemeService.CurrentTheme;
        var stack = new StackPanel { Margin = new Thickness(28) };
        var root = new Grid();root.Children.Add(stack);
        var window = new Window { Owner = this, Title = "iPhone control interaction checks", Width = 900, Height = 720, Content = root };
        OperatorDesign.Apply(window);
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"ControlCentre.CanvasBrush");
        var search = new OperatorSearchBox { Placeholder = "Search clubs, settings or releases", Margin = new Thickness(0,0,0,22) };
        var model = new TextBox();
        search.SetBinding(TextBox.TextProperty,new Binding(nameof(TextBox.Text)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var slider = new Slider { Minimum = 0, Maximum = 10, Value = 5, SmallChange = 1, MaxWidth = 350, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Margin = new Thickness(0,10,0,20) };
        var vertical = new Slider { Minimum = 0, Maximum = 10, Value = 4, Width = 38, Height = 130, Orientation = Orientation.Vertical, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        var toggle = new CheckBox { Content = "Automatic overlays", Margin = new Thickness(0,15,0,20) };
        var button = new Button { Content = "⋯", ToolTip="Match actions", Width=44, Height=40, MinHeight=0, Padding=new Thickness(0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        var title = new TextBlock { Text = "Search and direct controls", FontSize = 28, Margin = new Thickness(0,0,0,20) };
        title.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");stack.Children.Add(title);
        stack.Children.Add(search); stack.Children.Add(slider); stack.Children.Add(vertical); stack.Children.Add(toggle); stack.Children.Add(button);
        async Task Pause(int ms = 100) { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.Render); await Task.Delay(ms); }
        async Task Settle(Func<bool> done)
        { var timer=System.Diagnostics.Stopwatch.StartNew(); while(!done() && timer.ElapsedMilliseconds<1600)await Pause(16); }
        KeyEventArgs KeyFor(Key key, RoutedEvent route) => new(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,key) { RoutedEvent = route };
        ContextMenu? menu = null;
        try
        {
            OperatorMotion.AnimationOverride = true; window.Show(); await Pause(240);
            checks["overflow-buttons-use-a-stateful-symbol-without-changing-command-content"] = Equals(button.Content,"⋯") && QaDescendants(button).OfType<OperatorSymbol>().Any(symbol=>Equals(symbol.Kind,"more"));
            checks["search-has-one-native-editor-and-semantic-parts"] = search.Template.FindName("PART_ContentHost",search) is ScrollViewer && search.Template.FindName("PART_SearchSymbol",search) is OperatorSymbol;
            var clear = (Button)search.Template.FindName("PART_Clear",search);
            var hint = (TextBlock)search.Template.FindName("PART_Placeholder",search);
            checks["empty-search-has-hint-without-a-clear-target"] = hint.IsVisible && !clear.IsVisible;
            search.SetCurrentValue(TextBox.TextProperty,"Malvern cricket"); await Pause();
            checks["search-query-has-one-clear-action-and-no-placeholder-overlap"] = search.HasQuery && clear.IsVisible && !hint.IsVisible;
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)).Invoke(); await Pause();
            checks["clear-search-updates-binding-immediately-and-retains-focus"] = model.Text.Length==0 && search.Text.Length==0 && search.IsKeyboardFocused && search.GetBindingExpression(TextBox.TextProperty) is not null;
            search.SetCurrentValue(TextBox.TextProperty,"Another query");
            var escape = KeyFor(Key.Escape,Keyboard.PreviewKeyDownEvent); search.RaiseEvent(escape);
            checks["escape-clears-search-before-it-can-dismiss-the-parent"] = escape.Handled && !search.HasQuery && model.Text.Length==0;
            escape = KeyFor(Key.Escape,Keyboard.PreviewKeyDownEvent); search.RaiseEvent(escape);
            checks["empty-search-leaves-escape-available-to-its-parent"] = !escape.Handled;
            for(int i=0;i<15;i++){ search.SetCurrentValue(TextBox.TextProperty,"Query "+i);search.ClearQuery(); }
            search.SetCurrentValue(TextBox.TextProperty,"Final query"); await Pause(250);
            checks["rapid-search-clears-do-not-replace-the-final-typed-text"] = model.Text=="Final query" && search.HasQuery && !hint.IsVisible;
            search.IsReadOnly=true;search.ClearQuery();await Pause();
            checks["read-only-search-cannot-be-cleared"] = search.Text=="Final query" && !clear.IsVisible;
            search.IsReadOnly=false; search.ClearQuery();
            checks["search-clear-uses-native-automation-name"] = System.Windows.Automation.AutomationProperties.GetName(clear)=="Clear search";
            var track=(Track)slider.Template.FindName("PART_Track",slider);
            var verticalTrack=(Track)vertical.Template.FindName("PART_Track",vertical);
            checks["range-controls-retain-native-tracks-and-orientation"] = track.Orientation==Orientation.Horizontal && verticalTrack.Orientation==Orientation.Vertical && track.DecreaseRepeatButton is not null && track.IncreaseRepeatButton is not null;
            var provider=(IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue);
            provider.SetValue(7);slider.Focus();slider.RaiseEvent(KeyFor(Key.Right,Keyboard.KeyDownEvent));
            checks["range-controls-support-automation-and-keyboard-adjustment"] = slider.Value==8;
            var grip=(ScaleTransform)track.Thumb.Template.FindName("PART_GripScale",track.Thumb);
            var hitSize=track.Thumb.RenderSize;
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0,0));await Pause(70);
            checks["range-grip-responds-without-resizing-the-drag-target"] = grip.HasAnimatedProperties && track.Thumb.RenderSize==hitSize && track.Thumb.RenderTransform.Value.IsIdentity;
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0,0,true));await Settle(()=>!grip.HasAnimatedProperties);
            checks["cancelled-range-drag-restores-the-grip"] = Math.Abs(grip.ScaleX-1)<.001 && Math.Abs(grip.ScaleY-1)<.001 && !grip.HasAnimatedProperties && slider.Value==8;
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent });await Pause(80);
            var knob=(ScaleTransform)toggle.Template.FindName("TbSc",toggle);
            checks["switch-press-deforms-the-knob-instead-of-the-control"] = knob.ScaleX>knob.ScaleY && toggle.RenderTransform.Value.IsIdentity;
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent });
            toggle.IsChecked=true;var thumb=(TranslateTransform)toggle.Template.FindName("Thumb",toggle);await Settle(()=>Math.Abs(thumb.X-20)<.03 && Math.Abs(knob.ScaleX-knob.ScaleY)<.001);
            checks["switch-release-retains-its-native-checked-state-and-endpoint"] = toggle.IsChecked==true && Math.Abs(thumb.X-20)<.03;
            var icon=new OperatorSymbol {Kind="copy",Width=20,Height=20};
            var command=new MenuItem {Header="Copy score",Icon=icon,InputGestureText="Ctrl+C",IsCheckable=true,IsChecked=true};
            var sub=new MenuItem {Header="More actions"};var child=new MenuItem {Header="Review scorecard"};sub.Items.Add(child);
            int invocations=0;command.Click+=(_,_)=>invocations++;
            menu=new ContextMenu {PlacementTarget=button};menu.Items.Add(command);menu.Items.Add(new Separator());menu.Items.Add(sub);button.ContextMenu=menu;
            menu.IsOpen=true;await Pause(200);
            checks["command-menu-adopts-the-shared-glass-surface"] = menu.Template.FindName("PART_MenuSurface",menu) is OperatorGlassSurface && OperatorMenuMotion.GetEnabled(menu);
            checks["command-menu-keeps-icons-shortcuts-and-check-state"] = command.Template.FindName("CommandIcon",command) is ContentPresenter presenter && ReferenceEquals(presenter.Content,icon)
                && QaDescendants(command).OfType<TextBlock>().Any(label=>label.Text=="Ctrl+C") && command.IsChecked;
            CaptureCricketQa(menu,Path.Combine(directory,"iphone-command-menu.png"));
            sub.IsSubmenuOpen=true;await Pause(100);
            checks["command-menu-submenus-remain-native-and-reachable"] = sub.IsSubmenuOpen && child.IsVisible;
            sub.IsSubmenuOpen=false; ((IInvokeProvider)new MenuItemAutomationPeer(command).GetPattern(PatternInterface.Invoke)).Invoke();await Pause();
            checks["command-menu-invokes-once-without-waiting-for-a-visual-exit"] = invocations==1;
            menu.IsOpen=false;await Pause();
            OperatorMotion.AnimationOverride=false;
            search.SetCurrentValue(TextBox.TextProperty,"Reduced motion");search.ClearQuery();track.Thumb.RaiseEvent(new DragStartedEventArgs(0,0));toggle.IsChecked=false;
            checks["reduced-motion-search-and-range-feedback-do-not-keep-clocks"] = !hint.HasAnimatedProperties && !grip.HasAnimatedProperties && thumb.X==0;
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0,0,true));
            CaptureCricketQa(root,Path.Combine(directory,"iphone-search-and-controls.png"));
            foreach(var selectedTheme in new[]{AppTheme.Dark,AppTheme.Light})
            {
                OperatorDesign.UpdateTheme(selectedTheme);await Pause();
                checks["search-text-and-placeholder-adapt-to-"+selectedTheme.ToString().ToLowerInvariant()] = search.Foreground.ToString()==((System.Windows.Media.Brush)search.FindResource("ControlCentre.TextBrush")).ToString()
                    && hint.Foreground.ToString()==((System.Windows.Media.Brush)search.FindResource("ControlCentre.SecondaryTextBrush")).ToString();
            }
            window.Width=420;window.Height=500;await Pause();
            search.SetCurrentValue(TextBox.TextProperty,"Narrow query");await Pause();
            bool clearFits=CricketQaBoundsWithin(clear,search);
            search.ClearQuery();await Pause();
            checks["search-native-content-and-clear-target-fit-a-narrow-window"] = clearFits && CricketQaBoundsWithin(hint,search);
            CaptureCricketQa(root,Path.Combine(directory,"iphone-controls-narrow-light.png"));
            OperatorDesign.UpdateTheme(AppTheme.Dark);
            foreach(var sport in new[]{Models.TeamLibrarySport.AustralianFootball,Models.TeamLibrarySport.Cricket})
            {
                var library=new TeamLibraryWindow("home",sport) {Owner=window};
                try
                {
                    library.Show();await Pause();library.SearchBox.Text="__no_club_match__";await Pause();
                    checks["club-library-has-shared-search-and-empty-state-"+sport] = library.SearchBox is OperatorSearchBox && library.TeamList.Items.Count==0 && library.EmptyResults.IsVisible;
                    library.SearchBox.ClearQuery();await Pause();
                    checks["club-library-clear-restores-results-without-saving-a-preset-"+sport] = library.TeamList.Items.Count>0 && library.Selection is null;
                    CaptureCricketQa((FrameworkElement)library.Content,Path.Combine(directory,"iphone-clubs-"+sport+".png"));
                }
                finally {library.Close();}
            }
            var mainVisibility=MainContent.Visibility;var setupVisibility=SetupWizardPanel.Visibility;bool setupInput=SetupWizardPanel.IsHitTestVisible;
            try
            {
                OperatorMotion.AnimationOverride=false;MainContent.Visibility=Visibility.Collapsed;SetupWizardPanel.Visibility=Visibility.Visible;
                BeginAflSetupTransition();
                checks["afl-setup-handoff-is-immediate-with-reduced-motion"] = MainContent.IsVisible && MainContent.Opacity==1 && SetupWizardPanel.Visibility==Visibility.Collapsed;
                OperatorMotion.AnimationOverride=true;MainContent.Visibility=Visibility.Collapsed;SetupWizardPanel.Visibility=Visibility.Visible;
                BeginAflSetupTransition();await Pause(40);
                checks["afl-setup-handoff-keeps-scoring-targets-stationary"] = MainContent.IsVisible && MainContent.RenderTransform.Value.IsIdentity && !SetupWizardPanel.IsHitTestVisible;
                MainContent.Visibility=Visibility.Collapsed;SetupWizardPanel.Visibility=Visibility.Visible;await Pause(750);
                checks["cancelled-setup-handoff-cannot-hide-the-next-wizard-or-block-input"] = SetupWizardPanel.IsVisible && SetupWizardPanel.IsHitTestVisible && _aflSetupTransitionFallback is null;
                for(int i=0;i<3;i++){SetupWizardPanel.Visibility=Visibility.Visible;BeginAflSetupTransition();await Pause(20);}
                await Settle(()=>SetupWizardPanel.Visibility==Visibility.Collapsed);
                checks["rapid-setup-handoff-reentry-retires-only-the-last-transition"] = MainContent.IsVisible && SetupWizardPanel.Visibility==Visibility.Collapsed && _aflSetupTransition is null && _aflSetupTransitionFallback is null;
            }
            finally {StopAflSetupTransition();MainContent.Visibility=mainVisibility;SetupWizardPanel.Visibility=setupVisibility;SetupWizardPanel.IsHitTestVisible=setupInput;}
        }
        finally { if(menu is not null)menu.IsOpen=false;window.Close();OperatorMotion.AnimationOverride=motion;OperatorDesign.UpdateTheme(theme); }
        return checks;
    }
}
