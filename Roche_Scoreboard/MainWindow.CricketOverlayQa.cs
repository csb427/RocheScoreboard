using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketOverlayVisualChecksAsync(string directory)
    {
        var checks = new Dictionary<string,bool>();
        var match = CricketReliabilityChecks.CreateMatch(true,50);
        match.TossWinner = match.TeamAName; match.TossWinnerElectedToBat = true;
        match.TeamALogoPath = Path.Combine(directory,"fixture-logo-A.png"); match.TeamBLogoPath = Path.Combine(directory,"fixture-logo-B.png");
        var overlays = new CricketOverlayHost(match) { AutomaticEnabled = false };
        var bug = new CricketScorebugControl { EventAnimationsEnabled=false }; overlays.Scene.Children.Add(bug);
        match.MatchChanged += () => bug.UpdateFromMatch(match); bug.UpdateFromMatch(match);
        var window = new Window { Content = overlays, Width = 386, Height = 193, ShowInTaskbar = false }; ApplyOutputWindowChrome(window);
        window.Show();
        try
        {
            foreach (var kind in new[] { CricketOverlayKind.Toss, CricketOverlayKind.LastOver, CricketOverlayKind.SinceScore, CricketOverlayKind.SinceWicket,
                CricketOverlayKind.SinceBoundary, CricketOverlayKind.Extras, CricketOverlayKind.LastWicket, CricketOverlayKind.Projection })
            {
                checks[$"overlay-manual-{kind}-has-resting-or-current-state"] = overlays.Request(kind);
                await Task.Delay(1350);
                checks[$"overlay-manual-{kind}-becomes-visible"] = overlays.Status.Contains("ON AIR") && overlays.IsShowing;
                CaptureCricketQa(overlays,Path.Combine(directory,$"overlay-{kind}-386x193.png"));
            }
            overlays.Request(CricketOverlayKind.SinceScore); overlays.Request(CricketOverlayKind.LastOver); overlays.Request(CricketOverlayKind.Extras);
            await WaitCricketQaStateAsync(window,()=>overlays.Status.Contains("EXTRAS") && !overlays.Status.Contains("CHANGING"));
            checks["overlay-rapid-requests-coalesce-to-latest-without-stale-reentry"] = overlays.Status.Contains("EXTRAS");
            overlays.Cancel(); await Task.Delay(850);
            checks["overlay-cancel-finishes-smoothly-with-no-reserved-space"] = !overlays.IsShowing && Math.Abs(bug.ActualHeight-overlays.ActualHeight)<1;
            match.RecordDelivery(6,CricketDeliveryType.Six);
            for(int i=0;i<5;i++) match.RecordDelivery(0,CricketDeliveryType.Dot);
            match.EndCurrentInnings(); match.StartNextInnings();
            if(match.CurrentInnings!.NeedsBatterSelection) { match.SetStriker(0); match.SetNonStriker(1); }
            if(match.CurrentInnings.NeedsBowlerSelection) match.ConfirmBowler(0);
            overlays.Request(CricketOverlayKind.Comparison); await Task.Delay(800);
            checks["overlay-chase-comparison-renders-in-second-innings"] = overlays.Status.Contains("AFTER");
            CaptureCricketQa(overlays,Path.Combine(directory,"overlay-comparison.png"));
            match.Format=CricketFormat.MultiDay; match.RaiseMatchChanged();
            await Task.Delay(850);
            checks["overlay-format-change-removes-inapplicable-live-comparison"] = !overlays.IsShowing;
            match.Format=CricketFormat.LimitedOvers; match.RaiseMatchChanged();
            overlays.Request(CricketOverlayKind.SinceBoundary); await Task.Delay(800);
            window.Width=280; window.Height=193; await Task.Delay(150);
            CaptureCricketQa(overlays,Path.Combine(directory,"overlay-narrow-280x193.png"));
            window.Width=1200; window.Height=180; await Task.Delay(150);
            CaptureCricketQa(overlays,Path.Combine(directory,"overlay-ultrawide-1200x180.png"));
            checks["overlay-resizes-without-nonfinite-layout"] = overlays.Scene.ActualHeight > 0 && double.IsFinite(bug.ActualWidth);
            var panel = new CricketControlPanel(); panel.SetMatch(match); panel.SetOverlayHost(overlays);
            var operatorWindow = new Window { Content=panel,Width=1200,Height=800,ShowInTaskbar=false }; operatorWindow.Show();
            try
            {
                panel.MainTabControl.SelectedItem=panel.OutputTab; await Task.Delay(250);
                var triggers=QaDescendants(panel.CricketOverlayButtons).OfType<Button>().Where(b=>b.Tag is CricketOverlayKind).ToArray();
                checks["cricket-output-tab-has-all-live-bound-overlay-actions"] = triggers.Length==Enum.GetValues<CricketOverlayKind>().Length;
                CaptureCricketQa(panel,Path.Combine(directory,"cricket-output-live-bound-overlay-groups.png"));
                triggers.Single(b=>Equals(b.Tag,CricketOverlayKind.Extras)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(1350);
                checks["cricket-output-tab-overlay-button-actually-reaches-public-display"] = overlays.Status.Contains("EXTRAS");
                panel.MainTabControl.SelectedItem=panel.CricketSettingsTab; await Task.Delay(200);
                CaptureCricketQa(panel,Path.Combine(directory,"cricket-live-settings-shared-navigation.png"));
            }
            finally { panel.ClearMatch(); operatorWindow.Close(); }
        }
        finally { overlays.Dispose(); bug.Dispose(); window.Close(); }

        var automaticMatch=CricketReliabilityChecks.CreateMatch(true); automaticMatch.TossWinner=automaticMatch.TeamAName;
        var automaticHost=new CricketOverlayHost(automaticMatch);
        var automaticWindow=new Window { Content=automaticHost, Width=386,Height=193,ShowInTaskbar=false }; ApplyOutputWindowChrome(automaticWindow); automaticWindow.Show();
        try
        {
            await Task.Delay(19100);
            checks["automatic-cricket-overlay-enters-without-manual-request"] = automaticHost.Status.Contains("TOSS RESULT") && automaticHost.IsShowing;
            CaptureCricketQa(automaticHost,Path.Combine(directory,"automatically-scheduled-toss.png"));
            automaticHost.AutomaticEnabled=false; await WaitCricketQaStateAsync(automaticWindow,()=>!automaticHost.IsShowing);
            checks["disabling-automatic-overlays-smoothly-clears-active-automatic-item"] = !automaticHost.IsShowing;
        }
        finally { automaticHost.Dispose(); automaticWindow.Close(); }

        var preferences = new GlobalSettingsScreen();
        var settingsWindow = new Window { Content=preferences, Width=1000,Height=720, ShowInTaskbar=false }; settingsWindow.Show();
        try
        {
            await Task.Delay(100);
            var workspace = preferences.SettingsWorkspace;
            var search = FindQaVisual<TextBox>(workspace)!; search.Text="screensaver"; await Task.Delay(100);
            checks["shared-settings-search-filters-unrelated-sections"] = preferences.ScreensaverClockToggle.IsVisible && preferences.AppearanceSection.Visibility==Visibility.Collapsed;
            search.Text="mp4";
            checks["global-settings-search-finds-custom-screensaver-media"] = preferences.ScreensaverClockToggle.IsVisible && preferences.AppearanceSection.Visibility==Visibility.Collapsed;
            search.Text="";
            var buttons = QaDescendants(workspace).OfType<Button>().ToArray();
            var automation=buttons.First(b=>b.Tag?.ToString()=="AUTOMATION"); automation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["shared-settings-category-navigation-actually-filters"] = preferences.AutomationSection.Visibility==Visibility.Visible && preferences.OutputDefaultsSection.Visibility==Visibility.Collapsed;
            CaptureCricketQa(preferences,Path.Combine(directory,"shared-settings-automation-category.png"));
            settingsWindow.Width=420; settingsWindow.Height=320; await Task.Delay(100);
            buttons.First(button=>Equals(button.Tag,"SCOREBOARD")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(400);
            workspace.Scroller.ScrollToBottom(); await WaitCricketQaStateAsync(settingsWindow,()=>CricketQaBoundsWithin(preferences.ScreensaverClockToggle,preferences));
            checks["shared-settings-small-window-can-reach-last-automation-option"] = CricketQaBoundsWithin(preferences.ScreensaverClockToggle,preferences);
            CaptureCricketQa(preferences,Path.Combine(directory,"shared-settings-420x320.png"));
        }
        finally { settingsWindow.Close(); }
        var resume = new ResumeGameDialog(new GameState { Sport = "cricket", CricketMatchState = match.ToState() }) { Width = 380, Height = 400 };
        resume.Show();
        try
        {
            await Task.Delay(100);
            InvokeCricketQa(resume,"Discard_Click",resume.DiscardButton,new RoutedEventArgs());
            resume.UpdateLayout();
            checks["simplified-resume-confirm-discard-label-fits"] = resume.DiscardButton.Content?.ToString()=="CONFIRM DISCARD"
                && CricketQaBoundsWithin(resume.DiscardButton,resume);
            checks["simplified-resume-shows-teams-and-phase-without-score-clutter"] = resume.HomeNameText.Text==match.TeamAName
                && resume.PhaseText.Text.Contains("INNINGS") && resume.FindName("HomeTotalText") is null;
            CaptureCricketQa(resume,Path.Combine(directory,"simplified-continue-match.png"));
        }
        finally { resume.Close(); }
        bool darkContrast=true;
        for(int red=0;red<=255;red+=17) for(int green=0;green<=255;green+=17) for(int blue=0;blue<=255;blue+=17)
        {
            string hex=$"#{red:X2}{green:X2}{blue:X2}";
            foreach(var stop in TeamVisuals.DarkBroadcast(hex,"#FFFFFF").GradientStops)
            {
                Color composite=Color.FromRgb((byte)(stop.Color.R*.78+255*.22),(byte)(stop.Color.G*.78+255*.22),(byte)(stop.Color.B*.78+255*.22));
                darkContrast &= ContrastHelper.ContrastRatio(Colors.White,composite)>=4.5;
            }
        }
        checks["4096-new-broadcast-gradients-support-white-text-even-on-white-logos"] = darkContrast;
        return checks;
    }
    private static IEnumerable<DependencyObject> QaDescendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        { var child=VisualTreeHelper.GetChild(root,i); yield return child; foreach(var descendant in QaDescendants(child)) yield return descendant; }
    }
}
