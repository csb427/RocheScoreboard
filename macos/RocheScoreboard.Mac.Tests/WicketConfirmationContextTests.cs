using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketDeliveryD23RegressionDraftTests
{
    [AvaloniaFact]
    [Trait("Evidence","DeliveryChoiceSingleRefresh")]
    public async Task OneDismissalChoiceUpdatesItsSurfaceOnceAndRepeatedSelectionIsAnInertDraftAction()
    {
        using var f=await DeliveryFixture.OpenActual("Wicket",700,450);
        var tile=Named<Control>(f.Entry,"DismissalTiles").GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Run out"));
        int updates=0;
        void Changed(object? sender,Avalonia.AvaloniaPropertyChangedEventArgs args){if(args.Property==Button.BorderBrushProperty)updates++;}
        tile.PropertyChanged+=Changed;
        try
        {
            await ChoiceTile(f,"DismissalTiles","Run out");
            Assert.Equal(1,updates);
            updates=0;await ChoiceTile(f,"DismissalTiles","Run out");Assert.Equal(0,updates);
            Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(0,f.ModelChanges);
        }
        finally{tile.PropertyChanged-=Changed;}
    }

    [AvaloniaTheory][InlineData(700,450)][InlineData(420,320)]
    [Trait("Evidence","WicketConfirmationContext")]
    public async Task PinnedWicketConfirmationIdentifiesCurrentVictimAndDismissalWithoutCommitting(int width,int height)
    {
        using var f=await DeliveryFixture.OpenActual("Wicket",width,height,longNames:true);
        var preview=Named<TextBlock>(f.Entry,"PreviewText");
        Assert.Contains(f.Match.CurrentInnings!.Striker!.FullName,preview.Text);
        Assert.Contains("Bowled",preview.Text);
        await ChoiceTile(f,"DismissalTiles","Run out");
        var dismissed=Named<ComboBox>(f.Entry,"DismissedChoice");
        await ChoiceTile(f,"DismissedTiles",f.Match.CurrentInnings.NonStriker!.FullName);Assert.Equal(1,dismissed.SelectedIndex);
        Assert.Contains(f.Match.CurrentInnings.NonStriker!.FullName,preview.Text);
        Assert.Contains("Run out",preview.Text);
        Assert.DoesNotContain(f.Match.CurrentInnings.Striker.FullName,preview.Text!.Split('\n')[0]);
        Assert.Contains("Next on strike: "+f.Match.CurrentInnings.Striker.DisplayName,preview.Text);
        Assert.True(Named<ScrollViewer>(f.Entry,"EntryScroll").Bounds.Height>=48);
        Assert.InRange(Named<ScrollViewer>(f.Entry,"PreviewScroll").Bounds.Height,1,height<330?54:76);
        CaptureMetadata(f,"wicket-confirmation-context");
        await Reveal(f.Window,Named<Button>(f.Entry,"CancelButton"));
        Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(0,f.ModelChanges);
        var summary=Named<ScrollViewer>(f.Entry,"PreviewScroll");await Reveal(f.Window,summary);Assert.True(summary.IsFocused);
        await Key(f.Window,PhysicalKey.Escape);await Settle(150);
        Assert.Equal(f.OpenedState,State(f.Match));Assert.Equal(1,f.EditorClosures);
    }
}
