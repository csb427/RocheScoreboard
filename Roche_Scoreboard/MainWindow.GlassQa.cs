using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string, bool>> RunOperatorGlassChecksAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var root = new Grid();
        var scene = new OperatorSceneBackdrop(); root.Children.Add(scene);
        var stack = new StackPanel { Margin = new Thickness(28) }; root.Children.Add(stack);
        var surface = new OperatorGlassSurface { Height = 120, CornerRadius = new CornerRadius(28), Background = new SolidColorBrush(Color.FromArgb(125, 30, 40, 54)), Child = new TextBlock { Text = "Operator material", Foreground = Brushes.White, FontSize = 28, Margin = new Thickness(20) } };
        stack.Children.Add(surface);
        var segments = new OperatorSegmentedPanel { Rows = 1, Height = 52, Margin = new Thickness(0,16,0,16) };
        foreach (string text in new[] { "Comfortable", "Compact", "Focused" }) { var option = new RadioButton { Content = text, GroupName = "GlassQa" }; option.SetResourceReference(StyleProperty,"SettingsSegment"); segments.Children.Add(option); }
        var options = segments.Children.OfType<RadioButton>().ToArray(); options[0].IsChecked = true; stack.Children.Add(segments);
        var clear = new Button { Content = "Unselected category", Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        stack.Children.Add(clear);
        var fixture = new Window { Title = "Operator glass QA", Width = 780, Height = 400, Content = root, Owner = this };
        OperatorDesign.Apply(fixture);
        bool? motion = OperatorMotion.AnimationOverride;
        try
        {
            fixture.Show(); await Task.Delay(250); fixture.UpdateLayout();
            checks["glass-packaged-shader-loads"] = OperatorGlassEffect.IsAvailable;
            checks["glass-background-only-shader"] = surface.UsesShader == (RenderCapability.IsPixelShaderVersionSupported(2,0) && !SystemParameters.HighContrast) && surface.Child.Effect is null;
            var rect = surface.Child.TranslatePoint(new System.Windows.Point(),root);
            for(int i=0;i<30;i++) { options[i%3].IsChecked=true; await Task.Delay(8); }
            options[1].IsChecked=true; await Task.Delay(750);
            checks["segmented-rapid-retarget-settles"] = options[1].IsChecked == true && !segments.IsAnimating;
            checks["segmented-selected-ink-readable"] = options[1].Foreground is SolidColorBrush ink && ink.Color.R < 80;
            checks["glass-interaction-does-not-move-content"] = surface.Child.TranslatePoint(new System.Windows.Point(),root) == rect;
            var clearSurface = (OperatorGlassSurface)clear.Template.FindName("Bd",clear);
            checks["transparent-category-remains-transparent"] = ((UIElement)VisualTreeHelper.GetChild(clearSurface,0)).Opacity == 0;
            CaptureCricketQa(root,Path.Combine(directory,"glass-material.png"));
            OperatorMotion.AnimationOverride=false; options[2].IsChecked=true;
            checks["segmented-reduced-motion-immediate"] = options[2].IsChecked == true && !segments.IsAnimating;
            OperatorGlassSurface.ForceFallback=true;
            surface.Background=new SolidColorBrush(Color.FromArgb(130,29,39,51));
            checks["glass-software-fallback"] = !surface.UsesShader && surface.Child.IsVisible;
            CaptureCricketQa(root,Path.Combine(directory,"glass-fallback.png"));
            OperatorGlassSurface.ForceFallback=false;
            surface.Background=new SolidColorBrush(Color.FromArgb(125,30,40,54));
            checks["glass-shader-restores-after-fallback"] = surface.UsesShader == (RenderCapability.IsPixelShaderVersionSupported(2,0) && !SystemParameters.HighContrast);
            var popupGlass = new OperatorGlassSurface { Width=260, Height=120, GlassSource=root, CornerRadius=new CornerRadius(22), Background=new SolidColorBrush(Color.FromArgb(175,28,34,44)), Child=new TextBlock { Text="Connected menu", Foreground=Brushes.White, FontSize=20, Margin=new Thickness(18) } };
            var popup = new Popup { PlacementTarget=clear, Placement=PlacementMode.Bottom, AllowsTransparency=true, StaysOpen=true, Child=popupGlass };
            try
            {
                popup.IsOpen=true; await Task.Delay(250);
                checks["glass-popup-samples-owner-once"] = popupGlass.HasCapturedBackdrop;
                popup.IsOpen=false; await Task.Delay(70);
                checks["glass-popup-releases-snapshot"] = !popupGlass.HasCapturedBackdrop;
                popup.IsOpen=true; await Task.Delay(180);
                checks["glass-popup-reopens-cleanly"] = popupGlass.HasCapturedBackdrop && popupGlass.IsVisible;
            }
            finally { popup.IsOpen=false; }
            surface.GlassSource=root; await Task.Delay(100);
            checks["glass-ancestor-never-captures-itself"] = !surface.HasCapturedBackdrop;
            fixture.Width=420; fixture.Height=340; await Task.Delay(200); fixture.UpdateLayout();
            checks["segmented-resize-preserves-selection"] = !segments.IsAnimating && options[2].IsChecked == true && CricketQaBoundsWithin(segments,root);
            checks["main-window-custom-chrome"] = WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { CaptionHeight: 36 };
            OperatorMotion.AnimationOverride=motion;
            var home = new SportSelectionScreen(); fixture.Content=home; fixture.Width=1000; fixture.Height=760; await Task.Delay(250);
            InvokeCricketQa(home,"ShowChangelog"); await Task.Delay(400); home.UpdateLayout();
            checks["changelog-glass-sheet-opens"] = home.ChangelogOverlay.IsVisible && home.ChangelogPanel.IsVisible && home.ChangelogEntries.Children.Count>0;
            checks["changelog-sheet-fits"] = CricketQaBoundsWithin(home.ChangelogPanel,home);
            CaptureCricketQa(home,Path.Combine(directory,"whats-new-wide.png"));
            InvokeCricketQa(home,"SelectRelease","4.1.2");await Task.Delay(300);
            checks["changelog-selects-one-release-at-a-time"] = QaDescendants(home.ChangelogEntries).OfType<TextBlock>().Any(text=>text.Text=="4.1.2") && !QaDescendants(home.ChangelogEntries).OfType<TextBlock>().Any(text=>text.Text=="4.1.3");
            CaptureCricketQa(home,Path.Combine(directory,"whats-new-previous-release.png"));
            InvokeCricketQa(home,"SelectRelease","4.1.3");await Task.Delay(300);
            home.ChangelogSearchBox.Text="no-release-matches-this-query"; await Task.Delay(70);
            var releaseClear = home.ChangelogSearchBox.Template.FindName("PART_Clear",home.ChangelogSearchBox) as System.Windows.Controls.Button;
            checks["changelog-empty-search-has-recovery"] = home.ChangelogEntries.Children.Count == 1 && releaseClear is {IsVisible:true};
            releaseClear?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(70);
            checks["changelog-search-clear-restores-the-release-content"] = home.ChangelogSearchBox.Text.Length==0 && home.ChangelogEntries.Children.Count>1;
            for(int i=0;i<8;i++) { InvokeCricketQa(home,i%2==0?"HideChangelog":"ShowChangelog"); await Task.Delay(20); }
            InvokeCricketQa(home,"ShowChangelog"); await Task.Delay(400);
            checks["changelog-rapid-reversal-remains-usable"] = home.ChangelogOverlay.IsVisible && home.ChangelogOverlay.IsHitTestVisible && home.ChangelogSearchBox.IsEnabled;
            fixture.Width=420; fixture.Height=420; await Task.Delay(200); home.UpdateLayout();
            checks["changelog-narrow-sheet-fits"] = CricketQaBoundsWithin(home.ChangelogPanel,home) && home.ChangelogSearchBox.ActualWidth>120;
            CaptureCricketQa(home,Path.Combine(directory,"whats-new-narrow.png"));
            InvokeCricketQa(home,"HideChangelog"); await Task.Delay(350);
            for(int i=0;i<50 && home.ChangelogOverlay.Visibility!=Visibility.Collapsed;i++)await Task.Delay(16);
            checks["changelog-close-restores-sports"] = home.ChangelogOverlay.Visibility == Visibility.Collapsed && home.AflCard.IsEnabled;
            var cricket = new CricketControlPanel(); cricket.SetMatch(CricketReliabilityChecks.CreateMatch(true));
            var menuHost = new Grid(); menuHost.Children.Add(cricket);
            var menuAnchor = new Button { Content="Player options", HorizontalAlignment=System.Windows.HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Top, Margin=new Thickness(12) }; menuHost.Children.Add(menuAnchor);
            fixture.Content=menuHost; fixture.Width=1000; fixture.Height=760; await Task.Delay(250);
            InvokeCricketQa(cricket,"ShowPlayerMenu",menuAnchor,false,0); await Task.Delay(350);
            Popup? CurrentMenu() => typeof(CricketControlPanel).GetField("_playerMenu",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.GetValue(cricket) as Popup;
            checks["player-popover-uses-connected-glass"] = CurrentMenu() is { IsOpen:true, Child:OperatorGlassSurface { HasCapturedBackdrop:true } };
            if(CurrentMenu()?.Child is FrameworkElement playerPopover)CaptureCricketQa(playerPopover,Path.Combine(directory,"player-popover.png"));
            for(int i=0;i<6;i++) { InvokeCricketQa(cricket,"ShowPlayerMenu",menuAnchor,false,0); await Task.Delay(25); }
            InvokeCricketQa(cricket,"ClosePlayerMenu"); await Task.Delay(250);
            checks["player-popover-rapid-toggle-cleans-up"] = CurrentMenu() is null;
            checks["player-popover-does-not-change-match"] = cricket.MainTabControl.SelectedIndex==0;
            File.WriteAllText(Path.Combine(directory,"glass-renderer.json"),System.Text.Json.JsonSerializer.Serialize(new { shaderAvailable=OperatorGlassEffect.IsAvailable, hardwareTier=RenderCapability.Tier >> 16, shaderFailure=OperatorGlassEffect.FailureReason, checks },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
        }
        finally { OperatorGlassSurface.ForceFallback=false; OperatorMotion.AnimationOverride=motion; fixture.Close(); }
        return checks;
    }
}
