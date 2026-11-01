using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketSelectorCancelBaselineTests
{
    private static void OpenSelectorInCurrentInputTurn(CricketControlPanel panel,bool bowling)
    {
        var source=Named<Button>(panel,bowling?"SelectOpeningBowler":"SelectOpeningBatters");
        Assert.True(source.IsLoaded&&source.IsEffectivelyVisible&&source.IsEffectivelyEnabled);
        // The real original control's public routed Click keeps this input
        // turn synchronous. No private Show/reset/dispatcher callback invoked.
        source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(Overlay(panel,bowling).IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(false,false,700,450)] [InlineData(true,false,700,450)]
    [InlineData(false,true,420,320)] [InlineData(true,true,420,320)]
    [Trait("Evidence","CricketSelectorScrollOwner")]
    public async Task AppOwnedDeferredOpeningScrollYieldsToTheCurrentListViewport(bool bowling,bool reopen,int width,int height)
    {
        using var scope=new Scope(true);var match=Required(bowling);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=scope.Window(panel,width,height);await Settle(500);
        // Establish the genuine source route and realized template first.
        // The subsequent routed activation supplies the precise queued-layout
        // boundary; public Offset publication represents later scroll intent,
        // not a claim of physical wheel timing before WPF Loaded priority.
        await Open(window,panel,bowling);await Key(window,PhysicalKey.Escape);Closed(panel);
        // Isolate the production controller's queued reset. Avalonia also
        // posts an independent SelectingItemsControl auto-scroll; the default
        // behavior and its offset=5 failure are retained in trace.trx/JSON.
        // Disabling that native mechanism here does NOT claim it is fixed or
        // reproduce a physical wheel input before WPF Loaded priority.
        Choices(panel,bowling).AutoScrollToSelectedItem=false;
        string state=State(match);int revision=match.DeliveryRevision,changes=0;match.MatchChanged+=()=>changes++;
        OpenSelectorInCurrentInputTurn(panel,bowling);
        var list=Choices(panel,bowling);object? firstMap=list.Tag;int firstGeneration=Read<int>(panel,"_selectorGeneration");
        if(reopen)
        {
            Skip(panel,bowling).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Closed(panel);
            OpenSelectorInCurrentInputTurn(panel,bowling);
            Assert.NotSame(firstMap,list.Tag);Assert.True(Read<int>(panel,"_selectorGeneration")>firstGeneration);
        }
        var scroll=Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroll.Extent.Height-scroll.Viewport.Height>20,"Actual realized selector must genuinely scroll.");
        object? selected=list.SelectedItem;int selectedIndex=list.SelectedIndex;Assert.NotNull(selected);
        double target=scroll.Extent.Height-scroll.Viewport.Height;
        scroll.Offset=new Vector(scroll.Offset.X,target);Assert.True(scroll.Offset.Y>20);
        double accepted=scroll.Offset.Y;
        await Settle(150);
        if(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR") is {Length:>0} directory)
        {
            Directory.CreateDirectory(directory);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);frame.Save(Path.Combine(directory,$"selector-scroll-{bowling}-{reopen}.png"));}
            File.WriteAllText(Path.Combine(directory,$"selector-scroll-{bowling}-{reopen}.json"),JsonSerializer.Serialize(new
            {bowling,reopen,accepted,actual=scroll.Offset.Y,scroll.Extent,scroll.Viewport,selectedIndex,actualSelection=list.SelectedIndex,
             firstGeneration,currentGeneration=Read<int>(panel,"_selectorGeneration"),sameModel=state==State(match),changes},new JsonSerializerOptions{WriteIndented=true}));
        }
        Assert.InRange(scroll.Offset.Y,accepted-.5,accepted+.5);
        Assert.Same(selected,list.SelectedItem);Assert.Equal(selectedIndex,list.SelectedIndex);
        Assert.Equal(state,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,changes);
        await Key(window,PhysicalKey.Escape);Closed(panel);Assert.Equal(state,State(match));Assert.Equal(0,changes);
    }
}
