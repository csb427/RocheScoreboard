using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private Storyboard? _aflSetupTransition;
    private DispatcherTimer? _aflSetupTransitionFallback;
    private int _aflSetupTransitionGeneration;
    private bool _aflSetupTransitionWatching;

    private void StopAflSetupTransition()
    {
        ++_aflSetupTransitionGeneration;
        _aflSetupTransitionFallback?.Stop(); _aflSetupTransitionFallback=null;
        _aflSetupTransition?.Remove(MainContent); _aflSetupTransition=null;
        MainContent.BeginAnimation(OpacityProperty,null); MainContent.Opacity=1;
        SetupWizardPanel.BeginAnimation(OpacityProperty,null); SetupWizardPanel.Opacity=1;
        SetupWizardPanel.IsHitTestVisible=true;
    }

    private void BeginAflSetupTransition()
    {
        StopAflSetupTransition();
        if(!_aflSetupTransitionWatching)
        {
            _aflSetupTransitionWatching=true;
            MainContent.IsVisibleChanged+=(_,args)=>{if(args.NewValue is false)StopAflSetupTransition();};
            Closed+=(_,_)=>StopAflSetupTransition();
        }
        int generation=_aflSetupTransitionGeneration;
        MainContent.Visibility=Visibility.Visible;
        // The outgoing layer gives up input immediately. Scoring targets stay
        // in their final positions throughout the optical handoff.
        SetupWizardPanel.IsHitTestVisible=false;
        void Finish()
        {
            if(generation!=_aflSetupTransitionGeneration)return;
            bool showingMatch=MainContent.IsVisible;
            StopAflSetupTransition();
            if(showingMatch)SetupWizardPanel.Visibility=Visibility.Collapsed;
        }
        if(!OperatorMotion.Enabled || !IsLoaded){Finish();return;}
        var transition=new Storyboard();_aflSetupTransition=transition;
        var outgoing=new DoubleAnimation(SetupWizardPanel.Opacity,0,OperatorMotion.SurfaceOpen) {EasingFunction=OperatorMotion.SmoothOut};
        Storyboard.SetTarget(outgoing,SetupWizardPanel);Storyboard.SetTargetProperty(outgoing,new PropertyPath(OpacityProperty));transition.Children.Add(outgoing);
        var incoming=new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen) {EasingFunction=OperatorMotion.SmoothOut};
        Storyboard.SetTarget(incoming,MainContent);Storyboard.SetTargetProperty(incoming,new PropertyPath(OpacityProperty));transition.Children.Add(incoming);
        transition.Completed+=(_,_)=>Finish();transition.Begin(MainContent,true);
        // A minimized or occluded window may stop producing composition frames.
        // Retire the old input layer even when no Completed callback arrives.
        _aflSetupTransitionFallback=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(650)};
        _aflSetupTransitionFallback.Tick+=(_,_)=>Finish();_aflSetupTransitionFallback.Start();
    }
}
