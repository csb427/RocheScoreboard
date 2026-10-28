// Generated from MainWindow.OperatorSetupTransition.cs; edit translator/adapters.
// Source SHA-256: e161417a0014c649b5957e0616571ac15054ce7e1ab5d865780bc3f957b4c28c
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            NativeEvents.WatchProperty(MainContent,Avalonia.Controls.Control.IsVisibleProperty,true,(_,args)=>{if(args.NewValue is false)StopAflSetupTransition();});
            Closed+=(_,_)=>StopAflSetupTransition();
        }
        int generation=_aflSetupTransitionGeneration;
        MainContent.IsVisible=true;
        // The outgoing layer gives up input immediately. Scoring targets stay
        // in their final positions throughout the optical handoff.
        SetupWizardPanel.IsHitTestVisible=false;
        void Finish()
        {
            if(generation!=_aflSetupTransitionGeneration)return;
            bool showingMatch=MainContent.IsVisible;
            StopAflSetupTransition();
            if(showingMatch)SetupWizardPanel.IsVisible=false;
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
        _aflSetupTransitionFallback=new DispatcherTimer() {Interval=TimeSpan.FromMilliseconds(650)};
        _aflSetupTransitionFallback.Tick+=(_,_)=>Finish();_aflSetupTransitionFallback.Start();
    }
}
