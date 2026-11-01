using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class FirstFeedbackChoiceGeometryTests
{
    [AvaloniaTheory] [InlineData(6)] [InlineData(15)] [InlineData(20)] [InlineData(26)]
    [Trait("Evidence","FirstFeedbackChoices")]
    public void CardFillUsesItsHostCornersWhileTheTabLensRemainsAPill(double hostRadius)
    {
        double radius=hostRadius-2;var rect=new Rect(2,2,196,96);var probe=new Point(rect.Left+radius*.35,rect.Top+radius*.35);
        var card=OperatorLiquidSelection.Geometry(new OperatorLiquidSelection.Shape(rect,0,1,radius));
        var tab=OperatorLiquidSelection.Geometry(new OperatorLiquidSelection.Shape(rect,0,1));
        Assert.True(card.FillContains(probe));Assert.False(tab.FillContains(probe));Assert.False(card.FillContains(new Point(2,2)));Assert.Equal(rect,card.Bounds);
    }
}
public sealed partial class UnifiedSetupTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","FirstFeedbackChoices")]
    public async Task SportSelectionFollowsTheCardAndFirstFooterHasNoEmptyBackSlot(int width,int height)
    {
        bool? prior=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=false;
        try
        {
            using var f=new Fixture();await f.Ready(width,height);f.Start();await Settle(200);
            var radio=f.Afl.FindControl<RadioButton>("AflSportChoice")!;var surface=Assert.Single(radio.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            Assert.Equal(20,surface.CornerRadius);Assert.Equal(1,surface.Progress,5);
            var nav=f.Afl.FindControl<Grid>("NavBar")!;var cancel=f.Afl.FindControl<Button>("CancelSetupButton")!;var next=f.Afl.FindControl<Button>("NextButton")!;
            Assert.Equal(0,nav.ColumnDefinitions[0].ActualWidth,3);Assert.False(f.Afl.FindControl<Button>("BackButton")!.IsVisible);
            Assert.InRange(Math.Abs(cancel.Bounds.Width-next.Bounds.Width),0,13);Capture(f.Window,"feedback-sport-"+width);
            f.Continue(false);await Settle();Assert.True(f.Afl.FindControl<Button>("BackButton")!.IsVisible);Assert.True(nav.ColumnDefinitions[0].ActualWidth>0);
            Click(f.Afl.FindControl<Button>("BackButton")!);await Settle();Assert.Equal(0,nav.ColumnDefinitions[0].ActualWidth,3);
            Click(cancel);await Settle();Assert.True(f.Home.IsVisible);
        }
        finally{OperatorMotion.AnimationOverride=prior;}
    }
    [AvaloniaFact] [Trait("Evidence","FirstFeedbackChoices")]
    public async Task FirstAndRepeatedPointerChoiceAnimateAndTrainingHoverMovesItsVisibleBody()
    {
        bool? prior=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=true;
        try
        {
            using var f=new Fixture();await f.Ready(1280,850);await Settle(400);
            var train=f.Home.FindControl<Control>("TrainingCard")!;var body=f.Home.FindControl<Control>("TrainingTileBody")!;
            var transform=Assert.IsType<TransformGroup>(body.RenderTransform);var scale=Assert.IsType<ScaleTransform>(transform.Children[0]);
            f.Window.MouseMove(train.TranslatePoint(new Point(train.Bounds.Width/2,train.Bounds.Height/2),f.Window)!.Value);
            for(int i=0;i<70&&scale.ScaleX<1.012;i++)await Settle(12);
            Assert.True(scale.ScaleX>1.012);Capture(f.Window,"feedback-training-hover");
            f.Window.MouseMove(new Point(2,2));f.Start();await Settle(400);
            foreach(string name in new[]{"CricketSportChoice","AflSportChoice","CricketSportChoice"})
            {
                var choice=f.Afl.FindControl<RadioButton>(name)!;choice.BringIntoView();await Settle(40);var fill=Assert.Single(choice.GetVisualDescendants().OfType<OperatorChoiceSurface>());
                var point=choice.TranslatePoint(new Point(choice.Bounds.Width/2,choice.Bounds.Height/2),f.Window)!.Value;
                f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);Assert.True(choice.IsChecked);
                bool intermediate=false;for(int i=0;i<60;i++){await Settle(6);if(fill.Progress>.001&&fill.Progress<.999){intermediate=true;break;}}
                Assert.True(intermediate,"The first and repeated pointer choices must have a real intermediate fill while selected state is immediate.");await Settle(500);Assert.Equal(1,fill.Progress,4);
            }
        }
        finally{OperatorMotion.AnimationOverride=prior;}
    }
}
