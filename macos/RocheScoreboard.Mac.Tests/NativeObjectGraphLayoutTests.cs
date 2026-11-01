// Extracted from the passing private diagnostic method; root owns integration.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using RocheScoreboard.Mac.Production;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class NativeObjectGraphLayoutTests
{
    [AvaloniaFact]
    public void WalkingAnAlreadyMeasuredDefinitionlessGridPreservesItsNextArrangeAndFindsPaintObjects()
    {
        var transform = new TranslateTransform();
        var child = new Border { Width = 20, Height = 20, RenderTransform = transform };
        var grid = new Grid { Children = { child } };
        grid.Measure(new Size(100, 100)); grid.Arrange(new Rect(0, 0, 100, 100));
        var graph = NativeObjectGraph.Walk(grid).ToArray();
        Assert.Contains(grid, graph); Assert.Contains(child, graph); Assert.Contains(transform, graph);
        Assert.True(grid.IsMeasureValid);
        grid.Arrange(new Rect(0, 0, 101, 100));
        Assert.Equal(101d, grid.Bounds.Width); Assert.True(grid.IsArrangeValid);
    }

    [AvaloniaFact]
    public async Task NamedProductionDefinitionKeepsRegistryOwnershipAndRetiresAtUnload()
    {
        var view=new ScorebugControl();var owner=new Window{Width=386,Height=193,Content=view};
        try
        {
            owner.Show();await Task.Delay(50);Dispatcher.UIThread.RunJobs();Assert.True(view.IsLoaded);
            var definition=Assert.IsAssignableFrom<ColumnDefinition>(ProductionNames.RegistryFor(view)["ExpClockColumn"]);
            // Explicit adapter contract; this is not a natural product animation.
            definition.BeginAnimation(ColumnDefinition.MinWidthProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(5)){RepeatBehavior=RepeatBehavior.Forever});
            Assert.True(Motion.IsAnimating(definition));
            owner.Content=null;await Task.Delay(50);Dispatcher.UIThread.RunJobs();
            Assert.False(view.IsLoaded);Assert.False(Motion.IsAnimating(definition));
        }
        finally {owner.Close();await Task.Delay(30);Dispatcher.UIThread.RunJobs();}
    }
}
