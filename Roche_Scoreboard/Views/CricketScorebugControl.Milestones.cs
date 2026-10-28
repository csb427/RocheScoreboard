using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Grid=System.Windows.Controls.Grid;
using TextBlock=System.Windows.Controls.TextBlock;
using Brushes=System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Views;
public partial class CricketScorebugControl
{
    private void ShowMilestone(CricketMilestone milestone)
    {
        if(_broadcastMatch is null || _isDisposed)return;
        ++_eventGeneration;_activeMilestone=milestone;_eventExiting=false;
        _burstStarted=_momentClock.Elapsed.TotalSeconds;
        EventPresentation.BeginAnimation(OpacityProperty,null);EventPresentation.Opacity=1;
        EventPresentation.Visibility=Visibility.Visible;EventPresentation.RenderTransform=Transform.Identity;
        EventPresentation.Background=CricketBroadcastStyle.Gradient(_broadcastMatch.BattingTeamPrimaryColor,_broadcastMatch.BattingTeamSecondaryColor);
        CenterLogoBackground(EventLogo,BattingHeaderLogo.Source,.16);
        EventWord.Text=milestone.Runs.ToString();EventWord.Opacity=0;EventAccents.Children.Clear();
        bool century=milestone.Runs==100;
        var scene=new Grid{Tag=century?"century-celebration":"half-century-celebration",Width=CricketDesignRoot.Width-36,Height=CricketDesignRoot.Height-14,Margin=new Thickness(18,6,18,8)};
        scene.RowDefinitions.Add(new(){Height=new GridLength(3.5,GridUnitType.Star)});
        scene.RowDefinitions.Add(new(){Height=new GridLength(1.1,GridUnitType.Star)});
        scene.RowDefinitions.Add(new(){Height=new GridLength(.8,GridUnitType.Star)});
        var numbers=new Grid();
        TextBlock Figure()=>new(){Text=milestone.Runs.ToString(),FontFamily=CricketBroadcastStyle.Typeface,FontWeight=FontWeights.Bold,FontSize=280,Foreground=Brushes.White,HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
        if(century)
        {
            var echo=Figure();echo.Foreground=new SolidColorBrush(System.Windows.Media.Color.FromRgb(255,218,123));echo.Opacity=.45;
            var spread=new ScaleTransform(1,1);echo.RenderTransform=spread;echo.RenderTransformOrigin=new System.Windows.Point(.5,.5);numbers.Children.Add(echo);
            spread.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,1.7,TimeSpan.FromMilliseconds(1600)));
            spread.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,1.7,TimeSpan.FromMilliseconds(1600)));
            echo.BeginAnimation(OpacityProperty,new DoubleAnimation(.45,0,TimeSpan.FromMilliseconds(1600)));
        }
        var figure=Figure();figure.Tag="milestone-number";numbers.Children.Add(figure);
        var fit=new Viewbox{Child=numbers,Stretch=Stretch.Uniform,Margin=new Thickness(12,0,12,0)};scene.Children.Add(fit);
        var impact=new ScaleTransform(century?4.8:2.8,century?4.8:.12);numbers.RenderTransform=impact;numbers.RenderTransformOrigin=new System.Windows.Point(.5,.5);
        impact.BeginAnimation(ScaleTransform.ScaleXProperty,Impact(century?4.8:2.8,.94,1,century?1400:1000,0));
        impact.BeginAnimation(ScaleTransform.ScaleYProperty,Impact(century?4.8:.12,1.08,1,century?1400:1000,0));
        var name=new TextBlock{Tag="milestone-player",Text=milestone.Player.ToUpperInvariant(),FontFamily=CricketBroadcastStyle.Typeface,FontWeight=FontWeights.Bold,FontSize=72,Foreground=Brushes.White};
        var nameFit=new Viewbox{Child=name,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(12,3,12,0)};Grid.SetRow(nameFit,1);scene.Children.Add(nameFit);
        var balls=new TextBlock{Tag="milestone-balls",Text=milestone.Balls+" BALLS",FontFamily=CricketBroadcastStyle.Typeface,FontWeight=FontWeights.Bold,FontSize=54,Foreground=Brushes.White};
        var ballsFit=new Viewbox{Child=balls,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly};Grid.SetRow(ballsFit,2);scene.Children.Add(ballsFit);
        foreach(var line in new FrameworkElement[]{nameFit,ballsFit})
        {
            var move=new TranslateTransform(0,18);line.RenderTransform=move;
            move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(18,0,TimeSpan.FromMilliseconds(500)){BeginTime=TimeSpan.FromMilliseconds(350),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd});
            line.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)){BeginTime=TimeSpan.FromMilliseconds(350),FillBehavior=FillBehavior.HoldEnd});
        }
        EventAccents.Children.Add(scene);
        _eventHold.Stop();_eventHold.Interval=TimeSpan.FromMilliseconds(century?4700:3700);
        _eventHold.Tick-=EndBroadcast;_eventHold.Tick+=EndBroadcast;_eventHold.Start();
    }
}
