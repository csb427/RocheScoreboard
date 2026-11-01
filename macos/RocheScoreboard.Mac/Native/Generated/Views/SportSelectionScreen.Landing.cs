// Generated from Views/SportSelectionScreen.Landing.cs; edit translator/adapters.
// Source SHA-256: 2b98fac02313d3e166d645ac5f2d7476bc65fddcd9082e42a67afbc9f0f7051f
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
    private OperatorSportArtwork? _trainingArtwork;
    private void InstallIllustratedLanding()
    {
        if(_illustratedLandingInstalled)return;_illustratedLandingInstalled=true;
        foreach(var (card,body,text,fit,kind) in new[]{(AflCard,AflTileBody,AflTextPanel,AflContentFit,"afl"),(CricketCard,CricketTileBody,CricketTextPanel,CricketContentFit,"cricket"),(TrainingCard,TrainingTileBody,TrainingTextPanel,TrainingContentFit,"training")})
        {
            fit.IsVisible=false;
            body.RowDefinitions.Clear();body.ColumnDefinitions.Clear();body.RowDefinitions.Add(new());
            var art=new OperatorSportArtwork{Sport=kind};body.Children.Insert(0,art);body.Margin=new Thickness(0);
            if(kind=="training")_trainingArtwork=art;
            Grid.SetRow(text,0);text.VerticalAlignment=VerticalAlignment.Center;text.HorizontalAlignment=HorizontalAlignment.Left;text.Margin=new Thickness(26,12,24,12);text.MaxWidth=430;
            foreach(var label in text.Children.OfType<TextBlock>()){label.TextAlignment=TextAlignment.Left;label.HorizontalAlignment=HorizontalAlignment.Left;}
            text.Children.OfType<TextBlock>().First().Text=kind=="training"?"Training":kind=="afl"?"Start Game":"New cricket match";
            var cta=new NativeTextBlock{Text=kind=="training"?"Start session  ›":"Choose sport  ›",FontSize=13,FontWeight=FontWeight.SemiBold,Foreground=Avalonia.Media.Brushes.White};_landingCtas.Add(cta);
            text.Children.Add(new NativeBorder{CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(Color.FromArgb(35,255,255,255)),Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Left,Child=cta});
            card.CornerRadius=new CornerRadius(28);card.BorderThickness=new Thickness(1);card.ClipToBounds=true;OperatorRoundedClip.SetRadius(card,28);
        }
        AflTileBody.RenderTransformOrigin=CricketTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        var aflMotion=new TransformGroup();aflMotion.Children.Add(AflIconScale);aflMotion.Children.Add(AflIconHover);AflTileBody.RenderTransform=aflMotion;
        var cricketMotion=new TransformGroup();cricketMotion.Children.Add(CricketIconScale);cricketMotion.Children.Add(CricketIconHover);CricketTileBody.RenderTransform=cricketMotion;
        TrainingTileBody.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
        TrainingCard.Effect=null;
        var trainingMotion=new TransformGroup();trainingMotion.Children.Add(TrainingIconMotionScale);trainingMotion.Children.Add(TrainingIconMotionShift);
        TrainingTileBody.RenderTransform=new MatrixTransform(Matrix.Identity);
        if(_trainingArtwork is {} trainingArt){trainingArt.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);trainingArt.RenderTransform=trainingMotion;}
        foreach(var layer in new[]{AflHover,CricketHover,TrainingHover,AflFlash,CricketFlash,TrainingFlash})layer.CornerRadius=new CornerRadius(28);
        foreach(var edge in new[]{AflGlow,CricketGlow,TrainingGlassEdge})NativePanel.SetZIndex(edge,2);
        AflDescription.Text="AFL (footy) or cricket";
        HomeHeading.Text="Home";HomeSubtitle.Text="Resume a match, or start something new.  ·  Ctrl+K for commands";
        HomeHeading.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");HomeSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        SettingsActionCaption.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");SettingsSubtitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
        AflGlow.CornerRadius=CricketGlow.CornerRadius=TrainingGlassEdge.CornerRadius=HomeInteractionLens.CornerRadius=new CornerRadius(28);
    }
}
