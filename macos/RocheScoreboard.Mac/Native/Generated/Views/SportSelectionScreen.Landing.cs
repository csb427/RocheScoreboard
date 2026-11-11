// Generated from Views/SportSelectionScreen.Landing.cs; edit translator/adapters.
// Source SHA-256: 473b38f3c4a6d47dc9f76b1f976b75a1f31f857207f29f6671872dae350f5c9b
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Border=Avalonia.Controls.Border;
using TextBlock=Avalonia.Controls.TextBlock;
using Color=Avalonia.Media.Color;
using HorizontalAlignment=Avalonia.Layout.HorizontalAlignment;

namespace Roche_Scoreboard.Views;

public partial class SportSelectionScreen
{
    private bool _illustratedLandingInstalled;
    private readonly List<TextBlock> _landingCtas=new();
    private void InstallIllustratedLanding()
    {
        if(_illustratedLandingInstalled)return;_illustratedLandingInstalled=true;
        foreach(var (card,body,text,fit,kind) in new[]{(AflCard,AflTileBody,AflTextPanel,AflContentFit,"afl"),(CricketCard,CricketTileBody,CricketTextPanel,CricketContentFit,"cricket"),(TrainingCard,TrainingTileBody,TrainingTextPanel,TrainingContentFit,"training")})
        {
            fit.IsVisible=false;
            body.RowDefinitions.Clear();body.ColumnDefinitions.Clear();body.RowDefinitions.Add(new());
            body.ColumnDefinitions.Add(new(){Width=GridLength.Auto});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.Margin=new Thickness(0);
            var icon=new OperatorSymbol{Kind=kind=="afl"?"match":kind,Width=44,Height=44,Margin=new Thickness(18,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            icon.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.AccentBrush");body.Children.Insert(0,icon);Grid.SetColumn(icon,0);Grid.SetRow(icon,0);
            Grid.SetRow(text,0);Grid.SetColumn(text,1);text.VerticalAlignment=VerticalAlignment.Center;text.HorizontalAlignment=HorizontalAlignment.Stretch;text.Margin=new Thickness(4,12,18,12);text.MaxWidth=520;
            foreach(var label in text.Children.OfType<TextBlock>()){label.TextAlignment=TextAlignment.Left;label.HorizontalAlignment=HorizontalAlignment.Left;}
            var labels=text.Children.OfType<TextBlock>().ToArray();
            labels[0].Text=kind=="training"?"Training":kind=="afl"?"Start a game":"Cricket";
            if(labels.Length>1)labels[1].Text=kind=="training"?"Create a training session.":kind=="afl"?"Choose AFL or Cricket.":"Play a cricket match.";
            var cta=new NativeTextBlock{Text=kind=="training"?"Start training  →":"Choose a sport  →",FontSize=13,FontWeight=FontWeight.SemiBold};
            cta.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.AccentBrush");_landingCtas.Add(cta);
            var actionHint=new NativeBorder{CornerRadius=new CornerRadius(12),Background=Avalonia.Media.Brushes.Transparent,Padding=new Thickness(0),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left,Child=cta};
            text.Children.Add(actionHint);
            card.Material=GlassMaterial.Opaque;card.SetResourceReference(BackgroundProperty,"ControlCentre.RaisedBrush");
            card.SetResourceReference(BorderBrushProperty,kind=="afl"?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
            card.CornerRadius=new CornerRadius(24);card.BorderThickness=kind=="afl"?new Thickness(2):new Thickness(1);card.ClipToBounds=true;OperatorRoundedClip.SetRadius(card,24);
        }
        AflTileBody.RenderTransformOrigin=CricketTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        var aflMotion=new TransformGroup();aflMotion.Children.Add(AflIconScale);aflMotion.Children.Add(AflIconHover);AflTileBody.RenderTransform=aflMotion;
        var cricketMotion=new TransformGroup();cricketMotion.Children.Add(CricketIconScale);cricketMotion.Children.Add(CricketIconHover);CricketTileBody.RenderTransform=cricketMotion;
        TrainingTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        TrainingCard.Effect=null;
        var trainingMotion=new TransformGroup();trainingMotion.Children.Add(TrainingIconMotionScale);trainingMotion.Children.Add(TrainingIconMotionShift);
        TrainingTileBody.RenderTransform=trainingMotion;
        foreach(var layer in new[]{AflHover,CricketHover,TrainingHover,AflFlash,CricketFlash,TrainingFlash})layer.CornerRadius=new CornerRadius(28);
        foreach(var edge in new[]{AflGlow,CricketGlow,TrainingGlassEdge})NativePanel.SetZIndex(edge,2);
        AflDescription.Text="Choose AFL or Cricket.";
        TrainingDescription.Text="Create a training session.";
        HomeHeading.Text="Welcome to Roche";HomeSubtitle.Text="Start a game or build a training session.";
        HomeHeading.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");HomeSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        SettingsActionCaption.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");SettingsSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        AflGlow.CornerRadius=CricketGlow.CornerRadius=TrainingGlassEdge.CornerRadius=HomeInteractionLens.CornerRadius=new CornerRadius(28);
    }
}
