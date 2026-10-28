// Generated from Views/SportSelectionScreen.Landing.cs; edit translator/adapters.
// Source SHA-256: 110fde529498e4801f8b3816dc03acc2734d7a3a55afa6cea23008c2b15657b6
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
            var art=new OperatorSportArtwork{Sport=kind};body.Children.Insert(0,art);body.Margin=new Thickness(0);
            Grid.SetRow(text,0);text.VerticalAlignment=VerticalAlignment.Center;text.HorizontalAlignment=HorizontalAlignment.Left;text.Margin=new Thickness(26,12,24,12);text.MaxWidth=430;
            foreach(var label in text.Children.OfType<TextBlock>()){label.TextAlignment=TextAlignment.Left;label.HorizontalAlignment=HorizontalAlignment.Left;}
            var cta=new NativeTextBlock{Text=kind=="training"?"Start session  ›":"Set up match  ›",FontSize=13,FontWeight=FontWeight.SemiBold,Foreground=Avalonia.Media.Brushes.White};_landingCtas.Add(cta);
            text.Children.Add(new NativeBorder{CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(Color.FromArgb(35,255,255,255)),Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Left,Child=cta});
            card.CornerRadius=new CornerRadius(28);card.BorderThickness=new Thickness(1);card.ClipToBounds=true;OperatorRoundedClip.SetRadius(card,28);
        }
        AflTileBody.RenderTransformOrigin=CricketTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        var aflMotion=new TransformGroup();aflMotion.Children.Add(AflIconScale);aflMotion.Children.Add(AflIconHover);AflTileBody.RenderTransform=aflMotion;
        var cricketMotion=new TransformGroup();cricketMotion.Children.Add(CricketIconScale);cricketMotion.Children.Add(CricketIconHover);CricketTileBody.RenderTransform=cricketMotion;
        TrainingTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        TrainingCard.Effect=null;
        var trainingMotion=new TransformGroup();trainingMotion.Children.Add(TrainingIconMotionScale);trainingMotion.Children.Add(TrainingIconMotionShift);TrainingTileBody.RenderTransform=trainingMotion;
        foreach(var layer in new[]{AflHover,CricketHover,TrainingHover,AflFlash,CricketFlash,TrainingFlash})layer.CornerRadius=new CornerRadius(28);
        foreach(var edge in new[]{AflGlow,CricketGlow,TrainingGlassEdge})NativePanel.SetZIndex(edge,2);
        HomeHeading.Text="Choose your sport";HomeSubtitle.Text="Set up a match, or start a training session.";
        HomeHeading.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");HomeSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        SettingsActionCaption.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");SettingsSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        AflGlow.CornerRadius=CricketGlow.CornerRadius=TrainingGlassEdge.CornerRadius=HomeInteractionLens.CornerRadius=new CornerRadius(28);
    }
}
