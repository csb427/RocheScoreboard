using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class SharedSurfaceOwnershipTests
{
    [AvaloniaTheory] [InlineData("complete")] [InlineData("hide")] [InlineData("reduce")] [InlineData("compose")]
    public async Task AnObsoleteArrivalCannotReplaceANewerControllerTransform(string path)
    {
        bool? prior=OperatorMotion.AnimationOverride;var surface=new Border {Width=180,Height=100,Child=new TextBlock {Text="Live controller"}};var window=new Window {Width=360,Height=240,Content=surface};
        try
        {
            OperatorMotion.AnimationOverride=true;window.Show();SharedSurfaceMotion.Reveal(surface);await Task.Delay(30);
            var current=new TranslateTransform(13,7);ITransform replacement=current;
            if(path=="compose")
            {
                var composed=new TransformGroup();composed.Children.Add(Assert.IsAssignableFrom<Transform>(surface.RenderTransform));composed.Children.Add(current);replacement=composed;
            }
            surface.RenderTransform=replacement;surface.RenderTransformOrigin=new Avalonia.RelativePoint(.25,.75,Avalonia.RelativeUnit.Relative);
            if(path=="hide")surface.IsVisible=false;else if(path=="reduce"){OperatorMotion.AnimationOverride=false;SharedSurfaceMotion.Reveal(surface,false);}else await Task.Delay(1000);
            Assert.Same(replacement,surface.RenderTransform);Assert.Equal(new Avalonia.RelativePoint(.25,.75,Avalonia.RelativeUnit.Relative),surface.RenderTransformOrigin);Assert.Equal(13,current.X);Assert.Equal(7,current.Y);
            if(path=="hide")surface.IsVisible=true;
            OperatorMotion.AnimationOverride=true;SharedSurfaceMotion.Reveal(surface);await Task.Delay(1000);Assert.Same(replacement,surface.RenderTransform);
        }
        finally{window.Close();OperatorMotion.AnimationOverride=prior;}
    }
}
